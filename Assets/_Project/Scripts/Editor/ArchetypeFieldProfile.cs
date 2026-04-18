namespace OSE.Editor
{
    /// <summary>
    /// Declares which fields are relevant for a given part-effect archetype.
    /// Consumed by the TTAW Interaction Panel to hide irrelevant fields per archetype
    /// (e.g. <c>lerp</c> shows no axis/rotation fields, <c>clamp_hold</c> shows nothing).
    ///
    /// <para>To register a new archetype: add a <see cref="TaskFieldProfile"/>-style
    /// entry to <see cref="ArchetypeFieldRegistry"/>. No other UI code changes.</para>
    /// </summary>
    internal readonly struct ArchetypeFieldProfile
    {
        /// <summary>Show the axis space + vector fields.</summary>
        public readonly bool ShowAxis;

        /// <summary>Show the distance field ("auto" toggle + override).</summary>
        public readonly bool ShowDistance;

        /// <summary>Show the total-rotation-degrees radio/field.</summary>
        public readonly bool ShowTotalRotations;

        /// <summary>Show the deg-per-meter thread-pitch field.</summary>
        public readonly bool ShowRotationPerUnit;

        /// <summary>Show the easing dropdown.</summary>
        public readonly bool ShowEasing;

        /// <summary>Show the "tool follows part" toggle.</summary>
        public readonly bool ShowFollowPart;

        /// <summary>One-sentence description of the archetype, shown inline under the dropdown.</summary>
        public readonly string HelpText;

        public ArchetypeFieldProfile(
            bool showAxis            = false,
            bool showDistance        = false,
            bool showTotalRotations  = false,
            bool showRotationPerUnit = false,
            bool showEasing          = false,
            bool showFollowPart      = false,
            string helpText          = null)
        {
            ShowAxis            = showAxis;
            ShowDistance        = showDistance;
            ShowTotalRotations  = showTotalRotations;
            ShowRotationPerUnit = showRotationPerUnit;
            ShowEasing          = showEasing;
            ShowFollowPart      = showFollowPart;
            HelpText            = helpText;
        }
    }
}
