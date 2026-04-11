using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OSE.Content.Validation;
using OSE.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace OSE.Content.Loading
{
    public sealed class MachinePackageLoader : IMachinePackageLoader
    {
        private const string MachinePackagesFolderName = "MachinePackages";
        private const string MachineJsonFileName        = "machine.json";
        private const string SharedJsonFileName         = "shared.json";
        private const string PreviewConfigFileName      = "preview_config.json";
        private const string AssembliesFolderName       = "assemblies";

        public async Task<MachinePackageLoadResult> LoadFromStreamingAssetsAsync(
            string packageId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return new MachinePackageLoadResult(
                    string.Empty,
                    string.Empty,
                    null,
                    MachinePackageValidationResult.Valid,
                    "Package id is required.");
            }

            string sanitizedPackageId = packageId.Trim();
            string packagePath = BuildMachineJsonPath(sanitizedPackageId);

            try
            {
                MachinePackageDefinition package;

#if UNITY_EDITOR
                // ── Split-layout detection (editor only) ─────────────────────────
                // If the package has an assemblies/ subfolder it uses the A+++ split
                // layout. Load machine.json (metadata) + shared.json + assemblies/*.json
                // + preview_config.json and merge them.  Packages without the folder
                // fall through to the single-file path below.
                string authoringFolder = BuildAuthoringPackageFolderPath(sanitizedPackageId);
                string assemblyFolder  = Path.Combine(authoringFolder, AssembliesFolderName);
                if (Directory.Exists(assemblyFolder))
                {
                    using (OseLog.Timed($"ReadJson({sanitizedPackageId})"))
                        package = await LoadSplitLayoutAsync(
                            sanitizedPackageId, authoringFolder, assemblyFolder, cancellationToken);
                }
                else
                {
#endif
                // ── Single-file layout (legacy, or packages not yet split) ────────
                string json;
                using (OseLog.Timed($"ReadJson({sanitizedPackageId})"))
                    json = await ReadTextAsync(packagePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                    return Failure(sanitizedPackageId, packagePath, "Package JSON was empty.");

                // Apply schema migrations before deserialization so structural
                // changes from older formats are handled transparently.
                MigrationResult migration;
                using (OseLog.Timed($"SchemaMigrate({sanitizedPackageId})"))
                    migration = PackageSchemaMigrator.Migrate(json);
                json = migration.Json;

                try
                {
                    using (OseLog.Timed($"Deserialize({sanitizedPackageId})"))
                        package = JsonUtility.FromJson<MachinePackageDefinition>(json);
                }
                catch (ArgumentException parseEx)
                {
                    string parseError = $"JSON parse error in '{packagePath}': {parseEx.Message}";
                    OseLog.Error(OseErrorCode.PackageLoadFailed, $"[Content] {parseError}");
                    return Failure(sanitizedPackageId, packagePath, parseError);
                }
#if UNITY_EDITOR
                } // end single-file block
#endif

                if (package == null || package.machine == null)
                {
                    return Failure(sanitizedPackageId, packagePath, "Package JSON did not deserialize into a valid machine package.");
                }

                // Quick structural check: catch completely empty JSON (e.g. `{}` or wrong file)
                // before the normalizer runs. The full validator will catch finer-grained issues.
                if (string.IsNullOrWhiteSpace(package.machine.id))
                {
                    return Failure(sanitizedPackageId, packagePath,
                        "Package JSON is missing required field 'machine.id'. Check that the file is the correct machine.json.");
                }

                if (package.assemblies == null || package.assemblies.Length == 0)
                {
                    return Failure(sanitizedPackageId, packagePath,
                        "Package JSON has no assemblies defined. At least one assembly is required.");
                }

                // Inflate compact JSON conventions (templates, inferred parent IDs, etc.)
                using (OseLog.Timed($"Normalize({sanitizedPackageId})"))
                    MachinePackageNormalizer.Normalize(package);

                MachinePackageValidationResult validation;
                using (OseLog.Timed($"Validate({sanitizedPackageId})"))
                    validation = MachinePackageValidator.Validate(package);
                package.packageId = sanitizedPackageId;
                if (validation.HasErrors)
                {
                    OseLog.Error(OseErrorCode.PackageValidationFailed,
                        $"[Content] Package '{sanitizedPackageId}' failed validation.\n{validation.FormatSummary()}");
                    return new MachinePackageLoadResult(
                        sanitizedPackageId,
                        packagePath,
                        package,
                        validation,
                        "Package validation failed.");
                }

                if (validation.HasWarnings)
                {
                    OseLog.Warn(OseErrorCode.PackageValidationFailed,
                        $"[Content] Package '{sanitizedPackageId}' loaded with warnings.\n{validation.FormatSummary()}");
                }
                else
                {
                    OseLog.Info($"[Content] Loaded machine package '{sanitizedPackageId}' from '{packagePath}'.");
                }

                return new MachinePackageLoadResult(
                    sanitizedPackageId,
                    packagePath,
                    package,
                    validation,
                    string.Empty);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                string errorMessage = $"Failed to load package '{sanitizedPackageId}': {exception.Message}";
                OseLog.Error(OseErrorCode.PackageLoadFailed, $"[Content] {errorMessage}");
                return Failure(sanitizedPackageId, packagePath, errorMessage);
            }
        }

        public static string BuildMachineJsonPath(string packageId)
        {
#if UNITY_EDITOR
            // In the editor, read directly from the authoring folder so you never
            // need to sync to StreamingAssets just to press Play or preview content.
            string authoringPath = Path.Combine(
                Application.dataPath,
                "_Project", "Data", "Packages",
                packageId, MachineJsonFileName);
            if (File.Exists(authoringPath))
                return authoringPath;
#endif
            return Path.Combine(
                Application.streamingAssetsPath,
                MachinePackagesFolderName, packageId, MachineJsonFileName);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Returns the authoring folder root for the package
        /// (Assets/_Project/Data/Packages/{packageId}/).
        /// Editor only — in builds the merged machine.json in StreamingAssets is used.
        /// </summary>
        private static string BuildAuthoringPackageFolderPath(string packageId) =>
            Path.Combine(Application.dataPath, "_Project", "Data", "Packages", packageId);

        /// <summary>
        /// Loads a package that uses the split-layout (A+++ architecture):
        ///   machine.json        — metadata (machine, version, challengeConfig, assetManifest)
        ///   shared.json         — tools, partTemplates, global hints, global validationRules, effects
        ///   assemblies/*.json   — per-assembly content (assemblies, subassemblies, parts, steps, targets, hints)
        ///   preview_config.json — TTAW/Blender-generated positional data (previewConfig)
        ///
        /// All files are merged into a single <see cref="MachinePackageDefinition"/> before
        /// normalization and validation. The normalizer and validator remain file-agnostic.
        /// </summary>
        private static async Task<MachinePackageDefinition> LoadSplitLayoutAsync(
            string packageId,
            string packageFolder,
            string assemblyFolder,
            CancellationToken cancellationToken)
        {
            // ── 1. machine.json — metadata ────────────────────────────────────
            string machineJsonPath = Path.Combine(packageFolder, MachineJsonFileName);
            string machineJson     = await File.ReadAllTextAsync(machineJsonPath, cancellationToken);
            var package            = JsonUtility.FromJson<MachinePackageDefinition>(machineJson);
            if (package == null)
                throw new InvalidOperationException($"Failed to parse machine.json in split-layout package '{packageId}'.");

            // ── 2. shared.json — tools, partTemplates, global hints, etc. ─────
            string sharedJsonPath = Path.Combine(packageFolder, SharedJsonFileName);
            if (File.Exists(sharedJsonPath))
            {
                string sharedJson = await File.ReadAllTextAsync(sharedJsonPath, cancellationToken);
                var shared        = JsonUtility.FromJson<MachinePackageDefinition>(sharedJson);
                if (shared != null)
                {
                    package.tools          = shared.tools          ?? package.tools;
                    package.partTemplates  = shared.partTemplates  ?? package.partTemplates;
                    package.validationRules= MergeArrays(package.validationRules, shared.validationRules);
                    package.effects        = MergeArrays(package.effects,         shared.effects);
                    package.hints          = MergeArrays(package.hints,           shared.hints);
                    if (package.challengeConfig == null && shared.challengeConfig != null)
                        package.challengeConfig = shared.challengeConfig;
                }
            }

            // ── 3. assemblies/*.json — per-assembly content ───────────────────
            string[] assemblyFiles = Directory.GetFiles(assemblyFolder, "*.json")
                                              .OrderBy(f => f)
                                              .ToArray();
            foreach (string asmFile in assemblyFiles)
            {
                string asmJson   = await File.ReadAllTextAsync(asmFile, cancellationToken);
                var    asmChunk  = JsonUtility.FromJson<MachinePackageDefinition>(asmJson);
                if (asmChunk == null) continue;

                package.assemblies    = MergeArrays(package.assemblies,    asmChunk.assemblies);
                package.subassemblies = MergeArrays(package.subassemblies, asmChunk.subassemblies);
                package.parts         = MergeArrays(package.parts,         asmChunk.parts);
                package.steps         = MergeArrays(package.steps,         asmChunk.steps);
                package.targets       = MergeArrays(package.targets,       asmChunk.targets);
                package.hints         = MergeArrays(package.hints,         asmChunk.hints);
                package.validationRules = MergeArrays(package.validationRules, asmChunk.validationRules);
            }

            // ── 4. preview_config.json — TTAW/Blender-generated positional data
            string previewConfigPath = Path.Combine(packageFolder, PreviewConfigFileName);
            if (File.Exists(previewConfigPath))
            {
                // preview_config.json is wrapped: { "previewConfig": { ... } }
                string previewJson  = await File.ReadAllTextAsync(previewConfigPath, cancellationToken);
                var    previewWrap  = JsonUtility.FromJson<MachinePackageDefinition>(previewJson);
                if (previewWrap?.previewConfig != null)
                    package.previewConfig = previewWrap.previewConfig;
            }

            return package;
        }

        /// <summary>Concatenates two nullable arrays, returning a non-null result.</summary>
        private static T[] MergeArrays<T>(T[] a, T[] b)
        {
            bool aEmpty = a == null || a.Length == 0;
            bool bEmpty = b == null || b.Length == 0;
            if (aEmpty) return bEmpty ? Array.Empty<T>() : b;
            if (bEmpty) return a;
            var result = new T[a.Length + b.Length];
            Array.Copy(a, 0, result, 0,        a.Length);
            Array.Copy(b, 0, result, a.Length, b.Length);
            return result;
        }
#endif

        private static async Task<string> ReadTextAsync(
            string path,
            CancellationToken cancellationToken)
        {
            if (RequiresUnityWebRequest(path))
            {
                return await ReadTextWithUnityWebRequestAsync(path, cancellationToken);
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Machine package JSON file was not found.", path);
            }

            return await File.ReadAllTextAsync(path, cancellationToken);
        }

        private static async Task<string> ReadTextWithUnityWebRequestAsync(
            string path,
            CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(request.error);
            }

            return request.downloadHandler.text;
        }

        private static bool RequiresUnityWebRequest(string path) =>
            path.Contains("://", StringComparison.Ordinal) ||
            path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);

        private static MachinePackageLoadResult Failure(
            string packageId,
            string path,
            string errorMessage) =>
            new MachinePackageLoadResult(
                packageId,
                path,
                null,
                MachinePackageValidationResult.Valid,
                errorMessage);
    }
}
