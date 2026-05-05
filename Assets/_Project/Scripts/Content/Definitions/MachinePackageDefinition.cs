using System;
using System.Collections.Generic;

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
        public PartGroupDefinition[] partGroups;
        public PartTemplateDefinition[] partTemplates;
        public PartDefinition[] parts;
        public ToolDefinition[] tools;
        public StepDefinition[] steps;
        /// <summary>
        /// Prefab instances declared per-assembly. Each entry is expanded into
        /// virtual <see cref="StepDefinition"/>s by
        /// <see cref="OSE.Content.Loading.MachinePackageNormalizer.ExpandPrefabInstances"/>
        /// at load time and merged into <see cref="steps"/>. Edits to the
        /// source prefab YAML propagate on next load — no JSON duplication.
        /// </summary>
        public PrefabInstance[] prefabInstances;
        public ValidationRuleDefinition[] validationRules;
        public EffectDefinition[] effects;
        public HintDefinition[] hints;
        public TargetDefinition[] targets;
        public ChallengeConfigDefinition challengeConfig;
        public AssetManifestDefinition assetManifest;
        public PackagePreviewConfig previewConfig;

        // ── Lookup caches (non-serialized, built lazily after load) ─────────
        [NonSerialized] private StepDefinition[] _orderedSteps;
        [NonSerialized] private Dictionary<string, PartDefinition> _partsById;
        [NonSerialized] private Dictionary<string, StepDefinition> _stepsById;
        [NonSerialized] private Dictionary<string, ToolDefinition> _toolsById;
        [NonSerialized] private Dictionary<string, TargetDefinition> _targetsById;
        [NonSerialized] private Dictionary<string, HintDefinition> _hintsById;
        [NonSerialized] private Dictionary<string, EffectDefinition> _effectsById;
        [NonSerialized] private Dictionary<string, StepDefinition[]> _stepsByAssemblyId;
        [NonSerialized] private Dictionary<string, StepDefinition[]> _stepsByPartGroupId;
        [NonSerialized] private string _stepStructureHash;

        /// <summary>
        /// Pre-baked pose lookup table (partId × seqIndex → <see cref="Loading.PoseResolution"/>).
        /// Populated by <see cref="Loading.MachinePackageNormalizer.Normalize"/>.
        /// Editor and runtime both read from this; nobody re-runs pose
        /// resolution at render time. Never persisted.
        /// </summary>
        [NonSerialized] public OSE.Content.Loading.PoseTable poseTable;

        /// <summary>
        /// Pre-baked per-partGroup lifecycle (firstBuiltSeq, lastTouchedSeq,
        /// touchedSeqs[]). Populated by
        /// <see cref="Loading.MachinePackageNormalizer.BakePartGroupLifecycle"/>.
        /// Both TTAW and the runtime overlay query this via
        /// <see cref="Loading.PartGroupLifecycleResolver"/> instead of rolling
        /// their own per-step filter. Never persisted.
        /// </summary>
        [NonSerialized] public Dictionary<string, OSE.Content.Loading.PartGroupLifecycle> partGroupLifecycleByGroupId;

        public AssemblyDefinition[] GetAssemblies() => assemblies ?? Array.Empty<AssemblyDefinition>();

        public PartGroupDefinition[] GetPartGroups() => partGroups ?? Array.Empty<PartGroupDefinition>();

        public PartDefinition[] GetParts() => parts ?? Array.Empty<PartDefinition>();

        public ToolDefinition[] GetTools() => tools ?? Array.Empty<ToolDefinition>();

        public StepDefinition[] GetSteps() => steps ?? Array.Empty<StepDefinition>();

        public PrefabInstance[] GetPrefabInstances() => prefabInstances ?? Array.Empty<PrefabInstance>();

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

        /// <summary>
        /// Returns all steps belonging to the given assembly, sorted by sequenceIndex.
        /// Derived from each step's <see cref="StepDefinition.assemblyId"/> — the
        /// assembly's <c>stepIds</c> array in machine.json is no longer authoritative.
        /// </summary>
        public StepDefinition[] GetStepsForAssembly(string assemblyId)
        {
            if (string.IsNullOrWhiteSpace(assemblyId))
                return Array.Empty<StepDefinition>();

            if (_stepsByAssemblyId == null)
                BuildStepsByOwnerCaches();

            return _stepsByAssemblyId.TryGetValue(assemblyId, out var result)
                ? result
                : Array.Empty<StepDefinition>();
        }

        /// <summary>
        /// Returns all steps belonging to the given partGroup, sorted by sequenceIndex.
        /// Derived from each step's <see cref="StepDefinition.partGroupId"/>.
        /// </summary>
        public StepDefinition[] GetStepsForPartGroup(string partGroupId)
        {
            if (string.IsNullOrWhiteSpace(partGroupId))
                return Array.Empty<StepDefinition>();

            if (_stepsByPartGroupId == null)
                BuildStepsByOwnerCaches();

            return _stepsByPartGroupId.TryGetValue(partGroupId, out var result)
                ? result
                : Array.Empty<StepDefinition>();
        }

        /// <summary>
        /// A hash of the step structure (count + ordered IDs) used to detect
        /// when a saved session is stale after machine.json changes.
        /// </summary>
        public string StepStructureHash
        {
            get
            {
                if (_stepStructureHash != null)
                    return _stepStructureHash;

                StepDefinition[] ordered = GetOrderedSteps();
                // Use a simple hash: stepCount + concatenated IDs
                var sb = new System.Text.StringBuilder(ordered.Length * 32);
                sb.Append(ordered.Length);
                for (int i = 0; i < ordered.Length; i++)
                {
                    sb.Append('|');
                    sb.Append(ordered[i]?.id ?? string.Empty);
                }
                _stepStructureHash = sb.ToString().GetHashCode().ToString("X8");
                return _stepStructureHash;
            }
        }

        private void BuildStepsByOwnerCaches()
        {
            StepDefinition[] ordered = GetOrderedSteps();
            var byAsm = new Dictionary<string, List<StepDefinition>>(StringComparer.OrdinalIgnoreCase);
            var bySub = new Dictionary<string, List<StepDefinition>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < ordered.Length; i++)
            {
                StepDefinition step = ordered[i];
                if (step == null) continue;

                if (!string.IsNullOrWhiteSpace(step.assemblyId))
                {
                    if (!byAsm.TryGetValue(step.assemblyId, out var asmList))
                    {
                        asmList = new List<StepDefinition>();
                        byAsm[step.assemblyId] = asmList;
                    }
                    asmList.Add(step);
                }

                if (!string.IsNullOrWhiteSpace(step.partGroupId))
                {
                    if (!bySub.TryGetValue(step.partGroupId, out var subList))
                    {
                        subList = new List<StepDefinition>();
                        bySub[step.partGroupId] = subList;
                    }
                    subList.Add(step);
                }
            }

            _stepsByAssemblyId = new Dictionary<string, StepDefinition[]>(byAsm.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in byAsm)
                _stepsByAssemblyId[kvp.Key] = kvp.Value.ToArray();

            _stepsByPartGroupId = new Dictionary<string, StepDefinition[]>(bySub.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in bySub)
                _stepsByPartGroupId[kvp.Key] = kvp.Value.ToArray();
        }

        public string GetDisplayMachineName() =>
            machine == null ? "Unknown Machine" : machine.GetDisplayName();

        public bool TryGetAssembly(string assemblyId, out AssemblyDefinition assembly) =>
            TryFindById(GetAssemblies(), assemblyId, item => item.id, out assembly);

        public bool TryGetPartGroup(string partGroupId, out PartGroupDefinition partGroup) =>
            TryFindById(GetPartGroups(), partGroupId, item => item.id, out partGroup);

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

        public bool TryGetPartGroupPreviewPlacement(string partGroupId, out PartGroupPreviewPlacement placement)
        {
            PartGroupPreviewPlacement[] placements = previewConfig?.partGroupPlacements ?? Array.Empty<PartGroupPreviewPlacement>();
            return TryFindById(placements, partGroupId, item => item.partGroupId, out placement);
        }

        public bool TryGetCompletedPartGroupParkingPlacement(string partGroupId, out PartGroupPreviewPlacement placement)
        {
            PartGroupPreviewPlacement[] placements = previewConfig?.completedPartGroupParkingPlacements ?? Array.Empty<PartGroupPreviewPlacement>();
            return TryFindById(placements, partGroupId, item => item.partGroupId, out placement);
        }

        public bool TryGetConstrainedPartGroupFitPreviewPlacement(
            string partGroupId,
            string targetId,
            out ConstrainedPartGroupFitPreviewPlacement placement)
        {
            ConstrainedPartGroupFitPreviewPlacement[] placements = previewConfig?.constrainedPartGroupFitPlacements ?? Array.Empty<ConstrainedPartGroupFitPreviewPlacement>();
            if (!string.IsNullOrWhiteSpace(partGroupId) && !string.IsNullOrWhiteSpace(targetId))
            {
                for (int i = 0; i < placements.Length; i++)
                {
                    ConstrainedPartGroupFitPreviewPlacement candidate = placements[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.partGroupId, partGroupId, StringComparison.OrdinalIgnoreCase) &&
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

        public bool TryGetIntegratedPartGroupPreviewPlacement(
            string partGroupId,
            string targetId,
            out IntegratedPartGroupPreviewPlacement placement)
        {
            IntegratedPartGroupPreviewPlacement[] placements = previewConfig?.integratedPartGroupPlacements ?? Array.Empty<IntegratedPartGroupPreviewPlacement>();
            if (!string.IsNullOrWhiteSpace(partGroupId) && !string.IsNullOrWhiteSpace(targetId))
            {
                for (int i = 0; i < placements.Length; i++)
                {
                    IntegratedPartGroupPreviewPlacement candidate = placements[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.partGroupId, partGroupId, StringComparison.OrdinalIgnoreCase) &&
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
            ref Dictionary<string, T> cache,
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
                cache = new Dictionary<string, T>(
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
