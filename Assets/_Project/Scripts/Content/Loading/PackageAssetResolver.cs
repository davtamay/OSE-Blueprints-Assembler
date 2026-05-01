using System;
using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Resolution result for a single part's model asset.
    /// </summary>
    public readonly struct AssetResolution
    {
        /// <summary>Relative path to the GLB/glTF file within the package (e.g. "assets/parts/frame_approved.glb").</summary>
        public readonly string AssetPath;

        /// <summary>
        /// Name of the child node inside the GLB that contains this part's mesh.
        /// Null when the entire GLB represents one part (individual file).
        /// </summary>
        public readonly string NodeName;

        /// <summary>True when the part was successfully resolved to a file.</summary>
        public readonly bool IsResolved;

        public AssetResolution(string assetPath, string nodeName = null)
        {
            AssetPath = assetPath;
            NodeName = nodeName;
            IsResolved = !string.IsNullOrEmpty(assetPath);
        }

        public static AssetResolution Unresolved => default;

        /// <summary>True when this part lives inside a multi-part combined GLB.</summary>
        public bool IsNodeInCombined => NodeName != null;
    }

    /// <summary>
    /// Scans a package's parts folder and builds a resolution catalog mapping
    /// part IDs from <c>machine.json</c> to GLB/glTF files on disk.
    ///
    /// Two passes:
    /// <list type="number">
    ///   <item><b>Filename match</b> — finds individual GLBs whose stem (after stripping
    ///     <c>_approved</c> / <c>_mesh</c>) equals the part ID. No file loading.</item>
    ///   <item><b>Node search</b> — for every still-unresolved part, walks the node
    ///     hierarchy of each GLB looking for a child whose name matches. Each GLB is
    ///     inspected at most once (results are cached).</item>
    /// </list>
    /// </summary>
    public sealed class PackageAssetResolver
    {
        private readonly Dictionary<string, AssetResolution> _catalog =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _unresolvedParts = new();

        /// <summary>Part IDs that could not be resolved to any GLB file or node.</summary>
        public IReadOnlyList<string> UnresolvedParts => _unresolvedParts;

        /// <summary>True when at least one required part has no matching asset.</summary>
        public bool HasUnresolved => _unresolvedParts.Count > 0;

        /// <summary>Number of parts successfully resolved.</summary>
        public int ResolvedCount => _catalog.Count;

        /// <summary>
        /// Looks up the resolution for a given part ID.
        /// Returns <see cref="AssetResolution.Unresolved"/> if not in the catalog.
        /// </summary>
        public AssetResolution Resolve(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return AssetResolution.Unresolved;
            return _catalog.TryGetValue(partId, out var res) ? res : AssetResolution.Unresolved;
        }

        /// <summary>
        /// Builds the resolution catalog by scanning the parts folder for GLB/glTF files
        /// and matching them to the required part IDs from <paramref name="parts"/>.
        /// </summary>
        public void BuildCatalog(
            string packageId,
            PartDefinition[] parts,
            string partsSubfolder = "assets/parts")
        {
            BuildCatalog(packageId, parts, manifest: null, partsSubfolder);
        }

        /// <summary>
        /// Manifest-driven catalog build. When <paramref name="manifest"/> is non-null
        /// and populated, file enumeration and node-name discovery come from the manifest
        /// instead of <see cref="Directory.GetFiles"/> + <see cref="InspectGlbNodes"/>.
        /// This is the runtime path — WebGL's StreamingAssets is HTTP-served and cannot
        /// be enumerated as a filesystem.
        /// Editor callers that pass <c>null</c> get the legacy live-scan behavior.
        /// </summary>
        public void BuildCatalog(
            string packageId,
            PartDefinition[] parts,
            AssetManifestDefinition manifest,
            string partsSubfolder = "assets/parts")
        {
            _catalog.Clear();
            _unresolvedParts.Clear();
            if (parts == null || parts.Length == 0) return;

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p.id)) continue;
                // assetRef == "" (empty string, not null) means the part is procedurally
                // generated (e.g. spline wires) and intentionally has no GLB asset.
                // Skip it so it doesn't appear as an unresolved warning.
                if (p.assetRef != null && p.assetRef.Length == 0) continue;
                required.Add(p.id);
            }

            if (required.Count == 0) return;

            // Build (filename → absolute-path-or-virtual-key) and per-file node-name lookups.
            // Manifest path: paths are virtual keys ("filename"); Pass 2 reads node names
            // from the manifest dictionary, never opens a file.
            // Live-scan path: paths are absolute, Pass 2 calls InspectGlbNodes per file.
            var glbFiles = new List<string>();
            var stemToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nameToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> manifestNodes = null;

            // Editor: ALWAYS use live AssetDatabase scan — it's ground truth, and an
            // older or hand-curated assetManifest in authoring/machine.json may be stale
            // or partial. Players have no filesystem enumeration, so manifest is the only
            // source of truth there.
