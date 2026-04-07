using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class AssemblyDefinition
    {
        public string id;
        public string name;
        public string description;
        public string machineId;
        public string[] subassemblyIds;
        public string[] stepIds;
        public string[] dependencyAssemblyIds;
        public string learningFocus;
    }
}
