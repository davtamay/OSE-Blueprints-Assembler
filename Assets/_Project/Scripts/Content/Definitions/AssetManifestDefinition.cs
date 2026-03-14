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
    }
}
