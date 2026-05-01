using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.Content;
using OSE.Content.Loading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

using OSE.Core;
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

        // Split-layout file names — must match MachinePackageLoader's split-layout constants.
        private const string MachineJsonFileName   = "machine.json";
        private const string SharedJsonFileName    = "shared.json";
        private const string PreviewConfigFileName = "preview_config.json";
        private const string AssembliesFolderName  = "assemblies";

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
            OseLog.Info($"[OSE] Package sync complete. {copied} file(s) copied to StreamingAssets.");
            EditorUtility.DisplayDialog("Package Sync", $"Sync complete.\n{copied} file(s) updated.", "OK");
        }

        [MenuItem("OSE/Bake Asset Refs")]
        public static void BakeAssetRefsFromMenu()
        {
            (int baked, int warned) = BakeAllAssetRefs();
            AssetDatabase.Refresh();
            string msg = $"Baked {baked} asset ref(s).\n{warned} part(s) could not be resolved — check Console.";
            OseLog.Info($"[OSE] {msg}");
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
                        OseLog.Warn($"[OSE BakeAssetRefs] {pkgId}/{part.id}: no matching GLB found — assign manually via Assembly Step Authoring.");
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
                OseLog.Warn($"[OSE] Authoring packages folder not found: {sourceRoot}");
                return 0;
            }

            Directory.CreateDirectory(destRoot);

            // Track every dest path the sync intends to keep, so we can prune orphans afterward.
            var liveDestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int count = 0;
            foreach (string sourceFile in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsRuntimeFile(sourceFile))
                    continue;

                string relative = Path.GetRelativePath(sourceRoot, sourceFile);
                string dest     = Path.Combine(destRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                liveDestPaths.Add(Path.GetFullPath(dest));

                if (NeedsUpdate(sourceFile, dest))
                {
                    File.Copy(sourceFile, dest, overwrite: true);
                    // Preserve source mtime so the next dirty check is stable across runs
                    // (otherwise dest mtime = now, source mtime = older, and inequality keeps re-copying).
                    File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(sourceFile));
                    count++;
                }
            }

            // Prune orphans: any runtime-relevant file in StreamingAssets that has no source
            // counterpart. Without this, a deleted/renamed assembly JSON would silently linger
            // and get folded into the merge, producing duplicate-ID bugs at runtime.
            PruneOrphans(destRoot, liveDestPaths);

            // Collapse split-layout (machine.json + shared.json + assemblies/*.json + preview_config.json)
            // into a single merged machine.json per package. The runtime loader is single-file, so
            // builds need one self-contained file. The authoring folder is left untouched.
            MergeSplitLayoutsInStreamingAssets();

            // For every package now in StreamingAssets, bake the GLB+node manifest into
            // its machine.json so the runtime resolver can locate parts without filesystem
            // scans (WebGL StreamingAssets is HTTP-served — Directory.GetFiles fails there).
            BakeAssetManifestsInStreamingAssets();

            return count;
        }

        /// <summary>
        /// For every package in StreamingAssets that has an <c>assemblies/</c> subfolder,
        /// merges <c>shared.json</c>, all <c>assemblies/*.json</c>, and <c>preview_config.json</c>
        /// into the package's <c>machine.json</c>, then deletes the now-redundant per-file artifacts.
        ///
        /// Mirrors the editor-only merge in
        /// <see cref="OSE.Content.Loading.MachinePackageLoader"/>'s split-layout path so player
        /// builds (which don't compile that path) get a flat single file.
        /// </summary>
        private static void MergeSplitLayoutsInStreamingAssets()
        {
            string streamingRoot = Path.GetFullPath(StreamingRoot);
            if (!Directory.Exists(streamingRoot)) return;

            foreach (string pkgDir in Directory.GetDirectories(streamingRoot))
            {
                string asmFolder = Path.Combine(pkgDir, AssembliesFolderName);
                if (!Directory.Exists(asmFolder)) continue; // package isn't split-layout

                MergeSplitLayoutPackage(pkgDir);
            }
        }

        private static void MergeSplitLayoutPackage(string packageFolder)
        {
            string machineJsonPath = Path.Combine(packageFolder, MachineJsonFileName);
            if (!File.Exists(machineJsonPath))
            {
                OseLog.Warn($"[OSE] Split-layout merge: missing machine.json in '{packageFolder}'.");
                return;
            }

            var package = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(machineJsonPath));
            if (package == null)
            {
                OseLog.Warn($"[OSE] Split-layout merge: failed to parse machine.json in '{packageFolder}'.");
                return;
            }

            string sharedJsonPath = Path.Combine(packageFolder, SharedJsonFileName);
            if (File.Exists(sharedJsonPath))
            {
                var shared = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(sharedJsonPath));
                if (shared != null)
                {
                    package.tools           = shared.tools           ?? package.tools;
                    package.partTemplates   = shared.partTemplates   ?? package.partTemplates;
                    package.validationRules = MergeArrays(package.validationRules, shared.validationRules);
                    package.effects         = MergeArrays(package.effects,         shared.effects);
                    package.hints           = MergeArrays(package.hints,           shared.hints);
                    if (package.challengeConfig == null && shared.challengeConfig != null)
                        package.challengeConfig = shared.challengeConfig;
                }
            }

            string asmFolder = Path.Combine(packageFolder, AssembliesFolderName);
            foreach (string asmFile in Directory.GetFiles(asmFolder, "*.json").OrderBy(f => f))
            {
                var chunk = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(asmFile));
                if (chunk == null) continue;

                package.assemblies      = MergeArrays(package.assemblies,      chunk.assemblies);
                package.partGroups      = MergeArrays(package.partGroups,      chunk.partGroups);
                package.parts           = MergeArrays(package.parts,           chunk.parts);
                package.steps           = MergeArrays(package.steps,           chunk.steps);
                package.prefabInstances = MergeArrays(package.prefabInstances, chunk.prefabInstances);
                package.targets         = MergeArrays(package.targets,         chunk.targets);
                package.hints           = MergeArrays(package.hints,           chunk.hints);
                package.validationRules = MergeArrays(package.validationRules, chunk.validationRules);
            }

            string previewConfigPath = Path.Combine(packageFolder, PreviewConfigFileName);
            if (File.Exists(previewConfigPath))
            {
                var previewWrap = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(previewConfigPath));
                if (previewWrap?.previewConfig != null)
                    package.previewConfig = previewWrap.previewConfig;
            }

            string mergedJson = JsonUtility.ToJson(package, prettyPrint: true);

            // JsonUtility writes non-finite floats (NaN, Infinity, -Infinity) as bare
            // tokens — which are NOT valid JSON. Unity itself parses them in the editor
            // but the WebGL JsonUtility throws "JSON parse error: Invalid value." at runtime.
            // Sanitize to 0 so the file is universally parseable; warn so the source data
            // gets fixed rather than silently masked forever.
            int sanitizedCount;
            mergedJson = SanitizeNonFiniteFloats(mergedJson, out sanitizedCount);
            if (sanitizedCount > 0)
                OseLog.Warn($"[OSE] Split-layout merge: scrubbed {sanitizedCount} non-finite float(s) (NaN/Infinity) " +
                            $"from '{packageFolder}'. Locate the source values via Package Health and replace them.");

            // Round-trip verification: if the merged output can't be re-parsed, fail the
            // build with the offending excerpt instead of shipping a broken machine.json.
            VerifyRoundTripParseable(mergedJson, machineJsonPath);

            // CRITICAL: write WITHOUT a UTF-8 BOM. Editor JsonUtility tolerates the
            // 0xEF 0xBB 0xBF prefix; WebGL JsonUtility rejects it as "Invalid value." at
            // position 0 — which looks indistinguishable from a corrupt file in the log.
            // Encoding.UTF8 emits a BOM by default; UTF8Encoding(false) omits it.
            File.WriteAllText(machineJsonPath, mergedJson, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            DeleteFileAndMeta(sharedJsonPath);
            DeleteFileAndMeta(previewConfigPath);
            DeleteDirectoryAndMeta(asmFolder);
        }

        /// <summary>
        /// For every StreamingAssets package, parse its <c>machine.json</c>, bake an asset
        /// manifest derived from the AUTHORING parts folder, and write the JSON back. The
        /// runtime <see cref="OSE.Content.Loading.PackageAssetResolver"/> reads this manifest
        /// instead of scanning the filesystem — required because WebGL StreamingAssets is
        /// HTTP-served and Directory.GetFiles cannot operate there.
        /// </summary>
        private static void BakeAssetManifestsInStreamingAssets()
        {
            string streamingRoot = Path.GetFullPath(StreamingRoot);
            if (!Directory.Exists(streamingRoot)) return;

            foreach (string pkgDir in Directory.GetDirectories(streamingRoot))
            {
                string machineJsonPath = Path.Combine(pkgDir, MachineJsonFileName);
                if (!File.Exists(machineJsonPath)) continue;

                string json = File.ReadAllText(machineJsonPath);
                var package = JsonUtility.FromJson<MachinePackageDefinition>(json);
                if (package == null)
                {
                    OseLog.Warn($"[OSE] BakeAssetManifest: failed to parse '{machineJsonPath}'.");
                    continue;
                }

                string packageId = Path.GetFileName(pkgDir.TrimEnd(Path.DirectorySeparatorChar, '/'));
                if (!BakeAssetManifestInto(packageId, package))
                    continue;

                string updated = JsonUtility.ToJson(package, prettyPrint: true);
                int sanitized;
                updated = SanitizeNonFiniteFloats(updated, out sanitized);
                VerifyRoundTripParseable(updated, machineJsonPath);
                File.WriteAllText(machineJsonPath, updated, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        /// <summary>
        /// Populates <paramref name="package"/>.assetManifest with the GLB
        /// relative paths and child-node indices from BOTH the AUTHORING
        /// <c>assets/parts/</c> and <c>assets/tools/</c> folders. Storing
        /// the FULL relative path (e.g. <c>assets/parts/frame_001.glb</c>,
        /// not just <c>frame_001.glb</c>) is critical: the runtime asset
        /// source composes the fetch URL as
        /// <c>streamingAssetsPath/&lt;pkg&gt;/&lt;ref&gt;</c>, so a bare
        /// filename produces a URL pointing at the package root instead of
        /// at the actual file location. The editor's AssetDatabase fallback
        /// path masks this in-editor; WebGL has no such fallback.
        /// Returns false when neither folder exists for the package.
        /// </summary>
        private static bool BakeAssetManifestInto(string packageId, MachinePackageDefinition package)
        {
            string pkgRoot     = Path.Combine(Application.dataPath, "_Project", "Data", "Packages", packageId);
            string partsDir    = Path.Combine(pkgRoot, "assets", "parts");
            string toolsDir    = Path.Combine(pkgRoot, "assets", "tools");
            bool partsPresent  = Directory.Exists(partsDir);
            bool toolsPresent  = Directory.Exists(toolsDir);

            if (!partsPresent && !toolsPresent)
            {
                OseLog.Warn($"[OSE] BakeAssetManifest: '{packageId}' has neither assets/parts/ nor " +
                            $"assets/tools/ — runtime asset resolution will fail in builds.");
                return false;
            }

            var modelRefs = new List<string>();
            var nodes     = new List<AssetNodeIndex>();

            // Walks both folders, storing entries as relative paths
            // ("assets/<sub>/<file>") so the runtime URL composer can
            // resolve them without a fallback search.
            void HarvestFolder(string folderPath, string subKind)
            {
                if (!Directory.Exists(folderPath)) return;
                foreach (string filePath in Directory.GetFiles(folderPath).OrderBy(f => f))
                {
                    string ext = Path.GetExtension(filePath);
                    if (!ext.Equals(".glb", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string filename    = Path.GetFileName(filePath);
                    string relativeRef = $"assets/{subKind}/{filename}";
                    modelRefs.Add(relativeRef);

                    string[] nodeNames = LoadGlbChildNodeNames(filePath);
                    if (nodeNames != null && nodeNames.Length > 0)
                        nodes.Add(new AssetNodeIndex { file = relativeRef, nodeNames = nodeNames });
                }
            }
            HarvestFolder(partsDir, "parts");
            HarvestFolder(toolsDir, "tools");

            if (package.assetManifest == null)
                package.assetManifest = new AssetManifestDefinition();

            // Preserve any author-managed fields (textureRefs/effectRefs/uiRefs) — only
            // overwrite the build-generated parts.
            package.assetManifest.modelRefs = modelRefs.ToArray();
            package.assetManifest.nodes     = nodes.ToArray();

            OseLog.Info($"[OSE] BakeAssetManifest: {packageId} → {modelRefs.Count} GLB(s) " +
                        $"(parts={Directory.Exists(partsDir)}, tools={Directory.Exists(toolsDir)}), " +
                        $"{nodes.Count} with node indices.");
            return true;
        }

        /// <summary>
        /// Loads the GLB at <paramref name="absolutePath"/> via AssetDatabase and returns
        /// every direct child transform name. Used to populate the manifest at build time.
        /// Editor-only — AssetDatabase doesn't exist in player builds.
        /// </summary>
        private static string[] LoadGlbChildNodeNames(string absolutePath)
        {
            string dataPath = Application.dataPath;
            if (!absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            string assetPath = "Assets" + absolutePath.Substring(dataPath.Length).Replace('\\', '/');
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is GameObject go) { prefab = go; break; }
                }
            }
            if (prefab == null) return Array.Empty<string>();

            var names = new List<string>();
            CollectChildNodeNames(prefab.transform, names);
            return names.ToArray();
        }

        private static void CollectChildNodeNames(Transform t, List<string> names)
        {
            foreach (Transform child in t)
            {
                if (!string.IsNullOrWhiteSpace(child.name))
                    names.Add(child.name);
                CollectChildNodeNames(child, names);
            }
        }

        // Match a bare NaN / Infinity / -Infinity token surrounded by JSON-structural chars
        // ([ , : whitespace) so we don't touch substrings inside user-facing strings.
        private static readonly System.Text.RegularExpressions.Regex NonFiniteFloatRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?<=[\s:,\[])-?(?:NaN|Infinity)(?=[\s,\]\}])",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string SanitizeNonFiniteFloats(string json, out int count)
        {
            int local = 0;
            string result = NonFiniteFloatRegex.Replace(json, _ => { local++; return "0.0"; });
            count = local;
            return result;
        }

        private static void VerifyRoundTripParseable(string json, string targetPath)
        {
            try
            {
                var verify = JsonUtility.FromJson<MachinePackageDefinition>(json);
                if (verify == null)
                    throw new InvalidOperationException("FromJson returned null.");
            }
            catch (Exception ex)
            {
                // Persist the offending JSON next to the target so the author can grep it.
                string dumpPath = targetPath + ".invalid.txt";
                try { File.WriteAllText(dumpPath, json, System.Text.Encoding.UTF8); } catch { }
                throw new BuildFailedException(
                    $"[OSE] Split-layout merge produced UNPARSEABLE JSON for '{targetPath}': {ex.Message}. " +
                    $"Offending output written to '{dumpPath}'. Fix the source authoring data; do not ship.");
            }
        }

        private static T[] MergeArrays<T>(T[] a, T[] b)
        {
            bool aEmpty = a == null || a.Length == 0;
            bool bEmpty = b == null || b.Length == 0;
            if (aEmpty) return bEmpty ? a : b;
            if (bEmpty) return a;
            var result = new T[a.Length + b.Length];
            Array.Copy(a, 0, result, 0,        a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }

        private static void DeleteFileAndMeta(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            string meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        private static void DeleteDirectoryAndMeta(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            string meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
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
            // Use inequality (not >) so older-than-dest source mtimes (git restore, branch swap)
            // also trigger a re-copy. After copy we stamp dest mtime = source mtime, so steady-state
            // syncs are no-ops.
            return s.LastWriteTimeUtc != d.LastWriteTimeUtc || s.Length != d.Length;
        }

        private static void PruneOrphans(string destRoot, HashSet<string> liveDestPaths)
        {
            if (!Directory.Exists(destRoot)) return;

            foreach (string existing in Directory.EnumerateFiles(destRoot, "*.*", SearchOption.AllDirectories))
            {
                if (!IsRuntimeFile(existing)) continue; // never touch .meta or non-runtime files
                if (liveDestPaths.Contains(Path.GetFullPath(existing))) continue;

                File.Delete(existing);
                string meta = existing + ".meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
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
            OseLog.Info("[OSE] Pre-build: baking missing asset refs...");
            (int baked, int warned) = PackageSyncTool.BakeAllAssetRefs();
            if (baked > 0)
                OseLog.Info($"[OSE] Pre-build: baked {baked} asset ref(s).");
            if (warned > 0)
                OseLog.Warn($"[OSE] Pre-build: {warned} part(s) still have no resolvable GLB. " +
                                 "Assign assetRef manually in Assembly Step Authoring, or add the GLB file.");

            // Step 2 — sync authoring folder → StreamingAssets (now with baked refs)
            OseLog.Info("[OSE] Pre-build: syncing machine packages to StreamingAssets...");
            int copied = PackageSyncTool.Sync();
            AssetDatabase.Refresh();
            OseLog.Info($"[OSE] Pre-build sync complete. {copied} file(s) updated.");
        }
    }
}
