using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Renders a cable-style line between two world-space points.
    /// Thicker than a measurement line, no label, colored per cable type.
    /// Designed as a drop-in <see cref="IAnchorLineVisual"/> for cable connection steps.
    ///
    /// <para>Future: swap the LineRenderer for a SplineExtrude mesh or physics rope
    /// by creating a new <see cref="IAnchorLineVisual"/> implementation.</para>
    /// </summary>
    internal sealed class CableLineVisual : MonoBehaviour, IAnchorLineVisual
    {
        private const float DefaultWidth = 0.025f;

        private LineRenderer _line;
        private GameObject _startMarker;
        private GameObject _endMarker;

        /// <summary>
        /// Spawns a cable line visual between two world-space points.
        /// </summary>
        public static CableLineVisual Spawn(Vector3 start, Vector3 end, Color color, float width = DefaultWidth)
        {
            var go = new GameObject("CableLineVisual");
            var vis = go.AddComponent<CableLineVisual>();
            vis.Build(start, end, color, width);
            return vis;
        }

        private void Build(Vector3 start, Vector3 end, Color color, float width)
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            _line.startWidth = width;
            _line.endWidth = width;
            _line.material = CreateMaterial(color);
            _line.startColor = color;
            _line.endColor = color;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.numCornerVertices = 4;
            _line.numCapVertices = 4;

            _startMarker = CreateEndpointMarker(start, color);
            _endMarker = CreateEndpointMarker(end, color);
        }

        // ── IAnchorLineVisual ──

        public void SetEndpoints(Vector3 start, Vector3 end)
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
        }

        public void SetLabel(string text)
        {
            // Cables don't show labels — intentional no-op.
        }

        public void Cleanup()
        {
            if (_startMarker != null) Destroy(_startMarker);
            if (_endMarker != null) Destroy(_endMarker);
            Destroy(gameObject);
        }

        // ── Helpers ──

        private static GameObject CreateEndpointMarker(Vector3 position, Color color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "CableEndpoint";
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.04f;

            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = marker.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Sprites/Default")) { name = "CableEndpointMat" };
            mat.color = color;
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            return marker;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            var mat = new Material(shader) { name = "CableLine" };
            mat.color = color;
            return mat;
        }
    }
}
