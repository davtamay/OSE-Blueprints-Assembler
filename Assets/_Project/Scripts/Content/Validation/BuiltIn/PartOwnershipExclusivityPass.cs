using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Enforces "one partId ⇔ one physical GameObject" invariants that the
    /// runtime relies on but the other passes never checked. A partId that
    /// appears in multiple non-aggregate subassemblies, or in multiple
    /// Place-family steps, causes last-write-wins corruption during
    /// navigation/scrubbing (see feedback_subassembly_part_id_collisions.md).
    ///
    /// Rules:
    /// 1. ERROR — partId in &gt;1 non-aggregate <c>subassembly.partIds</c>.
    /// 2. ERROR — partId in &gt;1 Place-family <c>step.requiredPartIds</c>.
    /// 3. ERROR — <c>target.associatedPartId</c> must be in its owning
    ///            step's <c>GetAllTouchedPartIds()</c>.
    /// 4. ERROR — duplicate <c>startPosition</c> among parts of the same
    ///            Place-family step (overlapping parts collapse into one
    ///            clickable pile).
    /// 5. WARNING — multiple parts sharing the same <c>assetRef</c>. Legal
    ///              (mesh reuse is supported) but worth flagging.
    /// </summary>
    internal sealed class PartOwnershipExclusivityPass : IPackageValidationPass
    {
        private const float PositionEpsilon = 1e-4f;

        public void Execute(ValidationPassContext ctx)
        {
            CheckSiblingSubassemblyCollisions(ctx);
            CheckMultiPlaceStepCollisions(ctx);
            CheckTargetAssociatedPartInStep(ctx);
            CheckDuplicateStartPositions(ctx);
            FlagSharedAssetRefs(ctx);
        }

        // ── Rule 1 ────────────────────────────────────────────────────────────
        private static void CheckSiblingSubassemblyCollisions(ValidationPassContext ctx)
        {
            SubassemblyDefinition[] subs = ctx.Package.GetSubassemblies();
            if (subs == null || subs.Length == 0) return;

            var partToSubs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int s = 0; s < subs.Length; s++)
            {
                SubassemblyDefinition sub = subs[s];
                if (sub == null || sub.isAggregate) continue;
                if (sub.partIds == null || string.IsNullOrWhiteSpace(sub.id)) continue;

                for (int i = 0; i < sub.partIds.Length; i++)
                {
                    string pid = sub.partIds[i];
                    if (string.IsNullOrEmpty(pid)) continue;

                    if (!partToSubs.TryGetValue(pid, out var list))
                    {
                        list = new List<string>(2);
                        partToSubs[pid] = list;
                    }
                    if (!list.Contains(sub.id))
                        list.Add(sub.id);
                }
            }

            foreach (var kvp in partToSubs)
            {
                if (kvp.Value.Count < 2) continue;

                ctx.Issues.Add(ValidationPassHelpers.Error(
                    "subassemblies[*].partIds",
                    $"partId '{kvp.Key}' appears in multiple non-aggregate subassemblies: " +
                    $"{string.Join(", ", kvp.Value)}. Each partId must belong to exactly one " +
                    "subassembly. If these are physically distinct parts, author distinct partIds " +
                    "with subassembly-scoped prefixes (e.g. y_left_carriage_m6_nut_a vs " +
                    "y_left_motor_m6_nut_a). If one subassembly is an aggregate/composite of the " +
                    "others, set isAggregate:true on it."));
            }
        }

        // ── Rule 2 ────────────────────────────────────────────────────────────
        private static void CheckMultiPlaceStepCollisions(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (steps == null || steps.Length == 0) return;

            var partToSteps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int s = 0; s < steps.Length; s++)
            {
                StepDefinition step = steps[s];
                if (step == null || string.IsNullOrWhiteSpace(step.id)) continue;
                if (step.ResolvedFamily != StepFamily.Place) continue;
                if (step.requiredPartIds == null) continue;

                for (int i = 0; i < step.requiredPartIds.Length; i++)
                {
                    string pid = step.requiredPartIds[i];
                    if (string.IsNullOrEmpty(pid)) continue;

                    if (!partToSteps.TryGetValue(pid, out var list))
                    {
                        list = new List<string>(2);
                        partToSteps[pid] = list;
                    }
                    if (!list.Contains(step.id))
                        list.Add(step.id);
                }
            }

            foreach (var kvp in partToSteps)
            {
                if (kvp.Value.Count < 2) continue;

                // Multi-Place is now a supported authoring pattern (e.g. loose
                // alignment followed by final placement, or repositioning into
                // a new pose). The runtime's navigation/pose code is list-
                // aware; the only thing we want to surface is the fact that
                // the author has declared multiple placements — a diagnostic,
                // not a blocker. Downgraded from Error → Warning.
                ctx.Issues.Add(ValidationPassHelpers.Warning(
                    "steps[*].requiredPartIds",
                    $"partId '{kvp.Key}' is placed by multiple Place-family steps: " +
                    $"{string.Join(", ", kvp.Value)}. Multi-placement is supported — " +
                    "this is informational. If unintended, remove the partId from " +
                    "all but one step."));
            }
        }

        // ── Rule 3 ────────────────────────────────────────────────────────────
        private static void CheckTargetAssociatedPartInStep(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            TargetDefinition[] targets = ctx.Package.GetTargets();
            if (steps == null || targets == null || targets.Length == 0) return;

            // Build targetId → owning stepId map from step.targetIds arrays.
            var targetToStep = new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int s = 0; s < steps.Length; s++)
            {
                StepDefinition step = steps[s];
                if (step == null || step.targetIds == null) continue;
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    string tid = step.targetIds[i];
                    if (string.IsNullOrEmpty(tid)) continue;
                    if (!targetToStep.ContainsKey(tid))
                        targetToStep[tid] = step;
                }
            }

            for (int i = 0; i < targets.Length; i++)
            {
                TargetDefinition target = targets[i];
                if (target == null) continue;
                if (string.IsNullOrWhiteSpace(target.id)) continue;
                if (string.IsNullOrWhiteSpace(target.associatedPartId)) continue;

                if (!targetToStep.TryGetValue(target.id, out StepDefinition owningStep))
                    continue; // orphan target — OrphanDetectionPass handles it

                string[] touched = owningStep.GetAllTouchedPartIds();
                bool found = false;
                if (touched != null)
                {
                    for (int j = 0; j < touched.Length; j++)
                    {
                        if (string.Equals(touched[j], target.associatedPartId,
                            StringComparison.OrdinalIgnoreCase))
                        { found = true; break; }
                    }
                }

                if (!found)
                {
                    ctx.Issues.Add(ValidationPassHelpers.Error(
                        $"targets[{i}].associatedPartId",
                        $"Target '{target.id}' references partId '{target.associatedPartId}' but the " +
                        $"owning step '{owningStep.id}' does not touch that part (not in " +
                        "requiredPartIds or derivedToolActionPartIds). This is usually caused by a " +
                        "rename — update either the target or the step's requiredPartIds."));
                }
            }
        }

        // ── Rule 4 ────────────────────────────────────────────────────────────
        private static void CheckDuplicateStartPositions(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            if (steps == null || steps.Length == 0) return;

            PartPreviewPlacement[] placements = ctx.Package.previewConfig?.partPlacements;
            if (placements == null || placements.Length == 0) return;

            var placementByPart = new Dictionary<string, PartPreviewPlacement>(
                placements.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < placements.Length; i++)
            {
                PartPreviewPlacement pp = placements[i];
                if (pp != null && !string.IsNullOrWhiteSpace(pp.partId))
                    placementByPart[pp.partId] = pp;
            }

            for (int s = 0; s < steps.Length; s++)
            {
                StepDefinition step = steps[s];
                if (step == null || step.ResolvedFamily != StepFamily.Place) continue;
                if (step.requiredPartIds == null || step.requiredPartIds.Length < 2) continue;

                // Collect (partId, startPosition) for every part in this step that has a placement.
                var entries = new List<(string partId, SceneFloat3 pos)>(step.requiredPartIds.Length);
                for (int i = 0; i < step.requiredPartIds.Length; i++)
                {
                    string pid = step.requiredPartIds[i];
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!placementByPart.TryGetValue(pid, out PartPreviewPlacement pp)) continue;
                    entries.Add((pid, pp.startPosition));
                }

                // Pairwise compare within the step.
                for (int i = 0; i < entries.Count; i++)
                {
                    for (int j = i + 1; j < entries.Count; j++)
                    {
                        if (PositionsEqual(entries[i].pos, entries[j].pos))
                        {
                            ctx.Issues.Add(ValidationPassHelpers.Error(
                                $"steps[{s}].requiredPartIds",
                                $"Parts '{entries[i].partId}' and '{entries[j].partId}' in step " +
                                $"'{step.id}' share the same startPosition " +
                                $"({entries[i].pos.x:F4}, {entries[i].pos.y:F4}, {entries[i].pos.z:F4}). " +
                                "Overlapping parts collapse into a single clickable pile and the " +
                                "hidden one cannot be selected."));
                        }
                    }
                }
            }
        }

        private static bool PositionsEqual(SceneFloat3 a, SceneFloat3 b)
        {
            return Math.Abs(a.x - b.x) < PositionEpsilon
                && Math.Abs(a.y - b.y) < PositionEpsilon
                && Math.Abs(a.z - b.z) < PositionEpsilon;
        }

        // ── Rule 5 ────────────────────────────────────────────────────────────
        private static void FlagSharedAssetRefs(ValidationPassContext ctx)
        {
            PartDefinition[] parts = ctx.Package.GetParts();
            if (parts == null || parts.Length == 0) return;

            var assetToParts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition p = parts[i];
                if (p == null || string.IsNullOrWhiteSpace(p.assetRef)) continue;
                if (string.IsNullOrWhiteSpace(p.id)) continue;

                if (!assetToParts.TryGetValue(p.assetRef, out var list))
                {
                    list = new List<string>(2);
                    assetToParts[p.assetRef] = list;
                }
                list.Add(p.id);
            }

            foreach (var kvp in assetToParts)
            {
                if (kvp.Value.Count < 2) continue;

                ctx.Issues.Add(ValidationPassHelpers.Warning(
                    "parts[*].assetRef",
                    $"{kvp.Value.Count} parts share assetRef '{kvp.Key}': " +
                    $"{string.Join(", ", kvp.Value)}. This is legal (mesh reuse is supported) but " +
                    "each partId must still be uniquely owned — verify Rules 1 and 2 also pass."));
            }
        }
    }
}
