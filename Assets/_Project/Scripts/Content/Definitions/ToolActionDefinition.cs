using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class ToolActionDefinition
    {
        public string id;
        public string toolId;
        public string actionType;
        public string targetId;
        public int requiredCount = 1;
        public string successMessage;
        public string failureMessage;
    }
}
