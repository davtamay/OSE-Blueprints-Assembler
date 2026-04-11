using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Content.Validation;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Validates all machine packages before entering Play mode.
    /// Blocks Play if any package has validation errors, showing a compact dialog.
    /// Warnings are logged but do not block Play.
    ///
    /// Also adds:
    ///   OSE → Validate All Packages   — manual run, always shows dialog
    ///   OSE → Validate d3d_v18_10     — single-package quick check
    /// </summary>
    [InitializeOnLoad]
    public static class MachineJsonPrePlayValidator
    {
        private const string AuthoringRoot = "Assets/_Project/Data/Packages";
        private const string BlockOnErrorPref = "OSE.PrePlayValidator.BlockOnError";

        static MachineJsonPrePlayValidator()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            if (!EditorPrefs.GetBool(BlockOnErrorPref, true)) return;

            var report = RunValidation();
            if (report.TotalErrors == 0) return;

            // Block Play mode
            EditorApplication.isPlaying = false;

            string message = BuildDialogMessage(report, maxErrors: 20);
            bool disable = !EditorUtility.DisplayDialog(
                $"Machine Package Errors — Play Blocked ({report.TotalErrors} error(s))",
                message,
                "Fix errors first",
                "Disable this check");

            if (disable)
            {
                EditorPrefs.SetBool(BlockOnErrorPref, false);
                UnityEngine.Debug.LogWarning("[OSE] Pre-play package validation disabled. Re-enable via OSE → Validate All Packages.");
            }
        }

        [MenuItem("OSE/Validate All Packages")]
        public static void ValidateAllMenu()
        {
            // Re-enable blocking in case it was disabled
            EditorPrefs.SetBool(BlockOnErrorPref, true);

            var report = RunValidation();
            string message = BuildDialogMessage(report, maxErrors: 40);

            string title = report.TotalErrors > 0
                ? $"Validation — {report.TotalErrors} error(s) in {report.PackagesWithErrors} package(s)"
                : report.TotalWarnings > 0
                    ? $"Validation — Clean ({report.TotalWarnings} warning(s))"
                    : "Validation — All packages clean ✓";

            EditorUtility.DisplayDialog(title, message, "OK");

            if (report.TotalErrors > 0)
                UnityEngine.Debug.LogError($"[OSE] Package validation: {report.TotalErrors} error(s). See console for details.");
            else if (report.TotalWarnings > 0)
                UnityEngine.Debug.LogWarning($"[OSE] Package validation: {report.TotalWarnings} warning(s).");
            else
                UnityEngine.Debug.Log("[OSE] Package validation: all packages clean.");
        }

        // ── Core validation ──────────────────────────────────────────────────

        private static ValidationReport RunValidation()
        {
            var report = new ValidationReport();
            string root = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(root)) return report;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string jsonPath = Path.Combine(dir, "machine.json");
                if (!File.Exists(jsonPath)) continue;

                string packageId = Path.GetFileName(dir);
                try
                {
                    // Use split-aware loading so split-layout packages (assemblies/ folder)
                    // are merged the same way the runtime loader does before validating.
                    var pkg = LoadPackageForValidation(dir);
                    if (pkg == null)
                    {
                        report.AddPackage(packageId, new[] { "Failed to parse JSON" }, Array.Empty<string>());
                        continue;
                    }

                    // Mirror the runtime loader: normalize before validating so template-based
                    // parts (and other inflated fields) are in the same shape the runtime sees.
                    MachinePackageNormalizer.Normalize(pkg);

                    var result = MachinePackageValidator.Validate(pkg);
                    var errors   = new List<string>();
                    var warnings = new List<string>();

                    foreach (var issue in result.Issues)
                    {
                        if (issue.Severity == MachinePackageIssueSeverity.Error)
                            errors.Add($"{issue.Path} — {issue.Message}");
                        else
                            warnings.Add($"{issue.Path} — {issue.Message}");
                    }

                    report.AddPackage(packageId, errors, warnings);

                    // Log all issues to console for easy navigation
                    foreach (var e in errors)
                        UnityEngine.Debug.LogError($"[OSE][{packageId}] {e}");
                    foreach (var w in warnings)
                        UnityEngine.Debug.LogWarning($"[OSE][{packageId}] {w}");
                }
                catch (Exception ex)
                {
                    report.AddPackage(packageId, new[] { $"Exception: {ex.Message}" }, Array.Empty<string>());
                }
            }

            return report;
        }

        private static string BuildDialogMessage(ValidationReport report, int maxErrors)
        {
            var sb = new StringBuilder();

            if (report.TotalErrors == 0 && report.TotalWarnings == 0)
            {
                sb.AppendLine("All packages are valid.");
                return sb.ToString();
            }

            foreach (var pkg in report.Packages)
            {
                if (pkg.Errors.Count == 0 && pkg.Warnings.Count == 0) continue;

                sb.AppendLine($"── {pkg.PackageId} ──");
                int shown = 0;
                foreach (var e in pkg.Errors)
                {
                    if (shown >= maxErrors) { sb.AppendLine($"  … and {pkg.Errors.Count - shown} more errors"); break; }
                    sb.AppendLine($"  ✗ {e}");
                    shown++;
                }
                if (pkg.Warnings.Count > 0)
                    sb.AppendLine($"  ⚠ {pkg.Warnings.Count} warning(s) — see Console");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ── Split-layout loading (mirrors MachinePackageLoader.LoadSplitLayoutAsync) ──

        /// <summary>
        /// Loads a package for validation. Handles both single-file (legacy machine.json)
        /// and split-layout (assemblies/ folder) packages, merging all files the same way
        /// the runtime loader does so the validator sees the complete merged definition.
        /// </summary>
        private static MachinePackageDefinition LoadPackageForValidation(string packageDir)
        {
            string assemblyFolder = Path.Combine(packageDir, "assemblies");
            if (Directory.Exists(assemblyFolder))
                return LoadSplitLayoutSync(packageDir, assemblyFolder);

            string json = File.ReadAllText(Path.Combine(packageDir, "machine.json"), Encoding.UTF8);
            return JsonUtility.FromJson<MachinePackageDefinition>(json);
        }

        private static MachinePackageDefinition LoadSplitLayoutSync(string packageDir, string assemblyFolder)
        {
            // machine.json — metadata only
            string json = File.ReadAllText(Path.Combine(packageDir, "machine.json"), Encoding.UTF8);
            var pkg = JsonUtility.FromJson<MachinePackageDefinition>(json) ?? new MachinePackageDefinition();

            // shared.json — tools, partTemplates, global hints, validationRules, effects
            string sharedPath = Path.Combine(packageDir, "shared.json");
            if (File.Exists(sharedPath))
            {
                var shared = JsonUtility.FromJson<MachinePackageDefinition>(
                    File.ReadAllText(sharedPath, Encoding.UTF8));
                if (shared != null)
                {
                    pkg.tools           = shared.tools           ?? pkg.tools;
                    pkg.partTemplates   = shared.partTemplates   ?? pkg.partTemplates;
                    pkg.validationRules = MergeArrays(pkg.validationRules, shared.validationRules);
                    pkg.effects         = MergeArrays(pkg.effects,         shared.effects);
                    pkg.hints           = MergeArrays(pkg.hints,           shared.hints);
                    if (pkg.challengeConfig == null && shared.challengeConfig != null)
                        pkg.challengeConfig = shared.challengeConfig;
                }
            }

            // assemblies/*.json — per-assembly content
            foreach (string asmFile in Directory.GetFiles(assemblyFolder, "*.json").OrderBy(f => f))
            {
                var asmChunk = JsonUtility.FromJson<MachinePackageDefinition>(
                    File.ReadAllText(asmFile, Encoding.UTF8));
                if (asmChunk == null) continue;

                pkg.assemblies      = MergeArrays(pkg.assemblies,      asmChunk.assemblies);
                pkg.subassemblies   = MergeArrays(pkg.subassemblies,   asmChunk.subassemblies);
                pkg.parts           = MergeArrays(pkg.parts,           asmChunk.parts);
                pkg.steps           = MergeArrays(pkg.steps,           asmChunk.steps);
                pkg.targets         = MergeArrays(pkg.targets,         asmChunk.targets);
                pkg.hints           = MergeArrays(pkg.hints,           asmChunk.hints);
                pkg.validationRules = MergeArrays(pkg.validationRules, asmChunk.validationRules);
            }

            // preview_config.json — TTAW-generated positional data
            string previewPath = Path.Combine(packageDir, "preview_config.json");
            if (File.Exists(previewPath))
            {
                var previewWrap = JsonUtility.FromJson<MachinePackageDefinition>(
                    File.ReadAllText(previewPath, Encoding.UTF8));
                if (previewWrap?.previewConfig != null)
                    pkg.previewConfig = previewWrap.previewConfig;
            }

            return pkg;
        }

        private static T[] MergeArrays<T>(T[] a, T[] b)
        {
            if (a == null || a.Length == 0) return b ?? Array.Empty<T>();
            if (b == null || b.Length == 0) return a;
            var result = new T[a.Length + b.Length];
            Array.Copy(a, 0, result, 0,        a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }

        // ── Data ─────────────────────────────────────────────────────────────

        private class PackageResult
        {
            public string PackageId;
            public List<string> Errors   = new();
            public List<string> Warnings = new();
        }

        private class ValidationReport
        {
            public List<PackageResult> Packages = new();
            public int TotalErrors;
            public int TotalWarnings;
            public int PackagesWithErrors;

            public void AddPackage(string id, IEnumerable<string> errors, IEnumerable<string> warnings)
            {
                var r = new PackageResult { PackageId = id };
                r.Errors.AddRange(errors);
                r.Warnings.AddRange(warnings);
                Packages.Add(r);
                TotalErrors   += r.Errors.Count;
                TotalWarnings += r.Warnings.Count;
                if (r.Errors.Count > 0) PackagesWithErrors++;
            }
        }
    }
}
