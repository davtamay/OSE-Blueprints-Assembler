using System.Collections.Generic;
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

        // Cached ghost overlay material loaded from Resources/GhostOverlay.mat.
        private static Material _ghostOverlayBase;

        // Shared tint materials keyed by (materialName + Color32). Avoids allocating
        // a new Material on every Apply / ApplyTint call.
        //
        // No eviction: evicting a Material while it is still assigned to a renderer's
        // sharedMaterials destroys it mid-frame → pink renderer on that part. The cache
        // has no visibility into which materials are currently live on renderers, so safe
        // eviction is impossible. The number of unique (name, color) combos is bounded by
        // the number of distinct part colors + a few interaction states (hover, select) —
        // typically well under 200 entries. Material objects are ~few KB each; the total
        // footprint is negligible compared to mesh/texture memory.
        private static readonly Dictionary<int, Material> _tintCacheMap =
            new Dictionary<int, Material>();

        private static int TintCacheKey(string name, Color color)
        {
            var c32 = (Color32)color;
            // Cheap hash: combine string hash with packed RGBA int.
            int rgba = (c32.r << 24) | (c32.g << 16) | (c32.b << 8) | c32.a;
            return (name?.GetHashCode() ?? 0) * 397 ^ rgba;
        }

        private static bool TryGetCachedTint(int key, out Material mat)
        {
            if (!_tintCacheMap.TryGetValue(key, out mat)) return false;
            return mat != null;
        }

        private static void AddTintToCache(int key, Material mat)
        {
            if (!_tintCacheMap.ContainsKey(key))
                _tintCacheMap[key] = mat;
        }

        private static Shader UrpLitShader =>
            _urpLitShader != null ? _urpLitShader
                : (_urpLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        // ── Renderer caching ──────────────────────────────────────────────

        /// <summary>
        /// Returns the cached <see cref="Renderer"/> array for <paramref name="target"/>,
        /// avoiding per-call <c>GetComponentsInChildren</c> allocations.
        /// The cache auto-invalidates when the hierarchy changes.
        /// </summary>
        public static Renderer[] GetRenderers(GameObject target)
        {
            if (target == null) return System.Array.Empty<Renderer>();
            var cache = target.GetComponent<RendererCache>();
            if (cache == null) cache = target.AddComponent<RendererCache>();
            return cache.Renderers;
        }

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

            // Guarantee originals are captured BEFORE replacing slots. If
            // MarkAsImported ran before glTFast finished applying materials,
            // the cache's Save() bailed and _saved stayed false; hover-exit
            // then finds no saved materials, falls through to the preview-
            // material fallback, and the part switches to a lighter solid
            // color permanently. ForceSave on hover-in captures the current
            // (now-applied) materials so RestoreOriginals can put them back.
            if (cache != null && !cache.HasSavedMaterials)
                cache.ForceSave();

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
        /// Materials are cached by (name, color) so repeated calls with the same
        /// arguments allocate nothing after the first call.
        /// </summary>
        private static void ApplyColorToAllSlots(GameObject target, Color color, string materialName = "OSE_Tint")
        {
            int key = TintCacheKey(materialName, color);
            if (!TryGetCachedTint(key, out Material tintMat))
            {
                tintMat = CreateUrpMaterial(materialName);
                if (tintMat == null) return;

                SetBaseColor(tintMat, color);
                if (tintMat.HasProperty("_EmissionColor"))
                {
                    tintMat.SetColor("_EmissionColor", color * 0.15f);
                    tintMat.EnableKeyword("_EMISSION");
                }

                AddTintToCache(key, tintMat);
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
            // Load ghost overlay material from Resources — Shader.Find is unreliable
            // for custom shaders not referenced by any scene material.
            if (_ghostOverlayBase == null)
                _ghostOverlayBase = Resources.Load<Material>("GhostOverlay");

            if (_ghostOverlayBase == null)
            {
                Debug.LogWarning("[MaterialHelper] Resources/GhostOverlay.mat not found — falling back to URP Lit.");
                Material fallback = CreateUrpMaterial("Preview Material");
                if (fallback == null) return;
                ConfigureTransparent(fallback, TransparentRenderQueue);
                SetBaseColor(fallback, previewColor);
                ApplyToAllSlots(target, fallback, skipOutline: true);
                return;
            }

            // Instantiate so each ghost can have its own color.
            Material previewMat = new Material(_ghostOverlayBase) { name = "Ghost Overlay" };
            previewMat.SetColor("_BaseColor", new Color(
                previewColor.r, previewColor.g, previewColor.b,
                Mathf.Max(previewColor.a, 0.35f)));
            previewMat.SetColor("_EmissionColor", previewColor * 0.15f);

            // Invalidate stale RendererCache (cloned objects carry cached arrays from the source).
            var staleCache = target.GetComponent<RendererCache>();
            if (staleCache != null) staleCache.Invalidate();

            // Apply directly to all child renderers (bypass ApplyToAllSlots to avoid
            // any stale-cache issues on cloned GameObjects).
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning($"[MaterialHelper] No renderers found on '{target.name}' or its children.");
                return;
            }

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == OutlineChildName) continue;
                int slotCount = renderer.sharedMaterials.Length;
                if (slotCount <= 0) slotCount = 1;
                Material[] mats = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                    mats[i] = previewMat;
                renderer.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Clones each renderer's materials and makes them semi-transparent while
        /// preserving original textures and colors. Returns the cloned material arrays
        /// per renderer so they can be stored and restored later.
        /// </summary>
        public static Material[][] MakeTransparent(GameObject target, float alpha = 0.55f)
        {
            var renderers = GetRenderers(target);
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
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            bool useShared = !Application.isPlaying;

            foreach (var renderer in renderers)
            {
                var mats = useShared ? renderer.sharedMaterials : renderer.materials;
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    ConfigureOpaque(mat);
                    SetBaseColorAlpha(mat, 1f);
                }
                if (!useShared)
                    renderer.materials = mats;
            }
        }

        /// <summary>
        /// Applies a transparent "tool in hand" material style that is visually
        /// distinct from placement previews.
        /// </summary>
        public static void ApplyToolCursor(GameObject target, Color toolColor)
        {
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            if (_ghostOverlayBase == null)
                _ghostOverlayBase = Resources.Load<Material>("GhostOverlay");

            if (_ghostOverlayBase == null)
            {
                // Fallback to URP Lit without x-ray.
                var shader = UrpLitShader;
                if (shader == null) return;
                foreach (var renderer in renderers)
                {
                    Material toolMat = new Material(shader) { name = "Tool Cursor Material" };
                    ConfigureTransparent(toolMat, OverlayRenderQueue);
                    SetBaseColor(toolMat, toolColor);
                    renderer.sharedMaterial = toolMat;
                }
                return;
            }

            foreach (var renderer in renderers)
            {
                Material toolMat = new Material(_ghostOverlayBase) { name = "Tool Cursor Material" };
                toolMat.SetColor("_BaseColor", toolColor);
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
            var renderers = GetRenderers(target);
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
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            bool useShared = !Application.isPlaying;

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == OutlineChildName) continue;

                Material[] mats = useShared ? renderer.sharedMaterials : renderer.materials;
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
                if (!useShared)
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
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            bool hasEmission = emissionColor.r > 0f || emissionColor.g > 0f || emissionColor.b > 0f;
            // In edit mode, .materials creates leaked instances. Use .sharedMaterials
            // to modify materials in-place (safe — they are per-import instances, not
            // project assets).
            bool useShared = !Application.isPlaying;

            foreach (var renderer in renderers)
            {
                if (renderer.gameObject.name == OutlineChildName) continue;

                // Use .materials (not .material) to update ALL material slots.
                // .material only returns slot 0, missing sub-meshes on multi-material models.
                Material[] mats = useShared ? renderer.sharedMaterials : renderer.materials;
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
                if (!useShared)
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
            var renderers = GetRenderers(target);
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
