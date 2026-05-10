using System;
using OSE.Content;

namespace OSE.Editor
{
    /// <summary>
    /// Load-time healer for <see cref="PartPreviewPlacement.stepPoses"/>.
    /// Empty-label entries pre-date the <c>label="Custom"</c> convention —
    /// they're either (a) legitimate auto-captures from
    /// <c>CaptureCurrentPoseAsStepPose</c> that pre-date the label fix, or
    /// (b) stale legacy artifacts from the deprecated
    /// <c>AutoPromoteAlienPartToNoTaskWaypoint</c> flow.
    ///
    /// We distinguish with positive evidence: if the entry's <c>(partId,
    /// stepId)</c> matches a current <see cref="StepDefinition.visualPartIds"/>
    /// membership, it's case (a) — promote label to "Custom" so the load-time
    /// strip leaves it alone and the inspector's NO TASK pose detection
    /// finds it. Otherwise it's case (b), and we KEEP IT (per
    /// <c>feedback_load_time_mutations_must_be_additive.md</c> — load-time
    /// mutations must be additive, not destructive). Truly-orphan entries
    /// stay on disk; an opt-in cleanup tool can be added later if they
    /// cause UI clutter.
    ///
    /// Extracted as a static helper so it's unit-testable without booting a
    /// full <see cref="ToolTargetAuthoringWindow"/> instance — see
    /// <c>StepPoseLoadHealerTests</c> for the round-trip safety net.
    /// </summary>
    public static class StepPoseLoadHealer
    {
        public static (int rescued, int orphans) RescueEmptyLabelStepPoses(MachinePackageDefinition pkg)
        {
            int rescued = 0, orphans = 0;
            var placements = pkg?.previewConfig?.partPlacements;
            if (placements == null) return (rescued, orphans);

            // Build a (partId|stepId) set of every current visualPartIds
            // membership. Entries hashing into this set are author-intended
            // NoTask poses and get rescued.
            var noTaskPair = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            if (pkg.steps != null)
            {
                foreach (var step in pkg.steps)
                {
                    if (step?.visualPartIds == null || string.IsNullOrEmpty(step.id)) continue;
                    foreach (var pid in step.visualPartIds)
                        if (!string.IsNullOrEmpty(pid)) noTaskPair.Add(pid + "|" + step.id);
                }
            }

            foreach (var pp in placements)
            {
                if (pp == null || pp.stepPoses == null || pp.stepPoses.Length == 0) continue;
                foreach (var sp in pp.stepPoses)
                {
                    if (sp == null) continue;
                    if (!string.IsNullOrEmpty(sp.label)) continue;        // author-created → leave alone
                    string key = (pp.partId ?? "") + "|" + (sp.stepId ?? "");
                    if (noTaskPair.Contains(key))
                    {
                        sp.label = "Custom";   // in-place promotion
                        rescued++;
                    }
                    else
                    {
                        orphans++;             // logged at the call site, NOT deleted
                    }
                }
            }
            return (rescued, orphans);
        }
    }
}
