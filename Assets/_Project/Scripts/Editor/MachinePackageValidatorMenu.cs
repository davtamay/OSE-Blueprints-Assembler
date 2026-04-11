using System.IO;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Content.Validation;
using OSE.Interaction;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Editor menu items for validating machine.json packages without entering Play mode.
    /// Loads each package from the authoring folder, runs <see cref="MachinePackageValidator"/>,
    /// and prints a full report to the Console.
    /// </summary>
    internal static class MachinePackageValidatorMenu
    {
        private const string AuthoringRoot = PackageJsonUtils.AuthoringRoot;

        [MenuItem("OSE/Validate All Packages")]
        private static void ValidateAllPackages()
        {
            // Ensure profile registry is wired for editor-time validation.
            MachinePackageValidator.IsProfileRegistered = ToolProfileRegistry.Has;
            string root = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(root))
            {
                Debug.LogWarning($"[Validator] Authoring root not found: {AuthoringRoot}");
                return;
            }

            int totalErrors = 0;
            int totalWarnings = 0;
            int packageCount = 0;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string jsonPath = Path.Combine(dir, "machine.json");
                if (!File.Exists(jsonPath))
                    continue;

                string folderName = Path.GetFileName(dir);
                packageCount++;

                ValidatePackageAt(jsonPath, folderName, out int errors, out int warnings);
                totalErrors += errors;
                totalWarnings += warnings;
            }

            if (packageCount == 0)
            {
                Debug.LogWarning("[Validator] No machine.json files found under " + AuthoringRoot);
                return;
            }

            string summary = $"[Validator] Validated {packageCount} package(s): {totalErrors} error(s), {totalWarnings} warning(s).";
            if (totalErrors > 0)
                Debug.LogError(summary);
            else if (totalWarnings > 0)
                Debug.LogWarning(summary);
            else
                Debug.Log(summary + " All clean.");
        }

        private static void ValidatePackageAt(string jsonPath, string folderName, out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;

            string json;
            try
            {
                json = File.ReadAllText(jsonPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Validator] [{folderName}] Failed to read file: {ex.Message}");
                errors = 1;
                return;
            }

            MachinePackageDefinition pkg;
            try
            {
                pkg = JsonUtility.FromJson<MachinePackageDefinition>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Validator] [{folderName}] JSON parse error: {ex.Message}");
                errors = 1;
                return;
            }

            if (pkg == null)
            {
                Debug.LogError($"[Validator] [{folderName}] JSON parsed to null.");
                errors = 1;
                return;
            }

            // Mirror the runtime loader: normalize before validating so template-based
            // parts (and other inflated fields) are in the same shape the runtime sees.
            MachinePackageNormalizer.Normalize(pkg);

            MachinePackageValidationResult result = MachinePackageValidator.Validate(pkg);

            foreach (var issue in result.Issues)
            {
                string msg = $"[Validator] [{folderName}] {issue.Severity} at {issue.Path}: {issue.Message}";
                if (issue.Severity == MachinePackageIssueSeverity.Error)
                {
                    Debug.LogError(msg);
                    errors++;
                }
                else
                {
                    Debug.LogWarning(msg);
                    warnings++;
                }
            }

            if (!result.HasErrors && !result.HasWarnings)
                Debug.Log($"[Validator] [{folderName}] Valid — no issues found.");
        }
    }
}
