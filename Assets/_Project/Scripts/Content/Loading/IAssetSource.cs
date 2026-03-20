using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Abstraction over where package model assets are fetched from at runtime.
    /// Swap implementations to load from StreamingAssets, an S3 bucket, a CDN, or any HTTP server.
    /// </summary>
    public interface IAssetSource
    {
        /// <summary>
        /// Asynchronously loads the GLB/GLTF at <paramref name="assetRef"/> for the given
        /// <paramref name="packageId"/>, instantiates its main scene under <paramref name="parent"/>,
        /// and returns the root <see cref="GameObject"/>, or null on failure.
        /// </summary>
        Task<GameObject> LoadAsync(
            string packageId,
            string assetRef,
            Transform parent,
            CancellationToken ct = default);
    }
}
