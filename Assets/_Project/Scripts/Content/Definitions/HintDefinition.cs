using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class HintDefinition
    {
        public string id;
        public string type;
        public string title;
        public string message;
        public string targetId;
        public string partId;
        public string toolId;
        public string priority;
    }
}
