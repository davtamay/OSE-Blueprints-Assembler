namespace OSE.Interaction
{
    /// <summary>
    /// Canonical archetype keys for <see cref="IPartEffect"/> implementations
    /// registered with <see cref="PartEffectRegistry"/>. Stored as constant strings
    /// (not an enum) so ToolActionDefinition.interaction.archetype stays forward-
    /// compatible with archetypes added by future content packs.
    /// </summary>
    public static class PartEffectArchetypes
    {
        /// <summary>Generic A→B lerp of local transform. Default when no payload is authored.</summary>
        public const string Lerp = "lerp";

        /// <summary>Translate along an axis to a distance (press-fit, pin-home).</summary>
        public const string AxisPlunge = "axis_plunge";

        /// <summary>Rotate about an axis in place (wrench on captive nut, valve handle).</summary>
        public const string RotateInPlace = "rotate_in_place";

        /// <summary>Translate + rotate locked by pitch (drilling/screwing into a tapped hole).</summary>
        public const string ThreadIn = "thread_in";

        /// <summary>No motion. Tool remains attached to the part for the action phase.</summary>
        public const string ClampHold = "clamp_hold";
    }
}
