using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step working orientation payload. Temporarily transforms the
    /// partGroup (and optionally individual parts) into a pose that makes
    /// the work area accessible — e.g., flip 180° to reach the underside.
    /// The orientation reverts automatically when the step changes.
    /// </summary>
    [Serializable]
    public sealed class StepWorkingOrientationPayload
    {
        /// <summary>
        /// Euler angles (degrees) applied to the partGroup proxy root
        /// relative to its authored fabrication pose.
        /// </summary>
        public SceneFloat3 partGroupRotation;

        /// <summary>
        /// Optional position offset (meters) in PreviewRoot local space,
        /// applied after rotation. Useful for keeping a flipped assembly
        /// at a comfortable working height.
        /// </summary>
        public SceneFloat3 partGroupPositionOffset;

        /// <summary>
        /// Optional human-readable explanation shown to the learner.
        /// When null/empty, a default message is auto-generated in
        /// <see cref="StepDefinition.BuildInstructionBody"/>.
        /// </summary>
        public string hint;

        /// <summary>
        /// Optional per-part pose overrides (escape hatch for non-rigid adjustments
        /// that can't be expressed as a single partGroup rotation).
        /// </summary>
        public StepPartPoseOverride[] partOverrides;

        /// <summary>
        /// True when the payload carries no authored intent — every field at its
        /// default (zero rotation, zero offset, no hint, no part overrides). Used
        /// by <c>MachinePackageNormalizer</c> to null out empty payloads that
        /// Unity's <c>JsonUtility</c> creates as default instances when a JSON
        /// field is absent. Without this check the runtime mistakes a default
        /// instance for an authored intent (e.g. appends "the assembly has been
        /// rotated to expose the work area" to every step's instruction text).
        /// </summary>
        public bool IsEmpty()
        {
            if (partGroupRotation.x != 0f || partGroupRotation.y != 0f || partGroupRotation.z != 0f)
                return false;
            if (partGroupPositionOffset.x != 0f || partGroupPositionOffset.y != 0f || partGroupPositionOffset.z != 0f)
                return false;
            if (!string.IsNullOrWhiteSpace(hint)) return false;
            if (partOverrides != null && partOverrides.Length > 0) return false;
            return true;
        }
    }

    /// <summary>
    /// Per-part pose override within a working orientation.
    /// Applies an additive position offset and/or replaces the assembled rotation.
    /// </summary>
    [Serializable]
    public sealed class StepPartPoseOverride
    {
        public string partId;

        /// <summary>Additive offset applied to the part's assembled position.</summary>
        public SceneFloat3 positionOffset;

        /// <summary>Euler degrees — replaces the part's assembled rotation when non-zero.</summary>
        public SceneFloat3 rotationOverride;
    }
}
