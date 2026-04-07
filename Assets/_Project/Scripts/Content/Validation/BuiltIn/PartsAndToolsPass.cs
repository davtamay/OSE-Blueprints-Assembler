namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="PartDefinition"/> and <see cref="ToolDefinition"/> entries —
    /// required fields, enum values, and inter-item references.
    /// </summary>
    internal sealed class PartsAndToolsPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            ValidateParts(ctx);
            ValidateTools(ctx);
        }

        private static void ValidateParts(ValidationPassContext ctx)
        {
            PartDefinition[] parts = ctx.Package.GetParts();
            var issues = ctx.Issues;

            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition p = parts[i];
                string path = $"parts[{i}]";
                if (p == null) { issues.Add(ValidationPassHelpers.Error(path, "Part definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(p.name,     $"{path}.name",     issues);
                ValidationPassHelpers.ValidateRequiredEnum(p.category, ValidationPassHelpers.PartCategoryValues, $"{path}.category", issues);
                ValidationPassHelpers.ValidateRequiredText(p.material, $"{path}.material", issues);
                ValidationPassHelpers.ValidateRequiredText(p.function, $"{path}.function", issues);
                // assetRef is optional — resolver can discover parts by filename or GLB node search.

                if (p.quantity < 1)
                    issues.Add(ValidationPassHelpers.Error($"{path}.quantity", "Part quantity must be at least 1."));

                ValidationPassHelpers.ValidateOptionalReferences(p.toolIds, ctx.ToolIds, $"{path}.toolIds", issues);
            }
        }

        private static void ValidateTools(ValidationPassContext ctx)
        {
            ToolDefinition[] tools = ctx.Package.GetTools();
            var issues = ctx.Issues;

            for (int i = 0; i < tools.Length; i++)
            {
                ToolDefinition t = tools[i];
                string path = $"tools[{i}]";
                if (t == null) { issues.Add(ValidationPassHelpers.Error(path, "Tool definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(t.name,     $"{path}.name",     issues);
                ValidationPassHelpers.ValidateRequiredEnum(t.category, ValidationPassHelpers.ToolCategoryValues, $"{path}.category", issues);
                ValidationPassHelpers.ValidateRequiredText(t.purpose,  $"{path}.purpose",  issues);
                ValidationPassHelpers.ValidateRequiredText(t.assetRef, $"{path}.assetRef", issues);
            }
        }
    }
}
