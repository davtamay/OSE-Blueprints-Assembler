using System.Collections.Generic;
using System.IO;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.GhostRender.cs  —  Editor-only ghost spawning for the Part Browser.
//
// While a pick request is open, every package part that isn't already in the
// live scene gets a transparent ghost instance at its assembled pose. Lets the
// scene-pick affordance work even when the active step has zero spawned parts.
//
// All ghosts are children of a single `_BrowserGhostRoot` GO under PreviewRoot.
// Each ghost GO is named with its partId so the existing
// HandleUtility.PickGameObject + ResolvePartIdUnderMouse path "just works" —
// no special component lookup needed. Each ghost also carries an
// EditorGhostMarker tag so other systems can ignore it.
//
// Tear-down on CancelPartPick / ConfirmPartPick destroys `_BrowserGhostRoot`
// and all its children. No state survives into Play mode.
//
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private const string GhostRootName = "_BrowserGhostRoot";

        [System.NonSerialized] private GameObject _browserGhostRoot;
        [System.NonSerialized] private Material   _ghostMaterialCache;

        /// <summary>
        /// Spawns transparent placeholders for every package part that isn't
        /// currently in the live scene. Idempotent — calling twice without a
        /// teardown rebuilds from scratch.
        /// </summary>
        private void EnsureBrowserGhosts()
        {
            TearDownBrowserGhosts();
            if (_pkg == null || string.IsNullOrEmpty(_pkgId)) return;

            var pr = GetPreviewRoot();
            if (pr == null) return;

            var parts = _pkg.GetParts();
            if (parts == null || parts.Length == 0) return;

            // Set of partIds that are *actually visible* in the scene right
            // now. The spawner registry tracks all spawned-or-registered GOs
            // regardless of activeInHierarchy, so we have to filter — without
            // that filter, we'd skip ghost spawning for parts whose runtime
            // GameObject exists but is hidden (the common case at most steps).
            var liveSet = new HashSet<string>(System.StringComparer.Ordinal);
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var s)
                && s?.SpawnedParts != null)
            {
                foreach (var go in s.SpawnedParts)
                {
                    if (go == null || string.IsNullOrEmpty(go.name)) continue;
                    if (!go.activeInHierarchy) continue;
                    var rends = go.GetComponentsInChildren<Renderer>();
                    bool anyVisible = false;
                    foreach (var r in rends) if (r != null && r.enabled) { anyVisible = true; break; }
                    if (anyVisible) liveSet.Add(go.name);
                }
            }

            string partsFolder = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/";
            var ghostMat = GetOrCreateGhostMaterial();

            _browserGhostRoot = new GameObject(GhostRootName);
            _browserGhostRoot.hideFlags = HideFlags.DontSave;
            _browserGhostRoot.transform.SetParent(pr, worldPositionStays: false);

            int spawned = 0;
            foreach (var part in parts)
            {
                if (part == null || string.IsNullOrEmpty(part.id)) continue;
                if (liveSet.Contains(part.id)) continue;

                string glbFile = ResolvePartAssetRef(part);
                if (string.IsNullOrEmpty(glbFile)) continue;
                string assetPath = partsFolder + glbFile;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null) continue;

                var ghost = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _browserGhostRoot.transform);
                if (ghost == null) continue;

                ghost.name = part.id;
                ghost.hideFlags = HideFlags.DontSave;

                // Pose at assembledPosition / stagingPose / origin, in priority order.
                Vector3    pos   = Vector3.zero;
                Quaternion rot   = Quaternion.identity;
                Vector3    scale = Vector3.one;
                if (TryGetGhostPose(part, out var p, out var r, out var sc))
                {
                    pos = p; rot = r; scale = sc;
                }
                ghost.transform.localPosition = pos;
                ghost.transform.localRotation = rot;
                if (scale.sqrMagnitude > 1e-8f) ghost.transform.localScale = scale;

                // Apply ghost material to every renderer + strip colliders.
                foreach (var rend in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    if (rend == null) continue;
                    int n = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
                    var mats = new Material[n];
                    for (int i = 0; i < n; i++) mats[i] = ghostMat;
                    rend.sharedMaterials = mats;
                }
                foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                    if (col != null) col.enabled = false;

                spawned++;
            }

            OseLog.Info($"[TTAW.GhostRender] Spawned {spawned} ghost(s) for browser. parts.Length={parts.Length}, liveSet.Count={liveSet.Count}");
            SceneView.RepaintAll();

            // Auto-frame the ghost cluster so the author sees something
            // immediately — the active step often has nothing in scene, so
            // without this the SceneView stays blank-looking.
            FrameGhostsInSceneView();
        }

        private void FrameGhostsInSceneView()
        {
            if (_browserGhostRoot == null) return;
            var renderers = _browserGhostRoot.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                OseLog.Warn("[TTAW.GhostRender] No renderers under ghost root — GLBs may have failed to instantiate or have no MeshRenderer.");
                return;
            }

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            OseLog.Info($"[TTAW.GhostRender] Ghost cluster bounds center={b.center} size={b.size} renderers={renderers.Length}");
            if (b.size.sqrMagnitude < 1e-6f) return;

            var sv = SceneView.lastActiveSceneView;
            if (sv != null) sv.Frame(b, false);
        }

        private void TearDownBrowserGhosts()
        {
            if (_browserGhostRoot != null)
            {
                Object.DestroyImmediate(_browserGhostRoot);
                _browserGhostRoot = null;
                SceneView.RepaintAll();
            }
        }

        private bool TryGetGhostPose(PartDefinition part, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scale = Vector3.one;
            var pp = FindPartPlacement(part.id);
            if (pp == null) return false;

            // A placement entry always counts as authored — even if the
            // assembled position is (0,0,0). Use scale.sqrMagnitude as the
            // signal for "this row was filled in" since unfilled rows leave
            // scale at zero (default(SceneFloat3)) but authored rows always
            // bake to non-zero scale via MachinePackageNormalizer.
            bool assembledFilled = SqrMag(pp.assembledScale) > 1e-8f;
            bool startFilled     = SqrMag(pp.startScale)     > 1e-8f;

            if (assembledFilled)
            {
                pos   = PackageJsonUtils.ToVector3(pp.assembledPosition);
                rot   = PackageJsonUtils.ToUnityQuaternion(pp.assembledRotation);
                scale = PackageJsonUtils.ToVector3(pp.assembledScale);
                return true;
            }
            if (startFilled)
            {
                pos   = PackageJsonUtils.ToVector3(pp.startPosition);
                rot   = PackageJsonUtils.ToUnityQuaternion(pp.startRotation);
                scale = PackageJsonUtils.ToVector3(pp.startScale);
                return true;
            }
            // Placement exists but neither pose is filled — read raw position
            // anyway and use unit scale. Better to render at origin than skip.
            pos   = PackageJsonUtils.ToVector3(pp.assembledPosition);
            rot   = PackageJsonUtils.ToUnityQuaternion(pp.assembledRotation);
            scale = Vector3.one;
            return true;
        }

        private static float SqrMag(SceneFloat3 v) => v.x * v.x + v.y * v.y + v.z * v.z;

        private Material GetOrCreateGhostMaterial()
        {
            if (_ghostMaterialCache != null) return _ghostMaterialCache;

            // Solid tinted material so ghosts are guaranteed visible regardless
            // of render pipeline. Transparency in URP requires shader-keyword
            // gymnastics that fail silently when properties don't exist —
            // opaque tint is dependable and still reads as "not real geometry"
            // because of the uniform color across all parts. We can layer real
            // transparency back in once visibility is proven.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Hidden/InternalErrorShader");
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            var ghostColor = new Color(0.45f, 0.70f, 1.0f, 1.0f);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", ghostColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     ghostColor);

            _ghostMaterialCache = mat;
            return _ghostMaterialCache;
        }

        private void DisposeGhostMaterial()
        {
            if (_ghostMaterialCache != null)
            {
                Object.DestroyImmediate(_ghostMaterialCache);
                _ghostMaterialCache = null;
            }
        }
    }

}
