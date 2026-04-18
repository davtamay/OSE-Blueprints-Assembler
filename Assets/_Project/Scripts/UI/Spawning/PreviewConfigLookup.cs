using System;
using System.Collections.Generic;
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
        private PackagePreviewConfig           _config;
        private StepDefinition[]               _steps;
        private ToolActionDefinition[]         _flatActions;
        private TargetDefinition[]             _targets;

        internal void SetConfig(PackagePreviewConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// G.2.5.b Path A: give the lookup access to the package's steps + targets
        /// so <see cref="FindPartStepPose"/> can resolve
        /// <see cref="TaskOrderEntry.endTransform"/> on-the-fly without injecting
        /// synthesized entries into package data. No mutation of anything — the
        /// synth only exists as a return value from the lookup call.
        /// </summary>
        internal void SetPackage(MachinePackageDefinition pkg)
        {
            _steps   = pkg?.steps;
            _targets = pkg?.GetTargets();

            // Flatten all requiredToolActions for fast id→action resolution.
            if (_steps == null)
            {
                _flatActions = null;
                return;
            }
            var list = new List<ToolActionDefinition>(_steps.Length * 2);
            foreach (var s in _steps)
            {
                if (s?.requiredToolActions == null) continue;
                foreach (var a in s.requiredToolActions)
                    if (a != null && !string.IsNullOrEmpty(a.id)) list.Add(a);
            }
            _flatActions = list.ToArray();
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
        ///
        /// <para>G.2.5.b Path A: first checks <see cref="TaskOrderEntry.endTransform"/> on
        /// any task in the step's taskOrder that touches the part. When found, wraps it
        /// in a fresh <see cref="StepPoseEntry"/> (never stored anywhere) and returns it.
        /// This closes the pose-chain loop for Part + Tool tasks without mutating the
        /// package data or interacting with the invariant validator at all.</para>
        /// </summary>
        internal StepPoseEntry FindPartStepPose(string partId, string stepId)
        {
            if (string.IsNullOrEmpty(partId) || string.IsNullOrEmpty(stepId)) return null;

            // G.2.5.b Path A: consult task endTransforms first (task-owned, Phase G.2).
            StepPoseEntry inline = TryBuildInlineTaskPose(partId, stepId);
            if (inline != null) return inline;

            // Fall back to authored stepPoses + normalizer synths on the placement.
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
        /// Walks the step's taskOrder in order and returns the LAST task touching
        /// <paramref name="partId"/> whose <see cref="TaskOrderEntry.endTransform"/>
        /// is authored, wrapped in a fresh <see cref="StepPoseEntry"/>. Returns
        /// null when there's no package context, no matching step, no matching
        /// task, or no endTransform on the matching task. Pure read — does not
        /// mutate or cache anything.
        /// </summary>
        private StepPoseEntry TryBuildInlineTaskPose(string partId, string stepId)
        {
            if (_steps == null) return null;

            // Locate the step.
            StepDefinition step = null;
            for (int i = 0; i < _steps.Length; i++)
            {
                if (_steps[i] != null && string.Equals(_steps[i].id, stepId, StringComparison.Ordinal))
                { step = _steps[i]; break; }
            }
            if (step?.taskOrder == null) return null;

            // Walk taskOrder for tasks that touch partId — last wins (matches
            // runtime "final pose after step completes").
            TaskEndTransform et = null;
            foreach (var entry in step.taskOrder)
            {
                if (entry?.endTransform == null) continue;
                string p = ResolvePartIdForEntry(entry);
                if (!string.IsNullOrEmpty(p) && string.Equals(p, partId, StringComparison.Ordinal))
                    et = entry.endTransform;
            }
            if (et == null) return null;

            // Build an ephemeral StepPoseEntry. Not stored anywhere, not persisted,
            // not visible to the invariant validator. Just a shape the existing
            // consumers know how to read.
            return new StepPoseEntry
            {
                stepId   = stepId,
                label    = null, // unlabelled — callers that branch on label treat as regular authored
                position = et.position,
                rotation = et.rotation,
                scale    = et.scale,
            };
        }

        /// <summary>
        /// Resolves which part a <see cref="TaskOrderEntry"/> touches.
        /// Part task: strip any #N instance suffix. Tool task: resolve
        /// through the action's targetId → target's associatedPartId.
        /// </summary>
        private string ResolvePartIdForEntry(TaskOrderEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.kind)) return null;
            if (entry.kind == "part") return TaskInstanceId.ToPartId(entry.id);
            if (entry.kind == "toolAction" && _flatActions != null && !string.IsNullOrEmpty(entry.id))
            {
                for (int i = 0; i < _flatActions.Length; i++)
                {
                    var a = _flatActions[i];
                    if (a == null || a.id != entry.id) continue;
                    if (string.IsNullOrEmpty(a.targetId) || _targets == null) return null;
                    for (int j = 0; j < _targets.Length; j++)
                    {
                        if (_targets[j]?.id == a.targetId) return _targets[j].associatedPartId;
                    }
                    return null;
                }
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

            if (pp.stepPoses != null && pp.stepPoses.Length > 0
                && TryPickStepPose(pp.stepPoses, orderedSteps, viewingStepIndex, out StepPoseEntry picked))
            {
                position = picked.position;
                rotation = picked.rotation;
                scale    = picked.scale;
                return true;
            }

            // Fallback: assembledPosition
            position = pp.assembledPosition;
            rotation = pp.assembledRotation;
            scale = pp.assembledScale;
            return true;
        }

        /// <summary>
        /// Picks the stepPose whose authored span (<c>propagateFromStep</c>…
        /// <c>propagateThroughStep</c>) covers <paramref name="viewingStepIndex"/>.
        /// When a span bound is empty it's treated as open-ended on that side.
        /// Among multiple covering entries, prefers the one whose anchor
        /// (<c>stepId</c>) is closest to the viewing step with preference for
        /// anchors at or before it — preserves the "backward-looking" feel of
        /// the previous walk. Legacy entries (no span fields) behave identically
        /// to before: they cover <c>stepId</c> forward until another entry wins.
        /// </summary>
        private bool TryPickStepPose(
            StepPoseEntry[] poses,
            StepDefinition[] orderedSteps,
            int viewingStepIndex,
            out StepPoseEntry picked)
        {
            picked = null;
            int bestDist = int.MaxValue;
            int bestAnchor = int.MinValue;

            for (int p = 0; p < poses.Length; p++)
            {
                var pose = poses[p];
                if (pose == null) continue;

                int anchorIdx    = IndexOfStep(orderedSteps, pose.stepId);
                int fromRaw      = string.IsNullOrEmpty(pose.propagateFromStep)
                                   ? int.MinValue
                                   : IndexOfStep(orderedSteps, pose.propagateFromStep);
                int fromIdx      = fromRaw == -1 ? int.MinValue : fromRaw;  // unresolved id → open-ended
                int throughRaw   = string.IsNullOrEmpty(pose.propagateThroughStep)
                                   ? int.MaxValue
                                   : IndexOfStep(orderedSteps, pose.propagateThroughStep);
                int throughIdx   = throughRaw == -1 ? int.MaxValue : throughRaw;  // unresolved id → open-ended

                // Anchor-only (legacy) entries need a lower bound so they don't
                // accidentally apply to steps before the anchor.
                int effectiveFrom = fromIdx != int.MinValue
                    ? fromIdx
                    : (anchorIdx >= 0 ? anchorIdx : int.MinValue);

                if (viewingStepIndex < effectiveFrom) continue;
                if (viewingStepIndex > throughIdx)    continue;

                int dist = anchorIdx >= 0
                    ? Math.Abs(viewingStepIndex - anchorIdx)
                    : int.MaxValue / 2;
                bool betterDist  = dist < bestDist;
                bool sameDistButBehind = dist == bestDist && anchorIdx <= viewingStepIndex && bestAnchor > viewingStepIndex;
                if (betterDist || sameDistButBehind)
                {
                    bestDist   = dist;
                    bestAnchor = anchorIdx;
                    picked     = pose;
                }
            }
            return picked != null;
        }

        // Returns the step's sequenceIndex (not the orderedSteps array
        // position). Callers pass viewingStepIndex = sequenceIndex, so every
        // bound must be resolved into the same space — earlier this method
        // returned the array index, which silently mismatched when
        // sequenceIndex != arrayIndex+1 (gaps, 1-based numbering, etc.) and
        // caused stepPoses to stop applying one step early.
        private static int IndexOfStep(StepDefinition[] orderedSteps, string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || orderedSteps == null) return -1;
            for (int i = 0; i < orderedSteps.Length; i++)
                if (orderedSteps[i] != null && string.Equals(orderedSteps[i].id, stepId, StringComparison.OrdinalIgnoreCase))
                    return orderedSteps[i].sequenceIndex;
            return -1;
        }

        /// <summary>
        /// Same range-covering resolution as <see cref="TryResolvePartPoseAtStep"/>
        /// but for a subassembly's authored <c>stepPoses</c>. Returns false when
        /// no group-level span covers the viewing step.
        /// </summary>
        internal bool TryResolveGroupPoseAtStep(
            string subassemblyId,
            StepDefinition[] orderedSteps,
            int viewingStepIndex,
            out SceneFloat3 position,
            out SceneQuaternion rotation,
            out SceneFloat3 scale)
        {
            position = default; rotation = default; scale = default;
            SubassemblyPreviewPlacement sp = FindSubassemblyPlacement(subassemblyId);
            if (sp?.stepPoses == null || sp.stepPoses.Length == 0) return false;
            if (!TryPickStepPose(sp.stepPoses, orderedSteps, viewingStepIndex, out StepPoseEntry picked)) return false;
            position = picked.position;
            rotation = picked.rotation;
            scale    = picked.scale;
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