#if UNITY_EDITOR
            bool useManifest = false;
#else
            bool useManifest = manifest != null
                               && manifest.modelRefs != null
                               && manifest.modelRefs.Length > 0;
#endif

            if (useManifest)
            {
                manifestNodes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in manifest.modelRefs)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string filename = Path.GetFileName(raw);
                    if (string.IsNullOrEmpty(filename)) continue;
                    if (nameToFile.ContainsKey(filename)) continue;

                    string ext = Path.GetExtension(filename);
                    if (!ext.Equals(".glb", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Store the full relative ref (e.g. "assets/parts/foo.glb")
                    // as the virtualKey. The runtime asset source composes the
                    // fetch URL as `streamingAssetsPath/<pkg>/<virtualKey>`, so
                    // a bare filename would point at the package root instead
                    // of the actual file location. Keying nameToFile + stemToFile
                    // on filename / stem (not raw) preserves the existing lookup
                    // semantics — callers that ask "give me foo.glb" still hit
                    // the right entry, but get back the path-aware virtualKey.
                    string virtualKey = raw;
                    glbFiles.Add(virtualKey);
                    nameToFile[filename] = virtualKey;
                    string stem = NormalizeToPartId(Path.GetFileNameWithoutExtension(filename));
                    if (!stemToFile.ContainsKey(stem))
                        stemToFile[stem] = virtualKey;
                }

                if (manifest.nodes != null)
                {
                    foreach (var entry in manifest.nodes)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.file) || entry.nodeNames == null)
                            continue;
                        string filename = Path.GetFileName(entry.file);
                        var list = new List<string>(entry.nodeNames.Length);
                        foreach (string n in entry.nodeNames)
                            if (!string.IsNullOrWhiteSpace(n)) list.Add(n);
                        manifestNodes[filename] = list;
                    }
                }
            }
            else
            {
                string partsDir = GetPartsDirectory(packageId, partsSubfolder);
                if (string.IsNullOrEmpty(partsDir) || !Directory.Exists(partsDir))
                {
                    foreach (string id in required)
                        _unresolvedParts.Add(id);
                    return;
                }

                foreach (string file in Directory.GetFiles(partsDir))
                {
                    string ext = Path.GetExtension(file);
                    if (!ext.Equals(".glb", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                        continue;

                    glbFiles.Add(file);
                    nameToFile[Path.GetFileName(file)] = file;
                    string stem = NormalizeToPartId(Path.GetFileNameWithoutExtension(file));
                    if (!stemToFile.ContainsKey(stem))
                        stemToFile[stem] = file;
                }
            }

            // Track files claimed by Pass 0/1 — these are known individual meshes
            // and are skipped in Pass 2 (no point loading them to inspect nodes).
            var individualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Pass 0: assetRef lookup ──────────────────────────────────────────
            // assetRef is a filename. Multiple parts can share one mesh (e.g. 24 frame
            // bars → one GLB). Resolves as whole-file; Pass 2 handles node extraction
            // for parts that live inside a multi-part GLB.
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p.id) || string.IsNullOrWhiteSpace(p.assetRef))
                    continue;
                if (_catalog.ContainsKey(p.id)) continue;

                string filename = Path.GetFileName(p.assetRef);
                string absolutePath = FindFileExact(nameToFile, filename);
                if (absolutePath != null)
                {
                    individualFiles.Add(absolutePath);
                    string relPath = partsSubfolder + "/" + Path.GetFileName(absolutePath);
                    _catalog[p.id] = new AssetResolution(relPath);
                }
            }

            // ── Pass 1: filename match (no file loading) ─────────────────────────
            foreach (string id in required)
            {
                if (_catalog.ContainsKey(id)) continue;
                if (stemToFile.TryGetValue(id, out string filePath))
                {
                    individualFiles.Add(filePath);
                    string relPath = partsSubfolder + "/" + Path.GetFileName(filePath);
                    _catalog[id] = new AssetResolution(relPath);
                }
            }

            // ── Pass 2: node search — only inspect unclaimed GLBs ────────────────
            var stillNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in required)
                if (!_catalog.ContainsKey(id))
                    stillNeeded.Add(id);

            if (stillNeeded.Count > 0)
            {
                foreach (string filePath in glbFiles)
                {
                    if (stillNeeded.Count == 0) break;
                    if (individualFiles.Contains(filePath)) continue;

                    string filename = Path.GetFileName(filePath);

                    List<string> nodeNames;
                    if (manifestNodes != null)
                    {
                        // Manifest path: O(1) lookup, no file I/O.
                        if (!manifestNodes.TryGetValue(filename, out nodeNames) || nodeNames == null)
                            continue;
                    }
                    else
                    {
                        // Live editor scan: open the GLB and walk the transform hierarchy.
                        nodeNames = InspectGlbNodes(filePath);
                        if (nodeNames == null || nodeNames.Count == 0) continue;
                    }

                    string relPath = partsSubfolder + "/" + filename;

                    foreach (string nodeName in nodeNames)
                    {
                        string candidateId = NormalizeToPartId(nodeName);
                        if (stillNeeded.Contains(candidateId) && !_catalog.ContainsKey(candidateId))
                        {
                            _catalog[candidateId] = new AssetResolution(relPath, nodeName);
                            stillNeeded.Remove(candidateId);
                        }
                    }
                }
            }

            // ── Collect final unresolved ─────────────────────────────────────────
            foreach (string id in required)
                if (!_catalog.ContainsKey(id))
                    _unresolvedParts.Add(id);
        }

        /// <summary>
        /// Logs all unresolved parts as errors so the author knows exactly which
        /// model files are missing.
        /// </summary>
        public void LogUnresolved(string packageId)
        {
            if (_unresolvedParts.Count == 0) return;

            string list = string.Join("\n  - ", _unresolvedParts);
            OseLog.Error(OseErrorCode.PackageLoadFailed,
                $"[PackageAssetResolver] Package '{packageId}': {_unresolvedParts.Count} part(s) " +
                $"have no matching GLB/glTF file or node in the assets/parts folder:\n  - {list}\n" +
                "Each part needs either an individual file (e.g. '{partId}_approved.glb') or " +
                "a named node inside a combined GLB.");
        }

        // ── File variant matching ────────────────────────────────────────────

        /// <summary>
        /// Finds a file in the parts-directory lookup by exact name.
        /// No variant guessing — assetRef must match an actual filename.
        /// </summary>
        private static string FindFileExact(
            Dictionary<string, string> nameToFile,
            string filename)
        {
            if (string.IsNullOrEmpty(filename)) return null;
            return nameToFile.TryGetValue(filename, out string path) ? path : null;
        }

        // ── Naming convention ────────────────────────────────────────────────

        /// <summary>
        /// Strips common suffixes to recover the canonical part ID.
        /// <c>cable_chain_approved</c> → <c>cable_chain</c>,
        /// <c>cable_chain_mesh</c> → <c>cable_chain</c>,
        /// <c>cable_chain_approved_mesh</c> → <c>cable_chain</c>.
        /// </summary>
        public static string NormalizeToPartId(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - "_mesh".Length);
            if (name.EndsWith("_approved", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - "_approved".Length);
            return name;
        }

        // ── Directory resolution ─────────────────────────────────────────────

        private static string GetPartsDirectory(string packageId, string partsSubfolder)
        {
#if UNITY_EDITOR
            string authoringDir = Path.Combine(
                Application.dataPath,
                "_Project", "Data", "Packages",
                packageId, partsSubfolder.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(authoringDir))
                return authoringDir;
#endif
            return Path.Combine(
                Application.streamingAssetsPath,
                "MachinePackages", packageId,
                partsSubfolder.Replace('/', Path.DirectorySeparatorChar));
        }

        // ── GLB node inspection ──────────────────────────────────────────────

        private static List<string> InspectGlbNodes(string absolutePath)
        {
#if UNITY_EDITOR
            return InspectGlbNodesEditor(absolutePath);
#else
            return null;
#endif
        }

#if UNITY_EDITOR
        private static List<string> InspectGlbNodesEditor(string absolutePath)
        {
            string dataPath = Application.dataPath;
            if (!absolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            string assetPath = "Assets" + absolutePath.Substring(dataPath.Length).Replace('\\', '/');
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab == null)
            {
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath))
                {
                    if (asset is GameObject go) { prefab = go; break; }
                }
            }

            if (prefab == null) return null;

            var names = new List<string>();
            CollectNodeNames(prefab.transform, names);
            return names;
        }

        private static void CollectNodeNames(Transform t, List<string> names)
        {
            foreach (Transform child in t)
            {
                if (!string.IsNullOrWhiteSpace(child.name))
                    names.Add(child.name);
                CollectNodeNames(child, names);
            }
        }
#endif
    }
}
