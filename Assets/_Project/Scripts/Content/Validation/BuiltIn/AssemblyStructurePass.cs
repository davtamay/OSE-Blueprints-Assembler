namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="AssemblyDefinition"/> and <see cref="SubassemblyDefinition"/>
    /// entries — required fields, machineId consistency, and cross-references.
    /// </summary>
    internal sealed class AssemblyStructurePass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            ValidateAssemblies(ctx);
            ValidateSubassemblies(ctx);
        }

        private static void ValidateAssemblies(ValidationPassContext ctx)
        {
            AssemblyDefinition[] assemblies = ctx.Package.GetAssemblies();
            string machineId = ctx.Package.machine != null ? ctx.Package.machine.id : string.Empty;
            var issues = ctx.Issues;

            for (int i = 0; i < assemblies.Length; i++)
            {
                AssemblyDefinition a = assemblies[i];
                string path = $"assemblies[{i}]";
                if (a == null) { issues.Add(ValidationPassHelpers.Error(path, "Assembly definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(a.name,      $"{path}.name",      issues);
                ValidationPassHelpers.ValidateRequiredText(a.machineId, $"{path}.machineId", issues);

                if (!string.IsNullOrWhiteSpace(machineId) &&
                    !string.Equals(a.machineId, machineId, System.StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ValidationPassHelpers.Error($"{path}.machineId",
                        $"Assembly '{a.id}' references machine '{a.machineId}', expected '{machineId}'."));
                }

                ValidationPassHelpers.ValidateRequiredReferences(a.subassemblyIds,       ctx.SubassemblyIds, $"{path}.subassemblyIds",       issues);
                ValidationPassHelpers.ValidateRequiredReferences(a.stepIds,              ctx.StepIds,        $"{path}.stepIds",              issues);
                ValidationPassHelpers.ValidateOptionalReferences(a.dependencyAssemblyIds, ctx.AssemblyIds,   $"{path}.dependencyAssemblyIds", issues);
            }
        }

        private static void ValidateSubassemblies(ValidationPassContext ctx)
        {
            SubassemblyDefinition[] subassemblies = ctx.Package.GetSubassemblies();
            var issues = ctx.Issues;

            for (int i = 0; i < subassemblies.Length; i++)
            {
                SubassemblyDefinition s = subassemblies[i];
                string path = $"subassemblies[{i}]";
                if (s == null) { issues.Add(ValidationPassHelpers.Error(path, "Subassembly definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(s.name, $"{path}.name", issues);
                ValidationPassHelpers.ValidateSingleReference(s.assemblyId, ctx.AssemblyIds, $"{path}.assemblyId", issues);
                // partIds on a subassembly is now derived from each
                // PartDefinition.subassemblyIds claim at load time (see
                // MachinePackageNormalizer.DeriveSubassemblyPartIds). A group
                // with no parts claiming membership is a smell, not a
                // blocker — warn instead of erroring so loading proceeds.
                // Ids present must still resolve.
                if (s.partIds == null || s.partIds.Length == 0)
                    issues.Add(ValidationPassHelpers.Warning($"{path}.partIds", "No parts claim membership of this subassembly."));
                else
                    ValidationPassHelpers.ValidateOptionalReferences(s.partIds, ctx.PartIds, $"{path}.partIds", issues);
                ValidationPassHelpers.ValidateRequiredReferences(s.stepIds,  ctx.StepIds,    $"{path}.stepIds",    issues);
            }
        }
    }
}
