using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class ToolDefinition
    {
        public string id;
        public string name;
        public string category;
        public string purpose;
        public string usageNotes;
        public string safetyNotes;
        public string[] searchTerms;
        public string assetRef;

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return "Unnamed Tool";
        }
    }
}
