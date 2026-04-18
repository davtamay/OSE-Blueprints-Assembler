using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Load-time structural checks that the pose data a package carries is
    /// well-formed enough for <see cref="PoseResolver"/> to answer every
    /// query deterministically. Runs after <see cref="PoseTable"/> is baked.
    ///
    /// In the current rollout phase these are WARN-only: every violation is
    /// logged with the <c>[POSE-INVARIANT]</c> prefix and the package is
    /// allowed to load. Step 6 of the rewrite flips these to throw so
    /// broken packages never silently reach render code.
    ///
    /// Each invariant prevents a specific past-bug class. See the decision
    /// tree in <see cref="PoseResolver.Resolve"/> for how the resolver
    /// depends on each.
    /// </summary>
    internal static class PoseTableInvariants
    {
        private const string Tag = "[POSE-INVARIANT]";

        public static void Validate(MachinePackageDefinition pkg, PoseResolverIndex idx, PoseTable table)
        {
            if (pkg == null || idx == null || table == null) return;
            int violations = 0;
            // Critical violations indicate silent-wrong-pose bug classes
            // (unresolved step refs, overlapping author spans) — these MUST
            // throw because downstream code will render incorrect poses with
            // no warning. Non-critical (coverage / orphan / structural)
            // downgrade to loud warnings: packages in mid-authoring state
            // legitimately trip them without causing render damage.
            int criticalViolations = 0;

            // [1] Coverage: every step between firstVisibleSeq..lastOrderedSeq
            // for each visible part must produce a non-Hidden entry. Parts
            // referenced by step data but missing from previewConfig have no
            // pose source to emit — that's a separate violation ([4]) and
            // would drown coverage output, so skip them here.
            if (idx.orderedSteps.Count > 0)
            {
                int lastOrderedSeq = idx.orderedSteps[idx.orderedSteps.Count - 1].sequenceIndex;
                foreach (var kvp in idx.firstVisibleSeqByPart)
                {
                    string pid = kvp.Key;
                    if (!idx.placementByPart.ContainsKey(pid)) continue; // no placement → no pose to compute
                    int firstSeq = kvp.Value;
                    foreach (var s in idx.orderedSteps)
                    {
                        int seq = s.sequenceIndex;
                        if (seq < firstSeq) continue;
                        if (seq > lastOrderedSeq) continue;
                        if (!table.TryGet(pid, seq, out _))
                        {
                            Report($"{Tag} [coverage] part '{pid}' has no PoseTable entry at seq {seq} (first visible at {firstSeq}).");
                            violations++;
                        }
                    }
                }
            }

            // [2] Propagate refs: any non-empty stepId reference inside a
            // stepPose must resolve to a real step.
            // [3] Monotonic spans: fromSeq <= throughSeq.
            // [5] Duplicate spans: two author spans for the same part whose
            // seq ranges overlap — first-hit wins in the resolver, so an
            // overlap silently discards the second.
            foreach (var kvp in idx.authorSpansByPart)
            {
                string pid = kvp.Key;
                var list = kvp.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    var sp = list[i];
                    var e  = sp.entry;

                    if (!string.IsNullOrEmpty(e.propagateFromStep) && !idx.seqByStepId.ContainsKey(e.propagateFromStep))
                    { Report($"{Tag} [propagate-ref] part '{pid}' stepPose@'{e.stepId}' has propagateFromStep='{e.propagateFromStep}' that doesn't match any step id."); violations++; criticalViolations++; }
                    if (!string.IsNullOrEmpty(e.propagateThroughStep) && !idx.seqByStepId.ContainsKey(e.propagateThroughStep))
                    { Report($"{Tag} [propagate-ref] part '{pid}' stepPose@'{e.stepId}' has propagateThroughStep='{e.propagateThroughStep}' that doesn't match any step id."); violations++; criticalViolations++; }

                    if (sp.fromSeq > sp.throughSeq)
                    { Report($"{Tag} [monotonic-span] part '{pid}' stepPose@'{e.stepId}' has fromSeq={sp.fromSeq} > throughSeq={sp.throughSeq}."); violations++; criticalViolations++; }

                    // Skip overlap detection when either side is a normalizer-
                    // synthesized entry (e.g. BakeHoldAtEndEndPoses produces
                    // "synthesized:holdAtEnd" records with open-ended forward
                    // propagation). The resolver picks the closest anchor
                    // correctly for those — their formal [anchor..MaxValue]
                    // spans overlap by design, not by authoring error.
                    bool eIsSynth = !string.IsNullOrEmpty(e.label)
                        && (e.label.StartsWith("synthesized:", StringComparison.Ordinal));
                    if (eIsSynth) continue;
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var other = list[j];
                        var oe = other.entry;
                        bool otherIsSynth = !string.IsNullOrEmpty(oe.label)
                            && (oe.label.StartsWith("synthesized:", StringComparison.Ordinal));
                        if (otherIsSynth) continue;
                        if (sp.fromSeq <= other.throughSeq && other.fromSeq <= sp.throughSeq)
                        {
                            Report($"{Tag} [duplicate-span] part '{pid}' has overlapping author stepPoses: '{e.stepId}' [{sp.fromSeq}..{sp.throughSeq}] and '{oe.stepId}' [{other.fromSeq}..{other.throughSeq}].");
                            violations++; criticalViolations++;
                        }
                    }
                }
            }

            // [4] Orphan visualPartIds: every NO-TASK partId must have a
            // PartPreviewPlacement — otherwise the resolver has no pose to
            // surface (step earlier this session: a typo'd partId).
            foreach (var s in idx.orderedSteps)
            {
                if (s.visualPartIds == null) continue;
                foreach (var pid in s.visualPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!idx.placementByPart.ContainsKey(pid))
                    { Report($"{Tag} [orphan-visualPart] step '{s.id}' visualPartIds contains '{pid}' but no partPlacement exists."); violations++; }
                }
            }

            // [6] Integrated member closure.
            var integrated = pkg.previewConfig?.integratedSubassemblyPlacements;
            if (integrated != null)
            {
                var stackedSubs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var ev in idx.stackingEvents) stackedSubs.Add(ev.subassemblyId);

                foreach (var pl in integrated)
                {
                    if (pl == null) continue;
                    if (!string.IsNullOrEmpty(pl.subassemblyId) && !stackedSubs.Contains(pl.subassemblyId))
                    { Report($"{Tag} [integrated-orphan] integratedSubassemblyPlacement '{pl.subassemblyId}|{pl.targetId}' has no step with requiredSubassemblyId='{pl.subassemblyId}'; members will never resolve to IntegratedMember."); violations++; }

                    if (pl.memberPlacements == null) continue;
                    foreach (var mp in pl.memberPlacements)
                    {
                        if (mp == null || string.IsNullOrEmpty(mp.partId)) continue;
                        if (!idx.placementByPart.ContainsKey(mp.partId))
                        { Report($"{Tag} [integrated-member-orphan] integrated '{pl.subassemblyId}|{pl.targetId}' references partId '{mp.partId}' with no partPlacement."); violations++; }
                    }
                }
            }

            // [8] FirstVisibleSeq consistent — no PoseTable entry can exist
            // before a part's first visible seq. Structural guarantee of the
            // bake, but verify so future refactors can't silently break it.
            foreach (var key in table.Keys)
            {
                int firstSeq = table.FirstVisibleSeq(key.partId);
                if (key.viewSeq < firstSeq)
                { Report($"{Tag} [firstVisible] part '{key.partId}' has pose entry at seq {key.viewSeq} before firstVisibleSeq={firstSeq}."); violations++; }
            }

            if (criticalViolations > 0)
                throw new InvalidOperationException(
                    $"{Tag} {criticalViolations} critical pose invariant violation(s) in package '{pkg.packageId}' — see console. " +
                    "These indicate silent-wrong-pose bugs (unresolved step refs, inverted ranges, overlapping author spans) and would render incorrectly. Fix the authored data before loading.");
            if (violations > 0)
                Debug.LogWarning($"{Tag} {violations} non-critical violation(s) logged for package '{pkg.packageId}'. Coverage/orphan issues may reflect mid-authoring state; review the individual warnings.");
        }

        private static void Report(string msg) => Debug.LogWarning(msg);
    }
}
