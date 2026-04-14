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
            {
                _modelInstance.transform.localRotation = Quaternion.Euler(euler);
                RecomputeBounds();
            }
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
        /// Optional annotation layers. Authors toggle these via the preview
        /// toolbar — see <see cref="OSE.Editor.ToolTargetAuthoringWindow"/>
        /// for persistence in EditorPrefs.
        /// </summary>
        internal struct DrawOptions
        {
            public bool useMm;
            public bool showBounds;     // wireframe box + L×W×H edge labels
            public bool showGridTicks;  // distance labels on major grid lines
        }

        /// <summary>
        /// Renders the preview into <paramref name="rect"/> and draws axis labels as a GUI overlay.
        /// Call this inside OnGUI. Returns true if the window should repaint next frame
        /// (e.g., while the mouse is dragging inside the rect).
        /// </summary>
        internal bool Draw(Rect rect, bool useMm)
            => Draw(rect, new DrawOptions { useMm = useMm, showBounds = true, showGridTicks = true });

        internal bool Draw(Rect rect, DrawOptions opts)
        {
            if (_preview == null || rect.width < 2 || rect.height < 2)
                return false;

            PositionCamera();

            _preview.BeginPreview(rect, GUIStyle.none);
            DrawGrid(opts.useMm);
            if (opts.showBounds) DrawBoundsBox();
            _preview.camera.Render();
            var tex = _preview.EndPreview();

            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
            if (opts.showBounds)    DrawDimensionLabels(rect, opts.useMm);
            if (opts.showGridTicks) DrawGridTickLabels(rect, opts.useMm);
            // Gnomon is always on — it's the only rotation reference that
            // stays in view when the camera orbits to extreme angles.
            DrawGnomon(rect);

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

            _modelInstance.transform.localRotation = Quaternion.Euler(initialEuler);
            _modelEuler = initialEuler;

            RecomputeBounds();

            _distance = Mathf.Max(_modelBounds.size.magnitude * 1.8f, 0.05f);
        }

        /// <summary>
        /// Recomputes <see cref="_modelBounds"/> from the current world-space
        /// renderer bounds and re-anchors the model so its bottom-centre sits
        /// at the world origin. Called after load and after every rotation change
        /// so dimension labels, bounding box, and grid scale stay accurate.
        /// </summary>
        private void RecomputeBounds()
        {
            var renderers = _modelInstance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _modelBounds = new Bounds(Vector3.zero, Vector3.one * 0.1f);
                return;
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            // Re-anchor: bottom-centre at world origin
            Vector3 shift = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            _modelInstance.transform.position += shift;
            bounds.center += shift;

            _modelBounds = bounds;
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

            // Minor step: 100 mm / ~4 in. Major step every 5th line for clarity.
            float step      = useMm ? 0.1f    : 0.1016f;
            float majorStep = step * 5f;   // 500 mm / ~20 in
            float halfExt   = Mathf.Max(_modelBounds.size.magnitude, 0.2f);
            int   lineCount = Mathf.CeilToInt(halfExt / step) + 2;
            float gridSize  = lineCount * step;

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            _gridMat.SetPass(0);

            // Subtle ground-plane quad so the grid has contrast against the
            // darker background above. Drawn first so the grid lines render
            // on top.
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0.09f, 0.09f, 0.10f, 1f));
            GL.Vertex3(-gridSize, gridY - 0.0001f, -gridSize);
            GL.Vertex3( gridSize, gridY - 0.0001f, -gridSize);
            GL.Vertex3( gridSize, gridY - 0.0001f,  gridSize);
            GL.Vertex3(-gridSize, gridY - 0.0001f,  gridSize);
            GL.End();

            GL.Begin(GL.LINES);

            // Grid lines — minor (darker) and major (brighter every 5th line).
            Color minorCol = new Color(0.38f, 0.38f, 0.40f, 1f);
            Color majorCol = new Color(0.58f, 0.58f, 0.62f, 1f);
            float eps = step * 0.001f;
            for (int i = -lineCount; i <= lineCount; i++)
            {
                float t = i * step;
                bool isMajor = Mathf.Abs(Mathf.Repeat(t + majorStep * 0.5f, majorStep) - majorStep * 0.5f) < eps;
                GL.Color(isMajor ? majorCol : minorCol);
                GL.Vertex3(-gridSize, gridY, t); GL.Vertex3(gridSize, gridY, t);
                GL.Vertex3(t, gridY, -gridSize); GL.Vertex3(t, gridY, gridSize);
            }

            // X axis — red
            GL.Color(new Color(0.92f, 0.30f, 0.30f, 1f));
            GL.Vertex3(-gridSize, gridY, 0f); GL.Vertex3(gridSize, gridY, 0f);

            // Z axis — blue
            GL.Color(new Color(0.35f, 0.55f, 1f, 1f));
            GL.Vertex3(0f, gridY, -gridSize); GL.Vertex3(0f, gridY, gridSize);

            // Y axis — green (vertical, height of model)
            GL.Color(new Color(0.35f, 0.95f, 0.35f, 1f));
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
            if (vp.z <= 0f) return;                     // behind camera
            if (vp.x < 0f || vp.x > 1f) return;         // outside rect horizontally
            if (vp.y < 0f || vp.y > 1f) return;         // outside rect vertically

            var screenPt = new Vector2(
                rect.x + vp.x * rect.width,
                rect.y + (1f - vp.y) * rect.height);
            // Size the label rect to text width so long dimensions ("1200 mm")
            // don't get clipped by the fixed 24 px rect. Style must be initialised.
            float w = style.CalcSize(new GUIContent(text)).x + 6f;
            float h = style.CalcSize(new GUIContent(text)).y + 2f;
            var labelRect = new Rect(screenPt.x - w * 0.5f, screenPt.y - h * 0.5f, w, h);

            // Drop the label if the computed rect would straddle the preview
            // border — avoids half-visible numbers at steep camera angles.
            if (labelRect.xMin < rect.xMin || labelRect.xMax > rect.xMax) return;
            if (labelRect.yMin < rect.yMin || labelRect.yMax > rect.yMax) return;

            style.normal.textColor = col;
            GUI.Label(labelRect, text, style);
        }

        // ── Gnomon (fixed-position rotation reference) ────────────────────────

        /// <summary>
        /// Small three-axis gizmo pinned to the lower-left corner of the
        /// preview. Unaffected by model scale or orbit distance — rotates
        /// with the camera so the author can always read world X/Y/Z at a
        /// glance, even when dimension/tick labels are off-screen.
        /// </summary>
        private void DrawGnomon(Rect rect)
        {
            var cam = _preview.camera;
            // Anchor in screen space, projected to a world point in front of
            // the camera at a fixed distance so the axes have consistent
            // on-screen length regardless of the model's scale.
            const float pad = 28f;
            Vector2 anchor = new Vector2(rect.xMin + pad, rect.yMax - pad);

            Quaternion invCam = Quaternion.Inverse(cam.transform.rotation);
            // Each world axis in camera-local space — drop the Z component to
            // project onto the screen plane.
            Vector3 xLocal = invCam * Vector3.right;
            Vector3 yLocal = invCam * Vector3.up;
            Vector3 zLocal = invCam * Vector3.forward;

            float len = 22f;
            DrawGnomonAxis(anchor, new Vector2(xLocal.x,  xLocal.y) * len, new Color(0.95f, 0.35f, 0.35f), "X");
            DrawGnomonAxis(anchor, new Vector2(yLocal.x,  yLocal.y) * len, new Color(0.40f, 0.95f, 0.40f), "Y");
            DrawGnomonAxis(anchor, new Vector2(zLocal.x,  zLocal.y) * len, new Color(0.45f, 0.65f, 1f),    "Z");

            // Origin dot
            var dot = new Rect(anchor.x - 2f, anchor.y - 2f, 4f, 4f);
            EditorGUI.DrawRect(dot, new Color(0.85f, 0.85f, 0.90f, 0.85f));
        }

        private static void DrawGnomonAxis(Vector2 origin, Vector2 screenDelta, Color col, string label)
        {
            // screenDelta y is camera-up positive; GUI y is screen-down positive
            // → flip y for IMGUI.
            Vector2 end = origin + new Vector2(screenDelta.x, -screenDelta.y);
            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = col;
            Handles.DrawAAPolyLine(2.5f, new Vector3(origin.x, origin.y, 0f), new Vector3(end.x, end.y, 0f));
            Handles.color = prev;
            Handles.EndGUI();

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = col },
            };
            var labelRect = new Rect(end.x - 8f, end.y - 8f, 16f, 16f);
            GUI.Label(labelRect, label, style);
        }

        // ── Bounding box (3D geometry pass) ───────────────────────────────────

        /// <summary>
        /// Draws the 12 edges of <see cref="_modelBounds"/> as thin pale lines
        /// using the same <see cref="GL"/> immediate pipeline as the grid.
        /// Inset slightly so edges don't z-fight against the model surface.
        /// </summary>
        private void DrawBoundsBox()
        {
            var cam = _preview.camera;
            Vector3 min = _modelBounds.min;
            Vector3 max = _modelBounds.max;
            // Inset by 0.5% of the longest axis so the wireframe stays
            // visually separate from the mesh.
            float inset = Mathf.Max(_modelBounds.size.x, _modelBounds.size.y, _modelBounds.size.z) * 0.005f;
            min -= Vector3.one * inset * 0.25f;
            max += Vector3.one * inset * 0.25f;

            Vector3 v000 = new Vector3(min.x, min.y, min.z);
            Vector3 v100 = new Vector3(max.x, min.y, min.z);
            Vector3 v010 = new Vector3(min.x, max.y, min.z);
            Vector3 v110 = new Vector3(max.x, max.y, min.z);
            Vector3 v001 = new Vector3(min.x, min.y, max.z);
            Vector3 v101 = new Vector3(max.x, min.y, max.z);
            Vector3 v011 = new Vector3(min.x, max.y, max.z);
            Vector3 v111 = new Vector3(max.x, max.y, max.z);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            _gridMat.SetPass(0);
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.85f, 0.85f, 0.90f, 0.65f));

            // bottom rectangle
            GL.Vertex(v000); GL.Vertex(v100);
            GL.Vertex(v100); GL.Vertex(v101);
            GL.Vertex(v101); GL.Vertex(v001);
            GL.Vertex(v001); GL.Vertex(v000);
            // top rectangle
            GL.Vertex(v010); GL.Vertex(v110);
            GL.Vertex(v110); GL.Vertex(v111);
            GL.Vertex(v111); GL.Vertex(v011);
            GL.Vertex(v011); GL.Vertex(v010);
            // vertical edges
            GL.Vertex(v000); GL.Vertex(v010);
            GL.Vertex(v100); GL.Vertex(v110);
            GL.Vertex(v001); GL.Vertex(v011);
            GL.Vertex(v101); GL.Vertex(v111);

            GL.End();
            GL.PopMatrix();
        }

        // ── Dimension + grid tick labels (GUI overlay) ────────────────────────

        private void DrawDimensionLabels(Rect rect, bool useMm)
        {
            var cam = _preview.camera;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

            Vector3 size = _modelBounds.size;
            // Place labels on the midpoint of one edge per principal axis,
            // choosing the edges along the +X, +Y, and +Z faces so the text
            // sits near the axis lines already drawn by DrawGrid.
            Vector3 xMid = new Vector3(_modelBounds.center.x, _modelBounds.min.y, _modelBounds.max.z);
            Vector3 yMid = new Vector3(_modelBounds.max.x, _modelBounds.center.y, _modelBounds.max.z);
            Vector3 zMid = new Vector3(_modelBounds.max.x, _modelBounds.min.y, _modelBounds.center.z);

            DrawLabel(rect, cam, xMid, FormatDim(size.x, useMm), new Color(0.95f, 0.55f, 0.55f), style);
            DrawLabel(rect, cam, yMid, FormatDim(size.y, useMm), new Color(0.55f, 0.95f, 0.55f), style);
            DrawLabel(rect, cam, zMid, FormatDim(size.z, useMm), new Color(0.55f, 0.70f, 0.98f), style);
        }

        private void DrawGridTickLabels(Rect rect, bool useMm)
        {
            var cam = _preview.camera;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 9,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.68f, 0.68f, 0.72f) },
            };

            float step    = useMm ? 0.1f : 0.1016f;
            float halfExt = Mathf.Max(_modelBounds.size.magnitude, 0.2f);
            int   count   = Mathf.CeilToInt(halfExt / step) + 1;
            // Label every other step so the grid stays readable.
            for (int i = -count; i <= count; i++)
            {
                if (i == 0 || (i & 1) != 0) continue;
                float t = i * step;
                DrawLabel(rect, cam,
                    new Vector3(t, _modelBounds.min.y, 0f),
                    FormatDim(Mathf.Abs(t), useMm),
                    style.normal.textColor, style);
                DrawLabel(rect, cam,
                    new Vector3(0f, _modelBounds.min.y, t),
                    FormatDim(Mathf.Abs(t), useMm),
                    style.normal.textColor, style);
            }
        }

        private static string FormatDim(float meters, bool useMm)
        {
            if (useMm) return $"{meters * 1000f:0} mm";
            return $"{meters * 39.3701f:0.00} in";
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
