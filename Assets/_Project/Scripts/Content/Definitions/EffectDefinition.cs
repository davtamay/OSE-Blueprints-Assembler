using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class EffectDefinition
    {
        public string id;
        public string type;
        public string assetRef;
        public string shaderRef;
        public string fallbackRef;
        public string triggerPolicy;
        public string notes;
    }
}
