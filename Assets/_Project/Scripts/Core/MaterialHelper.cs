using UnityEngine;
using UnityEngine.Rendering;

namespace OSE.Core
{
    /// <summary>
    /// Shared static utilities for creating and applying preview materials.
    /// Used by PreviewSceneSetup, PackagePartSpawner, and PartInteractionBridge.
    /// </summary>
    public static class MaterialHelper
    {
        private static readonly Color PreviewColor = new Color(0.4f, 0.8f, 1.0f, 0.3f);

        private const string OutlineChildName = "__ose_outline__";
        private const int TransparentRenderQueue = 3000;
        private const int OverlayRenderQueue = 4000;

        // Cached shader — resolved on first use. Unity clears this on domain reload.
        private static Shader _urpLitShader;

        private static Shader UrpLitShader =>
            _urpLitShader != null ? _urpLitShader
                : (_urpLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        // ── Original-material preservation ──────────────────────────────

        /// <summary>
        /// Caches the current materials on all renderers so they can be restored later.
        /// Call this once before any Apply/ApplyPreviewMaterial that might replace materials.
        /// </summary>
        public static void SaveOriginals(GameObject target)
        {
            if (target == null) return;
            GetOrAddCache(target).Save();
        }

        /// <summary>
        /// Re-saves current materials even if already saved.
        /// </summary>
        public static void ForceSaveOriginals(GameObject target)
        {
            if (target == null) return;
            var cache = target.GetComponent<OriginalMaterialCache>();
            if (cache == null)
            {
                MarkAsImported(target);
                return;
            }
            cache.ForceSave();
        }

        /// <summary>
        /// Marks this GameObject as an imported model (from GLB/GLTF, not a primitive).
        /// When marked, the system preserves original materials instead of replacing
        /// them with solid colors.
        /// </summary>
        public static void MarkAsImported(GameObject target)
        {
            if (target == null) return;
            var cache = GetOrAddCache(target);
            cache.MarkAsImported();
            cache.Save();
        }

        /// <summary>
        /// Restores the original cached materials on all renderers, clearing any
        /// MaterialPropertyBlock overrides. Returns false if no cache exists.
        /// </summary>
        public static bool RestoreOriginals(GameObject target)
        {
            if (target == null) return false;
            var cache = target.GetComponent<OriginalMaterialCache>();
            if (cache == null || !cache.HasSavedMaterials) return false;
            cache.Restore();
            return true;
        }

        /// <summary>
        /// Returns true if this is an imported model with original materials to preserve.
        /// </summary>
        public static bool IsImportedModel(GameObject target)
        {
            if (target == null) return false;
            var cache = target.GetComponent<OriginalMaterialCache>();
            return cache != null && cache.IsImportedModel;
        }

        /// <summary>
        /// Shows a colored outline around imported/textured models.
        /// Uses inverted-hull child objects — the original materials stay untouched.
        /// </summary>
        public static void ApplyTint(GameObject target, Color tint)
        {
            if (target == null) return;
            var cache = target.GetComponent<OriginalMaterialCache>();
            if (cache != null)
                cache.ShowOutline(tint);

            // Replace ALL material slots on every renderer with a shared tint
            // material.  Emission/keyword tricks don't cover all glTFast shader
            // variants, so a full replacement is the only reliable approach.
            // OriginalMaterialCache.Restore() puts the originals back.
            ApplyColorToAllSlots(target, tint);
        }

        /// <summary>
        /// Hides the outline effect, returning the model to its idle visual state.
        /// </summary>
        public static void ClearTint(GameObject target)
        {
            if (target == null) return;
            var cache = target.GetComponent<OriginalMaterialCache>();
            if (cache != null)
                cache.HideOutline();

            // Restore originals to undo the full-slot replacement from ApplyTint.
            RestoreOriginals(target);
        }

        /// <summary>
        /// Applies an opaque URP/Lit material with the given color to all renderers
        /// on the target GameObject (including children), filling every material slot.
        /// </summary>
        public static void Apply(GameObject target, string materialName, Color color)
        {
            ApplyColorToAllSlots(target, color, materialName);
        }

        /// <summary>
        /// Replaces every material slot on every renderer with a single shared
        /// URP/Lit material in the given color. Skips outline children.
        /// </summary>
        private static void ApplyColorToAllSlots(GameObject target, Color color, string materialName = "OSE_Tint")
        {
            Material tintMat = CreateUrpMaterial(materialName);
            if (tintMat == null) return;

            SetBaseColor(tintMat, color);
            if (tintMat.HasProperty("_EmissionColor"))
            {
                tintMat.SetColor("_EmissionColor", color * 0.15f);
                tintMat.EnableKeyword("_EMISSION");
            }

            ApplyToAllSlots(target, tintMat, skipOutline: true);
        }

        /// <summary>
        /// Applies a transparent preview material to all renderers on the target.
        /// </summary>
        public static void ApplyPreviewMaterial(GameObject target)
        {
            ApplyPreviewMaterial(target, PreviewColor);
        }

        /// <summary>
        /// Applies a transparent preview material with a custom tint to all renderers on the target.
        /// </summary>
        public static void ApplyPreviewMaterial(GameObject target, Color previewColor)
        {
            Material previewMat = CreateUrpMaterial("Preview Material");
            if (previewMat == null) return;

            ConfigureTransparent(previewMat, TransparentRenderQueue);
            SetBaseColor(previewMat, previewColor);

            ApplyToAllSlots(target, previewMat, skipOutline: true);
        }

        /// <summary>
        /// Clones each renderer's materials and makes them semi-transparent while
        /// preserving original textures and colors. Returns the cloned material arrays
        /// per renderer so they can be stored and restored later.
        /// </summary>
        public static Material[][] MakeTransparent(GameObject target, float alpha = 0.55f)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return System.Array.Empty<Material[]>();

            var result = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                var originals = renderers[i].sharedMaterials;
                var clones = new Material[originals.Length];
                for (int j = 0; j < originals.Length; j++)
                {
                    if (originals[j] == null) { clones[j] = null; continue; }
                    var mat = new Material(originals[j]);

                    ConfigureTransparent(mat, OverlayRenderQueue, zWrite: true);
                    mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
                    if (originals[j].HasProperty("_Cull"))
                        mat.SetInt("_Cull", originals[j].GetInt("_Cull"));
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

                    SetBaseColorAlpha(mat, alpha);
                    clones[j] = mat;
                }
                renderers[i].sharedMaterials = clones;
                result[i] = clones;
            }
            return result;
        }

