using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Manages persistent tool instances (clamps, fixtures) that survive
    /// step transitions. Implemented by PartInteractionBridge, consumed by
    /// PersistentToolController and InteractionOrchestrator.
    /// </summary>
    public interface IPersistentToolManager
    {
        GameObject ConvertPreviewToPersistent(string toolId, string targetId, Vector3 worldPos, Quaternion rotation);
        GameObject SpawnPersistentTool(string toolId, string targetId, Vector3 worldPos, Quaternion rotation);
        bool RemovePersistentTool(string targetId);
        int RemoveAllPersistentTools(string toolId);
        bool HasPersistentToolAt(string targetId);
        /// <summary>Returns the distinct tool IDs of all currently placed persistent tools.</summary>
        string[] GetPlacedPersistentToolIds();
    }
}
