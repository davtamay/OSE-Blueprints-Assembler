using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using OSE.Core;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Loads package model assets from <c>StreamingAssets/MachinePackages/</c> using glTFast.
    /// This is the default runtime source used by <see cref="OSE.UI.Root.PackagePartSpawner"/>.
    /// </summary>
    public sealed class StreamingAssetsSource : IAssetSource
    {
        private const string FolderName = "MachinePackages";

        // Per-session cache of loaded combined-GLB roots, keyed by "{packageId}/{assetRef}".
        // Lets a multi-part GLB load once and serve N cloned child nodes
        // instead of re-fetching + re-importing the same bytes per node.
        private readonly Dictionary<string, GameObject> _combinedRootCache =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private GameObject _combinedCacheHolder;

        // Retained GltfImport instances. Calling gltf.Dispose() immediately
        // after InstantiateMainSceneAsync destroys the Mesh asset objects
        // that the freshly-instantiated MeshFilters reference — leaving
        // every part GameObject with a "Missing (Mesh)" filter and no
        // draw call submission. Hold the imports alive for the lifetime
        // of the spawned GameObjects (cleared on package change via
        // ClearCombinedCache) so the meshes survive.
        private readonly List<GltfImport> _retainedImports = new List<GltfImport>();

        public async Task<GameObject> LoadAsync(
            string packageId,
            string assetRef,
            Transform parent,
            CancellationToken ct = default)
        {
            string uri = BuildUri(packageId, assetRef);

            var gltf = new GltfImport();
            bool success = await gltf.Load(uri, cancellationToken: ct);

            if (!success)
            {
                gltf.Dispose();
                OseLog.Warn($"[StreamingAssetsSource] Failed to load '{assetRef}' for package '{packageId}' (uri={uri}).");
                return null;
            }

            var wrapper = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetRef));
            wrapper.transform.SetParent(parent, false);

            bool instantiated = await gltf.InstantiateMainSceneAsync(wrapper.transform);

            if (!instantiated)
            {
                gltf.Dispose();
                UnityEngine.Object.Destroy(wrapper);
                OseLog.Warn($"[StreamingAssetsSource] InstantiateMainScene failed for '{assetRef}'.");
                return null;
            }

            // Retain (do NOT Dispose). Disposing here destroys the Mesh
            // assets the new MeshFilters point at — see _retainedImports
            // comment above. Disposed in bulk on ClearCombinedCache.
            _retainedImports.Add(gltf);
            return wrapper;
        }

        public async Task<GameObject> LoadCombinedNodeAsync(
            string packageId,
            string assetRef,
            string nodeName,
            Transform parent,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(assetRef) || string.IsNullOrWhiteSpace(nodeName))
                return null;

            string cacheKey = packageId + "/" + assetRef;
            if (!_combinedRootCache.TryGetValue(cacheKey, out GameObject root) || root == null)
            {
                if (_combinedCacheHolder == null)
                {
                    _combinedCacheHolder = new GameObject("OSE.CombinedGlbCache");
                    _combinedCacheHolder.SetActive(false); // hidden template root
                    UnityEngine.Object.DontDestroyOnLoad(_combinedCacheHolder);
                }

                root = await LoadAsync(packageId, assetRef, _combinedCacheHolder.transform, ct);
                if (root == null)
                {
                    OseLog.Warn($"[StreamingAssetsSource] LoadCombinedNodeAsync: combined GLB '{assetRef}' " +
                                $"failed to load for package '{packageId}'.");
                    return null;
                }
                _combinedRootCache[cacheKey] = root;
            }

            Transform node = FindNodeRecursive(root.transform, nodeName);
            if (node == null)
            {
                OseLog.Warn($"[StreamingAssetsSource] LoadCombinedNodeAsync: node '{nodeName}' not found " +
                            $"inside '{assetRef}' (package '{packageId}').");
                return null;
            }

            GameObject copy = UnityEngine.Object.Instantiate(node.gameObject, parent);
            copy.name = node.name;
            return copy;
        }

        /// <summary>
        /// Drops cached combined-GLB roots. Call between sessions / package switches so
        /// stale meshes don't survive into a different package's session.
        /// </summary>
        public void ClearCombinedCache()
        {
            foreach (var kv in _combinedRootCache)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _combinedRootCache.Clear();
            if (_combinedCacheHolder != null)
            {
                UnityEngine.Object.Destroy(_combinedCacheHolder);
                _combinedCacheHolder = null;
            }
            // Now safe to Dispose the imports — the GameObjects that
            // referenced their Meshes are about to be replaced by the new
            // package's spawn pass. (If a caller clears the cache while
            // GameObjects are still using these meshes, those GameObjects
            // become Missing(Mesh) again — that's the intended contract.)
            for (int i = 0; i < _retainedImports.Count; i++)
                _retainedImports[i]?.Dispose();
            _retainedImports.Clear();
        }

        private static Transform FindNodeRecursive(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                var found = FindNodeRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Builds the URI for a given package asset.
        /// On Android and WebGL <c>Application.streamingAssetsPath</c> is already a URL;
        /// on desktop we prefix <c>file://</c>.
        /// </summary>
        private static string BuildUri(string packageId, string assetRef)
        {
            string normalized = assetRef.Replace('\\', '/');
            string basePath   = Application.streamingAssetsPath;

            // streamingAssetsPath already contains a scheme on Android (jar:) and WebGL (http:)
            if (basePath.Contains("://", StringComparison.Ordinal))
                return $"{basePath}/{FolderName}/{packageId}/{normalized}";

            return $"file://{basePath}/{FolderName}/{packageId}/{normalized}";
        }
    }
}
