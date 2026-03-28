using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="RuntimeEventBus"/> step/navigation/restore events on behalf of
    /// <see cref="PartInteractionBridge"/>, rebuilding visual state as the session progresses.
    /// Also owns the deferred startup sync that runs until the spawner is ready.
    /// </summary>
    internal sealed class StepStateResponder
    {
        private readonly IBridgeContext _ctx;
        private readonly SelectionCoordinator _selection;

        private bool _startupSyncPending;

        public StepStateResponder(IBridgeContext ctx, SelectionCoordinator selection)
        {
            _ctx = ctx;
            _selection = selection;
        }

        // ── Called from PartInteractionBridge.OnEnable / OnDisable ────────

        public void SetStartupSyncPending(bool pending) => _startupSyncPending = pending;

        // ── Called from PartInteractionBridge.Update ──────────────────────

        public void TrySyncStartupState()
        {
            if (!_startupSyncPending || !Application.isPlaying)
                return;

            var spawner = _ctx.Spawner;
            if (spawner == null)
            {
                _startupSyncPending = false;
                return;
            }

            if (spawner.CurrentPackage == null && SessionDriver.CurrentPackage != null)
                spawner.ApplyPackageSnapshot(SessionDriver.CurrentPackage);

            if (spawner.CurrentPackage == null)
                return;

            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                var stepController = session.AssemblyController?.StepController;
                if (stepController != null && stepController.HasActiveStep)
                {
                    string activeStepId = stepController.CurrentStepState.StepId;
                    if (!string.IsNullOrWhiteSpace(activeStepId))
                    {
                        int completedCount = session.SessionState != null ? session.SessionState.CompletedStepCount : 0;
                        StepDefinition[] completedSteps = GetCompletedSteps(session, completedCount);
                        RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);
                    }
                }
            }

            _startupSyncPending = false;
        }

        // ── RuntimeEventBus handlers ──────────────────────────────────────

        public void HandleStepStateChanged(StepStateChanged evt)
        {
            _ctx.ResetDragState();

            // FailedAttempt is a transient state within the same step (Active → FailedAttempt → Active).
            // Preserve previews and sequential progress for both transitions.
            bool isFailRelated = evt.Current == StepState.FailedAttempt
                              || (evt.Current == StepState.Active && evt.Previous == StepState.FailedAttempt);

            if (!isFailRelated)
            {
                _ctx.ClearHintHighlight();
                _ctx.ToolAction?.ClearToolActionTargets();
                _ctx.PreviewManager?.ResetSequentialState();
                _ctx.ConnectHandler?.ClearTransientVisuals();
            }

            if (evt.Current == StepState.Active)
            {
                if (isFailRelated)
                {
                    OseLog.VerboseInfo(
                        $"[PartInteraction] Step '{evt.StepId}' re-activated after failed attempt — " +
                        $"keeping {_ctx.PreviewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
                }
                else
                {
                    // Clear stale selection so same part is selectable on the next step.
                    _selection.DeselectFromSelectionService();

                    _ctx.VisualFeedback?.HideNonIntroducedParts();
                    _ctx.VisualFeedback?.RevealStepParts(evt.StepId);
                    _ctx.VisualFeedback?.ApplyStepPartHighlighting(evt.StepId);
                    _ctx.SubassemblyController?.RefreshForStep(evt.StepId);

                    _ctx.PreviewManager?.SpawnPreviewsForStep(evt.StepId);
                    if (TryBuildHandlerContext(out var activatedCtx))
                        _ctx.Router?.OnStepActivated(in activatedCtx);
                    _ctx.FocusComputer?.FocusCameraOnStepArea(evt.StepId);

                    OseLog.VerboseInfo(
                        $"[PartInteraction] Step '{evt.StepId}' active: spawned " +
                        $"{_ctx.PreviewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
                }
            }
            else if (evt.Current == StepState.Completed)
            {
                if (TryBuildHandlerContextForStep(evt.StepId, out var completedCtx))
                    _ctx.Router?.OnStepCompleted(in completedCtx);

                var package = _ctx.Spawner?.CurrentPackage;
                if (package != null &&
                    package.TryGetStep(evt.StepId, out var completedStep) &&
                    completedStep != null &&
                    completedStep.RequiresSubassemblyPlacement &&
                    _ctx.SubassemblyController != null &&
                    !string.IsNullOrWhiteSpace(completedStep.requiredSubassemblyId) &&
                    _ctx.SubassemblyController.TryGetProxy(completedStep.requiredSubassemblyId, out GameObject completedProxy))
                {
                    _ctx.RestorePartVisual(completedProxy);
                }

                _selection.DeselectFromSelectionService();
                _ctx.VisualFeedback?.ClearPartHoverVisual();
                if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                    partController.DeselectPart();

                _ctx.VisualFeedback?.MoveStepPartsToPlayPosition(evt.StepId);
                _ctx.SubassemblyController?.HandleStepCompleted(evt.StepId);
                _ctx.PreviewManager?.ClearPreviews();
            }

            _ctx.ToolAction?.RefreshToolPreviewIndicator();
            _ctx.RefreshToolActionTargets();
            if (_ctx.IsToolModeLockedForParts())
                _ctx.VisualFeedback?.ClearPartHoverVisual();
        }

        public void HandleStepNavigated(StepNavigated evt)
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out _))
                return;

            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null) return;

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            int targetGlobalIndex = Mathf.Clamp(evt.TargetStepIndex, 0, Mathf.Max(orderedSteps.Length - 1, 0));

            _ctx.Router?.CleanupAll();
            _ctx.PreviewManager?.ClearPreviews();
            _ctx.ToolAction?.ClearToolActionTargets();
            _ctx.PlaceHandler?.ClearRequiredPartEmission();
            _ctx.ConnectHandler?.ClearTransientVisuals();
            _ctx.VisualFeedback?.RevealedPartIds.Clear();
            _ctx.SubassemblyController?.ResetReplayState();

            StepDefinition[] completedSteps = Array.Empty<StepDefinition>();
            if (targetGlobalIndex > 0 && orderedSteps.Length > 0)
            {
                completedSteps = new StepDefinition[targetGlobalIndex];
                Array.Copy(orderedSteps, completedSteps, targetGlobalIndex);
            }

            if (completedSteps.Length > 0)
            {
                _ctx.VisualFeedback?.RestoreCompletedStepParts(completedSteps);
                _ctx.SubassemblyController?.RestoreCompletedPlacements(completedSteps);
            }

            if (targetGlobalIndex < orderedSteps.Length)
                _ctx.VisualFeedback?.RevertFutureStepParts(orderedSteps, targetGlobalIndex);

            OseLog.Info(
                $"[PartInteraction] Navigated from global step {evt.PreviousStepIndex + 1} " +
                $"to {evt.TargetStepIndex + 1}: repositioned parts.");
        }

        public void HandleSessionRestored(SessionRestored evt)
        {
            if (evt.CompletedStepCount <= 0) return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            StepDefinition[] completedSteps = GetCompletedSteps(session, evt.CompletedStepCount);
            string activeStepId = GetActiveStepId(session);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

            OseLog.Info($"[PartInteraction] Restored visual state for {completedSteps.Length} completed steps.");
        }

        /// <summary>
        /// Called when PackagePartSpawner finishes spawning all parts (including async GLB models).
        /// Re-applies completed-part positioning after async spawn may have overwritten restore positions.
        /// </summary>
        public void HandlePartsReady()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            if (session.SessionState == null || !session.SessionState.IsRestored)
                return;

            int completedCount = session.SessionState.CompletedStepCount;
            if (completedCount <= 0) return;

            StepDefinition[] completedSteps = GetCompletedSteps(session, completedCount);
            string activeStepId = GetActiveStepId(session);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

            OseLog.Info(
                $"[PartInteraction] Re-applied restore positioning after async part spawn " +
                $"({completedSteps.Length} steps).");
        }

        // ── Context builders (also used by PartInteractionBridge.Update) ──

        public bool TryBuildHandlerContext(out StepHandlerContext context)
        {
            context = default;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
                return false;
            var step = stepCtrl.CurrentStepDefinition;
            context = new StepHandlerContext(step, stepCtrl, step.id, session.GetElapsedSeconds());
            return true;
        }

        // ── Private ───────────────────────────────────────────────────────

        private void RebuildVisualStateForActiveStep(
            StepDefinition[] completedSteps,
            string activeStepId,
            bool resetToDefaultView)
        {
            if (string.IsNullOrWhiteSpace(activeStepId))
                return;

            _ctx.Router?.CleanupAll();
            _ctx.PreviewManager?.ClearPreviews();
            _ctx.ToolAction?.ClearToolActionTargets();
            _ctx.PlaceHandler?.ClearRequiredPartEmission();
            _ctx.ConnectHandler?.ClearTransientVisuals();
            _ctx.VisualFeedback?.RevealedPartIds.Clear();
            _ctx.VisualFeedback?.ActiveStepPartIds.Clear();
            _ctx.VisualFeedback?.ClearPartHoverVisual();
            _ctx.SubassemblyController?.ResetReplayState();

            if (completedSteps != null && completedSteps.Length > 0)
            {
                _ctx.VisualFeedback?.RestoreCompletedStepParts(completedSteps);
                _ctx.SubassemblyController?.RestoreCompletedPlacements(completedSteps);
            }

            _ctx.VisualFeedback?.HideNonIntroducedParts();
            _ctx.VisualFeedback?.RevealStepParts(activeStepId);
            _ctx.VisualFeedback?.ApplyStepPartHighlighting(activeStepId);
            _ctx.SubassemblyController?.RefreshForStep(activeStepId);

            _ctx.PreviewManager?.SpawnPreviewsForStep(activeStepId);
            if (TryBuildHandlerContext(out var rebuildCtx))
                _ctx.Router?.OnStepActivated(in rebuildCtx);

            _ctx.FocusComputer?.FocusCameraOnStepArea(activeStepId, resetToDefaultView);
            _ctx.ToolAction?.RefreshToolPreviewIndicator();
            _ctx.RefreshToolActionTargets();
        }

        private bool TryBuildHandlerContextForStep(string stepId, out StepHandlerContext context)
        {
            context = default;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null)
                return false;
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return false;
            context = new StepHandlerContext(step, stepCtrl, stepId, session.GetElapsedSeconds());
            return true;
        }

        private StepDefinition[] GetCompletedSteps(IMachineSessionController session, int completedCount)
        {
            if (session == null || completedCount <= 0)
                return Array.Empty<StepDefinition>();

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage ?? session.Package;
            StepDefinition[] orderedSteps = package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return Array.Empty<StepDefinition>();

            int clamped = Math.Min(completedCount, orderedSteps.Length);
            if (clamped <= 0)
                return Array.Empty<StepDefinition>();

            StepDefinition[] result = new StepDefinition[clamped];
            Array.Copy(orderedSteps, result, clamped);
            return result;
        }

        private static string GetActiveStepId(IMachineSessionController session)
        {
            StepController stepController = session?.AssemblyController?.StepController;
            if (stepController != null && stepController.HasActiveStep)
            {
                string stepId = stepController.CurrentStepState.StepId;
                if (!string.IsNullOrWhiteSpace(stepId))
                    return stepId;
            }

            return session?.SessionState?.CurrentStepId;
        }
    }
}
