using System;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Pure-lookup helper that resolves placement data from a
    /// <see cref="PackagePreviewConfig"/>. Extracted from
    /// <see cref="PackagePartSpawner"/> for single-responsibility.
    /// </summary>
    internal sealed class PreviewConfigLookup
    {
        private PackagePreviewConfig _config;

        internal void SetConfig(PackagePreviewConfig config)
        {
            _config = config;
        }

        internal PartPreviewPlacement FindPartPlacement(string partId)
        {
            if (_config?.partPlacements == null) return null;
            foreach (var p in _config.partPlacements)
                if (p.partId == partId) return p;
            return null;
        }

        internal TargetPreviewPlacement FindTargetPlacement(string targetId)
        {
            if (_config?.targetPlacements == null) return null;
            foreach (var t in _config.targetPlacements)
                if (t.targetId == targetId) return t;
            return null;
        }

        internal SubassemblyPreviewPlacement FindSubassemblyPlacement(string subassemblyId)
        {
            if (_config?.subassemblyPlacements == null) return null;
            foreach (var placement in _config.subassemblyPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }
            return null;
        }

        internal ConstrainedSubassemblyFitPreviewPlacement FindConstrainedSubassemblyFitPlacement(string subassemblyId, string targetId)
        {
            if (_config?.constrainedSubassemblyFitPlacements == null)
                return null;

            foreach (var placement in _config.constrainedSubassemblyFitPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(placement.targetId, targetId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }

            return null;
        }

        internal SubassemblyPreviewPlacement FindCompletedSubassemblyParkingPlacement(string subassemblyId)
        {
            if (_config?.completedSubassemblyParkingPlacements == null) return null;
            foreach (var placement in _config.completedSubassemblyParkingPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }
            return null;
        }

        internal IntegratedSubassemblyPreviewPlacement FindIntegratedSubassemblyPlacement(string subassemblyId, string targetId)
        {
            if (_config?.integratedSubassemblyPlacements == null)
                return null;

            foreach (var placement in _config.integratedSubassemblyPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(placement.targetId, targetId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the <see cref="StepPoseEntry"/> for the given part at the given step,
        /// or null if no intermediate pose is authored (caller falls back to assembledPosition).
        /// </summary>
        internal StepPoseEntry FindPartStepPose(string partId, string stepId)
        {
            if (string.IsNullOrEmpty(partId) || string.IsNullOrEmpty(stepId)) return null;
            PartPreviewPlacement pp = FindPartPlacement(partId);
            if (pp?.stepPoses == null) return null;
            for (int i = 0; i < pp.stepPoses.Length; i++)
            {
                if (string.Equals(pp.stepPoses[i].stepId, stepId, StringComparison.OrdinalIgnoreCase))
                    return pp.stepPoses[i];
            }
            return null;
        }

        /// <summary>
        /// Resolves the pose a part should occupy when viewing the assembly at
        /// <paramref name="viewingStepIndex"/>. Walks backward through completed steps
        /// to find the most recent <see cref="StepPoseEntry"/>. Falls back to assembledPosition.
        /// </summary>
        internal bool TryResolvePartPoseAtStep(
            string partId,
            StepDefinition[] orderedSteps,
            int viewingStepIndex,
            out SceneFloat3 position,
            out SceneQuaternion rotation,
            out SceneFloat3 scale)
        {
            PartPreviewPlacement pp = FindPartPlacement(partId);
            if (pp == null)
            {
                position = default; rotation = default; scale = default;
                return false;
            }

            if (pp.stepPoses != null && pp.stepPoses.Length > 0)
            {
                // Walk backward from the last completed step to find the most recent stepPose
                for (int s = viewingStepIndex - 1; s >= 0; s--)
                {
                    if (s >= orderedSteps.Length) continue;
                    string sid = orderedSteps[s]?.id;
                    if (string.IsNullOrEmpty(sid)) continue;

                    for (int p = 0; p < pp.stepPoses.Length; p++)
                    {
                        if (string.Equals(pp.stepPoses[p].stepId, sid, StringComparison.OrdinalIgnoreCase))
                        {
                            position = pp.stepPoses[p].position;
                            rotation = pp.stepPoses[p].rotation;
                            scale = pp.stepPoses[p].scale;
                            return true;
                        }
                    }
                }
            }

            // Fallback: assembledPosition
            position = pp.assembledPosition;
            rotation = pp.assembledRotation;
            scale = pp.assembledScale;
            return true;
        }

        internal IntegratedMemberPreviewPlacement FindIntegratedMemberPlacement(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return null;

            IntegratedSubassemblyPreviewPlacement[] intPlacements = _config?.integratedSubassemblyPlacements;
            if (intPlacements == null)
                return null;

            for (int ip = 0; ip < intPlacements.Length; ip++)
            {
                IntegratedSubassemblyPreviewPlacement intPlacement = intPlacements[ip];
                if (intPlacement?.memberPlacements == null) continue;

                for (int mp = 0; mp < intPlacement.memberPlacements.Length; mp++)
                {
                    IntegratedMemberPreviewPlacement member = intPlacement.memberPlacements[mp];
                    if (member != null && string.Equals(member.partId, partId, System.StringComparison.OrdinalIgnoreCase))
                        return member;
                }
            }

            return null;
        }
    }
}
