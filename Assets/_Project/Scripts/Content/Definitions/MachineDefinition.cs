using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class MachineDefinition
    {
        public string id;
        public string name;
        public string displayName;
        public string description;
        public string difficulty;
        public int estimatedBuildTimeMinutes;
        public string[] learningObjectives;
        public string recommendedMode;
        public string[] entryAssemblyIds;
        public string[] prerequisiteNotes;
        public SourceReferenceDefinition[] sourceReferences;
        public string introImageRef;

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return "Unnamed Machine";
        }
    }
}
