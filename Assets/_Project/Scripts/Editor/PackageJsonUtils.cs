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
    }
}
