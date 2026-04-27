using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Shared helpers for reading and writing machine.json package files from editor tooling.
    /// All file I/O is synchronous and editor-only.
    /// </summary>
    internal static class PackageJsonUtils
    {
        internal const string AuthoringRoot = "Assets/_Project/Data/Packages";

        // Matches any float literal with 5 or more decimal digits (e.g. 1.23456789, -0.00123456).
        // Replaced by the same value rounded to 4 decimal places.
        private static readonly Regex _floatPattern =
            new(@"-?\d+\.\d{5,}", RegexOptions.Compiled);

        // ── Entity → source-file map (structural, built at load) ──────────────
        //
        // WriteJson needs to know which file to modify when editing an entity.
        // The old "find the first file that textually contains '\"id\": \"<x>\"'"
        // heuristic got fooled by compact taskOrder entries
        // ({"kind":"part","id":"foo"...}) in neighbouring assembly files: the
        // heuristic returned the taskOrder file, injection landed inside a
        // TaskOrderEntry (no schema slot for stagingPose / assetRef / etc.),
        // and the author's edits silently vanished on reload.
        //
        // This map is populated at LoadPackage time by walking each chunk's
        // parts[] / targets[] / tools[] / steps[] / partGroups[] /
        // partTemplates[] arrays and recording <entityId → sourceFilePath>.
        // It is the ONE authoritative answer to "which file contains this
        // entity's definition?" — no string matching, no alphabetical order
        // dependency, no ambiguity with taskOrder refs.
        //
        // Keyed by packageId at the outer level so multiple packages can
        // coexist (editor can switch between packages without collisions).
        private static readonly Dictionary<string, Dictionary<string, string>> _entityOriginByPackage
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>
        /// Returns the absolute path of the file containing the first-class
        /// definition of <paramref name="entityId"/> in package
        /// <paramref name="packageId"/>, or <c>null</c> if the entity is
        /// unknown (never loaded, or created in-editor and not yet saved).
        /// Populated by <see cref="LoadPackage"/> — callers that need an
        /// origin-file answer MUST ensure LoadPackage ran for this package
        /// in the current editor session first.
        /// </summary>
        internal static string TryGetEntityOriginFile(string packageId, string entityId)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(entityId)) return null;
            if (!_entityOriginByPackage.TryGetValue(packageId, out var map)) return null;
            return map.TryGetValue(entityId, out string fp) ? fp : null;
        }

        // Records (entityId → sourceFilePath) for every entity found in the
        // definition-bearing arrays of a parsed chunk. Duplicate keys
        // surfaced here indicate the same id is defined in multiple files
        // — ambiguous ownership and very likely an authoring bug. Log
        // loudly so the author finds it immediately instead of hitting a
        // silent edit-loss later.
        private static void RecordEntityOrigins(
            Dictionary<string, string> map, MachinePackageDefinition chunk, string sourceFile)
        {
            if (chunk == null || map == null || string.IsNullOrEmpty(sourceFile)) return;
            RecordIds(map, chunk.parts,         p => p?.id, sourceFile, "part");
            RecordIds(map, chunk.targets,       t => t?.id, sourceFile, "target");
            RecordIds(map, chunk.tools,         t => t?.id, sourceFile, "tool");
            RecordIds(map, chunk.steps,         s => s?.id, sourceFile, "step");
            RecordIds(map, chunk.partGroups, s => s?.id, sourceFile, "partGroup");
            RecordIds(map, chunk.partTemplates, t => t?.id, sourceFile, "partTemplate");
            // Prefab instances live per-assembly; saving one needs the
            // origin file so the writer doesn't relocate the entry.
            RecordIds(map, chunk.prefabInstances, p => p?.instanceId, sourceFile, "prefabInstance");
        }

        private static void RecordIds<T>(
            Dictionary<string, string> map, T[] array, Func<T, string> idOf,
            string sourceFile, string kind)
        {
            if (array == null) return;
            for (int i = 0; i < array.Length; i++)
            {
                string id = idOf(array[i]);
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (map.TryGetValue(id, out string prior) && prior != sourceFile)
                {
                    Debug.LogError(
                        $"[PackageJsonUtils] Duplicate {kind} definition for id '{id}': " +
                        $"first seen in '{prior}', also found in '{sourceFile}'. " +
                        "Editor edits will write to the FIRST-seen file, silently dropping " +
                        "any changes intended for the other. Resolve by renaming or removing one definition.");
                    continue;
                }
                map[id] = sourceFile;
            }
        }

        /// <summary>
        /// Rounds all float literals in a JSON string to <paramref name="decimals"/> decimal places
        /// (default 4). Unity's JsonUtility writes up to 9 significant digits; 4 places gives
        /// 0.1 mm / 0.01° precision — sufficient for assembly training content.
        /// </summary>
        internal static string RoundFloatsInJson(string json, int decimals = 4)
        {
            return _floatPattern.Replace(json, m =>
            {
                if (double.TryParse(m.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v))
                    return System.Math.Round(v, decimals)
                               .ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                return m.Value;
            });
        }

        /// <summary>
        /// Within the JSON object that has <c>"id": "<paramref name="partId"/>"</c>,
        /// replaces <c>"<paramref name="key"/>": ""</c> with the new value.
        /// Only fires when the field is currently an empty string — non-empty values are
        /// left untouched so explicit authoring is never silently overwritten.
        /// Returns true when the JSON was modified.
        /// </summary>
        internal static bool SetEmptyStringField(ref string json, string partId, string key, string newValue)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(partId)) return false;

            // Locate the object by its "id" field (handles both spaced and non-spaced colons)
            int idPos = json.IndexOf($"\"id\": \"{partId}\"", StringComparison.Ordinal);
            if (idPos < 0)
                idPos = json.IndexOf($"\"id\":\"{partId}\"", StringComparison.Ordinal);
            if (idPos < 0) return false;

            // Walk back to the opening '{' of the enclosing object
            int objStart = idPos - 1;
            while (objStart >= 0 && json[objStart] != '{') objStart--;
            if (objStart < 0) return false;

            // Walk forward to the matching '}' tracking nesting depth
            int depth = 0, objEnd = -1;
            for (int i = objStart; i < json.Length; i++)
            {
                if      (json[i] == '{') depth++;
                else if (json[i] == '}') { if (--depth == 0) { objEnd = i; break; } }
            }
            if (objEnd < 0) return false;

            // Inside that object, find the field with an empty string value
            int searchLen = objEnd - objStart + 1;
            string emptySpaced    = $"\"{key}\": \"\"";
            string emptyCompact   = $"\"{key}\":\"\"";
            int fieldIdx = json.IndexOf(emptySpaced,  objStart, searchLen, StringComparison.Ordinal);
            bool usedSpaced = fieldIdx >= 0;
            if (!usedSpaced)
                fieldIdx = json.IndexOf(emptyCompact, objStart, searchLen, StringComparison.Ordinal);
            if (fieldIdx < 0) return false;  // field absent or already non-empty — leave it alone

            string escaped     = newValue.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string patternUsed = usedSpaced ? emptySpaced : emptyCompact;
            string replacement = usedSpaced
                ? $"\"{key}\": \"{escaped}\""
                : $"\"{key}\":\"{escaped}\"";

            json = json.Substring(0, fieldIdx) + replacement + json.Substring(fieldIdx + patternUsed.Length);
            return true;
        }

        /// <summary>
        /// Returns the absolute path to the authoring machine.json for a given package id,
        /// or null if the file does not exist.
        /// </summary>
        internal static string GetJsonPath(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            string path = Path.Combine(AuthoringRoot, packageId, "machine.json");
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Returns true if this package uses the split-layout architecture
        /// (an <c>assemblies/</c> subfolder exists under the authoring folder).
        /// </summary>
        internal static bool IsSplitLayout(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            return Directory.Exists(Path.Combine(AuthoringRoot, packageId, "assemblies"));
        }

        /// <summary>
        /// Returns the absolute path of the file that owns the <c>previewConfig</c> block.
        /// For split-layout packages this is <c>preview_config.json</c>;
        /// for monolithic packages it is <c>machine.json</c> (previewConfig is inline).
        /// Returns null if the file does not exist.
        /// </summary>
        internal static string GetPreviewConfigJsonPath(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            if (IsSplitLayout(packageId))
            {
                string path = Path.Combine(AuthoringRoot, packageId, "preview_config.json");
                return File.Exists(path) ? path : null;
            }
            return GetJsonPath(packageId);
        }

        /// <summary>
        /// Returns the authoring file path that contains an entity with <paramref name="entityId"/>
        /// (target, step, part, tool, hint, etc.) for the given split-layout package.
        /// Checks assembly files first, then shared.json.
        /// Returns null if not found or the package is not split-layout.
        /// For monolithic packages use <see cref="GetJsonPath"/> instead.
        /// </summary>
        internal static string FindEntityFilePath(string packageId, string entityId)
        {
            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(entityId)) return null;

            // Structural map populated at LoadPackage time — authoritative.
            // Covers every entity that has a first-class definition in the
            // package. String matching below is a fallback for entities
            // created in-editor THIS session and not yet persisted (the map
            // doesn't know about them until the first save+reload).
            string mapped = TryGetEntityOriginFile(packageId, entityId);
            if (!string.IsNullOrEmpty(mapped) && File.Exists(mapped))
                return mapped;

            string packageDir     = Path.Combine(AuthoringRoot, packageId);
            string assemblyFolder = Path.Combine(packageDir, "assemblies");
            if (!Directory.Exists(assemblyFolder)) return null;

            string needle1 = $"\"id\": \"{entityId}\"";
            string needle2 = $"\"id\":\"{entityId}\"";

            foreach (string asmFile in Directory.GetFiles(assemblyFolder, "*.json"))
            {
                string text = File.ReadAllText(asmFile);
                if (ContainsDefinitionMatch(text, needle1, needle2))
                    return asmFile;
            }

            string sharedPath = Path.Combine(packageDir, "shared.json");
            if (File.Exists(sharedPath))
            {
                string text = File.ReadAllText(sharedPath);
                if (ContainsDefinitionMatch(text, needle1, needle2))
                    return sharedPath;
            }
            return null;
        }

        /// <summary>
        /// Returns true when the <paramref name="text"/> contains an id match
        /// for the entity in a context that looks like a first-class
        /// definition — i.e. a standalone <c>"id": "..."</c> line in a
        /// pretty-printed <c>parts[]</c> / <c>targets[]</c> / <c>tools[]</c> /
        /// <c>steps[]</c> array, NOT a compact one-line taskOrder reference
        /// like <c>{"kind":"part","id":"foo",...}</c>. The <c>kind</c>
        /// keyword on the same line is the sure sign of a taskOrder entry;
        /// authoritative definitions never co-locate <c>kind</c> with
        /// <c>id</c> on the same line. Without this filter, a taskOrder
        /// reference in a neighbouring assembly file hijacks the entity →
        /// file mapping, and WriteJson's <c>stagingPose</c> injection ends
        /// up inside a TaskOrderEntry where JsonUtility silently drops it
        /// on reload (no schema slot), so per-part gizmo edits vanish after
        /// Save.
        /// </summary>
        private static bool ContainsDefinitionMatch(string text, string needle1, string needle2)
        {
            return HasDefinitionMatch(text, needle1) || HasDefinitionMatch(text, needle2);
        }

        private static bool HasDefinitionMatch(string text, string needle)
        {
            int from = 0;
            while (from < text.Length)
            {
                int idx = text.IndexOf(needle, from, StringComparison.Ordinal);
                if (idx < 0) return false;
                int lineStart = text.LastIndexOf('\n', idx) + 1;
                int lineEnd   = text.IndexOf('\n', idx);
                if (lineEnd < 0) lineEnd = text.Length;
                int lineLen = lineEnd - lineStart;
                if (lineLen > 0)
                {
                    // A definition line has "id": ... by itself (possibly
                    // trailing comma / brace). A taskOrder reference has
                    // "kind":"part","id":"..." on the same line — reject.
                    string line = text.Substring(lineStart, lineLen);
                    if (line.IndexOf("\"kind\"", StringComparison.Ordinal) < 0)
                        return true;
                }
                from = idx + needle.Length;
            }
            return false;
        }

        /// <summary>
        /// Deserializes the fully-merged package definition, handling both monolithic
        /// (machine.json) and split-layout (assemblies/ folder) packages.
        /// Returns null on any failure.
        /// </summary>
        internal static MachinePackageDefinition LoadPackage(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            string packageDir     = Path.Combine(AuthoringRoot, packageId);
            string assemblyFolder = Path.Combine(packageDir, "assemblies");
            if (Directory.Exists(assemblyFolder))
                return LoadSplitLayoutPackage(packageId, packageDir, assemblyFolder);

            string path = GetJsonPath(packageId);
            if (path == null) return null;
            var pkg = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(path));

            // Monolithic: every entity lives in the one file. Build the
            // origin-file map so WriteJson can still resolve deterministically.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            RecordEntityOrigins(map, pkg, path);
            _entityOriginByPackage[packageId] = map;

            return pkg;
        }

        private static MachinePackageDefinition LoadSplitLayoutPackage(
            string packageId, string packageDir, string assemblyFolder)
        {
            // Fresh map per load — stale mappings from a previous edit
            // session would point at files whose layouts may have changed.
            var originMap = new Dictionary<string, string>(StringComparer.Ordinal);

            string machinePath = Path.Combine(packageDir, "machine.json");
            string machineJson = File.ReadAllText(machinePath);
            var pkg = JsonUtility.FromJson<MachinePackageDefinition>(machineJson) ?? new MachinePackageDefinition();
            RecordEntityOrigins(originMap, pkg, machinePath);

            string sharedPath = Path.Combine(packageDir, "shared.json");
            if (File.Exists(sharedPath))
            {
                var shared = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(sharedPath));
                if (shared != null)
                {
                    pkg.tools           = shared.tools           ?? pkg.tools;
                    pkg.partTemplates   = shared.partTemplates   ?? pkg.partTemplates;
                    pkg.validationRules = MergeArrays(pkg.validationRules, shared.validationRules);
                    pkg.effects         = MergeArrays(pkg.effects,         shared.effects);
                    pkg.hints           = MergeArrays(pkg.hints,           shared.hints);
                    if (pkg.challengeConfig == null && shared.challengeConfig != null)
                        pkg.challengeConfig = shared.challengeConfig;
                    RecordEntityOrigins(originMap, shared, sharedPath);
                }
            }

            foreach (string asmFile in Directory.GetFiles(assemblyFolder, "*.json").OrderBy(f => f))
            {
                var chunk = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(asmFile));
                if (chunk == null) continue;
                pkg.assemblies      = MergeArrays(pkg.assemblies,      chunk.assemblies);
                pkg.partGroups   = MergeArrays(pkg.partGroups,   chunk.partGroups);
                pkg.parts           = MergeArrays(pkg.parts,           chunk.parts);
                pkg.steps           = MergeArrays(pkg.steps,           chunk.steps);
                pkg.prefabInstances = MergeArrays(pkg.prefabInstances, chunk.prefabInstances);
                pkg.targets         = MergeArrays(pkg.targets,         chunk.targets);
                pkg.hints           = MergeArrays(pkg.hints,           chunk.hints);
                pkg.validationRules = MergeArrays(pkg.validationRules, chunk.validationRules);
                RecordEntityOrigins(originMap, chunk, asmFile);
            }

            string previewPath = Path.Combine(packageDir, "preview_config.json");
            if (File.Exists(previewPath))
            {
                var wrap = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(previewPath));
                if (wrap?.previewConfig != null)
                    pkg.previewConfig = wrap.previewConfig;
            }

            _entityOriginByPackage[packageId] = originMap;
            return pkg;
        }

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

        /// <summary>
        /// Replaces the "previewConfig": { ... } block in the JSON file with a freshly
        /// serialized version of <paramref name="config"/>. All other JSON data is preserved
        /// by doing a targeted string substitution instead of a full round-trip.
        ///
        /// If the file has no previewConfig key yet, the block is appended before the
        /// root closing brace.
        /// </summary>
        internal static void WritePreviewConfig(string jsonPath, PackagePreviewConfig config)
        {
            string text      = File.ReadAllText(jsonPath);
            string configJson = RoundFloatsInJson(JsonUtility.ToJson(config, true));

            const string label = "\"previewConfig\"";
            int labelIdx = text.IndexOf(label, System.StringComparison.Ordinal);

            if (labelIdx < 0)
            {
                // Append before final closing brace
                int lastBrace = text.LastIndexOf('}');
                text = text.Substring(0, lastBrace)
                     + ",\n  " + label + ": " + configJson + "\n}";
            }
            else
            {
                // Find the opening { for the value
                int valueStart = text.IndexOf('{', labelIdx);
                if (valueStart < 0) return;

                // Count braces to find the matching closing }
                int depth = 0, valueEnd = valueStart;
                for (int i = valueStart; i < text.Length; i++)
                {
                    if (text[i] == '{')      depth++;
                    else if (text[i] == '}') { depth--; if (depth == 0) { valueEnd = i; break; } }
                }

                text = text.Substring(0, labelIdx)
                     + label + ": " + configJson
                     + text.Substring(valueEnd + 1);
            }

            File.WriteAllText(jsonPath, text);
        }

        // ── Type conversion helpers ───────────────────────────────────────────

        internal static SceneFloat3     ToFloat3(Vector3 v)         => new SceneFloat3    { x = v.x, y = v.y, z = v.z };
        internal static SceneQuaternion ToQuaternion(Quaternion q)  => new SceneQuaternion { x = q.x, y = q.y, z = q.z, w = q.w };

        internal static Vector3    ToVector3(SceneFloat3 v)       => new Vector3(v.x, v.y, v.z);
        internal static Quaternion ToUnityQuaternion(SceneQuaternion q) =>
            (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0)
                ? Quaternion.identity
                : new Quaternion(q.x, q.y, q.z, q.w);

        // ── Step insertion ────────────────────────────────────────────────────

        /// <summary>
        /// Inserts a new step object into the "steps": [ ... ] array in machine.json.
        /// The step is appended after the last existing step whose sequenceIndex is less
        /// than <paramref name="step"/>.sequenceIndex, or at the end of the array if all
        /// existing steps have a lower or equal sequenceIndex.
        /// Validates the JSON round-trip, creates a timestamped backup, then writes.
        /// Throws <see cref="System.Exception"/> on any failure so the caller can show an error dialog.
        /// </summary>
        internal static void InsertStep(string jsonPath, StepDefinition step)
        {
            string original = File.ReadAllText(jsonPath);

            // Serialize the new step — minimal fields only (JsonUtility emits all [Serializable] fields)
            string stepJson = RoundFloatsInJson(JsonUtility.ToJson(step));

            // Find "steps": [
            const string stepsLabel = "\"steps\"";
            int labelIdx = original.IndexOf(stepsLabel, System.StringComparison.Ordinal);
            if (labelIdx < 0)
                throw new System.Exception("Could not find \"steps\" array in machine.json.");

            int arrayOpen = original.IndexOf('[', labelIdx);
            if (arrayOpen < 0)
                throw new System.Exception("Could not find opening '[' of steps array.");

            // Walk to the closing ] of the steps array, tracking depth
            int depth = 0, arrayClose = -1;
            for (int i = arrayOpen; i < original.Length; i++)
            {
                char c = original[i];
                if (c == '[' || c == '{') depth++;
                else if (c == ']' || c == '}')
                {
                    depth--;
                    if (depth == 0) { arrayClose = i; break; }
                }
            }
            if (arrayClose < 0)
                throw new System.Exception("Could not find closing ']' of steps array.");

            // Find the last object end '}' inside the array to append after
            int insertAfter = -1;
            {
                int d = 0;
                for (int i = arrayOpen + 1; i < arrayClose; i++)
                {
                    char c = original[i];
                    if (c == '{') d++;
                    else if (c == '}') { d--; if (d == 0) insertAfter = i; }
                }
            }

            string modified;
            if (insertAfter < 0)
            {
                // Array is empty — insert as first element
                modified = original.Substring(0, arrayOpen + 1)
                         + "\n    " + stepJson
                         + original.Substring(arrayOpen + 1);
            }
            else
            {
                modified = original.Substring(0, insertAfter + 1)
                         + ",\n    " + stepJson
                         + original.Substring(insertAfter + 1);
            }

            // Validate round-trip
            try { JsonUtility.FromJson<MachinePackageDefinition>(modified); }
            catch (System.Exception ex)
            {
                throw new System.Exception($"Inserted JSON failed validation: {ex.Message}");
            }

            // Backup + write
            string dir    = Path.GetDirectoryName(jsonPath);
            string backup = Path.Combine(dir, ".pose_backups",
                $"machine_{System.DateTime.Now:yyyyMMdd_HHmmss}_before_new_step.json");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.WriteAllText(backup, original);
            File.WriteAllText(jsonPath, modified);
            UnityEditor.AssetDatabase.Refresh();
        }
        /// <summary>
        /// Inserts a new <see cref="PartGroupDefinition"/> into the <c>"partGroups"</c>
        /// array of the given JSON file. If the file has no <c>"partGroups"</c> key yet,
        /// one is created at the top-level object.
        /// </summary>
        internal static void InsertPartGroup(string jsonPath, PartGroupDefinition sub)
        {
            string original = File.ReadAllText(jsonPath);
            string subJson  = RoundFloatsInJson(JsonUtility.ToJson(sub));

            const string label = "\"partGroups\"";
            int labelIdx = original.IndexOf(label, System.StringComparison.Ordinal);

            string modified;
            if (labelIdx >= 0)
            {
                // Array exists — append to it (same algorithm as InsertStep).
                int arrayOpen = original.IndexOf('[', labelIdx);
                if (arrayOpen < 0)
                    throw new System.Exception("Found \"partGroups\" but no opening '['.");

                int depth = 0, arrayClose = -1;
                for (int i = arrayOpen; i < original.Length; i++)
                {
                    char c = original[i];
                    if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}')
                    {
                        depth--;
                        if (depth == 0) { arrayClose = i; break; }
                    }
                }
                if (arrayClose < 0)
                    throw new System.Exception("Could not find closing ']' of partGroups array.");

                int insertAfter = -1;
                {
                    int d = 0;
                    for (int i = arrayOpen + 1; i < arrayClose; i++)
                    {
                        char c = original[i];
                        if (c == '{') d++;
                        else if (c == '}') { d--; if (d == 0) insertAfter = i; }
                    }
                }

                if (insertAfter < 0)
                    modified = original.Substring(0, arrayOpen + 1)
                             + "\n    " + subJson
                             + original.Substring(arrayOpen + 1);
                else
                    modified = original.Substring(0, insertAfter + 1)
                             + ",\n    " + subJson
                             + original.Substring(insertAfter + 1);
            }
            else
            {
                // No "partGroups" key yet — create one before the file's
                // closing '}'. Find the last '}' in the file.
                int lastBrace = original.LastIndexOf('}');
                if (lastBrace < 0)
                    throw new System.Exception("JSON file has no closing '}'.");

                // Insert a comma after the last field + the new array.
                // Walk back from lastBrace to find the nearest non-whitespace
                // character. If it's not a comma, insert one.
                int beforeBrace = lastBrace - 1;
                while (beforeBrace >= 0 && char.IsWhiteSpace(original[beforeBrace])) beforeBrace--;
                bool needComma = beforeBrace >= 0 && original[beforeBrace] != ',';

                string prefix = needComma ? ",\n  " : "\n  ";
                modified = original.Substring(0, lastBrace)
                         + prefix + "\"partGroups\": [\n    " + subJson + "\n  ]\n}"
                         + (lastBrace + 1 < original.Length ? original.Substring(lastBrace + 1) : "");
            }

            // Backup + write
            string dir    = Path.GetDirectoryName(jsonPath);
            string backup = Path.Combine(dir, ".pose_backups",
                $"{Path.GetFileNameWithoutExtension(jsonPath)}_{System.DateTime.Now:yyyyMMdd_HHmmss}_before_new_sub.json");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.WriteAllText(backup, original);
            File.WriteAllText(jsonPath, modified);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Replaces the top-level array <paramref name="fieldName"/> with
        /// <paramref name="arrayJson"/> (a complete <c>[ ... ]</c> string,
        /// including the brackets). When the array is absent, inserts it
        /// just before the file's closing brace. Returns true when the JSON
        /// was modified.
        ///
        /// <para>Used by the Slice 1 prefab-instance save path
        /// (<c>TTAW.WriteJson.cs</c>) to flush the per-assembly
        /// <c>"prefabInstances"</c> block. Whole-array replacement is fine
        /// because a single instance is only ~12 lines and authors edit
        /// them rarely; the cost of regenerating the array is negligible
        /// compared to the precision required to splice individual entries.</para>
        /// </summary>
        internal static bool TryReplaceTopLevelArray(ref string json, string fieldName, string arrayJson)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName) || arrayJson == null) return false;

            string label = $"\"{fieldName}\"";
            int labelIdx = FindTopLevelLabel(json, label);
            if (labelIdx >= 0)
            {
                int arrayOpen = json.IndexOf('[', labelIdx);
                if (arrayOpen < 0) return false;
                int depth = 0, arrayClose = -1;
                for (int i = arrayOpen; i < json.Length; i++)
                {
                    char c = json[i];
                    if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}')
                    {
                        depth--;
                        if (depth == 0) { arrayClose = i; break; }
                    }
                }
                if (arrayClose < 0) return false;
                json = json.Substring(0, arrayOpen) + arrayJson + json.Substring(arrayClose + 1);
                return true;
            }

            int lastBrace = json.LastIndexOf('}');
            if (lastBrace < 0) return false;
            int beforeBrace = lastBrace - 1;
            while (beforeBrace >= 0 && char.IsWhiteSpace(json[beforeBrace])) beforeBrace--;
            bool needComma = beforeBrace >= 0 && json[beforeBrace] != ',' && json[beforeBrace] != '{';
            string prefix = needComma ? ",\n  " : "\n  ";
            json = json.Substring(0, lastBrace)
                 + prefix + $"\"{fieldName}\": " + arrayJson + "\n}"
                 + (lastBrace + 1 < json.Length ? json.Substring(lastBrace + 1) : "");
            return true;
        }

        // Outer-scope label search. Returns the index of <paramref name="label"/>
        // (which already includes the surrounding quotes) when it appears as a
        // top-level key inside the JSON object — i.e. at brace-depth 1 and
        // outside any other string literal. Required because labels like
        // `"prefabInstances"` could legally appear inside nested values
        // (descriptions, payloads, etc.); only the top-level definition is
        // safe to splice.
        private static int FindTopLevelLabel(string json, string label)
        {
            int depth = 0;
            bool inDouble = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inDouble)
                {
                    if (c == '\\' && i + 1 < json.Length) { i++; continue; }
                    if (c == '"') inDouble = false;
                    continue;
                }
                if (c == '{' || c == '[')      { depth++; continue; }
                if (c == '}' || c == ']')      { depth--; continue; }
                if (c == '"')
                {
                    if (depth == 1 && i + label.Length <= json.Length
                        && json.Substring(i, label.Length) == label)
                        return i;
                    inDouble = true;
                }
            }
            return -1;
        }
    }
}
