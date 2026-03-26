namespace OSE.Interaction
{
    /// <summary>
    /// Which <see cref="IToolActionPreview"/> implementation to use for a profile.
    /// Mapped in <see cref="ToolProfileRegistry"/>.
    /// </summary>
    public enum PreviewStyle
    {
        Default = 0,
        Torque = 1,
        Weld = 2,
        Cut = 3,
        SquareCheck = 4,
    }
}
