using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// World-space curved arrow overlay showing a clockwise rotation arc around a target.
    /// Used for torque/wrench gestures to clearly communicate "rotate here".
    /// Billboards to camera. Pulses opacity. Fades as progress increases.
    /// </summary>
    public sealed class CurvedArrowOverlay : MonoBehaviour
    {
        private LineRenderer _arcLine;
        private LineRenderer _headLine;
        private Material _arcMat;
        private Material _headMat;
        private float _fade = 1f;

        private static readonly Color ArrowColor = new Color(0.3f, 1f, 0.6f, 0.9f);

        private const float ArcRadius = 0.07f;
        private const float ArcStartDeg = 120f;   // start angle (top-left)
        private const float ArcSweepDeg = -210f;   // clockwise sweep (negative = CW)
        private const int ArcSegments = 32;
        private const float HeadSize = 0.025f;
        private const float LineWidth = 0.008f;
        private const float HeadWidth = 0.007f;

        public static CurvedArrowOverlay Spawn(Vector3 worldPosition)
        {
            var go = new GameObject("CurvedArrowOverlay");
            go.transform.position = worldPosition + Vector3.up * 0.06f;

            var overlay = go.AddComponent<CurvedArrowOverlay>();
            overlay.Initialize();
            return overlay;
        }

        private void Initialize()
        {
            // Arc line
            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(transform, false);
            _arcLine = arcGo.AddComponent<LineRenderer>();
            _arcMat = ConfigureLine(_arcLine, LineWidth);

            // Arrowhead
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(transform, false);
            _headLine = headGo.AddComponent<LineRenderer>();
            _headMat = ConfigureLine(_headLine, HeadWidth);

            BuildArc();
            BuildArrowhead();
        }

        private void BuildArc()
        {
            _arcLine.positionCount = ArcSegments + 1;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float t = (float)i / ArcSegments;
                float angleDeg = ArcStartDeg + ArcSweepDeg * t;
                float rad = angleDeg * Mathf.Deg2Rad;
                _arcLine.SetPosition(i, new Vector3(
                    Mathf.Cos(rad) * ArcRadius,
                    Mathf.Sin(rad) * ArcRadius,
                    0f));
            }
        }

        private void BuildArrowhead()
        {
            // Arrowhead at the end of the arc (tip of the sweep)
            float endAngleDeg = ArcStartDeg + ArcSweepDeg;
            float endRad = endAngleDeg * Mathf.Deg2Rad;
            Vector3 tip = new Vector3(
                Mathf.Cos(endRad) * ArcRadius,
                Mathf.Sin(endRad) * ArcRadius,
                0f);

            // Tangent direction at the tip (perpendicular to radius, in sweep direction)
            // For CW sweep, tangent points clockwise
            Vector3 radialDir = tip.normalized;
            // Tangent is perpendicular to radial, in the direction of motion (CW = -cross with Z)
            Vector3 tangent = new Vector3(radialDir.y, -radialDir.x, 0f).normalized;
            // For CW sweep (negative), the tangent should point in the sweep direction
            if (ArcSweepDeg < 0f) tangent = -tangent;

            // Two barbs of the arrowhead
            Vector3 backCenter = tip - tangent * HeadSize;
            Vector3 perpendicular = new Vector3(-tangent.y, tangent.x, 0f);

            _headLine.positionCount = 3;
            _headLine.SetPosition(0, backCenter + perpendicular * HeadSize * 0.5f);
            _headLine.SetPosition(1, tip);
            _headLine.SetPosition(2, backCenter - perpendicular * HeadSize * 0.5f);
        }

        public void SetFade(float fade01)
        {
            _fade = Mathf.Clamp01(fade01);
        }

        private void LateUpdate()
        {
            // Billboard
            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);

            // Pulse + fade
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
            float alpha = ArrowColor.a * _fade * pulse;
            Color c = new Color(ArrowColor.r, ArrowColor.g, ArrowColor.b, alpha);

            if (_arcMat != null) _arcMat.color = c;
            if (_headMat != null) _headMat.color = c;
        }

        private static Material ConfigureLine(LineRenderer lr, float width)
        {
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.widthMultiplier = width;
            var mat = CreateOverlayMaterial(ArrowColor);
            lr.material = mat;
            lr.startColor = ArrowColor;
            lr.endColor = ArrowColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 10;
            return mat;
        }

        private static Material CreateOverlayMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 4000;
            mat.SetColor("_Color", color);
            return mat;
        }

        private void OnDestroy()
        {
            if (_arcMat != null) Destroy(_arcMat);
            if (_headMat != null) Destroy(_headMat);
        }
    }
}
