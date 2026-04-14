using System;

namespace OSE.Content
{
    /// <summary>
    /// Where a part floats in the preview scene before the trainee grabs and places it.
    /// This is the agent-authored source of truth for staging positions.
    ///
    /// Lives in <see cref="PartDefinition.stagingPose"/> so staging data is co-located with
    /// the part definition — agents write here, not in previewConfig.
    ///
    /// <see cref="MachinePackageNormalizer"/> bakes these values into
    /// <see cref="PartPreviewPlacement.startPosition/startRotation/startScale/color"/>
    /// at load time so all existing runtime code continues to work unchanged.
    ///
    /// All values are in PreviewRoot local space (same coordinate system as assembledPosition).
    /// </summary>
    [Serializable]
    public sealed class StagingPose
    {
        public SceneFloat3    position;
        public SceneQuaternion rotation;
        /// <summary>Zero means "use Vector3.one". Leave unset to inherit the default scale.</summary>
        public SceneFloat3    scale;
        /// <summary>RGBA. Zero alpha means "use the default ColAuthored color in TTAW".</summary>
        public SceneFloat4    color;
    }


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

        /// <summary>
        /// Where this part floats in the preview scene before the trainee places it.
        /// Agent-authored source of truth — edit here, not in previewConfig.
        /// Null means use the legacy startPosition from previewConfig.partPlacements (or fallback row).
        /// </summary>
        public StagingPose stagingPose;

        /// <summary>
        /// XR grab metadata: where the hand grabs this part and how it's oriented when held.
        /// Auto-detected or authored via the Pose Editor. When present, drives
        /// <c>XRGrabInteractable.attachTransform</c> offset.
        /// </summary>
        public PartGrabConfig grabConfig;

        /// <summary>True when <see cref="grabConfig"/> carries any authored spatial data.</summary>
        public bool HasGrabConfig => grabConfig != null && grabConfig.HasGripPoint;

        /// <summary>
        /// The unique non-aggregate subassembly that owns this part, populated by
        /// <c>MachinePackageNormalizer.IndexPartOwnership</c> after deserialization.
        /// First-writer-wins during normalization; <c>PartOwnershipExclusivityPass</c>
        /// guarantees uniqueness at validation time.
        /// </summary>
        [NonSerialized] public string owningSubassemblyId;

        /// <summary>
        /// First (lowest-sequenceIndex) Place-family step that requires this
        /// part. Retained as the canonical "first placement" for legacy
        /// callers that still expect a scalar owner. Multi-placement is now
        /// supported — see <see cref="owningPlaceStepIds"/> for the full set.
        /// </summary>
        [NonSerialized] public string owningPlaceStepId;

        /// <summary>
        /// All Place-family step ids that require this part, sorted by each
        /// step's <c>sequenceIndex</c>. Populated by
        /// <see cref="Loading.MachinePackageNormalizer.IndexPartOwnership"/>.
        /// A part may be Required by multiple Place steps to represent distinct
        /// physical placements (e.g. loose alignment followed by final
        /// placement). Consumers that care about "which step placed this part
        /// at the currently-viewed seq" should walk this array and pick the
        /// most recent entry ≤ view seq — same pattern the
        /// <see cref="PoseResolver"/> already uses for subassembly stacking.
        /// </summary>
        [NonSerialized] public string[] owningPlaceStepIds;

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
