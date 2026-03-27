using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns tool action resolution, execution, tool mode gating,
    /// and tool preview cursor management.
    /// Extracted from PartInteractionBridge (Phase 5).
    /// </summary>
    internal sealed class ToolActionExecutor
    {
        private readonly IBridgeContext _ctx;

        private Vector3 _lastToolActionWorldPos;
        public Vector3 LastToolActionWorldPos => _lastToolActionWorldPos;

        public ToolActionExecutor(IBridgeContext context)
        {
            _ctx = context;
        }

        // ════════════════════════════════════════════════════════════════════
        // Tool mode lock
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns true when a tool is active, configured, and not completed,
        /// preventing part selection/movement while the tool is in use.
        /// </summary>
        public bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            if (session?.ToolController == null)
                return false;

            if (!session.ToolController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot))
                return false;

            if (!snapshot.IsConfigured || snapshot.IsCompleted)
                return false;

            // Mixed placement+tool steps: don't lock parts until all placements are done.
            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController) &&
                !partController.AreActiveStepRequiredPartsPlaced())
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Blocks pointer-down from reaching part selection/drag when tool mode is active.
        /// </summary>
        public bool TryHandleToolActionPointerDown(Vector2 screenPos)
        {
            if (!IsToolModeLockedForParts())
                return false;

            // Don't block pipe_connection steps — port sphere clicks need to pass through.
            var connectHandler = _ctx.ConnectHandler;
            if (connectHandler != null && connectHandler.HasActivePortSpheres)
                return false;

            // Block pointer-down from reaching part selection/drag when tool mode is active.
            // The actual tool action execution is handled exclusively by the canonical action
            // path (HandleConfirmOrToolPrimaryAction) to prevent double-execution per click.
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // Tool action resolution & execution
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves the executable tool target for the current click without executing it.
        /// Called by V2 orchestrator via IPartActionBridge.
        /// </summary>
        public bool TryResolveToolActionTarget(Vector2 screenPos, out ToolActionContext context)
        {
            context = default;

            if (!Application.isPlaying)
                return false;

            if (!TryGetToolActionTargetForExecution(screenPos, out ToolActionTargetInfo resolvedTarget) || resolvedTarget == null)
                return false;

            // Resolve the active tool's spatial pose metadata (if authored).
            Content.ToolPoseConfig toolPose = null;
            var activeToolId = GetActiveToolId();
            if (!string.IsNullOrEmpty(activeToolId))
            {
                var package = _ctx.Spawner?.CurrentPackage;
                if (package != null && package.TryGetTool(activeToolId, out var toolDef))
                    toolPose = toolDef.toolPose;
            }

            context = new ToolActionContext
            {
                TargetId = resolvedTarget.TargetId,
                TargetWorldPos = resolvedTarget.transform.position,
                SurfaceWorldPos = resolvedTarget.SurfaceWorldPos,
                TargetWorldRotation = resolvedTarget.TargetWorldRotation,
                WeldAxis = resolvedTarget.WeldAxis,
                WeldLength = resolvedTarget.WeldLength,
                HasToolActionRotation = resolvedTarget.HasToolActionRotation,
                ToolActionRotation = resolvedTarget.ToolActionRotation,
                ToolPose = toolPose,
            };
            return !string.IsNullOrWhiteSpace(context.TargetId);
        }

        /// <summary>
        /// Executes the tool primary action for an explicitly resolved target id.
        /// Called by V2 orchestrator via IPartActionBridge.
        /// </summary>
        public bool TryExecuteToolAction(string interactedTargetId)
        {
            if (!Application.isPlaying)
                return false;

            return TryHandleToolAction(interactedTargetId);
        }

        /// <summary>
        /// Directly executes the tool primary action using a direct hit on a spawned
        /// tool target sphere, bypassing the canonical action router.
        /// Called by V2 orchestrator via IPartActionBridge.
        /// </summary>
        public bool TryExecuteToolActionAtScreen(Vector2 screenPos)
        {
            if (!Application.isPlaying)
                return false;

            // Pipe connection steps: handle even when a tool is held.
            var router = _ctx.Router;
            if (router != null && TryBuildHandlerContext(out var pipeCtx) && router.TryHandlePointerDown(in pipeCtx, screenPos))
                return true;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            bool allowToolActionStepCompletion = step == null || !step.IsPlacement;

            var useHandler = _ctx.UseHandler;
            int spawnedTargetCount = useHandler?.SpawnedTargetCount ?? 0;
            OseLog.Info($"[PartInteraction] TryExternalToolAction at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={spawnedTargetCount}. Tool='{session.ToolController?.ActiveToolId ?? "none"}'.");
            if (!TryResolveToolActionTarget(screenPos, out ToolActionContext ctx))
            {
                OseLog.Info($"[PartInteraction] TryExternalToolAction: no ready tool target resolved at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={spawnedTargetCount}.");
                return false;
            }

            // Capture world position before executing (the target may be destroyed/refreshed after).
            _lastToolActionWorldPos = ctx.TargetWorldPos;

            return TryHandleToolAction(ctx.TargetId);
        }

        private bool TryHandleToolAction(string interactedTargetId)
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            bool allowToolActionStepCompletion = step == null || !step.IsPlacement;

            var useHandler = _ctx.UseHandler;
            int spawnedTargetCount = useHandler?.SpawnedTargetCount ?? 0;
            if (string.IsNullOrWhiteSpace(interactedTargetId))
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: no target id provided. Spawned={spawnedTargetCount}.");
                return false;
            }

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: action rejected for '{interactedTargetId}'.");
                return false;
            }

            if (!handled)
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: not handled for '{interactedTargetId}'.");
                return false;
            }

            OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: success on '{interactedTargetId}'. shouldComplete={shouldCompleteStep}, allowCompletion={allowToolActionStepCompletion}.");
            if (!allowToolActionStepCompletion)
                return true;

            OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: calling HandleToolPrimaryResult shouldComplete={shouldCompleteStep}.");
            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        // ════════════════════════════════════════════════════════════════════
        // UseStepHandler delegations
        // ════════════════════════════════════════════════════════════════════

        public bool TryExecuteToolPrimaryAction(string interactedTargetId, out bool shouldCompleteStep, out bool handled)
        {
            var useHandler = _ctx.UseHandler;
            if (useHandler != null)
                return useHandler.TryExecuteToolPrimaryAction(interactedTargetId, out shouldCompleteStep, out handled);

            shouldCompleteStep = false;
            handled = false;
            return false;
        }

        public bool HandleToolPrimaryResult(MachineSessionController session, StepController stepController, bool shouldCompleteStep)
            => UseStepHandler.HandleToolPrimaryResult(session, stepController, shouldCompleteStep);

        public bool TryExecuteToolPrimaryActionFromPointer(MachineSessionController session, StepController stepController, bool allowStepCompletion = true)
        {
            var useHandler = _ctx.UseHandler;
            return useHandler != null && useHandler.TryExecuteToolPrimaryActionFromPointer(session, stepController, allowStepCompletion);
        }

        public void RefreshToolActionTargets()
            => _ctx.UseHandler?.RefreshToolActionTargets();

        public void ClearToolActionTargets()
            => _ctx.UseHandler?.ClearToolActionTargets();

        public bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            var useHandler = _ctx.UseHandler;
            return useHandler != null && useHandler.TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        public bool TryGetToolActionTargetForExecution(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            var useHandler = _ctx.UseHandler;
            return useHandler != null && useHandler.TryResolveToolActionTargetForExecution(screenPos, out targetInfo);
        }

        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            var useHandler = _ctx.UseHandler;
            return useHandler != null && useHandler.TryGetNearestToolTargetWorldPos(screenPos, out worldPos);
        }

        public Vector3[] GetActiveToolTargetPositions()
            => _ctx.UseHandler?.GetActiveToolTargetPositions() ?? Array.Empty<Vector3>();

        public bool TryFocusCameraOnToolTarget(Vector2 screenPos)
        {
            var useHandler = _ctx.UseHandler;
            return useHandler != null && useHandler.TryFocusCameraOnToolTarget(screenPos);
        }

        public void FlashToolTargetOnFailure()
            => _ctx.UseHandler?.FlashToolTargetOnFailure();

        public int GetCompletedToolTargetCount()
            => _ctx.UseHandler?.CompletedTargetCountForStep ?? 0;

        public void IncrementCompletedToolTargetCount()
            => _ctx.UseHandler?.IncrementCompletedTargetCount();

        public int SpawnedTargetCount
            => _ctx.UseHandler?.SpawnedTargetCount ?? 0;

        // ════════════════════════════════════════════════════════════════════
        // Tool preview cursor
        // ════════════════════════════════════════════════════════════════════

        public void RefreshToolPreviewIndicator()
            => _ = RefreshToolPreviewIndicatorAsync();

        private async System.Threading.Tasks.Task RefreshToolPreviewIndicatorAsync()
        {
            var cursorManager = _ctx.CursorManager;
            var visualFeedback = _ctx.VisualFeedback;
            await cursorManager.RefreshAsync(_ctx.Spawner, _ctx.Setup, visualFeedback?.HintPreview == cursorManager.ToolPreview, _ctx.ClearHintHighlight);

            // In XR mode, make the tool preview grabbable with toolPose-driven attach point
            if (cursorManager.ToolPreview != null && UnityEngine.XR.XRSettings.isDeviceActive)
            {
                bool isControllerMode = !IsHandTrackingActive();
                cursorManager.ConfigureXRGrab(isControllerMode);
            }
        }

        public void UpdateToolPreviewIndicatorPosition(Vector2 screenPos)
        {
            _ctx.CursorManager.UpdatePosition(_ctx.IsDragging, screenPos);
        }

        public void ClearToolPreviewIndicator()
        {
            var cursorManager = _ctx.CursorManager;
            var visualFeedback = _ctx.VisualFeedback;
            cursorManager.Clear(visualFeedback?.HintPreview == cursorManager.ToolPreview, _ctx.ClearHintHighlight);
        }

        public GameObject GetToolPreview()
            => _ctx.CursorManager.ToolPreview;

        public void SetToolPreviewPositionSuspended(bool suspended)
            => _ctx.CursorManager.PositionUpdateSuspended = suspended;

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        public string GetActiveToolId()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return null;
            return session?.ToolController?.ActiveToolId;
        }

        public string GetActiveToolProfile()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return null;
            var stepCtrl = session?.AssemblyController?.StepController;
            return stepCtrl != null && stepCtrl.HasActiveStep ? stepCtrl.CurrentStepDefinition?.profile : null;
        }

        private static bool TryBuildHandlerContext(out StepHandlerContext context)
        {
            context = default;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
                return false;
            var step = stepCtrl.CurrentStepDefinition;
            context = new StepHandlerContext(step, stepCtrl, step.id, session.GetElapsedSeconds());
            return true;
        }

        private static bool IsHandTrackingActive()
        {
            ServiceRegistry.TryGet<OSE.Interaction.XRRigModeSwitcher>(out var switcher);
            return switcher != null && switcher.UsingHands;
        }
    }
}
