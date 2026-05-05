using System;
using System.Collections.Generic;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Per-partGroup lifecycle metadata, baked by
    /// <see cref="MachinePackageNormalizer.BakePartGroupLifecycle"/>.
    ///
    /// A "touch" means the step references the group OR any of its members
    /// in any of these ways: stepGroupId match, requiredPartIds /
    /// optionalPartIds / targetPartIds / visualPartIds intersecting the
    /// group's partIds, animationCue targetSubassemblyId / targetPartIds
    /// match, or a target's associatedPartId / associatedPartGroupId match.
    ///
    /// FirstBuiltSeq is the smallest seqIndex of any touch (the group exists
    /// from this step onward). LastTouchedSeq is the largest. TouchedSeqs is
    /// the sorted-ascending list of every touched seq, suitable for binary
    /// search by <see cref="PartGroupLifecycleResolver"/>.
    /// </summary>
    public sealed class PartGroupLifecycle
    {
        public string GroupId;
        public int FirstBuiltSeq;
        public int LastTouchedSeq;
        public int[] TouchedSeqs;

        public static readonly PartGroupLifecycle Empty = new PartGroupLifecycle
        {
            GroupId = string.Empty,
            FirstBuiltSeq = -1,
            LastTouchedSeq = -1,
            TouchedSeqs = Array.Empty<int>(),
        };
    }

    /// <summary>
    /// Lifecycle tier for a partGroup at a given viewing seqIndex. Both the
    /// TTAW group panel and the runtime parts/groups overlay categorize
    /// groups via <see cref="PartGroupLifecycleResolver.Classify"/> so the
    /// list presentation stays consistent across surfaces.
    /// </summary>
    public enum PartGroupLifecycleTier
    {
        /// <summary>Group not yet built at this seq — don't show.</summary>
        Hidden,
        /// <summary>Built earlier, no recent involvement — show dimmed and collapsed.</summary>
        Built,
        /// <summary>Touched within the recent window — show with medium opacity, collapsed.</summary>
        Recent,
        /// <summary>Touched by the current step — show full color, expanded.</summary>
        Active,
    }

    /// <summary>
    /// Pure read API over <see cref="PartGroupLifecycle"/>. Both UIs query
    /// this — no per-UI filtering logic.
    /// </summary>
    public static class PartGroupLifecycleResolver
    {
        public const int DefaultRecentWindow = 5;

        /// <summary>
        /// Classify a group at the given viewing seqIndex.
        /// </summary>
        /// <param name="lifecycle">Baked lifecycle for the group; null is treated as Hidden.</param>
        /// <param name="currentSeq">Current viewing seqIndex (1-based, matches StepDefinition.sequenceIndex).</param>
        /// <param name="recentWindow">Steps after lastTouchedSeq the group remains in Recent tier. Default 5.</param>
        public static PartGroupLifecycleTier Classify(
            PartGroupLifecycle lifecycle,
            int currentSeq,
            int recentWindow = DefaultRecentWindow)
        {
            if (lifecycle == null || lifecycle.TouchedSeqs == null || lifecycle.TouchedSeqs.Length == 0)
                return PartGroupLifecycleTier.Hidden;

            if (lifecycle.FirstBuiltSeq > currentSeq)
                return PartGroupLifecycleTier.Hidden;

            // Active: currentSeq is in TouchedSeqs (binary search).
            if (Array.BinarySearch(lifecycle.TouchedSeqs, currentSeq) >= 0)
                return PartGroupLifecycleTier.Active;

            // Recent: any touched seq in [currentSeq - recentWindow, currentSeq).
            int lower = currentSeq - recentWindow;
            for (int i = lifecycle.TouchedSeqs.Length - 1; i >= 0; i--)
            {
                int t = lifecycle.TouchedSeqs[i];
                if (t >= currentSeq) continue;          // future or current; skip
                if (t < lower) break;                   // sorted ascending — older entries can't qualify
                return PartGroupLifecycleTier.Recent;
            }

            return PartGroupLifecycleTier.Built;
        }

        /// <summary>
        /// Convenience overload for callers that already have a package and groupId.
        /// Returns Hidden if the package has no baked lifecycle table or the group is missing.
        /// </summary>
        public static PartGroupLifecycleTier Classify(
            MachinePackageDefinition package,
            string groupId,
            int currentSeq,
            int recentWindow = DefaultRecentWindow)
        {
            if (package?.partGroupLifecycleByGroupId == null || string.IsNullOrEmpty(groupId))
                return PartGroupLifecycleTier.Hidden;
            return package.partGroupLifecycleByGroupId.TryGetValue(groupId, out var gl)
                ? Classify(gl, currentSeq, recentWindow)
                : PartGroupLifecycleTier.Hidden;
        }
    }
}
