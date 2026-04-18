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
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            if (session?.ToolController == null)
                return false;

            if (!session.ToolController.TryGetPrimaryActionSnapshot(out ToolActionSnapshot snapshot))
                return false;

            if (!snapshot.IsConfigured || snapshot.IsCompleted)
                return false;

            // Mixed placement+tool steps: don't lock parts until all placements are done.
            if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController) &&
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
        /// Called by orchestrator via IPartActionBridge.
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
            bool toolActionRotIsMesh = false;
            var activeToolId = GetActiveToolId();
            if (!string.IsNullOrEmpty(activeToolId))
            {
                var package = _ctx.Spawner?.CurrentPackage;
                if (package != null && package.TryGetTool(activeToolId, out var toolDef))
                {
                    toolPose = toolDef.toolPose;
                    toolActionRotIsMesh = package.previewConfig?.targetRotationFormat == "mesh";
                }
            }

            bool isPersistent = false;
            if (!string.IsNullOrEmpty(activeToolId))
            {
                var package = _ctx.Spawner?.CurrentPackage;
                if (package != null && package.TryGetTool(activeToolId, out var toolDefForPersist))
                    isPersistent = toolDefForPersist.persistent;
            }

            // Compute stable positions from local-space snapshots (set at spawn,
            // unaffected by ToolTargetAnimator's pulse). TransformPoint keeps them
            // accurate when the assembly scale changes after spawn.
            Transform markerParent = resolvedTarget.transform.parent;
            Vector3 stableWorldPos = markerParent != null
                ? markerParent.TransformPoint(resolvedTarget.BaseLocalPosition)
                : resolvedTarget.transform.position;
            Vector3 surfaceWorldPos = markerParent != null
                ? markerParent.TransformPoint(resolvedTarget.SurfaceLocalPosition)
                : resolvedTarget.transform.position;

            context = new ToolActionContext
            {
                TargetId = resolvedTarget.TargetId,
                TargetWorldPos = stableWorldPos,
                SurfaceWorldPos = surfaceWorldPos,
                TargetWorldRotation = resolvedTarget.TargetWorldRotation,
                WeldAxis = resolvedTarget.WeldAxis,
                WeldLength = resolvedTarget.WeldLength,
                HasToolActionRotation = resolvedTarget.HasToolActionRotation,
                ToolActionRotation = resolvedTarget.ToolActionRotation,
                ToolPose = toolPose,
                ToolActionRotationIsMesh = toolActionRotIsMesh,
                InstantPlacement = isPersistent,
                AssemblyScale = _ctx.CursorManager.AssemblyScale,
            };

            // Resolve optional part effect for progress-driven part movement
            context.PartEffect = TryResolvePartEffect(context.TargetId);

            return !string.IsNullOrWhiteSpace(context.TargetId);
        }

        /// <summary>
        /// Executes the tool primary action for an explicitly resolved target id.
        /// Called by orchestrator via IPartActionBridge.
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
        /// Called by orchestrator via IPartActionBridge.
        /// </summary>
        public bool TryExecuteToolActionAtScreen(Vector2 screenPos)
        {
            if (!Application.isPlaying)
                return false;

            // Pipe connection steps: handle even when a tool is held.
            var router = _ctx.Router;
            if (router != null && TryBuildHandlerContext(out var pipeCtx) && router.TryHandlePointerDown(in pipeCtx, screenPos))
                return true;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

        public bool HandleToolPrimaryResult(IMachineSessionController session, StepController stepController, bool shouldCompleteStep)
            => UseStepHandler.HandleToolPrimaryResult(session, stepController, shouldCompleteStep);

        public bool TryExecuteToolPrimaryActionFromPointer(IMachineSessionController session, StepController stepController, bool allowStepCompletion = true)
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
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return null;
            return session?.ToolController?.ActiveToolId;
        }

        public string GetActiveToolProfile()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return null;
            var stepCtrl = session?.AssemblyController?.StepController;
            return stepCtrl != null && stepCtrl.HasActiveStep ? stepCtrl.CurrentStepDefinition?.profile : null;
        }

        private static bool TryBuildHandlerContext(out StepHandlerContext context)
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

        private static bool IsHandTrackingActive()
        {
            ServiceRegistry.TryGet<OSE.Interaction.XRRigModeSwitcher>(out var switcher);
            return switcher != null && switcher.UsingHands;
        }

        // ════════════════════════════════════════════════════════════════════
        // Part effect resolution
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves an <see cref="IPartEffect"/> for the target's associated part.
        /// When present the tool action preview will drive the part from its current
        /// local pose to the step-scoped or assembled end pose in sync with progress.
        /// Returns null when no part is associated or no movement is needed.
        /// </summary>
        private IPartEffect TryResolvePartEffect(string targetId)
        {
            var spawner = _ctx.Spawner;
            var package = spawner?.CurrentPackage;
            if (package == null) return null;

            if (!package.TryGetTarget(targetId, out var targetDef))
                return null;

            string partId = targetDef.associatedPartId;
            if (string.IsNullOrEmpty(partId))
                return null;

            GameObject partGo = _ctx.FindSpawnedPart(partId);
            if (partGo == null) return null;

            // Resolve current step to determine the end pose
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return null;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
                return null;

            string currentStepId = stepCtrl.CurrentStepDefinition.id;

            // Resolve the authored interaction payload (Phase B) from the
            // current step's requiredToolActions. Null means "no payload → lerp / auto".
            ToolPartInteraction payload = TryResolveInteractionPayload(stepCtrl.CurrentStepDefinition, targetId, out var toolPose);

            // End pose: honor payload.toPose when set (Phase F), fall back to implicit
            // stepPoses[currentStepId] → assembledPosition chain.
            if (!ResolveEndPose(payload?.toPose, partId, currentStepId, spawner,
                                out Vector3 endPos, out Quaternion endRot, out Vector3 endScale))
                return null;

            // Start pose: part's current local transform. Never authored — the pose
            // chain derives start from whatever the previous task left the part at.
            Vector3 startPos = partGo.transform.localPosition;
            Quaternion startRot = partGo.transform.localRotation;
            Vector3 startScale = partGo.transform.localScale;

            // Skip if already at the end pose
            if (Vector3.Distance(startPos, endPos) < 0.0001f &&
                Quaternion.Angle(startRot, endRot) < 0.01f)
                return null;

            Transform previewRoot = _ctx.Setup?.PreviewRoot;

            var args = new PartEffectBuildArgs
            {
                PartTransform = partGo.transform,
                PreviewRoot   = previewRoot,
                Start         = new PoseSnapshot(startPos, startRot, startScale),
                End           = new PoseSnapshot(endPos,   endRot,   endScale),
                Payload       = payload,
                ToolPose      = toolPose,
            };

            string archetype = !string.IsNullOrEmpty(payload?.archetype)
                ? payload.archetype
                : PartEffectArchetypes.Lerp;
            return PartEffectRegistry.Build(archetype, args);
        }

        /// <summary>
        /// Walks the step's <c>requiredToolActions</c> for the entry keyed to
        /// <paramref name="targetId"/>, returning its authored interaction payload
        /// and the referenced tool's pose metadata. Both are null when absent.
        /// </summary>
        private ToolPartInteraction TryResolveInteractionPayload(
            StepDefinition step, string targetId, out ToolPoseConfig toolPose)
        {
            toolPose = null;
            if (step?.requiredToolActions == null || string.IsNullOrEmpty(targetId)) return null;

            ToolActionDefinition match = null;
            foreach (var a in step.requiredToolActions)
            {
                if (a != null && a.targetId == targetId) { match = a; break; }
            }
            if (match == null) return null;

            // Look up the tool's pose for archetypes that reference tool_action_axis.
            var package = _ctx.Spawner?.CurrentPackage;
            if (package?.tools != null && !string.IsNullOrEmpty(match.toolId))
            {
                foreach (var t in package.tools)
                {
                    if (t != null && t.id == match.toolId) { toolPose = t.toolPose; break; }
                }
            }

            return match.interaction;
        }

        /// <summary>
        /// Phase F end-pose resolver. Honors the authored <c>toPose</c> token from
        /// <see cref="ToolPartInteraction"/> and falls back to the legacy implicit
        /// resolution (<c>stepPoses[currentStepId]</c> → <c>assembledPosition</c>)
        /// when the token is <c>"auto"</c> / null / empty.
        ///
        /// <para>Token grammar (see <see cref="ToPoseTokens"/>):</para>
        /// <list type="bullet">
        ///   <item><c>"auto"</c> / null → implicit chain (unchanged pre-F behavior).</item>
        ///   <item><c>"start"</c> → <see cref="PartPreviewPlacement.startPosition"/>.</item>
        ///   <item><c>"assembled"</c> → <see cref="PartPreviewPlacement.assembledPosition"/>.</item>
        ///   <item><c>"step:&lt;stepId&gt;"</c> → named <see cref="StepPoseEntry"/>.</item>
        /// </list>
        ///
        /// <para>Returns <c>false</c> only when neither the authored token nor the
        /// fallback chain can produce a pose (e.g. the part has no placement data at
        /// all). Stale <c>"step:&lt;id&gt;"</c> references silently degrade to the
        /// implicit chain — authoring-time validation surfaces the stale reference in
        /// the TTAW panel before it reaches runtime.</para>
        /// </summary>
        private static bool ResolveEndPose(
            string toPoseToken, string partId, string currentStepId,
            PackagePartSpawner spawner,
            out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scale = Vector3.one;

            PartPreviewPlacement pp = spawner.FindPartPlacement(partId);

            // Explicit token dispatch.
            if (!ToPoseTokens.IsAuto(toPoseToken))
            {
                if (toPoseToken == ToPoseTokens.Start && pp != null)
                {
                    AssignFromFloats(pp.startPosition, pp.startRotation, pp.startScale, out pos, out rot, out scale);
                    return true;
                }
                if (toPoseToken == ToPoseTokens.Assembled && pp != null)
                {
                    AssignFromFloats(pp.assembledPosition, pp.assembledRotation, pp.assembledScale, out pos, out rot, out scale);
                    return true;
                }
                if (toPoseToken.StartsWith(ToPoseTokens.StepPrefix, System.StringComparison.Ordinal))
                {
                    string stepId = toPoseToken.Substring(ToPoseTokens.StepPrefix.Length);
                    StepPoseEntry sp = spawner.FindPartStepPose(partId, stepId);
                    if (sp != null)
                    {
                        AssignFromFloats(sp.position, sp.rotation, sp.scale, out pos, out rot, out scale);
                        return true;
                    }
                    // stale reference — fall through to implicit chain
                }
            }

            // Implicit chain (pre-Phase-F behavior, unchanged).
            StepPoseEntry stepPose = spawner.FindPartStepPose(partId, currentStepId);
            if (stepPose != null)
            {
                AssignFromFloats(stepPose.position, stepPose.rotation, stepPose.scale, out pos, out rot, out scale);
                return true;
            }
            if (pp != null)
            {
                AssignFromFloats(pp.assembledPosition, pp.assembledRotation, pp.assembledScale, out pos, out rot, out scale);
                return true;
            }
            return false;
        }

        private static void AssignFromFloats(SceneFloat3 p, SceneQuaternion r, SceneFloat3 s,
            out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = new Vector3(p.x, p.y, p.z);
            rot = !r.IsIdentity ? new Quaternion(r.x, r.y, r.z, r.w) : Quaternion.identity;
            scale = new Vector3(s.x, s.y, s.z);
        }
    }
}
