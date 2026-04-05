using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Content.Loading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Copies machine packages from the authoring folder (Assets/_Project/Data/Packages/)
    /// to StreamingAssets/MachinePackages/ so that builds always ship the latest authored data.
    ///
    /// The sync copies machine.json and all binary asset files (glb, fbx, usd, png, etc.)
    /// while skipping Unity .meta files, which have no meaning at runtime.
    ///
    /// Usage:
    ///   - OSE → Sync Packages to StreamingAssets   (manual, any time)
    ///   - Runs automatically before every build via IPreprocessBuildWithReport
    /// </summary>
    public static class PackageSyncTool
    {
        private const string AuthoringRoot    = "Assets/_Project/Data/Packages";
        private const string StreamingRoot    = "Assets/StreamingAssets/MachinePackages";
        private const string MenuPath         = "OSE/Sync Packages to StreamingAssets";

        // Extensions the runtime actually needs at runtime. Everything else is editor-only.
        private static readonly string[] RuntimeExtensions =
        {
            ".json", ".glb", ".fbx", ".usd", ".usda", ".usdc",
            ".png", ".jpg", ".jpeg", ".tga", ".wav", ".mp3", ".ogg"
        };

        [MenuItem(MenuPath)]
        public static void SyncFromMenu()
        {
            int copied = Sync();
            AssetDatabase.Refresh();
            Debug.Log($"[OSE] Package sync complete. {copied} file(s) copied to StreamingAssets.");
            EditorUtility.DisplayDialog("Package Sync", $"Sync complete.\n{copied} file(s) updated.", "OK");
        }

        [MenuItem("OSE/Bake Asset Refs")]
        public static void BakeAssetRefsFromMenu()
        {
            (int baked, int warned) = BakeAllAssetRefs();
            AssetDatabase.Refresh();
            string msg = $"Baked {baked} asset ref(s).\n{warned} part(s) could not be resolved — check Console.";
            Debug.Log($"[OSE] {msg}");
            EditorUtility.DisplayDialog("Bake Asset Refs", msg, "OK");
        }

        /// <summary>
        /// For every package under the authoring folder, runs the 3-pass asset resolver and
        /// writes the resolved filename into any <c>assetRef</c> field that is currently empty.
        /// Already-authored values are never overwritten.
        ///
        /// Returns (bakedCount, warnCount): baked = fields updated, warned = parts still unresolved.
        /// </summary>
        public static (int baked, int warned) BakeAllAssetRefs()
        {
            string authoringRoot = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(authoringRoot)) return (0, 0);

            int totalBaked = 0, totalWarned = 0;

            foreach (string pkgDir in Directory.GetDirectories(authoringRoot))
            {
                string pkgId    = Path.GetFileName(pkgDir);
                string jsonPath = PackageJsonUtils.GetJsonPath(pkgId);
                if (jsonPath == null) continue;

                MachinePackageDefinition pkg = PackageJsonUtils.LoadPackage(pkgId);
                if (pkg?.parts == null || pkg.parts.Length == 0) continue;

                // Run full 3-pass resolver (includes editor-only Pass 2 node scan)
                var resolver = new PackageAssetResolver();
                resolver.BuildCatalog(pkgId, pkg.parts);

                string json = File.ReadAllText(jsonPath);
                bool   anyChanged = false;

                foreach (var part in pkg.parts)
                {
                    if (!string.IsNullOrEmpty(part.assetRef)) continue; // already authored — never overwrite
                    AssetResolution res = resolver.Resolve(part.id);
                    if (!res.IsResolved) continue;

                    string filename = Path.GetFileName(res.AssetPath);
                    if (PackageJsonUtils.SetEmptyStringField(ref json, part.id, "assetRef", filename))
                    {
                        totalBaked++;
                        anyChanged = true;
                    }
                }

                if (anyChanged)
                    File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);

                // Count and log parts that still couldn't be resolved
                foreach (var part in pkg.parts)
                {
                    if (!string.IsNullOrEmpty(part.assetRef)) continue;
                    if (!resolver.Resolve(part.id).IsResolved)
                    {
                        totalWarned++;
                        Debug.LogWarning($"[OSE BakeAssetRefs] {pkgId}/{part.id}: no matching GLB found — assign manually via Assembly Step Authoring.");
                    }
                }
            }

            return (totalBaked, totalWarned);
        }

        /// <summary>
        /// Copies all authoring packages to StreamingAssets.
        /// Returns the number of files written.
        /// </summary>
        public static int Sync()
        {
            string sourceRoot = Path.GetFullPath(AuthoringRoot);
            string destRoot   = Path.GetFullPath(StreamingRoot);

            if (!Directory.Exists(sourceRoot))
            {
                Debug.LogWarning($"[OSE] Authoring packages folder not found: {sourceRoot}");
                return 0;
            }

            Directory.CreateDirectory(destRoot);

            int count = 0;
            foreach (string sourceFile in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsRuntimeFile(sourceFile))
                    continue;

                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string dest     = Path.Combine(destRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                // Only overwrite if source is newer or sizes differ (cheap dirty check)
                if (NeedsUpdate(sourceFile, dest))
                {
                    File.Copy(sourceFile, dest, overwrite: true);
                    count++;
                }
            }

            return count;
        }

        private static bool IsRuntimeFile(string path)
        {
            if (path.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                return false;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            foreach (string allowed in RuntimeExtensions)
            {
                if (ext == allowed)
                    return true;
            }
            return false;
        }

        private static bool NeedsUpdate(string source, string dest)
        {
            if (!File.Exists(dest))
                return true;

            FileInfo s = new FileInfo(source);
            FileInfo d = new FileInfo(dest);
            return s.LastWriteTimeUtc > d.LastWriteTimeUtc || s.Length != d.Length;
        }
    }

    /// <summary>
    /// Runs PackageSyncTool automatically before every build so builds always
    /// ship the latest authored machine packages in StreamingAssets.
    /// </summary>
    internal sealed class PackageSyncPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // Step 1 — auto-bake any missing assetRefs using the full 3-pass resolver.
            // This ensures every part that can be discovered gets an explicit reference
            // before the JSON is copied to StreamingAssets, so runtime loading never needs
            // to do editor-only file scanning.
            Debug.Log("[OSE] Pre-build: baking missing asset refs...");
            (int baked, int warned) = PackageSyncTool.BakeAllAssetRefs();
            if (baked > 0)
                Debug.Log($"[OSE] Pre-build: baked {baked} asset ref(s).");
            if (warned > 0)
                Debug.LogWarning($"[OSE] Pre-build: {warned} part(s) still have no resolvable GLB. " +
                                 "Assign assetRef manually in Assembly Step Authoring, or add the GLB file.");

            // Step 2 — sync authoring folder → StreamingAssets (now with baked refs)
            Debug.Log("[OSE] Pre-build: syncing machine packages to StreamingAssets...");
            int copied = PackageSyncTool.Sync();
            AssetDatabase.Refresh();
            Debug.Log($"[OSE] Pre-build sync complete. {copied} file(s) updated.");
        }
    }
}
