using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class SubassemblyDefinition
    {
        public string id;
        public string name;
        public string assemblyId;
        public string description;
        public string[] partIds;
        public string[] stepIds;
        public string milestoneMessage;

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();

            return "Unnamed Subassembly";
        }
    }
}
