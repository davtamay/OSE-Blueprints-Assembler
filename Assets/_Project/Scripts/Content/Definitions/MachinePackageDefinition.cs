using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class MachinePackageDefinition
    {
        /// <summary>
        /// Set by MachinePackageLoader after JSON deserialization.
        /// Matches the folder name under Assets/_Project/Data/Packages/ and StreamingAssets/MachinePackages/.
        /// Not persisted in machine.json — JsonUtility skips [NonSerialized] fields.
        /// </summary>
        [NonSerialized] public string packageId;
        public string schemaVersion;
        public string packageVersion;
        public MachineDefinition machine;
        public AssemblyDefinition[] assemblies;
        public SubassemblyDefinition[] subassemblies;
        public PartTemplateDefinition[] partTemplates;
        public PartDefinition[] parts;
        public ToolDefinition[] tools;
        public StepDefinition[] steps;
        public ValidationRuleDefinition[] validationRules;
        public EffectDefinition[] effects;
        public HintDefinition[] hints;
        public TargetDefinition[] targets;
        public ChallengeConfigDefinition challengeConfig;
        public AssetManifestDefinition assetManifest;
        public PackagePreviewConfig previewConfig;

        // ── Lookup caches (non-serialized, built lazily after load) ─────────
        [NonSerialized] private StepDefinition[] _orderedSteps;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, PartDefinition> _partsById;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, StepDefinition> _stepsById;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, ToolDefinition> _toolsById;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, TargetDefinition> _targetsById;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, HintDefinition> _hintsById;
        [NonSerialized] private System.Collections.Generic.Dictionary<string, EffectDefinition> _effectsById;

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
            if (_orderedSteps != null)
                return _orderedSteps;

            StepDefinition[] source = GetSteps();
            if (source.Length == 0)
                return source;

            var sorted = new StepDefinition[source.Length];
            Array.Copy(source, sorted, source.Length);
            Array.Sort(sorted, CompareStepOrder);
            _orderedSteps = sorted;
            return _orderedSteps;
        }

        public string GetDisplayMachineName() =>
            machine == null ? "Unknown Machine" : machine.GetDisplayName();

        public bool TryGetAssembly(string assemblyId, out AssemblyDefinition assembly) =>
            TryFindById(GetAssemblies(), assemblyId, item => item.id, out assembly);

        public bool TryGetSubassembly(string subassemblyId, out SubassemblyDefinition subassembly) =>
            TryFindById(GetSubassemblies(), subassemblyId, item => item.id, out subassembly);

        public bool TryGetPart(string partId, out PartDefinition part) =>
            TryGetByIdFast(ref _partsById, GetParts(), p => p.id, partId, out part);

        public bool TryGetTool(string toolId, out ToolDefinition tool) =>
            TryGetByIdFast(ref _toolsById, GetTools(), t => t.id, toolId, out tool);

        public bool TryGetStep(string stepId, out StepDefinition step) =>
            TryGetByIdFast(ref _stepsById, GetSteps(), s => s.id, stepId, out step);

        public bool TryGetValidationRule(string validationRuleId, out ValidationRuleDefinition validationRule) =>
            TryFindById(GetValidationRules(), validationRuleId, item => item.id, out validationRule);

        public bool TryGetHint(string hintId, out HintDefinition hint) =>
            TryGetByIdFast(ref _hintsById, GetHints(), h => h.id, hintId, out hint);

        public bool TryGetEffect(string effectId, out EffectDefinition effect) =>
            TryGetByIdFast(ref _effectsById, GetEffects(), e => e.id, effectId, out effect);

        public bool TryGetTarget(string targetId, out TargetDefinition target) =>
            TryGetByIdFast(ref _targetsById, GetTargets(), t => t.id, targetId, out target);

        public bool TryGetSubassemblyPreviewPlacement(string subassemblyId, out SubassemblyPreviewPlacement placement)
        {
            SubassemblyPreviewPlacement[] placements = previewConfig?.subassemblyPlacements ?? Array.Empty<SubassemblyPreviewPlacement>();
            return TryFindById(placements, subassemblyId, item => item.subassemblyId, out placement);
        }

        public bool TryGetCompletedSubassemblyParkingPlacement(string subassemblyId, out SubassemblyPreviewPlacement placement)
        {
            SubassemblyPreviewPlacement[] placements = previewConfig?.completedSubassemblyParkingPlacements ?? Array.Empty<SubassemblyPreviewPlacement>();
            return TryFindById(placements, subassemblyId, item => item.subassemblyId, out placement);
        }

        public bool TryGetConstrainedSubassemblyFitPreviewPlacement(
            string subassemblyId,
            string targetId,
            out ConstrainedSubassemblyFitPreviewPlacement placement)
        {
            ConstrainedSubassemblyFitPreviewPlacement[] placements = previewConfig?.constrainedSubassemblyFitPlacements ?? Array.Empty<ConstrainedSubassemblyFitPreviewPlacement>();
            if (!string.IsNullOrWhiteSpace(subassemblyId) && !string.IsNullOrWhiteSpace(targetId))
            {
                for (int i = 0; i < placements.Length; i++)
                {
                    ConstrainedSubassemblyFitPreviewPlacement candidate = placements[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.subassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        placement = candidate;
                        return true;
                    }
                }
            }

            placement = null;
            return false;
        }

        public bool TryGetIntegratedSubassemblyPreviewPlacement(
            string subassemblyId,
            string targetId,
            out IntegratedSubassemblyPreviewPlacement placement)
        {
            IntegratedSubassemblyPreviewPlacement[] placements = previewConfig?.integratedSubassemblyPlacements ?? Array.Empty<IntegratedSubassemblyPreviewPlacement>();
            if (!string.IsNullOrWhiteSpace(subassemblyId) && !string.IsNullOrWhiteSpace(targetId))
            {
                for (int i = 0; i < placements.Length; i++)
                {
                    IntegratedSubassemblyPreviewPlacement candidate = placements[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.subassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        placement = candidate;
                        return true;
                    }
                }
            }

            placement = null;
            return false;
        }

        private static bool TryGetByIdFast<T>(
            ref System.Collections.Generic.Dictionary<string, T> cache,
            T[] source,
            Func<T, string> keySelector,
            string id,
            out T match)
            where T : class
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                match = default;
                return false;
            }

            if (cache == null)
            {
                cache = new System.Collections.Generic.Dictionary<string, T>(
                    source.Length, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < source.Length; i++)
                {
                    T item = source[i];
                    if (item == null) continue;
                    string key = keySelector(item);
                    if (!string.IsNullOrWhiteSpace(key))
                        cache[key] = item;
                }
            }

            return cache.TryGetValue(id, out match);
        }

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
