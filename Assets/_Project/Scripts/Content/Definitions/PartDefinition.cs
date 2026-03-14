using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class PartDefinition
    {
        public string id;
        public string name;
        public string displayName;
        public string category;
        public string material;
        public string function;
        public string structuralRole;
        public int quantity;
        public string[] toolIds;
        public string assetRef;
        public string ghostAssetRef;
        public string[] searchTerms;
        public bool allowPhysicalSubstitution;
        public string defaultOrientationHint;
        public string[] tags;

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

            return "Unnamed Part";
        }
    }
}
