using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class AssetManifestDefinition
    {
        public string[] modelRefs;
        public string[] textureRefs;
        public string[] effectRefs;
        public string[] uiRefs;

        /// <summary>
        /// Generated at build/sync time by <c>PackageSyncTool</c>. Lists the child-node
        /// names inside each combined GLB so the runtime <c>PackageAssetResolver</c>
        /// can resolve parts that live as named nodes inside multi-part files —
        /// without filesystem scans (which don't work over WebGL's HTTP StreamingAssets).
        /// </summary>
        public AssetNodeIndex[] nodes;
    }

    [Serializable]
    public sealed class AssetNodeIndex
    {
        /// <summary>GLB filename (no path), e.g. "frame_approved.glb".</summary>
        public string file;

        /// <summary>Names of the child transforms inside <see cref="file"/>.</summary>
        public string[] nodeNames;
    }
}
