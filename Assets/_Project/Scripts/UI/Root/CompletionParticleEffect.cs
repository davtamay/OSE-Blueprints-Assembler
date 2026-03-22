using System.Collections.Generic;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Spawns a self-destructing ParticleSystem at a world position based on a named preset.
    /// Presets are built procedurally — no prefab files required.
    /// Content authors reference presets by ID in the feedback payload
    /// (e.g. "torque_sparks", "weld_glow").
    /// </summary>
    internal static class CompletionParticleEffect
    {
        private delegate void ConfigureParticle(ParticleSystem ps, Vector3 scale);

        private static readonly Dictionary<string, ConfigureParticle> Presets = new()
        {
            ["torque_sparks"] = ConfigureTorqueSparks,
            ["weld_glow"] = ConfigureWeldGlow,
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
                Debug.LogWarning($"[CompletionParticleEffect] Unknown preset '{presetId}'.");
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

        private static void ConfigureWeldGlow(ParticleSystem ps, Vector3 scale)
        {
            float radius = Mathf.Max(scale.x, scale.z) * 0.5f;

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new Color(0.4f, 0.7f, 1f, 0.8f); // blue-white
            main.gravityModifier = -0.2f; // float upward
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 20;

            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10, 18) });
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.2f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.6f, 0.85f, 1f), 0f), new GradientColorKey(new Color(0.3f, 0.5f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) }
            );
            colorOverLifetime.color = gradient;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateParticleMaterial();
        }

        // ── Shared helpers ───────────────────────────────────────────────

        private static Material CreateParticleMaterial()
        {
            var shader = Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");

            if (shader == null)
                return null;

            var mat = new Material(shader) { name = "CompletionParticle" };
            mat.SetFloat("_Mode", 1f); // Additive-ish
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.renderQueue = 3100;
            return mat;
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
