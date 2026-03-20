using System;
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

            bool instantiated = gltf.InstantiateMainScene(wrapper.transform);
            gltf.Dispose();

            if (!instantiated)
            {
                UnityEngine.Object.Destroy(wrapper);
                OseLog.Warn($"[StreamingAssetsSource] InstantiateMainScene failed for '{assetRef}'.");
                return null;
            }

            return wrapper;
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
