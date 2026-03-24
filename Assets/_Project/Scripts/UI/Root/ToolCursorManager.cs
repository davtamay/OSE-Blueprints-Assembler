using System;
using System.Threading;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages the tool-cursor ghost and pipe-cursor ghost that follow the pointer
    /// on screen during tool-mode and pipe-connection steps respectively.
    /// Extracted from PartInteractionBridge to keep cursor concerns in one place.
    /// </summary>
    internal sealed class ToolCursorManager
    {
        // ── Constants ──────────────────────────────────────────────────────────────
        public const float CursorUniformScale      = 0.16f;
        public const float CursorRayDistance       = 0.75f;
        public const float PipeCursorRayDistance   = 0.35f;  // closer than tool ghost
        public const float CursorVerticalOffset    = 0.15f;
        public const float ScreenProximityReadyPx  = 150f;   // screen pixels — cursor changes colour

        private static readonly Color ReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.65f);

        // ── State ──────────────────────────────────────────────────────────────────
        private readonly Transform _fallbackParent;
        private GameObject _toolGhostIndicator;
        private Quaternion _toolGhostUpCorrection = Quaternion.identity;
        private Material[][] _toolGhostOriginalMaterials;
        private int _refreshGeneration;
        private GameObject _pipeCursorGhost;
        private bool _cursorInReadyState;
        private bool _positionUpdateSuspended;

        // ── Public interface ───────────────────────────────────────────────────────
        public GameObject ToolGhost       => _toolGhostIndicator;
        public bool CursorInReadyState    => _cursorInReadyState;

        /// <summary>
        /// When true, UpdatePosition() skips repositioning the tool ghost.
        /// Used by the preview system while the ghost is detached from the camera.
        /// </summary>
        public bool PositionUpdateSuspended
        {
            get => _positionUpdateSuspended;
            set => _positionUpdateSuspended = value;
        }

        public ToolCursorManager(Transform fallbackParent)
        {
            _fallbackParent = fallbackParent;
        }

        /// <summary>Destroys the tool ghost, honouring the hint-ghost coupling.</summary>
        public void Clear(bool toolGhostIsHintGhost, Action clearHintCallback)
        {
            if (_toolGhostIndicator == null)
                return;

            if (toolGhostIsHintGhost)
                clearHintCallback?.Invoke();

            UnityEngine.Object.Destroy(_toolGhostIndicator);
            _toolGhostIndicator = null;
            _toolGhostUpCorrection = Quaternion.identity;
            _cursorInReadyState = false;
            _positionUpdateSuspended = false;
            _toolGhostOriginalMaterials = null;
        }

        /// <summary>
        /// Releases the tool ghost without destroying it, so the caller can
        /// repurpose it (e.g. convert it into a persistent tool).
        /// Returns the detached GameObject, or null if there was no ghost.
        /// After this call, <see cref="ToolGhost"/> is null and a new
        /// <see cref="RefreshAsync"/> call will create a fresh cursor ghost.
        /// </summary>
        public GameObject DetachGhost()
        {
            GameObject ghost = _toolGhostIndicator;
            if (ghost == null) return null;

            _toolGhostIndicator = null;
            _toolGhostUpCorrection = Quaternion.identity;
            _cursorInReadyState = false;
            _positionUpdateSuspended = false;
            _toolGhostOriginalMaterials = null;
            return ghost;
        }

        /// <summary>Loads the active tool model, parents it to the camera and configures it as the cursor ghost.</summary>
        public async Task RefreshAsync(PackagePartSpawner spawner, PreviewSceneSetup setup,
                            bool toolGhostIsHintGhost, Action clearHintCallback,
                            CancellationToken ct = default)
        {
            Clear(toolGhostIsHintGhost, clearHintCallback);

            if (!Application.isPlaying || spawner == null || setup == null)
                return;

            if (!TryGetActiveToolDefinition(out string activeToolId, out ToolDefinition tool))
                return;

            int myGeneration = ++_refreshGeneration;

            GameObject ghostTool = null;
            if (!string.IsNullOrWhiteSpace(tool.assetRef))
            {
                ghostTool = await spawner.LoadPackageAssetAsync(tool.assetRef, ct: ct);
            }

            // Another RefreshAsync call started while we were awaiting — discard our result
            if (myGeneration != _refreshGeneration)
            {
                if (ghostTool != null)
                    UnityEngine.Object.Destroy(ghostTool);
                return;
            }

            if (ghostTool == null)
                ghostTool = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            _toolGhostIndicator = ghostTool;
            _toolGhostIndicator.name = $"CursorTool_{activeToolId}";

            Camera mainCam = Camera.main;
            _toolGhostIndicator.transform.SetParent(
                mainCam != null ? mainCam.transform : _fallbackParent, false);

            float toolScale = (tool.scaleOverride > 0f)
                ? CursorUniformScale * tool.scaleOverride
                : CursorUniformScale;
            _toolGhostIndicator.transform.localScale = Vector3.one * toolScale;

            bool hasOverride = tool.HasOrientationOverride;
            OseLog.Info($"[ToolGhost] tool={activeToolId} useOrientationOverride={tool.useOrientationOverride} orientationEuler={tool.orientationEuler} hasOverride={hasOverride}");
            if (hasOverride)
            {
                _toolGhostUpCorrection = Quaternion.Euler(tool.orientationEuler);
                OseLog.Info($"[ToolGhost] Using orientationEuler override: {tool.orientationEuler} -> quat={_toolGhostUpCorrection}");
            }
            else
            {
                _toolGhostUpCorrection = ComputeUprightCorrection(_toolGhostIndicator)
                    * Quaternion.Euler(0f, 180f, 180f);
                OseLog.Info($"[ToolGhost] Using ComputeUprightCorrection fallback -> quat={_toolGhostUpCorrection}");
            }

            foreach (Collider col in _toolGhostIndicator.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(col);

            _toolGhostOriginalMaterials = MaterialHelper.MakeTransparent(_toolGhostIndicator, 0.55f);

            // Start hidden — the first real UpdatePosition call from the bridge's
            // Update loop will place it at the actual cursor and show it.
            // This prevents a one-frame flash at screen center that looks like
            // the tool is pre-placed on the workpiece.
            _toolGhostIndicator.SetActive(false);
        }

        /// <summary>
        /// Moves both cursor ghosts to track the given screen position.
        /// Call every frame from PartInteractionBridge.Update().
        /// </summary>
        public void UpdatePosition(bool isDragging, Vector2 screenPos)
        {
            if (isDragging)
            {
                if (_toolGhostIndicator != null) _toolGhostIndicator.SetActive(false);
                if (_pipeCursorGhost    != null) _pipeCursorGhost.SetActive(false);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 vp = cam.ScreenToViewportPoint(screenPos);
            float halfH = CursorRayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            float localX = (vp.x - 0.5f) * 2f * halfW;
            float localY = (vp.y - 0.5f) * 2f * halfH + CursorVerticalOffset;
            var localPos = new Vector3(localX, localY, CursorRayDistance);

            if (_toolGhostIndicator != null && !_positionUpdateSuspended)
            {
                if (!_toolGhostIndicator.activeSelf) _toolGhostIndicator.SetActive(true);
                _toolGhostIndicator.transform.localPosition = localPos;
                _toolGhostIndicator.transform.localRotation = _toolGhostUpCorrection;
            }

            if (_pipeCursorGhost != null)
            {
                float pipeHalfH = PipeCursorRayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float pipeHalfW = pipeHalfH * cam.aspect;
                var pipeLocalPos = new Vector3(
                    (vp.x - 0.5f) * 2f * pipeHalfW,
                    (vp.y - 0.5f) * 2f * pipeHalfH,
                    PipeCursorRayDistance);
                if (!_pipeCursorGhost.activeSelf) _pipeCursorGhost.SetActive(true);
                _pipeCursorGhost.transform.localPosition = pipeLocalPos;
                _pipeCursorGhost.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Spawns a semi-transparent clone of the pipe part that tracks the cursor.</summary>
        public async Task SpawnPipeCursorGhostAsync(MachinePackageDefinition package, StepDefinition step,
                                         Func<string, GameObject> findSpawnedPart, PackagePartSpawner spawner,
                                         CancellationToken ct = default)
        {
            ClearPipeCursorGhost();

            string[] reqParts = step.requiredPartIds;
            if (reqParts == null || reqParts.Length == 0) return;
            string partId = reqParts[0];

            GameObject ghost = null;
            GameObject spawnedSource = findSpawnedPart?.Invoke(partId);
            if (spawnedSource != null)
            {
                ghost = UnityEngine.Object.Instantiate(spawnedSource);
            }
            else if (package.TryGetPart(partId, out PartDefinition partDef)
                     && !string.IsNullOrWhiteSpace(partDef.assetRef))
            {
                ghost = await spawner.LoadPackageAssetAsync(partDef.assetRef, ct: ct);
            }

            if (ghost == null)
                ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            _pipeCursorGhost = ghost;
            _pipeCursorGhost.name = $"CursorCable_{partId}";

            Camera mainCam = Camera.main;
            _pipeCursorGhost.transform.SetParent(
                mainCam != null ? mainCam.transform : _fallbackParent, false);

            _pipeCursorGhost.transform.localScale = Vector3.one * CursorUniformScale;

            foreach (Collider col in _pipeCursorGhost.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(col);

            MaterialHelper.MakeTransparent(_pipeCursorGhost, 0.55f);
        }

        /// <summary>Destroys the pipe-cursor ghost if one is active.</summary>
        public void ClearPipeCursorGhost()
        {
            if (_pipeCursorGhost == null) return;
            UnityEngine.Object.Destroy(_pipeCursorGhost);
            _pipeCursorGhost = null;
        }

        /// <summary>Sets the cursor to ready state (green) and applies the ready colour to the ghost.</summary>
        public void SetReadyState(bool ready)
        {
            if (ready == _cursorInReadyState) return;
            if (ready)
            {
                if (_toolGhostIndicator != null && _toolGhostIndicator.activeSelf)
                    MaterialHelper.ApplyToolCursor(_toolGhostIndicator, ReadyColor);
                _cursorInReadyState = true;
            }
            else
            {
                RestoreColor();
            }
        }

        /// <summary>Restores the original semi-transparent tool materials, exiting ready state.</summary>
        public void RestoreColor()
        {
            _cursorInReadyState = false;
            if (_toolGhostIndicator == null) return;

            var renderers = _toolGhostIndicator.GetComponentsInChildren<Renderer>(true);
            if (_toolGhostOriginalMaterials != null && _toolGhostOriginalMaterials.Length == renderers.Length)
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterials = _toolGhostOriginalMaterials[i];
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private static bool TryGetActiveToolDefinition(out string toolId, out ToolDefinition tool)
        {
            toolId = null;
            tool = null;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            if (session == null || session.Package == null || session.ToolController == null)
                return false;

            toolId = session.ToolController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(toolId))
                return false;

            return session.Package.TryGetTool(toolId, out tool);
        }

        /// <summary>
        /// Computes a local rotation that aligns the model's principal axis with local Y (up).
        /// Uses actual mesh vertices to find the two farthest-apart points, which defines
        /// the shaft direction even for diagonally-oriented models from image-to-3D.
        /// For puck/disc-shaped models (where the two longest bbox extents are similar),
        /// falls back to aligning the thinnest axis to camera-forward so the wide face is visible.
        /// </summary>
        public static Quaternion ComputeUprightCorrection(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return Quaternion.identity;

            var allPoints = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var verts = mf.sharedMesh.vertices;
                var localToRoot = root.transform.InverseTransformPoint(mf.transform.position);
                var rot = Quaternion.Inverse(root.transform.rotation) * mf.transform.rotation;
                var scale = mf.transform.lossyScale;
                var rootScale = root.transform.lossyScale;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 v = rot * Vector3.Scale(verts[i],
                        new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z))
                        + localToRoot;
                    allPoints.Add(v);
                }
            }

            if (allPoints.Count < 2) return Quaternion.identity;

            Vector3 bmin = allPoints[0], bmax = allPoints[0];
            for (int i = 1; i < allPoints.Count; i++)
            {
                bmin = Vector3.Min(bmin, allPoints[i]);
                bmax = Vector3.Max(bmax, allPoints[i]);
            }
            Vector3 extents = bmax - bmin;

            float[] sorted = { extents.x, extents.y, extents.z };
            System.Array.Sort(sorted);
            float mid    = sorted[1];
            float longest = sorted[2];

            // Puck/disc detection: if the two longest extents are nearly equal (ratio < 1.2),
            // the model has no clear shaft. Align the thinnest axis to forward (Z)
            // so the wide face is visible to the user.
            const float PuckThreshold = 1.2f;
            if (longest > 0.001f && mid > 0.001f && longest / mid < PuckThreshold)
            {
                Vector3 thinAxis;
                if (extents.x <= extents.y && extents.x <= extents.z)
                    thinAxis = Vector3.right;
                else if (extents.y <= extents.x && extents.y <= extents.z)
                    thinAxis = Vector3.up;
                else
                    thinAxis = Vector3.forward;

                return Quaternion.FromToRotation(thinAxis, Vector3.forward);
            }

            // Standard shaft-detection: find approximate shaft axis via farthest vertex pair
            int step = Mathf.Max(1, allPoints.Count / 200);
            Vector3 bestA = Vector3.zero, bestB = Vector3.zero;
            float bestDistSq = 0f;
            for (int i = 0; i < allPoints.Count; i += step)
            {
                for (int j = i + step; j < allPoints.Count; j += step)
                {
                    float dSq = (allPoints[i] - allPoints[j]).sqrMagnitude;
                    if (dSq > bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestA = allPoints[i];
                        bestB = allPoints[j];
                    }
                }
            }

            Vector3 shaftDir = (bestB - bestA).normalized;
            if (shaftDir.sqrMagnitude < 0.001f) return Quaternion.identity;

            if (shaftDir.y < 0f) shaftDir = -shaftDir;

            return Quaternion.FromToRotation(shaftDir, Vector3.up);
        }
    }
}
