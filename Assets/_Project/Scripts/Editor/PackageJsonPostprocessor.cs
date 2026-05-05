using System;
using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Content.Validation;
using OSE.Core;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Auto-validates a machine package whenever any of its JSON files
    /// (machine.json, shared.json, assemblies/*.json, preview_config.json)
    /// is saved in the editor. Runs the full validator pipeline (including
    /// the StepRuntimeSimulationPass that catches deadlocks at authoring
    /// time) and surfaces errors / warnings in the Unity console.
    ///
    /// <para>Why this exists: <see cref="MachineJsonPrePlayValidator"/> runs
    /// only at Play-mode entry. If the author saves a broken edit and then
    /// goes for coffee or doesn't press Play for an hour, the broken state
    /// sits silently. This gives instant feedback the moment the file hits
    /// disk — the same Warn/Error stream that pre-Play uses, just earlier.</para>
    ///
    /// <para>Cheap by construction: only fires when an actual package JSON
    /// changes (not every asset import), validates only the affected
    /// packages, and short-circuits if nothing to validate.</para>
    /// </summary>
    public sealed class PackageJsonPostprocessor : AssetPostprocessor
    {
        private const string PackagesDataPath = "Assets/_Project/Data/Packages";

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAsset)
        {
            HashSet<string> changedPackageIds = null;

            foreach (string path in importedAssets)
            {
                if (!IsPackageJson(path)) continue;
                string packageId = ExtractPackageId(path);
                if (string.IsNullOrEmpty(packageId)) continue;
                (changedPackageIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(packageId);
            }

            if (changedPackageIds == null || changedPackageIds.Count == 0) return;

            foreach (string packageId in changedPackageIds)
                ValidatePackage(packageId);
        }

        private static bool IsPackageJson(string path)
        {
            if (!path.StartsWith(PackagesDataPath, StringComparison.OrdinalIgnoreCase)) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractPackageId(string path)
        {
            string rel = path.Substring(PackagesDataPath.Length).TrimStart('/', '\\');
            string id = rel.Split(new[] { '/', '\\' }, 2)[0];
            return string.IsNullOrEmpty(id) ? null : id;
        }

        private static void ValidatePackage(string packageId)
        {
            MachinePackageDefinition pkg;
            try
            {
                pkg = PackageJsonUtils.LoadPackage(packageId);
            }
            catch (Exception ex)
            {
                OseLog.Error($"[PackageJsonPostprocessor] Failed to load '{packageId}' for validation: {ex.Message}");
                return;
            }
            if (pkg == null) return;

            // Normalize first so the validator sees the same state runtime
            // does (taskOrder fills, hold-at-end synthesis, etc.). The
            // normalizer's own per-step Warns will fire here too — useful.
            try { MachinePackageNormalizer.Normalize(pkg); }
            catch (Exception ex)
            {
                OseLog.Error($"[PackageJsonPostprocessor] Normalize failed for '{packageId}': {ex.Message}");
                return;
            }

            MachinePackageValidationResult result;
            try { result = MachinePackageValidator.Validate(pkg); }
            catch (Exception ex)
            {
                OseLog.Error($"[PackageJsonPostprocessor] Validate failed for '{packageId}': {ex.Message}");
                return;
            }

            int errors = 0, warnings = 0;
            if (result.Issues != null)
            {
                for (int i = 0; i < result.Issues.Length; i++)
                {
                    var issue = result.Issues[i];
                    string line = $"[PackageValidate:{packageId}] {issue.Path}: {issue.Message}";
                    if (issue.Severity == MachinePackageIssueSeverity.Error)
                    {
                        errors++;
                        OseLog.Error(line);
                    }
                    else
                    {
                        warnings++;
                        OseLog.Warn(line);
                    }
                }
            }

            if (errors > 0 || warnings > 0)
                OseLog.Info($"[PackageJsonPostprocessor] '{packageId}' validated after edit: {errors} error(s), {warnings} warning(s).");
        }
    }
}
