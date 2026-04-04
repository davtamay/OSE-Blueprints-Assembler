using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OSE.Content.Loading;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Loads package model assets from AssetDatabase (editor) or <see cref="IAssetSource"/>
    /// (runtime). Extracted from <see cref="PackagePartSpawner"/> for single-responsibility.
    ///
    /// In the Editor, assets are resolved synchronously via AssetDatabase.
    /// In builds, use <see cref="LoadAsync"/> so assets stream from <see cref="AssetSource"/>.
    /// </summary>
    internal sealed class PartAssetLoader
    {
        // Suppresses repeated "not in AssetDatabase" warnings per session.
        private static readonly HashSet<string> MissingAssetWarnings =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Func<string> _getPackageId;
        private readonly Func<Transform> _getPreviewRoot;

        /// <summary>
        /// Runtime asset source for builds. Set after construction;
        /// ignored in the Editor where AssetDatabase is used instead.
        /// </summary>
        public IAssetSource AssetSource { get; set; }

        public PartAssetLoader(Func<string> getPackageId, Func<Transform> getPreviewRoot)
        {
            _getPackageId  = getPackageId;
            _getPreviewRoot = getPreviewRoot;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Asynchronously loads a package model asset.
        /// In the Editor falls back to the synchronous AssetDatabase path.
        /// </summary>
        public async Task<GameObject> LoadAsync(
            string assetRef,
            Transform parent,
            CancellationToken ct)
        {
            if (!IsValidRef(assetRef))
                return null;

            Transform target = parent != null ? parent : _getPreviewRoot();

#if UNITY_EDITOR
            return TryLoad(assetRef);
#else
            if (AssetSource == null)
            {
                OseLog.Warn("[PartAssetLoader] AssetSource is null — cannot load asset at runtime.");
                return null;
            }

            string packageId = _getPackageId();
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            GameObject go = await AssetSource.LoadAsync(packageId, assetRef, target, ct);
            if (go != null)
                PackagePartSpawner.EnsureColliders(go);
            return go;
#endif
        }

        /// <summary>
        /// Synchronously loads a package model asset from AssetDatabase.
        /// <para>In builds, use <see cref="LoadAsync"/> instead — this always returns null outside the Editor.</para>
        /// </summary>
        public GameObject TryLoad(string assetRef)
        {
            if (!IsValidRef(assetRef))
                return null;

#if UNITY_EDITOR
            string packageId = _getPackageId();
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            string normalizedRef = assetRef.Replace('\\', '/');
            string assetPath = $"Assets/_Project/Data/Packages/{packageId}/{normalizedRef}";

            var prefab = LoadFromDatabase(assetPath);

            // Fallback: try with assets/parts/ prefix when the ref is a bare filename
            if (prefab == null && !normalizedRef.Contains("/"))
            {
                string prefixedPath = $"Assets/_Project/Data/Packages/{packageId}/assets/parts/{normalizedRef}";
                prefab = LoadFromDatabase(prefixedPath);
                if (prefab != null)
                    assetPath = prefixedPath;
            }

            // .glb → .gltf fallback
            if (prefab == null && assetPath.EndsWith(".glb"))
            {
                string gltfPath = Path.ChangeExtension(assetPath, ".gltf");
                prefab = LoadFromDatabase(gltfPath);
                if (prefab != null)
                    OseLog.Warn($"[PartAssetLoader] Fallback loaded GLTF for missing GLB ref: {assetPath} -> {gltfPath}");
            }

            if (prefab == null)
            {
                if (MissingAssetWarnings.Add(assetPath))
                {
                    if (File.Exists(assetPath))
                        OseLog.Warn($"[PartAssetLoader] Model exists but could not be imported as GameObject: {assetPath}. Falling back to primitive.");
                    else
                        OseLog.Warn($"[PartAssetLoader] Asset prefab not in AssetDatabase: {assetPath}");
                }
                return null;
            }

            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(_getPreviewRoot(), false);
            if (Application.isPlaying)
                PackagePartSpawner.EnsureColliders(instance);
            else
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(col);

            return instance;
#else
            OseLog.Warn("[PartAssetLoader] TryLoad called in a build — use LoadAsync instead.");
            return null;
#endif
        }

        /// <summary>
        /// Loads a named node from a combined GLB file, caching the loaded root per asset path.
        /// Returns a new independent GameObject containing the requested subtree, or null.
        /// </summary>
        public GameObject TryLoadCombinedNode(
            string assetRef,
            string nodeName,
            Dictionary<string, GameObject> cache)
        {
#if UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(assetRef) || string.IsNullOrWhiteSpace(nodeName))
                return null;

            string packageId = _getPackageId();
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            string assetPath = $"Assets/_Project/Data/Packages/{packageId}/{assetRef.Replace('\\', '/')}";

            if (!cache.TryGetValue(assetPath, out GameObject root) || root == null)
            {
                var prefab = LoadFromDatabase(assetPath);
                if (prefab == null) return null;

                root = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
                root.transform.SetParent(_getPreviewRoot(), false);
                cache[assetPath] = root;
            }

            Transform node = FindNodeRecursive(root.transform, nodeName);
            if (node == null)
                node = FindNodeByNormalizedName(root.transform, PackageAssetResolver.NormalizeToPartId(nodeName));

            if (node == null) return null;

            GameObject copy = UnityEngine.Object.Instantiate(node.gameObject, _getPreviewRoot());
            copy.name = node.name;

            if (!Application.isPlaying)
                foreach (var col in copy.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(col);

            return copy;
#else
            return null;
#endif
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private bool IsValidRef(string assetRef) =>
            !string.IsNullOrWhiteSpace(assetRef) &&
            !string.IsNullOrWhiteSpace(_getPackageId());

#if UNITY_EDITOR
        private static GameObject LoadFromDatabase(string assetPath)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null) return prefab;
            foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (asset is GameObject go) return go;
            return null;
        }
#endif

        private static Transform FindNodeRecursive(Transform t, string name)
        {
            if (t.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return t;
            foreach (Transform child in t)
            {
                var found = FindNodeRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindNodeByNormalizedName(Transform t, string normalizedId)
        {
            foreach (Transform child in t)
            {
                if (PackageAssetResolver.NormalizeToPartId(child.name)
                    .Equals(normalizedId, StringComparison.OrdinalIgnoreCase))
                    return child;
                var found = FindNodeByNormalizedName(child, normalizedId);
                if (found != null) return found;
            }
            return null;
        }
    }
}
