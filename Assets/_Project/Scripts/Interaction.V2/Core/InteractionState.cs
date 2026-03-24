namespace OSE.Interaction.V2
{
    /// <summary>
    /// Top-level interaction state managed by the InteractionOrchestrator.
    /// Only one state is active at a time. Camera movement is suppressed
    /// while in DraggingPart state.
    /// </summary>
    public enum InteractionState
    {
        Idle,
        PartHovered,
        PartSelected,
        DraggingPart,
        CameraOrbit,
        CameraPan,
        CameraZoom,
        InspectMode,
        UIInteraction,
        RepositioningAssembly,
        ToolFocus
    }
}
