using System;
using System.IO;
using System.Text.RegularExpressions;
using OSE.Content;
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
        /// Deserializes a machine.json from the authoring folder.
        /// Returns null on any failure.
        /// </summary>
        internal static MachinePackageDefinition LoadPackage(string packageId)
        {
            string path = GetJsonPath(packageId);
            if (path == null) return null;
            string text = File.ReadAllText(path);
            return JsonUtility.FromJson<MachinePackageDefinition>(text);
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
    }
}
