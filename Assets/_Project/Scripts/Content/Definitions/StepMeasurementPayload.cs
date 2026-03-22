using System;

namespace OSE.Content
{
    /// <summary>
    /// Authored measurement data for anchor-to-anchor measurement steps.
    /// Defines the two anchor targets, expected distance, tolerance, and display unit.
    /// </summary>
    [Serializable]
    public sealed class StepMeasurementPayload
    {
        /// <summary>Target ID for the start anchor (hook end of tape).</summary>
        public string startAnchorTargetId;

        /// <summary>Target ID for the end anchor (measurement mark).</summary>
        public string endAnchorTargetId;

        /// <summary>Expected measurement in millimeters.</summary>
        public float expectedValueMm;

        /// <summary>Acceptable tolerance in millimeters. 0 = no validation.</summary>
        public float toleranceMm;

        /// <summary>Display unit: "inches", "mm", "cm", "ft". Controls label formatting.</summary>
        public string displayUnit;

        /// <summary>
        /// Returns true when the payload contains actual authored data
        /// (not just a default instance created by JsonUtility deserialization).
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(startAnchorTargetId) ||
            !string.IsNullOrWhiteSpace(endAnchorTargetId);
    }
}
