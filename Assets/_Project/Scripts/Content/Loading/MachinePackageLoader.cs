using System;
using System.IO;
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
        private const string MachineJsonFileName = "machine.json";

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
                string json;
                using (OseLog.Timed($"ReadJson({sanitizedPackageId})"))
                    json = await ReadTextAsync(packagePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Failure(sanitizedPackageId, packagePath, "Package JSON was empty.");
                }

                // Apply schema migrations before deserialization so structural
                // changes from older formats are handled transparently.
                MigrationResult migration;
                using (OseLog.Timed($"SchemaMigrate({sanitizedPackageId})"))
                    migration = PackageSchemaMigrator.Migrate(json);
                json = migration.Json;

                MachinePackageDefinition package;
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
