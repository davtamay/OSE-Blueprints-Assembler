using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Detects orphaned parts, targets, hints, and steps (defined but not referenced),
    /// and validates that step sequence indices are contiguous within each assembly.
    /// </summary>
    internal sealed class OrphanDetectionPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            DetectOrphanParts(ctx);
            DetectOrphanTargets(ctx);
            DetectOrphanHints(ctx);
            DetectOrphanSteps(ctx);
            ValidateContiguousSequenceIndices(ctx);
        }

        private static void DetectOrphanParts(ValidationPassContext ctx)
        {
            PartDefinition[] parts = ctx.Package.GetParts();
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (parts.Length == 0) return;

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                string[] required = steps[i].requiredPartIds;
                if (required != null) for (int j = 0; j < required.Length; j++) referenced.Add(required[j]);
                string[] optional = steps[i].optionalPartIds;
                if (optional != null) for (int j = 0; j < optional.Length; j++) referenced.Add(optional[j]);
                // visualPartIds — show-without-require references count too,
                // otherwise visual-only parts get flagged as orphans.
                string[] visual = steps[i].visualPartIds;
                if (visual != null) for (int j = 0; j < visual.Length; j++) referenced.Add(visual[j]);
            }
            foreach (var t in ctx.Package.GetTargets())
                if (!string.IsNullOrEmpty(t?.associatedPartId))
                    referenced.Add(t.associatedPartId);

            for (int i = 0; i < parts.Length; i++)
                if (!string.IsNullOrEmpty(parts[i]?.id) && !referenced.Contains(parts[i].id))
                    ctx.Issues.Add(ValidationPassHelpers.Warning($"parts[{i}]",
                        $"Part '{parts[i].id}' is defined but never referenced by any step or target."));
        }

        private static void DetectOrphanTargets(ValidationPassContext ctx)
        {
            TargetDefinition[] targets = ctx.Package.GetTargets();
            StepDefinition[]   steps   = ctx.Package.GetSteps();
            if (targets.Length == 0) return;

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                string[] tIds = steps[i].targetIds;
                if (tIds != null) for (int j = 0; j < tIds.Length; j++) referenced.Add(tIds[j]);

                var toolActions = steps[i].requiredToolActions;
                if (toolActions != null)
                    for (int j = 0; j < toolActions.Length; j++)
                        if (!string.IsNullOrEmpty(toolActions[j]?.targetId))
                            referenced.Add(toolActions[j].targetId);
            }

            for (int i = 0; i < targets.Length; i++)
                if (!string.IsNullOrEmpty(targets[i]?.id) && !referenced.Contains(targets[i].id))
                    ctx.Issues.Add(ValidationPassHelpers.Warning($"targets[{i}]",
                        $"Target '{targets[i].id}' is defined but never referenced by any step."));
        }

        private static void DetectOrphanHints(ValidationPassContext ctx)
        {
            HintDefinition[] hints = ctx.Package.GetHints();
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (hints.Length == 0) return;

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                string[] flat = steps[i].hintIds;
                if (flat != null) for (int j = 0; j < flat.Length; j++) referenced.Add(flat[j]);

                string[] resolved = steps[i].ResolvedHintIds;
                if (resolved != null && resolved != flat)
                    for (int j = 0; j < resolved.Length; j++) referenced.Add(resolved[j]);
            }
            // Hints referenced as correctionHintId on validation rules
            foreach (var rule in ctx.Package.GetValidationRules())
                if (!string.IsNullOrEmpty(rule?.correctionHintId))
                    referenced.Add(rule.correctionHintId);

            for (int i = 0; i < hints.Length; i++)
                if (!string.IsNullOrEmpty(hints[i]?.id) && !referenced.Contains(hints[i].id))
                    ctx.Issues.Add(ValidationPassHelpers.Warning($"hints[{i}]",
                        $"Hint '{hints[i].id}' is defined but never referenced by any step or validation rule."));
        }

        private static void DetectOrphanSteps(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (steps.Length == 0) return;

            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == null) continue;
                if (string.IsNullOrWhiteSpace(steps[i].assemblyId))
                {
                    ctx.Issues.Add(ValidationPassHelpers.Warning($"steps[{i}]",
                        $"Step '{steps[i].id}' has no assemblyId — it won't appear in any assembly."));
                }
                else if (!ctx.AssemblyIds.Contains(steps[i].assemblyId))
                {
                    ctx.Issues.Add(ValidationPassHelpers.Warning($"steps[{i}]",
                        $"Step '{steps[i].id}' references assemblyId '{steps[i].assemblyId}' which does not exist."));
                }
            }
        }

        private static void ValidateContiguousSequenceIndices(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (steps == null || steps.Length == 0) return;

            var sorted = new StepDefinition[steps.Length];
            Array.Copy(steps, sorted, steps.Length);
            Array.Sort(sorted, (a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

            for (int i = 0; i < sorted.Length; i++)
            {
                int expected = i + 1;
                if (sorted[i].sequenceIndex != expected)
                {
                    ctx.Issues.Add(ValidationPassHelpers.Error("steps",
                        $"sequenceIndex gap or shift: step '{sorted[i].id}' has sequenceIndex {sorted[i].sequenceIndex}, expected {expected}. " +
                        $"Indices must be contiguous 1..{sorted.Length}."));
                    break; // One error is enough to flag the problem
                }
            }
        }
    }
}
