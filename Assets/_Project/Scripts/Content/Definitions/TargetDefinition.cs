using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class TargetDefinition
    {
        public string id;
        public string name;
        public string anchorRef;
        public string description;
        public string associatedPartId;
        public string associatedSubassemblyId;
        public string[] tags;
    }
}
