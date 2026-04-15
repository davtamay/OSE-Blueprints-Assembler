using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Spawns a particle prefab at each target GameObject's pose and parents
    /// it to the target so it follows transforms (including transient
    /// anim-group shake / rotate). Prefab path lives on
    /// <see cref="OSE.Content.AnimationCueEntry.particlePrefabRef"/>; the
    /// content layer resolves it via the spawner's package-asset loader.
    /// Effect lifetime: parented to the target, destroyed on Stop or when
    /// duration elapses (use 0 for indefinite — coordinator's Cleanup will
    /// stop it on next step).
    /// </summary>
    public sealed class ParticlePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "particle";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private GameObject[] _instances;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            string prefabRef = context.Entry?.particlePrefabRef;
            if (string.IsNullOrEmpty(prefabRef) || context.Targets == null || context.Targets.Count == 0)
            {
                IsPlaying = false;
                return;
            }

            // Resources.Load convention: authors place particle prefabs under
            // any Assets/.../Resources/ folder and reference them by their
            // path relative to that Resources root (no extension). Unity's
            // resource lookup walks every Resources folder, so packaged
            // effects ship without explicit registration. If the load fails
            // we still create an empty parented GameObject so the cue
            // lifecycle (Stop / cleanup) is consistent.
            GameObject prefab = Resources.Load<GameObject>(prefabRef);
            _instances = new GameObject[context.Targets.Count];

            for (int i = 0; i < context.Targets.Count; i++)
            {
                var host = context.Targets[i];
                if (host == null) continue;

                GameObject instance = prefab != null
                    ? Object.Instantiate(prefab, host.transform)
                    : new GameObject($"Particle_{prefabRef}");

                if (prefab == null)
                    instance.transform.SetParent(host.transform, false);

                // For group hosts (Group_*) the host pivot is at PreviewRoot
                // origin, not at the geometric centre — drop the particle at
                // the centroid of the host's active children so the effect
                // appears WHERE the parts are. For non-group hosts the host
                // is the part itself; centroid is identity.
                instance.transform.localPosition = ComputeChildrenCentroidLocal(host.transform);
                instance.transform.localRotation = Quaternion.identity;

                var ps = instance.GetComponentInChildren<ParticleSystem>();
                if (ps != null) ps.Play(true);

                _instances[i] = instance;
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;
            if (_ctx.Duration > 0f && _elapsed >= _ctx.Duration)
            {
                Stop();
                return false;
            }
            return true;
        }

        private static Vector3 ComputeChildrenCentroidLocal(Transform root)
        {
            if (root == null || root.childCount == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                sum += c.localPosition;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        public void Stop()
        {
            if (_instances != null)
            {
                for (int i = 0; i < _instances.Length; i++)
                {
                    if (_instances[i] != null) Object.Destroy(_instances[i]);
                }
                _instances = null;
            }
            IsPlaying = false;
        }
    }
}
