using System.IO;
using OSE.App;
using OSE.Runtime;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// One-shot maintenance operations for machine.json authoring files.
    /// Access via the OSE / Package Builder menu in the Unity Editor.
    /// </summary>
    internal static class MachinePackageBuilder
    {
        private const string AuthoringRoot = PackageJsonUtils.AuthoringRoot;

        /// <summary>
        /// Rounds all float literals in every machine.json under the authoring root
        /// to 4 decimal places. Safe to run multiple times (idempotent).
        ///
        /// Why: Unity's JsonUtility writes up to 9 significant digits per float.
        /// 4 decimal places gives 0.1 mm / 0.01° precision — sufficient for
        /// assembly training. Reduces file size by ~15–20%.
        /// </summary>
        [MenuItem("OSE/Package Builder/Normalize Float Precision (All Packages)")]
        private static void NormalizeFloatPrecisionAll()
        {
            string[] jsonFiles = Directory.GetFiles(AuthoringRoot, "machine.json", SearchOption.AllDirectories);
            if (jsonFiles.Length == 0)
            {
                Debug.LogWarning("[PackageBuilder] No machine.json files found under " + AuthoringRoot);
                return;
            }

            int totalDelta = 0;
            foreach (string path in jsonFiles)
            {
                string before = File.ReadAllText(path);
                string after  = PackageJsonUtils.RoundFloatsInJson(before);
                if (after == before)
                {
                    Debug.Log($"[PackageBuilder] {Path.GetDirectoryName(path)} — already normalized, no changes.");
                    continue;
                }

                File.WriteAllText(path, after);
                int delta = before.Length - after.Length;
                totalDelta += delta;
                Debug.Log($"[PackageBuilder] {Path.GetDirectoryName(path)} — {before.Length:N0} → {after.Length:N0} chars  (saved {delta:N0})");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[PackageBuilder] Float precision normalization complete. Total saved: {totalDelta:N0} chars across {jsonFiles.Length} file(s).");
        }

        [MenuItem("OSE/Debug/Jump to Last Step %#l")]
        private static void JumpToLastStep()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Debug] Jump to Last Step requires Play mode.");
                return;
            }

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                Debug.LogWarning("[Debug] No active session found.");
                return;
            }

            bool ok = session.NavigateToLastStep();
            Debug.Log($"[Debug] Jump to last step: {(ok ? "success" : "failed")}");
        }

        [MenuItem("OSE/Debug/Jump to Last Step %#l", validate = true)]
        private static bool JumpToLastStepValidate() => Application.isPlaying;
    }
}
