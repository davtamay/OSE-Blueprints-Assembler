using System;
using System.Collections;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction.Integration;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Coordinates step guidance, camera framing on step transitions, and
    /// target-sphere pulsing. Extracted from InteractionOrchestrator to
    /// isolate guidance/framing concerns.
    /// </summary>
    internal sealed class StepGuidanceCoordinator
    {
        private readonly StepGuidanceService _guidanceService;
        private readonly TargetSphereAnimator _targetSphereAnimator;
        private readonly ISpawnerQueryService _spawnerQuery;
        private readonly IPartActionBridge _partBridge;
        private readonly Func<IEnumerator, Coroutine> _startCoroutine;

        private bool _guidanceContextProvided;
        private int _deferredFrameRequestId;
        private string _pendingIntroFrameStepId;

        /// <summary>Checked by coroutines to bail out if the orchestrator was destroyed.</summary>
        public bool IsAlive { get; set; } = true;

        public StepGuidanceCoordinator(
            StepGuidanceService guidanceService,
            TargetSphereAnimator targetSphereAnimator,
            ISpawnerQueryService spawnerQuery,
            IPartActionBridge partBridge,
            Func<IEnumerator, Coroutine> startCoroutine)
        {
            _guidanceService = guidanceService;
            _targetSphereAnimator = targetSphereAnimator;
            _spawnerQuery = spawnerQuery;
            _partBridge = partBridge;
            _startCoroutine = startCoroutine;
        }

        // -- Event handlers (forwarded by InteractionOrchestrator) --

        public void HandleStepStateChanged(StepStateChanged evt)
        {
            // Only the orchestrator's own field cleanup (SelectedPart/DraggedPart reset)
            // is handled in the orchestrator. Guidance has nothing extra to do here
            // beyond what the orchestrator already forwards.
        }

        public void HandleStepActivated(StepActivated evt)
        {
            // Stop any previous target sphere pulsing
            _targetSphereAnimator?.Stop();

            if (_guidanceService != null)
            {
                // Lazily provide package context on first activation
                if (!_guidanceContextProvided)
                    TryProvideGuidanceContext();

                // Defer camera framing until the intro overlay is dismissed so the
                // user doesn't see multiple camera jumps behind the intro screen.
                if (OSE.Runtime.Preview.SessionDriver.IsIntroActive)
                {
                    _pendingIntroFrameStepId = evt.StepId;
                    _guidanceService.OnStepActivatedNoFrame(evt);
                    OseLog.Info($"[Interaction] Intro active — deferring camera frame for step '{evt.StepId}'.");
                }
                else
                {
                    _guidanceService.OnStepActivated(evt);
                    ScheduleDeferredStepFrame(evt.StepId);
                }
            }

            // Start target sphere pulsing for Use steps (deferred so targets are spawned first)
            _startCoroutine(DeferredStartTargetPulsing());
        }

        public void HandleSessionRestored(SessionRestored evt)
        {
            // After restore, the next StepActivated will trigger framing.
            // But if it already fired, frame the current step now.
            TryFrameCurrentStep();
        }

        public void HandleIntroDismissedFraming(MachineIntroDismissed evt)
        {
            if (_guidanceService == null)
                return;

            string stepId = _pendingIntroFrameStepId;
            _pendingIntroFrameStepId = null;

            if (string.IsNullOrWhiteSpace(stepId))
                return;

            OseLog.Info($"[Interaction] Intro dismissed — framing step '{stepId}'.");
            _guidanceService.FrameStep(stepId);
            _guidanceService.CaptureHome();
        }

        public void TryFrameCurrentStep()
        {
            if (_guidanceService == null || _partBridge == null) return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            var stepCtrl = session?.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep) return;

            string stepId = stepCtrl.CurrentStepState.StepId;
            if (string.IsNullOrWhiteSpace(stepId)) return;

            if (OSE.Runtime.Preview.SessionDriver.IsIntroActive)
            {
                _pendingIntroFrameStepId = stepId;
                OseLog.Info($"[Interaction] TryFrameCurrentStep '{stepId}' — intro active, deferring.");
                return;
            }

            OseLog.Info($"[Interaction] TryFrameCurrentStep '{stepId}'");
            _guidanceService.FrameStep(stepId);
        }

        // -- Internal helpers --

        private void ScheduleDeferredStepFrame(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId) || _guidanceService == null)
                return;

            _deferredFrameRequestId++;
            _startCoroutine(DeferredFrameStep(stepId, _deferredFrameRequestId));
        }

        private IEnumerator DeferredFrameStep(string stepId, int requestId)
        {
            yield return null;

            if (!IsAlive || _guidanceService == null || requestId != _deferredFrameRequestId)
                yield break;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                yield break;

            string activeStepId = session?.AssemblyController?.StepController?.CurrentStepState.StepId;
            if (string.IsNullOrWhiteSpace(activeStepId))
                activeStepId = session?.SessionState?.CurrentStepId;

            if (!string.Equals(activeStepId, stepId, StringComparison.Ordinal))
                yield break;

            OseLog.Info($"[Interaction] Deferred reframe for step '{stepId}' after transition visuals settled.");
            _guidanceService.FrameStep(stepId);
        }

        private IEnumerator DeferredStartTargetPulsing()
        {
            // Wait one frame so the UseStepHandler has spawned tool-action target markers
            yield return null;

            if (!IsAlive || _targetSphereAnimator == null || _partBridge == null)
                yield break;

            // Only pulse for Use steps
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                yield break;
            var step = session?.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step == null || step.ResolvedFamily != StepFamily.Use)
                yield break;

            var positions = _partBridge.GetActiveToolTargetPositions();
            if (positions.Length > 0)
            {
                _targetSphereAnimator.StartAtPositions(positions);
                OseLog.Info($"[Interaction] Target sphere pulsing started for {positions.Length} target(s).");
            }
        }

        private void TryProvideGuidanceContext()
        {
            if (_spawnerQuery == null) return;

            var package = _spawnerQuery.CurrentPackage;
            if (package == null) return;

            Func<string, TargetPreviewPlacement> findTarget = _spawnerQuery.FindTargetPlacement;
            Transform previewRoot = _spawnerQuery.PreviewRoot;

            _guidanceService.SetPackageContext(package, findTarget, previewRoot);
            _guidanceContextProvided = true;
            OseLog.Info("[InteractionOrchestrator] Guidance service package context provided.");
        }
    }
}
