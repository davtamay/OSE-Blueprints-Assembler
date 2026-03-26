namespace OSE.Interaction
{
    /// <summary>
    /// Maps Use-family profile strings to <see cref="IToolActionPreview"/> implementations.
    /// Reads <see cref="PreviewStyle"/> from <see cref="ToolProfileRegistry"/> —
    /// adding a new profile with a preview is a one-line registry entry.
    /// </summary>
    public static class ToolActionPreviewFactory
    {
        public static IToolActionPreview Create(string profile)
        {
            var style = ToolProfileRegistry.Get(profile).PreviewStyle;

            return style switch
            {
                PreviewStyle.Torque => new TorquePreview(),
                PreviewStyle.Weld => new WeldPreview(),
                PreviewStyle.Cut => new CutPreview(),
                PreviewStyle.SquareCheck => new SquareCheckPreview(),
                _ => new DefaultPreview(),
            };
        }
    }
}
