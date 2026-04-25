using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Runtime-simulation pass — catches the class of bug where structural
    /// validation passes (every reference resolves) but the step still
    /// deadlocks at play time because the runtime's own filters / state
    /// machines reject the configuration.
    ///
    /// <para>This pass mirrors the runtime's load-time and step-activation
    /// logic to surface failures at validation time, so authors see the
    /// problem in the OSE menu / pre-Play dialog instead of as a silent
    /// "can't complete this step" at runtime.</para>
    ///
    /// <para>Each check here corresponds to a real bug we've shipped and
    /// then fixed. Adding a new check costs ~10 lines; the lesson it bakes
    /// in saves the next 25-chat debugging session. Add liberally.</para>
    /// </summary>
    internal sealed class StepRuntimeSimulationPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            CheckToolsSurviveRuntimeLoader(ctx);
            CheckUseStepsHaveActionableTaskOrder(ctx);
            CheckUseStepToolActionPartsArePlacedEarlier(ctx);
            CheckUseStepEquippedToolMatchesActions(ctx);
        }

        // ── Check 1 ──────────────────────────────────────────────────────────
        // Mirrors ToolRuntimeController.ResolveAvailableTools. If the runtime
        // loader would drop a tool, every step that references it deadlocks.
        // Bug history: 2026-04 step 58 hand-tighten — tool_hand had no assetRef
        // AND no "conceptual" category exemption in the loader, so EquipTool
        // failed silently with "Cannot equip unknown tool". Loader was fixed
        // to keep all tools regardless of assetRef, but the validator pass
        // here defends against any future loader filter creep.
        private static void CheckToolsSurviveRuntimeLoader(ValidationPassContext ctx)
        {
            var tools = ctx.Package.GetTools();
            if (tools == null) return;

            HashSet<string> droppedToolIds = null;
            for (int i = 0; i < tools.Length; i++)
            {
                var t = tools[i];
                if (t == null || string.IsNullOrWhiteSpace(t.id)) continue;

                // The runtime currently keeps every tool. If a future change
                // adds a filter (e.g. "drop tools whose GLB fails to load"),
                // mirror that filter here and add the dropped id to the set.
                bool wouldBeDropped = false;
                // Currently no drop conditions — placeholder for future filters.
                if (wouldBeDropped)
                {
                    droppedToolIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    droppedToolIds.Add(t.id.Trim());
                }
            }

            if (droppedToolIds == null || droppedToolIds.Count == 0) return;

            // Find every step that references a dropped tool.
            var steps = ctx.Package.GetSteps();
            if (steps == null) return;
            for (int si = 0; si < steps.Length; si++)
            {
                var step = steps[si];
                if (step == null) continue;

                if (step.relevantToolIds != null)
                {
                    for (int ti = 0; ti < step.relevantToolIds.Length; ti++)
                    {
                        string tid = step.relevantToolIds[ti];
                        if (string.IsNullOrEmpty(tid) || !droppedToolIds.Contains(tid)) continue;
                        ctx.Issues.Add(ValidationPassHelpers.Error(
                            $"steps[{si}].relevantToolIds[{ti}]",
                            $"Step '{step.id}' references tool '{tid}' which the runtime loader would drop. Step would deadlock at play time."));
                    }
                }

                if (step.requiredToolActions != null)
                {
                    for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                    {
                        var a = step.requiredToolActions[ai];
                        if (a == null || string.IsNullOrEmpty(a.toolId) || !droppedToolIds.Contains(a.toolId)) continue;
                        ctx.Issues.Add(ValidationPassHelpers.Error(
                            $"steps[{si}].requiredToolActions[{ai}].toolId",
                            $"Step '{step.id}' action '{a.id}' requires tool '{a.toolId}' which the runtime loader would drop. Step would deadlock."));
                    }
                }
            }
        }

        // ── Check 2 ──────────────────────────────────────────────────────────
        // Mirrors MachinePackageNormalizer.EnsureTaskOrderCoversRequirements.
        // The normalizer auto-fills missing taskOrder entries at load — but a
        // Warn at authoring time encourages source updates so the auto-fill
        // doesn't quietly mask drift. Bug history: 2026-04 step 83 had no
        // taskOrder; relied on normalizer's silent auto-fill, masked the
        // authoring gap until runtime click-doesn't-fire was reported.
        private static void CheckUseStepsHaveActionableTaskOrder(ValidationPassContext ctx)
        {
            var steps = ctx.Package.GetSteps();
            if (steps == null) return;

            for (int si = 0; si < steps.Length; si++)
            {
                var step = steps[si];
                if (step == null) continue;

                bool hasRequiredActions = step.requiredToolActions != null && step.requiredToolActions.Length > 0;
                bool hasRequiredParts = step.requiredPartIds != null && step.requiredPartIds.Length > 0;
                if (!hasRequiredActions && !hasRequiredParts) continue;

                // Build a (kind, id) set of the authored taskOrder entries.
                var coveredTuples = new HashSet<string>(StringComparer.Ordinal);
                if (step.taskOrder != null)
                {
                    for (int ti = 0; ti < step.taskOrder.Length; ti++)
                    {
                        var e = step.taskOrder[ti];
                        if (e == null || string.IsNullOrEmpty(e.kind) || string.IsNullOrEmpty(e.id)) continue;
                        coveredTuples.Add(e.kind + ":" + e.id);
                    }
                }

                int missingActions = 0;
                if (hasRequiredActions)
                {
                    for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                    {
                        var a = step.requiredToolActions[ai];
                        if (a == null || string.IsNullOrEmpty(a.id)) continue;
                        if (!coveredTuples.Contains("toolAction:" + a.id)) missingActions++;
                    }
                }

                int missingParts = 0;
                if (hasRequiredParts)
                {
                    for (int pi = 0; pi < step.requiredPartIds.Length; pi++)
                    {
                        string pid = step.requiredPartIds[pi];
                        if (string.IsNullOrEmpty(pid)) continue;
                        bool covered = false;
                        if (step.taskOrder != null)
                        {
                            for (int ti = 0; ti < step.taskOrder.Length && !covered; ti++)
                            {
                                var e = step.taskOrder[ti];
                                if (e == null || !string.Equals(e.kind, "part", StringComparison.Ordinal) || string.IsNullOrEmpty(e.id))
                                    continue;
                                if (string.Equals(TaskInstanceId.ToPartId(e.id), pid, StringComparison.Ordinal))
                                    covered = true;
                            }
                        }
                        if (!covered) missingParts++;
                    }
                }

                if (missingActions == 0 && missingParts == 0) continue;

                // Warning, not Error — the normalizer auto-fixes at load. But
                // surfacing it here pushes the author to update source.
                ctx.Issues.Add(ValidationPassHelpers.Warning(
                    $"steps[{si}].taskOrder",
                    $"Step '{step.id}' is missing {missingActions} toolAction and {missingParts} part entries in taskOrder. Normalizer auto-fills at load, but please update source so taskOrder explicitly reflects every requiredToolAction/requiredPart."));
            }
        }

        // ── Check 3 ──────────────────────────────────────────────────────────
        // Use-step targets reference parts via target.associatedPartId. If
        // that part hasn't been placed by an earlier Place step, the click
        // target floats over empty space — user clicks nothing.
        private static void CheckUseStepToolActionPartsArePlacedEarlier(ValidationPassContext ctx)
        {
            var steps = ctx.Package.GetSteps();
            if (steps == null) return;

            // Build (partId → earliest seq it's placed).
            var firstPlacedSeqByPart = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int si = 0; si < steps.Length; si++)
            {
                var step = steps[si];
                if (step == null) continue;
                if (step.requiredPartIds == null) continue;
                if (!string.Equals(step.family, "Place", StringComparison.OrdinalIgnoreCase)) continue;

                for (int pi = 0; pi < step.requiredPartIds.Length; pi++)
                {
                    string pid = step.requiredPartIds[pi];
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!firstPlacedSeqByPart.TryGetValue(pid, out int existing) || step.sequenceIndex < existing)
                        firstPlacedSeqByPart[pid] = step.sequenceIndex;
                }
            }

            // Build (targetId → associatedPartId) lookup.
            var assocByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targets = ctx.Package.GetTargets();
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var t = targets[i];
                    if (t == null || string.IsNullOrEmpty(t.id)) continue;
                    if (!string.IsNullOrEmpty(t.associatedPartId))
                        assocByTarget[t.id] = t.associatedPartId;
                }
            }

            for (int si = 0; si < steps.Length; si++)
            {
                var step = steps[si];
                if (step == null) continue;
                if (!string.Equals(step.family, "Use", StringComparison.OrdinalIgnoreCase)) continue;
                if (step.requiredToolActions == null) continue;

                for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                {
                    var a = step.requiredToolActions[ai];
                    if (a == null || string.IsNullOrEmpty(a.targetId)) continue;
                    if (!assocByTarget.TryGetValue(a.targetId, out string assocPartId)) continue;
                    if (!firstPlacedSeqByPart.TryGetValue(assocPartId, out int placedSeq))
                    {
                        ctx.Issues.Add(ValidationPassHelpers.Warning(
                            $"steps[{si}].requiredToolActions[{ai}].targetId",
                            $"Step '{step.id}' (seq {step.sequenceIndex}) tool action targets '{a.targetId}' whose associatedPartId '{assocPartId}' is never placed by any Place step. Click will hit empty space."));
                        continue;
                    }
                    if (placedSeq >= step.sequenceIndex)
                    {
                        ctx.Issues.Add(ValidationPassHelpers.Warning(
                            $"steps[{si}].requiredToolActions[{ai}].targetId",
                            $"Step '{step.id}' (seq {step.sequenceIndex}) tool action targets '{a.targetId}' whose associatedPartId '{assocPartId}' is first placed at seq {placedSeq} (this step or later). Part won't be in scene yet — click won't register."));
                    }
                }
            }
        }

        // ── Check 4 ──────────────────────────────────────────────────────────
        // Auto-equip resolves the step's tool from requiredToolActions[0].toolId
        // (or relevantToolIds[0] as fallback). If a step's actions split across
        // multiple tools, only the first one auto-equips — actions for the
        // other tools deadlock until the user manually equips.
        private static void CheckUseStepEquippedToolMatchesActions(ValidationPassContext ctx)
        {
            var steps = ctx.Package.GetSteps();
            if (steps == null) return;

            for (int si = 0; si < steps.Length; si++)
            {
                var step = steps[si];
                if (step == null) continue;
                if (!string.Equals(step.family, "Use", StringComparison.OrdinalIgnoreCase)) continue;
                if (step.requiredToolActions == null || step.requiredToolActions.Length == 0) continue;

                string firstToolId = null;
                var distinctTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                {
                    var a = step.requiredToolActions[ai];
                    if (a == null || string.IsNullOrEmpty(a.toolId)) continue;
                    string toolId = a.toolId.Trim();
                    if (firstToolId == null) firstToolId = toolId;
                    distinctTools.Add(toolId);
                }

                if (distinctTools.Count > 1)
                {
                    var toolList = string.Join(", ", distinctTools);
                    ctx.Issues.Add(ValidationPassHelpers.Warning(
                        $"steps[{si}].requiredToolActions",
                        $"Step '{step.id}' tool actions span multiple tools [{toolList}]. Auto-equip picks only the first ('{firstToolId}'); user must manually swap to others. Consider splitting into separate Use steps per tool."));
                }
            }
        }
    }
}
