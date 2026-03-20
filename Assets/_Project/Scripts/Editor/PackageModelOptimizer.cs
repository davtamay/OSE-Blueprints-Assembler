using System;
using System.Diagnostics;
using System.IO;
using OSE.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OSE.Editor
{
    /// <summary>
    /// Optimizes GLB models in machine packages using gltfpack (meshoptimizer).
    ///
    /// Applies non-destructive optimizations:
    ///   1. Meshopt compression (-cc) — lossless vertex/index buffer compression
    ///   2. KTX2/Basis texture compression (-tc) — reduces texture memory 4-8x
    ///      WARNING: -tc can distort UVs on models with fine-detail textures
    ///      (gauge markings, text, logos). Use the non-KTX2 presets for those.
    ///
    /// All presets use -noq (no vertex quantization) to preserve coordinate space
    /// for the scale normalizer.
    ///
    /// The original GLB is backed up to {name}.glb.bak before overwriting.
    ///
    /// Prerequisites: gltfpack must be on PATH or placed in the project's Tools/ folder.
    ///   Download: https://github.com/zeux/meshoptimizer/releases
    ///
    /// Usage:  OSE → Optimize Package Models (gltfpack)
    /// </summary>
    public static class PackageModelOptimizer
    {
        private const string ToolsDir   = "Tools";
        private const string GltfpackExe = "gltfpack.exe";

        // ── Quality presets ─────────────────────────────────────────────

        /// <summary>
        /// Recommended — meshopt compression only. Preserves textures bit-for-bit.
        /// Safe for all models including those with fine-detail textures (gauges, labels).
        /// ~5-10% file size reduction (mesh data only).
        /// </summary>
        private const string ArgsRecommended = "-noq -cc";

        /// <summary>
        /// Smaller — adds KTX2/Basis texture compression for much better size reduction.
        /// WARNING: Can distort UVs on models with fine-detail textures (gauge markings,
        /// text, logos). Verify visually after applying. ~70-90% file size reduction.
        /// </summary>
        private const string ArgsSmaller = "-noq -cc -tc";

        /// <summary>
        /// Aggressive — KTX2 textures + mesh simplification (target 50% triangles).
        /// Same UV warning as Smaller, plus minor geometry loss.
        /// ~85-95% file size reduction.
        /// </summary>
        private const string ArgsAggressive = "-noq -cc -tc -si 0.5";

        [MenuItem("OSE/Optimize Package Models (Recommended — Mesh Compression)")]
        private static void OptimizeRecommended() => OptimizeAllPackages(ArgsRecommended, "Recommended");

        [MenuItem("OSE/Optimize Package Models (Smaller — With KTX2 Textures ⚠️)")]
        private static void OptimizeSmaller() => OptimizeAllPackages(ArgsSmaller, "Smaller (KTX2)");

        [MenuItem("OSE/Optimize Package Models (Aggressive — Simplify + KTX2 ⚠️)")]
        private static void OptimizeAggressive() => OptimizeAllPackages(ArgsAggressive, "Aggressive (KTX2)");

        private static void OptimizeAllPackages(string gltfpackArgs, string presetName)
        {
            string gltfpackPath = FindGltfpack();
            if (gltfpackPath == null)
            {
                EditorUtility.DisplayDialog("gltfpack Not Found",
                    "gltfpack executable not found.\n\n" +
                    "Download from: https://github.com/zeux/meshoptimizer/releases\n\n" +
                    "Place gltfpack.exe in the project's Tools/ folder, or add it to your system PATH.",
                    "OK");
                return;
            }

            string fullRoot = Path.GetFullPath(PackageJsonUtils.AuthoringRoot);
            if (!Directory.Exists(fullRoot))
            {
                Debug.LogError("[ModelOptimizer] Authoring root not found: " + fullRoot);
                return;
            }

            int optimized = 0;
            int skipped   = 0;
            int errors    = 0;
            long savedBytes = 0;

            try
            {
                EditorUtility.DisplayProgressBar("Optimizing Models", $"Preset: {presetName}", 0f);

                var glbFiles = Directory.GetFiles(fullRoot, "*.glb", SearchOption.AllDirectories);

                for (int i = 0; i < glbFiles.Length; i++)
                {
                    string glbPath = glbFiles[i];
                    string relativePath = glbPath.Substring(fullRoot.Length + 1);

                    // Skip already-backed-up originals
                    if (glbPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    float progress = (float)i / glbFiles.Length;
                    EditorUtility.DisplayProgressBar("Optimizing Models",
                        $"[{presetName}] {relativePath}", progress);

                    long originalSize = new FileInfo(glbPath).Length;

                    // Skip very small files (likely placeholders, < 10KB)
                    if (originalSize < 10240)
                    {
                        skipped++;
                        continue;
                    }

                    string tempOutput = glbPath + ".opt";
                    string backupPath = glbPath + ".bak";

                    try
                    {
                        string args = $"-i \"{glbPath}\" -o \"{tempOutput}\" {gltfpackArgs}";

                        var psi = new ProcessStartInfo
                        {
                            FileName = gltfpackPath,
                            Arguments = args,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(glbPath)
                        };

                        using (var proc = Process.Start(psi))
                        {
                            string stdout = proc.StandardOutput.ReadToEnd();
                            string stderr = proc.StandardError.ReadToEnd();
                            proc.WaitForExit(30000);

                            if (proc.ExitCode != 0)
                            {
                                Debug.LogWarning($"[ModelOptimizer] gltfpack failed for {relativePath}: {stderr}");
                                if (File.Exists(tempOutput)) File.Delete(tempOutput);
                                errors++;
                                continue;
                            }
                        }

                        long optimizedSize = new FileInfo(tempOutput).Length;
                        long saved = originalSize - optimizedSize;

                        // Only apply if we actually reduced size
                        if (optimizedSize >= originalSize)
                        {
                            File.Delete(tempOutput);
                            skipped++;
                            Debug.Log($"[ModelOptimizer] Skipped {relativePath} (no size reduction)");
                            continue;
                        }

                        // Backup original, swap in optimized
                        if (!File.Exists(backupPath))
                            File.Copy(glbPath, backupPath, false);
                        File.Copy(tempOutput, glbPath, true);
                        File.Delete(tempOutput);

                        savedBytes += saved;
                        optimized++;

                        float pct = (1f - (float)optimizedSize / originalSize) * 100f;
                        Debug.Log($"[ModelOptimizer] {relativePath}: {FormatBytes(originalSize)} → " +
                                  $"{FormatBytes(optimizedSize)} ({pct:F0}% reduction)");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ModelOptimizer] Error processing {relativePath}: {ex.Message}");
                        if (File.Exists(tempOutput)) File.Delete(tempOutput);
                        errors++;
                    }
                }

                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Model Optimization",
                $"Preset: {presetName}\n\n" +
                $"Optimized: {optimized}\n" +
                $"Skipped: {skipped}\n" +
                $"Errors: {errors}\n" +
                $"Total saved: {FormatBytes(savedBytes)}", "OK");
        }

        private static string FindGltfpack()
        {
            // Check project Tools/ folder first
            string localPath = Path.Combine(ToolsDir, GltfpackExe);
            if (File.Exists(localPath))
                return Path.GetFullPath(localPath);

            // Check PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(dir.Trim(), GltfpackExe);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }
    }
}
