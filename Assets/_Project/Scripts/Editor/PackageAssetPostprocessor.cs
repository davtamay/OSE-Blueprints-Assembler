using System;
using System.IO;
using OSE.Content;
using OSE.Core;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Monitors Assets/_Project/Data/Packages/ for newly imported 3D model files.
    ///
    /// Blender authoring workflow:
    ///   1. In Blender, create a "layout scene" with all assembly parts positioned in their
    ///      final assembled state. Name each object with the exact partId or targetId from
    ///      machine.json (e.g. "tutorial_beam", "target_beam_slot").
    ///   2. Export as GLB or FBX and drop it into any sub-folder under the package folder
    ///      (e.g. Assets/_Project/Data/Packages/onboarding_tutorial/assets/parts/).
    ///   3. Unity imports the file. This postprocessor detects it, traverses its node
    ///      hierarchy, matches node names to partIds/targetIds, and writes the assembled
    ///      transforms into the package's machine.json previewConfig automatically.
    ///
    /// Node naming convention (case-sensitive):
    ///   - Part assembly position → name the node with the part's partId
    ///   - Target marker position → name the node with the target's targetId
    ///
    /// Nodes that don't match any known id are silently ignored.
    /// Only previewConfig is modified; all other machine.json data is untouched.
    /// </summary>
    public sealed class PackageAssetPostprocessor : AssetPostprocessor
    {
        private const string PackagesDataPath = "Assets/_Project/Data/Packages";

        // ── FBX / OBJ / native Unity model importer path ────────────────────

        private void OnPostprocessModel(GameObject go)
        {
            if (!IsPackageModel(assetPath)) return;
            ProcessModelImport(assetPath, go);
        }

        // ── GLB / glTF path (processed by glTFast ScriptedImporter) ─────────

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAsset)
        {
            foreach (string path in importedAssets)
            {
                string ext = Path.GetExtension(path);
                if (!ext.Equals(".glb", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsPackageModel(path)) continue;

                // glTFast produces a prefab-style asset loadable as GameObject
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                    ProcessModelImport(path, go);
            }
        }

        // ── Core logic ───────────────────────────────────────────────────────

        private static bool IsPackageModel(string path) =>
            path.StartsWith(PackagesDataPath, StringComparison.OrdinalIgnoreCase);

        private static void ProcessModelImport(string modelPath, GameObject root)
        {
            // Path: Assets/_Project/Data/Packages/<packageId>/...
            string rel       = modelPath.Substring(PackagesDataPath.Length).TrimStart('/', '\\');
            string packageId = rel.Split(new[] { '/', '\\' }, 2)[0];
            if (string.IsNullOrEmpty(packageId)) return;

            string jsonPath = Path.Combine(PackagesDataPath, packageId, "machine.json");
            if (!File.Exists(jsonPath)) return;

            string rawJson  = File.ReadAllText(jsonPath);
            var    pkg      = JsonUtility.FromJson<MachinePackageDefinition>(rawJson);
            if (pkg?.previewConfig == null) return;

            bool modified = false;

            // ── Parts: assembled transforms go into playPosition/playRotation/playScale
            if (pkg.previewConfig.partPlacements != null)
            {
                foreach (var placement in pkg.previewConfig.partPlacements)
                {
                    Transform node = FindNode(root.transform, placement.partId);
                    if (node == null) continue;

                    placement.playPosition = ToFloat3(node.localPosition);
                    placement.playRotation = ToQuaternion(node.localRotation);
                    placement.playScale    = ToFloat3(node.localScale);
                    modified = true;
                    OseLog.Info(
                        $"[PackageAssetPostprocessor] '{packageId}' — updated play transform for part '{placement.partId}' from {Path.GetFileName(modelPath)}");
                }
            }

            // ── Targets: transforms go into target position/rotation/scale
            if (pkg.previewConfig.targetPlacements != null)
            {
                foreach (var placement in pkg.previewConfig.targetPlacements)
                {
                    Transform node = FindNode(root.transform, placement.targetId);
                    if (node == null) continue;

                    placement.position = ToFloat3(node.localPosition);
                    placement.rotation = ToQuaternion(node.localRotation);
                    placement.scale    = ToFloat3(node.localScale);
                    modified = true;
                    OseLog.Info(
                        $"[PackageAssetPostprocessor] '{packageId}' — updated transform for target '{placement.targetId}' from {Path.GetFileName(modelPath)}");
                }
            }

            if (modified)
            {
                PackageJsonUtils.WritePreviewConfig(jsonPath, pkg.previewConfig);
                OseLog.Info($"[PackageAssetPostprocessor] machine.json updated for package '{packageId}'.");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Transform FindNode(Transform t, string name)
        {
            if (t.name.Equals(name, StringComparison.Ordinal)) return t;
            foreach (Transform child in t)
            {
                var found = FindNode(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static SceneFloat3     ToFloat3(Vector3 v)     => PackageJsonUtils.ToFloat3(v);
        private static SceneQuaternion ToQuaternion(Quaternion q) => PackageJsonUtils.ToQuaternion(q);
    }
}
