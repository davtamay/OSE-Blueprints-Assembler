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

        // ── Part motion during action ──
        /// <summary>
        /// When true, the active step's associated part is expected to move
        /// <b>during</b> the tool action (not just end up at a new pose after
        /// the step). The controller builds a <c>LerpPosePartEffect</c> so the
        /// tool follows the part's motion each frame — e.g. a drill driving a
        /// bolt down: tool and bolt descend together.
        ///
        /// <para>Default <c>false</c> (fail-closed): profiles like Weld,
        /// SquareCheck, Measure, Cut etc. keep the part stationary and the
        /// tool rides a seam/axis/probe on it. Building a PartEffect in those
        /// cases dragged the tool along with any step-to-step pose delta of
        /// the part — visible as "tool flies off-screen when action starts,
        /// returns when action ends" (seen on step 27 first-visit 2026-04-18).</para>
        ///
        /// <para>New profiles inherit the safe default. Only flip to true when
        /// the authored animation semantics require tool-follows-part.</para>
        /// </summary>
        public bool PartFollowsTool;
    }
}
