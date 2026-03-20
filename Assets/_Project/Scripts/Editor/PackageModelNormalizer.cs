using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Post-import scale normalization for AI-generated GLB models.
    ///
    /// AI 3D generators (Rodin, etc.) export models at arbitrary scales.
    /// This tool reads the POSITION accessor min/max from each GLB's binary header,
    /// computes a uniform scale factor so the model's largest axis matches the
    /// real-world largest axis, and writes it into the package's previewConfig
    /// (startScale + playScale). Uniform scaling preserves the model's natural
    /// shape — proportions should be corrected at generation time, not here.
    ///
    /// Real-world dimensions are stored in <see cref="PartDimensionCatalog"/>.
    ///
    /// Usage:  OSE → Normalize Package Model Scales
    /// </summary>
    public static class PackageModelNormalizer
    {
        [MenuItem("OSE/Normalize Package Model Scales")]
        private static void NormalizeAllPackages()
        {
            string fullRoot = Path.GetFullPath(PackageJsonUtils.AuthoringRoot);
            if (!Directory.Exists(fullRoot))
            {
                Debug.LogError("[ModelNormalizer] Authoring root not found: " + fullRoot);
                return;
            }

            int updated = 0;
            int skipped = 0;

            try
            {
                EditorUtility.DisplayProgressBar("Normalizing Model Scales", "Reading packages...", 0f);

                var packageDirs = Directory.GetDirectories(fullRoot);

                for (int pi = 0; pi < packageDirs.Length; pi++)
                {
                    string packageDir = packageDirs[pi];
                    string packageId  = Path.GetFileName(packageDir);
                    string jsonPath   = Path.Combine(packageDir, "machine.json");

                    if (!File.Exists(jsonPath)) continue;

                    float progress = (float)pi / packageDirs.Length;
                    EditorUtility.DisplayProgressBar("Normalizing Model Scales",
                        $"Package: {packageId}", progress);

                    var pkg = PackageJsonUtils.LoadPackage(packageId);
                    if (pkg?.parts == null || pkg.previewConfig?.partPlacements == null)
                        continue;

                    bool changed = false;

                    foreach (var placement in pkg.previewConfig.partPlacements)
                    {
                        if (string.IsNullOrEmpty(placement.partId)) continue;

                        // Find matching part definition
                        PartDefinition partDef = null;
                        foreach (var p in pkg.parts)
                            if (p.id == placement.partId) { partDef = p; break; }

                        if (partDef == null || string.IsNullOrEmpty(partDef.assetRef))
                            continue;

                        // Get real-world target size
                        Vector3 targetSize = PartDimensionCatalog.GetDimensions(placement.partId);
                        if (targetSize == Vector3.zero)
                        {
                            skipped++;
                            continue;
                        }

                        // Read the GLB native bounds
                        string glbPath = Path.Combine(packageDir,
                            partDef.assetRef.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(glbPath)) continue;

                        Vector3 nativeSize = ReadGlbBoundsSize(glbPath);
                        if (nativeSize == Vector3.zero)
                        {
                            Debug.LogWarning($"[ModelNormalizer] Could not read bounds from: {glbPath}");
                            continue;
                        }

                        // Compute uniform scale: fit largest model axis to largest target axis.
                        // Uniform scaling preserves the model's shape. If proportions are wrong,
                        // fix them at generation time (see PART_AUTHORING_PIPELINE.md §28.16).
                        float modelMax  = Mathf.Max(nativeSize.x, nativeSize.y, nativeSize.z);
                        float targetMax = Mathf.Max(targetSize.x, targetSize.y, targetSize.z);

                        if (modelMax < 0.0001f) continue;
                        float uniformScale = Mathf.Round(targetMax / modelMax * 10000f) / 10000f;

                        // Apply to both start and play scales
                        placement.startScale = new SceneFloat3 { x = uniformScale, y = uniformScale, z = uniformScale };
                        placement.playScale  = new SceneFloat3 { x = uniformScale, y = uniformScale, z = uniformScale };
                        changed = true;
                        updated++;

                        Debug.Log($"[ModelNormalizer] {placement.partId}: native {nativeSize} → " +
                                  $"target {targetSize} → scale {uniformScale:F4}");
                    }

                    if (changed)
                    {
                        PackageJsonUtils.WritePreviewConfig(jsonPath, pkg.previewConfig);
                        Debug.Log($"[ModelNormalizer] Updated previewConfig in {packageId}/machine.json");
                    }
                }

                // Auto-sync authoring → StreamingAssets so builds stay current
                int synced = PackageSyncTool.Sync();
                if (synced > 0)
                    Debug.Log($"[ModelNormalizer] Auto-synced {synced} file(s) to StreamingAssets.");

                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Model Scale Normalization",
                $"Done.\n  Updated: {updated}\n  Skipped (no dimensions): {skipped}", "OK");
        }

        /// <summary>
        /// Reads a GLB file's first POSITION accessor min/max to determine
        /// the model's native bounding box size in meters.
        /// </summary>
        internal static Vector3 ReadGlbBoundsSize(string glbPath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(glbPath);
                if (bytes.Length < 20) return Vector3.zero;

                // GLB header: magic(4) + version(4) + length(4) + chunkLen(4) + chunkType(4)
                uint magic = BitConverter.ToUInt32(bytes, 0);
                if (magic != 0x46546C67) return Vector3.zero; // "glTF"

                uint chunkLen = BitConverter.ToUInt32(bytes, 12);
                if (chunkLen == 0 || 20 + chunkLen > bytes.Length) return Vector3.zero;

                string json = System.Text.Encoding.UTF8.GetString(bytes, 20, (int)chunkLen);

                // Parse all accessor min/max pairs — the POSITION accessor has 3-component min/max
                // We look for the first accessor with "type":"VEC3" that has both min and max
                return ParseFirstPositionBounds(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ModelNormalizer] Error reading GLB: {glbPath}: {ex.Message}");
                return Vector3.zero;
            }
        }

        private static Vector3 ParseFirstPositionBounds(string json)
        {
            // Find "accessors" array and extract first VEC3 with min+max
            // Using regex since we don't want a JSON dependency in editor code
            var accessorPattern = new Regex(
                @"\{[^{}]*""type""\s*:\s*""VEC3""[^{}]*\}", RegexOptions.Singleline);

            foreach (Match accessor in accessorPattern.Matches(json))
            {
                string block = accessor.Value;

                var minMatch = Regex.Match(block, @"""min""\s*:\s*\[\s*([-\d.eE+]+)\s*,\s*([-\d.eE+]+)\s*,\s*([-\d.eE+]+)\s*\]");
                var maxMatch = Regex.Match(block, @"""max""\s*:\s*\[\s*([-\d.eE+]+)\s*,\s*([-\d.eE+]+)\s*,\s*([-\d.eE+]+)\s*\]");

                if (!minMatch.Success || !maxMatch.Success) continue;

                float minX = float.Parse(minMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float minY = float.Parse(minMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float minZ = float.Parse(minMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                float maxX = float.Parse(maxMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float maxY = float.Parse(maxMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float maxZ = float.Parse(maxMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

                return new Vector3(
                    Mathf.Abs(maxX - minX),
                    Mathf.Abs(maxY - minY),
                    Mathf.Abs(maxZ - minZ));
            }

            return Vector3.zero;
        }
    }
}
