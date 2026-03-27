namespace OSE.Interaction
{
    /// <summary>
    /// All profile-specific tuning values in one place.
    /// Consumers read from <see cref="ToolProfileRegistry"/> instead of
    /// maintaining their own if/else chains per profile string.
    /// </summary>
    public struct ToolProfileDescriptor
    {
        // ── Camera ──
        /// <summary>Camera distance during tool action framing (smaller = closer).</summary>
        public float FramingDistance;

        // ── Tool approach pose ──
        /// <summary>Gap between tool tip and surface during action (metres).</summary>
        public float WorkingDistance;
        /// <summary>Tool tilt in degrees from the approach axis (realism).</summary>
        public float ApproachTiltDegrees;

        // ── Preview animation ──
        /// <summary>Which IToolActionPreview implementation to instantiate.</summary>
        public PreviewStyle PreviewStyle;

        // ── View mode overrides (null = use family default) ──
        /// <summary>Override for Use-family steps.</summary>
        public ViewMode? UseViewModeOverride;
        /// <summary>Override for Place-family steps.</summary>
        public ViewMode? PlaceViewModeOverride;

        // ── Preview behaviour ──
        /// <summary>When true, the "I Do / We Do / You Do" preview is skipped entirely
        /// for this profile (e.g. tape measure uses its own anchor-to-anchor flow).</summary>
        public bool SkipPreview;
        /// <summary>When true, the preview always plays in Observe mode (auto-play).
        /// The Guided phase is skipped — the user watches but never provides input.
        /// Used for verification tools like the framing square.</summary>
        public bool ObserveOnly;
        /// <summary>Maximum speed multiplier for repeated preview animations (default 2.5).
        /// Higher values make late targets play faster; 1 = no acceleration.</summary>
        public float PreviewSpeedCap;

        // ── Completion effects ──
        /// <summary>Whether to spawn a click/particle effect on target completion.</summary>
        public bool SpawnClickEffect;
    }
}
