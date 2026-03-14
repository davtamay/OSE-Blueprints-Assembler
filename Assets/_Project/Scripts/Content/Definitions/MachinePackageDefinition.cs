using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class MachinePackageDefinition
    {
        public string schemaVersion;
        public string packageVersion;
        public MachineDefinition machine;
        public AssemblyDefinition[] assemblies;
        public SubassemblyDefinition[] subassemblies;
        public PartDefinition[] parts;
        public ToolDefinition[] tools;
        public StepDefinition[] steps;
        public ValidationRuleDefinition[] validationRules;
        public EffectDefinition[] effects;
        public HintDefinition[] hints;
        public TargetDefinition[] targets;
        public ChallengeConfigDefinition challengeConfig;
        public AssetManifestDefinition assetManifest;

        public AssemblyDefinition[] GetAssemblies() => assemblies ?? Array.Empty<AssemblyDefinition>();

        public SubassemblyDefinition[] GetSubassemblies() => subassemblies ?? Array.Empty<SubassemblyDefinition>();

        public PartDefinition[] GetParts() => parts ?? Array.Empty<PartDefinition>();

        public ToolDefinition[] GetTools() => tools ?? Array.Empty<ToolDefinition>();

        public StepDefinition[] GetSteps() => steps ?? Array.Empty<StepDefinition>();

        public ValidationRuleDefinition[] GetValidationRules() => validationRules ?? Array.Empty<ValidationRuleDefinition>();

        public EffectDefinition[] GetEffects() => effects ?? Array.Empty<EffectDefinition>();

        public HintDefinition[] GetHints() => hints ?? Array.Empty<HintDefinition>();

        public TargetDefinition[] GetTargets() => targets ?? Array.Empty<TargetDefinition>();

        public StepDefinition[] GetOrderedSteps()
        {
            StepDefinition[] orderedSteps = GetSteps();
            Array.Sort(orderedSteps, CompareStepOrder);
            return orderedSteps;
        }

        public string GetDisplayMachineName() =>
            machine == null ? "Unknown Machine" : machine.GetDisplayName();

        public bool TryGetAssembly(string assemblyId, out AssemblyDefinition assembly) =>
            TryFindById(GetAssemblies(), assemblyId, item => item.id, out assembly);

        public bool TryGetSubassembly(string subassemblyId, out SubassemblyDefinition subassembly) =>
            TryFindById(GetSubassemblies(), subassemblyId, item => item.id, out subassembly);

        public bool TryGetPart(string partId, out PartDefinition part) =>
            TryFindById(GetParts(), partId, item => item.id, out part);

        public bool TryGetTool(string toolId, out ToolDefinition tool) =>
            TryFindById(GetTools(), toolId, item => item.id, out tool);

        public bool TryGetStep(string stepId, out StepDefinition step) =>
            TryFindById(GetSteps(), stepId, item => item.id, out step);

        public bool TryGetValidationRule(string validationRuleId, out ValidationRuleDefinition validationRule) =>
            TryFindById(GetValidationRules(), validationRuleId, item => item.id, out validationRule);

        public bool TryGetHint(string hintId, out HintDefinition hint) =>
            TryFindById(GetHints(), hintId, item => item.id, out hint);

        public bool TryGetEffect(string effectId, out EffectDefinition effect) =>
            TryFindById(GetEffects(), effectId, item => item.id, out effect);

        public bool TryGetTarget(string targetId, out TargetDefinition target) =>
            TryFindById(GetTargets(), targetId, item => item.id, out target);

        private static int CompareStepOrder(StepDefinition left, StepDefinition right)
        {
            int leftSequence = left != null ? left.sequenceIndex : int.MaxValue;
            int rightSequence = right != null ? right.sequenceIndex : int.MaxValue;
            int comparison = leftSequence.CompareTo(rightSequence);

            if (comparison != 0)
            {
                return comparison;
            }

            string leftId = left != null ? left.id ?? string.Empty : string.Empty;
            string rightId = right != null ? right.id ?? string.Empty : string.Empty;
            return string.Compare(leftId, rightId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindById<T>(
            T[] items,
            string id,
            Func<T, string> idSelector,
            out T match)
            where T : class
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                for (int i = 0; i < items.Length; i++)
                {
                    T item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    string candidateId = idSelector(item);
                    if (string.Equals(candidateId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        match = item;
                        return true;
                    }
                }
            }

            match = default;
            return false;
        }
    }
}
