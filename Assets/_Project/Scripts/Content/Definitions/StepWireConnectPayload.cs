using System;

namespace OSE.Content
{
    /// <summary>
    /// Polarity-aware wire connection payload for Connect-family steps with
    /// <c>profile: "WireConnect"</c>.
    ///
    /// Each entry in <see cref="wires"/> maps to one targetId (by index or by targetId).
    /// Port spheres are colored by polarity type instead of the generic red/blue A/B pair.
    /// When <see cref="enforcePortOrder"/> is true, the learner must click portA first;
    /// clicking portB first triggers a polarity-order warning and requires a retry.
    ///
    /// JSON example:
    /// <code>
    /// "wireConnect": {
    ///   "enforcePortOrder": false,
    ///   "wires": [
    ///     {
    ///       "targetId": "target_ramps_psu_12v",
    ///       "portAPolarityType": "+12V",
    ///       "portBPolarityType": "GND",
    ///       "portAConnectorType": "screw_terminal",
    ///       "portBConnectorType": "screw_terminal"
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public sealed class StepWireConnectPayload
    {
        /// <summary>
        /// Per-wire polarity definitions, parallel to the step's <c>targetIds</c>.
        /// Each entry carries the port polarity and connector type for one wire run.
        /// </summary>
        public WireConnectEntry[] wires;

        /// <summary>
        /// When true, the learner must click portA before portB.
        /// Clicking portB first shows a polarity-order hint and resets that wire's
        /// confirmation state. Default: false (either order accepted).
        /// </summary>
        public bool enforcePortOrder;

        /// <summary>
        /// Returns true when the payload carries at least one wire entry.
        /// JsonUtility always instantiates the payload even on empty steps,
        /// so callers must check <see cref="IsConfigured"/> before using wire data.
        /// </summary>
        public bool IsConfigured => wires != null && wires.Length > 0;
    }

    /// <summary>
    /// Polarity and connector definition for one wire run in a
    /// <see cref="StepWireConnectPayload"/>.
    /// </summary>
    [Serializable]
    public sealed class WireConnectEntry
    {
        /// <summary>
        /// The targetId this entry applies to. When null, matched by array index
        /// against the step's <c>targetIds</c> array.
        /// </summary>
        public string targetId;

        /// <summary>
        /// The part this wire represents (e.g. <c>"wire_psu_12v"</c>). When set, the
        /// runtime treats this part identically to a <c>requiredPartId</c> — reveal,
        /// hide, state tracking, camera framing, and UI display all include it.
        /// Eliminates the need for a separate PART task row on Connect-family steps.
        /// </summary>
        public string partId;

        // ── Port polarity ──────────────────────────────────────────────────────

        /// <summary>
        /// Polarity/signal type at portA.
        /// <para>Accepted tokens: "+12V", "+5V", "+", "GND", "-", "-12V",
        /// "signal", "pwm", "enable", "thermistor", "fan", "endstop".</para>
        /// <para>Controls port sphere color and polarity-order validation.
        /// When null, falls back to the generic red A/B color.</para>
        /// </summary>
        public string portAPolarityType;

        /// <summary>
        /// Polarity/signal type at portB. Same token set as <see cref="portAPolarityType"/>.
        /// </summary>
        public string portBPolarityType;

        // ── Connector types ────────────────────────────────────────────────────

        /// <summary>
        /// Physical connector at the portA end.
        /// <para>Accepted tokens: "dupont_1pin", "dupont_2pin", "dupont_3pin",
        /// "jst_xh_2pin", "jst_xh_3pin", "screw_terminal", "spade", "barrel_jack",
        /// "bare_wire", "molex".</para>
        /// <para>Optional — used for hint text and future connector-type validation.</para>
        /// </summary>
        public string portAConnectorType;

        /// <summary>
        /// Physical connector at the portB end. Same token set as
        /// <see cref="portAConnectorType"/>.
        /// </summary>
        public string portBConnectorType;

        /// <summary>
        /// When true, swapping portA and portB is a polarity violation that blocks
        /// step completion (e.g., +12V must not connect to GND terminal).
        /// When false, reversed connection is allowed (e.g., stepper motor coils).
        /// Default: true.
        /// </summary>
        public bool polarityOrderMatters = true;

        // ── Resolved helpers ───────────────────────────────────────────────────

        /// <summary>Returns true when portA is a positive power rail (+12V, +5V, +).</summary>
        public bool IsPortAPositive =>
            portAPolarityType == "+12V" || portAPolarityType == "+5V" || portAPolarityType == "+";

        /// <summary>Returns true when portA is ground or negative rail.</summary>
        public bool IsPortAGround =>
            portAPolarityType == "GND" || portAPolarityType == "-" || portAPolarityType == "-12V";

        /// <summary>Returns true when portA is a logic/signal line.</summary>
        public bool IsPortASignal =>
            portAPolarityType == "signal" || portAPolarityType == "pwm" ||
            portAPolarityType == "enable" || portAPolarityType == "thermistor" ||
            portAPolarityType == "fan" || portAPolarityType == "endstop";
    }
}
