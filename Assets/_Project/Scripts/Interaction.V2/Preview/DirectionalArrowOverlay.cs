using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// World-space directional arrow at a tool target showing the expected drag direction.
    /// Pulses opacity on a sine curve. Fades out as progress increases.
    /// Billboards to camera. Uses LineRenderer (same pattern as GestureProgressVisual).
    /// </summary>
    public sealed class DirectionalArrowOverlay : MonoBehaviour
    {
        private LineRenderer _shaftLine;
        private LineRenderer _headLine;
        private Material _shaftMat;
        private Material _headMat;
        private float _fade = 1f;

        private static readonly Color ArrowColor = new Color(0.3f, 1f, 0.6f, 0.9f);
        private const float ShaftLength = 0.1f;
        private const float HeadSize = 0.03f;

        public static DirectionalArrowOverlay Spawn(Vector3 worldPosition, Vector2 screenDir)
        {
            var go = new GameObject("DirectionalArrowOverlay");
            go.transform.position = worldPosition + Vector3.up * 0.06f;

            var overlay = go.AddComponent<DirectionalArrowOverlay>();
            overlay.Initialize(screenDir);
            return overlay;
        }

        private void Initialize(Vector2 screenDir)
        {
            if (screenDir.sqrMagnitude < 0.001f)
                screenDir = Vector2.right;
            screenDir.Normalize();

            // Shaft
            var shaftGo = new GameObject("Shaft");
            shaftGo.transform.SetParent(transform, false);
            _shaftLine = shaftGo.AddComponent<LineRenderer>();
            _shaftMat = ConfigureLine(_shaftLine, 0.008f);

            // Arrow head (two lines forming a V)
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(transform, false);
            _headLine = headGo.AddComponent<LineRenderer>();
            _headMat = ConfigureLine(_headLine, 0.006f);

            // Convert screen-space direction to local-space (billboard will face camera)
            // In local space: X = right, Y = up, Z = forward (toward camera)
            Vector3 localDir = new Vector3(screenDir.x, screenDir.y, 0f).normalized;
            Vector3 tip = localDir * ShaftLength;

            // Shaft: center to tip
            _shaftLine.positionCount = 2;
            _shaftLine.SetPosition(0, Vector3.zero);
            _shaftLine.SetPosition(1, tip);

            // Arrowhead: two angled lines from near-tip
            Vector3 perpendicular = new Vector3(-localDir.y, localDir.x, 0f);
            Vector3 headBase = localDir * (ShaftLength - HeadSize);
            _headLine.positionCount = 5;
            _headLine.SetPosition(0, headBase + perpendicular * HeadSize * 0.6f);
            _headLine.SetPosition(1, tip);
            _headLine.SetPosition(2, headBase - perpendicular * HeadSize * 0.6f);
            // Close back for a filled look
            _headLine.SetPosition(3, tip);
            _headLine.SetPosition(4, headBase + perpendicular * HeadSize * 0.6f);
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

            if (_shaftMat != null) _shaftMat.color = c;
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
            if (_shaftMat != null) Destroy(_shaftMat);
            if (_headMat != null) Destroy(_headMat);
        }
    }
}
