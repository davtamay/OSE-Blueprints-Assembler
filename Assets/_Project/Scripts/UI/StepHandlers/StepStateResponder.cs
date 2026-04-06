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

            if (evt.Current == StepState.FailedAttempt)
            {
                if (ServiceRegistry.TryGet<IAudioFeedbackService>(out var audioFail))
                    audioFail.PlayValidationFailed();
            }

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
                    _ctx.SubassemblyController?.HideNonActivePendingProxyBars();

                    // Final-pass guarantee: any panels stacked in prior steps stay at
                    // their integrated cube positions after reveal/refresh have run.
                    // Cap by the active step's index: backward navigation should NOT
                    // re-assemble bars that belong to future steps.
                    if (ServiceRegistry.TryGet<IMachineSessionController>(out var liveSession))
                    {
                        int liveCompleted = liveSession.SessionState?.CompletedStepCount ?? 0;
                        int cappedCompleted = GetCompletedCountCappedByNavigation(liveSession, liveCompleted, evt.StepId);
                        if (cappedCompleted > 0)
                        {
                            StepDefinition[] liveCompletedSteps = GetCompletedSteps(liveSession, cappedCompleted);
                            _ctx.SubassemblyController?.EnforceIntegratedPositions(liveCompletedSteps);
                        }
                    }

                    _ctx.PreviewManager?.SpawnPreviewsForStep(evt.StepId);
                    if (TryBuildHandlerContext(out var activatedCtx))
                        _ctx.Router?.OnStepActivated(in activatedCtx);
                    ApplyFinalAssemblyOverviewIfLastStep(evt.StepId);
                    _ctx.FocusComputer?.FocusCameraOnStepArea(evt.StepId);

                    OseLog.VerboseInfo(
                        $"[PartInteraction] Step '{evt.StepId}' active: spawned " +
                        $"{_ctx.PreviewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
                }
            }
            else if (evt.Current == StepState.Completed)
            {
                if (ServiceRegistry.TryGet<IAudioFeedbackService>(out var audio))
                    audio.PlayStepCompleted();

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

            // Clear stale part states carried over from a prior session or navigation.
            // PartInteractionBridge._partStates is a separate dictionary from
            // PartRuntimeController._partStates — RecomputePartsForNavigation only
            // clears the runtime copy. Without clearing here, HideNonIntroducedParts
            // sees old Completed entries and skips hiding parts that should revert
            // (e.g. frame bars still at their integrated cube positions when scrubbing back).
            _ctx.PartStates.Clear();

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

            // Reset + re-hide so that parts not in any completed/current step
            // are guaranteed hidden after navigation.
            _ctx.VisualFeedback?.ResetHiddenOnSpawnGuard();
            _ctx.VisualFeedback?.HideNonIntroducedParts();

            if (targetGlobalIndex < orderedSteps.Length)
                _ctx.VisualFeedback?.RevertFutureStepParts(orderedSteps, targetGlobalIndex);

            // When navigating to the very last step, ensure every spawned part
            // is visible at its assembled (play) position — not just those
            // referenced in requiredPartIds.
            if (targetGlobalIndex == orderedSteps.Length - 1)
            {
                _ctx.VisualFeedback?.ShowAllPartsAssembled();
                if (completedSteps.Length > 0)
                    _ctx.SubassemblyController?.RestoreCompletedPlacements(completedSteps);
            }

            // Final-pass guarantee: stacked panel bars always end up at their integrated
            // cube positions, regardless of what earlier restore passes left them at.
            // EnforceIntegratedPositions seeds the controller's pending-integration set so
            // any GLBs still loading will be repositioned as soon as they appear.
            if (completedSteps.Length > 0)
                _ctx.SubassemblyController?.EnforceIntegratedPositions(completedSteps);

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

            // Force-resave GLB originals now that all async loads are done.
            // MarkAsImported → Save() fires at swap time (during SpawnGlbPartsAsync),
            // which may be before glTFast has applied Shader Graph materials to the
            // instantiated scene. Since _saved is now only set when valid materials
            // exist, a force-resave here re-captures the final correct materials.
            var spawnedParts = _ctx.Spawner?.SpawnedParts;
            if (spawnedParts != null)
            {
                for (int i = 0; i < spawnedParts.Count; i++)
                {
                    var part = spawnedParts[i];
                    if (part != null && MaterialHelper.IsImportedModel(part))
                        MaterialHelper.ForceSaveOriginals(part);
                }
            }

            int rawCompletedCount = session.SessionState != null ? session.SessionState.CompletedStepCount : 0;
            string activeStepId = GetActiveStepId(session);

            // session.SessionState.CompletedStepCount reflects actual play progression and does NOT
            // update when the user navigates backward via the step scrubber. Cap by the active step's
            // global index so this async callback doesn't re-assemble bars that belong to future steps.
            // Example: user played to step 47 (rawCompleted=46), then navigated back to step 1 (activeIndex=0)
            // → effective = min(46,0) = 0 → no EnforceIntegratedPositions → frame stays unassembled. ✓
            int completedCount = GetCompletedCountCappedByNavigation(session, rawCompletedCount, activeStepId);

            if (session.SessionState != null && session.SessionState.IsRestored && completedCount > 0)
            {
                StepDefinition[] completedSteps = GetCompletedSteps(session, completedCount);
                RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

                OseLog.Info(
                    $"[PartInteraction] Re-applied restore positioning after async part spawn " +
                    $"({completedSteps.Length} steps, capped from {rawCompletedCount}).");
                return;
            }

            // Parts just finished async GLB loading. GLB loading replaces placeholder GameObjects
            // entirely — any SetActive(true) applied to the placeholder is lost on the new model.
            // RebuildVisualStateForActiveStep clears _revealedPartIds first, so RevealStepParts
            // actually calls SetActive(true) on the freshly-loaded GLB objects.
            // (Contrast with a bare RevealStepParts call, which sees the parts already in
            // _revealedPartIds and skips them — the new model stays hidden.)
            StepDefinition[] effectiveSteps = GetEffectiveCompletedStepsForPartsReady(
                session, completedCount, activeStepId);
            RebuildVisualStateForActiveStep(
                effectiveSteps ?? Array.Empty<StepDefinition>(), activeStepId, resetToDefaultView: false);

            OseLog.VerboseInfo(
                $"[PartInteraction] Rebuilt visual state after async GLB swap " +
                $"({effectiveSteps?.Length ?? 0} effective steps, activeStep='{activeStepId}').");
        }

        /// <summary>
        /// Returns the array of steps to treat as "completed" for <see cref="HandlePartsReady"/>.
        /// <paramref name="completedCount"/> is already capped by navigation position.
        /// Uses it directly for live-play sessions; falls back to all steps before the active step
        /// for scrubbing sessions (CompletedStepCount == 0 and no forward navigation occurred).
        /// </summary>
        private StepDefinition[] GetEffectiveCompletedStepsForPartsReady(
            IMachineSessionController session, int completedCount, string activeStepId)
        {
            // completedCount is already capped by GetCompletedCountCappedByNavigation.
            if (completedCount > 0)
                return GetCompletedSteps(session, completedCount);

            // Scrubbing session: treat steps before the active step as effectively completed.
            if (string.IsNullOrWhiteSpace(activeStepId))
                return null;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage ?? session?.Package;
            StepDefinition[] orderedSteps = package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return null;

            int activeIndex = -1;
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (string.Equals(orderedSteps[i]?.id, activeStepId, StringComparison.OrdinalIgnoreCase))
                { activeIndex = i; break; }
            }

            if (activeIndex <= 0)
                return null;

            StepDefinition[] result = new StepDefinition[activeIndex];
            Array.Copy(orderedSteps, result, activeIndex);
            return result;
        }

        /// <summary>
        /// Caps <paramref name="completedCount"/> by the global index of <paramref name="activeStepId"/>.
        /// This prevents stale <see cref="IMachineSessionState.CompletedStepCount"/> (which does not
        /// update on backward navigation) from causing future-step bars to be integrated too early.
        /// </summary>
        private int GetCompletedCountCappedByNavigation(
            IMachineSessionController session, int completedCount, string activeStepId)
        {
            if (completedCount <= 0 || string.IsNullOrWhiteSpace(activeStepId))
                return completedCount;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage ?? session?.Package;
            StepDefinition[] orderedSteps = package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return completedCount;

            int activeIndex = -1;
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (string.Equals(orderedSteps[i]?.id, activeStepId, StringComparison.OrdinalIgnoreCase))
                { activeIndex = i; break; }
            }

            if (activeIndex < 0)
                return completedCount;

            return Math.Min(completedCount, activeIndex);
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

            // Full rebuild — reset the one-shot guard so HideNonIntroducedParts
            // actually re-hides parts (e.g. after async GLB swap replaced models).
            _ctx.VisualFeedback?.ResetHiddenOnSpawnGuard();
            _ctx.VisualFeedback?.HideNonIntroducedParts();
            _ctx.VisualFeedback?.RevealStepParts(activeStepId);
            _ctx.VisualFeedback?.ApplyStepPartHighlighting(activeStepId);
            _ctx.SubassemblyController?.RefreshForStep(activeStepId);
            _ctx.SubassemblyController?.HideNonActivePendingProxyBars();

            // Final-pass guarantee: stacked panel bars always end up at their integrated
            // cube positions after all restores/reveals/hides have run.
            if (completedSteps != null && completedSteps.Length > 0)
                _ctx.SubassemblyController?.EnforceIntegratedPositions(completedSteps);

            _ctx.PreviewManager?.SpawnPreviewsForStep(activeStepId);
            if (TryBuildHandlerContext(out var rebuildCtx))
                _ctx.Router?.OnStepActivated(in rebuildCtx);

            ApplyFinalAssemblyOverviewIfLastStep(activeStepId, completedSteps);

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

        private void ApplyFinalAssemblyOverviewIfLastStep(string activeStepId, StepDefinition[] completedSteps = null)
        {
            if (string.IsNullOrWhiteSpace(activeStepId))
                return;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            StepDefinition[] orderedSteps = package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return;

            if (!string.Equals(orderedSteps[orderedSteps.Length - 1]?.id, activeStepId, System.StringComparison.OrdinalIgnoreCase))
                return;

            if (completedSteps == null && ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                int completedCount = session.SessionState != null ? session.SessionState.CompletedStepCount : 0;
                completedSteps = GetCompletedSteps(session, completedCount);
            }

            _ctx.VisualFeedback?.ShowAllPartsAssembled();
            if (completedSteps != null && completedSteps.Length > 0)
                _ctx.SubassemblyController?.RestoreCompletedPlacements(completedSteps);

            // The Stage 02 "simplified carriage" is a procedural surrogate for the
            // later printer-side carriage body. In the final machine overview we
            // show the Stage 03 carriage-side body instead of a second duplicate.
            GameObject simplifiedCarriage = _ctx.FindSpawnedPart("d3d_extruder_simplified_carriage");
            if (simplifiedCarriage != null)
                simplifiedCarriage.SetActive(false);
        }
    }
}
