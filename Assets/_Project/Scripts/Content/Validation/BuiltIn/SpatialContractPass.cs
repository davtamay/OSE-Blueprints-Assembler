using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Layer 3 — Spatial Contract validation.
    ///
    /// Checks physical-layer consistency that spans across parts and steps,
    /// enforcing the seven absolute ordering rules in CLAUDE.md and the
    /// staging-pose uniqueness rule.
    ///
    /// Three checks:
    ///   1. Staging pose collision — two authored parts within 0.05 m of each other
    ///      (CLAUDE.md rule 7: "Staging positions must be unique.")
    ///   2. Subassembly ordering — Confirm-family step is the first step in its
    ///      subassembly by sequenceIndex (nothing has been placed yet; the Confirm
    ///      has no physical state to confirm)
    ///   3. Use-step tool coverage — a Use-family step has neither
    ///      requiredToolActions nor relevantToolIds; the runtime cannot know which
    ///      tool to offer the user
    /// </summary>
    internal sealed class SpatialContractPass : IPackageValidationPass
    {
        /// <summary>
        /// Minimum separation between any two part staging positions.
        /// Parts closer than this will visually overlap in the preview scene.
        /// </summary>
        private const float StagingCollisionThresholdM = 0.05f;

        public void Execute(ValidationPassContext ctx)
        {
            CheckStagingPoseCollisions(ctx);
            CheckSubassemblyOrdering(ctx);
            CheckUseStepToolCoverage(ctx);
        }

        // ── Check 1: Staging pose collision ──────────────────────────────────

        private static void CheckStagingPoseCollisions(ValidationPassContext ctx)
        {
            PartDefinition[] parts = ctx.Package.GetParts();
            var issues = ctx.Issues;

            // Collect parts that have an authored staging position.
            // We only check explicitly authored positions — parts without stagingPose
            // fall back to the previewConfig row layout, which the normalizer handles.
            var staged = new List<(int index, string id, float x, float y, float z)>();
            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition p = parts[i];
                if (p == null) continue;
                var pose = p.stagingPose;
                if (pose == null) continue;
                var pos = pose.position;
                staged.Add((i, p.id ?? $"parts[{i}]", pos.x, pos.y, pos.z));
            }

            // O(n²) pair check — part counts are small (< 200 per package).
            float threshold2 = StagingCollisionThresholdM * StagingCollisionThresholdM;
            for (int a = 0; a < staged.Count; a++)
            {
                for (int b = a + 1; b < staged.Count; b++)
                {
                    float dx = staged[a].x - staged[b].x;
                    float dy = staged[a].y - staged[b].y;
                    float dz = staged[a].z - staged[b].z;
                    float dist2 = dx * dx + dy * dy + dz * dz;

                    if (dist2 < threshold2)
                    {
                        float dist = (float)Math.Sqrt(dist2);
                        issues.Add(ValidationPassHelpers.Warning(
                            $"parts[{staged[a].index}].stagingPose.position",
                            $"Parts '{staged[a].id}' and '{staged[b].id}' have overlapping staging positions " +
                            $"({dist * 100f:F1} cm apart, minimum {StagingCollisionThresholdM * 100f:F0} cm required). " +
                            $"They will visually overlap in the preview scene."));
                    }
                }
            }
        }

        // ── Check 2: Subassembly ordering ─────────────────────────────────────

        private static void CheckSubassemblyOrdering(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            var issues = ctx.Issues;

            // Group steps by subassemblyId; skip steps with no subassembly.
            var bySubassembly = new Dictionary<string, List<StepDefinition>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                StepDefinition step = steps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.subassemblyId)) continue;
                if (!bySubassembly.TryGetValue(step.subassemblyId, out var list))
                {
                    list = new List<StepDefinition>();
                    bySubassembly[step.subassemblyId] = list;
                }
                list.Add(step);
            }

            foreach (var kvp in bySubassembly)
            {
                string saId = kvp.Key;
                List<StepDefinition> saSteps = kvp.Value;

                // Sort by sequenceIndex ascending to find the first step.
                saSteps.Sort((x, y) => x.sequenceIndex.CompareTo(y.sequenceIndex));

                StepDefinition first = saSteps[0];
                if (first.ResolvedFamily == StepFamily.Confirm)
                {
                    issues.Add(ValidationPassHelpers.Warning(
                        $"steps[*].subassemblyId={saId}",
                        $"Subassembly '{saId}': first step by sequenceIndex is '{first.id}' " +
                        $"(seq {first.sequenceIndex}, family Confirm). " +
                        $"A Confirm step cannot confirm anything that has not yet been placed. " +
                        $"Expected a Place or Use step to precede it."));
                }
            }
        }

        // ── Check 3: Use step tool coverage ───────────────────────────────────

        private static void CheckUseStepToolCoverage(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            var issues = ctx.Issues;

            for (int i = 0; i < steps.Length; i++)
            {
                StepDefinition step = steps[i];
                if (step == null) continue;
                if (step.ResolvedFamily != StepFamily.Use) continue;

                bool hasToolActions    = ValidationPassHelpers.HasAnyValues(
                    step.requiredToolActions != null
                        ? Array.ConvertAll(step.requiredToolActions, a => a?.toolId)
                        : null);
                bool hasRelevantTools  = ValidationPassHelpers.HasAnyValues(step.relevantToolIds);

                if (!hasToolActions && !hasRelevantTools)
                {
                    issues.Add(ValidationPassHelpers.Warning(
                        $"steps[{i}]",
                        $"Use-family step '{step.id}' (seq {step.sequenceIndex}) has neither " +
                        $"requiredToolActions nor relevantToolIds. The runtime cannot determine " +
                        $"which tool to offer the user for this step."));
                }
            }
        }
    }
}
