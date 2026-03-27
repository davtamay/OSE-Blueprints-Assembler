using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Use"/> (tool_action) steps.
    /// Orchestrates four focused sub-systems: spawning, detection,
    /// animation, and measurement — keeping this class as a thin
    /// lifecycle facade over <see cref="IStepFamilyHandler"/>.
    /// </summary>
    internal sealed class UseStepHandler : IStepFamilyHandler
    {
        // ── Feedback defaults ──
        private static readonly Color DefaultCompletionColor = new Color(0.2f, 1.0f, 0.4f, 0.9f);
        private const float DefaultPulseScale = 1.8f;

        // ── Dependencies ──
        private readonly IBridgeContext _ctx;
        private readonly ToolTargetSpawner _spawner;
        private readonly ToolTargetDetector _detector;
        private readonly ToolTargetAnimator _animator;
        private readonly MeasureInteractionHandler _measure;

        // ── State ──
        private string _activeProfile;
        private StepProfile _activeProfileEnum;
        private Color _completionEffectColor = DefaultCompletionColor;
        private string _completionParticleId;
        private float _completionPulseScale = DefaultPulseScale;
        private int _completedTargetCountForStep;

        public UseStepHandler(IBridgeContext context)
        {
            _ctx = context;
            _spawner = new ToolTargetSpawner(context, context);
            _detector = new ToolTargetDetector(context, _spawner.SpawnedTargets);
            _animator = new ToolTargetAnimator(context, _detector, _spawner.SpawnedTargets);
            _measure = new MeasureInteractionHandler(context, _spawner, _animator);
        }

        // ── Public accessors for bridge delegation ──

        /// <summary>Number of currently-spawned tool-action target markers.</summary>
        public int SpawnedTargetCount => _spawner.SpawnedTargets.Count;

        /// <summary>
        /// Number of tool targets completed so far in this step.
        /// Used by the preview system to resolve the learning phase:
        /// 0 → Observe ("I Do"), 1 → Guided ("We Do"), 2+ → Solo ("You Do").
        /// </summary>
        public int CompletedTargetCountForStep => _completedTargetCountForStep;

        /// <summary>Increments the completed target count. Called by the orchestrator after a preview completes.</summary>
        public void IncrementCompletedTargetCount() => _completedTargetCountForStep++;

        /// <summary>
        /// Returns world positions of all currently-spawned tool-action target markers.
        /// </summary>
        public Vector3[] GetActiveToolTargetPositions()
        {
            List<GameObject> targets = _spawner.SpawnedTargets;
            if (targets.Count == 0)
                return System.Array.Empty<Vector3>();

            var positions = new Vector3[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                positions[i] = t != null ? t.transform.position : Vector3.zero;
            }
            return positions;
        }

        /// <summary>
        /// Returns combined world bounds for the currently spawned tool-action markers.
        /// </summary>
        public bool TryGetSpawnedTargetBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            List<GameObject> targets = _spawner.SpawnedTargets;
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject marker = targets[i];
                if (marker == null || !marker.activeInHierarchy)
                    continue;

                if (!TryGetWorldBounds(marker, out Bounds markerBounds))
                    continue;

                if (!hasBounds)
                {
                    bounds = markerBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(markerBounds);
                }
            }

            return hasBounds;
        }

        // ====================================================================
        //  IStepFamilyHandler
        // ====================================================================

        public void OnStepActivated(in StepHandlerContext context)
        {
            _completedTargetCountForStep = 0;
            _activeProfile = context.Step.profile;
            _activeProfileEnum = context.Step.ResolvedProfile;

            var fb = context.Step.feedback;
            _completionEffectColor = TryParseHexColor(fb?.completionEffectColor, DefaultCompletionColor);
            _completionPulseScale = fb != null && fb.completionPulseScale > 0f
                ? fb.completionPulseScale
                : DefaultPulseScale;
            _completionParticleId = fb?.completionParticleId;

            _measure.OnStepActivated(context.Step);
            _spawner.Refresh(_activeProfile, _activeProfileEnum);
        }

        public bool TryHandlePointerAction(in StepHandlerContext context) => false;

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos) => false;

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            _animator.UpdateVisuals();
            _animator.UpdateCursorProximity();
            _ctx.CursorManager?.UpdateReadyPulse();
            _measure.Tick();

            if (_spawner.RetryPending)
                _spawner.Refresh(_activeProfile, _activeProfileEnum);
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            _spawner.Clear();
            _measure.Cleanup();
        }

        public void Cleanup()
        {
            _spawner.Clear();
            _measure.Reset();
            _activeProfile = null;
            _activeProfileEnum = StepProfile.None;
            _completedTargetCountForStep = 0;
        }

        // ====================================================================
        //  Public methods (called by bridge)
        // ====================================================================

        /// <summary>Re-spawns tool action target markers for the current step.</summary>
        public void RefreshToolActionTargets() => _spawner.Refresh(_activeProfile, _activeProfileEnum);

        /// <summary>Destroys all spawned tool action target markers.</summary>
        public void ClearToolActionTargets() => _spawner.Clear();

        /// <summary>Applies fail-flash colour to all spawned tool targets.</summary>
        public void FlashToolTargetOnFailure() => _animator.FlashOnFailure();

        /// <summary>Detects the tool action target at the given screen position.</summary>
        public bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
            => _detector.TryGetToolActionTargetAtScreen(screenPos, out targetInfo);

        /// <summary>Resolves the target that can execute a Use step from the pointer.</summary>
        public bool TryResolveToolActionTargetForExecution(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
            => _detector.TryResolveToolActionTargetForExecution(screenPos, out targetInfo);

        /// <summary>Returns world position of the nearest tool target within screen proximity.</summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
            => _detector.TryGetNearestToolTargetWorldPos(screenPos, out worldPos);

        /// <summary>Focuses camera on a tool target near the pointer.</summary>
        public bool TryFocusCameraOnToolTarget(Vector2 screenPos)
            => _detector.TryFocusCameraOnToolTarget(screenPos);

        // ====================================================================
        //  Tool action execution
        // ====================================================================

        /// <summary>
        /// Executes the tool primary action via ToolRuntimeController.
        /// For Measure profile, the first anchor click suppresses step completion
        /// and spawns the end anchor guide. The second click completes the step.
        /// </summary>
        public bool TryExecuteToolPrimaryAction(
            string interactedTargetId,
            out bool shouldCompleteStep,
            out bool handled)
        {
            shouldCompleteStep = false;
            handled = false;

            // Phase 2: end anchor tap — spawn effect before clearing targets
            if (_measure.IsActive && _measure.TryCompletePhase2(interactedTargetId, out shouldCompleteStep, out _))
            {
                _animator.SpawnClickEffect(
                    interactedTargetId, _activeProfile, _activeProfileEnum,
                    _completionEffectColor, _completionPulseScale, _completionParticleId,
                    out _, null);
                _spawner.Clear();
                handled = true;
                return true;
            }

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session.ToolController == null)
            {
                OseLog.VerboseInfo("[UseStepHandler] TryExecuteToolPrimaryAction: no session or tool controller.");
                return false;
            }

            ToolRuntimeController.ToolActionExecutionResult toolResult =
                session.ToolController.TryExecutePrimaryAction(interactedTargetId);

            handled = toolResult.Handled;
            shouldCompleteStep = toolResult.ShouldCompleteStep;

            bool actionRan = handled && toolResult.FailureReason == ToolActionFailureReason.None;
            if (!actionRan)
            {
                if (toolResult.FailureReason != ToolActionFailureReason.None)
                    OseLog.Info($"[UseStepHandler] Tool action rejected ({toolResult.FailureReason}): {toolResult.Message}.");
                return false;
            }

            OseLog.Info($"[UseStepHandler] Tool action succeeded on '{interactedTargetId}'. shouldComplete={shouldCompleteStep}, profile={_activeProfile}.");

            string measureStartAnchor = _measure.Payload?.startAnchorTargetId;
            _animator.SpawnClickEffect(
                interactedTargetId, _activeProfile, _activeProfileEnum,
                _completionEffectColor, _completionPulseScale, _completionParticleId,
                out Vector3? anchorWorldPos, measureStartAnchor);

            if (anchorWorldPos.HasValue)
                _measure.SetAnchorAWorldPos(anchorWorldPos.Value);

            // Measure profile: suppress step completion after first anchor, enter phase 2
            if (_measure.IsMeasureProfile(_activeProfileEnum) && shouldCompleteStep && _measure.Payload != null)
            {
                shouldCompleteStep = false;
                _spawner.Clear();
                _measure.BeginPhase2();
                OseLog.Info("[UseStepHandler] Measure phase 1 complete — tap end anchor to finish.");
                return true;
            }

            bool actionCompletedButStepContinues =
                !shouldCompleteStep &&
                toolResult.CurrentCount > 0 &&
                toolResult.CurrentCount >= toolResult.RequiredCount;

            if (actionCompletedButStepContinues)
                _ctx.PreviewManager?.AdvanceSequentialTarget();

            _spawner.Refresh(_activeProfile, _activeProfileEnum);
            return true;
        }

        /// <summary>Completes the step if the tool action result says so.</summary>
        public static bool HandleToolPrimaryResult(
            MachineSessionController session,
            StepController stepController,
            bool shouldCompleteStep)
        {
            if (!shouldCompleteStep)
            {
                OseLog.Info("[UseStepHandler] HandleToolPrimaryResult: shouldCompleteStep=false — not completing.");
                return true;
            }

            string stepId = stepController?.CurrentStepState.StepId ?? "unknown";
            OseLog.Info($"[UseStepHandler] HandleToolPrimaryResult: COMPLETING step '{stepId}'.");
            stepController.CompleteStep(session.GetElapsedSeconds());
            return true;
        }

        /// <summary>
        /// Executes the tool action using the current pointer position.
        /// </summary>
        public bool TryExecuteToolPrimaryActionFromPointer(
            MachineSessionController session,
            StepController stepController,
            bool allowStepCompletion = true)
        {
            string interactedTargetId = null;
            StepDefinition currentStep = stepController != null && stepController.HasActiveStep
                ? stepController.CurrentStepDefinition
                : null;

            if (ToolTargetDetector.TryGetPointerPosition(out Vector2 pointerPos) &&
                _detector.TryResolveToolActionTargetForExecution(pointerPos, out ToolActionTargetInfo resolvedTarget))
                interactedTargetId = resolvedTarget.TargetId;

            // Measure phase 2 fallback: relaxed detection for end anchor
            if (interactedTargetId == null && _measure.IsActive)
            {
                if (_detector.TryGetToolActionTargetAtScreen(pointerPos, out ToolActionTargetInfo hitTarget))
                    interactedTargetId = hitTarget.TargetId;
            }

            if (interactedTargetId == null)
            {
                OseLog.VerboseInfo($"[UseStepHandler] Tool action: no ready tool target resolved. Spawned targets={_spawner.SpawnedTargets.Count}.");
                return false;
            }

            interactedTargetId = _spawner.NormalizeSequentialExecutionTargetId(currentStep, interactedTargetId);

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.VerboseInfo($"[UseStepHandler] Tool action failed for target '{interactedTargetId}'.");
                return false;
            }

            if (!handled)
                return false;

            if (!allowStepCompletion)
                return true;

            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        // ====================================================================
        //  Private helpers
        // ====================================================================

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = MaterialHelper.GetRenderers(target);
            if (renderers != null && renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        bounds.Encapsulate(renderers[i].bounds);
                }
                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                        bounds.Encapsulate(colliders[i].bounds);
                }
                return true;
            }

            return false;
        }

        private static Color TryParseHexColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
                return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : fallback;
        }
    }
}
