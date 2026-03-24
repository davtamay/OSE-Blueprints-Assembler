using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Lightweight in-world arc used to show how a finished subassembly will dock
    /// from its current source pose to the active target pose.
    /// </summary>
    internal sealed class DockArcVisual : MonoBehaviour
    {
        private const int SegmentCount = 24;
        private const float BaseWidth = 0.012f;
        private const float MinArcHeight = 0.12f;
        private const float MaxArcHeight = 0.55f;
        private const float MinSideOffset = 0.05f;
        private const float MaxSideOffset = 0.22f;
        private const float MinMarkerScale = 0.035f;
        private const float MaxMarkerScale = 0.055f;
        private const float MinArrowHeadLength = 0.06f;
        private const float MaxArrowHeadLength = 0.12f;
        private const float ArrowSideFactor = 0.42f;

        private static readonly Color SourceColor = new Color(1.0f, 0.77f, 0.18f, 0.55f);
        private static readonly Color TargetColor = new Color(0.35f, 0.9f, 1.0f, 0.85f);

        private LineRenderer _line;
        private LineRenderer _arrowLeft;
        private LineRenderer _arrowRight;
        private GameObject _sourceMarker;
        private GameObject _targetMarker;
        private Material _lineMaterial;
        private Material _arrowLeftMaterial;
        private Material _arrowRightMaterial;

        public static DockArcVisual Spawn()
        {
            var go = new GameObject("DockArcVisual");
            var visual = go.AddComponent<DockArcVisual>();
            visual.Build();
            return visual;
        }

        public void SetArc(Vector3 start, Vector3 end, Vector3 sourceUp, Vector3 targetUp, float emphasis)
        {
            if (_line == null)
                Build();

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            Vector3 direction = end - start;
            float distance = direction.magnitude;
            Vector3 forward = distance > 0.0001f ? direction / distance : Vector3.forward;

            Vector3 planeNormal = sourceUp + targetUp;
            if (planeNormal.sqrMagnitude < 0.0001f)
                planeNormal = Vector3.up;

            Vector3 lateral = Vector3.Cross(planeNormal.normalized, forward);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.Cross(Vector3.up, forward);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.right;
            lateral.Normalize();

            float arcHeight = Mathf.Clamp(distance * 0.26f, MinArcHeight, MaxArcHeight);
            float sideOffset = Mathf.Clamp(distance * 0.12f, MinSideOffset, MaxSideOffset);
            Vector3 control = (start + end) * 0.5f + Vector3.up * arcHeight + lateral * sideOffset;
            _line.positionCount = SegmentCount;

            for (int i = 0; i < SegmentCount; i++)
            {
                float t = i / (SegmentCount - 1f);
                _line.SetPosition(i, QuadraticBezier(start, control, end, t));
            }

            float pulse = Mathf.Lerp(0.7f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * 3f));
            float alphaScale = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(emphasis)) * pulse;
            float width = Mathf.Lerp(BaseWidth * 0.9f, BaseWidth * 1.75f, Mathf.Clamp01(emphasis));

            Color startColor = new Color(SourceColor.r, SourceColor.g, SourceColor.b, SourceColor.a * alphaScale);
            Color endColor = new Color(TargetColor.r, TargetColor.g, TargetColor.b, TargetColor.a * alphaScale);

            _line.startWidth = width;
            _line.endWidth = width * 0.9f;
            _line.startColor = startColor;
            _line.endColor = endColor;
            if (_lineMaterial != null)
                _lineMaterial.color = Color.Lerp(startColor, endColor, 0.5f);

            float sourceMarkerScale = Mathf.Lerp(MinMarkerScale, MaxMarkerScale, Mathf.Clamp01(emphasis));
            float targetMarkerScale = Mathf.Lerp(MinMarkerScale * 1.1f, MaxMarkerScale * 1.2f, Mathf.Clamp01(emphasis));
            UpdateMarker(_sourceMarker, start, startColor, sourceMarkerScale);
            UpdateMarker(_targetMarker, end, endColor, targetMarkerScale);
            UpdateArrowLines(end, forward, lateral, width, endColor, 0f);
        }

        public void SetLinearGuide(Vector3 start, Vector3 end, Vector3 sourceUp, Vector3 targetUp, float emphasis)
        {
            if (_line == null)
                Build();

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            Vector3 direction = end - start;
            float distance = direction.magnitude;
            Vector3 forward = distance > 0.0001f ? direction / distance : Vector3.right;

            Vector3 lateral = Vector3.Cross(targetUp.sqrMagnitude > 0.0001f ? targetUp.normalized : Vector3.up, forward);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.Cross(sourceUp.sqrMagnitude > 0.0001f ? sourceUp.normalized : Vector3.up, forward);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.Cross(Vector3.up, forward);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.forward;
            lateral.Normalize();

            _line.positionCount = 2;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);

            float pulse = Mathf.Lerp(0.7f, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * 4f));
            float emphasis01 = Mathf.Clamp01(emphasis);
            float alphaScale = Mathf.Lerp(0.45f, 1f, emphasis01) * pulse;
            float width = Mathf.Lerp(BaseWidth * 1.05f, BaseWidth * 1.95f, emphasis01);

            Color startColor = new Color(SourceColor.r, SourceColor.g, SourceColor.b, SourceColor.a * alphaScale);
            Color endColor = new Color(TargetColor.r, TargetColor.g, TargetColor.b, TargetColor.a * alphaScale);

            _line.startWidth = width;
            _line.endWidth = width * 0.95f;
            _line.startColor = startColor;
            _line.endColor = endColor;
            if (_lineMaterial != null)
                _lineMaterial.color = Color.Lerp(startColor, endColor, 0.65f);

            float sourceMarkerScale = Mathf.Lerp(MinMarkerScale, MaxMarkerScale, emphasis01);
            float targetMarkerScale = Mathf.Lerp(MinMarkerScale * 1.1f, MaxMarkerScale * 1.25f, emphasis01);
            UpdateMarker(_sourceMarker, start, startColor, sourceMarkerScale);
            UpdateMarker(_targetMarker, end, endColor, targetMarkerScale);

            float arrowHeadLength = Mathf.Clamp(distance * 0.22f, MinArrowHeadLength, MaxArrowHeadLength);
            UpdateArrowLines(end, -forward, lateral, width, endColor, arrowHeadLength);
        }

        public void Hide()
        {
            if (gameObject != null)
            {
                if (_arrowLeft != null)
                    _arrowLeft.gameObject.SetActive(false);
                if (_arrowRight != null)
                    _arrowRight.gameObject.SetActive(false);
                gameObject.SetActive(false);
            }
        }

        public void Cleanup()
        {
            if (_sourceMarker != null)
                Destroy(_sourceMarker);
            if (_targetMarker != null)
                Destroy(_targetMarker);

            if (_line != null && _line.material != null)
                Destroy(_line.material);
            if (_arrowLeft != null && _arrowLeft.material != null)
                Destroy(_arrowLeft.material);
            if (_arrowRight != null && _arrowRight.material != null)
                Destroy(_arrowRight.material);

            Destroy(gameObject);
        }

        private void Build()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = SegmentCount;
            _line.alignment = LineAlignment.View;
            _line.numCornerVertices = 8;
            _line.numCapVertices = 6;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            _lineMaterial = new Material(shader) { name = "DockArcLine" };
            _line.material = _lineMaterial;
            _arrowLeft = CreateAuxLineRenderer("DockArcArrowLeft", shader, out _arrowLeftMaterial);
            _arrowRight = CreateAuxLineRenderer("DockArcArrowRight", shader, out _arrowRightMaterial);

            _sourceMarker = CreateMarker("DockArcSource");
            _targetMarker = CreateMarker("DockArcTarget");
        }

        private static Vector3 QuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            float u = 1f - t;
            return (u * u * start) + (2f * u * t * control) + (t * t * end);
        }

        private static GameObject CreateMarker(string name)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            renderer.material = new Material(shader) { name = $"{name}Mat" };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return marker;
        }

        private static void UpdateMarker(GameObject marker, Vector3 position, Color color, float scale)
        {
            if (marker == null)
                return;

            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * scale;

            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
                renderer.material.color = color;
        }

        private LineRenderer CreateAuxLineRenderer(string name, Shader shader, out Material material)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);

            LineRenderer renderer = child.AddComponent<LineRenderer>();
            renderer.useWorldSpace = true;
            renderer.positionCount = 2;
            renderer.alignment = LineAlignment.View;
            renderer.numCornerVertices = 4;
            renderer.numCapVertices = 2;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            material = new Material(shader) { name = $"{name}Mat" };
            renderer.material = material;
            return renderer;
        }

        private void UpdateArrowLines(Vector3 end, Vector3 backward, Vector3 lateral, float width, Color color, float arrowHeadLength)
        {
            if (_arrowLeft == null || _arrowRight == null)
                return;

            float arrowWidth = Mathf.Max(width * 0.72f, 0.004f);
            _arrowLeft.startWidth = arrowWidth;
            _arrowLeft.endWidth = arrowWidth * 0.4f;
            _arrowRight.startWidth = arrowWidth;
            _arrowRight.endWidth = arrowWidth * 0.4f;

            _arrowLeft.startColor = color;
            _arrowLeft.endColor = color;
            _arrowRight.startColor = color;
            _arrowRight.endColor = color;

            if (_arrowLeftMaterial != null)
                _arrowLeftMaterial.color = color;
            if (_arrowRightMaterial != null)
                _arrowRightMaterial.color = color;

            if (arrowHeadLength <= 0.0001f)
            {
                _arrowLeft.gameObject.SetActive(false);
                _arrowRight.gameObject.SetActive(false);
                return;
            }

            _arrowLeft.gameObject.SetActive(true);
            _arrowRight.gameObject.SetActive(true);

            Vector3 wing = lateral * (arrowHeadLength * ArrowSideFactor);
            Vector3 arrowBase = end + (backward.normalized * arrowHeadLength);

            _arrowLeft.SetPosition(0, end);
            _arrowLeft.SetPosition(1, arrowBase + wing);

            _arrowRight.SetPosition(0, end);
            _arrowRight.SetPosition(1, arrowBase - wing);
        }
    }
}
