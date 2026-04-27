namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="AssemblyDefinition"/> and <see cref="PartGroupDefinition"/>
    /// entries — required fields, machineId consistency, and cross-references.
    /// </summary>
    internal sealed class AssemblyStructurePass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            ValidateAssemblies(ctx);
            ValidatePartGroups(ctx);
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

                ValidationPassHelpers.ValidateRequiredReferences(a.partGroupIds,       ctx.PartGroupIds, $"{path}.partGroupIds",       issues);
                ValidationPassHelpers.ValidateRequiredReferences(a.stepIds,              ctx.StepIds,        $"{path}.stepIds",              issues);
                ValidationPassHelpers.ValidateOptionalReferences(a.dependencyAssemblyIds, ctx.AssemblyIds,   $"{path}.dependencyAssemblyIds", issues);
            }
        }

        private static void ValidatePartGroups(ValidationPassContext ctx)
        {
            PartGroupDefinition[] partGroups = ctx.Package.GetPartGroups();
            var issues = ctx.Issues;

            for (int i = 0; i < partGroups.Length; i++)
            {
                PartGroupDefinition s = partGroups[i];
                string path = $"partGroups[{i}]";
                if (s == null) { issues.Add(ValidationPassHelpers.Error(path, "PartGroup definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(s.name, $"{path}.name", issues);
                ValidationPassHelpers.ValidateSingleReference(s.assemblyId, ctx.AssemblyIds, $"{path}.assemblyId", issues);
                // partIds on a partGroup is now derived from each
                // PartDefinition.partGroupIds claim at load time (see
                // MachinePackageNormalizer.DerivePartGroupPartIds). A group
                // with no parts claiming membership is a smell, not a
                // blocker — warn instead of erroring so loading proceeds.
                // Ids present must still resolve.
                if (s.partIds == null || s.partIds.Length == 0)
                    issues.Add(ValidationPassHelpers.Warning($"{path}.partIds", "No parts claim membership of this partGroup."));
                else
                    ValidationPassHelpers.ValidateOptionalReferences(s.partIds, ctx.PartIds, $"{path}.partIds", issues);
                ValidationPassHelpers.ValidateRequiredReferences(s.stepIds,  ctx.StepIds,    $"{path}.stepIds",    issues);
            }
        }
    }
}
