namespace OSE.Editor
{
    /// <summary>
    /// Maps task kind strings to their <see cref="TaskFieldProfile"/>.
    ///
    /// <para><b>To add a new task kind:</b></para>
    /// <list type="number">
    ///   <item>Add a <c>private static readonly TaskFieldProfile s_myKind = new(...)</c> below.</item>
    ///   <item>Add a <c>case "my_kind" =&gt; s_myKind,</c> line to <see cref="Get"/>.</item>
    /// </list>
    /// No other files need to change.
    /// </summary>
    internal static class TaskFieldRegistry
    {
        /// <summary>
        /// Returns the field profile for the given task kind string.
        /// Unknown kinds fall back to <see cref="s_default"/>.
        /// </summary>
        public static TaskFieldProfile Get(string taskKind) => taskKind switch
        {
            "confirm_action" => s_confirmAction,
            "confirm"        => s_observe,
            "part"           => s_part,
            "wire"           => s_wire,
            "target"         => s_target,
            "toolAction"     => s_toolAction,
            _                => s_default,
        };

        // ── Profiles ─────────────────────────────────────────────────────────

        /// <summary>
        /// Terminal button-press — user just presses Confirm.
        /// No placement data, no gizmos.
        /// </summary>
        private static readonly TaskFieldProfile s_confirmAction = new();

        /// <summary>
        /// Inspection point — camera must frame this world position before
        /// Confirm unlocks. Position only; no tool, no rotation needed.
        /// </summary>
        private static readonly TaskFieldProfile s_observe = new(
            showPosition:        true,
            scenePositionHandle: true);

        /// <summary>
        /// Physical part — defines start and play positions for a mesh in the scene.
        /// </summary>
        private static readonly TaskFieldProfile s_part = new(
            showModelAsset:      true,
            showPosition:        true,
            showRotation:        true,
            showScale:           true,
            scenePositionHandle: true,
            sceneRotationHandle: true);

        /// <summary>
        /// Wire / cable connection — two port endpoints define the cable run.
        /// Position/rotation anchor the connector body; ports define the cable ends.
        /// </summary>
        private static readonly TaskFieldProfile s_wire = new(
            showPosition:        true,
            showRotation:        true,
            showPortFields:      true,
            scenePositionHandle: true,
            scenePortPoints:     true,
            scenePartConnector:  true);

        /// <summary>
        /// Snap / placement target (no tool required).
        /// Full transform + weld axis for directional snap.
        /// </summary>
        private static readonly TaskFieldProfile s_target = new(
            showPosition:        true,
            showRotation:        true,
            showScale:           true,
            showWeldAxis:        true,
            showWeldLength:      true,
            showClickToSnap:     true,
            scenePositionHandle: true,
            sceneRotationHandle: true,
            sceneWeldArrow:      true,
            scenePartConnector:  true);

        /// <summary>
        /// Tool action — the tool drifts to this target and performs its action.
        /// Weld axis = drift direction; weld length = distance to travel.
        /// </summary>
        private static readonly TaskFieldProfile s_toolAction = new(
            showPosition:        true,
            showRotation:        true,
            showWeldAxis:        true,
            showWeldLength:      true,
            showToolOptions:     true,
            showClickToSnap:     true,
            scenePositionHandle: true,
            sceneRotationHandle: true,
            sceneWeldArrow:      true,
            scenePortPoints:     true,
            scenePartConnector:  true);

        /// <summary>Fallback — same as tool action.</summary>
        private static readonly TaskFieldProfile s_default = s_toolAction;
    }
}
