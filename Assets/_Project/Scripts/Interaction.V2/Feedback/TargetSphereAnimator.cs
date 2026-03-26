using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Pulses emission glow on tool target sphere renderers to draw user attention
    /// before they tap. Uses MaterialHelper.SetEmission with a sine wave.
    /// Start when a Use step activates with targets. Stop when preview begins or step completes.
    /// </summary>
    public sealed class TargetSphereAnimator
    {
        private Renderer[] _sphereRenderers;
        private bool _active;

        private static readonly Color LowGlow = new Color(0f, 0.6f, 0.9f, 1f) * 0.3f;
        private static readonly Color HighGlow = new Color(0f, 0.8f, 1f, 1f) * 1.2f;
        private const float PulsePeriod = 1.5f;

        /// <summary>
        /// Begin pulsing the given target sphere renderers.
        /// Pass renderers discovered via Physics.OverlapSphere or similar.
        /// </summary>
        public void Start(Renderer[] sphereRenderers)
        {
            _sphereRenderers = sphereRenderers;
            _active = sphereRenderers != null && sphereRenderers.Length > 0;
        }

        /// <summary>
        /// Discover target spheres near the given world positions and begin pulsing.
        /// Uses OverlapSphere to find SphereColliders near each position.
        /// </summary>
        public void StartAtPositions(Vector3[] targetPositions, float searchRadius = 0.5f)
        {
            if (targetPositions == null || targetPositions.Length == 0)
            {
                _active = false;
                return;
            }

            var renderers = new System.Collections.Generic.List<Renderer>();
            foreach (var pos in targetPositions)
            {
                var colliders = Physics.OverlapSphere(pos, searchRadius);
                foreach (var col in colliders)
                {
                    if (col is SphereCollider)
                    {
                        var r = col.GetComponent<Renderer>();
                        if (r != null && !renderers.Contains(r))
                            renderers.Add(r);
                    }
                }
            }

            Start(renderers.ToArray());
        }

        public void Stop()
        {
            if (!_active) return;
            _active = false;

            // Clear emission on all tracked renderers
            if (_sphereRenderers != null)
            {
                foreach (var r in _sphereRenderers)
                {
                    if (r != null && r.gameObject != null)
                        MaterialHelper.SetEmission(r.gameObject, Color.black);
                }
            }
            _sphereRenderers = null;
        }

        /// <summary>Call each frame to update the pulse animation.</summary>
        public void Tick()
        {
            if (!_active || _sphereRenderers == null) return;

            float t = (Mathf.Sin(Time.time * (2f * Mathf.PI / PulsePeriod)) + 1f) * 0.5f;
            Color emission = Color.Lerp(LowGlow, HighGlow, t);

            foreach (var r in _sphereRenderers)
            {
                if (r != null && r.gameObject != null)
                    MaterialHelper.SetEmission(r.gameObject, emission);
            }
        }

        public bool IsActive => _active;
    }
}
