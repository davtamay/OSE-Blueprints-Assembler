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
            string packageDir     = Path.Combine(AuthoringRoot, packageId);
            string assemblyFolder = Path.Combine(packageDir, "assemblies");
            if (!Directory.Exists(assemblyFolder)) return null;

            string needle1 = $"\"id\": \"{entityId}\"";
            string needle2 = $"\"id\":\"{entityId}\"";

            foreach (string asmFile in Directory.GetFiles(assemblyFolder, "*.json"))
            {
                string text = File.ReadAllText(asmFile);
                if (text.Contains(needle1, StringComparison.Ordinal) ||
                    text.Contains(needle2, StringComparison.Ordinal))
                    return asmFile;
            }

            string sharedPath = Path.Combine(packageDir, "shared.json");
            if (File.Exists(sharedPath))
            {
                string text = File.ReadAllText(sharedPath);
                if (text.Contains(needle1, StringComparison.Ordinal) ||
                    text.Contains(needle2, StringComparison.Ordinal))
                    return sharedPath;
            }
            return null;
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
                return LoadSplitLayoutPackage(packageDir, assemblyFolder);

            string path = GetJsonPath(packageId);
            if (path == null) return null;
            return JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(path));
        }

        private static MachinePackageDefinition LoadSplitLayoutPackage(string packageDir, string assemblyFolder)
        {
            string machineJson = File.ReadAllText(Path.Combine(packageDir, "machine.json"));
            var pkg = JsonUtility.FromJson<MachinePackageDefinition>(machineJson) ?? new MachinePackageDefinition();

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
                }
            }

            foreach (string asmFile in Directory.GetFiles(assemblyFolder, "*.json").OrderBy(f => f))
            {
                var chunk = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(asmFile));
                if (chunk == null) continue;
                pkg.assemblies      = MergeArrays(pkg.assemblies,      chunk.assemblies);
                pkg.subassemblies   = MergeArrays(pkg.subassemblies,   chunk.subassemblies);
                pkg.parts           = MergeArrays(pkg.parts,           chunk.parts);
                pkg.steps           = MergeArrays(pkg.steps,           chunk.steps);
                pkg.targets         = MergeArrays(pkg.targets,         chunk.targets);
                pkg.hints           = MergeArrays(pkg.hints,           chunk.hints);
                pkg.validationRules = MergeArrays(pkg.validationRules, chunk.validationRules);
            }

            string previewPath = Path.Combine(packageDir, "preview_config.json");
            if (File.Exists(previewPath))
            {
                var wrap = JsonUtility.FromJson<MachinePackageDefinition>(File.ReadAllText(previewPath));
                if (wrap?.previewConfig != null)
                    pkg.previewConfig = wrap.previewConfig;
            }
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
        /// Inserts a new <see cref="SubassemblyDefinition"/> into the <c>"subassemblies"</c>
        /// array of the given JSON file. If the file has no <c>"subassemblies"</c> key yet,
        /// one is created at the top-level object.
        /// </summary>
        internal static void InsertSubassembly(string jsonPath, SubassemblyDefinition sub)
        {
            string original = File.ReadAllText(jsonPath);
            string subJson  = RoundFloatsInJson(JsonUtility.ToJson(sub));

            const string label = "\"subassemblies\"";
            int labelIdx = original.IndexOf(label, System.StringComparison.Ordinal);

            string modified;
            if (labelIdx >= 0)
            {
                // Array exists — append to it (same algorithm as InsertStep).
                int arrayOpen = original.IndexOf('[', labelIdx);
                if (arrayOpen < 0)
                    throw new System.Exception("Found \"subassemblies\" but no opening '['.");

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
                    throw new System.Exception("Could not find closing ']' of subassemblies array.");

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
                // No "subassemblies" key yet — create one before the file's
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
                         + prefix + "\"subassemblies\": [\n    " + subJson + "\n  ]\n}"
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
    }
}
