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
    /// Loads package model assets from a remote HTTP/HTTPS endpoint (S3, CDN, custom server).
    /// Assign to <see cref="OSE.UI.Root.PackagePartSpawner.AssetSource"/> at startup to switch
    /// the runtime away from StreamingAssets.
    ///
    /// The resolved URL has the form:
    ///   {BaseUrl}/{packageId}/{assetRef}
    ///
    /// Example:
    ///   BaseUrl  = "https://assets.example.com/packages"
    ///   packageId= "power_cube_frame"
    ///   assetRef = "assets/tools/tool_welder.glb"
    ///   → https://assets.example.com/packages/power_cube_frame/assets/tools/tool_welder.glb
    /// </summary>
    public sealed class RemoteAssetSource : IAssetSource
    {
        private readonly string _baseUrl;

        /// <param name="baseUrl">
        /// Base URL without a trailing slash, e.g. <c>https://bucket.s3.amazonaws.com/packages</c>.
        /// </param>
        public RemoteAssetSource(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("baseUrl must not be empty.", nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
        }

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
                OseLog.Warn($"[RemoteAssetSource] Failed to load '{assetRef}' for package '{packageId}' (uri={uri}).");
                return null;
            }

            var wrapper = new GameObject(System.IO.Path.GetFileNameWithoutExtension(assetRef));
            wrapper.transform.SetParent(parent, false);

            bool instantiated = await gltf.InstantiateMainSceneAsync(wrapper.transform);
            gltf.Dispose();

            if (!instantiated)
            {
                UnityEngine.Object.Destroy(wrapper);
                OseLog.Warn($"[RemoteAssetSource] InstantiateMainScene failed for '{assetRef}'.");
                return null;
            }

            return wrapper;
        }

        private readonly Dictionary<string, GameObject> _combinedRootCache =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private GameObject _combinedCacheHolder;

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
                    _combinedCacheHolder = new GameObject("OSE.RemoteCombinedGlbCache");
                    _combinedCacheHolder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_combinedCacheHolder);
                }

                root = await LoadAsync(packageId, assetRef, _combinedCacheHolder.transform, ct);
                if (root == null) return null;
                _combinedRootCache[cacheKey] = root;
            }

            Transform node = FindNodeRecursive(root.transform, nodeName);
            if (node == null) return null;

            GameObject copy = UnityEngine.Object.Instantiate(node.gameObject, parent);
            copy.name = node.name;
            return copy;
        }

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

        private string BuildUri(string packageId, string assetRef) =>
            $"{_baseUrl}/{packageId}/{assetRef.Replace('\\', '/')}";
    }
}
