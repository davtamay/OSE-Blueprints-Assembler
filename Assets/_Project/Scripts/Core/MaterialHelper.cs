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

        // Cached shader property IDs. Property lookups via int ID are significantly
        // faster than string lookups, and the values never change across sessions.
        private static readonly int ID_BaseColor       = Shader.PropertyToID("_BaseColor");
        private static readonly int ID_Color           = Shader.PropertyToID("_Color");
        private static readonly int ID_BaseColorFactor = Shader.PropertyToID("baseColorFactor");
        private static readonly int ID_EmissionColor   = Shader.PropertyToID("_EmissionColor");
        private static readonly int ID_EmissiveFactor  = Shader.PropertyToID("emissiveFactor");

        // Reusable per-renderer override scratchpad. MaterialPropertyBlock writes
        // per-renderer overrides without mutating the shared Material asset — so
        // two GameObjects that instance the same GLB prefab (e.g. 12 frame bars
        // from one assetRef) can have independent emission/tint without cross-
        // contaminating each other's visible state. Reusing one block across
        // calls is the documented Unity pattern; GetPropertyBlock reads into it,
        // SetPropertyBlock consumes it.
        private static readonly MaterialPropertyBlock s_propBlock = new MaterialPropertyBlock();

        // Cached shader — resolved on first use. Unity clears this on domain reload.
        private static Shader _urpLitShader;

        // Cached tool target marker base material loaded from
        // Resources/ToolTargetMarker.mat. Cloned per spawn — matches the
        // GhostOverlay pattern. Loading the material asset (vs.
        // Shader.Find + new Material(shader)) ensures URP's SRP-Batcher
        // initialization runs over an asset whose properties are
        // pre-serialized inside UnityPerMaterial, which is what the
        // overlay shader's CBUFFER expects.
        private static Material _toolTargetMarkerBase;

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

        private static Material ToolTargetMarkerBase =>
            _toolTargetMarkerBase != null ? _toolTargetMarkerBase
                : (_toolTargetMarkerBase = Resources.Load<Material>("ToolTargetMarker"));

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
                // The previous version enabled _EMISSION here. URP's WebGL
                // build-time shader stripping drops emission variants when
                // no scene material uses them — runtime-created materials
                // with EnableKeyword("_EMISSION") then end up referencing a
                // stripped variant and silently render nothing in the build
                // (Renderer.isVisible=false even though bounds + layer +
                // frustum all check out). Without the keyword the material
                // uses URP/Lit's base lit pass which is always retained.
                // Editor / native builds are unaffected because variant
                // stripping is far less aggressive there.
                if (tintMat.HasProperty("_EmissionColor"))
                {
                    tintMat.SetColor("_EmissionColor", color * 0.15f);
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
                OseLog.Warn("[MaterialHelper] Resources/GhostOverlay.mat not found — falling back to URP Lit.");
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
                OseLog.Warn($"[MaterialHelper] No renderers found on '{target.name}' or its children.");
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
        /// Clones the <c>Resources/ToolTargetMarker.mat</c> asset (whose
        /// shader bakes Queue=Overlay, Blend SrcAlpha OneMinusSrcAlpha,
        /// ZWrite Off, ZTest Always into a single Pass) and overrides the
        /// per-spawn colors. Loading the asset (vs Shader.Find +
        /// new Material(shader)) is the same pattern GhostOverlay uses
        /// successfully — it ensures URP's SRP-Batcher and CBUFFER
        /// initialization runs over a serialized material.
        /// </summary>
        public static void ApplyToolTargetMarker(GameObject target, Color markerColor)
        {
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            var baseMat = ToolTargetMarkerBase;
            if (baseMat == null)
            {
                OseLog.Warn(OseErrorCode.ContentLoadFailed,
                    "[MaterialHelper] Resources/ToolTargetMarker.mat missing — marker will be invisible.");
                return;
            }

            // Authored base alpha for the sphere fill — ToolTargetAnimator
            // modulates per-frame via a MaterialPropertyBlock (camera-distance
            // fade + pulse), so this is the upper bound, not the runtime value.
            Color visibleColor = markerColor;
            visibleColor.a = 0.30f;

            Color emissionColor = markerColor;
            emissionColor.a = 1f;

            foreach (var renderer in renderers)
            {
                Material markerMat = new Material(baseMat) { name = "Tool Target Marker Material" };
                markerMat.SetColor(ID_BaseColor, visibleColor);
                markerMat.SetColor(ID_EmissionColor, emissionColor * 2.2f);
                renderer.sharedMaterial = markerMat;
            }
        }

        /// <summary>
        /// Updates the color of renderers on <paramref name="target"/> via a
        /// per-renderer MaterialPropertyBlock — no Material allocation and no
        /// mutation of the shared asset. Safe to call per-frame for highlight
        /// pulses. Sibling instances that share the same source GLB material
        /// are unaffected.
        /// </summary>
        public static void SetMaterialColor(GameObject target, Color color)
        {
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            Color faintEmission = color * 0.08f;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer.gameObject.name == OutlineChildName) continue;

                renderer.GetPropertyBlock(s_propBlock);
                // Cover URP/Lit + Built-in + glTFast Shader Graph — property
                // block writes that target a property the shader doesn't
                // expose are silently ignored, so it's safe to always write
                // all the candidates.
                s_propBlock.SetColor(ID_BaseColor,       color);
                s_propBlock.SetColor(ID_Color,           color);
                s_propBlock.SetColor(ID_BaseColorFactor, color);
                s_propBlock.SetColor(ID_EmissionColor,   faintEmission);
                s_propBlock.SetColor(ID_EmissiveFactor,  faintEmission);
                renderer.SetPropertyBlock(s_propBlock);
            }
        }

        // Cached fallback material for imported GLBs that ship without any
        // material bound (Blender export without a valid PBR graph, for
        // example). Cached so repeated fallbacks on a dozen instances of the
        // same bad GLB share one material instead of leaking one per slot.
        private static readonly Dictionary<int, Material> _fallbackCacheMap =
            new Dictionary<int, Material>();

        // Logged-once per asset family so we don't spam the console — the
        // message is meant to flag an authoring regression, not tell the
        // author about it every frame.
        private static readonly HashSet<string> _fallbackWarnedOwners = new HashSet<string>();

        /// <summary>
        /// Scans every renderer under <paramref name="target"/> and fills any
        /// null material slots with a cached URP/Lit fallback in
        /// <paramref name="fallbackColor"/>. Intended for imported GLBs that
        /// shipped without materials — without this they render as Unity's
        /// missing-material pink (or worse, invisible on some pipelines).
        /// Existing non-null slots are left alone, so the fix is a no-op on
        /// correctly authored assets. Call right after <see cref="MarkAsImported"/>
        /// so the <see cref="OriginalMaterialCache"/> snapshot captures the
        /// fallback as the "original" for future Restore calls.
        /// </summary>
        public static bool EnsureFallbackMaterialForImported(GameObject target, Color fallbackColor)
        {
            if (target == null) return false;
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return false;

            bool anyFilled = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer.gameObject.name == OutlineChildName) continue;

                var slots = renderer.sharedMaterials;
                if (slots == null || slots.Length == 0) continue;

                bool dirty = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null) continue;
                    slots[i] = GetOrCreateFallback(fallbackColor);
                    dirty = true;
                    anyFilled = true;
                }
                if (dirty)
                    renderer.sharedMaterials = slots;
            }

            if (anyFilled && _fallbackWarnedOwners.Add(target.name))
            {
                OseLog.Warn($"[MaterialHelper] '{target.name}' GLB has null material slot(s); applied fallback. " +
                            "Re-export the asset from Blender with a bound PBR material to fix at the source.");
            }
            return anyFilled;
        }

        private static Material GetOrCreateFallback(Color color)
        {
            int key = TintCacheKey("OSE_FallbackMaterial", color);
            if (_fallbackCacheMap.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var mat = CreateUrpMaterial("OSE_FallbackMaterial");
            if (mat == null) return null;

            SetBaseColor(mat, color);
            // Moderate metallic + low smoothness reads as "mild steel-ish"
            // for the common case (frame bars, brackets) without looking
            // like a deliberate material choice — the warning log nudges
            // authors to fix the GLB rather than normalize around this.
            if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", 0.4f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);

            _fallbackCacheMap[key] = mat;
            return mat;
        }

        /// <summary>
        /// Sets emission glow on each renderer via a per-renderer
        /// MaterialPropertyBlock — no Material allocation and no mutation of
        /// the shared asset. Pass <see cref="Color.black"/> to clear emission.
        ///
        /// Because PropertyBlock cannot toggle shader keywords, the
        /// <c>_EMISSION</c> / <c>_EMISSIVE</c> keyword is enabled once on the
        /// shared material the first time we touch emission on its renderer.
        /// This is a one-time write (keyword flag, not color), so it does not
        /// cause the cross-instance bleed the old sharedMaterial.SetColor path
        /// did — subsequent per-frame color writes go to the property block
        /// and stay local to each renderer.
        /// </summary>
        public static void SetEmission(GameObject target, Color emissionColor)
        {
            var renderers = GetRenderers(target);
            if (renderers == null || renderers.Length == 0) return;

            bool hasEmission = emissionColor.r > 0f || emissionColor.g > 0f || emissionColor.b > 0f;

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (renderer.gameObject.name == OutlineChildName) continue;

                if (hasEmission)
                    EnsureEmissionKeywords(renderer);

                renderer.GetPropertyBlock(s_propBlock);
                s_propBlock.SetColor(ID_EmissionColor,  emissionColor);
                s_propBlock.SetColor(ID_EmissiveFactor, emissionColor);
                renderer.SetPropertyBlock(s_propBlock);
            }
        }

        // Enables the emission keyword on each shared material under the
        // renderer the first time we want emission to render on it. Idempotent
        // and cheap (IsKeywordEnabled + EnableKeyword). Keywords live at the
        // material level — PropertyBlock overrides take effect only when the
        // material's keyword is on. Leaving it on when emission is black is
        // fine: the black color contributes nothing to the lit pass.
        private static void EnsureEmissionKeywords(Renderer renderer)
        {
            var mats = renderer.sharedMaterials;
            if (mats == null) return;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (m.HasProperty(ID_EmissionColor) && !m.IsKeywordEnabled("_EMISSION"))
                    m.EnableKeyword("_EMISSION");
                if (m.HasProperty(ID_EmissiveFactor) && !m.IsKeywordEnabled("_EMISSIVE"))
                    m.EnableKeyword("_EMISSIVE");
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
