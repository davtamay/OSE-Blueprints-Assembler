namespace OSE.Editor
{
    /// <summary>
    /// Maps archetype keys to their <see cref="ArchetypeFieldProfile"/>.
    /// Keys mirror <c>OSE.Interaction.PartEffectArchetypes</c>.
    ///
    /// <para><b>To add a new archetype:</b></para>
    /// <list type="number">
    ///   <item>Add a <c>private static readonly ArchetypeFieldProfile s_myArchetype = new(...)</c> below.</item>
    ///   <item>Add a <c>case "my_archetype" =&gt; s_myArchetype,</c> line to <see cref="Get"/>.</item>
    ///   <item>Register a <c>PartEffectFactoryFn</c> for it in <c>PartEffectBootstrap</c>.</item>
    /// </list>
    /// No other UI code changes.
    /// </summary>
    internal static class ArchetypeFieldRegistry
    {
        public static ArchetypeFieldProfile Get(string archetype) => archetype switch
        {
            "thread_in"       => s_threadIn,
            "clamp_hold"      => s_clampHold,
            "axis_plunge"     => s_axisPlunge,
            "rotate_in_place" => s_rotateInPlace,
            "lerp"            => s_lerp,
            _                 => s_lerp,
        };

        /// <summary>Returns the canonical list of archetype keys the author may pick.</summary>
        public static readonly string[] KnownArchetypes = new[]
        {
            "lerp", "thread_in", "clamp_hold", "axis_plunge", "rotate_in_place",
        };

        // ── Profiles ─────────────────────────────────────────────────────────

        private static readonly ArchetypeFieldProfile s_lerp = new(
            showEasing:     true,
            showFollowPart: true,
            helpText:       "Generic A→B lerp. Uses the part's authored stepPoses as endpoints.");

        private static readonly ArchetypeFieldProfile s_threadIn = new(
            showAxis:            true,
            showTotalRotations:  true,
            showRotationPerUnit: true,
            showEasing:          true,
            showFollowPart:      true,
            helpText:            "Helical motion: translates along axis while rotating. Canonical for drilling/screwing a bolt in.");

        private static readonly ArchetypeFieldProfile s_axisPlunge = new(
            showAxis:       true,
            showDistance:   true,
            showEasing:     true,
            showFollowPart: true,
            helpText:       "Pure translation along an axis (no rotation). Use for press-fits and pin-home motions.");

        private static readonly ArchetypeFieldProfile s_rotateInPlace = new(
            showAxis:           true,
            showTotalRotations: true,
            showEasing:         true,
            showFollowPart:     true,
            helpText:           "Rotation about an axis with no translation. Wrench on captive nut, valve handle.");

        private static readonly ArchetypeFieldProfile s_clampHold = new(
            showFollowPart: false,
            helpText:       "No motion: the tool engages the part without moving it. Torch on seam, clamp, multimeter probe.");
    }
}
