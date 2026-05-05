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

        /// <summary>
        /// Asynchronously loads <paramref name="assetRef"/>, locates the child node named
        /// <paramref name="nodeName"/> inside it, and returns an independent
        /// <see cref="GameObject"/> containing that subtree under <paramref name="parent"/>.
        /// Returns null if the file or the node cannot be loaded.
        ///
        /// Implementations should cache the loaded GLB root keyed by (packageId, assetRef)
        /// so a multi-part GLB (e.g. <c>frame_approved.glb</c> with 24 child bars) is loaded
        /// once per session and child nodes are cloned from the cached root.
        /// </summary>
        Task<GameObject> LoadCombinedNodeAsync(
            string packageId,
            string assetRef,
            string nodeName,
            Transform parent,
            CancellationToken ct = default);
    }
}
