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
    }
}
