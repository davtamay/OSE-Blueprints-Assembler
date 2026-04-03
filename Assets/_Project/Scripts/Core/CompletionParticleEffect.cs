using System.Collections.Generic;
using UnityEngine;

namespace OSE.Core
{
    /// <summary>
    /// Spawns a self-destructing ParticleSystem at a world position based on a named preset.
    /// Presets are built procedurally — no prefab files required.
    /// Content authors reference presets by ID in the feedback payload
    /// (e.g. "torque_sparks", "weld_glow").
    /// </summary>
    public static class CompletionParticleEffect
    {
        private delegate void ConfigureParticle(ParticleSystem ps, Vector3 scale);

        private static readonly Dictionary<string, ConfigureParticle> Presets = new()
        {
            ["torque_sparks"] = ConfigureTorqueSparks,
            ["weld_glow"] = ConfigureWeldGlow,
            ["weld_arc"] = ConfigureWeldArc,
        };

        /// <summary>
        /// Spawns a particle effect at <paramref name="worldPosition"/> using the named preset.
        /// Returns false if the preset ID is unknown.
        /// </summary>
        public static bool TrySpawn(string presetId, Vector3 worldPosition, Vector3 baseScale)
        {
            if (string.IsNullOrEmpty(presetId))
                return false;

            if (!Presets.TryGetValue(presetId, out var configure))
            {
                OseLog.Warn(OseErrorCode.ContentLoadFailed, $"[CompletionParticleEffect] Unknown preset '{presetId}'.");
                return false;
            }

            GameObject go = new GameObject($"ParticleEffect_{presetId}");
            go.transform.position = worldPosition;

            var ps = go.AddComponent<ParticleSystem>();

            // Stop default playback so we can configure first
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            configure(ps, baseScale);

            // Self-destruct after the particle system finishes
            var autoDestroy = go.AddComponent<ParticleAutoDestroy>();
            autoDestroy.Init(ps);

            ps.Play();
            return true;
        }

        /// <summary>
        /// Spawns a continuous particle effect and returns the <see cref="GameObject"/>
        /// so the caller can reposition it each frame. Call
        /// <c>ps.Stop(true, ParticleSystemStopBehavior.StopEmitting)</c> when done —
        /// <see cref="ParticleAutoDestroy"/> handles cleanup after the last particle expires.
        /// Returns null if the preset ID is unknown.
        /// </summary>
        public static GameObject TrySpawnContinuous(string presetId, Vector3 worldPosition, Vector3 baseScale)
        {
            if (string.IsNullOrEmpty(presetId))
                return null;

            if (!Presets.TryGetValue(presetId, out var configure))
            {
                OseLog.Warn(OseErrorCode.ContentLoadFailed, $"[CompletionParticleEffect] Unknown preset '{presetId}'.");
                return null;
            }

            GameObject go = new GameObject($"ParticleEffect_{presetId}");
            go.transform.position = worldPosition;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            configure(ps, baseScale);

            var autoDestroy = go.AddComponent<ParticleAutoDestroy>();
            autoDestroy.Init(ps);

            ps.Play();
            return go;
        }

        // ── Presets ──────────────────────────────────────────────────────

