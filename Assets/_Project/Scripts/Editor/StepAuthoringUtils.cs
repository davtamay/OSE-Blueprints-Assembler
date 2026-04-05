using System.Collections.Generic;
using System.IO;
using OSE.Content;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Stateless helpers shared between step-based authoring windows
    /// (ToolTargetAuthoringWindow, PartPlacementAuthoringWindow, …).
    ///
    /// All methods are pure — they take inputs and return outputs, mutating
    /// nothing outside their scope.  This makes them safe to call from any
    /// EditorWindow without risk of cross-window side effects.
    /// </summary>
    internal static class StepAuthoringUtils
    {
        // ── Package discovery ─────────────────────────────────────────────────

        /// <summary>
        /// Scans the authoring root for directories that contain a machine.json
        /// and returns their names as package IDs.
        /// </summary>
        internal static string[] DiscoverPackageIds()
        {
            string root = PackageJsonUtils.AuthoringRoot;
            if (!Directory.Exists(root)) return System.Array.Empty<string>();
            var dirs = Directory.GetDirectories(root);
            var ids  = new List<string>();
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "machine.json")))
                    ids.Add(Path.GetFileName(d));
            return ids.ToArray();
        }

        // ── Step option building ──────────────────────────────────────────────

        /// <summary>
        /// Builds three parallel arrays for populating a step-filter dropdown:
        /// <list type="bullet">
        ///   <item><term>options</term><description>Human-readable labels, index 0 = "(All Steps)".</description></item>
        ///   <item><term>ids</term><description>Raw step IDs, index 0 = null.</description></item>
        ///   <item><term>sequenceIdxs</term><description>step.sequenceIndex, index 0 = 0.</description></item>
        /// </list>
        /// Steps are sorted by sequenceIndex.  Steps with no parts/targets are
        /// included so the window always covers the full sequence (matching SessionDriver).
        /// </summary>
        internal static void BuildStepOptions(
            MachinePackageDefinition pkg,
            out string[] options,
            out string[] ids,
            out int[]    sequenceIdxs)
        {
            var optList = new List<string> { "(All Steps)" };
            var idList  = new List<string> { null };
            var seqList = new List<int>    { 0 };

            if (pkg?.steps != null)
            {
                var allSteps = new List<StepDefinition>(pkg.steps.Length);
                foreach (var step in pkg.steps)
                    if (step != null) allSteps.Add(step);
                allSteps.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                foreach (var step in allSteps)
                {
                    // Build display: "[seq] name · requiredParts/targets count"
                    int partCount   = step.requiredPartIds?.Length ?? 0;
                    int targetCount = step.targetIds?.Length ?? 0;
                    string family   = string.IsNullOrEmpty(step.family)
                                       ? step.profile ?? ""
                                       : step.family;
                    string familyPart = string.IsNullOrEmpty(family) ? "" : $"  ·  {family}";
                    string countPart  = partCount > 0
                        ? $"  ·  {partCount} part{(partCount == 1 ? "" : "s")}"
                        : targetCount > 0
                            ? $"  ·  {targetCount} target{(targetCount == 1 ? "" : "s")}"
                            : "  ·  (no parts)";
                    string display = $"[{step.sequenceIndex}] {step.name}{familyPart}{countPart}";
                    optList.Add(display);
                    idList.Add(step.id);
                    seqList.Add(step.sequenceIndex);
                }
            }

            options      = optList.ToArray();
            ids          = idList.ToArray();
            sequenceIdxs = seqList.ToArray();
        }

        // ── SessionDriver sync ────────────────────────────────────────────────

        /// <summary>
        /// Finds the dropdown filter index whose <paramref name="sequenceIdxs"/> entry
        /// matches <paramref name="sequenceIndex"/>.
        /// Returns the index (≥ 1), or -1 if not found.
        /// </summary>
        internal static int FindStepFilterIdx(int[] sequenceIdxs, int sequenceIndex)
        {
            if (sequenceIdxs == null) return -1;
            for (int i = 1; i < sequenceIdxs.Length; i++)
                if (sequenceIdxs[i] == sequenceIndex) return i;
            return -1;
        }
    }
}
