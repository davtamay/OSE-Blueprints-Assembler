using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;
using UnityEngine.Pool;

namespace OSE.UI.Root
{
    /// <summary>
    /// Spawns, clears, and resolves poses for tool-action target markers.
    /// Owns the live list of spawned marker GameObjects.
    /// </summary>
    internal sealed class ToolTargetSpawner
    {
        private const float ToolTargetColliderRadius = 1.5f;

        private readonly ISpawnerContext _ctx;
        private readonly IPreviewContext _preview;
        private readonly List<GameObject> _spawnedToolActionTargets = new();
        private readonly ObjectPool<GameObject> _markerPool;

        // Targets the user has already clicked in the current step. Refresh
        // skips spawning markers for these IDs even if the runtime snapshot
        // hasn't yet flipped IsCompleted=true (the snapshot can lag a frame
        // or two behind the click → onComplete callback chain). Cleared
        // when the step ID changes.
        private readonly HashSet<string> _hiddenForCurrentStep = new(StringComparer.OrdinalIgnoreCase);
        private string _hiddenSetStepId;

        public ToolTargetSpawner(ISpawnerContext ctx, IPreviewContext preview)
        {
            _ctx = ctx;
            _preview = preview;
            _markerPool = new ObjectPool<GameObject>(
                createFunc: CreateMarkerInstance,
                actionOnGet: static go => go.SetActive(true),
                actionOnRelease: ReleaseMarkerToPool,
                actionOnDestroy: static go => UnityEngine.Object.Destroy(go),
                collectionCheck: false,
                defaultCapacity: 8,
                maxSize: 32);

            // Live-edit hot-reload listener. Authoring tools (TTAW) call
            // IMachineSessionController.HotReloadTargetPlacement when the
            // author edits a target's transform during Play; that publishes
            // this event, which we apply to the live marker GameObject. Owner
            // (UseStepHandler) calls Cleanup() to unsubscribe.
            RuntimeEventBus.Subscribe<TargetPlacementHotReloaded>(OnTargetPlacementHotReloaded);
        }

        /// <summary>
        /// Releases pooled markers and unsubscribes from runtime events. Call
        /// from <see cref="UseStepHandler.Cleanup"/> when the handler is being
        /// torn down (e.g. session end / package reload).
        /// </summary>
        public void Cleanup()
        {
            RuntimeEventBus.Unsubscribe<TargetPlacementHotReloaded>(OnTargetPlacementHotReloaded);
        }

        private void OnTargetPlacementHotReloaded(TargetPlacementHotReloaded evt)
        {
            if (string.IsNullOrEmpty(evt.TargetId)) return;
            if (_spawnedToolActionTargets == null || _spawnedToolActionTargets.Count == 0) return;

            // Linear scan — packages have at most a handful of markers spawned
            // for the active step at any time, and this fires on author edits,
            // not per-frame.
            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker == null) continue;

                ToolActionTargetInfo info = marker.GetComponent<ToolActionTargetInfo>();
                if (info == null || !string.Equals(info.TargetId, evt.TargetId, StringComparison.Ordinal))
                    continue;

                Transform previewRoot = _ctx?.Setup?.PreviewRoot;
                if (previewRoot == null) return;

                // Placements are PreviewRoot-local; convert to world for the
                // live marker. Mirror the same transform path
                // TrySpawnToolActionTargetMarker uses on initial spawn.
                Vector3 localPos = new Vector3(evt.PosX, evt.PosY, evt.PosZ);
                Quaternion localRot = new Quaternion(evt.RotX, evt.RotY, evt.RotZ, evt.RotW);
                Vector3 localScl = new Vector3(evt.SclX, evt.SclY, evt.SclZ);

