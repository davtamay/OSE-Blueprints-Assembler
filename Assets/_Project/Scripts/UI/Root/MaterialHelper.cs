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
                    ghostMat.SetColor("_BaseColor", GhostColor);
                if (ghostMat.HasProperty("_Color"))
                    ghostMat.SetColor("_Color", GhostColor);

                renderer.sharedMaterial = ghostMat;
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
    }
}
