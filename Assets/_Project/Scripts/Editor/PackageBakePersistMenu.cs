using System.IO;
using OSE.Content;
using OSE.Content.Loading;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// One-shot maintenance utility: re-runs <see cref="MachinePackageNormalizer"/>
    /// against an authoring package and persists the resulting
    /// <see cref="PackagePreviewConfig"/> back to <c>preview_config.json</c> on disk.
    ///
    /// <para><b>Why this exists.</b> The runtime relies on
    /// <c>BakeStagingPoses</c> (inside <c>Normalize</c>) to synthesize a
    /// <c>partPlacements</c> entry for every part with a <c>stagingPose</c>.
    /// When the on-disk JSON is incomplete (missing entries the bake would
    /// produce), the runtime works because Normalize fills the gap in memory
    /// — but any other code path that reads <c>previewConfig.partPlacements</c>
    /// before Normalize runs (or that uses the disk file directly) sees the
    /// stale shape and produces wrong behavior. Symptoms: parts not rendering
    /// at certain steps, FocusBounds reporting <c>parts=0/N</c>, "Revealed N"
    /// being much smaller than expected. This bug class has hit the project
    /// at least 4 times.</para>
    ///
    /// <para><b>The fix.</b> Run this menu once per package. It loads via the
    /// authoring path, runs <c>Normalize</c>, and writes the post-bake
    /// <c>previewConfig</c> back to <c>preview_config.json</c>. Now the disk
    /// matches what the bake produces — any future load is correct from the
    /// first byte read.</para>
    ///
    /// <para><b>Safe to re-run.</b> Idempotent: if the disk is already in sync
    /// with the bake, the file isn't rewritten. The log line tells you which
    /// packages needed updating and which didn't.</para>
    /// </summary>
    internal static class PackageBakePersistMenu
    {
        private const string AuthoringRoot = "Assets/_Project/Data/Packages";

        [MenuItem("OSE/Package/Persist Bake to Disk")]
        public static void PersistBakeForAllPackages()
        {
            string rootAbs = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(rootAbs))
            {
                Debug.LogWarning($"[PersistBake] Authoring root not found: {AuthoringRoot}");
                return;
            }

            int totalPackages = 0;
            int updated = 0;
            int alreadyInSync = 0;

            foreach (string dir in Directory.GetDirectories(rootAbs))
            {
                string packageId = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, "machine.json"))) continue;
                totalPackages++;

                if (PersistBakeForPackage(packageId)) updated++;
                else alreadyInSync++;
            }

            if (totalPackages == 0)
                Debug.LogWarning($"[PersistBake] No packages found under {AuthoringRoot}.");
            else
                Debug.Log($"[PersistBake] Done. Packages: {totalPackages}, updated: {updated}, already in sync: {alreadyInSync}.");

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Loads <paramref name="packageId"/>, runs Normalize, and writes the
        /// post-bake <c>previewConfig</c> back if the placement count grew.
        /// Returns true if the file was rewritten.
        /// </summary>
        private static bool PersistBakeForPackage(string packageId)
        {
            MachinePackageDefinition pkg = PackageJsonUtils.LoadPackage(packageId);
            if (pkg == null)
            {
                Debug.LogWarning($"[PersistBake] {packageId}: load failed, skipping.");
                return false;
            }

            int beforeParts   = pkg.previewConfig?.partPlacements?.Length   ?? 0;
            int beforeTargets = pkg.previewConfig?.targetPlacements?.Length ?? 0;

            MachinePackageNormalizer.Normalize(pkg);

            int afterParts   = pkg.previewConfig?.partPlacements?.Length   ?? 0;
            int afterTargets = pkg.previewConfig?.targetPlacements?.Length ?? 0;

            // The bake adds NEW partPlacement entries for any part with
            // stagingPose that didn't already have one. If the count didn't
            // grow, the disk file is already in sync with what the bake
            // produces — no rewrite needed.
            if (afterParts == beforeParts && afterTargets == beforeTargets)
            {
                Debug.Log($"[PersistBake] {packageId}: in sync (parts={afterParts}, targets={afterTargets}).");
                return false;
            }

            string previewPath = ResolvePreviewConfigPath(packageId);
            if (string.IsNullOrEmpty(previewPath))
            {
                Debug.LogWarning($"[PersistBake] {packageId}: cannot resolve preview_config.json path; skipping write.");
                return false;
            }

            // Ensure the file exists with a minimal wrapper so WritePreviewConfig
            // can do its in-place substitution. New file gets the bake on first
            // write — same outcome as appending to a real-but-empty config.
            if (!File.Exists(previewPath))
                File.WriteAllText(previewPath, "{\n  \"previewConfig\": {}\n}\n");

            PackageJsonUtils.WritePreviewConfig(previewPath, pkg.previewConfig);

            Debug.Log($"[PersistBake] {packageId}: wrote preview_config.json — parts {beforeParts}→{afterParts}, targets {beforeTargets}→{afterTargets}.");
            return true;
        }

        private static string ResolvePreviewConfigPath(string packageId)
        {
            string existing = PackageJsonUtils.GetPreviewConfigJsonPath(packageId);
            if (!string.IsNullOrEmpty(existing)) return existing;
            // Split-layout convention: <package>/preview_config.json. Build
            // the path even if the file doesn't exist yet — caller creates
            // the wrapper before WritePreviewConfig substitutes into it.
            return Path.Combine(AuthoringRoot, packageId, "preview_config.json");
        }
    }
}
