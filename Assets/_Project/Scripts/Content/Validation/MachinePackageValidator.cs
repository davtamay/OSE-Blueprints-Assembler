using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    public static class MachinePackageValidator
    {
        private static readonly HashSet<string> DifficultyValues = CreateSet("beginner", "intermediate", "advanced");
        private static readonly HashSet<string> RecommendedModeValues = CreateSet("tutorial", "guided", "standard", "challenge");
        private static readonly HashSet<string> PartCategoryValues = CreateSet("plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom");
        private static readonly HashSet<string> ToolCategoryValues = CreateSet("hand_tool", "power_tool", "measurement", "safety", "specialty");
        private static readonly HashSet<string> CompletionModeValues = CreateSet("virtual_only", "physical_only", "virtual_or_physical", "confirmation_only", "multi_part_required");
        private static readonly HashSet<string> ValidationTypeValues = CreateSet("placement", "orientation", "part_identity", "dependency", "multi_part", "confirmation");
        private static readonly HashSet<string> HintTypeValues = CreateSet("text", "highlight", "ghost", "directional", "explanatory", "tool_reminder");
        private static readonly HashSet<string> HintPriorityValues = CreateSet("low", "medium", "high");
        private static readonly HashSet<string> EffectTypeValues = CreateSet("placement_feedback", "success_feedback", "error_feedback", "welding", "sparks", "heat_glow", "fire", "dust", "milestone");
        private static readonly HashSet<string> EffectTriggerValues = CreateSet("on_step_enter", "on_valid_candidate", "on_success", "on_failure", "on_completion");
        private static readonly HashSet<string> SourceTypeValues = CreateSet("blueprint", "photo", "diagram", "author_note", "reference_doc");

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
            ValidateSteps(package.GetSteps(), assemblyIds, subassemblyIds, partIds, toolIds, targetIds, validationRuleIds, hintIds, effectIds, issues);
            ValidateValidationRules(package.GetValidationRules(), partIds, stepIds, targetIds, hintIds, issues);
            ValidateHints(package.GetHints(), partIds, toolIds, targetIds, issues);
            ValidateEffects(package.GetEffects(), issues);
            ValidateTargets(package.GetTargets(), partIds, issues);

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
                ValidateRequiredText(part.assetRef, $"{path}.assetRef", issues);

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
            }
        }

        private static void ValidateSteps(
            StepDefinition[] steps,
            HashSet<string> assemblyIds,
            HashSet<string> subassemblyIds,
            HashSet<string> partIds,
            HashSet<string> toolIds,
            HashSet<string> targetIds,
            HashSet<string> validationRuleIds,
            HashSet<string> hintIds,
            HashSet<string> effectIds,
            List<MachinePackageValidationIssue> issues)
        {
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
                ValidateRequiredEnum(step.completionMode, CompletionModeValues, $"{path}.completionMode", issues);
                ValidateOptionalReferences(step.requiredPartIds, partIds, $"{path}.requiredPartIds", issues);
                ValidateOptionalReferences(step.optionalPartIds, partIds, $"{path}.optionalPartIds", issues);
                ValidateOptionalReferences(step.relevantToolIds, toolIds, $"{path}.relevantToolIds", issues);
                ValidateOptionalReferences(step.targetIds, targetIds, $"{path}.targetIds", issues);
                ValidateOptionalReferences(step.validationRuleIds, validationRuleIds, $"{path}.validationRuleIds", issues);
                ValidateOptionalReferences(step.hintIds, hintIds, $"{path}.hintIds", issues);
                ValidateOptionalReferences(step.effectTriggerIds, effectIds, $"{path}.effectTriggerIds", issues);

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
            }
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

        private static HashSet<string> CreateSet(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);

        private static MachinePackageValidationIssue Warning(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Warning, path, message);

        private static MachinePackageValidationIssue Error(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Error, path, message);
    }
}