        private static void ConfigureTorqueSparks(ParticleSystem ps, Vector3 scale)
        {
            float radius = Mathf.Max(scale.x, scale.z) * 0.5f;

            var main = ps.main;
            main.duration = 0.4f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new Color(1f, 0.85f, 0.3f, 1f); // warm yellow-orange
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 30;

            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 15, 25) });
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.3f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial();
        }

        // ── Weld presets (burst + continuous) ─────────────────────────────

        private static void ConfigureWeldGlow(ParticleSystem ps, Vector3 scale)
        {
            ConfigureWeldLayers(ps, scale, continuous: false);
        }

        private static void ConfigureWeldArc(ParticleSystem ps, Vector3 scale)
        {
            ConfigureWeldLayers(ps, scale, continuous: true);
        }

        /// <summary>
        /// Builds a 5-layer realistic welding effect: arc core (root), spark shower,
        /// spatter droplets, heat glow, and smoke wisps.
        /// When <paramref name="continuous"/> is true, layers loop with rateOverTime
        /// (for use during the weld animation). Otherwise layers use bursts (for completion).
        /// </summary>
        private static void ConfigureWeldLayers(ParticleSystem ps, Vector3 scale, bool continuous)
        {
            float radius = Mathf.Max(scale.x, scale.z) * 0.5f;
            Material sharedMat = CreateParticleMaterial();

            // ── Layer 1: Arc Core (root PS) ─ intense white-blue flickers ──
            ConfigureArcCore(ps, radius, continuous, sharedMat);

            // ── Layer 2: Spark Shower ─ the hero layer ─────────────────────
            var sparkPs = CreateChildSystem(ps.gameObject, "WeldSparks", sharedMat);
            ConfigureSparkShower(sparkPs, radius, continuous, sharedMat);

            // ── Layer 3: Spatter Droplets ──────────────────────────────────
            var spatterPs = CreateChildSystem(ps.gameObject, "WeldSpatter", sharedMat);
            ConfigureSpatter(spatterPs, radius, continuous, sharedMat);

            // ── Layer 4: Heat Glow ─────────────────────────────────────────
            var glowPs = CreateChildSystem(ps.gameObject, "WeldHeatGlow", sharedMat);
            ConfigureHeatGlow(glowPs, radius, continuous, sharedMat);

            // ── Layer 5: Smoke Wisps ───────────────────────────────────────
            var smokePs = CreateChildSystem(ps.gameObject, "WeldSmoke", sharedMat);
            ConfigureSmokeWisps(smokePs, radius, continuous, sharedMat);
        }

        // ── Layer 1: Arc Core ─────────────────────────────────────────────

        private static void ConfigureArcCore(ParticleSystem ps, float radius, bool continuous, Material mat)
        {
            var main = ps.main;
            main.duration = continuous ? 0f : 1.2f;
            main.loop = continuous;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new Color(0.85f, 0.92f, 1f, 1f); // blue-white
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 15;

            var emission = ps.emission;
            emission.enabled = true;
            if (continuous)
            {
                emission.rateOverTime = 40f;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 8, 12),
                    new ParticleSystem.Burst(0.1f, 5, 8),
                    new ParticleSystem.Burst(0.3f, 3, 5),
                });
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(radius * 0.05f, 0.002f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.7f, 0.85f, 1f), 0.4f),
                    new GradientColorKey(new Color(0.4f, 0.6f, 1f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.3f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.3f, 2f), new Keyframe(1f, 0f)));

            // Rotate arc flickers so they don't look like aligned squares
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
        }

        // ── Layer 2: Spark Shower ─────────────────────────────────────────

        private static void ConfigureSparkShower(ParticleSystem ps, float radius, bool continuous, Material mat)
        {
            var main = ps.main;
            main.duration = continuous ? 0f : 1.0f;
            main.loop = continuous;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 3.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new Color(1f, 0.95f, 0.8f, 1f); // white-hot
            main.gravityModifier = 2.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.enabled = true;
            if (continuous)
            {
                emission.rateOverTime = 60f;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 30, 50),
                    new ParticleSystem.Burst(0.15f, 15, 25),
                    new ParticleSystem.Burst(0.4f, 10, 15),
                });
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 45f;
            shape.radius = Mathf.Max(radius * 0.02f, 0.001f);
            shape.rotation = new Vector3(-90f, 0f, 0f); // cone points up, sparks ejected up then fall

            // Color: white-hot → yellow → orange → dark red (cooling metal)
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.9f, 0.3f), 0.2f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.5f),
                    new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 0.85f),
                    new GradientColorKey(new Color(0.4f, 0.1f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            // Shrink as they cool
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

            // Decelerate (air drag simulation)
            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.4f)));

            // Turbulence for natural scatter
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.3f;
            noise.frequency = 3f;
            noise.scrollSpeed = 1f;
            noise.damping = true;
            noise.octaveCount = 1;

            // Spark trails
            var trails = ps.trails;
            trails.enabled = true;
            trails.ratio = 0.4f;
            trails.lifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            trails.minVertexDistance = 0.01f;
            trails.dieWithParticles = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            // Billboard + trails gives realistic spark streaks without the scale-dependent
            // explosion that Stretch+velocityScale causes at cm-scale weld distances.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
            renderer.trailMaterial = mat;
        }

        // ── Layer 3: Spatter Droplets ─────────────────────────────────────

        private static void ConfigureSpatter(ParticleSystem ps, float radius, bool continuous, Material mat)
        {
            var main = ps.main;
            main.duration = continuous ? 0f : 0.8f;
            main.loop = continuous;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new Color(1f, 0.7f, 0.2f, 1f); // bright orange
            main.gravityModifier = 3.0f; // heavy — fall fast
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            var emission = ps.emission;
            emission.enabled = true;
            if (continuous)
            {
                emission.rateOverTime = 8f;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0.05f, 5, 10),
                    new ParticleSystem.Burst(0.3f, 3, 6),
                });
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(radius * 0.03f, 0.001f);

            // Cooling: bright orange → dark red → nearly black
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.7f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.7f, 0.2f, 0.05f), 0.6f),
                    new GradientColorKey(new Color(0.3f, 0.08f, 0.02f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.5f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
        }

        // ── Layer 4: Heat Glow ────────────────────────────────────────────

        private static void ConfigureHeatGlow(ParticleSystem ps, float radius, bool continuous, Material mat)
        {
            var main = ps.main;
            main.duration = continuous ? 0f : 1.2f;
            main.loop = continuous;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new Color(1f, 0.5f, 0.15f, 0.25f); // warm orange, low alpha
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 10;

            var emission = ps.emission;
            emission.enabled = true;
            if (continuous)
            {
                emission.rateOverTime = 5f;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0f, 4, 6),
                    new ParticleSystem.Burst(0.2f, 3, 4),
                });
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(radius * 0.15f, 0.005f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.5f, 0.15f), 0f),
                    new GradientColorKey(new Color(0.6f, 0.2f, 0.05f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.25f, 0f),
                    new GradientAlphaKey(0.15f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            // Expand slightly then vanish
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.8f), new Keyframe(0.4f, 1.5f), new Keyframe(1f, 0f)));

            // Random start rotation and slow spin so blobs don't look like aligned squares
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            var rotationOverLifetime = ps.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
        }

        // ── Layer 5: Smoke Wisps ──────────────────────────────────────────

        private static void ConfigureSmokeWisps(ParticleSystem ps, float radius, bool continuous, Material mat)
        {
            var main = ps.main;
            main.duration = continuous ? 0f : 1.0f;
            main.loop = continuous;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new Color(0.6f, 0.6f, 0.6f, 0.08f); // very faint gray
            main.gravityModifier = -0.3f; // floats upward
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 8;

            var emission = ps.emission;
            emission.enabled = true;
            if (continuous)
            {
                emission.rateOverTime = 4f;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[]
                {
                    new ParticleSystem.Burst(0.1f, 3, 5),
                    new ParticleSystem.Burst(0.5f, 2, 3),
                });
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(radius * 0.1f, 0.003f);

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 0f),
                    new GradientColorKey(new Color(0.5f, 0.5f, 0.5f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.08f, 0f),
                    new GradientAlphaKey(0.04f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                }
            );
            colorOverLifetime.color = gradient;

            // Expand as they rise (dissipation)
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)));

            // Organic drift
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 0.5f;
            noise.octaveCount = 1;

            // Random start rotation and slow tumble so wisps don't look like gray squares
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            var rotationOverLifetime = ps.rotationOverLifetime;
            rotationOverLifetime.enabled = true;
            rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;
        }

        // ── Shared helpers ───────────────────────────────────────────────

        private static ParticleSystem CreateChildSystem(GameObject parent, string name, Material mat)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            var ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = mat;

            return ps;
        }

        // Cached soft-circle texture — created once, reused across all spawns.
        private static Texture2D _softCircleTex;

        private static Texture2D GetSoftCircleTexture()
        {
            if (_softCircleTex != null)
                return _softCircleTex;

            const int Size = 64;
            _softCircleTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "SoftCircle"
            };

            float center = (Size - 1) * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    float t = Mathf.Clamp01(1f - dist / center);
                    // Smooth cubic falloff: bright centre, soft edge
                    float alpha = t * t * (3f - 2f * t);
                    _softCircleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            _softCircleTex.Apply();
            return _softCircleTex;
        }

        private static Material CreateParticleMaterial()
        {
            Texture2D circle = GetSoftCircleTexture();

            // ── Try URP Particles/Unlit ──────────────────────────────────
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null)
            {
                var mat = new Material(shader) { name = "CompletionParticle" };

                // Surface type: Transparent (required for alpha to work at all)
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 2f);          // 2 = Additive in URP enum
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);

                // Raw blend ops (belt-and-suspenders — some URP versions ignore _Blend)
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);

                // Keywords required by the URP shader pipeline
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");

                mat.SetTexture("_BaseMap", circle);
                mat.SetColor("_BaseColor", Color.white); // white so texture tint is neutral
                mat.renderQueue = 3000;
                return mat;
            }

            // ── Fallback: Sprites/Default (legacy / built-in RP) ─────────
            shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                var mat = new Material(shader) { name = "CompletionParticle" };
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.mainTexture = circle;
                mat.renderQueue = 3000;
                return mat;
            }

            return null;
        }
    }

    /// <summary>
    /// Auto-destroys the GameObject once the ParticleSystem finishes playing.
    /// </summary>
    internal sealed class ParticleAutoDestroy : MonoBehaviour
    {
        private ParticleSystem _ps;

        public void Init(ParticleSystem ps) => _ps = ps;

        private void LateUpdate()
        {
            if (_ps != null && !_ps.IsAlive(true))
                Destroy(gameObject);
        }
    }
}
