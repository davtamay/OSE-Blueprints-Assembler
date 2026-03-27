namespace OSE.Content
{
    /// <summary>
    /// The four fundamental step interaction families.
    /// Every step resolves to exactly one family.
    /// </summary>
    public enum StepFamily
    {
        /// <summary>Spatial placement onto preview targets.</summary>
        Place = 0,
        /// <summary>Tool activation on targets (torque, weld, cut, measure).</summary>
        Use = 1,
        /// <summary>Two-endpoint connection (pipe, cable).</summary>
        Connect = 2,
        /// <summary>Non-spatial acknowledgement or verification.</summary>
        Confirm = 3
    }

    /// <summary>
    /// Family-scoped profile that refines behavior within a step family.
    /// Resolved from <see cref="StepDefinition.profile"/> string at runtime.
    /// </summary>
    public enum StepProfile
    {
        /// <summary>No explicit profile — family default behavior applies.</summary>
        None = 0,

        // ── Place-family profiles ──

        /// <summary>Clamp-assisted placement (spawns persistent tool).</summary>
        Clamp = 10,
        /// <summary>Constrained adjustable-fit along an axis.</summary>
        AxisFit = 11,

        // ── Use-family profiles ──

        /// <summary>Rotational tightening (wrench, socket).</summary>
        Torque = 20,
        /// <summary>Linear weld/solder pass.</summary>
        Weld = 21,
        /// <summary>Linear cut/grind pass.</summary>
        Cut = 22,
        /// <summary>Impact strike (hammer, mallet).</summary>
        Strike = 23,
        /// <summary>Anchor-to-anchor measurement (tape, ruler).</summary>
        Measure = 24,
        /// <summary>Verification overlay (framing square).</summary>
        SquareCheck = 25,

        // ── Connect-family profiles ──

        /// <summary>Two-port cable/pipe connection.</summary>
        Cable = 30
    }
}
