using System;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Maps Use-family profile strings to <see cref="IToolActionPreview"/> implementations.
    /// Cascade: explicit previewType → profile default → DefaultPreview.
    /// </summary>
    public static class ToolActionPreviewFactory
    {
        public static IToolActionPreview Create(string profile)
        {
            if (string.IsNullOrEmpty(profile))
                return new DefaultPreview();

            // Case-insensitive match against canonical profile constants
            if (profile.Equals(ToolActionProfiles.Torque, StringComparison.OrdinalIgnoreCase))
                return new TorquePreview();

            if (profile.Equals(ToolActionProfiles.Clamp, StringComparison.OrdinalIgnoreCase))
                return new DefaultPreview();

            if (profile.Equals(ToolActionProfiles.Weld, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("solder", StringComparison.OrdinalIgnoreCase))
                return new WeldPreview();

            if (profile.Equals(ToolActionProfiles.Cut, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("grind", StringComparison.OrdinalIgnoreCase))
                return new CutPreview();

            return new DefaultPreview();
        }
    }
}
