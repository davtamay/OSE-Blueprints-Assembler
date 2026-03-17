using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Generates minimal dummy .glb stubs for every part/tool assetRef referenced in each
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
        private const string GameObjectExportTypeName = "GLTFast.Export.GameObjectExport";

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

        [MenuItem("OSE/Generate Dummy Package Meshes (Parts + Tools)")]
        [MenuItem("OSE/Generate Dummy Part Meshes")]
        private static async void GenerateDummyMeshes()
        {
            string fullRoot = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(fullRoot))
            {
                Debug.LogError($"[PackageDummyMeshGenerator] Authoring root not found: {fullRoot}");
                return;
            }

            if (!TryResolveGltfExportApi(out var exportType, out var addSceneMethod, out var saveMethod))
            {
                const string message =
                    "[PackageDummyMeshGenerator] glTFast export API is unavailable. " +
                    "Install the glTFast package to generate .glb dummy meshes.";
                Debug.LogError(message);
                EditorUtility.DisplayDialog("Dummy Mesh Generation", message, "OK");
                return;
            }

            int generated = 0;
            int skipped   = 0;
            var tempObjects = new List<GameObject>();

            try
            {
                EditorUtility.DisplayProgressBar("Generating Dummy Package Meshes", "Reading packages...", 0f);

                var packageDirs = Directory.GetDirectories(fullRoot);

                for (int pi = 0; pi < packageDirs.Length; pi++)
                {
                    string packageDir = packageDirs[pi];
                    string packageId  = Path.GetFileName(packageDir);
                    string jsonPath   = Path.Combine(packageDir, "machine.json");

                    if (!File.Exists(jsonPath)) continue;

                    float progress = (float)pi / packageDirs.Length;
                    EditorUtility.DisplayProgressBar(
                        "Generating Dummy Package Meshes",
                        $"Package: {packageId}", progress);

                    var pkg = JsonUtility.FromJson<MachinePackageDefinition>(
                        File.ReadAllText(jsonPath));

                    if (pkg == null) continue;

                    // Collect unique assetRef paths across all parts + tools
                    var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (pkg.parts != null)
                    {
                        foreach (var part in pkg.parts)
                        {
                            if (part == null ||
                                string.IsNullOrWhiteSpace(part.assetRef) ||
                                string.IsNullOrWhiteSpace(part.id))
                            {
                                continue;
                            }

                            AddAssetRef(refs, part.assetRef, part.id);
                        }
                    }

                    if (pkg.tools != null)
                    {
                        foreach (var tool in pkg.tools)
                        {
                            if (tool == null ||
                                string.IsNullOrWhiteSpace(tool.assetRef) ||
                                string.IsNullOrWhiteSpace(tool.id))
                            {
                                continue;
                            }

                            AddAssetRef(refs, tool.assetRef, tool.id);
                        }
                    }

                    foreach (var assetEntry in refs)
                    {
                        string assetRef = assetEntry.Key;
                        string outPath = Path.Combine(packageDir, assetRef.Replace('/', Path.DirectorySeparatorChar));

                        if (File.Exists(outPath))
                        {
                            skipped++;
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                        string sourceId = assetEntry.Value;
                        PrimitiveType shape = PickShape(sourceId);

                        // Create the primitive, export, destroy
                        GameObject go = GameObject.CreatePrimitive(shape);
                        go.name = Path.GetFileNameWithoutExtension(assetRef);
                        // Remove the collider — not needed in GLB
                        foreach (var col in go.GetComponents<Collider>())
                            UnityEngine.Object.DestroyImmediate(col);

                        tempObjects.Add(go);

                        bool ok = await ExportWithGltfFastAsync(
                            exportType,
                            addSceneMethod,
                            saveMethod,
                            go,
                            outPath);

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

            if (lower.Contains("wrench") || lower.Contains("spanner"))
                return PrimitiveType.Cylinder;

            if (lower.Contains("hammer") || lower.Contains("mallet"))
                return PrimitiveType.Capsule;

            if (lower.Contains("measure") || lower.Contains("square"))
                return PrimitiveType.Cube;

            // flat plate heuristics
            if (lower.Contains("plate") || lower.Contains("panel") ||
                lower.Contains("sheet") || lower.Contains("frame"))
            {
                return PrimitiveType.Cube; // will be scaled flat by previewConfig
            }

            return PrimitiveType.Cube;
        }

        private static void AddAssetRef(Dictionary<string, string> refs, string assetRef, string sourceId)
        {
            if (string.IsNullOrWhiteSpace(assetRef))
                return;

            string normalizedRef = assetRef.Trim();
            if (refs.ContainsKey(normalizedRef))
                return;

            refs[normalizedRef] = string.IsNullOrWhiteSpace(sourceId)
                ? normalizedRef
                : sourceId.Trim();
        }

        private static bool TryResolveGltfExportApi(
            out Type exportType,
            out MethodInfo addSceneMethod,
            out MethodInfo saveMethod)
        {
            exportType = null;
            addSceneMethod = null;
            saveMethod = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                exportType = assembly.GetType(GameObjectExportTypeName, throwOnError: false);
                if (exportType != null)
                    break;
            }

            if (exportType == null)
                return false;

            addSceneMethod = exportType.GetMethod(
                "AddScene",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(GameObject[]) },
                null);

            if (addSceneMethod == null)
            {
                MethodInfo[] candidates = exportType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < candidates.Length; i++)
                {
                    MethodInfo candidate = candidates[i];
                    if (!string.Equals(candidate.Name, "AddScene", StringComparison.Ordinal))
                        continue;

                    if (candidate.GetParameters().Length == 1)
                    {
                        addSceneMethod = candidate;
                        break;
                    }
                }
            }

            saveMethod = exportType.GetMethod(
                "SaveToFileAndDispose",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(string) },
                null);

            return addSceneMethod != null && saveMethod != null;
        }

        private static async Task<bool> ExportWithGltfFastAsync(
            Type exportType,
            MethodInfo addSceneMethod,
            MethodInfo saveMethod,
            GameObject sourceRoot,
            string outPath)
        {
            object exporter = null;

            try
            {
                exporter = Activator.CreateInstance(exportType);
                if (exporter == null)
                    return false;

                ParameterInfo[] addSceneParams = addSceneMethod.GetParameters();
                object[] addArgs;

                if (addSceneParams.Length == 1)
                {
                    addArgs = new object[] { new[] { sourceRoot } };
                }
                else
                {
                    Debug.LogError("[PackageDummyMeshGenerator] Unsupported AddScene signature on GLTFast exporter.");
                    return false;
                }

                addSceneMethod.Invoke(exporter, addArgs);
                object saveResult = saveMethod.Invoke(exporter, new object[] { outPath });

                if (saveResult is Task<bool> boolTask)
                    return await boolTask;

                if (saveResult is Task task)
                {
                    await task;
                    return true;
                }

                if (saveResult is bool boolResult)
                    return boolResult;

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PackageDummyMeshGenerator] glTFast export invocation failed: {ex}");
                return false;
            }
            finally
            {
                if (exporter is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }
}
