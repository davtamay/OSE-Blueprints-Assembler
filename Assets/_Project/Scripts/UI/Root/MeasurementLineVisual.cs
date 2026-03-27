using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Renders a measurement line between two world-space anchor points
    /// with a billboard TextMesh label at the midpoint showing the formatted distance.
    /// Spawned by <see cref="UseStepHandler"/> when the Measure profile completes both anchors.
    /// </summary>
    internal sealed class MeasurementLineVisual : MonoBehaviour, IAnchorLineVisual
    {
        private const float LineWidth = 0.008f;
        private static readonly Color DefaultLineColor = new Color(1f, 0.8f, 0.2f, 1f); // yellow-orange

        private const float LabelFontSize = 0.12f;
        private static readonly Color LabelColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color LabelBackgroundColor = new Color(0.08f, 0.12f, 0.2f, 0.85f);

        private const float EndpointMarkerScale = 0.03f;
        private static readonly Color EndpointColor = new Color(1f, 0.8f, 0.2f, 0.9f);

        private LineRenderer _line;
        private GameObject _labelObject;
        private TextMesh _textMesh;
        private GameObject _startMarker;
        private GameObject _endMarker;

        /// <summary>
        /// Spawns a measurement line visual between two world-space points.
        /// </summary>
        public static MeasurementLineVisual Spawn(Vector3 worldStart, Vector3 worldEnd, string label, Color? lineColor = null)
        {
            var go = new GameObject("MeasurementLineVisual");
            var vis = go.AddComponent<MeasurementLineVisual>();
            vis.Build(worldStart, worldEnd, label, lineColor ?? DefaultLineColor);
            return vis;
        }

        private void Build(Vector3 start, Vector3 end, string label, Color lineColor)
        {
            // --- Line ---
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            _line.startWidth = LineWidth;
            _line.endWidth = LineWidth;
            _line.material = CreateLineMaterial(lineColor);
            _line.startColor = lineColor;
            _line.endColor = lineColor;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            // --- Endpoint markers (small spheres at A and B) ---
            _startMarker = CreateEndpointMarker(start, lineColor);
            _endMarker = CreateEndpointMarker(end, lineColor);

            // --- Label at midpoint ---
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 lineDir = (end - start).normalized;
            Vector3 labelOffset = Vector3.up * 0.06f;

            _labelObject = new GameObject("MeasurementLabel");
            _labelObject.transform.position = midpoint + labelOffset;

            // Background quad
            var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgQuad.name = "LabelBackground";
            bgQuad.transform.SetParent(_labelObject.transform, false);
            bgQuad.transform.localPosition = Vector3.zero;

            float bgWidth = Mathf.Max(label.Length * LabelFontSize * 0.55f, 0.15f);
            float bgHeight = LabelFontSize * 1.6f;
            bgQuad.transform.localScale = new Vector3(bgWidth, bgHeight, 1f);

            var bgCollider = bgQuad.GetComponent<Collider>();
            if (bgCollider != null) Object.Destroy(bgCollider);

            var bgRenderer = bgQuad.GetComponent<MeshRenderer>();
            bgRenderer.material = CreateBackgroundMaterial();
            bgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bgRenderer.receiveShadows = false;

            // TextMesh
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_labelObject.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.001f);

            _textMesh = textGo.AddComponent<TextMesh>();
            _textMesh.text = label;
            _textMesh.characterSize = LabelFontSize * 0.1f;
            _textMesh.fontSize = 120;
            _textMesh.anchor = TextAnchor.MiddleCenter;
            _textMesh.alignment = TextAlignment.Center;
            _textMesh.color = LabelColor;

            var textRenderer = textGo.GetComponent<MeshRenderer>();
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        private void LateUpdate()
        {
            if (_labelObject == null) return;

            Camera cam = CameraUtil.GetMain();
            if (cam == null) return;

            // Billboard: face the camera
            Vector3 lookDir = cam.transform.position - _labelObject.transform.position;
            _labelObject.transform.rotation = Quaternion.LookRotation(lookDir);
        }

        /// <summary>
        /// Updates the end point of the line and repositions the label at the new midpoint.
        /// Used for live preview during drag-to-measure.
        /// </summary>
        public void UpdateEndpoint(Vector3 newEnd)
        {
            if (_line != null)
                _line.SetPosition(1, newEnd);

            if (_endMarker != null)
                _endMarker.transform.position = newEnd;

            if (_line != null && _labelObject != null)
            {
                Vector3 start = _line.GetPosition(0);
                Vector3 midpoint = (start + newEnd) * 0.5f;
                _labelObject.transform.position = midpoint + Vector3.up * 0.06f;
            }
        }

        /// <summary>
        /// Updates the label text (e.g. live distance readout during drag).
        /// </summary>
        public void UpdateLabel(string text)
        {
            if (_textMesh != null)
                _textMesh.text = text;

            // Resize background to match new text
            if (_labelObject != null)
            {
                var bgQuad = _labelObject.transform.Find("LabelBackground");
                if (bgQuad != null)
                {
                    float bgWidth = Mathf.Max(text.Length * LabelFontSize * 0.55f, 0.15f);
                    float bgHeight = LabelFontSize * 1.6f;
                    bgQuad.localScale = new Vector3(bgWidth, bgHeight, 1f);
                }
            }
        }

        // ── IAnchorLineVisual ──

        void IAnchorLineVisual.SetEndpoints(Vector3 start, Vector3 end)
        {
            if (_line != null)
            {
                _line.SetPosition(0, start);
                _line.SetPosition(1, end);
            }
            if (_startMarker != null)
                _startMarker.transform.position = start;
            if (_endMarker != null)
                _endMarker.transform.position = end;
            if (_labelObject != null)
            {
                Vector3 midpoint = (start + end) * 0.5f;
                _labelObject.transform.position = midpoint + Vector3.up * 0.06f;
            }
        }

        void IAnchorLineVisual.SetLabel(string text) => UpdateLabel(text);

        void IAnchorLineVisual.Cleanup() => Cleanup();

        public void Cleanup()
        {
            if (_startMarker != null) Destroy(_startMarker);
            if (_endMarker != null) Destroy(_endMarker);
            if (_labelObject != null) Destroy(_labelObject);
            Destroy(gameObject);
        }

        private static GameObject CreateEndpointMarker(Vector3 position, Color color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "MeasureEndpoint";
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * EndpointMarkerScale;

            var col = marker.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var rend = marker.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default")) { name = "EndpointMat" };
            mat.color = color;
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            return marker;
        }

        private static Material CreateLineMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader) { name = "MeasurementLine" };
            mat.color = color;
            return mat;
        }

        private static Material CreateBackgroundMaterial()
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader) { name = "MeasurementLabelBg" };
            mat.color = LabelBackgroundColor;
            return mat;
        }

        // --- Formatting helpers ---

        /// <summary>
        /// Formats a world-space distance (meters) into a display string using the given unit.
        /// </summary>
        public static string FormatDistance(float distanceMeters, string displayUnit)
        {
            float mm = distanceMeters * 1000f;
            return displayUnit switch
            {
                "inches" => $"{mm / 25.4f:F1}\"",
                "cm" => $"{mm / 10f:F1} cm",
                "ft" => $"{mm / 304.8f:F2} ft",
                _ => $"{mm:F0} mm"
            };
        }
    }
}
