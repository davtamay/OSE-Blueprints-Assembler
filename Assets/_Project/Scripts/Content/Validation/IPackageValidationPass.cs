using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// A single, focused validation step against a <see cref="MachinePackageDefinition"/>.
    /// Implement this interface to add new validation logic without modifying
    /// <see cref="MachinePackageValidator"/> (Open/Closed Principle).
    /// </summary>
    public interface IPackageValidationPass
    {
        void Execute(ValidationPassContext ctx);
    }

    /// <summary>
    /// Shared state passed to every <see cref="IPackageValidationPass"/> during a
    /// single <see cref="MachinePackageValidator.Validate"/> run.
    /// All id sets are pre-built once by the validator entry point.
    /// </summary>
    public sealed class ValidationPassContext
    {
        public MachinePackageDefinition Package { get; }
        public List<MachinePackageValidationIssue> Issues { get; }

        public HashSet<string> AssemblyIds     { get; }
        public HashSet<string> SubassemblyIds  { get; }
        public HashSet<string> PartIds         { get; }
        public HashSet<string> ToolIds         { get; }
        public HashSet<string> StepIds         { get; }
        public HashSet<string> ValidationRuleIds { get; }
        public HashSet<string> HintIds         { get; }
        public HashSet<string> EffectIds       { get; }
        public HashSet<string> TargetIds       { get; }
        public Dictionary<string, ToolDefinition> ToolDefsById { get; }

        public ValidationPassContext(
            MachinePackageDefinition package,
            List<MachinePackageValidationIssue> issues,
            HashSet<string> assemblyIds,
            HashSet<string> subassemblyIds,
            HashSet<string> partIds,
            HashSet<string> toolIds,
            HashSet<string> stepIds,
            HashSet<string> validationRuleIds,
            HashSet<string> hintIds,
            HashSet<string> effectIds,
            HashSet<string> targetIds,
            Dictionary<string, ToolDefinition> toolDefsById)
        {
            Package          = package;
            Issues           = issues;
            AssemblyIds      = assemblyIds;
            SubassemblyIds   = subassemblyIds;
            PartIds          = partIds;
            ToolIds          = toolIds;
            StepIds          = stepIds;
            ValidationRuleIds = validationRuleIds;
            HintIds          = hintIds;
            EffectIds        = effectIds;
            TargetIds        = targetIds;
            ToolDefsById     = toolDefsById;
        }
    }
}
