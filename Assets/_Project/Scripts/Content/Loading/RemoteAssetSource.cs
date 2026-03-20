using System;
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

            bool instantiated = gltf.InstantiateMainScene(wrapper.transform);
            gltf.Dispose();

            if (!instantiated)
            {
                UnityEngine.Object.Destroy(wrapper);
                OseLog.Warn($"[RemoteAssetSource] InstantiateMainScene failed for '{assetRef}'.");
                return null;
            }

            return wrapper;
        }

        private string BuildUri(string packageId, string assetRef) =>
            $"{_baseUrl}/{packageId}/{assetRef.Replace('\\', '/')}";
    }
}
