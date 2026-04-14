using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Prebuilt lookup structures that <see cref="PoseResolver.Resolve"/> needs
    /// to answer "what is this part's pose at this seq?" in constant time.
    /// Built once per <see cref="MachinePackageNormalizer.Normalize"/> call and
    /// cached inside the <see cref="PoseTable"/> bake; never exposed to editor
    /// or runtime callers.
    ///
    /// Everything here is derived from the raw
    /// <see cref="MachinePackageDefinition"/> — no additional authoring. The
    /// point of pre-computation is to prevent live resolvers from re-walking
    /// arrays on every lookup and to give one canonical answer to questions
    /// like "is this part a NO-TASK intro?" or "when is this subassembly
    /// stacked?".
    /// </summary>
    public sealed class PoseResolverIndex
    {
        // ── Steps ─────────────────────────────────────────────────────────
        /// <summary>Every step, sorted by ascending sequenceIndex.</summary>
        public readonly List<StepDefinition> orderedSteps;
        /// <summary>stepId → sequenceIndex.</summary>
        public readonly Dictionary<string, int> seqByStepId;

        // ── Parts ─────────────────────────────────────────────────────────
        /// <summary>partId → its <see cref="PartPreviewPlacement"/> (or null).</summary>
        public readonly Dictionary<string, PartPreviewPlacement> placementByPart;

        /// <summary>
        /// partId → smallest seq at which this part is referenced by ANY source
        /// (requiredPartIds / optionalPartIds / visualPartIds / subassembly member
        /// via a step's requiredSubassemblyId). <see cref="int.MaxValue"/> if unseen.
        /// </summary>
        public readonly Dictionary<string, int> firstVisibleSeqByPart;

        /// <summary>
        /// partId → largest seq at which this part is referenced by ANY source.
        /// <see cref="int.MinValue"/> if unseen.
        /// </summary>
        public readonly Dictionary<string, int> lastVisibleSeqByPart;

        /// <summary>
        /// partId → smallest seq at which this part is a TASK (required or
        /// optional — not visual-only). <see cref="int.MaxValue"/> if never a task.
        /// </summary>
        public readonly Dictionary<string, int> firstTaskSeqByPart;

        /// <summary>
        /// partId → largest seq at which this part is a task. <see cref="int.MinValue"/>
        /// if never a task.
        /// </summary>
        public readonly Dictionary<string, int> lastTaskSeqByPart;

        /// <summary>
        /// partId → set of seqs at which this part is a required/optional task
        /// (so the resolver can answer "does this seq act on this part?" in O(1)).
        /// </summary>
        public readonly Dictionary<string, HashSet<int>> taskSeqsByPart;

        /// <summary>
        /// partId → author-written stepPose entries only (synthetic
        /// <see cref="MachinePackageNormalizer.AutoNoTaskLabel"/> entries excluded),
        /// pre-resolved with each entry's effective [fromSeq..throughSeq] span.
        /// </summary>
        public readonly Dictionary<string, List<ResolvedAuthorSpan>> authorSpansByPart;

        // ── Subassembly / groups ─────────────────────────────────────────
        /// <summary>
        /// partId → id of the (non-aggregate) subassembly that owns it, if any.
        /// Used to detect group rigid-body membership.
        /// </summary>
        public readonly Dictionary<string, string> subassemblyIdByMember;

        /// <summary>
        /// (subassemblyId, targetId) → committed integrated placement. Lookup
        /// key built via <see cref="PackKey"/> for allocation-free access.
        /// </summary>
        public readonly Dictionary<string, IntegratedSubassemblyPreviewPlacement> integratedBySubTarget;

        /// <summary>
        /// (subassemblyId, targetId, partId) → integrated member placement.
        /// Flat key so lookup is one hash.
        /// </summary>
        public readonly Dictionary<string, IntegratedMemberPreviewPlacement> integratedMemberBySubTargetPart;

        /// <summary>
        /// Ordered list of "a subassembly was stacked onto a target at this seq"
        /// events, one per step with <c>requiredSubassemblyId</c> + <c>targetIds[0]</c>.
        /// Ordered ascending by seq.
        /// </summary>
        public readonly List<StackingEvent> stackingEvents;

        public PoseResolverIndex(MachinePackageDefinition pkg)
        {
            if (pkg == null) throw new ArgumentNullException(nameof(pkg));

            orderedSteps = new List<StepDefinition>();
            seqByStepId  = new Dictionary<string, int>(StringComparer.Ordinal);
            if (pkg.steps != null)
            {
                foreach (var s in pkg.steps) if (s != null) orderedSteps.Add(s);
                orderedSteps.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));
                foreach (var s in orderedSteps)
                    if (!string.IsNullOrEmpty(s.id)) seqByStepId[s.id] = s.sequenceIndex;
            }

            placementByPart = new Dictionary<string, PartPreviewPlacement>(StringComparer.Ordinal);
            if (pkg.previewConfig?.partPlacements != null)
            {
                foreach (var pp in pkg.previewConfig.partPlacements)
                    if (pp != null && !string.IsNullOrEmpty(pp.partId))
                        placementByPart[pp.partId] = pp;
            }

            firstVisibleSeqByPart = new Dictionary<string, int>(StringComparer.Ordinal);
            lastVisibleSeqByPart  = new Dictionary<string, int>(StringComparer.Ordinal);
            firstTaskSeqByPart    = new Dictionary<string, int>(StringComparer.Ordinal);
            lastTaskSeqByPart     = new Dictionary<string, int>(StringComparer.Ordinal);
            taskSeqsByPart        = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

            void NoteVisible(string pid, int seq)
            {
                if (string.IsNullOrEmpty(pid)) return;
                if (!firstVisibleSeqByPart.TryGetValue(pid, out int cur) || seq < cur) firstVisibleSeqByPart[pid] = seq;
                if (!lastVisibleSeqByPart.TryGetValue(pid, out int lst)  || seq > lst) lastVisibleSeqByPart[pid]  = seq;
            }
            void NoteTask(string pid, int seq)
            {
                if (string.IsNullOrEmpty(pid)) return;
                if (!firstTaskSeqByPart.TryGetValue(pid, out int cur) || seq < cur) firstTaskSeqByPart[pid] = seq;
                if (!lastTaskSeqByPart.TryGetValue(pid, out int lst)  || seq > lst) lastTaskSeqByPart[pid]  = seq;
                if (!taskSeqsByPart.TryGetValue(pid, out var set))
                    taskSeqsByPart[pid] = set = new HashSet<int>();
                set.Add(seq);
                NoteVisible(pid, seq);
            }

            stackingEvents = new List<StackingEvent>();
            foreach (var s in orderedSteps)
            {
                int seq = s.sequenceIndex;
                if (s.requiredPartIds != null) foreach (var pid in s.requiredPartIds) NoteTask(pid, seq);
                if (s.optionalPartIds != null) foreach (var pid in s.optionalPartIds) NoteTask(pid, seq);
                if (s.visualPartIds   != null) foreach (var pid in s.visualPartIds)   NoteVisible(pid, seq);

                if (!string.IsNullOrEmpty(s.requiredSubassemblyId)
                    && pkg.TryGetSubassembly(s.requiredSubassemblyId, out var subDef)
                    && subDef?.partIds != null)
                {
                    string targetId = s.targetIds != null && s.targetIds.Length > 0 ? s.targetIds[0] : null;
                    if (!string.IsNullOrEmpty(targetId))
                        stackingEvents.Add(new StackingEvent(seq, s.requiredSubassemblyId, targetId));
                    foreach (var pid in subDef.partIds) NoteVisible(pid, seq);
                }
            }
            stackingEvents.Sort((a, b) => a.seq.CompareTo(b.seq));

            // author-written spans
            authorSpansByPart = new Dictionary<string, List<ResolvedAuthorSpan>>(StringComparer.Ordinal);
            foreach (var kvp in placementByPart)
            {
                var pp = kvp.Value;
                if (pp.stepPoses == null || pp.stepPoses.Length == 0) continue;
                var list = new List<ResolvedAuthorSpan>(pp.stepPoses.Length);
                foreach (var sp in pp.stepPoses)
                {
                    if (sp == null) continue;
                    if (!string.IsNullOrEmpty(sp.label)
                        && sp.label.StartsWith(MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                        continue; // legacy synthetic — ignored; resolver handles NO TASK directly

                    int anchorSeq = seqByStepId.TryGetValue(sp.stepId ?? "", out int a) ? a : int.MinValue;
                    int fromSeq = string.IsNullOrEmpty(sp.propagateFromStep)
                        ? (anchorSeq >= 0 ? anchorSeq : int.MinValue)
                        : (seqByStepId.TryGetValue(sp.propagateFromStep, out int f) ? f : int.MinValue);
                    int throughSeq = string.IsNullOrEmpty(sp.propagateThroughStep)
                        ? int.MaxValue
                        : (seqByStepId.TryGetValue(sp.propagateThroughStep, out int t) ? t : int.MaxValue);
                    list.Add(new ResolvedAuthorSpan(sp, anchorSeq, fromSeq, throughSeq));
                }
                if (list.Count > 0) authorSpansByPart[kvp.Key] = list;
            }

            // subassembly membership (non-aggregate only — aggregates don't drive poses)
            subassemblyIdByMember = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pkg.subassemblies != null)
            {
                foreach (var sub in pkg.subassemblies)
                {
                    if (sub == null || sub.isAggregate || sub.partIds == null) continue;
                    foreach (var pid in sub.partIds)
                        if (!string.IsNullOrEmpty(pid) && !subassemblyIdByMember.ContainsKey(pid))
                            subassemblyIdByMember[pid] = sub.id;
                }
            }

            // integrated placements
            integratedBySubTarget          = new Dictionary<string, IntegratedSubassemblyPreviewPlacement>(StringComparer.Ordinal);
            integratedMemberBySubTargetPart = new Dictionary<string, IntegratedMemberPreviewPlacement>(StringComparer.Ordinal);
            var integrated = pkg.previewConfig?.integratedSubassemblyPlacements;
            if (integrated != null)
            {
                foreach (var pl in integrated)
                {
                    if (pl == null || string.IsNullOrEmpty(pl.subassemblyId) || string.IsNullOrEmpty(pl.targetId)) continue;
                    integratedBySubTarget[PackKey(pl.subassemblyId, pl.targetId)] = pl;
                    if (pl.memberPlacements == null) continue;
                    foreach (var mp in pl.memberPlacements)
                    {
                        if (mp == null || string.IsNullOrEmpty(mp.partId)) continue;
                        integratedMemberBySubTargetPart[PackKey(pl.subassemblyId, pl.targetId, mp.partId)] = mp;
                    }
                }
            }
        }

        public static string PackKey(string a, string b) => a + "\u0001" + b;
        public static string PackKey(string a, string b, string c) => a + "\u0001" + b + "\u0001" + c;
    }

    /// <summary>
    /// A single stacking moment in the step timeline: at seq S, subassembly
    /// <see cref="subassemblyId"/> is committed onto target <see cref="targetId"/>.
    /// Used by the resolver to decide "is this part an IntegratedMember at seq N?"
    /// </summary>
    public readonly struct StackingEvent
    {
        public readonly int seq;
        public readonly string subassemblyId;
        public readonly string targetId;
        public StackingEvent(int seq, string subassemblyId, string targetId)
        {
            this.seq = seq;
            this.subassemblyId = subassemblyId;
            this.targetId = targetId;
        }
    }

    /// <summary>
    /// Author-written stepPose paired with its pre-resolved [from..through] seq
    /// bounds so the resolver never re-walks <c>seqByStepId</c>.
    /// </summary>
    public readonly struct ResolvedAuthorSpan
    {
        public readonly StepPoseEntry entry;
        public readonly int anchorSeq;
        public readonly int fromSeq;
        public readonly int throughSeq;
        public ResolvedAuthorSpan(StepPoseEntry entry, int anchorSeq, int fromSeq, int throughSeq)
        {
            this.entry      = entry;
            this.anchorSeq  = anchorSeq;
            this.fromSeq    = fromSeq;
            this.throughSeq = throughSeq;
        }
        public bool Covers(int seq) => seq >= fromSeq && seq <= throughSeq;
    }
}
