using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Multi-part variant of <see cref="PartModelPreviewRenderer"/>. Renders
    /// every member of a part group inside an isolated preview scene at the
    /// poses chosen by the inspector's Start/Assembled toggle, with the same
    /// world grid + axis labels + drag-to-orbit camera as the single-part
    /// preview. Used inside the Group Context panel of the Assembly Step
    /// Authoring window so authors can see what a group "looks like as a
    /// whole" without leaving the inspector.
    ///
    /// <para>Lifecycle: create when a group is selected, <see cref="Rebuild"/>
    /// when the selection or pose mode changes, <see cref="Dispose"/> when
    /// the group changes or the authoring window closes.</para>
    /// </summary>
    internal sealed class PartGroupModelPreviewRenderer : IDisposable
    {
        // ── Camera orbit state ──────────────────────────────────────────────
        private float _yaw   = 30f;
        private float _pitch = 20f;
        private float _distance;

        // ── Scene state ──────────────────────────────────────────────────────
        private readonly List<GameObject> _modelInstances = new List<GameObject>();
        private Bounds _bounds;

        // ── Preview infra ────────────────────────────────────────────────────
        private PreviewRenderUtility _preview;
        private Material             _gridMat;

        private const float CameraFov = 30f;

        /// <summary>
        /// One member to render. <see cref="position"/>/<see cref="rotation"/>/
        /// <see cref="scale"/> are PreviewRoot-local poses — the renderer
        /// re-anchors the combined group bounds so the bottom-centre sits at
        /// the preview origin, identical to <see cref="PartModelPreviewRenderer"/>.
        /// </summary>
        internal struct MemberPose
        {
            public string assetPath;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        // ── Construction ─────────────────────────────────────────────────────

        internal PartGroupModelPreviewRenderer(IEnumerable<MemberPose> members)
        {
            _preview = new PreviewRenderUtility();
            _preview.camera.nearClipPlane = 0.001f;
            _preview.camera.farClipPlane  = 500f;
            _preview.camera.fieldOfView   = CameraFov;
            _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            _preview.camera.clearFlags      = CameraClearFlags.SolidColor;

            _preview.lights[0].intensity = 0.8f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _preview.lights[1].intensity = 0.4f;
            _preview.ambientColor = new Color(0.3f, 0.3f, 0.3f);

            CreateGridMaterial();
            LoadMembers(members);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Tears down all current model instances and re-loads the supplied
        /// members. Used when the pose mode toggle flips from Start to
        /// Assembled (or vice versa) so the preview reflects the new layout
        /// without requiring the inspector to dispose + recreate the renderer.
        /// </summary>
        internal void Rebuild(IEnumerable<MemberPose> members)
        {
            ClearModels();
            LoadMembers(members);
        }

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
        /// Mirrors <see cref="PartModelPreviewRenderer.DrawOptions"/> so the
        /// inspector can pass the same toolbar prefs through to either widget.
        /// </summary>
        internal struct DrawOptions
        {
            public bool useMm;
            public bool showBounds;
            public bool showGridTicks;
        }

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
            DrawGnomon(rect);

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

        private void LoadMembers(IEnumerable<MemberPose> members)
        {
            Bounds? combined = null;
            foreach (MemberPose m in members)
            {
                if (string.IsNullOrEmpty(m.assetPath)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.assetPath);
                if (prefab == null) continue;

                GameObject inst = UnityEngine.Object.Instantiate(prefab);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.SetPositionAndRotation(m.position, m.rotation);
                Vector3 scl = m.scale.sqrMagnitude > 0.000001f ? m.scale : Vector3.one;
                inst.transform.localScale = scl;
                _preview.AddSingleGO(inst);
                _modelInstances.Add(inst);

                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] == null) continue;
                    Bounds b = renderers[r].bounds;
                    if (b.size.sqrMagnitude < 0.000001f) continue;
                    combined = combined.HasValue ? Encapsulate(combined.Value, b) : b;
                }
            }

            _bounds = combined ?? new Bounds(Vector3.zero, Vector3.one * 0.5f);

            // Re-anchor the entire group so its bottom-centre sits at preview
            // origin. Matches PartModelPreviewRenderer's framing convention so
            // single-part and group previews feel identical.
            Vector3 shift = new Vector3(-_bounds.center.x, -_bounds.min.y, -_bounds.center.z);
            for (int i = 0; i < _modelInstances.Count; i++)
            {
                if (_modelInstances[i] == null) continue;
                _modelInstances[i].transform.position += shift;
            }
            _bounds.center += shift;

            _distance = Mathf.Max(_bounds.size.magnitude * 1.8f, 0.05f);
        }

        private static Bounds Encapsulate(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        private void ClearModels()
        {
            for (int i = 0; i < _modelInstances.Count; i++)
            {
                if (_modelInstances[i] != null)
                    UnityEngine.Object.DestroyImmediate(_modelInstances[i]);
            }
            _modelInstances.Clear();
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
            var pivot = new Vector3(_bounds.center.x, _bounds.min.y + _bounds.size.y * 0.5f, _bounds.center.z);
            _preview.camera.transform.position = pivot + rot * (Vector3.back * _distance);
            _preview.camera.transform.LookAt(pivot);
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        private void DrawGrid(bool useMm)
        {
            var cam = _preview.camera;
            float gridY = _bounds.min.y;

            float step      = useMm ? 0.1f    : 0.1016f;
            float majorStep = step * 5f;
            float halfExt   = Mathf.Max(_bounds.size.magnitude, 0.2f);
            int   lineCount = Mathf.CeilToInt(halfExt / step) + 2;
            float gridSize  = lineCount * step;

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            _gridMat.SetPass(0);

            GL.Begin(GL.QUADS);
            GL.Color(new Color(0.09f, 0.09f, 0.10f, 1f));
            GL.Vertex3(-gridSize, gridY - 0.0001f, -gridSize);
            GL.Vertex3( gridSize, gridY - 0.0001f, -gridSize);
            GL.Vertex3( gridSize, gridY - 0.0001f,  gridSize);
            GL.Vertex3(-gridSize, gridY - 0.0001f,  gridSize);
            GL.End();

            GL.Begin(GL.LINES);
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

            GL.Color(new Color(0.92f, 0.30f, 0.30f, 1f));
            GL.Vertex3(-gridSize, gridY, 0f); GL.Vertex3(gridSize, gridY, 0f);
            GL.Color(new Color(0.35f, 0.55f, 1f, 1f));
            GL.Vertex3(0f, gridY, -gridSize); GL.Vertex3(0f, gridY, gridSize);
            GL.Color(new Color(0.35f, 0.95f, 0.35f, 1f));
            GL.Vertex3(0f, gridY, 0f); GL.Vertex3(0f, gridY + _bounds.size.y * 1.3f, 0f);

            GL.End();
            GL.PopMatrix();
        }

        // ── Bounding box (3D geometry pass) ───────────────────────────────────

        private void DrawBoundsBox()
        {
            var cam = _preview.camera;
            Vector3 min = _bounds.min;
            Vector3 max = _bounds.max;
            float inset = Mathf.Max(_bounds.size.x, _bounds.size.y, _bounds.size.z) * 0.005f;
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

            GL.Vertex(v000); GL.Vertex(v100);
            GL.Vertex(v100); GL.Vertex(v101);
            GL.Vertex(v101); GL.Vertex(v001);
            GL.Vertex(v001); GL.Vertex(v000);
            GL.Vertex(v010); GL.Vertex(v110);
            GL.Vertex(v110); GL.Vertex(v111);
            GL.Vertex(v111); GL.Vertex(v011);
            GL.Vertex(v011); GL.Vertex(v010);
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

            Vector3 size = _bounds.size;
            Vector3 xMid = new Vector3(_bounds.center.x, _bounds.min.y, _bounds.max.z);
            Vector3 yMid = new Vector3(_bounds.max.x, _bounds.center.y, _bounds.max.z);
            Vector3 zMid = new Vector3(_bounds.max.x, _bounds.min.y, _bounds.center.z);

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
            float halfExt = Mathf.Max(_bounds.size.magnitude, 0.2f);
            int   count   = Mathf.CeilToInt(halfExt / step) + 1;
            for (int i = -count; i <= count; i++)
            {
                if (i == 0 || (i & 1) != 0) continue;
                float t = i * step;
                DrawLabel(rect, cam, new Vector3(t, _bounds.min.y, 0f),
                    FormatDim(Mathf.Abs(t), useMm), style.normal.textColor, style);
                DrawLabel(rect, cam, new Vector3(0f, _bounds.min.y, t),
                    FormatDim(Mathf.Abs(t), useMm), style.normal.textColor, style);
            }
        }

        private static void DrawLabel(Rect rect, Camera cam, Vector3 worldPos, string text, Color col, GUIStyle style)
        {
            Vector3 vp = cam.WorldToViewportPoint(worldPos);
            if (vp.z <= 0f) return;
            if (vp.x < 0f || vp.x > 1f) return;
            if (vp.y < 0f || vp.y > 1f) return;

            var screenPt = new Vector2(rect.x + vp.x * rect.width, rect.y + (1f - vp.y) * rect.height);
            float w = style.CalcSize(new GUIContent(text)).x + 6f;
            float h = style.CalcSize(new GUIContent(text)).y + 2f;
            var labelRect = new Rect(screenPt.x - w * 0.5f, screenPt.y - h * 0.5f, w, h);

            if (labelRect.xMin < rect.xMin || labelRect.xMax > rect.xMax) return;
            if (labelRect.yMin < rect.yMin || labelRect.yMax > rect.yMax) return;

            style.normal.textColor = col;
            GUI.Label(labelRect, text, style);
        }

        private static string FormatDim(float meters, bool useMm)
        {
            if (useMm) return $"{meters * 1000f:0} mm";
            return $"{meters * 39.3701f:0.00} in";
        }

        // ── Gnomon ────────────────────────────────────────────────────────────

        private void DrawGnomon(Rect rect)
        {
            var cam = _preview.camera;
            const float pad = 28f;
            Vector2 anchor = new Vector2(rect.xMin + pad, rect.yMax - pad);

            Quaternion invCam = Quaternion.Inverse(cam.transform.rotation);
            Vector3 xLocal = invCam * Vector3.right;
            Vector3 yLocal = invCam * Vector3.up;
            Vector3 zLocal = invCam * Vector3.forward;

            float len = 22f;
            DrawGnomonAxis(anchor, new Vector2(xLocal.x,  xLocal.y) * len, new Color(0.95f, 0.35f, 0.35f), "X");
            DrawGnomonAxis(anchor, new Vector2(yLocal.x,  yLocal.y) * len, new Color(0.40f, 0.95f, 0.40f), "Y");
            DrawGnomonAxis(anchor, new Vector2(zLocal.x,  zLocal.y) * len, new Color(0.45f, 0.65f, 1f),    "Z");

            var dot = new Rect(anchor.x - 2f, anchor.y - 2f, 4f, 4f);
            EditorGUI.DrawRect(dot, new Color(0.85f, 0.85f, 0.90f, 0.85f));
        }

        private static void DrawGnomonAxis(Vector2 origin, Vector2 screenDelta, Color col, string label)
        {
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

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            ClearModels();
            if (_gridMat != null)
                UnityEngine.Object.DestroyImmediate(_gridMat);
            _preview?.Cleanup();
            _preview = null;
        }
    }
}
