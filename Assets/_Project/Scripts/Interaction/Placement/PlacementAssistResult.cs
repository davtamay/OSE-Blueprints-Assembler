using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Result of placement assist evaluation for a single frame while dragging.
    /// Contains the original (raw) position and the assisted position after
    /// magnetic pull and corridor validation.
    /// </summary>
    public struct PlacementAssistResult
    {
        public Vector3 RawPosition;
        public Quaternion RawRotation;

        public Vector3 AssistedPosition;
        public Quaternion AssistedRotation;

        public float MagneticStrength;  // 0 = no pull, 1 = at target
        public bool IsInMagneticField;
        public bool IsInCorridor;

        /// <summary>
        /// The position to use for rendering the dragged part.
        /// Returns assisted position if in magnetic field, raw otherwise.
        /// </summary>
        public Vector3 EffectivePosition => IsInMagneticField ? AssistedPosition : RawPosition;
        public Quaternion EffectiveRotation => IsInMagneticField ? AssistedRotation : RawRotation;
    }
}
