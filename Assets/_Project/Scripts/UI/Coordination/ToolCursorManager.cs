using System;
using System.Threading;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages the tool-cursor preview and pipe-cursor preview that follow the pointer
    /// on screen during tool-mode and pipe-connection steps respectively.
    /// Extracted from PartInteractionBridge to keep cursor concerns in one place.
    /// </summary>
    internal sealed class ToolCursorManager
    {
        // ── Constants ──────────────────────────────────────────────────────────────
        public const float CursorUniformScale      = 0.16f;
        public const float CursorRayDistance       = 0.75f;
        public const float PipeCursorRayDistance   = 0.35f;  // closer than tool preview
        public const float CursorVerticalOffset    = 0.15f;
        public const float ScreenProximityReadyPx  = 150f;   // screen pixels — cursor changes colour

        private static readonly Color ReadyColor = new Color(0.15f, 1.0f, 0.4f, 0.85f);
        private static readonly Color ReadyEmission = new Color(0.1f, 0.8f, 0.3f, 1f);

        // ── State ──────────────────────────────────────────────────────────────────
        private readonly Transform _fallbackParent;
        private GameObject _toolPreviewIndicator;
        private Quaternion _toolPreviewUpCorrection = Quaternion.identity;
        private Vector3 _gripOffset = Vector3.zero;
        private Material[][] _toolPreviewOriginalMaterials;
        private int _refreshGeneration;
        private Vector3 _baseLocalScale;
        private float _baseToolScale;             // CursorUniformScale * scaleOverride before assembly scale
        private PreviewSceneSetup _sceneSetup;  // cached to read assembly scale each frame
        private GameObject _pipeCursorPreview;
        private bool _cursorInReadyState;
        private bool _positionUpdateSuspended;

        // ── Public interface ───────────────────────────────────────────────────────
        public GameObject ToolPreview     => _toolPreviewIndicator;
        public bool CursorInReadyState    => _cursorInReadyState;

        /// <summary>
        /// When true, UpdatePosition() skips repositioning the tool preview.
        /// Used by the preview system while the preview is detached from the camera.
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

        /// <summary>Destroys the tool preview, honouring the hint-preview coupling.</summary>
        public void Clear(bool toolPreviewIsHintPreview, Action clearHintCallback)
        {
            if (_toolPreviewIndicator == null)
                return;

            if (toolPreviewIsHintPreview)
                clearHintCallback?.Invoke();

            UnityEngine.Object.Destroy(_toolPreviewIndicator);
            _toolPreviewIndicator = null;
            _toolPreviewUpCorrection = Quaternion.identity;
            _cursorInReadyState = false;
            _positionUpdateSuspended = false;
            _toolPreviewOriginalMaterials = null;
        }

        /// <summary>
        /// Releases the tool preview without destroying it, so the caller can
        /// repurpose it (e.g. convert it into a persistent tool).
        /// Returns the detached GameObject, or null if there was no preview.
        /// After this call, <see cref="ToolPreview"/> is null and a new
        /// <see cref="RefreshAsync"/> call will create a fresh cursor preview.
        /// </summary>
        public GameObject DetachPreview()
        {
            GameObject preview = _toolPreviewIndicator;
            if (preview == null) return null;

            _toolPreviewIndicator = null;
            _toolPreviewUpCorrection = Quaternion.identity;
            _cursorInReadyState = false;
            _positionUpdateSuspended = false;
            _toolPreviewOriginalMaterials = null;
            return preview;
        }

        /// <summary>Loads the active tool model, parents it to the camera and configures it as the cursor preview.</summary>
        public async Task RefreshAsync(PackagePartSpawner spawner, PreviewSceneSetup setup,
                            bool toolPreviewIsHintPreview, Action clearHintCallback,
                            CancellationToken ct = default)
        {
            Clear(toolPreviewIsHintPreview, clearHintCallback);

            if (!Application.isPlaying || spawner == null || setup == null)
                return;

            if (!TryGetActiveToolDefinition(out string activeToolId, out ToolDefinition tool))
                return;

            int myGeneration = ++_refreshGeneration;

            GameObject previewTool = null;
            if (!string.IsNullOrWhiteSpace(tool.assetRef))
            {
                string toolPath = tool.assetRef.Contains("/")
                    ? tool.assetRef
                    : "assets/tools/" + tool.assetRef;
                previewTool = await spawner.LoadPackageAssetAsync(toolPath, ct: ct);
            }

            // Another RefreshAsync call started while we were awaiting — discard our result
            if (myGeneration != _refreshGeneration)
            {
                if (previewTool != null)
                    UnityEngine.Object.Destroy(previewTool);
                return;
            }

            if (previewTool == null)
                previewTool = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            _toolPreviewIndicator = previewTool;
            _toolPreviewIndicator.name = $"CursorTool_{activeToolId}";

            Camera mainCam = CameraUtil.GetMain();
            _toolPreviewIndicator.transform.SetParent(
                mainCam != null ? mainCam.transform : _fallbackParent, false);

            _sceneSetup = setup;
            float toolScale = (tool.scaleOverride > 0f)
                ? CursorUniformScale * tool.scaleOverride
                : CursorUniformScale;
            _baseToolScale = toolScale;
            _toolPreviewIndicator.transform.localScale = Vector3.one * toolScale;

            _toolPreviewUpCorrection = ToolPoseResolver.ResolvePreviewRotation(tool, _toolPreviewIndicator);
            _gripOffset = ToolPoseResolver.ResolveCursorOffset(tool);
            OseLog.Info($"[ToolPreview] tool={activeToolId} hasToolPose={tool.HasToolPose} hasOrientationOverride={tool.HasOrientationOverride} -> quat={_toolPreviewUpCorrection} gripOffset={_gripOffset}");

            foreach (Collider col in _toolPreviewIndicator.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(col);

            // Cache the original imported materials so they can be restored
            // when the preview is converted into a persistent (placed) tool.
            MaterialHelper.SaveOriginals(_toolPreviewIndicator);

            _toolPreviewOriginalMaterials = MaterialHelper.MakeTransparent(_toolPreviewIndicator, 0.55f);
            _baseLocalScale = _toolPreviewIndicator.transform.localScale;

            // Start hidden — the first real UpdatePosition call from the bridge's
            // Update loop will place it at the actual cursor and show it.
            // This prevents a one-frame flash at screen center that looks like
            // the tool is pre-placed on the workpiece.
            _toolPreviewIndicator.SetActive(false);
        }

        /// <summary>
        /// Moves both cursor previews to track the given screen position.
        /// Call every frame from PartInteractionBridge.Update().
        /// </summary>
        public void UpdatePosition(bool isDragging, Vector2 screenPos)
        {
            if (isDragging)
            {
                if (_toolPreviewIndicator != null) _toolPreviewIndicator.SetActive(false);
                if (_pipeCursorPreview    != null) _pipeCursorPreview.SetActive(false);
                return;
            }

            Camera cam = CameraUtil.GetMain();
            if (cam == null)
                return;

            Vector3 vp = cam.ScreenToViewportPoint(screenPos);
            float halfH = CursorRayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cam.aspect;
            float localX = (vp.x - 0.5f) * 2f * halfW;

            // Cursor tool stays at fixed camera-relative size (_baseToolScale).
            // Assembly-matched scale is applied by ToolActionPreviewController during
            // the approach lerp so the tool matches the parts when it reaches the target.

            if (_toolPreviewIndicator != null && !_positionUpdateSuspended)
            {
                if (!_toolPreviewIndicator.activeSelf) _toolPreviewIndicator.SetActive(true);

                // When a grip offset is authored, place grip exactly at the pointer.
                // When no grip offset, float the preview above the pointer so the
                // workpiece stays visible under the tool.
                bool hasGrip = _gripOffset.sqrMagnitude > 0.001f;
                float yOffset = hasGrip ? 0f : CursorVerticalOffset;
                float localY = (vp.y - 0.5f) * 2f * halfH + yOffset;
                var localPos = new Vector3(localX, localY, CursorRayDistance);

                // Shift the preview so the grip point (+ cursorOffset) sits at the pointer.
                float s = _toolPreviewIndicator.transform.localScale.x;
                Vector3 adjustedPos = localPos - _toolPreviewUpCorrection * (_gripOffset * s);
                _toolPreviewIndicator.transform.localPosition = adjustedPos;
                _toolPreviewIndicator.transform.localRotation = _toolPreviewUpCorrection;
            }

            if (_pipeCursorPreview != null)
            {
                float pipeHalfH = PipeCursorRayDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float pipeHalfW = pipeHalfH * cam.aspect;
                var pipeLocalPos = new Vector3(
                    (vp.x - 0.5f) * 2f * pipeHalfW,
                    (vp.y - 0.5f) * 2f * pipeHalfH,
                    PipeCursorRayDistance);
                if (!_pipeCursorPreview.activeSelf) _pipeCursorPreview.SetActive(true);
                _pipeCursorPreview.transform.localPosition = pipeLocalPos;
                _pipeCursorPreview.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Spawns a semi-transparent wire stub or part clone that tracks the cursor.</summary>
        public async Task SpawnPipeCursorPreviewAsync(MachinePackageDefinition package, StepDefinition step,
                                         Func<string, GameObject> findSpawnedPart, PackagePartSpawner spawner,
                                         CancellationToken ct = default)
        {
            ClearPipeCursorPreview();

            GameObject preview = null;

            // WireConnect steps carry no part reference — create a small stub tube from
            // the first wire entry's color and width so the cursor indicates wire mode.
            if (step.wireConnect?.IsConfigured == true)
            {
                WireConnectEntry firstEntry = step.wireConnect.wires[0];
                float radius = firstEntry.radius > 0f ? firstEntry.radius : 0.003f;
                Color c = firstEntry.color.a > 0f
                    ? new Color(firstEntry.color.r, firstEntry.color.g, firstEntry.color.b, firstEntry.color.a)
                    : new Color(0.15f, 0.15f, 0.15f, 1f);
                var stubPath = new SplinePathDefinition
                {
                    radius = radius, segments = 6, metallic = 0f, smoothness = 0.25f,
                    knots  = new SceneFloat3[]
                    {
                        new SceneFloat3 { x = 0f, y = -0.05f, z = 0f },
                        new SceneFloat3 { x = 0f, y =  0.05f, z = 0f },
                    }
                };
                preview = SplinePartFactory.Create("CursorWire", stubPath, c, _fallbackParent);
            }
            else
            {
                string[] reqParts = step.GetEffectiveRequiredPartIds();
                if (reqParts == null || reqParts.Length == 0) return;
                string partId = reqParts[0];

                GameObject spawnedSource = findSpawnedPart?.Invoke(partId);
                if (spawnedSource != null)
                {
                    preview = UnityEngine.Object.Instantiate(spawnedSource);
                }
                else if (package.TryGetPart(partId, out PartDefinition partDef)
                         && !string.IsNullOrWhiteSpace(partDef.assetRef))
                {
                    preview = await spawner.LoadPackageAssetAsync(partDef.assetRef, ct: ct);
                }

                if (preview == null)
                    preview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }

            _pipeCursorPreview = preview;
            _pipeCursorPreview.name = "CursorCable";

            Camera mainCam = CameraUtil.GetMain();
            _pipeCursorPreview.transform.SetParent(
                mainCam != null ? mainCam.transform : _fallbackParent, false);

            _pipeCursorPreview.transform.localScale = Vector3.one * CursorUniformScale;

            foreach (Collider col in _pipeCursorPreview.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.Destroy(col);

            MaterialHelper.MakeTransparent(_pipeCursorPreview, 0.55f);
        }

        /// <summary>Destroys the pipe-cursor preview if one is active.</summary>
        public void ClearPipeCursorPreview()
        {
            if (_pipeCursorPreview == null) return;
            UnityEngine.Object.Destroy(_pipeCursorPreview);
            _pipeCursorPreview = null;
        }

        /// <summary>Sets the cursor to ready state (bright green glow) and applies it to the preview.</summary>
        public void SetReadyState(bool ready)
        {
            if (ready == _cursorInReadyState) return;
            if (ready)
            {
                if (_toolPreviewIndicator != null && _toolPreviewIndicator.activeSelf)
                {
                    MaterialHelper.ApplyToolCursor(_toolPreviewIndicator, ReadyColor);
                    MaterialHelper.SetEmission(_toolPreviewIndicator, ReadyEmission * 2f);
                }
                _cursorInReadyState = true;
            }
            else
            {
                RestoreColor();
            }
        }

        /// <summary>
        /// Call every frame from the bridge's Update loop to animate the ready-state pulse.
        /// When ready, the tool preview gently pulses in scale and emission intensity
        /// so the user clearly sees "this tool is ready to act — click now."
        /// </summary>
        public void UpdateReadyPulse()
        {
            if (!_cursorInReadyState || _toolPreviewIndicator == null || _positionUpdateSuspended) return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            float scale = 1f + 0.08f * pulse;
            _toolPreviewIndicator.transform.localScale = _baseLocalScale * scale;

            Color emission = ReadyEmission * Mathf.Lerp(1.2f, 2.5f, pulse);
            MaterialHelper.SetEmission(_toolPreviewIndicator, emission);
        }

        /// <summary>Restores the original semi-transparent tool materials, exiting ready state.</summary>
        public void RestoreColor()
        {
            _cursorInReadyState = false;
            if (_toolPreviewIndicator == null) return;

            var renderers = MaterialHelper.GetRenderers(_toolPreviewIndicator);
            if (_toolPreviewOriginalMaterials != null && _toolPreviewOriginalMaterials.Length == renderers.Length)
            {
                for (int i = 0; i < renderers.Length; i++)
                    renderers[i].sharedMaterials = _toolPreviewOriginalMaterials[i];
            }

            // Reset scale back to base after ready pulse
            if (_baseLocalScale.sqrMagnitude > 0f)
                _toolPreviewIndicator.transform.localScale = _baseLocalScale;

            MaterialHelper.SetEmission(_toolPreviewIndicator, Color.black);
        }

        /// <summary>
        /// Configures the tool preview for XR grab interaction using toolPose data.
        /// Call after <see cref="RefreshAsync"/> when in XR mode.
        /// In controller mode, the tool renders semi-transparent so the controller
        /// model remains visible through it.
        /// </summary>
        public void ConfigureXRGrab(bool isControllerMode)
        {
            if (_toolPreviewIndicator == null) return;

            if (!TryGetActiveToolDefinition(out _, out ToolDefinition tool))
                return;

            // Unparent from camera — XR grab will control position
            _toolPreviewIndicator.transform.SetParent(null, true);
            _positionUpdateSuspended = true;

            var handler = _toolPreviewIndicator.GetComponent<XRToolGrabHandler>();
            if (handler == null)
                handler = _toolPreviewIndicator.AddComponent<XRToolGrabHandler>();

            handler.Setup(tool.toolPose, isControllerMode);
        }

        /// <summary>
        /// Switches the XR tool visual mode (controller = faded, hand = opaque).
        /// No-op if the preview has no <see cref="XRToolGrabHandler"/>.
        /// </summary>
        public void SetXRControllerMode(bool isController)
        {
            if (_toolPreviewIndicator == null) return;
            var handler = _toolPreviewIndicator.GetComponent<XRToolGrabHandler>();
            handler?.SetControllerMode(isController);
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        public float AssemblyScale
        {
            get
            {
                if (_sceneSetup != null && _sceneSetup.PreviewRoot != null)
                    return _sceneSetup.PreviewRoot.lossyScale.x;
                return 1f;
            }
        }

        private static bool TryGetActiveToolDefinition(out string toolId, out ToolDefinition tool)
        {
            toolId = null;
            tool = null;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            if (session == null || session.Package == null || session.ToolController == null)
                return false;

            toolId = session.ToolController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(toolId))
                return false;

            return session.Package.TryGetTool(toolId, out tool);
        }

        /// <summary>
        /// Delegates to <see cref="ToolPoseResolver.ComputeUprightCorrection"/>.
        /// Kept for backward compatibility with any external callers.
        /// </summary>
        public static Quaternion ComputeUprightCorrection(GameObject root)
            => ToolPoseResolver.ComputeUprightCorrection(root);
    }
}
