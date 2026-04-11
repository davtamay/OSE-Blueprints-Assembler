using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction.Integration;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Coordinates tool-action routing, preview sequencing ("I Do / We Do / You Do"),
    /// and persistent-tool lifecycle. Extracted from <see cref="InteractionOrchestrator"/>
    /// to keep tool-mode concerns in a focused class.
    ///
    /// Owns the "saved instruction" state used while a preview is running.
    /// All navigation side-effects (camera framing, state transitions) are performed
    /// via delegates so the coordinator stays decoupled from the orchestrator.
    /// </summary>
    internal sealed class ToolActionCoordinator
    {
        private readonly InteractionSettings _settings;
        private readonly ToolActionPreviewController _previewController;
        private readonly IToolPreviewProvider _toolPreview;
        private readonly IPartActionBridge _partBridge;
        private readonly CanonicalActionBridge _actionBridge;
        private readonly AssemblyCameraRig _cameraRig;
        private readonly StepGuidanceService _guidanceService;
        private readonly TargetSphereAnimator _targetSphereAnimator;
        private readonly PersistentToolController _persistentToolController;

        /// <summary>Delegate back to InteractionOrchestrator.TransitionTo(state).</summary>
        private readonly Action<InteractionState> _transitionTo;

        /// <summary>Returns the orchestrator's current resolved interaction mode.</summary>
        private readonly Func<InteractionMode> _getMode;

        private string _savedInstruction;

        public ToolActionCoordinator(
            InteractionSettings settings,
            ToolActionPreviewController previewController,
            IToolPreviewProvider toolPreview,
            IPartActionBridge partBridge,
            CanonicalActionBridge actionBridge,
            AssemblyCameraRig cameraRig,
            StepGuidanceService guidanceService,
            TargetSphereAnimator targetSphereAnimator,
            PersistentToolController persistentToolController,
            Action<InteractionState> transitionTo,
            Func<InteractionMode> getMode)
        {
            _settings              = settings;
            _previewController     = previewController;
            _toolPreview           = toolPreview;
            _partBridge            = partBridge;
            _actionBridge          = actionBridge;
            _cameraRig             = cameraRig;
            _guidanceService       = guidanceService;
            _targetSphereAnimator  = targetSphereAnimator;
            _persistentToolController = persistentToolController;
            _transitionTo          = transitionTo;
            _getMode               = getMode;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Routes a tool action directly to the legacy bridge, bypassing the
        /// canonical action router. Falls back to the router if the direct call
        /// is unavailable.
        /// </summary>
        public void RouteToolAction(Vector2 screenPos)
        {
            if (_partBridge == null)
            {
                OseLog.Warn("[Interaction] RouteToolAction: no part bridge — canonical fallback only.");
                _actionBridge?.OnToolPrimaryAction();
                return;
            }

            OseLog.Info($"[Interaction] RouteToolAction resolve at ({screenPos.x:F0},{screenPos.y:F0})");
            if (_partBridge.TryResolveToolActionTarget(screenPos, out ToolActionContext ctx))
            {
                OseLog.Info($"[Interaction] RouteToolAction: resolved target='{ctx.TargetId}' at {ctx.TargetWorldPos}, surface={ctx.SurfaceWorldPos}. Executing...");

                // -- Tool Action Preview: always show animation for Use steps --
                if (_settings.EnableToolActionPreview && _previewController != null)
                {
                    var previewConfig = ResolvePreviewMode();
                    OseLog.Info($"[Interaction] Preview check: config={previewConfig}, target='{ctx.TargetId}', pos={ctx.TargetWorldPos}");
                    if (previewConfig.HasValue && TryEnterToolActionPreview(ctx, previewConfig.Value.mode, previewConfig.Value.speed))
                        return;
                }
                else
                {
                    OseLog.Info($"[Interaction] Preview skipped: EnableToolActionPreview={_settings.EnableToolActionPreview}, controller={_previewController != null}");
                }

                // Spawn persistent tool BEFORE completing the action, because
                // TryToolAction may complete the step synchronously — advancing
                // to the next step, unequipping the tool, and destroying the preview
                // before we get a chance to convert it.
                TrySpawnPersistentToolOnComplete(
                    ctx.TargetId,
                    ctx.SurfaceWorldPos,
                    ctx.HasToolActionRotation ? ctx.ToolActionRotation : (Quaternion?)null);

                if (_partBridge.TryToolAction(ctx.TargetId))
                {
                    OseLog.Info($"[Interaction] RouteToolAction: TryToolAction SUCCESS for '{ctx.TargetId}'.");
                    _cameraRig?.FocusOn(ctx.TargetWorldPos);
                    return;
                }

                // Action failed — remove the persistent tool we speculatively created
                _persistentToolController?.RemoveAt(ctx.TargetId);

                OseLog.Warn($"[Interaction] RouteToolAction: TryToolAction FAILED for '{ctx.TargetId}' — execution rejected.");
                // Execution was rejected (wrong tool, missing tool, etc.). Keep the
                // fallback ordering by focusing the already-resolved target.
                _cameraRig?.FocusOn(ctx.TargetWorldPos);
                return;
            }
            OseLog.Info("[Interaction] RouteToolAction: TryResolveToolActionTarget returned false.");

            // Tool action failed (wrong tool, no tool equipped, etc.) — still focus
            // camera on the nearest target sphere so the user can navigate to it.
            if (_cameraRig != null && _partBridge != null &&
                _partBridge.TryGetNearestToolTargetWorldPos(screenPos, out Vector3 nearestTargetPos))
            {
                _cameraRig.FocusOn(nearestTargetPos);
                return;
            }

            OseLog.Info("[Interaction] RouteToolAction: bridge returned false — canonical fallback.");
            _actionBridge?.OnToolPrimaryAction();
        }

        /// <summary>
        /// Removes all persistent tools placed during the given step.
        /// Called by the orchestrator on StepActivated (when a step completes/changes).
        /// </summary>
        public void CleanUpPersistentToolsForStep(string stepId)
            => _persistentToolController?.CleanUpForStep(stepId);

        /// <summary>
        /// Returns true when a tool is actively equipped OR the step has an incomplete
        /// tool action and all required part placements are already done.
        /// Static because it reads only from ServiceRegistry — no instance state needed.
        /// </summary>
        public static bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session) ||
                session == null ||
                session.ToolController == null)
            {
                return false;
            }

            // If a tool is actively equipped, always lock parts.
            if (!string.IsNullOrWhiteSpace(session.ToolController.ActiveToolId))
                return true;

            // If the step has a configured (incomplete) tool action, only lock parts
            // after all required part placements are done. Mixed placement+tool steps
            // (e.g. "place 4 posts then clamp") must allow part interaction first.
            if (session.ToolController.TryGetPrimaryActionSnapshot(
                    out ToolActionSnapshot snapshot))
            {
                if (!snapshot.IsConfigured || snapshot.IsCompleted)
                    return false;

                // Check if the step still has outstanding part placements
                if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController) &&
                    !partController.AreActiveStepRequiredPartsPlaced())
                {
                    return false; // parts still need placing — don't lock for tools yet
                }

                return true;
            }

            return false;
        }

        // ── Preview mode resolution ───────────────────────────────────────────

        /// <summary>
        /// Resolves the preview mode and speed for the current target.
        /// Returns null only when the step/profile should skip preview entirely.
        /// Speed escalates with completed target count so repeated actions feel snappy, not tedious.
        /// </summary>
        private (PreviewMode mode, float speed)? ResolvePreviewMode()
        {
            if (_toolPreview == null)
                return null;

            // Must be a Use-family step
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                OseLog.Info("[Interaction] ResolvePreviewMode: no MachineSessionController");
                return null;
            }
            StepController stepCtrl = session?.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
            {
                OseLog.Info($"[Interaction] ResolvePreviewMode: stepCtrl={stepCtrl != null}, hasActive={stepCtrl?.HasActiveStep}");
                return null;
            }
            StepDefinition step = stepCtrl.CurrentStepDefinition;
            if (step == null || step.ResolvedFamily != Content.StepFamily.Use)
            {
                OseLog.Info($"[Interaction] ResolvePreviewMode: step={step?.id}, family={step?.ResolvedFamily} (need Use)");
                return null;
            }

            // Some profiles manage their own interaction flow (e.g. tape measure uses
            // AnchorToAnchorInteraction) — the profile descriptor opts out of preview.
            string activeProfile = _toolPreview?.GetActiveToolProfile();
            var profileDesc = ToolProfileRegistry.Get(activeProfile);
            if (profileDesc.SkipPreview)
                return null;

            int completedCount = _toolPreview.GetCompletedToolTargetCount();
            OseLog.Info($"[Interaction] ResolvePreviewMode: step='{step.id}', family={step.ResolvedFamily}, completedCount={completedCount}");

            // "I Do, We Do, then reinforcement at increasing speed"
            // ObserveOnly profiles (e.g. framing square) always auto-play — no guided phase.
            if (completedCount == 0)
                return (PreviewMode.Observe, 1f);
            if (completedCount == 1 && !profileDesc.ObserveOnly)
                return (PreviewMode.Guided, 1f);

            // 2+ → auto-play at escalating speed so the visual reinforcement stays
            // but doesn't become tedious. Speed ramps from 1.35x up to the profile cap.
            float speed = Mathf.Min(1f + (completedCount - 1) * 0.35f, profileDesc.PreviewSpeedCap);
            return (PreviewMode.Observe, speed);
        }

        /// <summary>
        /// Enters the Tool Action Preview for the resolved target.
        /// Returns true if preview was activated (caller should skip immediate execution).
        /// </summary>
        private bool TryEnterToolActionPreview(ToolActionContext ctx, PreviewMode mode, float speed = 1f)
        {
            GameObject toolPreview = _toolPreview?.GetToolPreview();
            string profile = _toolPreview?.GetActiveToolProfile();
            OseLog.Info($"[Interaction] TryEnterToolActionPreview: preview={toolPreview?.name ?? "NULL"}, profile='{profile}', mode={mode}");
            if (toolPreview == null)
            {
                OseLog.Info("[Interaction] TryEnterToolActionPreview: no tool preview — falling back to click-to-complete.");
                return false;
            }

            _transitionTo(InteractionState.ToolFocus);

            // Suspend cursor position updates so the preview isn't yanked back each frame
            _toolPreview?.SetToolPreviewPositionSuspended(true);

            // Profile-aware camera: tighten to close-up of the action point
            _guidanceService?.FrameToolAction(ctx.SurfaceWorldPos, profile ?? "");

            // Stop target sphere pulsing during preview
            _targetSphereAnimator?.Stop();

            // Update instruction text for the preview mode
            UpdateInstructionForPreview(mode, _toolPreview?.GetActiveToolId());

            _previewController.Enter(
                ctx,
                toolPreview,
                profile ?? "",
                mode,
                _getMode(),
                speed,
                onComplete: completedTargetId =>
                {
                    _toolPreview?.SetToolPreviewPositionSuspended(false);
                    OseLog.Info($"[Interaction] Tool action preview completed for '{completedTargetId}' — executing tool action.");
                    _toolPreview?.IncrementCompletedToolTargetCount();

                    // Spawn persistent tool BEFORE TryToolAction — it may complete
                    // the step synchronously, unequipping the tool and destroying the preview.
                    TrySpawnPersistentToolOnComplete(
                        completedTargetId,
                        ctx.SurfaceWorldPos,
                        ctx.HasToolActionRotation ? ctx.ToolActionRotation : (Quaternion?)null);

                    if (_partBridge != null && _partBridge.TryToolAction(completedTargetId))
                    {
                        OseLog.Info($"[Interaction] Post-preview TryToolAction SUCCESS for '{completedTargetId}'.");
                    }
                    else
                    {
                        _persistentToolController?.RemoveAt(completedTargetId);
                    }
                    // Ease back to step home framing after the action
                    _guidanceService?.ReturnFromToolAction();
                    RestoreInstructionAfterPreview();
                    _transitionTo(InteractionState.Idle);
                },
                onCancel: () =>
                {
                    _toolPreview?.SetToolPreviewPositionSuspended(false);
                    OseLog.Info("[Interaction] Tool action preview cancelled — returning to idle.");
                    // Return to step home on cancel too
                    _guidanceService?.ReturnFromToolAction();
                    RestoreInstructionAfterPreview();
                    _transitionTo(InteractionState.Idle);
                },
                onActionDone: (doneTargetId, actionPos, actionRot) =>
                {
                    // For persistent tools (clamps), convert the cursor preview in-place
                    // so it stays at the target — no clone, no return animation.
                    // When the author has specified a placement rotation, use it directly
                    // rather than the animation's final pose (TorquePreview spins the
                    // preview 120° — the authored rotation should be the resting state).
                    // "mesh" packages: TargetWorldRotation is the live sphere world rotation
                    // (previewRoot.rotation * placement.rotation) — identical to TTAW preview.
                    // Legacy packages: apply grip correction to the stored Euler value.
                    Quaternion placementRot = actionRot;
                    if (ctx.HasToolActionRotation)
                    {
                        if (ctx.ToolActionRotationIsMesh)
                            placementRot = ctx.TargetWorldRotation;
                        else
                        {
                            placementRot = ctx.ToolActionRotation;
                            if (ctx.ToolPose != null && ctx.ToolPose.HasGripRotation)
                                placementRot = ctx.ToolActionRotation * Quaternion.Inverse(ctx.ToolPose.GetGripRotation());
                        }
                    }

                    // Offset placement so the tool's tipPoint lands at the surface,
                    // matching the editor preview which positions the GO root at (surface - tip*scale).
                    Vector3 placementPos = ctx.SurfaceWorldPos;
                    if (ctx.HasToolActionRotation && ctx.ToolPose != null && ctx.ToolPose.HasTipPoint)
                    {
                        float s = toolPreview != null ? toolPreview.transform.localScale.x : 1f;
                        placementPos = ctx.SurfaceWorldPos - placementRot * (ctx.ToolPose.GetTipPoint() * s);
                    }

                    if (TryConvertPreviewToPersistentAtAction(doneTargetId, placementPos, placementRot))
                    {
                        _previewController.SkipReturn();
                    }
                });

            return true;
        }

        // ── Instruction text management ───────────────────────────────────────

        private void UpdateInstructionForPreview(PreviewMode mode, string toolId)
        {
            if (!ServiceRegistry.TryGet<IStepPresenter>(out var ui)) return;

            // Save the step's original instruction for restoration after preview
            if (_savedInstruction == null)
            {
                if (ServiceRegistry.TryGet<IMachineSessionController>(out var sess))
                {
                    var step = sess?.AssemblyController?.StepController?.CurrentStepDefinition;
                    _savedInstruction = step?.ResolvedInstructionText ?? "";
                }
            }

            string toolName = string.IsNullOrEmpty(toolId) ? "tool" : toolId.Replace("_", " ");
            string instruction = mode switch
            {
                PreviewMode.Observe => $"Watch how the {toolName} is used",
                PreviewMode.Guided  => "Your turn \u2014 drag in the direction shown",
                _ => null
            };

            if (instruction != null)
                ui.ShowInstruction(instruction);
        }

        private void RestoreInstructionAfterPreview()
        {
            if (_savedInstruction == null) return;
            if (ServiceRegistry.TryGet<IStepPresenter>(out var ui))
                ui.ShowInstruction(_savedInstruction);
            _savedInstruction = null;
        }

        // ── Persistent tool delegation ────────────────────────────────────────

        private bool TryConvertPreviewToPersistentAtAction(string targetId, Vector3 actionPos, Quaternion actionRot)
            => _persistentToolController?.TryConvertPreviewAtAction(targetId, actionPos, actionRot) ?? false;

        private void TrySpawnPersistentToolOnComplete(string targetId, Vector3 worldPos, Quaternion? rotationOverride = null)
            => _persistentToolController?.TryConvertPreviewOnComplete(targetId, worldPos, rotationOverride);
    }
}
