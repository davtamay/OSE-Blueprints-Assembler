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
}
