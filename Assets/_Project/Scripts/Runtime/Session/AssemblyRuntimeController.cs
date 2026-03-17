using System;
using System.Collections.Generic;
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

        public string CurrentAssemblyId => _currentAssemblyId;
        public StepController StepController { get; } = new StepController();
        public ProgressionController ProgressionController { get; } = new ProgressionController();
        private readonly StepPreflightValidator _preflightValidator = new StepPreflightValidator();

        /// <summary>
        /// Raised when an assembly completes all its steps.
        /// The MachineSessionController subscribes to this to advance to the next assembly.
        /// </summary>
        public event Action<string> OnAssemblyCompleted;

        public void Initialize(MachinePackageDefinition package)
        {
            _package = package;

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

            OseLog.Info($"[AssemblyRuntimeController] Beginning assembly '{assemblyId}' with {assemblySteps.Length} steps.");
            RuntimeEventBus.Publish(new AssemblyStarted(assemblyId));

            // Activate the first step
            StepDefinition firstStep = ProgressionController.GetCurrentStep();
            _preflightValidator.Validate(_package, firstStep);
            StepController.ActivateStep(firstStep, getElapsed());
        }

        public void Dispose()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            StepController.Reset();
            ProgressionController.Reset();
            _currentAssemblyId = null;
            _package = null;
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (evt.Current != StepState.Completed)
                return;

            // Record the completed step
            ProgressionController.RecordStepCompletion(StepController.CurrentStepState);

            // Try to advance
            StepDefinition nextStep = ProgressionController.AdvanceToNextStep();

            if (nextStep != null)
            {
                _preflightValidator.Validate(_package, nextStep);
                StepController.ActivateStep(nextStep, evt.AtSeconds);
            }
            else
            {
                CompleteAssembly();
            }
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
            if (!_package.TryGetAssembly(assemblyId, out AssemblyDefinition assembly))
            {
                OseLog.Warn($"[AssemblyRuntimeController] Assembly '{assemblyId}' not found in package.");
                return Array.Empty<StepDefinition>();
            }

            string[] stepIds = assembly.stepIds ?? Array.Empty<string>();
            if (stepIds.Length == 0)
            {
                // Fallback: use all ordered steps from the package
                OseLog.VerboseInfo($"[AssemblyRuntimeController] Assembly '{assemblyId}' has no explicit stepIds. Using all package steps.");
                return _package.GetOrderedSteps();
            }

            // Collect steps matching the assembly's stepIds, preserving package order
            StepDefinition[] allOrdered = _package.GetOrderedSteps();
            var stepIdSet = new HashSet<string>(stepIds, StringComparer.OrdinalIgnoreCase);
            var result = new List<StepDefinition>(stepIds.Length);

            for (int i = 0; i < allOrdered.Length; i++)
            {
                if (allOrdered[i] != null && stepIdSet.Contains(allOrdered[i].id))
                    result.Add(allOrdered[i]);
            }

            return result.ToArray();
        }
    }
}
