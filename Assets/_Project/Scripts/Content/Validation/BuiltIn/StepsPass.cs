using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="StepDefinition"/> entries including profile checks,
    /// tool actions, sequence indices, payload sub-objects, and subassembly constraints.
    /// </summary>
    internal sealed class StepsPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            StepDefinition[] steps = ctx.Package.GetSteps();
            TargetDefinition[] targets = ctx.Package.GetTargets();
            var issues = ctx.Issues;

            // Build target lookup for subassembly constraint checks
            var targetLookup = new Dictionary<string, TargetDefinition>(StringComparer.OrdinalIgnoreCase);
            if (targets != null)
                foreach (var t in targets)
                    if (t != null && !string.IsNullOrWhiteSpace(t.id))
                        targetLookup[t.id] = t;

            var sequenceUsageByAssembly = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < steps.Length; i++)
            {
                StepDefinition step = steps[i];
                string path = $"steps[{i}]";
                if (step == null) { issues.Add(ValidationPassHelpers.Error(path, "Step definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(step.name, $"{path}.name", issues);
                ValidationPassHelpers.ValidateSingleReference(step.assemblyId,   ctx.AssemblyIds,    $"{path}.assemblyId",   issues);
                ValidationPassHelpers.ValidateOptionalReference(step.subassemblyId, ctx.SubassemblyIds, $"{path}.subassemblyId", issues);
                // Validate resolved instruction text so steps using guidance payload pass the check.
                ValidationPassHelpers.ValidateRequiredText(step.ResolvedInstructionText, $"{path}.instructionText (resolved)", issues);

                // family is authoritative; completionType is the legacy fallback — require at least one.
                if (string.IsNullOrEmpty(step.family))
                {
#pragma warning disable CS0618
                    ValidationPassHelpers.ValidateRequiredEnum(step.completionType, ValidationPassHelpers.CompletionTypeValues, $"{path}.completionType", issues);
#pragma warning restore CS0618
                }
                else
                {
                    ValidationPassHelpers.ValidateOptionalEnum(step.family, ValidationPassHelpers.FamilyValues, $"{path}.family", issues);
                }

                ValidateStepProfile(step, path, issues);

                ValidationPassHelpers.ValidateOptionalEnum(step.viewMode,    ValidationPassHelpers.ViewModeValues,    $"{path}.viewMode",    issues);
                ValidationPassHelpers.ValidateOptionalEnum(step.targetOrder, ValidationPassHelpers.TargetOrderValues, $"{path}.targetOrder", issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.requiredPartIds,        ctx.PartIds,            $"{path}.requiredPartIds",        issues);
                ValidationPassHelpers.ValidateOptionalReference (step.requiredSubassemblyId,  ctx.SubassemblyIds,     $"{path}.requiredSubassemblyId",  issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.optionalPartIds,        ctx.PartIds,            $"{path}.optionalPartIds",        issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.relevantToolIds,        ctx.ToolIds,            $"{path}.relevantToolIds",        issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.targetIds,              ctx.TargetIds,          $"{path}.targetIds",              issues);
                // Validate resolved arrays so both payload and flat-field paths are checked.
                ValidationPassHelpers.ValidateOptionalReferences(step.ResolvedValidationRuleIds, ctx.ValidationRuleIds, $"{path}.validationRuleIds (resolved)", issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.ResolvedHintIds,           ctx.HintIds,           $"{path}.hintIds (resolved)",          issues);
                ValidationPassHelpers.ValidateOptionalReferences(step.ResolvedEffectTriggerIds,  ctx.EffectIds,         $"{path}.effectTriggerIds (resolved)",  issues);
                ValidateToolActions(step.requiredToolActions, ctx.ToolIds, ctx.TargetIds, $"{path}.requiredToolActions", issues);

                // Payload sub-object validation
                ValidateGuidancePayload(step.guidance,   ctx.HintIds,            $"{path}.guidance",   issues);
                ValidateValidationPayload(step.validation, ctx.ValidationRuleIds, $"{path}.validation", issues);
                ValidateFeedbackPayload(step.feedback,   ctx.EffectIds,          $"{path}.feedback",   issues);
                ValidateDifficultyPayload(step.difficulty, $"{path}.difficulty", issues);

                // Sequence index uniqueness per assembly
                if (step.sequenceIndex < 1)
                {
                    issues.Add(ValidationPassHelpers.Error($"{path}.sequenceIndex", "Step sequenceIndex must be at least 1."));
                }
                else
                {
                    string assemblyKey = string.IsNullOrWhiteSpace(step.assemblyId) ? "__missing__" : step.assemblyId;
                    if (!sequenceUsageByAssembly.TryGetValue(assemblyKey, out HashSet<int> seq))
                    {
                        seq = new HashSet<int>();
                        sequenceUsageByAssembly[assemblyKey] = seq;
                    }
                    if (!seq.Add(step.sequenceIndex))
                        issues.Add(ValidationPassHelpers.Warning($"{path}.sequenceIndex",
                            $"Sequence index '{step.sequenceIndex}' is reused inside assembly '{assemblyKey}'."));
                }

                ValidateToolActionCrossReferences(step, path, issues);

                if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId) &&
                    ValidationPassHelpers.HasAnyValues(step.requiredPartIds))
                {
                    issues.Add(ValidationPassHelpers.Error(path,
                        "A step may define either requiredPartIds or requiredSubassemblyId, not both."));
                }

                if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                {
                    if (step.ResolvedFamily != StepFamily.Place)
                    {
                        issues.Add(ValidationPassHelpers.Error($"{path}.requiredSubassemblyId",
                            "Subassembly placement is only supported on Place-family steps."));
                    }

                    if (step.targetIds == null || step.targetIds.Length != 1)
                    {
                        issues.Add(ValidationPassHelpers.Error($"{path}.targetIds",
                            "A subassembly placement step must reference exactly one target in v1."));
                    }
                    else if (targetLookup.TryGetValue(step.targetIds[0], out TargetDefinition target))
                    {
                        if (!string.Equals(target.associatedSubassemblyId, step.requiredSubassemblyId, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(ValidationPassHelpers.Error($"{path}.targetIds[0]",
                                $"Target '{target.id}' must reference associatedSubassemblyId '{step.requiredSubassemblyId}'."));
                        }
                    }
                }

                if (step.IsAxisFitPlacement && string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                {
                    issues.Add(ValidationPassHelpers.Error($"{path}.profile",
                        "AxisFit is only supported on Place-family subassembly placement steps."));
                }

                // Clamp and AxisFit steps place a persistent tool — tool must have persistent = true.
                if (step.IsPlacement &&
                    (step.ResolvedProfile == StepProfile.Clamp || step.ResolvedProfile == StepProfile.AxisFit) &&
                    ValidationPassHelpers.HasAnyValues(step.relevantToolIds))
                {
                    foreach (string tid in step.relevantToolIds)
                    {
                        if (!string.IsNullOrWhiteSpace(tid) &&
                            ctx.ToolDefsById.TryGetValue(tid, out ToolDefinition td) &&
                            !td.persistent)
                        {
                            issues.Add(ValidationPassHelpers.Warning($"{path}.relevantToolIds",
                                $"Tool '{tid}' is used in a {step.profile} step but ToolDefinition.persistent is false. " +
                                $"Set persistent = true in machine.json so PersistentToolController tracks it."));
                        }
                    }
                }
            }
        }

        // ── Step-level sub-validators ─────────────────────────────────────────

        private static void ValidateStepProfile(
            StepDefinition step, string path, List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(step.profile)) return;

            if (!ValidationPassHelpers.ProfileValues.Contains(step.profile))
            {
                issues.Add(ValidationPassHelpers.Error($"{path}.profile",
                    $"Profile '{step.profile}' is not a known profile. " +
                    $"Valid values: {string.Join(", ", ValidationPassHelpers.ProfileValues)}."));
                return;
            }

            var checker = MachinePackageValidator.IsProfileRegistered;
            if (checker != null && !checker(step.profile))
            {
                issues.Add(ValidationPassHelpers.Warning($"{path}.profile",
                    $"Profile '{step.profile}' is not registered in ToolProfileRegistry. " +
                    $"Register it or fix the typo in machine.json."));
            }
        }

        private static void ValidateGuidancePayload(
            StepGuidancePayload guidance, HashSet<string> hintIds, string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (guidance == null) return;
            ValidationPassHelpers.ValidateOptionalReferences(guidance.hintIds, hintIds, $"{path}.hintIds", issues);
        }

        private static void ValidateValidationPayload(
            StepValidationPayload validation, HashSet<string> validationRuleIds, string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (validation == null) return;
            ValidationPassHelpers.ValidateOptionalReferences(validation.validationRuleIds, validationRuleIds, $"{path}.validationRuleIds", issues);
        }

        private static void ValidateFeedbackPayload(
            StepFeedbackPayload feedback, HashSet<string> effectIds, string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (feedback == null) return;
            ValidationPassHelpers.ValidateOptionalReferences(feedback.effectTriggerIds, effectIds, $"{path}.effectTriggerIds", issues);
        }

        private static void ValidateDifficultyPayload(
            StepDifficultyPayload difficulty, string path, List<MachinePackageValidationIssue> issues)
        {
            if (difficulty == null) return;
            ValidationPassHelpers.ValidateOptionalEnum(difficulty.hintAvailability, ValidationPassHelpers.HintAvailabilityValues, $"{path}.hintAvailability", issues);
            if (difficulty.timeLimitSeconds < 0)
                issues.Add(ValidationPassHelpers.Error($"{path}.timeLimitSeconds", "Time limit cannot be negative."));
        }

        private static void ValidateToolActions(
            ToolActionDefinition[] toolActions, HashSet<string> toolIds, HashSet<string> targetIds,
            string path, List<MachinePackageValidationIssue> issues)
        {
            if (toolActions == null || toolActions.Length == 0) return;
            for (int i = 0; i < toolActions.Length; i++)
            {
                ToolActionDefinition a = toolActions[i];
                string actionPath = $"{path}[{i}]";
                if (a == null) { issues.Add(ValidationPassHelpers.Error(actionPath, "Tool action definition is null.")); continue; }

                ValidationPassHelpers.ValidateSingleReference(a.toolId, toolIds, $"{actionPath}.toolId", issues);
                ValidationPassHelpers.ValidateRequiredEnum(a.actionType, ValidationPassHelpers.ToolActionTypeValues, $"{actionPath}.actionType", issues);
                ValidationPassHelpers.ValidateOptionalReference(a.targetId, targetIds, $"{actionPath}.targetId", issues);

                if (a.requiredCount < 1)
                    issues.Add(ValidationPassHelpers.Error($"{actionPath}.requiredCount", "Tool action requiredCount must be at least 1."));
            }
        }

        private static void ValidateToolActionCrossReferences(
            StepDefinition step, string path, List<MachinePackageValidationIssue> issues)
        {
            if (step.requiredToolActions == null) return;

            var stepTargetIds = step.targetIds != null
                ? new HashSet<string>(step.targetIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var stepToolIds = step.relevantToolIds != null
                ? new HashSet<string>(step.relevantToolIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null) continue;

                if (!string.IsNullOrWhiteSpace(action.targetId) && !stepTargetIds.Contains(action.targetId))
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.requiredToolActions[{i}].targetId",
                        $"Tool action target '{action.targetId}' is not listed in step's targetIds. Preview/marker may not spawn."));
                }
                if (!string.IsNullOrWhiteSpace(action.toolId) && !stepToolIds.Contains(action.toolId))
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.requiredToolActions[{i}].toolId",
                        $"Tool action tool '{action.toolId}' is not listed in step's relevantToolIds. Tool may not be offered to user."));
                }
            }
        }
    }
}
