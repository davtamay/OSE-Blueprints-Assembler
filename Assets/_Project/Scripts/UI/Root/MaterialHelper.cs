using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Shared static utilities for creating and applying preview materials.
    /// Used by PreviewSceneSetup, PackagePartSpawner, and PartInteractionBridge.
    /// </summary>
    public static class MaterialHelper
    {
        private static readonly Color GhostColor = new Color(0.4f, 0.8f, 1.0f, 0.3f);

        /// <summary>
        /// Applies an opaque URP/Lit material with the given color to all renderers
        /// on the target GameObject (including children).
        /// </summary>
        public static void Apply(GameObject target, string materialName, Color color)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            foreach (var renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader != shader || material.name != materialName)
                {
                    material = new Material(shader) { name = materialName };
                    renderer.sharedMaterial = material;
                }

                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", color * 0.08f);
            }
        }

        /// <summary>
        /// Applies a transparent ghost material to all renderers on the target.
        /// </summary>
        public static void ApplyGhost(GameObject target)
        {
            ApplyGhost(target, GhostColor);
        }

        /// <summary>
        /// Applies a transparent ghost material with a custom tint to all renderers on the target.
        /// </summary>
        public static void ApplyGhost(GameObject target, Color ghostColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            foreach (var renderer in renderers)
            {
                Material ghostMat = new Material(shader) { name = "Ghost Material" };

                ghostMat.SetFloat("_Surface", 1f); // Transparent
                ghostMat.SetFloat("_Blend", 0f);   // Alpha blend
                ghostMat.SetOverrideTag("RenderType", "Transparent");
                ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                ghostMat.SetInt("_ZWrite", 0);
                ghostMat.renderQueue = 3000;

                if (ghostMat.HasProperty("_BaseColor"))
                    ghostMat.SetColor("_BaseColor", ghostColor);
                if (ghostMat.HasProperty("_Color"))
                    ghostMat.SetColor("_Color", ghostColor);

                renderer.sharedMaterial = ghostMat;
            }
        }

        /// <summary>
        /// Applies a transparent "tool in hand" material style that is visually
        /// distinct from placement ghosts.
        /// </summary>
        public static void ApplyToolCursor(GameObject target, Color toolColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            foreach (var renderer in renderers)
            {
                Material toolMat = new Material(shader) { name = "Tool Cursor Material" };

                toolMat.SetFloat("_Surface", 1f); // Transparent
                toolMat.SetFloat("_Blend", 0f);   // Alpha blend
                toolMat.SetOverrideTag("RenderType", "Transparent");
                toolMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                toolMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                toolMat.SetInt("_ZWrite", 0);
                toolMat.renderQueue = 3001;

                if (toolMat.HasProperty("_BaseColor"))
                    toolMat.SetColor("_BaseColor", toolColor);
                if (toolMat.HasProperty("_Color"))
                    toolMat.SetColor("_Color", toolColor);
                if (toolMat.HasProperty("_EmissionColor"))
                    toolMat.SetColor("_EmissionColor", toolColor * 0.35f);

                renderer.sharedMaterial = toolMat;
            }
        }

        /// <summary>
        /// Applies a high-visibility marker style for tool action targets.
        /// This is intentionally brighter and less transparent than the tool cursor.
        /// </summary>
        public static void ApplyToolTargetMarker(GameObject target, Color markerColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;

            Color visibleColor = markerColor;
            visibleColor.a = 1f;

            foreach (var renderer in renderers)
            {
                Material markerMat = new Material(shader) { name = "Tool Target Marker Material" };

                if (markerMat.HasProperty("_Surface"))
                    markerMat.SetFloat("_Surface", 0f); // Opaque
                if (markerMat.HasProperty("_Blend"))
                    markerMat.SetFloat("_Blend", 0f);
                markerMat.SetOverrideTag("RenderType", "Opaque");
                markerMat.SetInt("_ZWrite", 1);
                markerMat.renderQueue = -1;

                if (markerMat.HasProperty("_BaseColor"))
                    markerMat.SetColor("_BaseColor", visibleColor);
                if (markerMat.HasProperty("_Color"))
                    markerMat.SetColor("_Color", visibleColor);
                if (markerMat.HasProperty("_EmissionColor"))
                    markerMat.SetColor("_EmissionColor", visibleColor * 0.9f);

                renderer.sharedMaterial = markerMat;
            }
        }

        /// <summary>
        /// Updates the color of existing materials on the target without allocating new ones.
        /// Useful for lightweight highlight pulses.
        /// </summary>
        public static void SetMaterialColor(GameObject target, Color color)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (var renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                if (material == null) continue;

                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", color);
                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", color * 0.08f);
            }
        }

        /// <summary>
        /// Sets emission glow on existing materials without affecting base color.
        /// Works alongside the XRI affordance system which only overrides _BaseColor
        /// via MaterialPropertyBlock — emission on the shared material passes through.
        /// Pass <see cref="Color.black"/> to clear emission.
        /// </summary>
        public static void SetEmission(GameObject target, Color emissionColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            bool hasEmission = emissionColor.r > 0f || emissionColor.g > 0f || emissionColor.b > 0f;

            foreach (var renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                if (material == null) continue;

                if (hasEmission)
                    material.EnableKeyword("_EMISSION");

                if (material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", emissionColor);
            }
        }
    }
}
