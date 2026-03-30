using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.Content.Validation
{
    public static class MachinePackageValidator
    {
        private static readonly HashSet<string> DifficultyValues = CreateSet("beginner", "intermediate", "advanced");
        private static readonly HashSet<string> RecommendedModeValues = CreateSet("tutorial", "guided", "standard", "challenge");
        private static readonly HashSet<string> PartCategoryValues = CreateSet("plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom");
        private static readonly HashSet<string> ToolCategoryValues = CreateSet("hand_tool", "power_tool", "measurement", "safety", "specialty");
        private static readonly HashSet<string> CompletionTypeValues = CreateSet("placement", "tool_action", "confirmation", "pipe_connection");
        private static readonly HashSet<string> FamilyValues = CreateSet("Place", "Use", "Connect", "Confirm");
        private static readonly HashSet<string> PlaceProfileValues = CreateSet("Clamp", "AxisFit");
        private static readonly HashSet<string> UseProfileValues = CreateSet("Torque", "Weld", "Cut", "Measure");
        private static readonly HashSet<string> ConnectProfileValues = CreateSet("Cable");
        private static readonly HashSet<string> ViewModeValues = CreateSet("SourceAndTarget", "PairEndpoints", "WorkZone", "PathView", "Overview", "Inspect");
        private static readonly HashSet<string> TargetOrderValues = CreateSet("sequential", "parallel");
        private static readonly HashSet<string> ValidationTypeValues = CreateSet("placement", "orientation", "part_identity", "dependency", "multi_part", "confirmation");
        private static readonly HashSet<string> HintTypeValues = CreateSet("text", "highlight", "ghost", "directional", "explanatory", "tool_reminder");
        private static readonly HashSet<string> HintPriorityValues = CreateSet("low", "medium", "high");
        private static readonly HashSet<string> ToolActionTypeValues = CreateSet("measure", "tighten", "strike", "weld_pass", "grind_pass");
        private static readonly HashSet<string> EffectTypeValues = CreateSet("placement_feedback", "success_feedback", "error_feedback", "welding", "sparks", "heat_glow", "fire", "dust", "milestone");
        private static readonly HashSet<string> EffectTriggerValues = CreateSet("on_step_enter", "on_valid_candidate", "on_success", "on_failure", "on_completion");
        private static readonly HashSet<string> SourceTypeValues = CreateSet("blueprint", "photo", "diagram", "author_note", "reference_doc");
        private static readonly HashSet<string> HintAvailabilityValues = CreateSet("always", "limited", "none");

        public static MachinePackageValidationResult Validate(MachinePackageDefinition package)
        {
            List<MachinePackageValidationIssue> issues = new List<MachinePackageValidationIssue>();

            if (package == null)
            {
                issues.Add(Error("$", "Machine package is null."));
                return new MachinePackageValidationResult(issues.ToArray());
            }

            ValidateRequiredText(package.schemaVersion, "schemaVersion", issues);
            ValidateRequiredText(package.packageVersion, "packageVersion", issues);

            if (package.machine == null)
            {
                issues.Add(Error("machine", "Machine definition is required."));
            }
            else
            {
                ValidateMachine(package.machine, issues);
            }

            HashSet<string> assemblyIds = BuildIdSet(package.GetAssemblies(), "assemblies", item => item.id, issues);
            HashSet<string> subassemblyIds = BuildIdSet(package.GetSubassemblies(), "subassemblies", item => item.id, issues);
            HashSet<string> partIds = BuildIdSet(package.GetParts(), "parts", item => item.id, issues);
            HashSet<string> toolIds = BuildIdSet(package.GetTools(), "tools", item => item.id, issues);
            HashSet<string> stepIds = BuildIdSet(package.GetSteps(), "steps", item => item.id, issues);
            HashSet<string> validationRuleIds = BuildIdSet(package.GetValidationRules(), "validationRules", item => item.id, issues);
            HashSet<string> hintIds = BuildIdSet(package.GetHints(), "hints", item => item.id, issues);
            HashSet<string> effectIds = BuildIdSet(package.GetEffects(), "effects", item => item.id, issues);
            HashSet<string> targetIds = BuildIdSet(package.GetTargets(), "targets", item => item.id, issues);

            ValidateMachineReferences(package.machine, assemblyIds, issues);
            ValidateAssemblies(package.GetAssemblies(), package.machine, subassemblyIds, stepIds, assemblyIds, issues);
            ValidateSubassemblies(package.GetSubassemblies(), assemblyIds, partIds, stepIds, issues);
            ValidateParts(package.GetParts(), toolIds, issues);
            ValidateTools(package.GetTools(), issues);
            Dictionary<string, ToolDefinition> toolDefsById = BuildToolDefsLookup(package.GetTools());
            ValidateSteps(package.GetSteps(), assemblyIds, subassemblyIds, partIds, toolIds, targetIds, package.GetTargets(), validationRuleIds, hintIds, effectIds, toolDefsById, issues);
            ValidateValidationRules(package.GetValidationRules(), partIds, stepIds, targetIds, hintIds, issues);
            ValidateHints(package.GetHints(), partIds, toolIds, targetIds, issues);
            ValidateEffects(package.GetEffects(), issues);
            ValidateTargets(package.GetTargets(), partIds, subassemblyIds, issues);
            ValidatePreviewConfigCoverage(package, partIds, targetIds, subassemblyIds, issues);

            // Cross-reference: orphan detection
            DetectOrphanParts(package, stepIds, issues);
            DetectOrphanTargets(package, issues);
            DetectOrphanSteps(package, assemblyIds, issues);

            return new MachinePackageValidationResult(issues.ToArray());
        }

        private static void ValidateMachine(MachineDefinition machine, List<MachinePackageValidationIssue> issues)
        {
            ValidateRequiredText(machine.id, "machine.id", issues);
            ValidateRequiredText(machine.name, "machine.name", issues);
            ValidateRequiredText(machine.description, "machine.description", issues);
            ValidateRequiredEnum(machine.difficulty, DifficultyValues, "machine.difficulty", issues);
            ValidateOptionalEnum(machine.recommendedMode, RecommendedModeValues, "machine.recommendedMode", issues);

            if (machine.learningObjectives == null || machine.learningObjectives.Length == 0)
            {
                issues.Add(Warning("machine.learningObjectives", "At least one learning objective is recommended."));
            }
        }

        private static void ValidateMachineReferences(
            MachineDefinition machine,
            HashSet<string> assemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (machine == null)
            {
                return;
            }

            ValidateRequiredReferences(machine.entryAssemblyIds, assemblyIds, "machine.entryAssemblyIds", issues);

            SourceReferenceDefinition[] sourceReferences = machine.sourceReferences ?? Array.Empty<SourceReferenceDefinition>();
            for (int i = 0; i < sourceReferences.Length; i++)
            {
                SourceReferenceDefinition sourceReference = sourceReferences[i];
                string path = $"machine.sourceReferences[{i}]";

                if (sourceReference == null)
                {
                    issues.Add(Error(path, "Source reference entry is null."));
                    continue;
                }

                ValidateRequiredText(sourceReference.title, $"{path}.title", issues);
                ValidateRequiredEnum(sourceReference.type, SourceTypeValues, $"{path}.type", issues);
            }
        }

        private static void ValidateAssemblies(
            AssemblyDefinition[] assemblies,
            MachineDefinition machine,
            HashSet<string> subassemblyIds,
            HashSet<string> stepIds,
            HashSet<string> assemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            string machineId = machine != null ? machine.id : string.Empty;

            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyDefinition assembly = assemblies[i];
                string path = $"assemblies[{i}]";

                if (assembly == null)
                {
                    issues.Add(Error(path, "Assembly definition is null."));
                    continue;
                }

                ValidateRequiredText(assembly.name, $"{path}.name", issues);
                ValidateRequiredText(assembly.machineId, $"{path}.machineId", issues);

                if (!string.IsNullOrWhiteSpace(machineId) &&
                    !string.Equals(assembly.machineId, machineId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Error($"{path}.machineId", $"Assembly '{assembly.id}' references machine '{assembly.machineId}', expected '{machineId}'."));
                }

                ValidateRequiredReferences(assembly.subassemblyIds, subassemblyIds, $"{path}.subassemblyIds", issues);
                ValidateRequiredReferences(assembly.stepIds, stepIds, $"{path}.stepIds", issues);
                ValidateOptionalReferences(assembly.dependencyAssemblyIds, assemblyIds, $"{path}.dependencyAssemblyIds", issues);
            }
        }

        private static void ValidateSubassemblies(
            SubassemblyDefinition[] subassemblies,
            HashSet<string> assemblyIds,
            HashSet<string> partIds,
            HashSet<string> stepIds,
            List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < subassemblies.Length; i++)
            {
                SubassemblyDefinition subassembly = subassemblies[i];
                string path = $"subassemblies[{i}]";

                if (subassembly == null)
                {
                    issues.Add(Error(path, "Subassembly definition is null."));
                    continue;
                }

                ValidateRequiredText(subassembly.name, $"{path}.name", issues);
                ValidateSingleReference(subassembly.assemblyId, assemblyIds, $"{path}.assemblyId", issues);
                ValidateRequiredReferences(subassembly.partIds, partIds, $"{path}.partIds", issues);
                ValidateRequiredReferences(subassembly.stepIds, stepIds, $"{path}.stepIds", issues);
            }
        }

        private static void ValidateParts(
            PartDefinition[] parts,
            HashSet<string> toolIds,
            List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition part = parts[i];
                string path = $"parts[{i}]";

                if (part == null)
                {
                    issues.Add(Error(path, "Part definition is null."));
                    continue;
                }

                ValidateRequiredText(part.name, $"{path}.name", issues);
                ValidateRequiredEnum(part.category, PartCategoryValues, $"{path}.category", issues);
                ValidateRequiredText(part.material, $"{path}.material", issues);
                ValidateRequiredText(part.function, $"{path}.function", issues);
                // assetRef is optional — the resolver can discover parts by filename or GLB node search.

                if (part.quantity < 1)
                {
                    issues.Add(Error($"{path}.quantity", "Part quantity must be at least 1."));
                }

                ValidateOptionalReferences(part.toolIds, toolIds, $"{path}.toolIds", issues);
            }
        }

        private static void ValidateTools(ToolDefinition[] tools, List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < tools.Length; i++)
            {
                ToolDefinition tool = tools[i];
                string path = $"tools[{i}]";

                if (tool == null)
                {
                    issues.Add(Error(path, "Tool definition is null."));
                    continue;
                }

                ValidateRequiredText(tool.name, $"{path}.name", issues);
                ValidateRequiredEnum(tool.category, ToolCategoryValues, $"{path}.category", issues);
                ValidateRequiredText(tool.purpose, $"{path}.purpose", issues);
                ValidateRequiredText(tool.assetRef, $"{path}.assetRef", issues);
            }
        }

        private static Dictionary<string, ToolDefinition> BuildToolDefsLookup(ToolDefinition[] tools)
        {
            var dict = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
            if (tools == null) return dict;
            foreach (var t in tools)
                if (t != null && !string.IsNullOrWhiteSpace(t.id))
                    dict[t.id] = t;
            return dict;
        }

        private static void ValidateSteps(
            StepDefinition[] steps,
            HashSet<string> assemblyIds,
            HashSet<string> subassemblyIds,
            HashSet<string> partIds,
            HashSet<string> toolIds,
            HashSet<string> targetIds,
            TargetDefinition[] targets,
            HashSet<string> validationRuleIds,
            HashSet<string> hintIds,
            HashSet<string> effectIds,
            Dictionary<string, ToolDefinition> toolDefs,
            List<MachinePackageValidationIssue> issues)
        {
            Dictionary<string, TargetDefinition> targetLookup = new Dictionary<string, TargetDefinition>(StringComparer.OrdinalIgnoreCase);
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    TargetDefinition target = targets[i];
                    if (target != null && !string.IsNullOrWhiteSpace(target.id))
                        targetLookup[target.id] = target;
                }
            }

            Dictionary<string, HashSet<int>> sequenceUsageByAssembly = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < steps.Length; i++)
            {
                StepDefinition step = steps[i];
                string path = $"steps[{i}]";

                if (step == null)
                {
                    issues.Add(Error(path, "Step definition is null."));
                    continue;
                }

                ValidateRequiredText(step.name, $"{path}.name", issues);
                ValidateSingleReference(step.assemblyId, assemblyIds, $"{path}.assemblyId", issues);
                ValidateOptionalReference(step.subassemblyId, subassemblyIds, $"{path}.subassemblyId", issues);
                ValidateRequiredText(step.instructionText, $"{path}.instructionText", issues);
                ValidateRequiredEnum(step.completionType, CompletionTypeValues, $"{path}.completionType", issues);
                ValidateOptionalEnum(step.family, FamilyValues, $"{path}.family", issues);
                ValidateStepProfile(step, $"{path}", issues);
                ValidateOptionalEnum(step.viewMode, ViewModeValues, $"{path}.viewMode", issues);
                ValidateOptionalEnum(step.targetOrder, TargetOrderValues, $"{path}.targetOrder", issues);
                ValidateOptionalReferences(step.requiredPartIds, partIds, $"{path}.requiredPartIds", issues);
                ValidateOptionalReference(step.requiredSubassemblyId, subassemblyIds, $"{path}.requiredSubassemblyId", issues);
                ValidateOptionalReferences(step.optionalPartIds, partIds, $"{path}.optionalPartIds", issues);
                ValidateOptionalReferences(step.relevantToolIds, toolIds, $"{path}.relevantToolIds", issues);
                ValidateOptionalReferences(step.targetIds, targetIds, $"{path}.targetIds", issues);
                ValidateOptionalReferences(step.validationRuleIds, validationRuleIds, $"{path}.validationRuleIds", issues);
                ValidateOptionalReferences(step.hintIds, hintIds, $"{path}.hintIds", issues);
                ValidateOptionalReferences(step.effectTriggerIds, effectIds, $"{path}.effectTriggerIds", issues);
                ValidateToolActions(step.requiredToolActions, toolIds, targetIds, $"{path}.requiredToolActions", issues);

                // Payload sub-object validation
                ValidateGuidancePayload(step.guidance, hintIds, $"{path}.guidance", issues);
                ValidateValidationPayload(step.validation, validationRuleIds, $"{path}.validation", issues);
                ValidateFeedbackPayload(step.feedback, effectIds, $"{path}.feedback", issues);
                ValidateDifficultyPayload(step.difficulty, $"{path}.difficulty", issues);

                if (step.sequenceIndex < 1)
                {
                    issues.Add(Error($"{path}.sequenceIndex", "Step sequenceIndex must be at least 1."));
                }
                else
                {
                    string assemblyKey = string.IsNullOrWhiteSpace(step.assemblyId) ? "__missing__" : step.assemblyId;
                    if (!sequenceUsageByAssembly.TryGetValue(assemblyKey, out HashSet<int> sequenceUsage))
                    {
                        sequenceUsage = new HashSet<int>();
                        sequenceUsageByAssembly[assemblyKey] = sequenceUsage;
                    }

                    if (!sequenceUsage.Add(step.sequenceIndex))
                    {
                        issues.Add(Warning($"{path}.sequenceIndex", $"Sequence index '{step.sequenceIndex}' is reused inside assembly '{assemblyKey}'."));
                    }
                }

                // Cross-reference: tool action targets should be in step's targetIds
                ValidateToolActionCrossReferences(step, $"{path}", issues);

                if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId) &&
                    HasAnyValues(step.requiredPartIds))
                {
                    issues.Add(Error(path, "A step may define either requiredPartIds or requiredSubassemblyId, not both."));
                }

                if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                {
                    if (step.ResolvedFamily != StepFamily.Place)
                    {
                        issues.Add(Error($"{path}.requiredSubassemblyId",
                            "Subassembly placement is only supported on Place-family steps."));
                    }

                    if (step.targetIds == null || step.targetIds.Length != 1)
                    {
                        issues.Add(Error($"{path}.targetIds",
                            "A subassembly placement step must reference exactly one target in v1."));
                    }
                    else if (targetLookup.TryGetValue(step.targetIds[0], out TargetDefinition target))
                    {
                        if (!string.Equals(target.associatedSubassemblyId, step.requiredSubassemblyId, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(Error($"{path}.targetIds[0]",
                                $"Target '{target.id}' must reference associatedSubassemblyId '{step.requiredSubassemblyId}'.")); 
                        }
                    }
                }

                if (step.IsAxisFitPlacement && string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                {
                    issues.Add(Error($"{path}.profile",
                        "AxisFit is only supported on Place-family subassembly placement steps."));
                }

                // Clamp and AxisFit steps place a persistent tool on the workpiece — the tool
                // must have persistent = true or PersistentToolController will not track it.
                if (step.IsPlacement &&
                    (step.ResolvedProfile == StepProfile.Clamp || step.ResolvedProfile == StepProfile.AxisFit) &&
                    HasAnyValues(step.relevantToolIds))
                {
                    foreach (string tid in step.relevantToolIds)
                    {
                        if (!string.IsNullOrWhiteSpace(tid)
                            && toolDefs.TryGetValue(tid, out ToolDefinition td)
                            && !td.persistent)
                        {
                            issues.Add(Warning($"{path}.relevantToolIds",
                                $"Tool '{tid}' is used in a {step.profile} step but ToolDefinition.persistent is false. " +
                                $"Set persistent = true in machine.json so PersistentToolController tracks it."));
                        }
                    }
                }
            }
        }

        private static void ValidateToolActionCrossReferences(
            StepDefinition step,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (step.requiredToolActions == null)
                return;

            HashSet<string> stepTargetIds = step.targetIds != null
                ? new HashSet<string>(step.targetIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            HashSet<string> stepToolIds = step.relevantToolIds != null
                ? new HashSet<string>(step.relevantToolIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null) continue;

                if (!string.IsNullOrWhiteSpace(action.targetId) && !stepTargetIds.Contains(action.targetId))
                {
                    issues.Add(Warning($"{path}.requiredToolActions[{i}].targetId",
                        $"Tool action target '{action.targetId}' is not listed in step's targetIds. Preview/marker may not spawn."));
                }

                if (!string.IsNullOrWhiteSpace(action.toolId) && !stepToolIds.Contains(action.toolId))
                {
                    issues.Add(Warning($"{path}.requiredToolActions[{i}].toolId",
                        $"Tool action tool '{action.toolId}' is not listed in step's relevantToolIds. Tool may not be offered to user."));
                }
            }
        }

        private static void ValidateStepProfile(
            StepDefinition step,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(step.profile))
                return;

            StepFamily resolvedFamily = step.ResolvedFamily;
            HashSet<string> allowedProfiles;

            switch (resolvedFamily)
            {
                case StepFamily.Place:   allowedProfiles = PlaceProfileValues;   break;
                case StepFamily.Use:     allowedProfiles = UseProfileValues;     break;
                case StepFamily.Connect: allowedProfiles = ConnectProfileValues; break;
                case StepFamily.Confirm: allowedProfiles = null;                 break;
                default:                 allowedProfiles = null;                 break;
            }

            if (allowedProfiles == null)
            {
                issues.Add(Warning($"{path}.profile",
                    $"Profile '{step.profile}' is set but family '{resolvedFamily}' has no defined profiles."));
                return;
            }

            if (!allowedProfiles.Contains(step.profile))
            {
                issues.Add(Warning($"{path}.profile",
                    $"Profile '{step.profile}' is not a recognized profile for family '{resolvedFamily}'. " +
                    $"Accepted: {string.Join(", ", allowedProfiles)}."));
            }
        }

        private static void ValidateGuidancePayload(
            StepGuidancePayload guidance,
            HashSet<string> hintIds,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (guidance == null) return;
            ValidateOptionalReferences(guidance.hintIds, hintIds, $"{path}.hintIds", issues);
        }

        private static void ValidateValidationPayload(
            StepValidationPayload validation,
            HashSet<string> validationRuleIds,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (validation == null) return;
            ValidateOptionalReferences(validation.validationRuleIds, validationRuleIds, $"{path}.validationRuleIds", issues);
        }

        private static void ValidateFeedbackPayload(
            StepFeedbackPayload feedback,
            HashSet<string> effectIds,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (feedback == null) return;
            ValidateOptionalReferences(feedback.effectTriggerIds, effectIds, $"{path}.effectTriggerIds", issues);
        }

        private static void ValidateDifficultyPayload(
            StepDifficultyPayload difficulty,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (difficulty == null) return;
            ValidateOptionalEnum(difficulty.hintAvailability, HintAvailabilityValues, $"{path}.hintAvailability", issues);

            if (difficulty.timeLimitSeconds < 0)
                issues.Add(Error($"{path}.timeLimitSeconds", "Time limit cannot be negative."));
        }

        private static void ValidateValidationRules(
            ValidationRuleDefinition[] validationRules,
            HashSet<string> partIds,
            HashSet<string> stepIds,
            HashSet<string> targetIds,
            HashSet<string> hintIds,
            List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < validationRules.Length; i++)
            {
                ValidationRuleDefinition validationRule = validationRules[i];
                string path = $"validationRules[{i}]";

                if (validationRule == null)
                {
                    issues.Add(Error(path, "Validation rule definition is null."));
                    continue;
                }

                ValidateRequiredEnum(validationRule.type, ValidationTypeValues, $"{path}.type", issues);
                ValidateOptionalReference(validationRule.targetId, targetIds, $"{path}.targetId", issues);
                ValidateOptionalReference(validationRule.expectedPartId, partIds, $"{path}.expectedPartId", issues);
                ValidateOptionalReferences(validationRule.requiredStepIds, stepIds, $"{path}.requiredStepIds", issues);
                ValidateOptionalReferences(validationRule.requiredPartIds, partIds, $"{path}.requiredPartIds", issues);
                ValidateOptionalReference(validationRule.correctionHintId, hintIds, $"{path}.correctionHintId", issues);
            }
        }

        private static void ValidateToolActions(
            ToolActionDefinition[] toolActions,
            HashSet<string> toolIds,
            HashSet<string> targetIds,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (toolActions == null || toolActions.Length == 0)
                return;

            for (int i = 0; i < toolActions.Length; i++)
            {
                ToolActionDefinition toolAction = toolActions[i];
                string actionPath = $"{path}[{i}]";

                if (toolAction == null)
                {
                    issues.Add(Error(actionPath, "Tool action definition is null."));
                    continue;
                }

                ValidateSingleReference(toolAction.toolId, toolIds, $"{actionPath}.toolId", issues);
                ValidateRequiredEnum(toolAction.actionType, ToolActionTypeValues, $"{actionPath}.actionType", issues);
                ValidateOptionalReference(toolAction.targetId, targetIds, $"{actionPath}.targetId", issues);

                if (toolAction.requiredCount < 1)
                {
                    issues.Add(Error($"{actionPath}.requiredCount", "Tool action requiredCount must be at least 1."));
                }
            }
        }

        private static void ValidateHints(
            HintDefinition[] hints,
            HashSet<string> partIds,
            HashSet<string> toolIds,
            HashSet<string> targetIds,
            List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < hints.Length; i++)
            {
                HintDefinition hint = hints[i];
                string path = $"hints[{i}]";

                if (hint == null)
                {
                    issues.Add(Error(path, "Hint definition is null."));
                    continue;
                }

                ValidateRequiredEnum(hint.type, HintTypeValues, $"{path}.type", issues);
                ValidateRequiredText(hint.message, $"{path}.message", issues);
                ValidateOptionalEnum(hint.priority, HintPriorityValues, $"{path}.priority", issues);
                ValidateOptionalReference(hint.targetId, targetIds, $"{path}.targetId", issues);
                ValidateOptionalReference(hint.partId, partIds, $"{path}.partId", issues);
                ValidateOptionalReference(hint.toolId, toolIds, $"{path}.toolId", issues);
            }
        }

        private static void ValidateEffects(EffectDefinition[] effects, List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < effects.Length; i++)
            {
                EffectDefinition effect = effects[i];
                string path = $"effects[{i}]";

                if (effect == null)
                {
                    issues.Add(Error(path, "Effect definition is null."));
                    continue;
                }

                ValidateRequiredEnum(effect.type, EffectTypeValues, $"{path}.type", issues);
                ValidateOptionalEnum(effect.triggerPolicy, EffectTriggerValues, $"{path}.triggerPolicy", issues);
            }
        }

        private static void ValidateTargets(
            TargetDefinition[] targets,
            HashSet<string> partIds,
            HashSet<string> subassemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                TargetDefinition target = targets[i];
                string path = $"targets[{i}]";

                if (target == null)
                {
                    issues.Add(Error(path, "Target definition is null."));
                    continue;
                }

                ValidateRequiredText(target.anchorRef, $"{path}.anchorRef", issues);
                ValidateOptionalReference(target.associatedPartId, partIds, $"{path}.associatedPartId", issues);
                ValidateOptionalReference(target.associatedSubassemblyId, subassemblyIds, $"{path}.associatedSubassemblyId", issues);

                if (!string.IsNullOrWhiteSpace(target.associatedPartId) &&
                    !string.IsNullOrWhiteSpace(target.associatedSubassemblyId))
                {
                    issues.Add(Error(path, "A target may define either associatedPartId or associatedSubassemblyId, not both."));
                }
            }
        }

        private static void ValidatePreviewConfigCoverage(
            MachinePackageDefinition package,
            HashSet<string> partIds,
            HashSet<string> targetIds,
            HashSet<string> subassemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            PackagePreviewConfig previewConfig = package.previewConfig;
            if (previewConfig == null)
            {
                if (partIds.Count > 0)
                    issues.Add(Warning("previewConfig", "No previewConfig defined but package has parts. Parts will use fallback positioning."));
                return;
            }

            // Check part placement coverage
            HashSet<string> coveredParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previewConfig.partPlacements != null)
            {
                for (int i = 0; i < previewConfig.partPlacements.Length; i++)
                {
                    if (previewConfig.partPlacements[i] != null)
                        coveredParts.Add(previewConfig.partPlacements[i].partId);
                }
            }
            foreach (string partId in partIds)
            {
                if (!coveredParts.Contains(partId))
                    issues.Add(Warning("previewConfig.partPlacements", $"Part '{partId}' has no placement entry. It will use fallback positioning."));
            }

            // Check target placement coverage
            HashSet<string> coveredTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previewConfig.targetPlacements != null)
            {
                for (int i = 0; i < previewConfig.targetPlacements.Length; i++)
                {
                    if (previewConfig.targetPlacements[i] != null)
                        coveredTargets.Add(previewConfig.targetPlacements[i].targetId);
                }
            }
            foreach (string tId in targetIds)
            {
                if (!coveredTargets.Contains(tId))
                    issues.Add(Warning("previewConfig.targetPlacements", $"Target '{tId}' has no placement entry. Preview will use fallback positioning."));
            }

            HashSet<string> coveredSubassemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previewConfig.subassemblyPlacements != null)
            {
                for (int i = 0; i < previewConfig.subassemblyPlacements.Length; i++)
                {
                    SubassemblyPreviewPlacement placement = previewConfig.subassemblyPlacements[i];
                    if (placement == null || string.IsNullOrWhiteSpace(placement.subassemblyId))
                        continue;

                    coveredSubassemblies.Add(placement.subassemblyId);
                    if (!subassemblyIds.Contains(placement.subassemblyId))
                    {
                        issues.Add(Error($"previewConfig.subassemblyPlacements[{i}].subassemblyId",
                            $"Reference '{placement.subassemblyId}' does not resolve."));
                    }
                }
            }

            if (previewConfig.completedSubassemblyParkingPlacements != null)
            {
                for (int i = 0; i < previewConfig.completedSubassemblyParkingPlacements.Length; i++)
                {
                    SubassemblyPreviewPlacement placement = previewConfig.completedSubassemblyParkingPlacements[i];
                    if (placement == null || string.IsNullOrWhiteSpace(placement.subassemblyId))
                        continue;

                    if (!subassemblyIds.Contains(placement.subassemblyId))
                    {
                        issues.Add(Error($"previewConfig.completedSubassemblyParkingPlacements[{i}].subassemblyId",
                            $"Reference '{placement.subassemblyId}' does not resolve."));
                    }
                }
            }

            if (previewConfig.integratedSubassemblyPlacements != null)
            {
                for (int i = 0; i < previewConfig.integratedSubassemblyPlacements.Length; i++)
                {
                    IntegratedSubassemblyPreviewPlacement placement = previewConfig.integratedSubassemblyPlacements[i];
                    string path = $"previewConfig.integratedSubassemblyPlacements[{i}]";
                    if (placement == null)
                    {
                        issues.Add(Error(path, "Integrated subassembly placement entry is null."));
                        continue;
                    }

                    ValidateRequiredText(placement.subassemblyId, $"{path}.subassemblyId", issues);
                    ValidateRequiredText(placement.targetId, $"{path}.targetId", issues);

                    if (!string.IsNullOrWhiteSpace(placement.subassemblyId) && !subassemblyIds.Contains(placement.subassemblyId))
                    {
                        issues.Add(Error($"{path}.subassemblyId",
                            $"Reference '{placement.subassemblyId}' does not resolve."));
                    }

                    if (!string.IsNullOrWhiteSpace(placement.targetId) && !targetIds.Contains(placement.targetId))
                    {
                        issues.Add(Error($"{path}.targetId",
                            $"Reference '{placement.targetId}' does not resolve."));
                    }

                    if (placement.memberPlacements == null || placement.memberPlacements.Length == 0)
                    {
                        issues.Add(Warning($"{path}.memberPlacements",
                            "Integrated subassembly placement has no member placements."));
                        continue;
                    }

                    HashSet<string> subassemblyPartIds = null;
                    if (!string.IsNullOrWhiteSpace(placement.subassemblyId) &&
                        package.TryGetSubassembly(placement.subassemblyId, out SubassemblyDefinition subassembly) &&
                        subassembly != null)
                    {
                        subassemblyPartIds = new HashSet<string>(subassembly.partIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    }

                    // Warn when member placement count doesn't match subassembly part count
                    // (catches authoring omissions where some members were forgotten).
                    if (subassemblyPartIds != null && placement.memberPlacements.Length != subassemblyPartIds.Count)
                    {
                        issues.Add(Warning($"{path}.memberPlacements",
                            $"Integrated placement has {placement.memberPlacements.Length} member(s) but subassembly '{placement.subassemblyId}' defines {subassemblyPartIds.Count} part(s). Some members may be missing or extraneous."));
                    }

                    for (int j = 0; j < placement.memberPlacements.Length; j++)
                    {
                        IntegratedMemberPreviewPlacement member = placement.memberPlacements[j];
                        string memberPath = $"{path}.memberPlacements[{j}]";
                        if (member == null)
                        {
                            issues.Add(Error(memberPath, "Integrated member placement entry is null."));
                            continue;
                        }

                        ValidateRequiredText(member.partId, $"{memberPath}.partId", issues);
                        if (!string.IsNullOrWhiteSpace(member.partId) && !partIds.Contains(member.partId))
                        {
                            issues.Add(Error($"{memberPath}.partId",
                                $"Reference '{member.partId}' does not resolve."));
                        }
                        else if (subassemblyPartIds != null &&
                                 !string.IsNullOrWhiteSpace(member.partId) &&
                                 !subassemblyPartIds.Contains(member.partId))
                        {
                            issues.Add(Error($"{memberPath}.partId",
                                $"Part '{member.partId}' is not a member of subassembly '{placement.subassemblyId}'."));
                        }
                    }
                }
            }

            if (previewConfig.constrainedSubassemblyFitPlacements != null)
            {
                for (int i = 0; i < previewConfig.constrainedSubassemblyFitPlacements.Length; i++)
                {
                    ConstrainedSubassemblyFitPreviewPlacement placement = previewConfig.constrainedSubassemblyFitPlacements[i];
                    string path = $"previewConfig.constrainedSubassemblyFitPlacements[{i}]";
                    if (placement == null)
                    {
                        issues.Add(Error(path, "Constrained subassembly fit entry is null."));
                        continue;
                    }

                    ValidateRequiredText(placement.subassemblyId, $"{path}.subassemblyId", issues);
                    ValidateRequiredText(placement.targetId, $"{path}.targetId", issues);

                    if (!string.IsNullOrWhiteSpace(placement.subassemblyId) && !subassemblyIds.Contains(placement.subassemblyId))
                    {
                        issues.Add(Error($"{path}.subassemblyId",
                            $"Reference '{placement.subassemblyId}' does not resolve."));
                    }

                    if (!string.IsNullOrWhiteSpace(placement.targetId) && !targetIds.Contains(placement.targetId))
                    {
                        issues.Add(Error($"{path}.targetId",
                            $"Reference '{placement.targetId}' does not resolve."));
                    }

                    if (placement.drivenPartIds == null || placement.drivenPartIds.Length == 0)
                    {
                        issues.Add(Warning($"{path}.drivenPartIds",
                            "Constrained subassembly fit has no drivenPartIds. The fit will behave like a rigid placement."));
                    }

                    HashSet<string> subassemblyPartIds = null;
                    if (!string.IsNullOrWhiteSpace(placement.subassemblyId) &&
                        package.TryGetSubassembly(placement.subassemblyId, out SubassemblyDefinition subassembly) &&
                        subassembly != null)
                    {
                        subassemblyPartIds = new HashSet<string>(subassembly.partIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    }

                    string[] drivenPartIds = placement.drivenPartIds ?? Array.Empty<string>();
                    for (int j = 0; j < drivenPartIds.Length; j++)
                    {
                        string drivenPartId = drivenPartIds[j];
                        string drivenPath = $"{path}.drivenPartIds[{j}]";
                        ValidateRequiredText(drivenPartId, drivenPath, issues);

                        if (!string.IsNullOrWhiteSpace(drivenPartId) && !partIds.Contains(drivenPartId))
                        {
                            issues.Add(Error(drivenPath, $"Reference '{drivenPartId}' does not resolve."));
                        }
                        else if (subassemblyPartIds != null &&
                                 !string.IsNullOrWhiteSpace(drivenPartId) &&
                                 !subassemblyPartIds.Contains(drivenPartId))
                        {
                            issues.Add(Error(drivenPath,
                                $"Part '{drivenPartId}' is not a member of subassembly '{placement.subassemblyId}'."));
                        }
                    }
                }
            }

            StepDefinition[] steps = package.GetSteps();
            for (int i = 0; i < steps.Length; i++)
            {
                StepDefinition step = steps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                    continue;

                if (!coveredSubassemblies.Contains(step.requiredSubassemblyId))
                {
                    issues.Add(Warning("previewConfig.subassemblyPlacements",
                        $"Subassembly '{step.requiredSubassemblyId}' is used by a placement step but has no authored subassembly placement frame."));
                }

                if (step.IsAxisFitPlacement)
                {
                    string targetId = step.targetIds != null && step.targetIds.Length == 1
                        ? step.targetIds[0]
                        : null;

                    if (string.IsNullOrWhiteSpace(targetId) ||
                        package.previewConfig?.constrainedSubassemblyFitPlacements == null ||
                        !package.TryGetConstrainedSubassemblyFitPreviewPlacement(step.requiredSubassemblyId, targetId, out _))
                    {
                        issues.Add(Warning("previewConfig.constrainedSubassemblyFitPlacements",
                            $"AxisFit step '{step.id}' has no matching constrained fit preview payload for subassembly '{step.requiredSubassemblyId}' and target '{targetId ?? "<missing>"}'."));
                    }
                }
            }

            // Cross-check: targetPlacement positions must match the associated part's
            // playPosition. A mismatch means the placement preview and the actual placed
            // position will disagree — confusing for the user.
            ValidatePreviewPlayPositionConsistency(package, previewConfig, coveredParts, issues);
        }

        private const float PositionTolerance = 0.001f;

        private static void ValidatePreviewPlayPositionConsistency(
            MachinePackageDefinition package,
            PackagePreviewConfig previewConfig,
            HashSet<string> coveredPartIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.targetPlacements == null || previewConfig.partPlacements == null)
                return;

            var placementTargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StepDefinition[] steps = package.GetSteps();
            foreach (StepDefinition step in steps)
            {
                if (step == null || !string.Equals(step.completionType, "placement", StringComparison.OrdinalIgnoreCase))
                    continue;

                string[] stepTargetIds = step.targetIds ?? Array.Empty<string>();
                for (int i = 0; i < stepTargetIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(stepTargetIds[i]))
                        placementTargetIds.Add(stepTargetIds[i]);
                }
            }

            // Build partId -> placement lookup
            var partLookup = new Dictionary<string, PartPreviewPlacement>(StringComparer.OrdinalIgnoreCase);
            foreach (var pp in previewConfig.partPlacements)
            {
                if (pp != null && !string.IsNullOrEmpty(pp.partId))
                    partLookup[pp.partId] = pp;
            }

            // Build targetId -> associatedPartId lookup
            TargetDefinition[] targets = package.GetTargets();
            var targetPartLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targets)
            {
                if (t != null &&
                    !string.IsNullOrEmpty(t.id) &&
                    !string.IsNullOrEmpty(t.associatedPartId) &&
                    string.IsNullOrEmpty(t.associatedSubassemblyId))
                {
                    targetPartLookup[t.id] = t.associatedPartId;
                }
            }

            foreach (var tp in previewConfig.targetPlacements)
            {
                if (tp == null || string.IsNullOrEmpty(tp.targetId))
                    continue;
                if (!placementTargetIds.Contains(tp.targetId))
                    continue;
                if (!targetPartLookup.TryGetValue(tp.targetId, out string partId))
                    continue;
                if (!partLookup.TryGetValue(partId, out var pp))
                    continue;

                float dx = tp.position.x - pp.playPosition.x;
                float dy = tp.position.y - pp.playPosition.y;
                float dz = tp.position.z - pp.playPosition.z;
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq > PositionTolerance * PositionTolerance)
                {
                    float dist = (float)Math.Sqrt(distSq);
                    issues.Add(Warning(
                        $"previewConfig.targetPlacements[{tp.targetId}]",
                        $"Preview position ({tp.position.x:F3}, {tp.position.y:F3}, {tp.position.z:F3}) differs from " +
                        $"part '{partId}' playPosition ({pp.playPosition.x:F3}, {pp.playPosition.y:F3}, {pp.playPosition.z:F3}) " +
                        $"by {dist:F4}m. Preview will appear at the wrong location. " +
                        $"Update targetPlacement to match playPosition or the preview code will override it."));
                }
            }
        }

        private static HashSet<string> BuildIdSet<T>(
            T[] items,
            string collectionName,
            Func<T, string> idSelector,
            List<MachinePackageValidationIssue> issues)
            where T : class
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < items.Length; i++)
            {
                T item = items[i];
                string path = $"{collectionName}[{i}]";

                if (item == null)
                {
                    issues.Add(Error(path, "Collection entry is null."));
                    continue;
                }

                string id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(Error($"{path}.id", "A stable id is required."));
                    continue;
                }

                if (!ids.Add(id))
                {
                    issues.Add(Error($"{path}.id", $"Duplicate id '{id}' found in {collectionName}."));
                }
            }

            return ids;
        }

        private static void ValidateRequiredText(
            string value,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(Error(path, "A non-empty value is required."));
            }
        }

        private static void ValidateRequiredEnum(
            string value,
            HashSet<string> allowedValues,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(Error(path, "A non-empty enum value is required."));
                return;
            }

            if (!allowedValues.Contains(value))
            {
                issues.Add(Error(path, $"Value '{value}' is not allowed here."));
            }
        }

        private static void ValidateOptionalEnum(
            string value,
            HashSet<string> allowedValues,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(value) && !allowedValues.Contains(value))
            {
                issues.Add(Error(path, $"Value '{value}' is not allowed here."));
            }
        }

        private static void ValidateSingleReference(
            string id,
            HashSet<string> referenceSet,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            ValidateRequiredText(id, path, issues);

            if (!string.IsNullOrWhiteSpace(id) && !referenceSet.Contains(id))
            {
                issues.Add(Error(path, $"Reference '{id}' does not resolve."));
            }
        }

        private static void ValidateOptionalReference(
            string id,
            HashSet<string> referenceSet,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(id) && !referenceSet.Contains(id))
            {
                issues.Add(Error(path, $"Reference '{id}' does not resolve."));
            }
        }

        private static void ValidateRequiredReferences(
            string[] ids,
            HashSet<string> referenceSet,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (ids == null || ids.Length == 0)
            {
                issues.Add(Error(path, "At least one reference is required."));
                return;
            }

            ValidateOptionalReferences(ids, referenceSet, path, issues);
        }

        private static void ValidateOptionalReferences(
            string[] ids,
            HashSet<string> referenceSet,
            string path,
            List<MachinePackageValidationIssue> issues)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                string itemPath = $"{path}[{i}]";

                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(Error(itemPath, "Reference id cannot be empty."));
                    continue;
                }

                if (!referenceSet.Contains(id))
                {
                    issues.Add(Error(itemPath, $"Reference '{id}' does not resolve."));
                }
            }
        }

        private static bool HasAnyValues(string[] values)
        {
            if (values == null || values.Length == 0)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return true;
            }

            return false;
        }

        // ── Orphan Detection ──

        private static void DetectOrphanParts(
            MachinePackageDefinition package,
            HashSet<string> stepIds,
            List<MachinePackageValidationIssue> issues)
        {
            PartDefinition[] parts = package.GetParts();
            StepDefinition[] steps = package.GetSteps();
            if (parts.Length == 0) return;

            var referencedPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                string[] required = steps[i].requiredPartIds;
                if (required != null)
                    for (int j = 0; j < required.Length; j++)
                        referencedPartIds.Add(required[j]);

                string[] optional = steps[i].optionalPartIds;
                if (optional != null)
                    for (int j = 0; j < optional.Length; j++)
                        referencedPartIds.Add(optional[j]);
            }

            // Also count parts referenced by targets
            TargetDefinition[] targets = package.GetTargets();
            for (int i = 0; i < targets.Length; i++)
            {
                if (!string.IsNullOrEmpty(targets[i].associatedPartId))
                    referencedPartIds.Add(targets[i].associatedPartId);
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i].id) && !referencedPartIds.Contains(parts[i].id))
                {
                    issues.Add(Warning($"parts[{i}]",
                        $"Part '{parts[i].id}' is defined but never referenced by any step or target."));
                }
            }
        }

        private static void DetectOrphanTargets(
            MachinePackageDefinition package,
            List<MachinePackageValidationIssue> issues)
        {
            TargetDefinition[] targets = package.GetTargets();
            StepDefinition[] steps = package.GetSteps();
            if (targets.Length == 0) return;

            var referencedTargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                string[] tIds = steps[i].targetIds;
                if (tIds != null)
                    for (int j = 0; j < tIds.Length; j++)
                        referencedTargetIds.Add(tIds[j]);
            }

            for (int i = 0; i < targets.Length; i++)
            {
                if (!string.IsNullOrEmpty(targets[i].id) && !referencedTargetIds.Contains(targets[i].id))
                {
                    issues.Add(Warning($"targets[{i}]",
                        $"Target '{targets[i].id}' is defined but never referenced by any step."));
                }
            }
        }

        private static void DetectOrphanSteps(
            MachinePackageDefinition package,
            HashSet<string> assemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            StepDefinition[] steps = package.GetSteps();
            AssemblyDefinition[] assemblies = package.GetAssemblies();
            SubassemblyDefinition[] subassemblies = package.GetSubassemblies();
            if (steps.Length == 0) return;

            var referencedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assemblies.Length; i++)
            {
                string[] sIds = assemblies[i].stepIds;
                if (sIds != null)
                    for (int j = 0; j < sIds.Length; j++)
                        referencedStepIds.Add(sIds[j]);
            }
            for (int i = 0; i < subassemblies.Length; i++)
            {
                string[] sIds = subassemblies[i].stepIds;
                if (sIds != null)
                    for (int j = 0; j < sIds.Length; j++)
                        referencedStepIds.Add(sIds[j]);
            }

            for (int i = 0; i < steps.Length; i++)
            {
                if (!string.IsNullOrEmpty(steps[i].id) && !referencedStepIds.Contains(steps[i].id))
                {
                    issues.Add(Warning($"steps[{i}]",
                        $"Step '{steps[i].id}' is defined but not listed in any assembly or subassembly."));
                }
            }
        }

        private static HashSet<string> CreateSet(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        private static MachinePackageValidationIssue Warning(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Warning, path, message);

        private static MachinePackageValidationIssue Error(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Error, path, message);
    }
}
