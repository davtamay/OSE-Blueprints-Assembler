using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-tool-action override payload. Lives on
    /// <see cref="ToolActionDefinition.previewConfig"/>. Every field is
    /// optional; a sentinel value (0 for floats, empty for strings, alpha=0
    /// for colours) means "the preview class's hardcoded default wins."
    /// Preview classes read via <c>ToolActionPreviewBase.Override(authored,
    /// fallback)</c> so zero-schema-overhead overrides stay zero-behaviour.
    ///
    /// Grouped by profile. Universal fields apply to every preview; the
    /// profile-specific groups (weld*, drill*, torque*, cut*, square*) are
    /// inspected only by the corresponding preview class. Authors see a
    /// profile-filtered subset in the TTAW Tool Action Config UI.
    /// </summary>
    [Serializable]
    public sealed class ToolActionPreviewConfig
    {
        // ── Universal (every preview / controller) ────────────────────────

        /// <summary>Overrides <c>preview.Duration</c> (seconds). 0 = default.</summary>
        public float duration;

        /// <summary>Overrides <c>ToolActionPreviewController.BaseApproachDuration</c> (seconds). 0 = default (0.5).</summary>
        public float approachDuration;

        /// <summary>Overrides <c>BaseReturnDuration</c> (seconds). 0 = default (0.35).</summary>
        public float returnDuration;

        /// <summary>Multiplier converting drag pixels to progress. 0 = preview default.</summary>
        public float guidedDragScale;

        /// <summary>Seconds of idle input before auto-assist engages. 0 = preview default.</summary>
        public float autoAssistDelay;

        /// <summary>Auto-assist progress per second. 0 = preview default.</summary>
        public float autoAssistRate;

        /// <summary>Overrides the profile's framing distance (metres). 0 = profile default.</summary>
        public float framingDistance;

        /// <summary>Overrides the profile's working distance (metres). 0 = profile default.</summary>
        public float workingDistance;

        /// <summary>Overrides the profile's approach tilt (degrees). 0 = profile default.</summary>
        public float approachTiltDegrees;

        // ── Weld-specific ─────────────────────────────────────────────────

        public float weldArcSpawnThreshold;      // 0..1 progress; default 0.1
        public float weldBeadSpawnThreshold;     // 0..1 progress; default 0.2
        public float weldBeadWidth;              // metres;        default 0.004
        public float weldBeadWindowStart;        // 0..1 progress; default 0.2
        public float weldBeadWindowEnd;          // 0..1 progress; default 0.9
        public float weldWobbleAmplitude;        // radians;       default 0.12
        public float weldWobbleFrequency;        // rad/s;         default 40
        public SceneFloat4 weldBeadHotColor;     // default (0.85, 0.82, 0.72, 1)
        public SceneFloat4 weldBeadCoolColor;    // default (0.55, 0.55, 0.52, 1)
        public float weldCoolerDuration;         // seconds;       default 2.0

        // ── Drill-specific ────────────────────────────────────────────────

        public float drillShakeFrequency;        // Hz;            default 55
        public float drillShakeAmplitude;        // metres;        default 0.0004
        public float drillRampUpEnd;             // 0..1 progress; default 0.15
        public float drillRampDownStart;         // 0..1 progress; default 0.85
        public float drillSpark1Threshold;       // 0..1 progress; default 0.4
        public float drillSpark1Scale;           // metres;        default 0.06
        public float drillSpark2Threshold;       // 0..1 progress; default 0.8
        public float drillSpark2Scale;           // metres;        default 0.04
        public float drillGlowIntensity;         // multiplier;    default 0.5
        public SceneFloat4 drillGlowColor;       // default (0.3, 0.7, 1, 1)

        // ── Torque-specific ───────────────────────────────────────────────

        public float torqueTargetAngle;          // degrees;       default 120
        public float torqueGestureArc;           // degrees;       default 180
        public float torqueMinRadius;            // pixels;        default 10
        public float torqueDragFallbackScale;    // drag→progress; default 0.008
        public float torqueSparkThreshold;       // 0..1 progress; default 0.5
        public float torqueSparkScale;           // metres;        default 0.1

        // ── Cut-specific ──────────────────────────────────────────────────

        public float cutEmissionThreshold;       // 0..1 progress; default 0.15
        public SceneFloat4 cutGlowColor;         // default (1, 0.5, 0.1, 1)
        public float cutSpark1Threshold;         // 0..1 progress; default 0.2
        public float cutSpark1Scale;             // metres;        default 0.12
        public float cutSpark2Threshold;         // 0..1 progress; default 0.6
        public float cutSpark2Scale;             // metres;        default 0.08
        public float cutLineSpawn;               // 0..1 progress; default 0.25
        public float cutLineWindowEnd;           // 0..1 progress; default 0.9
        public float cutVibrationWindowStart;    // 0..1 progress; default 0.15
        public float cutVibrationWindowEnd;      // 0..1 progress; default 0.9
        public float cutVibrationFrequency;      // Hz;            default 80
        public float cutVibrationAmplitude;      // metres;        default 0.00015 (= 0.15 × 0.001)
        public SceneFloat4 cutLineColor;         // default (0.15, 0.1, 0.05, 0.9)

        // ── SquareCheck-specific ──────────────────────────────────────────

        public float squareSettleDistance;       // metres;        default 0.015
        public float squareSettleEnd;            // 0..1 progress; default 0.4
        public float squareHoldEnd;              // 0..1 progress; default 0.7
        public SceneFloat4 squareGlowColor;      // default (0.1, 0.8, 0.2, 1)
        public float squarePulseFrequency;       // cycles/action; default 1.0
    }
}
