namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="ValidationRuleDefinition"/>, <see cref="HintDefinition"/>,
    /// and <see cref="EffectDefinition"/> entries.
    /// </summary>
    internal sealed class RulesHintsEffectsPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            ValidateValidationRules(ctx);
            ValidateHints(ctx);
            ValidateEffects(ctx);
        }

        private static void ValidateValidationRules(ValidationPassContext ctx)
        {
            ValidationRuleDefinition[] rules = ctx.Package.GetValidationRules();
            var issues = ctx.Issues;

            for (int i = 0; i < rules.Length; i++)
            {
                ValidationRuleDefinition r = rules[i];
                string path = $"validationRules[{i}]";
                if (r == null) { issues.Add(ValidationPassHelpers.Error(path, "Validation rule definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(r.type, ValidationPassHelpers.ValidationTypeValues, $"{path}.type", issues);
                ValidationPassHelpers.ValidateOptionalReference(r.targetId,       ctx.TargetIds, $"{path}.targetId",       issues);
                ValidationPassHelpers.ValidateOptionalReference(r.expectedPartId, ctx.PartIds,   $"{path}.expectedPartId", issues);
                ValidationPassHelpers.ValidateOptionalReferences(r.requiredStepIds, ctx.StepIds, $"{path}.requiredStepIds", issues);
                ValidationPassHelpers.ValidateOptionalReferences(r.requiredPartIds, ctx.PartIds, $"{path}.requiredPartIds", issues);
                ValidationPassHelpers.ValidateOptionalReference(r.correctionHintId, ctx.HintIds, $"{path}.correctionHintId", issues);
            }
        }

        private static void ValidateHints(ValidationPassContext ctx)
        {
            HintDefinition[] hints = ctx.Package.GetHints();
            var issues = ctx.Issues;

            for (int i = 0; i < hints.Length; i++)
            {
                HintDefinition h = hints[i];
                string path = $"hints[{i}]";
                if (h == null) { issues.Add(ValidationPassHelpers.Error(path, "Hint definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(h.type,     ValidationPassHelpers.HintTypeValues,     $"{path}.type",     issues);
                ValidationPassHelpers.ValidateRequiredText(h.message,                                            $"{path}.message",  issues);
                ValidationPassHelpers.ValidateOptionalEnum(h.priority,  ValidationPassHelpers.HintPriorityValues, $"{path}.priority", issues);
                ValidationPassHelpers.ValidateOptionalReference(h.targetId, ctx.TargetIds, $"{path}.targetId", issues);
                ValidationPassHelpers.ValidateOptionalReference(h.partId,   ctx.PartIds,   $"{path}.partId",   issues);
                ValidationPassHelpers.ValidateOptionalReference(h.toolId,   ctx.ToolIds,   $"{path}.toolId",   issues);
            }
        }

        private static void ValidateEffects(ValidationPassContext ctx)
        {
            EffectDefinition[] effects = ctx.Package.GetEffects();
            var issues = ctx.Issues;

            for (int i = 0; i < effects.Length; i++)
            {
                EffectDefinition e = effects[i];
                string path = $"effects[{i}]";
                if (e == null) { issues.Add(ValidationPassHelpers.Error(path, "Effect definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(e.type,          ValidationPassHelpers.EffectTypeValues,    $"{path}.type",          issues);
                ValidationPassHelpers.ValidateOptionalEnum(e.triggerPolicy, ValidationPassHelpers.EffectTriggerValues, $"{path}.triggerPolicy", issues);
            }
        }
    }
}
