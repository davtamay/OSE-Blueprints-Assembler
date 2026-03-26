using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Provides access to the tool cursor preview and active tool state.
    /// Implemented by PartInteractionBridge, consumed by the preview system.
    /// </summary>
    public interface IToolPreviewProvider
    {
        GameObject GetToolPreview();
        string GetActiveToolProfile();
        string GetActiveToolId();
        void SetToolPreviewPositionSuspended(bool suspended);
        int GetCompletedToolTargetCount();
        void IncrementCompletedToolTargetCount();
    }
}
