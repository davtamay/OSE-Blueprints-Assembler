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

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var earlySession))
                return;

            StepController earlyStepCtrl = earlySession?.AssemblyController?.StepController;
            if (earlyStepCtrl == null || !earlyStepCtrl.HasActiveStep)
                return;

            StepDefinition currentStep = earlyStepCtrl.CurrentStepDefinition;

            if (currentStep?.requiredToolActions == null || currentStep.requiredToolActions.Length == 0)
                return;

            MachineSessionController session = earlySession;
            bool spawnedAny = false;

            bool runtimeMatchesStep =
                session?.ToolController != null &&
                string.Equals(session.ToolController.ActiveStepId, currentStep.id, StringComparison.OrdinalIgnoreCase);

            if (runtimeMatchesStep &&
                TryGetActionSnapshots(out ToolRuntimeController.ToolActionSnapshot[] actionSnapshots, out session))
            {
                string sequentialTargetId = ResolveSequentialTargetId(currentStep, actionSnapshots);

                for (int i = 0; i < actionSnapshots.Length; i++)
                {
                    ToolRuntimeController.ToolActionSnapshot action = actionSnapshots[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    if (!StepDefinesToolAction(currentStep, action.ToolId, action.TargetId))
                        continue;

                    if (!string.IsNullOrWhiteSpace(sequentialTargetId) &&
                        !string.Equals(action.TargetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

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
            float lift = Mathf.Max(scale.y * 0.75f, guide.transform.localScale.y * 0.6f);
            guide.transform.position += Vector3.up * lift;

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
        //  Sequential target resolution
        // ====================================================================

        public string ResolveSequentialTargetId(
            StepDefinition currentStep,
            ToolRuntimeController.ToolActionSnapshot[] actionSnapshots = null)
        {
            if (currentStep == null || !currentStep.IsSequential)
                return null;

            string sequentialTargetId = _preview.PreviewManager?.GetCurrentSequentialTargetId();
            if (!string.IsNullOrWhiteSpace(sequentialTargetId))
                return sequentialTargetId;

            if (actionSnapshots != null)
            {
                for (int i = 0; i < actionSnapshots.Length; i++)
                {
                    ToolRuntimeController.ToolActionSnapshot action = actionSnapshots[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    if (!StepDefinesToolAction(currentStep, action.ToolId, action.TargetId))
                        continue;

                    return action.TargetId.Trim();
                }
            }

            ToolActionDefinition[] requiredActions = currentStep.requiredToolActions;
            if (requiredActions == null)
                return null;

            for (int i = 0; i < requiredActions.Length; i++)
            {
                ToolActionDefinition action = requiredActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                    continue;

                return action.targetId.Trim();
            }

            return null;
        }

        public string NormalizeSequentialExecutionTargetId(StepDefinition currentStep, string interactedTargetId)
        {
            if (string.IsNullOrWhiteSpace(interactedTargetId) || currentStep == null || !currentStep.IsSequential)
                return interactedTargetId;

            string sequentialTargetId = null;
            if (TryGetActionSnapshots(out ToolRuntimeController.ToolActionSnapshot[] actionSnapshots, out _))
                sequentialTargetId = ResolveSequentialTargetId(currentStep, actionSnapshots);

            if (string.IsNullOrWhiteSpace(sequentialTargetId))
                sequentialTargetId = ResolveSequentialTargetId(currentStep);

            if (string.IsNullOrWhiteSpace(sequentialTargetId) ||
                string.Equals(interactedTargetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
            {
                return interactedTargetId;
            }

            return sequentialTargetId;
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

            if (package != null &&
                package.TryGetTarget(targetId, out TargetDefinition targetDef) &&
                !string.IsNullOrWhiteSpace(targetDef.associatedPartId))
            {
                PartPreviewPlacement partPlacement = _ctx.Spawner.FindPartPlacement(targetDef.associatedPartId);
                if (partPlacement != null)
                {
                    position = new Vector3(partPlacement.playPosition.x, partPlacement.playPosition.y, partPlacement.playPosition.z);
                    rotation = !partPlacement.playRotation.IsIdentity
                        ? new Quaternion(partPlacement.playRotation.x, partPlacement.playRotation.y, partPlacement.playRotation.z, partPlacement.playRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(partPlacement.playScale.x, partPlacement.playScale.y, partPlacement.playScale.z);
                    return true;
                }
            }

            return false;
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
            float lift = Mathf.Max(scale.y * 0.75f, ResolveToolTargetMarkerScale(scale).y * 0.6f);
            PreviewSceneSetup setup = _ctx.Setup;
            if (setup?.PreviewRoot != null)
                return setup.PreviewRoot.TransformPoint(localPos) + Vector3.up * lift;
            return localPos + Vector3.up * lift;
        }

        public static Vector3 ResolveToolTargetMarkerScale(Vector3 sourceScale)
        {
            float dominant = Mathf.Max(sourceScale.x, Mathf.Max(sourceScale.y, sourceScale.z));
            float uniform = Mathf.Clamp(dominant * 0.55f, 0.15f, 0.40f);
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
            out ToolRuntimeController.ToolActionSnapshot[] snapshots,
            out MachineSessionController session)
        {
            snapshots = Array.Empty<ToolRuntimeController.ToolActionSnapshot>();
            session = null;

            if (!ServiceRegistry.TryGet(out session) ||
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
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session == null)
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
            string sequentialTargetId = ResolveSequentialTargetId(step);

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.toolId) || string.IsNullOrWhiteSpace(action.targetId))
                    continue;

                if (!string.IsNullOrWhiteSpace(sequentialTargetId) &&
                    !string.Equals(action.targetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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

            Vector3 surfaceWorldPos = marker.transform.position;

            float markerLift = Mathf.Max(markerScale.y * 0.75f, marker.transform.localScale.y * 0.6f);
            marker.transform.position += Vector3.up * markerLift;

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
            info.SurfaceWorldPos = surfaceWorldPos;
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
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session == null)
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
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
                info.SurfaceWorldPos = Vector3.zero;
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
