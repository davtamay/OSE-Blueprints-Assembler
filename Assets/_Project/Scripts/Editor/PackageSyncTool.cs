using System.IO;
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
            Debug.Log("[OSE] Pre-build: syncing machine packages to StreamingAssets...");
            int copied = PackageSyncTool.Sync();
            AssetDatabase.Refresh();
            Debug.Log($"[OSE] Pre-build sync complete. {copied} file(s) updated.");
        }
    }
}
