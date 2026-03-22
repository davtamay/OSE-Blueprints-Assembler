namespace OSE.Interaction.V2
{
    /// <summary>
    /// Semantic classification of how the camera should frame a step's spatial context.
    /// Resolved from family + profile, or authored explicitly via StepDefinition.viewMode.
    /// </summary>
    public enum ViewMode
    {
        /// <summary>Frame the source part and its ghost/target zone together.</summary>
        SourceAndTarget = 0,
        /// <summary>Frame both endpoints of a connection or measurement.</summary>
        PairEndpoints = 1,
        /// <summary>Frame the tool target area at working distance.</summary>
        WorkZone = 2,
        /// <summary>Frame a linear work path (cut line, weld seam).</summary>
        PathView = 3,
        /// <summary>Wide shot of the full assembly.</summary>
        Overview = 4,
        /// <summary>Close-up detail view for verification.</summary>
        Inspect = 5
    }
}
