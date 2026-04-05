using System.Collections.Generic;
using OSE.Content;
using UnityEngine;

namespace OSE.Runtime
{
    public interface ISubassemblyPlacementService
    {
        string ActiveSubassemblyId { get; }

        void ResetReplayState();
        void RefreshForStep(string stepId);
        void HandleStepCompleted(string stepId);
        void RestoreCompletedPlacements(StepDefinition[] completedSteps);

        bool IsSubassemblyReady(string subassemblyId);
        bool IsProxy(GameObject target);
        bool TryGetProxy(string subassemblyId, out GameObject proxyRoot);
        bool TryGetSubassemblyId(GameObject target, out string subassemblyId);
        GameObject ResolveSelectableFromHit(Transform hitTransform);
        bool TryGetDisplayInfo(GameObject target, out string displayName, out string description);
        IEnumerable<GameObject> EnumerateMemberParts(GameObject target);
        bool TryResolveTargetPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale);
        bool TryApplyPlacementPreview(GameObject target, string targetId, float nearestDist, float previewRadius);
        bool IsPlacementCommitReady(GameObject target, string targetId);
        bool TryApplyPlacement(string subassemblyId, string targetId);
        void ApplyProxyTransform(GameObject target);
        bool IsActiveStepPlacementSatisfied(string stepId);

        /// <summary>
        /// Hides member bars of every proxy that has been built but not yet placed
        /// (no committed target) and is not the currently active proxy.
        /// Call after <see cref="RefreshForStep"/> so completed-fabrication panels
        /// don't remain visible during other panels' stacking steps.
        /// </summary>
        void HideNonActivePendingProxyBars();

        /// <summary>
        /// Final-pass guarantee: for every completed stacking step in
        /// <paramref name="completedSteps"/>, moves member bars to their integrated
        /// cube positions using per-member lookup (no targetId required).
        /// Call after all other visual restores to override any stale flat-layout
        /// positioning left by <c>RestoreCompletedStepParts</c> or a failed
        /// <c>RestoreCompletedPlacements</c> pass.
        /// </summary>
        void EnforceIntegratedPositions(StepDefinition[] completedSteps);

        /// <summary>
        /// Called every frame from <see cref="PartInteractionBridge"/>. Retries
        /// integrated-position enforcement for any stacking subassembly whose member
        /// parts haven't all loaded yet. Once all members for a subassembly are
        /// confirmed found and positioned, it stops being retried. The pending set is
        /// seeded by <see cref="EnforceIntegratedPositions"/> and cleared by
        /// <see cref="ResetReplayState"/>.
        /// </summary>
        void TickPendingIntegration();
    }
}
