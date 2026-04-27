namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="TargetDefinition"/> entries — required fields,
    /// optional part/partGroup references, and mutual exclusion constraints.
    /// </summary>
    internal sealed class TargetsPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            TargetDefinition[] targets = ctx.Package.GetTargets();
            var issues = ctx.Issues;

            for (int i = 0; i < targets.Length; i++)
            {
                TargetDefinition t = targets[i];
                string path = $"targets[{i}]";
                if (t == null) { issues.Add(ValidationPassHelpers.Error(path, "Target definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(t.anchorRef,                                      $"{path}.anchorRef",               issues);
                ValidationPassHelpers.ValidateOptionalReference(t.associatedPartId,       ctx.PartIds,       $"{path}.associatedPartId",         issues);
                ValidationPassHelpers.ValidateOptionalReference(t.associatedPartGroupId, ctx.PartGroupIds, $"{path}.associatedPartGroupId", issues);

                if (!string.IsNullOrWhiteSpace(t.associatedPartId) &&
                    !string.IsNullOrWhiteSpace(t.associatedPartGroupId))
                {
                    issues.Add(ValidationPassHelpers.Error(path,
                        "A target may define either associatedPartId or associatedPartGroupId, not both."));
                }
            }
        }
    }
}
