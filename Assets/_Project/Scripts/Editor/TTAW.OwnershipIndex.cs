using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.Editor
{
    /// <summary>
    /// Per-partId snapshot of where a part lives across the authored package:
    /// which subassembly owns it, which Place step physically places it, and
    /// every other step that requires / makes-optional / renders it as a
    /// visual. Populated by <see cref="PartOwnershipIndex.Build"/> once per
    /// authoring-window rebuild so the four proactive-guidance surfaces
    /// (drop-zone pre-check, part ownership panel, step "owns" row, task-row
    /// cross-ref badges) share one cached answer instead of each scanning
    /// <c>_pkg.steps</c> on every repaint.
    ///
    /// Mirrors the load-time
    /// <see cref="OSE.Content.Validation.PartOwnershipExclusivityPass"/>
    /// rules 1 and 2. Stays in sync via the editor's <c>BuildPartList</c>
    /// call which re-builds this whenever parts / steps / subassemblies
    /// change.
    /// </summary>
    internal readonly struct PartOwnership
    {
        /// <summary>Non-aggregate subassembly that contains this part, or null.</summary>
        public readonly string subassemblyId;

        /// <summary>
        /// Sequence index of the FIRST Place-family step that places this
        /// part (lowest seqIndex). -1 when no Place step requires it.
        /// Multi-placement is supported — see <see cref="placeStepSeqs"/> for
        /// the full set.
        /// </summary>
        public readonly int    placeStepSeq;

        /// <summary>Step id of the first Place-family owner, or null when <see cref="placeStepSeq"/> is -1.</summary>
        public readonly string placeStepId;

        /// <summary>Every Place-family step sequenceIndex that requires this part, sorted ascending. Empty when no Place step requires it.</summary>
        public readonly int[]  placeStepSeqs;

        /// <summary>Every Place-family step id that requires this part, in the same order as <see cref="placeStepSeqs"/>.</summary>
        public readonly string[] placeStepIds;

        /// <summary>Every step seq (sorted) where the part is in <c>requiredPartIds</c>, including Place owners.</summary>
        public readonly int[]  requiredAtSeqs;
        public readonly int[]  optionalAtSeqs;
        public readonly int[]  visualAtSeqs;

        /// <summary>
        /// Legacy "conflict" list — step ids other than the first Place owner.
        /// Retained so existing UI can render a "multi-placed" chip. Presence
        /// no longer indicates an error; multi-placement is a supported
        /// authoring pattern.
        /// </summary>
        public readonly string[] conflictingPlaceStepIds;

        /// <summary>Rule-1 conflict: multiple non-aggregate subassemblies claim this partId. Empty when clean.</summary>
        public readonly string[] conflictingSubassemblyIds;

        public PartOwnership(
            string subassemblyId,
            int[] placeStepSeqs, string[] placeStepIds,
            int[] requiredAtSeqs, int[] optionalAtSeqs, int[] visualAtSeqs,
            string[] conflictingSubassemblyIds)
        {
            this.subassemblyId              = subassemblyId;
            this.placeStepSeqs              = placeStepSeqs              ?? Array.Empty<int>();
            this.placeStepIds               = placeStepIds               ?? Array.Empty<string>();
            this.requiredAtSeqs             = requiredAtSeqs             ?? Array.Empty<int>();
            this.optionalAtSeqs             = optionalAtSeqs             ?? Array.Empty<int>();
            this.visualAtSeqs               = visualAtSeqs               ?? Array.Empty<int>();
            this.conflictingSubassemblyIds  = conflictingSubassemblyIds  ?? Array.Empty<string>();
            if (this.placeStepSeqs.Length > 0)
            {
                this.placeStepSeq = this.placeStepSeqs[0];
                this.placeStepId  = this.placeStepIds.Length > 0 ? this.placeStepIds[0] : null;
            }
            else
            {
                this.placeStepSeq = -1;
                this.placeStepId  = null;
            }
            this.conflictingPlaceStepIds = this.placeStepIds.Length > 1
                ? ArraySlice(this.placeStepIds, 1)
                : Array.Empty<string>();
        }

        private static string[] ArraySlice(string[] src, int startIdx)
        {
            if (src == null || startIdx >= src.Length) return Array.Empty<string>();
            var dst = new string[src.Length - startIdx];
            Array.Copy(src, startIdx, dst, 0, dst.Length);
            return dst;
        }

        public static PartOwnership Empty => new PartOwnership(null, null, null, null, null, null, null);

        public bool HasPlaceOwner     => placeStepSeqs.Length > 0;
        /// <summary>True when more than one Place-family step places this part (now allowed — informational only).</summary>
        public bool HasMultiplePlaces => placeStepSeqs.Length > 1;
        [Obsolete("Use HasMultiplePlaces; multi-placement is supported, not a conflict.")]
        public bool HasPlaceConflict  => HasMultiplePlaces;
        public bool HasSubConflict    => conflictingSubassemblyIds.Length > 0;
        public bool HasAnyConflict    => HasSubConflict; // multi-place no longer counted as conflict

        /// <summary>True when at least one step (any role) references this part.</summary>
        public bool IsReferenced
            => placeStepSeqs.Length > 0
            || requiredAtSeqs.Length > 0
            || optionalAtSeqs.Length > 0
            || visualAtSeqs.Length   > 0;
    }

    /// <summary>
    /// Package-wide cache of <see cref="PartOwnership"/> entries plus step-seq
    /// ↔ step-id lookup helpers the surfaces need for clickable chips.
    /// Immutable after <see cref="Build"/>; discard and rebuild via
    /// <see cref="ToolTargetAuthoringWindow.BuildPartList"/> when data changes.
    /// </summary>
    internal sealed class PartOwnershipIndex
    {
        private readonly Dictionary<string, PartOwnership> _byPart;
        private readonly Dictionary<int, string>           _stepIdBySeq;

        private PartOwnershipIndex(
            Dictionary<string, PartOwnership> byPart,
            Dictionary<int, string> stepIdBySeq)
        {
            _byPart      = byPart      ?? new Dictionary<string, PartOwnership>(StringComparer.Ordinal);
            _stepIdBySeq = stepIdBySeq ?? new Dictionary<int, string>();
        }

        public static PartOwnershipIndex Empty { get; } = new PartOwnershipIndex(null, null);

        public PartOwnership ForPart(string partId)
            => !string.IsNullOrEmpty(partId) && _byPart.TryGetValue(partId, out var o)
                ? o : PartOwnership.Empty;

        public string StepIdForSeq(int seq)
            => _stepIdBySeq.TryGetValue(seq, out var id) ? id : null;

        public static PartOwnershipIndex Build(MachinePackageDefinition pkg)
        {
            if (pkg == null) return Empty;

            var stepIdBySeq = new Dictionary<int, string>();
            if (pkg.steps != null)
            {
                foreach (var s in pkg.steps)
                    if (s != null && !string.IsNullOrEmpty(s.id)) stepIdBySeq[s.sequenceIndex] = s.id;
            }

            // Subassembly membership: which non-aggregate sub owns each part, and
            // whether multiple subs claim the same part (Rule 1 conflict).
            var subByPart       = new Dictionary<string, string>(StringComparer.Ordinal);
            var subConflictList = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (pkg.subassemblies != null)
            {
                foreach (var sub in pkg.subassemblies)
                {
                    if (sub == null || sub.isAggregate || sub.partIds == null || string.IsNullOrEmpty(sub.id)) continue;
                    foreach (var pid in sub.partIds)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!subByPart.ContainsKey(pid))
                        {
                            subByPart[pid] = sub.id;
                            continue;
                        }
                        if (!subConflictList.TryGetValue(pid, out var conflicts))
                        {
                            conflicts = new List<string> { subByPart[pid] };
                            subConflictList[pid] = conflicts;
                        }
                        if (!conflicts.Contains(sub.id)) conflicts.Add(sub.id);
                    }
                }
            }

            // Step roles per part (Required / Optional / Visual) + the full
            // ordered list of Place-family owners. Multi-placement is now a
            // supported authoring pattern (loose alignment → final pose), so
            // every Place step's appearance in requiredPartIds is recorded.
            var required      = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var optional      = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var visual        = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var placeOwners   = new Dictionary<string, List<(int seq, string id)>>(StringComparer.Ordinal);

            static void Note(Dictionary<string, List<int>> map, string pid, int seq)
            {
                if (string.IsNullOrEmpty(pid)) return;
                if (!map.TryGetValue(pid, out var list)) map[pid] = list = new List<int>();
                if (!list.Contains(seq)) list.Add(seq);
            }

            if (pkg.steps != null)
            {
                var sortedSteps = new List<StepDefinition>();
                foreach (var s in pkg.steps) if (s != null) sortedSteps.Add(s);
                sortedSteps.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                foreach (var s in sortedSteps)
                {
                    int seq = s.sequenceIndex;
                    bool isPlace = s.ResolvedFamily == StepFamily.Place;

                    if (s.requiredPartIds != null)
                    {
                        foreach (var pid in s.requiredPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            Note(required, pid, seq);
                            if (!isPlace) continue;
                            if (!placeOwners.TryGetValue(pid, out var list))
                                placeOwners[pid] = list = new List<(int, string)>();
                            // De-dupe: a step that lists the same partId twice
                            // in requiredPartIds shouldn't inflate the owner count.
                            bool already = false;
                            foreach (var existing in list)
                                if (string.Equals(existing.id, s.id, StringComparison.Ordinal)) { already = true; break; }
                            if (!already) list.Add((seq, s.id));
                        }
                    }

                    if (s.optionalPartIds != null)
                        foreach (var pid in s.optionalPartIds) Note(optional, pid, seq);
                    if (s.visualPartIds != null)
                        foreach (var pid in s.visualPartIds) Note(visual, pid, seq);
                }
            }

            // Union every partId we saw so callers can ForPart() any of them.
            var allParts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in subByPart.Keys)    allParts.Add(k);
            foreach (var k in required.Keys)     allParts.Add(k);
            foreach (var k in optional.Keys)     allParts.Add(k);
            foreach (var k in visual.Keys)       allParts.Add(k);
            if (pkg.parts != null)
                foreach (var p in pkg.parts) if (p != null && !string.IsNullOrEmpty(p.id)) allParts.Add(p.id);

            var byPart = new Dictionary<string, PartOwnership>(StringComparer.Ordinal);
            foreach (var pid in allParts)
            {
                string subId       = subByPart.TryGetValue(pid, out var s0) ? s0 : null;
                int[]    req       = required.TryGetValue(pid, out var r) ? ToSortedArray(r) : Array.Empty<int>();
                int[]    opt       = optional.TryGetValue(pid, out var o) ? ToSortedArray(o) : Array.Empty<int>();
                int[]    vis       = visual.TryGetValue(pid,   out var v) ? ToSortedArray(v) : Array.Empty<int>();
                int[]    placeSeqs = Array.Empty<int>();
                string[] placeIds  = Array.Empty<string>();
                if (placeOwners.TryGetValue(pid, out var owners) && owners.Count > 0)
                {
                    // Already inserted in ascending-seq order (see sortedSteps above).
                    placeSeqs = new int[owners.Count];
                    placeIds  = new string[owners.Count];
                    for (int i = 0; i < owners.Count; i++) { placeSeqs[i] = owners[i].seq; placeIds[i] = owners[i].id; }
                }
                string[] scon = subConflictList.TryGetValue(pid, out var sc)
                    ? sc.ToArray() : Array.Empty<string>();
                byPart[pid] = new PartOwnership(subId, placeSeqs, placeIds, req, opt, vis, scon);
            }

            return new PartOwnershipIndex(byPart, stepIdBySeq);
        }

        private static int[] ToSortedArray(List<int> list)
        {
            if (list == null || list.Count == 0) return Array.Empty<int>();
            var arr = list.ToArray();
            Array.Sort(arr);
            return arr;
        }
    }
}
