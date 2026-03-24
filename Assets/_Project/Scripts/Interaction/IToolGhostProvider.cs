using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Provides access to the tool cursor ghost and active tool state.
    /// Implemented by PartInteractionBridge, consumed by the preview system.
    /// </summary>
    public interface IToolGhostProvider
    {
        GameObject GetToolGhost();
        string GetActiveToolProfile();
        string GetActiveToolId();
        void SetToolGhostPositionSuspended(bool suspended);
        int GetCompletedToolTargetCount();
        void IncrementCompletedToolTargetCount();
    }
}
