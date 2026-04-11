using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Entry point for package validation. Builds the shared id sets, creates
    /// a <see cref="ValidationPassContext"/>, runs all built-in passes in order,
    /// then runs any external passes registered via <see cref="RegisterPass"/>.
    ///
    /// To add new validation logic: implement <see cref="IPackageValidationPass"/> and
    /// call <see cref="RegisterPass"/> at startup — do not modify this class.
    /// </summary>
    public static class MachinePackageValidator
    {
        /// <summary>
        /// Optional delegate that checks whether a profile string is registered.
        /// Set by higher-level code (e.g. ToolProfileRegistry.Has) at startup.
        /// When null, profile validation is skipped (all profiles accepted).
        /// </summary>
        public static Func<string, bool> IsProfileRegistered { get; set; }

        /// <summary>
        /// Additional passes registered at startup by external systems.
        /// Executed after all built-in passes. Extend without modifying this class.
        /// </summary>
        private static readonly List<IPackageValidationPass> _externalPasses = new List<IPackageValidationPass>();

        /// <summary>
        /// The ordered sequence of built-in validation passes.
        /// Order matters — later passes may rely on issues surfaced by earlier ones.
        /// </summary>
        private static readonly IPackageValidationPass[] _builtInPasses =
        {
            new MachineDefinitionPass(),
            new AssemblyStructurePass(),
            new PartsAndToolsPass(),
            new StepsPass(),
            new RulesHintsEffectsPass(),
            new TargetsPass(),
            new PreviewConfigPass(),
            new OrphanDetectionPass(),
            new PartOwnershipExclusivityPass(),
            new SpatialContractPass(),       // Layer 3: staging collision, ordering, Use tool coverage
        };

        /// <summary>
        /// Register a custom validation pass to run at the end of every
        /// <see cref="Validate"/> call. Safe to call at startup before any validation.
        /// </summary>
        public static void RegisterPass(IPackageValidationPass pass)
        {
            if (pass != null && !_externalPasses.Contains(pass))
                _externalPasses.Add(pass);
        }

        /// <summary>
        /// Removes all externally registered passes.
        /// <para><b>Test-only.</b> Use in <c>[TearDown]</c> to prevent cross-test contamination.</para>
        /// </summary>
        public static void ClearExternalPasses() => _externalPasses.Clear();

        public static MachinePackageValidationResult Validate(MachinePackageDefinition package)
        {
            var issues = new List<MachinePackageValidationIssue>();

            if (package == null)
            {
                issues.Add(ValidationPassHelpers.Error("$", "Machine package is null."));
                return new MachinePackageValidationResult(issues.ToArray());
            }

            ValidateRequiredText(package.schemaVersion,  "schemaVersion",  issues);
            ValidateRequiredText(package.packageVersion, "packageVersion", issues);

            // Build id sets once — shared across all passes via context.
            HashSet<string> assemblyIds      = BuildIdSet(package.GetAssemblies(),       "assemblies",       item => item.id, issues);
            HashSet<string> subassemblyIds   = BuildIdSet(package.GetSubassemblies(),    "subassemblies",    item => item.id, issues);
            HashSet<string> partIds          = BuildIdSet(package.GetParts(),            "parts",            item => item.id, issues);
            HashSet<string> toolIds          = BuildIdSet(package.GetTools(),            "tools",            item => item.id, issues);
            HashSet<string> stepIds          = BuildIdSet(package.GetSteps(),            "steps",            item => item.id, issues);
            HashSet<string> validationRuleIds = BuildIdSet(package.GetValidationRules(), "validationRules",  item => item.id, issues);
            HashSet<string> hintIds          = BuildIdSet(package.GetHints(),            "hints",            item => item.id, issues);
            HashSet<string> effectIds        = BuildIdSet(package.GetEffects(),          "effects",          item => item.id, issues);
            HashSet<string> targetIds        = BuildIdSet(package.GetTargets(),          "targets",          item => item.id, issues);

            Dictionary<string, ToolDefinition> toolDefsById = BuildToolDefsLookup(package.GetTools());

            var ctx = new ValidationPassContext(
                package, issues,
                assemblyIds, subassemblyIds, partIds, toolIds,
                stepIds, validationRuleIds, hintIds, effectIds,
                targetIds, toolDefsById);

            // Built-in passes
            for (int i = 0; i < _builtInPasses.Length; i++)
                _builtInPasses[i].Execute(ctx);

            // External passes registered via RegisterPass
            for (int i = 0; i < _externalPasses.Count; i++)
                _externalPasses[i].Execute(ctx);

            return new MachinePackageValidationResult(issues.ToArray());
        }

        // ── Setup helpers (private — used only by Validate to build the context) ──

        private static void ValidateRequiredText(string value, string path, List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
                issues.Add(ValidationPassHelpers.Error(path, "A non-empty value is required."));
        }

        private static HashSet<string> BuildIdSet<T>(
            T[] items,
            string collectionName,
            Func<T, string> idSelector,
            List<MachinePackageValidationIssue> issues)
            where T : class
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items.Length; i++)
            {
                T item = items[i];
                string path = $"{collectionName}[{i}]";
                if (item == null) { issues.Add(ValidationPassHelpers.Error(path, "Collection entry is null.")); continue; }

                string id = idSelector(item);
                if (string.IsNullOrWhiteSpace(id)) { issues.Add(ValidationPassHelpers.Error($"{path}.id", "A stable id is required.")); continue; }
                if (!ids.Add(id)) issues.Add(ValidationPassHelpers.Error($"{path}.id", $"Duplicate id '{id}' found in {collectionName}."));
            }
            return ids;
        }

        private static Dictionary<string, ToolDefinition> BuildToolDefsLookup(ToolDefinition[] tools)
        {
            var dict = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
            if (tools == null) return dict;
            foreach (var t in tools)
                if (t != null && !string.IsNullOrWhiteSpace(t.id))
                    dict[t.id] = t;
            return dict;
        }
    }
}
