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

            // Keep retrying until session is registered AND has an active step.
            // First-play-after-compile can land SpawnerPartsReady before the
            // session's first step activates; without this retry loop the
            // startup rebuild (which would reveal step parts) never runs and
            // carriages stay hidden until the user stops and plays again.
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            var stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            string activeStepId = stepController.CurrentStepState.StepId;
            if (string.IsNullOrWhiteSpace(activeStepId))
                return;

            // Derive completed-set from the active step's position in the global
            // ordered list — never from session.CompletedStepCount, which has
            // been observed inflated (540 / 305 vs ~305 total) and would re-mark
            // every part Completed every frame, defeating per-event rebuilds.
            StepDefinition[] completedSteps = DeriveCompletedStepsBeforeActive(session, activeStepId);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

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

                    // Centralised rebuild — same path used by HandleAssemblyStarted,
                    // HandleSessionRestored, and TrySyncStartupState. This is the
                    // single source of truth for "set visual state to match
                    // <completed steps before active, active step>". Inlining a
                    // parallel rebuild here was the source of skip-step / Continue
                    // divergence: any pass added to one but not the other left a
                    // visible-state bug for the missing path.
                    if (ServiceRegistry.TryGet<IMachineSessionController>(out var sessionForRebuild))
                    {
                        StepDefinition[] completedSteps = DeriveCompletedStepsBeforeActive(sessionForRebuild, evt.StepId);
                        RebuildVisualStateForActiveStep(completedSteps, evt.StepId, resetToDefaultView: false);
                    }

                    OseLog.VerboseInfo(
                        $"[PartInteraction] Step '{evt.StepId}' active: spawned " +
                        $"{_ctx.PreviewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
                }
            }
            else if (evt.Current == StepState.Completed)
            {
                _ctx.AnimationCues?.Cleanup();
                if (ServiceRegistry.TryGet<IAudioFeedbackService>(out var audio))
                    audio.PlayStepCompleted();

                if (TryBuildHandlerContextForStep(evt.StepId, out var completedCtx))
                    _ctx.Router?.OnStepCompleted(in completedCtx);

                var package = _ctx.Spawner?.CurrentPackage;
                if (package != null &&
                    package.TryGetStep(evt.StepId, out var completedStep) &&
                    completedStep != null &&
                    completedStep.RequiresPartGroupPlacement &&
                    _ctx.PartGroupController != null &&
                    !string.IsNullOrWhiteSpace(completedStep.requiredPartGroupId) &&
                    _ctx.PartGroupController.TryGetProxy(completedStep.requiredPartGroupId, out GameObject completedProxy))
                {
                    _ctx.RestorePartVisual(completedProxy);
                }

                _selection.DeselectFromSelectionService();
                _ctx.VisualFeedback?.ClearPartHoverVisual();
                if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                    partController.DeselectPart();

                _ctx.VisualFeedback?.MoveStepPartsToPlayPosition(evt.StepId);
                _ctx.PartGroupController?.HandleStepCompleted(evt.StepId);
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

            _ctx.AnimationCues?.Cleanup();
            _ctx.Router?.CleanupAll();
            _ctx.PreviewManager?.ClearPreviews();
            _ctx.ToolAction?.ClearToolActionTargets();
            _ctx.PlaceHandler?.ClearRequiredPartEmission();
            _ctx.ConnectHandler?.ClearTransientVisuals();
            _ctx.VisualFeedback?.RevealedPartIds.Clear();
            _ctx.PartGroupController?.ResetReplayState();

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

            string navTargetStepId = (targetGlobalIndex >= 0 && targetGlobalIndex < orderedSteps.Length)
                ? orderedSteps[targetGlobalIndex]?.id
                : null;

            if (completedSteps.Length > 0)
            {
                _ctx.VisualFeedback?.RestoreCompletedStepParts(completedSteps, navTargetStepId);
                _ctx.PartGroupController?.RestoreCompletedPlacements(completedSteps);
                _ctx.ConnectHandler?.RenderCompletedWires(completedSteps);
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
                    _ctx.PartGroupController?.RestoreCompletedPlacements(completedSteps);
            }

            // Final-pass guarantee: stacked panel bars always end up at their integrated
            // cube positions, regardless of what earlier restore passes left them at.
            // EnforceIntegratedPositions seeds the controller's pending-integration set so
            // any GLBs still loading will be repositioned as soon as they appear.
            if (completedSteps.Length > 0)
                _ctx.PartGroupController?.EnforceIntegratedPositions(completedSteps);

            OseLog.Info(
                $"[PartInteraction] Navigated from global step {evt.PreviousStepIndex + 1} " +
                $"to {evt.TargetStepIndex + 1}: repositioned parts.");
        }

        public void HandleSessionRestored(SessionRestored evt)
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            // Derive completedSteps from the active step's GLOBAL INDEX,
            // not from evt.CompletedStepCount. The count can accumulate
            // across replays/navigations and exceed the total step count
            // (observed 2026-04-24: CompletedStepCount=540 with only 305
            // steps in the package). Using an out-of-range count clamps
            // to all-steps-completed, which marks every part Completed
            // and defeats HideNonIntroducedParts for the active step —
            // trainee sees the fully-assembled machine at an early step.
            string activeStepId = GetActiveStepId(session);
            StepDefinition[] completedSteps = DeriveCompletedStepsBeforeActive(session, activeStepId);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

            OseLog.Info($"[PartInteraction] Restored visual state for {completedSteps.Length} completed steps (derived from active step '{activeStepId}').");
        }

        /// <summary>
        /// Returns every step ordered BEFORE <paramref name="activeStepId"/>.
        /// Delegates to <see cref="CompletedStepResolver.DeriveCompletedStepsBefore"/>
        /// so all visual-state rebuild sites share the same derivation rule —
        /// never read the unreliable persisted CompletedStepCount.
        /// </summary>
        private StepDefinition[] DeriveCompletedStepsBeforeActive(IMachineSessionController session, string activeStepId)
        {
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage ?? session?.Package;
            return CompletedStepResolver.DeriveCompletedStepsBefore(package, activeStepId);
        }

        /// <summary>
        /// Clears visual state that was valid for the PRIOR assembly but is
        /// stale now that a new assembly is starting. Most importantly: the
        /// final-step overview path (<c>ShowAllPartsAssembled</c>) sets every
        /// part's state to <see cref="PartPlacementState.Completed"/> so the
        /// machine renders fully assembled. Without clearing that here, the
        /// next assembly's first step activation sees "all parts completed"
        /// and <c>HideNonIntroducedParts</c> keeps every part visible —
        /// trainee starts step 50 with the entire printer already on screen.
        ///
        /// <para>Parts actually completed in prior steps get restored
        /// immediately after the clear so HideNonIntroducedParts (which fires
        /// on the subsequent StepStateChanged for the new assembly's first
        /// step) leaves them alone.</para>
        /// </summary>
        public void HandleAssemblyStarted(AssemblyStarted evt)
        {
            // Run the SAME comprehensive rebuild used by manual navigation
            // (see HandleStepNavigated), targeting the new assembly's first
            // step. Without this, the flow "module-complete → press Continue
            // → next assembly's first step" doesn't go through
            // HandleStepNavigated (the transition is via BeginCurrentAssembly,
            // not navigation), and HandleStepStateChanged's lighter Active
            // branch misses the full clear-and-restore pass. Result: prior
            // state (ShowAllPartsAssembled's all-Completed marking, leftover
            // _revealedPartIds) sticks around and HideNonIntroducedParts
            // keeps every part visible.
            //
            // RebuildVisualStateForActiveStep takes care of: clearing
            // PartStates / RevealedPartIds / guards, restoring Completed
            // state for steps before the new assembly, hiding future
            // parts, revealing the new step, and spawning previews. It's
            // the single source of truth for "set visual state to match
            // <completed steps, active step>" — same path manual navigation
            // uses.
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null) return;

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0) return;

            int firstStepIdx = -1;
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].assemblyId, evt.AssemblyId, StringComparison.Ordinal))
                {
                    firstStepIdx = i;
                    break;
                }
            }
            if (firstStepIdx < 0) return;

            StepDefinition firstStep = orderedSteps[firstStepIdx];
            StepDefinition[] completedSteps;
            if (firstStepIdx > 0)
            {
                completedSteps = new StepDefinition[firstStepIdx];
                Array.Copy(orderedSteps, completedSteps, firstStepIdx);
            }
            else
            {
                completedSteps = Array.Empty<StepDefinition>();
            }

            RebuildVisualStateForActiveStep(completedSteps, firstStep.id, resetToDefaultView: true);

            OseLog.Info(
                $"[PartInteraction] AssemblyStarted '{evt.AssemblyId}' — rebuilt visual state: " +
                $"{completedSteps.Length} step(s) Completed, active='{firstStep.id}'.");
        }

        /// <summary>
        /// Called immediately after a single GLB model swaps in to replace its placeholder.
        /// Re-applies the correct material/visual state for this part so it doesn't render
        /// with raw glTFast materials (or pink during Shader Graph compilation) until
        /// the full <see cref="HandlePartsReady"/> rebuild fires after all GLBs are done.
        /// </summary>
        public void HandlePartSwapped(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId)) return;

            GameObject partGo = _ctx.FindSpawnedPart(partId);
            if (partGo == null || !partGo.activeSelf) return;

            // Force-save originals now that this GLB's materials are applied.
            MaterialHelper.ForceSaveOriginals(partGo);

            // Re-apply whatever visual state this part already has.
            PartPlacementState state = _ctx.PartStates.TryGetValue(partId, out var s)
                ? s : PartPlacementState.Available;
            _ctx.VisualFeedback?.ApplyPartVisualForState(partGo, partId, state);
        }

        /// <summary>
        /// Called when PackagePartSpawner finishes spawning all parts (including async GLB models).
        /// Re-applies completed-part positioning after async spawn may have overwritten restore positions.
        /// </summary>
        public void HandlePartsReady()
        {
            // Fallback: even if the session controller isn't registered yet
            // (first play-press after compile can race the session-init),
            // still hide non-introduced parts so the scene doesn't start
            // with every part visible. Without this, the first play shows
            // the full scene assembled and the user has to stop/play again.
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                _ctx.VisualFeedback?.ResetHiddenOnSpawnGuard();
                _ctx.VisualFeedback?.HideNonIntroducedParts();
                return;
            }

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

        private float GetPreviewDelay(string stepId)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package != null && package.TryGetStep(stepId, out var step) && step?.animationCues != null)
                return step.animationCues.previewDelaySeconds;
            return 0f;
        }

        // ── Private ───────────────────────────────────────────────────────

        private void RebuildVisualStateForActiveStep(
            StepDefinition[] completedSteps,
            string activeStepId,
            bool resetToDefaultView)
        {
            if (string.IsNullOrWhiteSpace(activeStepId))
            {
                // No active step yet (can happen on the first SpawnerPartsReady
                // after a fresh play-press before a step has activated).
                // At minimum, hide non-introduced parts so the scene doesn't
                // start with every part visible; the next OnStepActivated
                // will do the full reveal pass.
                _ctx.VisualFeedback?.ResetHiddenOnSpawnGuard();
                _ctx.VisualFeedback?.HideNonIntroducedParts();
                return;
            }

            _ctx.AnimationCues?.Cleanup();
            _ctx.Router?.CleanupAll();
            _ctx.PreviewManager?.ClearPreviews();
            _ctx.ToolAction?.ClearToolActionTargets();
            _ctx.PlaceHandler?.ClearRequiredPartEmission();
            _ctx.ConnectHandler?.ClearTransientVisuals();
            // CRITICAL: clear PartStates BEFORE Restore. ShowAllPartsAssembled
            // (final-step overview) leaves every spawned part flagged Completed.
            // HideNonIntroducedParts AND RevealStepParts' deactivation pass
            // both treat Completed as "keep visible", so any rebuild that
            // doesn't first wipe PartStates can't hide future-assembly parts —
            // 100+ parts stay rendered through Hide / Revert / Reveal because
            // the Completed-skip clause fires for every one of them. Restore
            // will re-mark the 32 prior-step parts Completed; the rest must
            // start at Available (default) so the deactivation passes work.
            _ctx.PartStates.Clear();
            _ctx.VisualFeedback?.RevealedPartIds.Clear();
            _ctx.VisualFeedback?.ActiveStepPartIds.Clear();
            _ctx.VisualFeedback?.ClearPartHoverVisual();
            _ctx.PartGroupController?.ResetReplayState();

            if (completedSteps != null && completedSteps.Length > 0)
            {
                _ctx.VisualFeedback?.RestoreCompletedStepParts(completedSteps, activeStepId);
                _ctx.PartGroupController?.RestoreCompletedPlacements(completedSteps);
                _ctx.ConnectHandler?.RenderCompletedWires(completedSteps);
            }

            // Full rebuild — reset the one-shot guard so HideNonIntroducedParts
            // actually re-hides parts (e.g. after async GLB swap replaced models).
            _ctx.VisualFeedback?.ResetHiddenOnSpawnGuard();
            _ctx.VisualFeedback?.HideNonIntroducedParts();

            // RevertFutureStepParts hides every part required by a step AT or
            // AFTER the active step's global index. Without this, parts
            // authored as "visible at seq N" for a LATER step (and left
            // SetActive by a prior ShowAllPartsAssembled / completion state)
            // stay rendered. HandleStepNavigated has done this since forever
            // and is why manual skip fixes the "all parts visible" bug —
            // but HandleStepStateChanged / Continue-after-module-complete
            // never called it, so the same state leak survived across those
            // paths. Centralising here means every rebuild hides future
            // parts consistently.
            MachinePackageDefinition rebuildPkg = _ctx.Spawner?.CurrentPackage;
            StepDefinition[] rebuildOrderedSteps = rebuildPkg?.GetOrderedSteps();
            if (rebuildOrderedSteps != null && rebuildOrderedSteps.Length > 0)
            {
                int activeIdx = -1;
                for (int i = 0; i < rebuildOrderedSteps.Length; i++)
                {
                    if (rebuildOrderedSteps[i] != null &&
                        string.Equals(rebuildOrderedSteps[i].id, activeStepId, StringComparison.Ordinal))
                    { activeIdx = i; break; }
                }
                if (activeIdx >= 0 && activeIdx < rebuildOrderedSteps.Length)
                    _ctx.VisualFeedback?.RevertFutureStepParts(rebuildOrderedSteps, activeIdx);
            }

            _ctx.VisualFeedback?.RevealStepParts(activeStepId);
            _ctx.VisualFeedback?.ApplyStepPartHighlighting(activeStepId);
            _ctx.PartGroupController?.RefreshForStep(activeStepId);
            _ctx.PartGroupController?.HideNonActivePendingProxyBars();

            // Final-pass guarantee: stacked panel bars always end up at their integrated
            // cube positions after all restores/reveals/hides have run.
            if (completedSteps != null && completedSteps.Length > 0)
                _ctx.PartGroupController?.EnforceIntegratedPositions(completedSteps);

            float rebuildPreviewDelay = GetPreviewDelay(activeStepId);
            if (rebuildPreviewDelay > 0f && _ctx.AnimationCues != null)
            {
                string capturedId = activeStepId;
                _ctx.AnimationCues.OnStepActivated(activeStepId, () =>
                {
                    _ctx.PreviewManager?.SpawnPreviewsForStep(capturedId);
                    _ctx.AnimationCues?.TransformDeferredPreviews();
                    if (TryBuildHandlerContext(out var deferredRebuildCtx))
                        _ctx.Router?.OnStepActivated(in deferredRebuildCtx);
                });
            }
            else
            {
                _ctx.PreviewManager?.SpawnPreviewsForStep(activeStepId);
                if (TryBuildHandlerContext(out var rebuildCtx))
                    _ctx.Router?.OnStepActivated(in rebuildCtx);
                _ctx.AnimationCues?.OnStepActivated(activeStepId);
            }

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
                completedSteps = CompletedStepResolver.DeriveCompletedStepsBefore(
                    _ctx.Spawner?.CurrentPackage ?? session.Package, activeStepId);
            }

            _ctx.VisualFeedback?.ShowAllPartsAssembled();
            if (completedSteps != null && completedSteps.Length > 0)
                _ctx.PartGroupController?.RestoreCompletedPlacements(completedSteps);

            // The Stage 02 "simplified carriage" is a procedural surrogate for the
            // later printer-side carriage body. In the final machine overview we
            // show the Stage 03 carriage-side body instead of a second duplicate.
            GameObject simplifiedCarriage = _ctx.FindSpawnedPart("d3d_extruder_simplified_carriage");
            if (simplifiedCarriage != null)
                simplifiedCarriage.SetActive(false);
        }
    }
}