        /// <summary>
        /// Reverses <see cref="MakeTransparent"/> — sets all materials back to opaque rendering.
        /// Used when cloning a transparent cursor preview for a persistent scene placement.
        /// </summary>
        public static void RestoreOpaque(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (var renderer in renderers)
            {
                var mats = renderer.materials; // instance copies
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    ConfigureOpaque(mat);
                    SetBaseColorAlpha(mat, 1f);
                }
                renderer.materials = mats;
            }
        }

        /// <summary>
        /// Applies a transparent "tool in hand" material style that is visually
        /// distinct from placement previews.
        /// </summary>
        public static void ApplyToolCursor(GameObject target, Color toolColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            var shader = UrpLitShader;
            if (shader == null) return;

            foreach (var renderer in renderers)
            {
                Material toolMat = new Material(shader) { name = "Tool Cursor Material" };
                ConfigureTransparent(toolMat, OverlayRenderQueue);
                toolMat.SetInt("_ZTest", (int)CompareFunction.Always);
                SetBaseColor(toolMat, toolColor);
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

            var shader = UrpLitShader;
            if (shader == null) return;

            // Semi-transparent so the tool action behind/inside is visible
            Color visibleColor = markerColor;
            visibleColor.a = 0.18f;

            // Stronger emission so the rim glows even though base is transparent
            Color emissionColor = markerColor;
            emissionColor.a = 1f;

            foreach (var renderer in renderers)
            {
                Material markerMat = new Material(shader) { name = "Tool Target Marker Material" };
                ConfigureTransparent(markerMat, TransparentRenderQueue);
                markerMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                markerMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");

                SetBaseColor(markerMat, visibleColor);
                if (markerMat.HasProperty("_EmissionColor"))
                    markerMat.SetColor("_EmissionColor", emissionColor * 1.2f);
                markerMat.EnableKeyword("_EMISSION");

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
                if (renderer.gameObject.name == OutlineChildName) continue;

                // Use .materials to get per-renderer instances for ALL material slots.
                // renderer.material only returns slot 0, missing sub-meshes on multi-material models.
                Material[] mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material material = mats[m];
                    if (material == null) continue;

                    // URP/Lit
                    SetBaseColor(material, color);
                    if (material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", color * 0.08f);

                    // glTFast Shader Graph (glTF-pbrMetallicRoughness)
                    if (material.HasProperty("baseColorFactor"))
                        material.SetColor("baseColorFactor", color);
                    if (material.HasProperty("emissiveFactor"))
                        material.SetColor("emissiveFactor", color * 0.08f);
                }
                renderer.materials = mats;
            }
        }

        /// <summary>
        /// Sets emission glow on existing materials without affecting base color.
        /// Supports both URP/Lit (<c>_EmissionColor</c> + <c>_EMISSION</c>) and
        /// glTFast Shader Graph (<c>emissiveFactor</c> + <c>_EMISSIVE</c>).
        /// Pass <see cref="Color.black"/> to clear emission.
        /// </summary>
        public static void SetEmission(GameObject target, Color emissionColor)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            bool hasEmission = emissionColor.r > 0f || emissionColor.g > 0f || emissionColor.b > 0f;

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == OutlineChildName) continue;

                // Use .materials (not .material) to update ALL material slots.
                // .material only returns slot 0, missing sub-meshes on multi-material models.
                Material[] mats = renderer.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material material = mats[m];
                    if (material == null) continue;

                    // URP/Lit shader
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", emissionColor);
                        if (hasEmission)
                            material.EnableKeyword("_EMISSION");
                        else
                            material.DisableKeyword("_EMISSION");
                    }

                    // glTFast Shader Graph (glTF-pbrMetallicRoughness)
                    if (material.HasProperty("emissiveFactor"))
                    {
                        material.SetColor("emissiveFactor", emissionColor);
                        if (hasEmission)
                            material.EnableKeyword("_EMISSIVE");
                        else
                            material.DisableKeyword("_EMISSIVE");
                    }
                }
                renderer.materials = mats;
            }
        }

        // ── Private helpers ──────────────────────────────────────────────

        private static OriginalMaterialCache GetOrAddCache(GameObject target)
        {
            var cache = target.GetComponent<OriginalMaterialCache>();
            return cache != null ? cache : target.AddComponent<OriginalMaterialCache>();
        }

        private static Material CreateUrpMaterial(string name)
        {
            var shader = UrpLitShader;
            return shader != null ? new Material(shader) { name = name } : null;
        }

        private static void SetBaseColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        private static void SetBaseColorAlpha(Material mat, float alpha)
        {
            if (mat.HasProperty("_BaseColor"))
            {
                Color c = mat.GetColor("_BaseColor");
                c.a = alpha;
                mat.SetColor("_BaseColor", c);
            }
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                c.a = alpha;
                mat.SetColor("_Color", c);
            }
        }

        private static void ConfigureTransparent(Material mat, int renderQueue, bool zWrite = false)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", zWrite ? 1 : 0);
            mat.renderQueue = renderQueue;
        }

        private static void ConfigureOpaque(Material mat)
        {
            mat.SetFloat("_Surface", 0f);
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.renderQueue = -1;
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private static void ApplyToAllSlots(GameObject target, Material mat, bool skipOutline)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return;

            foreach (var renderer in renderers)
            {
                if (skipOutline && renderer.gameObject.name == OutlineChildName) continue;

                int slotCount = renderer.sharedMaterials.Length;
                if (slotCount <= 0) slotCount = 1;
                Material[] mats = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                    mats[i] = mat;
                renderer.sharedMaterials = mats;
            }
        }
    }
}
