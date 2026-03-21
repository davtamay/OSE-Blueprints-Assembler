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
    public sealed class MachinePackageLoader
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
                string json = await ReadTextAsync(packagePath, cancellationToken);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Failure(sanitizedPackageId, packagePath, "Package JSON was empty.");
                }

                MachinePackageDefinition package = JsonUtility.FromJson<MachinePackageDefinition>(json);
                if (package == null || package.machine == null)
                {
                    return Failure(sanitizedPackageId, packagePath, "Package JSON did not deserialize into a valid machine package.");
                }

                // Inflate compact JSON conventions (templates, inferred parent IDs, etc.)
                MachinePackageNormalizer.Normalize(package);

                MachinePackageValidationResult validation = MachinePackageValidator.Validate(package);
                package.packageId = sanitizedPackageId;
                if (validation.HasErrors)
                {
                    OseLog.Error($"[Content] Package '{sanitizedPackageId}' failed validation.\n{validation.FormatSummary()}");
                    return new MachinePackageLoadResult(
                        sanitizedPackageId,
                        packagePath,
                        package,
                        validation,
                        "Package validation failed.");
                }

                if (validation.HasWarnings)
                {
                    OseLog.Warn($"[Content] Package '{sanitizedPackageId}' loaded with warnings.\n{validation.FormatSummary()}");
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
                OseLog.Error($"[Content] {errorMessage}");
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
