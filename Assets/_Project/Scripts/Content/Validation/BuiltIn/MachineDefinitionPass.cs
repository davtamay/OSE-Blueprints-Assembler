using System;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates the top-level <see cref="MachineDefinition"/> fields and its
    /// cross-references to assembly ids, source references, and entry points.
    /// </summary>
    internal sealed class MachineDefinitionPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            if (ctx.Package.machine == null)
            {
                ctx.Issues.Add(ValidationPassHelpers.Error("machine", "Machine definition is required."));
                return;
            }

            ValidateMachine(ctx);
            ValidateMachineReferences(ctx);
        }

        private static void ValidateMachine(ValidationPassContext ctx)
        {
            var m = ctx.Package.machine;
            var issues = ctx.Issues;
            ValidationPassHelpers.ValidateRequiredText(m.id,          "machine.id",          issues);
            ValidationPassHelpers.ValidateRequiredText(m.name,        "machine.name",        issues);
            ValidationPassHelpers.ValidateRequiredText(m.description, "machine.description", issues);
            ValidationPassHelpers.ValidateRequiredEnum(m.difficulty,  ValidationPassHelpers.DifficultyValues,        "machine.difficulty",      issues);
            ValidationPassHelpers.ValidateOptionalEnum(m.recommendedMode, ValidationPassHelpers.RecommendedModeValues, "machine.recommendedMode", issues);

            if (m.learningObjectives == null || m.learningObjectives.Length == 0)
                issues.Add(ValidationPassHelpers.Warning("machine.learningObjectives", "At least one learning objective is recommended."));
        }

        private static void ValidateMachineReferences(ValidationPassContext ctx)
        {
            var m = ctx.Package.machine;
            var issues = ctx.Issues;
            ValidationPassHelpers.ValidateRequiredReferences(m.entryAssemblyIds, ctx.AssemblyIds, "machine.entryAssemblyIds", issues);

            SourceReferenceDefinition[] sourceRefs = m.sourceReferences ?? Array.Empty<SourceReferenceDefinition>();
            for (int i = 0; i < sourceRefs.Length; i++)
            {
                SourceReferenceDefinition sr = sourceRefs[i];
                string path = $"machine.sourceReferences[{i}]";
                if (sr == null) { issues.Add(ValidationPassHelpers.Error(path, "Source reference entry is null.")); continue; }
                ValidationPassHelpers.ValidateRequiredText(sr.title, $"{path}.title", issues);
                ValidationPassHelpers.ValidateRequiredEnum(sr.type,  ValidationPassHelpers.SourceTypeValues, $"{path}.type", issues);
            }
        }
    }
}
