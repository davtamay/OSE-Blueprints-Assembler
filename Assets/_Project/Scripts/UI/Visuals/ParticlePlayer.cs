using UnityEngine;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Plays a "particle" animation cue. Two source modes:
    ///   "preset"  — instantiates a procedural preset from
    ///               <see cref="CompletionParticleEffect"/> (torque_sparks,
    ///               weld_arc, weld_glow, square_confirm).
    ///   "prefab"  — Resources.Load the path in <c>particlePrefabRef</c>.
    /// In both modes the instance is parented to the host so it follows
    /// animation cues running on the same host (shake / rotate / pose).
    /// Optional <c>particleScale</c> and <c>particleColorTint</c> apply on
    /// top of the authored system. Graceful stop: emission halts and
    /// in-flight particles fade naturally via <see cref="ParticleAutoDestroy"/>.
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

            var entry = context.Entry;
            if (entry == null || context.Targets == null || context.Targets.Count == 0)
            {
                IsPlaying = false;
                return;
            }

            string mode = ResolveSourceMode(entry);
            float scale = entry.particleScale > 0f ? entry.particleScale : 1f;

            GameObject prefab = null;
            if (mode == "prefab")
            {
                if (string.IsNullOrEmpty(entry.particlePrefabRef))
                {
                    IsPlaying = false;
                    return;
                }
                prefab = Resources.Load<GameObject>(entry.particlePrefabRef);
            }
            else if (string.IsNullOrEmpty(entry.particlePresetId))
            {
                IsPlaying = false;
                return;
            }

            _instances = new GameObject[context.Targets.Count];

            for (int i = 0; i < context.Targets.Count; i++)
            {
                var host = context.Targets[i];
                if (host == null) continue;

                // Use the caller-provided pivot hint (PivotCentroidResolver,
                // shared with rotation cues + authoring) when present. Hint
                // applies to target[0] only — the same contract as other
                // subassembly-hosted cue players. Falls back to zero (host
                // origin) for part/tool hosts or when no body centroid is
                // meaningful on this step.
                Vector3 pivot = (i == 0 && context.PivotHintLocal.HasValue)
                    ? context.PivotHintLocal.Value
                    : Vector3.zero;
                if (entry.pivotOffsetOverride)
                {
                    pivot += new Vector3(entry.pivotOffset.x, entry.pivotOffset.y, entry.pivotOffset.z);
                }

                GameObject instance = mode == "preset"
                    ? SpawnPreset(host.transform, pivot, entry.particlePresetId, scale)
                    : SpawnPrefab(host.transform, pivot, prefab, entry.particlePrefabRef, scale);

                if (instance != null)
                {
                    ApplyColorTint(instance, entry.particleColorTint);
                    ForceLoopInEditor(instance);
                    _instances[i] = instance;
                }
            }
        }

        /// <summary>
        /// In edit-mode preview, force every child ParticleSystem to loop
        /// so burst-style presets (torque_sparks, weld_glow, square_confirm)
        /// keep re-emitting during the preview instead of firing once and
        /// going static. Runtime (play mode) honours the preset's authored
        /// loop flag unchanged.
        /// </summary>
        private static void ForceLoopInEditor(GameObject instance)
        {
            if (Application.isPlaying || instance == null) return;
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (systems == null) return;
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                main.loop = true;
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;

            // In edit mode Unity does NOT auto-advance ParticleSystems —
            // drive simulation manually so authors can audition the effect.
            // Use restart=false + deltaTime so the simulation progresses
            // continuously: burst presets (torque_sparks, weld_glow,
            // square_confirm) that were authored with loop=false get
            // forced to loop=true in Start() for preview, so they keep
            // emitting while the cue is active instead of playing once
            // and going static. Runtime (Application.isPlaying) leaves
            // Unity's normal simulation alone.
            if (!Application.isPlaying && _instances != null)
            {
                for (int i = 0; i < _instances.Length; i++)
                {
                    var go = _instances[i];
                    if (go == null) continue;
                    var rootPs = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>();
                    if (rootPs != null)
                        rootPs.Simulate(deltaTime, withChildren: true, restart: false, fixedTimeStep: false);
                }
            }

            if (_ctx.Duration > 0f && _elapsed >= _ctx.Duration)
            {
                if (_ctx.Entry != null && _ctx.Entry.loop)
                {
                    // Restart loop: tear down + re-spawn so the burst re-fires.
                    var ctx = _ctx;
                    Stop();
                    Start(ctx);
                    return true;
                }
                Stop();
                return false;
            }
            return true;
        }

        public void Stop()
        {
            if (_instances != null)
            {
                for (int i = 0; i < _instances.Length; i++)
                {
                    var go = _instances[i];
                    if (go == null) continue;

                    // Destroy immediately. Previous version let particles fade
                    // via Stop(StopEmitting) + ParticleAutoDestroy, but that
                    // leaked the GO for 1–2 s during which rapid step-switches
                    // or preview clicks accumulated orphan ParticleEffect_*
                    // GameObjects in the scene. Immediate destroy keeps the
                    // scene clean; the cost is no fade-out on cue stop, which
                    // is acceptable for ephemeral preview effects.
                    if (Application.isPlaying) Object.Destroy(go);
                    else                       Object.DestroyImmediate(go);
                }
                _instances = null;
            }
            IsPlaying = false;

            // Belt-and-suspenders sweep: any ParticleEffect_* GO that escaped
            // tracking (e.g. orphan from an earlier aborted Start) is cleaned
            // up on any Stop. Cheap — one scene scan per cue teardown.
            SweepOrphanParticleGOs();
        }

        private static void SweepOrphanParticleGOs()
        {
            var all = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                var ps = all[i];
                if (ps == null) continue;
                var go = ps.gameObject;
                // Only target GOs we spawned via CompletionParticleEffect —
                // their name starts with "ParticleEffect_". Anything else
                // belongs to the scene / other systems.
                if (!go.name.StartsWith("ParticleEffect_", System.StringComparison.Ordinal)) continue;
                // Skip if still playing (legitimate active cue).
                if (ps.isPlaying && ps.IsAlive(true)) continue;
                if (Application.isPlaying) Object.Destroy(go);
                else                       Object.DestroyImmediate(go);
            }
        }

        private static string ResolveSourceMode(AnimationCueEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.particleSourceMode))
                return entry.particleSourceMode;
            return !string.IsNullOrEmpty(entry.particlePresetId) ? "preset" : "prefab";
        }

        // Reference size the procedural presets feel right at — tool-marker
        // scale, ~6 cm. Matches WeldPreview's Vector3.one * 0.06f spawn hint.
        // The particleScale field multiplies this reference.
        private const float PresetReferenceScale = 0.06f;

        private static GameObject SpawnPreset(Transform host, Vector3 localPivot, string presetId, float scale)
        {
            Vector3 worldPos = host.TransformPoint(localPivot);

            // Configure the preset at WeldPreview's reference size (0.06 m).
            // At particleScale = 1 the effect looks exactly like weld_arc
            // does today. The slider scales the whole transform hierarchy
            // on top — shrinking / growing per-particle size, speed, and
            // emission radius together.
            GameObject instance = CompletionParticleEffect.TrySpawnContinuous(
                presetId, worldPos, Vector3.one * PresetReferenceScale);
            if (instance == null) return null;

            // Don't parent — mirror the legacy tool-preview behavior
            // (WeldPreview / DrillPreview / TorquePreview all spawn at a
            // world position and never parent). Keeps emission direction
            // world-aligned and isolates the effect from host transform.
            instance.transform.rotation = Quaternion.identity;

            // Uniform hierarchy scale so size/speed/gravity/radius all
            // track the slider together. Default scalingMode is Local,
            // which only affects startSize — that's why small slider
            // values still looked large (speeds and emission radius
            // stayed at the preset's authored value).
            instance.transform.localScale = Vector3.one * scale;
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int s = 0; s < systems.Length; s++)
            {
                var m = systems[s].main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            return instance;
        }

        private static GameObject SpawnPrefab(Transform host, Vector3 localPivot, GameObject prefab, string prefabRef, float scale)
        {
            GameObject instance = prefab != null
                ? Object.Instantiate(prefab, host)
                : new GameObject($"Particle_{prefabRef}");

            if (prefab == null)
                instance.transform.SetParent(host, false);

            instance.transform.localPosition = localPivot;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;

            var ps = instance.GetComponentInChildren<ParticleSystem>();
            if (ps != null) ps.Play(true);
            return instance;
        }

        private static void ApplyColorTint(GameObject root, SceneFloat4 tint)
        {
            if (tint.a <= 0f) return;

            Color tintColor = new Color(tint.r, tint.g, tint.b, tint.a);
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                Color current = main.startColor.color;
                main.startColor = new ParticleSystem.MinMaxGradient(current * tintColor);
            }
        }

    }
}
