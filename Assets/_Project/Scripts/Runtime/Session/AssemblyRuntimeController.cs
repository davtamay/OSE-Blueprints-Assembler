using System;
using OSE.App;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Manages the current assembly context, owns StepController and ProgressionController,
    /// and coordinates step activation and advancement within a single assembly.
    /// </summary>
    public sealed class AssemblyRuntimeController
    {
        private MachinePackageDefinition _package;
        private string _currentAssemblyId;
        private float _sessionElapsedRef;
        private Func<bool> _isSessionNavigating;

        public string CurrentAssemblyId => _currentAssemblyId;
        public StepController StepController { get; } = new StepController();
        public ProgressionController ProgressionController { get; } = new ProgressionController();
        private readonly StepPreflightValidator _preflightValidator = new StepPreflightValidator();

        /// <summary>
        /// Raised when an assembly completes all its steps.
        /// The MachineSessionController subscribes to this to advance to the next assembly.
        /// </summary>
        public event Action<string> OnAssemblyCompleted;

        public void Initialize(MachinePackageDefinition package, Func<bool> isNavigating = null)
        {
            _package = package;
            _isSessionNavigating = isNavigating ?? (() => false);

            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
        }

        public void BeginAssembly(string assemblyId, Func<float> getElapsed)
        {
            if (_package == null)
            {
                OseLog.Error("[AssemblyRuntimeController] Cannot begin assembly — not initialized.");
                return;
            }

            _currentAssemblyId = assemblyId;
            _sessionElapsedRef = getElapsed();

            // Resolve the assembly's ordered steps
            StepDefinition[] assemblySteps = ResolveAssemblySteps(assemblyId);
            if (assemblySteps.Length == 0)
            {
                OseLog.Warn($"[AssemblyRuntimeController] Assembly '{assemblyId}' has no steps. Completing immediately.");
                CompleteAssembly();
                return;
            }

            ProgressionController.Initialize(assemblySteps);

            // Recompute PartRuntimeController._partStates BEFORE publishing
            // AssemblyStarted, mirroring what skip-step navigation does. This is
            // load-bearing — without it, the runtime PartStates dict carries
            // stale Completed entries from the prior assembly's
            // ShowAllPartsAssembled, and downstream consumers re-activate
            // future-step parts every frame. Centralised here so EVERY caller
            // of BeginAssembly is protected; do not move back to the call site.
            // See feedback_continue_must_recompute_parts_like_skip.md.
            RecomputePartStatesForAssemblyStart(assemblySteps[0]);

            OseLog.Info($"[AssemblyRuntimeController] Beginning assembly '{assemblyId}' with {assemblySteps.Length} steps.");
            RuntimeEventBus.Publish(new AssemblyStarted(assemblyId));

            // Activate the first step
            StepDefinition firstStep = ProgressionController.GetCurrentStep();
            _preflightValidator.Validate(_package, firstStep);
            StepController.ActivateStep(firstStep, getElapsed());
            PublishStepActivated(firstStep);
        }

        /// <summary>
        /// Recomputes the runtime <see cref="IPartRuntimeController"/>'s
        /// <c>_partStates</c> dictionary so it matches "every step BEFORE the
        /// new assembly's first step is Completed; the first step's parts are
        /// Available; everything else is absent." This is the same call
        /// SessionNavigationController makes for skip-step jumps, lifted into
        /// BeginAssembly / RestoreAssemblyState so any future entry point that
        /// starts an assembly automatically gets the right runtime state — no
        /// caller-must-remember invariant.
        /// </summary>
        private void RecomputePartStatesForAssemblyStart(StepDefinition firstStep)
        {
            if (firstStep == null) return;
            if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController) || partController == null)
                return;

            StepDefinition[] orderedSteps = _package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
            {
                partController.RecomputePartsForNavigation(Array.Empty<StepDefinition>(), firstStep);
                return;
            }

            int firstStepIdx = -1;
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].id, firstStep.id, StringComparison.Ordinal))
                { firstStepIdx = i; break; }
            }
            if (firstStepIdx < 0)
            {
                partController.RecomputePartsForNavigation(Array.Empty<StepDefinition>(), firstStep);
                return;
            }

            StepDefinition[] completedGlobalSteps;
            if (firstStepIdx > 0)
            {
                completedGlobalSteps = new StepDefinition[firstStepIdx];
                Array.Copy(orderedSteps, completedGlobalSteps, firstStepIdx);
            }
            else
            {
                completedGlobalSteps = Array.Empty<StepDefinition>();
            }

            partController.RecomputePartsForNavigation(completedGlobalSteps, firstStep);
        }

        public void RestoreAssemblyState(string assemblyId, int stepIndex, Func<float> getElapsed)
        {
            if (_package == null)
            {
                OseLog.Error("[AssemblyRuntimeController] Cannot restore assembly — not initialized.");
                return;
            }

            _currentAssemblyId = assemblyId;
            _sessionElapsedRef = getElapsed();

            StepDefinition[] assemblySteps = ResolveAssemblySteps(assemblyId);
            if (assemblySteps.Length == 0)
            {
                OseLog.Warn($"[AssemblyRuntimeController] Assembly '{assemblyId}' has no steps. Completing immediately.");
                CompleteAssembly();
                return;
            }

            ProgressionController.Initialize(assemblySteps);

            int clampedIndex = System.Math.Max(0, System.Math.Min(stepIndex, assemblySteps.Length - 1));
            if (clampedIndex > 0)
                ProgressionController.SkipToIndex(clampedIndex);

            StepDefinition step = ProgressionController.GetCurrentStep();
            if (step == null)
            {
                CompleteAssembly();
                return;
            }

            // Same invariant as BeginAssembly: recompute runtime PartStates
            // BEFORE publishing AssemblyStarted. Restore lands at an arbitrary
            // step within the assembly (not necessarily index 0), so the active
            // step here is `step`, not `assemblySteps[0]`.
            RecomputePartStatesForAssemblyStart(step);

            OseLog.Info($"[AssemblyRuntimeController] Restoring assembly '{assemblyId}' at step index {stepIndex} of {assemblySteps.Length}.");
            RuntimeEventBus.Publish(new AssemblyStarted(assemblyId));

            _preflightValidator.Validate(_package, step);
            StepController.ActivateStep(step, getElapsed());
            PublishStepActivated(step);
        }

        public void Dispose()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            StepController.Reset();
            ProgressionController.Reset();
            _currentAssemblyId = null;
            _package = null;
        }

        /// <summary>
        /// Navigates to a specific step index without completing the current step.
        /// The caller must recompute part states and publish StepNavigated before calling this.
        /// </summary>
        public void NavigateToStep(int targetIndex, Func<float> getElapsed)
        {
            if (_package == null)
            {
                OseLog.Error("[AssemblyRuntimeController] Cannot navigate — not initialized.");
                return;
            }

            StepController.Reset();
            ProgressionController.SetCurrentIndex(targetIndex);

            StepDefinition step = ProgressionController.GetCurrentStep();
            if (step != null)
            {
                _preflightValidator.Validate(_package, step);
                StepController.ActivateStep(step, getElapsed());
                PublishStepActivated(step);
                OseLog.Info($"[AssemblyRuntimeController] Navigated to step {targetIndex + 1}/{ProgressionController.TotalSteps}: '{step.id}'");
            }
        }

        /// <summary>
        /// Switches to a different assembly and navigates to a specific local step index.
        /// Used by cross-assembly navigation (e.g. skip-to-end) where the target step
        /// is in a different assembly than the current one.
        /// </summary>
        public void NavigateToStepInAssembly(string assemblyId, int localStepIndex, Func<float> getElapsed)
        {
            if (_package == null)
            {
                OseLog.Error("[AssemblyRuntimeController] Cannot navigate cross-assembly — not initialized.");
                return;
            }

            _currentAssemblyId = assemblyId;

            StepDefinition[] assemblySteps = ResolveAssemblySteps(assemblyId);
            if (assemblySteps.Length == 0)
            {
                OseLog.Warn($"[AssemblyRuntimeController] Assembly '{assemblyId}' has no steps.");
                return;
            }

            ProgressionController.Initialize(assemblySteps);

            int clamped = Math.Max(0, Math.Min(localStepIndex, assemblySteps.Length - 1));
            OseLog.Info($"[AssemblyRuntimeController] Switching to assembly '{assemblyId}', step {clamped + 1}/{assemblySteps.Length}.");

            NavigateToStep(clamped, getElapsed);
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (evt.Current != StepState.Completed)
                return;

            // During explicit navigation, suppress auto-advance so the user
            // stays on the step they navigated to.
            if (_isSessionNavigating())
                return;

            // Record the completed step
            ProgressionController.RecordStepCompletion(StepController.CurrentStepState);

            // Try to advance
            StepDefinition nextStep = ProgressionController.AdvanceToNextStep();

            if (nextStep != null)
            {
                _preflightValidator.Validate(_package, nextStep);
                StepController.ActivateStep(nextStep, evt.AtSeconds);
                PublishStepActivated(nextStep);
            }
            else
            {
                CompleteAssembly();
            }
        }

        private void PublishStepActivated(StepDefinition step)
        {
            RuntimeEventBus.Publish(new StepActivated(
                step.id,
                _currentAssemblyId,
                ProgressionController.CurrentStepIndex,
                ProgressionController.TotalSteps));
        }

        private void CompleteAssembly()
        {
            string assemblyId = _currentAssemblyId;
            OseLog.Info($"[AssemblyRuntimeController] Assembly '{assemblyId}' completed.");
            RuntimeEventBus.Publish(new AssemblyCompleted(assemblyId));

            OnAssemblyCompleted?.Invoke(assemblyId);
        }

        private StepDefinition[] ResolveAssemblySteps(string assemblyId)
        {
            // Derive assembly steps from step.assemblyId — no dependency on
            // the assembly's stepIds array, eliminating the sync-bug class.
            StepDefinition[] derived = _package.GetStepsForAssembly(assemblyId);
            if (derived.Length > 0)
                return derived;

            OseLog.Warn($"[AssemblyRuntimeController] Assembly '{assemblyId}' has no steps with matching assemblyId.");
            return Array.Empty<StepDefinition>();
        }
    }
}