                marker.transform.SetPositionAndRotation(
                    previewRoot.TransformPoint(localPos),
                    previewRoot.rotation * localRot);
                marker.transform.localScale = localScl;
                return;
            }
        }

        /// <summary>Read-only access to the live spawned marker list.</summary>
        public List<GameObject> SpawnedTargets => _spawnedToolActionTargets;

        /// <summary>True when targets failed to spawn and should be retried next frame.</summary>
        public bool RetryPending { get; private set; }

        // ====================================================================
        //  Public API
        // ====================================================================

        /// <summary>
        /// Re-spawns tool action target markers for the current step.
        /// </summary>
        public void Refresh(string activeProfile, StepProfile activeProfileEnum)
        {
            Clear();
            RetryPending = false;

            PreviewSceneSetup setup = _ctx.Setup;
            if (_ctx.Spawner == null || setup == null)
                return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var earlySession))
                return;

            StepController earlyStepCtrl = earlySession?.AssemblyController?.StepController;
            if (earlyStepCtrl == null || !earlyStepCtrl.HasActiveStep)
                return;

            StepDefinition currentStep = earlyStepCtrl.CurrentStepDefinition;

            if (currentStep?.requiredToolActions == null || currentStep.requiredToolActions.Length == 0)
                return;

            // Reset the "already-clicked" guard when crossing step boundaries.
            if (!string.Equals(_hiddenSetStepId, currentStep.id, StringComparison.OrdinalIgnoreCase))
            {
                _hiddenForCurrentStep.Clear();
                _hiddenSetStepId = currentStep.id;
            }

            IMachineSessionController session = earlySession;
            bool spawnedAny = false;

            bool runtimeMatchesStep =
                session?.ToolController != null &&
                string.Equals(session.ToolController.ActiveStepId, currentStep.id, StringComparison.OrdinalIgnoreCase);

            if (runtimeMatchesStep &&
                TryGetActionSnapshots(out ToolActionSnapshot[] actionSnapshots, out session))
            {
                // Cursor-driven filter: only spawn markers for target IDs
                // that are actionable RIGHT NOW (current span). Null set
                // = no cursor / no taskOrder → spawn all (legacy content).
                HashSet<string> actionable = GetActionableTargetIdSet(currentStep);

                for (int i = 0; i < actionSnapshots.Length; i++)
                {
                    ToolActionSnapshot action = actionSnapshots[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    // Race guard: snapshot.IsCompleted can lag a frame
                    // behind the click. _hiddenForCurrentStep tracks ids
                    // the user just clicked so the marker doesn't briefly
                    // re-spawn during that lag window.
                    if (_hiddenForCurrentStep.Contains(action.TargetId))
                        continue;

                    if (!StepDefinesToolAction(currentStep, action.ToolId, action.TargetId))
                        continue;

                    if (actionable != null && !actionable.Contains(action.TargetId))
                        continue;

                    if (TrySpawnToolActionTargetMarker(session.Package, setup, action.ToolId, action.TargetId))
                        spawnedAny = true;
                }
            }
            else
            {
                if (!TrySpawnFallbackStepToolActionTargets(currentStep, session?.Package, setup))
                {
                    if (ActiveStepHasRequiredToolActions())
                        RetryPending = true;
                    else
                        TryWarnMissingPrimaryToolActionSnapshot();
                    return;
                }

                spawnedAny = _spawnedToolActionTargets.Count > 0;
                if (spawnedAny)
                    OseLog.Warn($"[ToolTargetSpawner] Falling back to step-defined tool targets for step '{currentStep?.id}'.");
            }

            if (!spawnedAny && ActiveStepHasRequiredToolActions())
                RetryPending = true;
        }

        /// <summary>
        /// Records that the user clicked / selected <paramref name="targetId"/>
        /// in the current step. Future <see cref="Refresh"/> calls within
        /// the same step will not re-spawn a marker for this id, even if
        /// the runtime snapshot's <c>IsCompleted</c> flag hasn't propagated
        /// yet. Cleared automatically on step transition.
        /// </summary>
        public void MarkTargetClicked(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            _hiddenForCurrentStep.Add(targetId);
        }

        /// <summary>Releases all spawned tool action target markers back to the pool.</summary>
        public void Clear()
        {
            for (int i = _spawnedToolActionTargets.Count - 1; i >= 0; i--)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker != null)
                    _markerPool.Release(marker);
            }

            _spawnedToolActionTargets.Clear();
            ToolActionTargetInfo.CurrentActionTarget = null;
        }

        /// <summary>Destroys all pooled markers and disposes the pool. Call once on session end.</summary>
        public void Dispose() => _markerPool.Dispose();

        /// <summary>
        /// Spawns a measure end-anchor marker at the given local pose.
        /// </summary>
        public void SpawnMeasureEndMarker(
            string endAnchorTargetId,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 scale,
            Color idleColor)
        {
            PreviewSceneSetup setup = _ctx.Setup;
            Transform previewRoot = setup?.PreviewRoot;

            GameObject guide = _markerPool.Get();
            if (previewRoot != null)
                guide.transform.SetParent(previewRoot, false);

            guide.name = $"MeasureEndGuide_{endAnchorTargetId}";
            guide.transform.SetLocalPositionAndRotation(localPos, localRot);
            guide.transform.localScale = ResolveToolTargetMarkerScale(scale);
            // No lift — ZTest=Always (OSE/ToolTargetMarker) handles depth.

            PackagePartSpawner.EnsureColliders(guide);
            var col = guide.GetComponent<SphereCollider>();
            if (col != null) col.radius = ToolTargetColliderRadius;
            MaterialHelper.ApplyToolTargetMarker(guide, idleColor);

            var info = guide.GetComponent<ToolActionTargetInfo>();
            info.TargetId = endAnchorTargetId;
            info.RequiredToolId = null;
            info.BaseScale = guide.transform.localScale;
            info.BaseLocalPosition = guide.transform.localPosition;

            _spawnedToolActionTargets.Add(guide);
        }

        // ====================================================================
        //  Actionable target set (cursor-driven)
        // ====================================================================

        /// <summary>
        /// Returns the set of tool-action target IDs that are actionable in
        /// the current cursor span, or <c>null</c> when no cursor filtering
        /// applies (legacy content with no taskOrder — callers spawn every
        /// target). Driven by <see cref="PreviewSpawnManager.GetActionableToolActionTargetIds"/>,
        /// which sources from <see cref="TaskCursor.OpenTasks"/>. Cursor is
        /// the single source of truth for "which target is now" — this
        /// method never maintains its own index / state.
        /// </summary>
        public HashSet<string> GetActionableTargetIdSet(StepDefinition currentStep)
        {
            if (currentStep == null) return null;
            string[] ids = _preview.PreviewManager?.GetActionableToolActionTargetIds();
            if (ids == null || ids.Length == 0) return null;
            return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// If <paramref name="interactedTargetId"/> is not actionable in the
        /// current cursor span, substitute the first actionable target so
        /// the tool action routes to the right place. Pass-through when
        /// cursor filtering is inactive (legacy) or the interaction already
        /// matches.
        /// </summary>
        public string NormalizeSequentialExecutionTargetId(StepDefinition currentStep, string interactedTargetId)
        {
            if (string.IsNullOrWhiteSpace(interactedTargetId) || currentStep == null)
                return interactedTargetId;

            HashSet<string> actionable = GetActionableTargetIdSet(currentStep);
            if (actionable == null || actionable.Count == 0) return interactedTargetId;
            if (actionable.Contains(interactedTargetId)) return interactedTargetId;

            // Not in the actionable set — route to the first one.
            foreach (var tid in actionable) return tid;
            return interactedTargetId;
        }

        // ====================================================================
        //  Pose resolution
        // ====================================================================

        public bool TryResolveToolActionTargetPose(
            MachinePackageDefinition package,
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one * 0.25f;

            // Anchor-resolved targets (anchorRef set) must track the live
            // spawned part — the static targetPlacement.position is baked
            // once at load and goes stale as the part moves through the
            // step chain. TTAW's editor display uses the same priority
            // (live anchor → static placement) so editor and Play stay
            // visually in sync. Without this, Play shows the marker at
            // the bake-time pose while the editor shows it on the bolt.
            TargetDefinition targetDef = null;
            bool hasDef = package != null && package.TryGetTarget(targetId, out targetDef);
            if (hasDef && targetDef != null && !string.IsNullOrWhiteSpace(targetDef.anchorRef))
            {
                Transform liveAnchor = TryGetLivePartTransform(targetDef.anchorRef);
                Transform pr = _ctx.Spawner.PreviewRoot;
                if (liveAnchor != null && pr != null)
                {
                    // Position tracks the live anchor part. Rotation + scale
                    // stay from the static targetPlacement — the authored
                    // rotation describes the tool's approach direction
                    // (e.g. drill perpendicular to bolt head, set via
                    // useToolActionRotation/toolActionRotation), independent
                    // of how the part itself is currently oriented. Mixing
                    // them would spin the tool 90° off whenever the part's
                    // intrinsic rotation differs from the action approach.
                    position = pr.InverseTransformPoint(liveAnchor.position);
                    var anchorPlacement = _ctx.Spawner.FindTargetPlacement(targetId);
                    if (anchorPlacement != null)
                    {
                        rotation = !anchorPlacement.rotation.IsIdentity
                            ? new Quaternion(anchorPlacement.rotation.x, anchorPlacement.rotation.y, anchorPlacement.rotation.z, anchorPlacement.rotation.w)
                            : Quaternion.identity;
                        scale = new Vector3(anchorPlacement.scale.x, anchorPlacement.scale.y, anchorPlacement.scale.z);
                    }
                    return true;
                }
            }

            TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(targetId);
            if (targetPlacement != null)
            {
                position = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                rotation = !targetPlacement.rotation.IsIdentity
                    ? new Quaternion(targetPlacement.rotation.x, targetPlacement.rotation.y, targetPlacement.rotation.z, targetPlacement.rotation.w)
                    : Quaternion.identity;
                scale = new Vector3(targetPlacement.scale.x, targetPlacement.scale.y, targetPlacement.scale.z);
                return true;
            }

            if (hasDef && targetDef != null && !string.IsNullOrWhiteSpace(targetDef.associatedPartId))
            {
                PartPreviewPlacement partPlacement = _ctx.Spawner.FindPartPlacement(targetDef.associatedPartId);
                if (partPlacement != null)
                {
                    position = new Vector3(partPlacement.assembledPosition.x, partPlacement.assembledPosition.y, partPlacement.assembledPosition.z);
                    rotation = !partPlacement.assembledRotation.IsIdentity
                        ? new Quaternion(partPlacement.assembledRotation.x, partPlacement.assembledRotation.y, partPlacement.assembledRotation.z, partPlacement.assembledRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(partPlacement.assembledScale.x, partPlacement.assembledScale.y, partPlacement.assembledScale.z);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Looks up the spawned part GO whose name matches the given partId
        /// (the spawner names each spawned mesh after its part definition id)
        /// and returns its Transform. Used by anchor-resolved target pose
        /// resolution so the marker tracks the part's CURRENT pose at the
        /// active step instead of a stale bake.
        /// </summary>
        private Transform TryGetLivePartTransform(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return null;
            var spawned = _ctx.Spawner.SpawnedParts;
            if (spawned == null) return null;
            for (int i = 0; i < spawned.Count; i++)
            {
                var go = spawned[i];
                if (go == null) continue;
                if (string.Equals(go.name, partId, System.StringComparison.Ordinal))
                    return go.transform;
            }
            return null;
        }

        public bool TryGetPreviewTargetPose(
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _preview.SpawnedPreviews.Count == 0)
                return false;

            PreviewSceneSetup setup = _ctx.Setup;
            Transform previewRoot = setup != null ? setup.PreviewRoot : null;

            for (int i = _preview.SpawnedPreviews.Count - 1; i >= 0; i--)
            {
                GameObject preview = _preview.SpawnedPreviews[i];
                if (preview == null) continue;

                var info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = preview.transform;
                if (previewRoot != null)
                {
                    position = previewRoot.InverseTransformPoint(tx.position);
                    rotation = Quaternion.Inverse(previewRoot.rotation) * tx.rotation;
                }
                else
                {
                    position = tx.position;
                    rotation = tx.rotation;
                }

                scale = tx.localScale;
                return true;
            }

            return false;
        }

        public Vector3 ResolveMarkerWorldPos(Vector3 localPos, Vector3 scale)
        {
            // No lift — marker sits exactly at the authored target point.
            // ZTest=Always (OSE/ToolTargetMarker.shader) renders the marker
            // on top of the surface even when they're co-planar.
            PreviewSceneSetup setup = _ctx.Setup;
            return setup?.PreviewRoot != null
                ? setup.PreviewRoot.TransformPoint(localPos)
                : localPos;
        }

        // Tool-target markers are aiming dots (bolt heads, screw heads, weld/clamp
        // points), not part-sized blobs. When a target has no own scale, the
        // caller's source is the associated part's scale — which for "unit"
        // parts (e.g. d3d_x_axis_idler_unit at 1×1×1) would produce a 40cm
        // sphere that occludes the workpiece. Cap at 3cm — large enough to
        // see, small enough to never block the part being worked on.
        public const float MaxToolTargetMarkerExtent = 0.03f;
        public const float MinToolTargetMarkerExtent = 0.008f;

        public static Vector3 ResolveToolTargetMarkerScale(Vector3 sourceScale)
        {
            float dominant = Mathf.Max(sourceScale.x, Mathf.Max(sourceScale.y, sourceScale.z));
            float uniform = Mathf.Clamp(dominant, MinToolTargetMarkerExtent, MaxToolTargetMarkerExtent);
            return Vector3.one * uniform;
        }

        // ====================================================================
        //  Static helpers
        // ====================================================================

        public static bool StepDefinesToolAction(StepDefinition step, string toolId, string targetId)
        {
            if (step?.requiredToolActions == null ||
                string.IsNullOrWhiteSpace(toolId) ||
                string.IsNullOrWhiteSpace(targetId))
            {
                return false;
            }

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null)
                    continue;

                if (!string.Equals(action.toolId?.Trim(), toolId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(action.targetId?.Trim(), targetId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                return true;
            }

            return false;
        }

        public static bool TryGetActionSnapshots(
            out ToolActionSnapshot[] snapshots,
            out IMachineSessionController session)
        {
            snapshots = Array.Empty<ToolActionSnapshot>();
            session = null;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out session) ||
                session == null ||
                session.Package == null ||
                session.ToolController == null)
            {
                return false;
            }

            return session.ToolController.TryGetActionSnapshots(out snapshots);
        }

        public static bool ActiveStepHasRequiredToolActions()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session) || session == null)
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            return step?.requiredToolActions != null && step.requiredToolActions.Length > 0;
        }

        // ====================================================================
        //  Private
        // ====================================================================

        private bool TrySpawnFallbackStepToolActionTargets(
            StepDefinition step,
            MachinePackageDefinition package,
            PreviewSceneSetup setup)
        {
            if (step?.requiredToolActions == null || package == null || setup == null)
                return false;

            bool spawnedAny = false;
            HashSet<string> actionable = GetActionableTargetIdSet(step);

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.toolId) || string.IsNullOrWhiteSpace(action.targetId))
                    continue;

                // Race guard — see Refresh's primary path.
                if (_hiddenForCurrentStep.Contains(action.targetId))
                    continue;

                if (actionable != null && !actionable.Contains(action.targetId))
                    continue;

                if (TrySpawnToolActionTargetMarker(package, setup, action.toolId.Trim(), action.targetId.Trim()))
                    spawnedAny = true;
            }

            return spawnedAny;
        }

        private bool TrySpawnToolActionTargetMarker(
            MachinePackageDefinition package,
            PreviewSceneSetup setup,
            string requiredToolId,
            string targetId)
        {
            if (package == null || setup == null || string.IsNullOrWhiteSpace(requiredToolId) || string.IsNullOrWhiteSpace(targetId))
                return false;

            if (!TryResolveToolActionTargetPose(package, targetId, out Vector3 markerPos, out Quaternion markerRot, out Vector3 markerScale))
            {
                OseLog.Warn($"[ToolTargetSpawner] Could not resolve tool target pose for '{targetId}'.");
                return false;
            }

            if (TryGetPreviewTargetPose(targetId, out Vector3 previewPos, out Quaternion previewRot, out Vector3 previewScale))
            {
                markerPos = previewPos;
                markerRot = previewRot;
                markerScale = previewScale;
            }

            Transform previewRoot = setup.PreviewRoot;
            GameObject marker = _markerPool.Get();
            if (previewRoot != null)
                marker.transform.SetParent(previewRoot, false);

            marker.name = $"ToolTarget_{requiredToolId}_{targetId}";
            marker.transform.SetLocalPositionAndRotation(markerPos, markerRot);
            marker.transform.localScale = ResolveToolTargetMarkerScale(markerScale);

            Vector3 surfaceLocalPos = marker.transform.localPosition;
            Vector3 surfaceWorldPos = marker.transform.position;

            // No lift — marker sits exactly at the authored target position.
            // Previously +0.75 × markerScale.y was added to avoid z-fighting
            // with the surface; ZTest=Always in OSE/ToolTargetMarker.shader
            // makes the marker render on top regardless, so the lift was
            // pure visual offset that the user (correctly) saw as wrong.

            PackagePartSpawner.EnsureColliders(marker);
            SphereCollider toolCol = marker.GetComponent<SphereCollider>();
            if (toolCol != null)
                toolCol.radius = ToolTargetColliderRadius;
            MaterialHelper.ApplyToolTargetMarker(marker, ToolTargetAnimator.ToolTargetIdleColor);

            ToolActionTargetInfo info = marker.GetComponent<ToolActionTargetInfo>();
            info.TargetId = targetId;
            info.RequiredToolId = requiredToolId;
            info.BaseScale = marker.transform.localScale;
            info.BaseLocalPosition = marker.transform.localPosition;
            info.SurfaceLocalPosition = surfaceLocalPos;
            info.SurfaceWorldPos = surfaceWorldPos;
            info.MarkerLift = 0f;
            info.TargetWorldRotation = marker.transform.rotation;

            if (package.TryGetTarget(targetId, out TargetDefinition targetDef))
            {
                Vector3 axis = targetDef.GetWeldAxisVector();
                if (axis.sqrMagnitude > 0.001f && previewRoot != null)
                    info.WeldAxis = previewRoot.TransformDirection(axis);
                else
                    info.WeldAxis = axis;
                info.WeldLength = targetDef.weldLength;

                if (targetDef.useToolActionRotation)
                {
                    info.HasToolActionRotation = true;
                    info.ToolActionRotation = targetDef.GetToolActionRotation();
                }
            }

            _spawnedToolActionTargets.Add(marker);

            OseLog.VerboseInfo(
                $"[ToolTargetSpawner] Spawned tool target marker '{marker.name}' for target '{info.TargetId}' at local {info.BaseLocalPosition} / world {marker.transform.position}.");
            return true;
        }

        private static void TryWarnMissingPrimaryToolActionSnapshot()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session) || session == null)
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return;

            OseLog.Warn($"[ToolTargetSpawner] Active step '{step.id}' has required tool actions, but no tool action snapshots were available.");
        }

        // ====================================================================
        //  Pool callbacks
        // ====================================================================

        private static GameObject CreateMarkerInstance()
        {
            // Solid sphere primitive. Reliable from any camera angle —
            // no billboard, no edge-on degradation, no procedural-mesh
            // surface-orientation issues. The sphere is intentionally
            // small (ToolTargetAnimator scales it down at close camera
            // distance) and semi-transparent (alpha fade modulates per
            // distance), so the workpiece underneath stays visible at
            // the click range.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ToolTargetMarker";
            go.AddComponent<ToolActionTargetInfo>();
            go.SetActive(false);
            return go;
        }

        private static void ReleaseMarkerToPool(GameObject go)
        {
            go.transform.SetParent(null, false);

            ToolActionTargetInfo info = go.GetComponent<ToolActionTargetInfo>();
            if (info != null)
            {
                info.TargetId = null;
                info.RequiredToolId = null;
                info.BaseScale = Vector3.one;
                info.BaseLocalPosition = Vector3.zero;
                info.SurfaceLocalPosition = Vector3.zero;
                info.SurfaceWorldPos = Vector3.zero;
                info.MarkerLift = 0f;
                info.TargetWorldRotation = Quaternion.identity;
                info.WeldAxis = Vector3.zero;
                info.WeldLength = 0f;
                info.HasToolActionRotation = false;
                info.ToolActionRotation = Quaternion.identity;
            }

            go.SetActive(false);
        }
    }
}
