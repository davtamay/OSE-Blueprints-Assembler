using System;
using System.Collections.Generic;
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

        public PersistentToolManagerBridge(
            Func<GameObject> getToolPreview,
            Func<GameObject> detachPreview,
            Func<Transform> getPreviewRoot,
            Action refreshPreview)
        {
            _getToolPreview = getToolPreview;
            _detachPreview = detachPreview;
            _getPreviewRoot = getPreviewRoot;
            _refreshPreview = refreshPreview;
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
    }

    /// <summary>Marker component for persistent tool instances in the scene.</summary>
    internal sealed class PersistentToolInstance : MonoBehaviour
    {
        public string ToolId;
        public string TargetId;
    }
}
