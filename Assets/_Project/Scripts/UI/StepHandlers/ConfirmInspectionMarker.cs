using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// A small floating sphere indicator that marks an inspection target for Confirm-family steps.
    /// Spawned above (not on) the target surface so it draws attention without obstructing the view.
    /// Changes from cyan-green to solid green once the camera has framed this target.
    /// </summary>
    internal sealed class ConfirmInspectionMarker : MonoBehaviour
    {
        // Shared materials — created once, ref-counted, destroyed when last marker is gone
        private static Material _pendingMat;
        private static Material _observedMat;
        private static int _instanceCount;

        private static readonly Color PendingColor  = new Color(0.3f, 1.0f, 0.8f, 0.85f);
        private static readonly Color ObservedColor = new Color(0.2f, 0.9f, 0.3f, 0.9f);

        private MeshRenderer _renderer;
        private bool _observed;
        private float _phase;

        // Float height above the authored target position (metres)
        private const float FloatHeight  = 0.18f;
        // Sphere diameter (metres)
        private const float SphereScale  = 0.035f;
        // Pulse amplitude (±fraction of base scale)
        private const float PulseAmp     = 0.12f;
        private const float PulsePeriod  = 1.4f;

        internal string  TargetId     { get; private set; }
        internal Vector3 WorldPosition { get; private set; }

        /// <summary>Spawns a marker floating above <paramref name="targetWorldPos"/>.</summary>
        internal static ConfirmInspectionMarker Spawn(Vector3 targetWorldPos, string targetId)
        {
            EnsureMaterials();
            _instanceCount++;

            var go = new GameObject($"[ConfirmMarker] {targetId}");
            go.hideFlags = HideFlags.DontSave;

            Vector3 floatPos = targetWorldPos + Vector3.up * FloatHeight;
            go.transform.position = floatPos;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * SphereScale;
            Object.Destroy(sphere.GetComponent<Collider>());

            var marker = go.AddComponent<ConfirmInspectionMarker>();
            marker._renderer          = sphere.GetComponent<MeshRenderer>();
            marker._renderer.sharedMaterial = _pendingMat;
            marker.TargetId           = targetId;
            marker.WorldPosition      = floatPos;

            return marker;
        }

        /// <summary>Switches the marker to "observed" state — solid green, no pulse.</summary>
        internal void SetObserved()
        {
            if (_observed) return;
            _observed = true;
            if (_renderer != null)
                _renderer.sharedMaterial = _observedMat;
            transform.GetChild(0).localScale = Vector3.one * SphereScale;
        }

        private void Update()
        {
            if (_observed) return;
            _phase += Time.deltaTime * (Mathf.PI * 2f / PulsePeriod);
            float s = SphereScale * (1f + PulseAmp * Mathf.Sin(_phase));
            transform.GetChild(0).localScale = Vector3.one * s;
        }

        private void OnDestroy()
        {
            _instanceCount--;
            if (_instanceCount <= 0)
            {
                _instanceCount = 0;
                DestroyMaterials();
            }
        }

        private static void EnsureMaterials()
        {
            if (_pendingMat != null) return;
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            _pendingMat  = new Material(shader) { color = PendingColor,  hideFlags = HideFlags.DontSave };
            _observedMat = new Material(shader) { color = ObservedColor, hideFlags = HideFlags.DontSave };
        }

        private static void DestroyMaterials()
        {
            if (_pendingMat  != null) { Object.Destroy(_pendingMat);  _pendingMat  = null; }
            if (_observedMat != null) { Object.Destroy(_observedMat); _observedMat = null; }
        }
    }
}
