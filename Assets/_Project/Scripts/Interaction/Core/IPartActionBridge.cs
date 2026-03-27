using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Typed contract for placement, tool actions, spatial queries, and hover.
    /// Implemented by PartInteractionBridge (OSE.UI), consumed by
    /// InteractionOrchestrator (OSE.Interaction).
    ///
    /// Part selection/grab/release are handled by CanonicalActionBridge,
    /// which feeds events into InputActionRouter and SelectionService.
    /// </summary>
    public interface IPartActionBridge
    {
        bool ExternalControlEnabled { get; set; }

        // ── Target Normalization ──

        GameObject NormalizeSelectableTarget(GameObject target);

        /// <summary>
        /// Returns true if <paramref name="target"/> is a selectable object — either a
        /// spawned part or a subassembly proxy. Used by <see cref="InteractionOrchestrator"/>
        /// to validate raycast hits that are not in the spawned-parts list (e.g. proxies).
        /// </summary>
        bool IsSelectableTarget(GameObject target);

        // ── Movement Lock ──

        /// <summary>
        /// Returns true when a part (or subassembly proxy) should be immovable
        /// because it has already been placed or completed.
        /// </summary>
        bool IsPartMovementLocked(GameObject target);

        // ── Placement ──

        bool TryClickToPlace(GameObject selectedPart, Vector2 screenPos);

        // ── Tool Actions ──

        bool TryToolAction(Vector2 screenPos);
        bool TryToolAction(string targetId);
        bool TryResolveToolActionTarget(Vector2 screenPos, out ToolActionContext context);
        bool TryPipeConnection(Vector2 screenPos);

        // ── Spatial Queries ──

        Vector3 LastToolActionWorldPos { get; }
        bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos);
        Vector3[] GetActiveToolTargetPositions();
        bool TryGetPreviewWorldPosForPart(string partId, out Vector3 worldPos);
        bool TryGetStepFocusBounds(string stepId, out Bounds bounds);

        // ── Hover ──

        void SetHoveredPart(GameObject part);
    }
}
