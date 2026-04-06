using System.Collections.Generic;
using UnityEngine;

namespace OSE.Core
{
    /// <summary>
    /// Caches original materials from glTFast-imported models so they can be
    /// restored after interaction effects (hover, select, preview, etc.) override them.
    /// Also manages outline child objects for non-destructive highlight effects.
    /// Attach via <see cref="MaterialHelper.SaveOriginals"/>.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class OriginalMaterialCache : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearStaticsOnDomainReload()
        {
            s_outlineMaterials?.Clear();
        }

#if UNITY_EDITOR
        static OriginalMaterialCache()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
                    s_outlineMaterials?.Clear();
            };
        }
#endif

        private struct Entry
        {
            public Renderer Renderer;
            public Material[] Materials;
        }

        private Entry[] _entries;
        private bool _saved;
        private bool _isImportedModel;

        // ── Outline support ─────────────────────────────────────────────
        private const float k_DefaultOutlineWidth = 5.0f;
        private static Shader s_outlineShader;
        private static readonly Dictionary<Color, Material> s_outlineMaterials = new();
        private GameObject[] _outlineObjects;

        public bool HasSavedMaterials => _saved;

        /// <summary>
        /// True if this model was loaded from a GLB/GLTF asset (not a Unity primitive).
        /// Set explicitly by the spawner — no fragile texture-property detection.
        /// </summary>
        public bool IsImportedModel => _isImportedModel;

        public void MarkAsImported() => _isImportedModel = true;

        /// <summary>
        /// Re-saves current materials even if already saved.
        /// </summary>
        public void ForceSave()
        {
            _saved = false;
            Save();
        }

        public void Save()
        {
            if (_saved) return;

            var renderers = MaterialHelper.GetRenderers(gameObject);
            _entries = new Entry[renderers.Length];

            bool anyValid = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                Material[] mats = r.sharedMaterials;

                // Clone the array so it's independent of future changes
                var copy = new Material[mats.Length];
                mats.CopyTo(copy, 0);

                _entries[i] = new Entry { Renderer = r, Materials = copy };

                // Check that at least one material slot is non-null so we don't
                // permanently block the fallback preview-material path.
                for (int m = 0; m < copy.Length; m++)
                    if (copy[m] != null) { anyValid = true; break; }
            }

            // Only mark as saved when real materials exist. If glTFast hasn't
            // applied materials yet (e.g. deferred Shader Graph setup), we leave
            // _saved = false so ForceSave() in HandlePartsReady can retry, and so
            // RestoreOriginals() returns false — allowing the preview-material
            // fallback in ApplyAvailablePartVisual to run instead of restoring nulls.
            if (anyValid)
                _saved = true;
        }

        public void Restore()
        {
            if (!_saved || _entries == null) return;

            HideOutline();

            foreach (var entry in _entries)
            {
                if (entry.Renderer != null)
                {
                    entry.Renderer.sharedMaterials = entry.Materials;
                    entry.Renderer.SetPropertyBlock(null);
                }
            }

            // Clear any emission residue via per-renderer MaterialPropertyBlock overrides.
            // DO NOT mutate material objects directly — entry.Materials stores shared
            // references, so mat.SetColor would bleed across all parts using the same
            // material asset (e.g. parts extracted from the same combined GLB), causing
            // the "whack-a-mole" where restoring part A wipes emission from part B.
            // A zero-emission property block is per-renderer and avoids cross-part mutation.
            if (_isImportedModel)
            {
                var block = new MaterialPropertyBlock();
                block.SetColor("emissiveFactor", Color.black);
                block.SetColor("_EmissionColor", Color.black);

                foreach (var entry in _entries)
                {
                    if (entry.Renderer == null) continue;
                    // Apply a minimal property block that only suppresses emission.
                    // This is cleared on the next interaction that calls SetPropertyBlock(null).
                    entry.Renderer.SetPropertyBlock(block);
                }
            }
        }

        // ── Outline API ─────────────────────────────────────────────────

        /// <summary>
        /// Shows a solid-color outline around the model. Creates outline child
        /// objects on first call, then reuses them. One extra draw call per renderer.
        /// </summary>
        public void ShowOutline(Color color)
        {
            if (_outlineObjects == null)
                BuildOutlineObjects();

            var mat = GetOrCreateOutlineMaterial(color);
            if (mat == null) return;

            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                if (_outlineObjects[i] == null) continue;
                _outlineObjects[i].SetActive(true);
                _outlineObjects[i].GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        /// <summary>Hides the outline, leaving the model unchanged.</summary>
        public void HideOutline()
        {
            if (_outlineObjects == null) return;
            for (int i = 0; i < _outlineObjects.Length; i++)
            {
                if (_outlineObjects[i] != null)
                    _outlineObjects[i].SetActive(false);
            }
        }

        private void BuildOutlineObjects()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            var list = new List<GameObject>(renderers.Length);

            for (int i = 0; i < renderers.Length; i++)
            {
                var mf = renderers[i].GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                var go = new GameObject("__ose_outline__");
                go.transform.SetParent(renderers[i].transform, false);
                go.layer = renderers[i].gameObject.layer;

                var outlineMf = go.AddComponent<MeshFilter>();
                outlineMf.sharedMesh = mf.sharedMesh;

                var outlineMr = go.AddComponent<MeshRenderer>();
                outlineMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineMr.receiveShadows = false;

                go.SetActive(false);
                list.Add(go);
            }

            _outlineObjects = list.ToArray();
        }

        private static Material GetOrCreateOutlineMaterial(Color color)
        {
            if (s_outlineMaterials.TryGetValue(color, out var mat) && mat != null)
                return mat;

            if (s_outlineShader == null)
                s_outlineShader = Shader.Find("Hidden/OSE/Outline");
            if (s_outlineShader == null)
                return null;

            mat = new Material(s_outlineShader);
            mat.SetColor("_OutlineColor", color);
            mat.SetFloat("_OutlineWidth", k_DefaultOutlineWidth);
            s_outlineMaterials[color] = mat;
            return mat;
        }
    }
}
