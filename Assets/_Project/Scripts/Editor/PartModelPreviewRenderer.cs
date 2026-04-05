using System;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Renders a GLB part model in an isolated preview scene with a world grid,
    /// axis labels, and drag-to-orbit camera. Used inside the Part Context panel
    /// of the Assembly Step Authoring window.
    ///
    /// Lifecycle: create when a part is selected, Dispose() when part changes or
    /// the authoring window closes.
    /// </summary>
    internal sealed class PartModelPreviewRenderer : IDisposable
    {
        // ── Camera orbit state ──────────────────────────────────────────────
        private float _yaw   = 30f;
        private float _pitch = 20f;
        private float _distance;

        // ── Model state ──────────────────────────────────────────────────────
        private Vector3    _modelEuler;
        private Bounds     _modelBounds;
        private GameObject _modelInstance;

        // ── Preview infra ────────────────────────────────────────────────────
        private PreviewRenderUtility _preview;
        private Material             _gridMat;

        // Camera field of view
        private const float CameraFov = 30f;

        internal Vector3 ModelEuler => _modelEuler;

        // ── Construction ─────────────────────────────────────────────────────

        internal PartModelPreviewRenderer(string assetPath, Vector3 initialEuler)
        {
            _preview = new PreviewRenderUtility();
            _preview.camera.nearClipPlane = 0.001f;
            _preview.camera.farClipPlane  = 500f;
            _preview.camera.fieldOfView   = CameraFov;
            _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            _preview.camera.clearFlags      = CameraClearFlags.SolidColor;

            // Ambient light so the model is visible from all angles
            _preview.lights[0].intensity  = 0.8f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _preview.lights[1].intensity  = 0.4f;
            _preview.ambientColor = new Color(0.3f, 0.3f, 0.3f);

            CreateGridMaterial();
            LoadModel(assetPath, initialEuler);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Sets the model's rotation and updates it in the preview scene.</summary>
        internal void SetModelEuler(Vector3 euler)
        {
            _modelEuler = euler;
            if (_modelInstance != null)
                _modelInstance.transform.localRotation = Quaternion.Euler(euler);
        }

        /// <summary>Applies a mouse-drag delta to the orbit camera. Call Repaint() after.</summary>
        internal void Orbit(Vector2 screenDelta)
        {
            _yaw   += screenDelta.x * 0.4f;
            _pitch -= screenDelta.y * 0.4f;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
        }

        internal void Zoom(float delta)
        {
            _distance = Mathf.Clamp(_distance - delta * _distance * 0.1f, 0.01f, 500f);
        }

        internal void ResetView()
        {
            _yaw = 30f; _pitch = 20f;
        }

        /// <summary>
        /// Renders the preview into <paramref name="rect"/> and draws axis labels as a GUI overlay.
        /// Call this inside OnGUI. Returns true if the window should repaint next frame
        /// (e.g., while the mouse is dragging inside the rect).
        /// </summary>
        internal bool Draw(Rect rect, bool useMm)
        {
            if (_preview == null || rect.width < 2 || rect.height < 2)
                return false;

            PositionCamera();

            _preview.BeginPreview(rect, GUIStyle.none);
            DrawGrid(useMm);
            DrawAxisLines();
            _preview.camera.Render();
            var tex = _preview.EndPreview();

            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
            DrawAxisLabels(rect);

            // Handle scroll-wheel zoom
            var ev = Event.current;
            if (rect.Contains(ev.mousePosition) && ev.type == EventType.ScrollWheel)
            {
                Zoom(ev.delta.y);
                ev.Use();
                return true;
            }

            return false;
        }

        // ── Setup ─────────────────────────────────────────────────────────────

        private void LoadModel(string assetPath, Vector3 initialEuler)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) return;

            _modelInstance = UnityEngine.Object.Instantiate(prefab);
            _modelInstance.hideFlags = HideFlags.HideAndDontSave;
            _preview.AddSingleGO(_modelInstance);

            // Compute aggregate world bounds
            var renderers = _modelInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                _modelBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    _modelBounds.Encapsulate(renderers[i].bounds);
            }
            else
            {
                _modelBounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }

            // Shift model so its bottom-centre sits at the origin
            Vector3 offset = -new Vector3(_modelBounds.center.x, _modelBounds.min.y, _modelBounds.center.z);
            _modelInstance.transform.position      = offset;
            _modelInstance.transform.localRotation = Quaternion.Euler(initialEuler);
            _modelEuler = initialEuler;

            // Re-compute bounds in local space (centred after shift)
            _modelBounds.center += offset;

            _distance = Mathf.Max(_modelBounds.size.magnitude * 1.8f, 0.05f);
        }

        private void CreateGridMaterial()
        {
            var shader = Shader.Find("Hidden/Internal-Colored")
                      ?? Shader.Find("Unlit/Color");
            _gridMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _gridMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _gridMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _gridMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
            _gridMat.SetInt("_ZWrite",   0);
        }

        // ── Camera ────────────────────────────────────────────────────────────

        private void PositionCamera()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var pivot = new Vector3(_modelBounds.center.x, _modelBounds.min.y + _modelBounds.size.y * 0.5f, _modelBounds.center.z);
            _preview.camera.transform.position = pivot + rot * (Vector3.back * _distance);
            _preview.camera.transform.LookAt(pivot);
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        private void DrawGrid(bool useMm)
        {
            var cam = _preview.camera;
            float gridY = _modelBounds.min.y;

            // Step: 100 mm (0.1 m) or ~4 inches (0.1016 m)
            float step     = useMm ? 0.1f : 0.1016f;
            float halfExt  = Mathf.Max(_modelBounds.size.magnitude, 0.2f);
            int   lineCount = Mathf.CeilToInt(halfExt / step) + 2;
            float gridSize  = lineCount * step;

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            _gridMat.SetPass(0);
            GL.Begin(GL.LINES);

            // Minor grid lines
            for (int i = -lineCount; i <= lineCount; i++)
            {
                float t = i * step;
                GL.Color(new Color(0.28f, 0.28f, 0.28f, 1f));
                GL.Vertex3(-gridSize, gridY, t); GL.Vertex3(gridSize, gridY, t);
                GL.Vertex3(t, gridY, -gridSize); GL.Vertex3(t, gridY, gridSize);
            }

            // X axis — red
            GL.Color(new Color(0.75f, 0.18f, 0.18f, 1f));
            GL.Vertex3(-gridSize, gridY, 0f); GL.Vertex3(gridSize, gridY, 0f);

            // Z axis — blue
            GL.Color(new Color(0.18f, 0.18f, 0.75f, 1f));
            GL.Vertex3(0f, gridY, -gridSize); GL.Vertex3(0f, gridY, gridSize);

            // Y axis — green (vertical, height of model)
            GL.Color(new Color(0.18f, 0.75f, 0.18f, 1f));
            GL.Vertex3(0f, gridY, 0f); GL.Vertex3(0f, gridY + _modelBounds.size.y * 1.3f, 0f);

            GL.End();
            GL.PopMatrix();
        }

        private void DrawAxisLines()
        {
            // Axis end-tip markers drawn as short crossing lines so labels have an anchor
            // (the labels themselves are drawn as GUI overlay in DrawAxisLabels)
        }

        // ── Axis labels (GUI overlay) ─────────────────────────────────────────

        private void DrawAxisLabels(Rect rect)
        {
            if (_preview == null) return;
            var cam = _preview.camera;

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

            float axisLen = _modelBounds.size.magnitude * 0.6f;
            DrawLabel(rect, cam, new Vector3( axisLen, _modelBounds.min.y, 0f), "X", new Color(0.9f, 0.3f, 0.3f), labelStyle);
            DrawLabel(rect, cam, new Vector3(0f, _modelBounds.min.y + _modelBounds.size.y * 1.35f, 0f), "Y", new Color(0.3f, 0.9f, 0.3f), labelStyle);
            DrawLabel(rect, cam, new Vector3(0f, _modelBounds.min.y, axisLen), "Z", new Color(0.3f, 0.5f, 0.95f), labelStyle);
        }

        private static void DrawLabel(Rect rect, Camera cam, Vector3 worldPos, string text, Color col, GUIStyle style)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            if (vp.z <= 0f) return; // behind camera
            var screenPt = new Vector2(
                rect.x + vp.x * rect.width,
                rect.y + (1f - vp.y) * rect.height);
            var labelRect = new Rect(screenPt.x - 12f, screenPt.y - 8f, 24f, 16f);
            style.normal.textColor = col;
            GUI.Label(labelRect, text, style);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_modelInstance != null)
                UnityEngine.Object.DestroyImmediate(_modelInstance);
            if (_gridMat != null)
                UnityEngine.Object.DestroyImmediate(_gridMat);
            _preview?.Cleanup();
            _preview = null;
        }
    }
}
