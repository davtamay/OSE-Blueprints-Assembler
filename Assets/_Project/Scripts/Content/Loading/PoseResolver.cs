using System;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// The single, canonical "what pose should this part show at this seq?"
    /// function. Used by <see cref="MachinePackageNormalizer"/> during
    /// <c>BakePoseTable</c> to populate every cell of the
    /// <see cref="PoseTable"/>. Editor and runtime read the baked table —
    /// neither ever calls <see cref="Resolve"/> directly at render time.
    ///
    /// The decision tree is ordered by priority (first match wins). Adding a
    /// new source means adding one branch here, one <see cref="PoseSource"/>
    /// enum member, and — for all callers — nothing else. That's the whole
    /// point of the rewrite.
    /// </summary>
    public static class PoseResolver
    {
        /// <summary>
        /// Resolve the pose for <paramref name="partId"/> at viewing sequence
        /// <paramref name="viewSeq"/>. Returns <see cref="PoseResolution.Hidden"/>
        /// when the part shouldn't be rendered at this seq.
        ///
        /// <paramref name="mode"/> only matters at the part's own task steps:
        /// <list type="bullet">
        ///   <item><see cref="PoseMode.Committed"/> — runtime view: part is at
        ///   its assembled pose once the step completes.</item>
        ///   <item><see cref="PoseMode.StartPreview"/> — editor "Start Pose" toggle:
        ///   show the part at its pre-placement position.</item>
        ///   <item><see cref="PoseMode.AssembledPreview"/> — editor "Assembled Pose"
        ///   toggle: show the part at its post-placement position.</item>
        /// </list>
        /// For every non-task seq the modes are identical.
        /// </summary>
        public static PoseResolution Resolve(
            string partId, int viewSeq, MachinePackageDefinition pkg,
            PoseResolverIndex idx, PoseMode mode)
        {
            var basePose = ResolveWithoutGroupCompose(partId, viewSeq, pkg, idx, mode);
            if (basePose.IsHidden) return basePose;
            return ApplyGroupStepPose(partId, viewSeq, idx, basePose);
        }

        /// <summary>
        /// Composes the member's per-part resolved pose with any group
        /// stepPose active for the owning subassembly at <paramref name="viewSeq"/>.
        /// No-op when the part has no owning subassembly OR the subassembly has
        /// no group stepPose covering this seq — keeps existing content byte-
        /// identical with the pre-Phase-A resolver.
        ///
        /// <para>Composition formula: world = groupTransform × memberLocal.
        /// <list type="bullet">
        ///   <item><c>composedPos = groupPos + groupRot * memberLocalPos</c></item>
        ///   <item><c>composedRot = groupRot * memberLocalRot</c></item>
        ///   <item>Scale is inherited from the member (group scale not yet used).</item>
        /// </list>
        /// Matches the Unity transform-hierarchy semantics: the group root
        /// carries <c>groupPos</c>/<c>groupRot</c>, members carry their
        /// already-resolved poses as their localTransform, and world =
        /// root×local.</para>
        /// </summary>
        private static PoseResolution ApplyGroupStepPose(
            string partId, int viewSeq, PoseResolverIndex idx, PoseResolution memberLocal)
        {
            if (!idx.subassemblyIdByMember.TryGetValue(partId, out string subId)) return memberLocal;
            if (!idx.groupStepPosesBySubassembly.TryGetValue(subId, out var spans)) return memberLocal;

            ResolvedAuthorSpan? best = null;
            int bestDist = int.MaxValue;
            int bestAnchor = int.MinValue;
            for (int i = 0; i < spans.Count; i++)
            {
                var sp = spans[i];
                if (!sp.Covers(viewSeq)) continue;
                int dist = sp.anchorSeq >= 0 ? Math.Abs(viewSeq - sp.anchorSeq) : int.MaxValue / 2;
                bool preferBackward = dist == bestDist
                                     && sp.anchorSeq <= viewSeq
                                     && bestAnchor   >  viewSeq;
                if (dist < bestDist || preferBackward)
                {
                    best       = sp;
                    bestDist   = dist;
                    bestAnchor = sp.anchorSeq;
                }
            }
            if (!best.HasValue) return memberLocal;

            var g = best.Value.entry;
            Quaternion groupRot = ToQuat(g.rotation);
            Vector3    groupPos = ToVec3(g.position);

            Vector3    composedPos = groupPos + groupRot * memberLocal.pos;
            Quaternion composedRot = groupRot * memberLocal.rot;

            return new PoseResolution(
                composedPos,
                composedRot,
                memberLocal.scl,
                memberLocal.source,
                memberLocal.meta);
        }

        private static PoseResolution ResolveWithoutGroupCompose(
            string partId, int viewSeq, MachinePackageDefinition pkg,
            PoseResolverIndex idx, PoseMode mode)
        {
            if (string.IsNullOrEmpty(partId) || pkg == null || idx == null) return PoseResolution.Hidden;
            if (!idx.placementByPart.TryGetValue(partId, out var pp) || pp == null) return PoseResolution.Hidden;

            // [1] Hidden: before the part ever appears.
            int firstVisible = idx.firstVisibleSeqByPart.TryGetValue(partId, out int fv) ? fv : int.MaxValue;
            if (viewSeq < firstVisible) return PoseResolution.Hidden;

            // [2] Explicit author stepPose span covering this seq wins over
            //     every computed source. If two spans cover the same seq we
            //     pick the one whose anchor is closest (backward-preferred)
            //     to preserve the pre-rewrite tie-break behaviour — this is
            //     also flagged by invariant #5 at load.
            if (idx.authorSpansByPart.TryGetValue(partId, out var spans))
            {
                ResolvedAuthorSpan? best = null;
                int bestDist = int.MaxValue;
                int bestAnchor = int.MinValue;
                for (int i = 0; i < spans.Count; i++)
                {
                    var sp = spans[i];
                    if (!sp.Covers(viewSeq)) continue;
                    int dist = sp.anchorSeq >= 0 ? Math.Abs(viewSeq - sp.anchorSeq) : int.MaxValue / 2;
                    bool preferBackward = dist == bestDist
                                         && sp.anchorSeq <= viewSeq
                                         && bestAnchor   >  viewSeq;
                    if (dist < bestDist || preferBackward)
                    {
                        best       = sp;
                        bestDist   = dist;
                        bestAnchor = sp.anchorSeq;
                    }
                }
                if (best.HasValue)
                {
                    var e = best.Value.entry;
                    return new PoseResolution(
                        ToVec3(e.position),
                        ToQuat(e.rotation),
                        NormalizeScale(ToVec3(e.scale), pp),
                        PoseSource.ExplicitSpan,
                        string.IsNullOrEmpty(e.stepId) ? e.label ?? string.Empty : e.stepId);
                }
            }

            // [3] Integrated subassembly member — part rides with a group
            //     that has been stacked onto a target at or before viewSeq.
            //     We want the *most recent* stacking event ≤ viewSeq for the
            //     part's subassembly (if any), then compose the authored
            //     integrated member pose directly.
            if (idx.subassemblyIdByMember.TryGetValue(partId, out string subId))
            {
                StackingEvent? active = null;
                for (int i = idx.stackingEvents.Count - 1; i >= 0; i--)
                {
                    var ev = idx.stackingEvents[i];
                    if (ev.seq > viewSeq) continue;
                    if (!string.Equals(ev.subassemblyId, subId, StringComparison.Ordinal)) continue;
                    active = ev;
                    break;
                }
                if (active.HasValue)
                {
                    string memberKey = PoseResolverIndex.PackKey(active.Value.subassemblyId, active.Value.targetId, partId);
                    if (idx.integratedMemberBySubTargetPart.TryGetValue(memberKey, out var member))
                    {
                        return new PoseResolution(
                            ToVec3(member.position),
                            ToQuat(member.rotation),
                            NormalizeScale(ToVec3(member.scale), pp),
                            PoseSource.IntegratedMember,
                            active.Value.subassemblyId + "|" + active.Value.targetId);
                    }
                    // Falls through to later branches if memberPlacement absent —
                    // invariant #6 at load catches authoring gaps.
                }
            }

            // [4] Task-step acts on this part at viewSeq — the Start/Assembled
            //     toggle is authoritative here, and it's the ONLY branch where
            //     PoseMode matters.
            //
            //     Runtime convention (PoseMode.Committed): at the part's FIRST
            //     task step, the trainee is placing the part — show Start
            //     (staging) pose so the floating ghost is visible and the
            //     user has something to drag. At any SUBSEQUENT task step
            //     (part already placed earlier; this step just re-touches
            //     it — confirm, tool action, etc.), show Assembled so the
            //     part sits at its committed position.
            //
            //     Editor previews (StartPreview / AssembledPreview) still
            //     honour the author's explicit toggle regardless of task
            //     ordering — that's the authoring intent.
            if (idx.taskSeqsByPart.TryGetValue(partId, out var taskSeqs) && taskSeqs.Contains(viewSeq))
            {
                bool wantAssembled;
                if (mode == PoseMode.Committed)
                {
                    int firstTaskSeq = idx.firstTaskSeqByPart.TryGetValue(partId, out int ft2) ? ft2 : int.MaxValue;
                    wantAssembled = viewSeq > firstTaskSeq;
                }
                else
                {
                    wantAssembled = mode == PoseMode.AssembledPreview;
                }

                if (wantAssembled)
                    return new PoseResolution(
                        ToVec3(pp.assembledPosition),
                        ToQuat(pp.assembledRotation),
                        NormalizeScale(ToVec3(pp.assembledScale), pp),
                        PoseSource.Assembled,
                        string.Empty);
                return new PoseResolution(
                    ToVec3(pp.startPosition),
                    ToQuat(pp.startRotation),
                    NormalizeScale(ToVec3(pp.startScale), pp),
                    PoseSource.Start,
                    string.Empty);
            }

            // [5] Past-placed: viewSeq is after the part's last task step →
            //     assembledPosition is the committed resting pose.
            int lastTask = idx.lastTaskSeqByPart.TryGetValue(partId, out int lt) ? lt : int.MinValue;
            if (lastTask > int.MinValue && viewSeq > lastTask)
            {
                return new PoseResolution(
                    ToVec3(pp.assembledPosition),
                    ToQuat(pp.assembledRotation),
                    NormalizeScale(ToVec3(pp.assembledScale), pp),
                    PoseSource.Assembled,
                    string.Empty);
            }

            // [6] NO TASK introduction: visible via visualPartIds but no task
            //     claim yet. Parts sit at their bench/layout startPosition and
            //     don't move until a later step acts on them.
            int firstTask = idx.firstTaskSeqByPart.TryGetValue(partId, out int ft) ? ft : int.MaxValue;
            if (viewSeq < firstTask)
            {
                return new PoseResolution(
                    ToVec3(pp.startPosition),
                    ToQuat(pp.startRotation),
                    NormalizeScale(ToVec3(pp.startScale), pp),
                    firstTask == int.MaxValue ? PoseSource.NoTaskStart : PoseSource.Start,
                    string.Empty);
            }

            // [7] Safety net — shouldn't reach here if invariants hold. Return
            //     assembledPosition as a conservative default; invariant #1 at
            //     load flags this as a coverage gap.
            return new PoseResolution(
                ToVec3(pp.assembledPosition),
                ToQuat(pp.assembledRotation),
                NormalizeScale(ToVec3(pp.assembledScale), pp),
                PoseSource.Assembled,
                "fallback");
        }

        private static Vector3 ToVec3(SceneFloat3 v) => new Vector3(v.x, v.y, v.z);
        private static Quaternion ToQuat(SceneQuaternion q)
            => (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f)
                ? Quaternion.identity
                : new Quaternion(q.x, q.y, q.z, q.w);

        /// <summary>
        /// Zero scale is a common authoring miss (missing authored field = all
        /// zeros after JsonUtility). Fall back to the part's assembledScale,
        /// then to identity. Matches legacy fallback in
        /// <c>TTAW.Parts.cs TryGetStepAwarePose</c> and spawner.
        /// </summary>
        private static Vector3 NormalizeScale(Vector3 scl, PartPreviewPlacement pp)
        {
            if (scl.sqrMagnitude >= 0.00001f) return scl;
            var a = ToVec3(pp.assembledScale);
            if (a.sqrMagnitude >= 0.00001f) return a;
            return Vector3.one;
        }
    }
}
