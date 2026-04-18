namespace OSE.Editor
{
    /// <summary>
    /// Declares which fields and scene gizmos are relevant for a given task kind.
    /// Consumed by <see cref="ToolTargetAuthoringWindow"/> to drive both the detail
    /// panel UI and the SceneView handle set without any per-kind branching at the
    /// call site.
    ///
    /// To add a new task kind: add one entry to <see cref="TaskFieldRegistry"/>.
    /// No other files need to change.
    /// </summary>
    internal readonly struct TaskFieldProfile
    {
        // ── Detail panel ──────────────────────────────────────────────────────

        /// <summary>Show the part or tool mesh asset reference picker.</summary>
        public readonly bool ShowModelAsset;

        /// <summary>Show the Position (local) Vector3 field.</summary>
        public readonly bool ShowPosition;

        /// <summary>Show the Rotation (euler) Vector3 field.</summary>
        public readonly bool ShowRotation;

        /// <summary>Show the Scale Vector3 field.</summary>
        public readonly bool ShowScale;

        /// <summary>Show the weld axis direction field and scene-gizmo toggle.</summary>
        public readonly bool ShowWeldAxis;

        /// <summary>Show the weld length (distance to travel) float field.</summary>
        public readonly bool ShowWeldLength;

        /// <summary>Show Port A / Port B fields (wire/connect steps).</summary>
        public readonly bool ShowPortFields;

        /// <summary>Show the persistent-tool toggle and tool-preview toggle.</summary>
        public readonly bool ShowToolOptions;

        /// <summary>Show the click-to-snap surface button.</summary>
        public readonly bool ShowClickToSnap;

        /// <summary>Show the tool-action <c>actionType</c> picker (drill, wrench, press, weld, …).</summary>
        public readonly bool ShowActionType;

        /// <summary>Show the Tool × Part Interaction authoring panel (archetype, axis, motion).</summary>
        public readonly bool ShowInteractionPanel;

        // ── Scene gizmos ──────────────────────────────────────────────────────

        /// <summary>Draw the position move handle in SceneView.</summary>
        public readonly bool ScenePositionHandle;

        /// <summary>Draw the rotation handle in SceneView.</summary>
        public readonly bool SceneRotationHandle;

        /// <summary>Draw the weld-axis arrow overlay.</summary>
        public readonly bool SceneWeldArrow;

        /// <summary>Draw port A/B sphere handles.</summary>
        public readonly bool ScenePortPoints;

        /// <summary>Draw the part-connector line between target and its associated part.</summary>
        public readonly bool ScenePartConnector;

        public TaskFieldProfile(
            bool showModelAsset      = false,
            bool showPosition        = false,
            bool showRotation        = false,
            bool showScale           = false,
            bool showWeldAxis        = false,
            bool showWeldLength      = false,
            bool showPortFields      = false,
            bool showToolOptions     = false,
            bool showClickToSnap     = false,
            bool showActionType      = false,
            bool showInteractionPanel = false,
            bool scenePositionHandle = false,
            bool sceneRotationHandle = false,
            bool sceneWeldArrow      = false,
            bool scenePortPoints     = false,
            bool scenePartConnector  = false)
        {
            ShowModelAsset      = showModelAsset;
            ShowPosition        = showPosition;
            ShowRotation        = showRotation;
            ShowScale           = showScale;
            ShowWeldAxis        = showWeldAxis;
            ShowWeldLength      = showWeldLength;
            ShowPortFields      = showPortFields;
            ShowToolOptions     = showToolOptions;
            ShowClickToSnap     = showClickToSnap;
            ShowActionType       = showActionType;
            ShowInteractionPanel = showInteractionPanel;
            ScenePositionHandle = scenePositionHandle;
            SceneRotationHandle = sceneRotationHandle;
            SceneWeldArrow      = sceneWeldArrow;
            ScenePortPoints     = scenePortPoints;
            ScenePartConnector  = scenePartConnector;
        }
    }
}
