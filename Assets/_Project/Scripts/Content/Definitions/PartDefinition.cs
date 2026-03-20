using System;

namespace OSE.Content
{
    /// <summary>
    /// Reusable template for parts that share the same model, material, and tools.
    /// Parts reference a template via <see cref="PartDefinition.templateId"/> and
    /// only override fields that differ (id, displayName, function, etc.).
    /// Inflated by <c>MachinePackageNormalizer</c> after deserialization.
    /// </summary>
    [Serializable]
    public sealed class PartTemplateDefinition
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
        public string[] searchTerms;
        public bool allowPhysicalSubstitution;
        public string defaultOrientationHint;
        public string[] tags;
    }

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
        public string[] searchTerms;
        public bool allowPhysicalSubstitution;
        public string defaultOrientationHint;
        public string[] tags;

        /// <summary>
        /// Optional reference to a <see cref="PartTemplateDefinition"/>.
        /// When set, any null/empty field on this part is filled from the template.
        /// </summary>
        public string templateId;

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
