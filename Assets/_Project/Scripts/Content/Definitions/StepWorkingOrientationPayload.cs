using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step working orientation payload. Temporarily transforms the
    /// subassembly (and optionally individual parts) into a pose that makes
    /// the work area accessible — e.g., flip 180° to reach the underside.
    /// The orientation reverts automatically when the step changes.
    /// </summary>
    [Serializable]
    public sealed class StepWorkingOrientationPayload
    {
        /// <summary>
        /// Euler angles (degrees) applied to the subassembly proxy root
        /// relative to its authored fabrication pose.
        /// </summary>
        public SceneFloat3 subassemblyRotation;

        /// <summary>
        /// Optional position offset (meters) in PreviewRoot local space,
        /// applied after rotation. Useful for keeping a flipped assembly
        /// at a comfortable working height.
        /// </summary>
        public SceneFloat3 subassemblyPositionOffset;

        /// <summary>
        /// Optional human-readable explanation shown to the learner.
        /// When null/empty, a default message is auto-generated in
        /// <see cref="StepDefinition.BuildInstructionBody"/>.
        /// </summary>
        public string hint;

        /// <summary>
        /// Optional per-part pose overrides (escape hatch for non-rigid adjustments
        /// that can't be expressed as a single subassembly rotation).
        /// </summary>
        public StepPartPoseOverride[] partOverrides;
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
