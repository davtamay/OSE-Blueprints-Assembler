using System;
using System.IO;
using System.Collections.Generic;
using GLTFast.Export;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Generates minimal dummy .glb stubs for every part assetRef referenced in each
    /// machine package's machine.json. These are Unity primitive shapes (Cube, Capsule,
    /// Cylinder) that give the harness something real to load and render until proper
    /// modelled assets are dropped into the package.
    ///
    /// Shape assignment by keywords in the part id:
    ///   beam, bar, rail, tube, rod  → Capsule
    ///   bolt, screw, pin, shaft     → Cylinder
    ///   plate, panel, sheet, frame  → flat Cube (y-scale 0.06)
    ///   everything else             → Cube
    ///
    /// Usage:   OSE → Generate Dummy Part Meshes
    ///
    /// Existing files are not overwritten (skip), so you can delete a stub and
    /// re-run to regenerate just that one without touching the others.
    /// After generation, AssetDatabase is refreshed so glTFast imports all new files.
    /// </summary>
    public static class PackageDummyMeshGenerator
    {
        private const string AuthoringRoot = "Assets/_Project/Data/Packages";

        private static readonly (string keyword, PrimitiveType shape)[] ShapeRules =
        {
            ("beam",  PrimitiveType.Capsule),
            ("bar",   PrimitiveType.Capsule),
            ("rail",  PrimitiveType.Capsule),
            ("tube",  PrimitiveType.Capsule),
            ("rod",   PrimitiveType.Cylinder),
            ("bolt",  PrimitiveType.Cylinder),
            ("screw", PrimitiveType.Cylinder),
            ("pin",   PrimitiveType.Cylinder),
            ("shaft", PrimitiveType.Cylinder),
            ("sphere", PrimitiveType.Sphere),
        };

        [MenuItem("OSE/Generate Dummy Part Meshes")]
        private static async void GenerateDummyMeshes()
        {
            string fullRoot = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(fullRoot))
            {
                Debug.LogError($"[PackageDummyMeshGenerator] Authoring root not found: {fullRoot}");
                return;
            }

            int generated = 0;
            int skipped   = 0;
            var tempObjects = new List<GameObject>();

            try
            {
                EditorUtility.DisplayProgressBar("Generating Dummy Part Meshes", "Reading packages...", 0f);

                var packageDirs = Directory.GetDirectories(fullRoot);

                for (int pi = 0; pi < packageDirs.Length; pi++)
                {
                    string packageDir = packageDirs[pi];
                    string packageId  = Path.GetFileName(packageDir);
                    string jsonPath   = Path.Combine(packageDir, "machine.json");

                    if (!File.Exists(jsonPath)) continue;

                    float progress = (float)pi / packageDirs.Length;
                    EditorUtility.DisplayProgressBar(
                        "Generating Dummy Part Meshes",
                        $"Package: {packageId}", progress);

                    var pkg = JsonUtility.FromJson<MachinePackageDefinition>(
                        File.ReadAllText(jsonPath));

                    if (pkg?.parts == null) continue;

                    // Collect unique assetRef paths across all parts
                    var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in pkg.parts)
                    {
                        if (!string.IsNullOrWhiteSpace(part.assetRef)) refs.Add(part.assetRef);
                    }

                    foreach (string assetRef in refs)
                    {
                        string outPath = Path.Combine(packageDir, assetRef.Replace('/', Path.DirectorySeparatorChar));

                        if (File.Exists(outPath))
                        {
                            skipped++;
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                        // Determine which part this ref belongs to (for shape choice)
                        string partId = assetRef;
                        foreach (var part in pkg.parts)
                        {
                            if (string.Equals(part.assetRef, assetRef, StringComparison.OrdinalIgnoreCase))
                            {
                                partId = part.id;
                                break;
                            }
                        }

                        PrimitiveType shape = PickShape(partId);

                        // Create the primitive, export, destroy
                        GameObject go = GameObject.CreatePrimitive(shape);
                        go.name = Path.GetFileNameWithoutExtension(assetRef);
                        // Remove the collider — not needed in GLB
                        foreach (var col in go.GetComponents<Collider>())
                            UnityEngine.Object.DestroyImmediate(col);

                        tempObjects.Add(go);

                        var export = new GameObjectExport();
                        export.AddScene(new[] { go });
                        bool ok = await export.SaveToFileAndDispose(outPath);

                        if (ok)
                        {
                            generated++;
                            Debug.Log($"[PackageDummyMeshGenerator] Generated: {outPath}");
                        }
                        else
                        {
                            Debug.LogError($"[PackageDummyMeshGenerator] Export failed: {outPath}");
                        }

                        // Destroy immediately after export
                        tempObjects.Remove(go);
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }

                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog(
                    "Dummy Mesh Generation",
                    $"Done.\n  Generated: {generated}\n  Skipped (already existed): {skipped}",
                    "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                // Clean up any dangling temp objects
                foreach (var t in tempObjects)
                    if (t != null) UnityEngine.Object.DestroyImmediate(t);
                Debug.LogError($"[PackageDummyMeshGenerator] Error: {ex}");
            }
        }

        private static PrimitiveType PickShape(string partId)
        {
            string lower = (partId ?? string.Empty).ToLowerInvariant();
            foreach (var (keyword, shape) in ShapeRules)
            {
                if (lower.Contains(keyword)) return shape;
            }

            // flat plate heuristics
            if (lower.Contains("plate") || lower.Contains("panel") ||
                lower.Contains("sheet") || lower.Contains("frame"))
            {
                return PrimitiveType.Cube; // will be scaled flat by previewConfig
            }

            return PrimitiveType.Cube;
        }
    }
}
