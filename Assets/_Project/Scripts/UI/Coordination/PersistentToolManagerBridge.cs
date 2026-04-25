using System;
using System.Collections;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages persistent tool instances (clamps, fixtures) that survive step
    /// transitions. Extracted from PartInteractionBridge to keep that class
    /// focused on input routing and step orchestration.
    /// </summary>
    internal sealed class PersistentToolManagerBridge : IPersistentToolManager
    {
        private readonly List<PersistentToolInstance> _tools = new();
        private Transform _container;

        private readonly Func<GameObject> _getToolPreview;
        private readonly Func<GameObject> _detachPreview;
        private readonly Func<Transform> _getPreviewRoot;
        private readonly Action _refreshPreview;
        private readonly Func<PackagePartSpawner> _getSpawner;

        // Navigation-sync generation counter — bumped on every new sync.
        // In-flight coroutines compare against this before applying their
        // results, so a newer nav supersedes an older one without threads.
        private int _syncGeneration;

        public PersistentToolManagerBridge(
            Func<GameObject> getToolPreview,
            Func<GameObject> detachPreview,
            Func<Transform> getPreviewRoot,
            Action refreshPreview,
            Func<PackagePartSpawner> getSpawner = null)
        {
            _getToolPreview = getToolPreview;
            _detachPreview = detachPreview;
            _getPreviewRoot = getPreviewRoot;
            _refreshPreview = refreshPreview;
            _getSpawner = getSpawner;
        }

        public GameObject SpawnPersistentTool(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
        {
            GameObject preview = _getToolPreview();
            if (preview == null)
            {
                OseLog.Warn($"[PersistentTool] Cannot spawn — no tool preview for '{toolId}'.");
                return null;
            }

            GameObject clone = Object.Instantiate(preview);
            clone.name = $"PersistentTool_{toolId}_{targetId}";
            clone.transform.SetPositionAndRotation(worldPos, rotation);
            clone.transform.SetParent(GetContainer(), worldPositionStays: true);

            foreach (var col in clone.GetComponentsInChildren<Collider>())
                Object.Destroy(col);

            if (!MaterialHelper.RestoreOriginals(clone))
                MaterialHelper.RestoreOpaque(clone);

            var info = clone.AddComponent<PersistentToolInstance>();
            info.ToolId = toolId;
            info.TargetId = targetId;

            _tools.Add(info);
            OseLog.Info($"[PersistentTool] Spawned '{clone.name}' at {worldPos}. Total persistent: {_tools.Count}");
            return clone;
        }

        public GameObject ConvertPreviewToPersistent(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
        {
            GameObject preview = _detachPreview();
            if (preview == null)
            {
                OseLog.Warn($"[PersistentTool] ConvertPreview — no preview to detach for '{toolId}'.");
                return null;
            }

            preview.name = $"PersistentTool_{toolId}_{targetId}";
            preview.transform.SetParent(null, worldPositionStays: true);
            preview.transform.SetPositionAndRotation(worldPos, rotation);
            preview.transform.SetParent(GetContainer(), worldPositionStays: true);

            foreach (var col in preview.GetComponentsInChildren<Collider>())
                Object.Destroy(col);

            if (!MaterialHelper.RestoreOriginals(preview))
                MaterialHelper.RestoreOpaque(preview);

            preview.SetActive(true);

            var info = preview.AddComponent<PersistentToolInstance>();
            info.ToolId = toolId;
            info.TargetId = targetId;

            _tools.Add(info);
            OseLog.Info($"[PersistentTool] Converted preview → persistent '{preview.name}' at {worldPos}. Total: {_tools.Count}");

            _refreshPreview();
            return preview;
        }

        public bool RemovePersistentTool(string targetId)
        {
            for (int i = _tools.Count - 1; i >= 0; i--)
            {
                var inst = _tools[i];
                if (inst == null) { _tools.RemoveAt(i); continue; }
                if (string.Equals(inst.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    OseLog.Info($"[PersistentTool] Removing '{inst.gameObject.name}' from target '{targetId}'.");
                    _tools.RemoveAt(i);
                    Object.Destroy(inst.gameObject);
                    return true;
                }
            }
            return false;
        }

        public int RemoveAllPersistentTools(string toolId = null)
        {
            int removed = 0;
            for (int i = _tools.Count - 1; i >= 0; i--)
            {
                var inst = _tools[i];
                if (inst == null) { _tools.RemoveAt(i); continue; }
                if (toolId == null || string.Equals(inst.ToolId, toolId, StringComparison.OrdinalIgnoreCase))
                {
                    Object.Destroy(inst.gameObject);
                    _tools.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
                OseLog.Info($"[PersistentTool] Removed {removed} persistent tool(s) (filter='{toolId ?? "all"}').");
            return removed;
        }

        public bool HasPersistentToolAt(string targetId)
        {
            for (int i = 0; i < _tools.Count; i++)
                if (_tools[i] != null && string.Equals(_tools[i].TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public int GetPersistentToolCount(string toolId = null)
        {
            if (toolId == null) return _tools.Count;
            int count = 0;
            for (int i = 0; i < _tools.Count; i++)
                if (_tools[i] != null && string.Equals(_tools[i].ToolId, toolId, StringComparison.OrdinalIgnoreCase))
                    count++;
            return count;
        }

        public string[] GetPlacedPersistentToolIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _tools.Count; i++)
                if (_tools[i] != null && !string.IsNullOrEmpty(_tools[i].ToolId))
                    ids.Add(_tools[i].ToolId);
            var result = new string[ids.Count];
            ids.CopyTo(result);
            return result;
        }

        private Transform GetContainer()
        {
            if (_container != null)
                return _container;

            var go = new GameObject("__PersistentTools__");
            Transform previewRoot = _getPreviewRoot();
            if (previewRoot != null)
                go.transform.SetParent(previewRoot, false);

            _container = go.transform;
            return _container;
        }

        // ════════════════════════════════════════════════════════════════════
        // Navigation-time synthesis (coroutine-based, no threads)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reconciles placed persistent tools against what SHOULD exist given
        /// the player has completed <paramref name="completedSteps"/>. Handles
        /// forward-skip (spawn clamps the user skipped) and backward-nav
        /// (remove clamps placed in steps we're now before). Returns an
        /// IEnumerator so PartInteractionBridge can drive it via
        /// <see cref="MonoBehaviour.StartCoroutine"/> on the main thread —
        /// no tasks, no threads. Each new call bumps _syncGeneration so any
        /// in-flight coroutine bails before mutating state.
        /// </summary>
        public IEnumerator SyncForCompletedSteps(StepDefinition[] completedSteps, MachinePackageDefinition package)
        {
            int myGeneration = ++_syncGeneration;
            if (package == null) yield break;

            // 1. Compute desired (toolId, targetId) set by replaying step history.
            var desired = BuildDesiredSet(completedSteps, package);

            if (myGeneration != _syncGeneration) yield break;

            // 2. Remove currently-placed tools not in desired set (backward nav).
            for (int i = _tools.Count - 1; i >= 0; i--)
            {
                var inst = _tools[i];
                if (inst == null) { _tools.RemoveAt(i); continue; }
                bool keep = desired.TryGetValue(inst.TargetId, out var toolId)
                            && string.Equals(toolId, inst.ToolId, StringComparison.OrdinalIgnoreCase);
                if (keep) continue;

                OseLog.Info($"[PersistentTool] SyncForNavigation removing '{inst.gameObject.name}' (not in desired set).");
                _tools.RemoveAt(i);
                Object.Destroy(inst.gameObject);
            }

            if (myGeneration != _syncGeneration) yield break;

            // 3. Spawn desired tools not yet placed (forward-skip synthesis).
            foreach (var kv in desired)
            {
                if (myGeneration != _syncGeneration) yield break;
                string targetId = kv.Key;
                string toolId = kv.Value;
                if (HasPersistentToolAt(targetId)) continue;

                yield return SpawnFromAssetCoroutine(toolId, targetId, package, myGeneration);
            }
        }

        private static Dictionary<string, string> BuildDesiredSet(StepDefinition[] completedSteps, MachinePackageDefinition package)
        {
            var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // targetId -> toolId
            if (completedSteps == null) return desired;

            for (int i = 0; i < completedSteps.Length; i++)
            {
                StepDefinition step = completedSteps[i];
                if (step == null) continue;

                if (step.requiredToolActions != null)
                {
                    for (int a = 0; a < step.requiredToolActions.Length; a++)
                    {
                        ToolActionDefinition action = step.requiredToolActions[a];
                        if (action == null) continue;
                        if (string.IsNullOrWhiteSpace(action.toolId) || string.IsNullOrWhiteSpace(action.targetId)) continue;
                        if (!package.TryGetTool(action.toolId, out var toolDef) || !toolDef.persistent) continue;
                        desired[action.targetId] = action.toolId;
                    }
                }

                if (step.removePersistentToolIds != null)
                {
                    for (int r = 0; r < step.removePersistentToolIds.Length; r++)
                    {
                        string removeId = step.removePersistentToolIds[r];
                        if (string.IsNullOrEmpty(removeId)) continue;
                        var doomed = new List<string>();
                        foreach (var kv in desired)
                            if (string.Equals(kv.Value, removeId, StringComparison.OrdinalIgnoreCase))
                                doomed.Add(kv.Key);
                        for (int d = 0; d < doomed.Count; d++) desired.Remove(doomed[d]);
                    }
                }
            }
            return desired;
        }

        private IEnumerator SpawnFromAssetCoroutine(
            string toolId,
            string targetId,
            MachinePackageDefinition package,
            int myGeneration)
        {
            if (!package.TryGetTool(toolId, out var toolDef) || toolDef == null)
            {
                OseLog.Warn($"[PersistentTool] SyncForNavigation: tool '{toolId}' not in package.");
                yield break;
            }
            if (string.IsNullOrWhiteSpace(toolDef.assetRef))
            {
                OseLog.Warn($"[PersistentTool] SyncForNavigation: tool '{toolId}' has no assetRef.");
                yield break;
            }
            if (!package.TryGetTarget(targetId, out var targetDef) || targetDef == null)
            {
                OseLog.Warn($"[PersistentTool] SyncForNavigation: target '{targetId}' not in package.");
                yield break;
            }

            PackagePartSpawner spawner = _getSpawner?.Invoke();
            if (spawner == null)
            {
                OseLog.Warn("[PersistentTool] SyncForNavigation: spawner unavailable.");
                yield break;
            }

            if (!TryResolveTargetWorldPose(spawner, package, targetDef, out Vector3 worldPos, out Quaternion worldRot, out bool applyGripCorrection))
            {
                OseLog.Warn($"[PersistentTool] SyncForNavigation: could not resolve world pose for target '{targetId}'.");
                yield break;
            }

            string toolPath = toolDef.assetRef.Contains("/")
                ? toolDef.assetRef
                : "assets/tools/" + toolDef.assetRef;

            // LoadPackageAssetAsync returns a Task internally but does not
            // spawn a thread — Unity WebGL's Task backing is main-thread
            // coroutine-driven. We yield until IsCompleted, no threading
            // primitives used.
            var task = spawner.LoadPackageAssetAsync(toolPath, GetContainer());
            yield return new WaitUntil(() => task.IsCompleted);

            GameObject clone = task.IsCompletedSuccessfully ? task.Result : null;

            if (myGeneration != _syncGeneration)
            {
                if (clone != null) Object.Destroy(clone);
                yield break;
            }
            if (clone == null)
            {
                OseLog.Warn($"[PersistentTool] SyncForNavigation: failed to load asset '{toolPath}'.");
                yield break;
            }

            // Re-check — another sync pass or a live click may have placed
            // the same tool while we awaited the load.
            if (HasPersistentToolAt(targetId))
            {
                Object.Destroy(clone);
                yield break;
            }

            clone.name = $"PersistentTool_{toolId}_{targetId}";

            // Apply the same grip-rotation / tip-point corrections
            // ToolActionCoordinator does on normal completion (see the
            // preview-done callback): the tool's grip must align with the
            // authored toolActionRotation and the tip must land on the
            // target surface. Without these offsets, the GO's ORIGIN lands
            // at the surface instead of the tip, and the grip points the
            // wrong way — visible as a floating clamp rotated wrong.
            float cursorScale = ToolCursorManager.CursorUniformScale *
                                (toolDef.scaleOverride > 0f ? toolDef.scaleOverride : 1f);
            Quaternion placementRot = worldRot;
            Vector3 placementPos = worldPos;
            if (toolDef.HasToolPose)
            {
                // Grip-rotation correction only applies in legacy (Euler)
                // rotation format. Mesh-format rotations are the target's
                // actual world rotation and already include the tool's grip
                // orientation — applying the inverse would rotate twice.
                if (applyGripCorrection && toolDef.toolPose.HasGripRotation)
                    placementRot = worldRot * Quaternion.Inverse(toolDef.toolPose.GetGripRotation());
                if (toolDef.toolPose.HasTipPoint)
                    placementPos = worldPos - placementRot * (toolDef.toolPose.GetTipPoint() * cursorScale);
            }

            // Detach → set local-as-world (scale + pose) → reparent with
            // worldPositionStays so the tool ends up at the computed world
            // transform regardless of PreviewRoot / container scale.
            clone.transform.SetParent(null, worldPositionStays: false);
            clone.transform.localScale = Vector3.one * cursorScale;
            clone.transform.SetPositionAndRotation(placementPos, placementRot);
            clone.transform.SetParent(GetContainer(), worldPositionStays: true);

            foreach (var col in clone.GetComponentsInChildren<Collider>())
                Object.Destroy(col);

            if (!MaterialHelper.RestoreOriginals(clone))
                MaterialHelper.RestoreOpaque(clone);

            var info = clone.AddComponent<PersistentToolInstance>();
            info.ToolId = toolId;
            info.TargetId = targetId;
            _tools.Add(info);

            OseLog.Info($"[PersistentTool] SyncForNavigation spawned '{clone.name}' at {worldPos}. Total: {_tools.Count}");
        }

        private bool TryResolveTargetWorldPose(
            PackagePartSpawner spawner,
            MachinePackageDefinition package,
            TargetDefinition targetDef,
            out Vector3 worldPos,
            out Quaternion worldRot,
            out bool applyGripCorrection)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            applyGripCorrection = false;
            Transform previewRoot = _getPreviewRoot();
            if (previewRoot == null) return false;

            // Rotation source mirrors ToolActionCoordinator's placement-rot
            // logic:
            //   - mesh format (previewConfig.targetRotationFormat=="mesh"):
            //     use TargetWorldRotation = previewRoot.rotation * tp.rotation
            //   - legacy + useToolActionRotation: targetDef.GetToolActionRotation()
            //     (world-space Euler)
            //   - else: previewRoot.rotation * tp.rotation (placement-local)
            bool isMeshFormat = package.previewConfig != null
                && string.Equals(package.previewConfig.targetRotationFormat, "mesh", StringComparison.OrdinalIgnoreCase);

            // Primary: preview_config.json TargetPreviewPlacement.
            TargetPreviewPlacement tp = spawner.FindTargetPlacement(targetDef.id);
            if (tp != null)
            {
                Vector3 localPos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                worldPos = previewRoot.TransformPoint(localPos);

                Quaternion localTpRot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;

                if (isMeshFormat && targetDef.useToolActionRotation)
                {
                    worldRot = previewRoot.rotation * localTpRot;
                    applyGripCorrection = false;
                }
                else if (targetDef.useToolActionRotation)
                {
                    worldRot = targetDef.GetToolActionRotation();
                    applyGripCorrection = true;
                }
                else
                {
                    worldRot = previewRoot.rotation * localTpRot;
                    applyGripCorrection = false;
                }
                return true;
            }

            // Fallback: associatedPartId's assembled placement.
            if (!string.IsNullOrWhiteSpace(targetDef.associatedPartId))
            {
                PartPreviewPlacement pp = spawner.FindPartPlacement(targetDef.associatedPartId);
                if (pp != null)
                {
                    Vector3 localPos = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                    worldPos = previewRoot.TransformPoint(localPos);
                    Quaternion localPpRot = !pp.assembledRotation.IsIdentity
                        ? new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w)
                        : Quaternion.identity;

                    if (isMeshFormat && targetDef.useToolActionRotation)
                    {
                        worldRot = previewRoot.rotation * localPpRot;
                        applyGripCorrection = false;
                    }
                    else if (targetDef.useToolActionRotation)
                    {
                        worldRot = targetDef.GetToolActionRotation();
                        applyGripCorrection = true;
                    }
                    else
                    {
                        worldRot = previewRoot.rotation * localPpRot;
                        applyGripCorrection = false;
                    }
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Marker component for persistent tool instances in the scene.</summary>
    internal sealed class PersistentToolInstance : MonoBehaviour
    {
        public string ToolId;
        public string TargetId;
    }
}
