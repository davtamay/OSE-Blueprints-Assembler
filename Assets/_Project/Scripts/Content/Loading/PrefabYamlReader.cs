using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Minimal YAML reader scoped to the Step Configuration Prefab schema
    /// authored under <c>AgentAssistant/prefabs/</c>. Produces a tree of
    /// <see cref="YamlNode"/>s that <see cref="PrefabExpander"/> consumes.
    ///
    /// <para>Handles only what the prefab schema requires — block-style maps,
    /// block-style sequences, scalar values (with quoted-string + comment
    /// support), and inline flow scalars. Does NOT handle anchors,
    /// merge-keys, multi-line strings, JSON-flow collections, or any other
    /// YAML 1.2 niceties. Indentation is the only structural signal.</para>
    ///
    /// <para>The Python engine (<c>Tools/instantiate_prefab.py</c>) keeps
    /// <c>yaml.safe_load</c> for its own use — both engines must accept the
    /// same prefab YAMLs. If you extend the schema with a YAML construct the
    /// reader below cannot parse, mirror the change here.</para>
    /// </summary>
    public static class PrefabYamlReader
    {
        public static YamlNode ReadFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path required.", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Prefab YAML not found.", path);
            return Read(File.ReadAllText(path));
        }

        public static YamlNode Read(string yaml)
        {
            if (yaml == null) yaml = string.Empty;
            string[] rawLines = yaml.Replace("\r\n", "\n").Split('\n');

            // First pass: drop comments + blank lines, keep (indent, content) pairs.
            var lines = new List<Line>();
            foreach (var raw in rawLines)
            {
                string stripped = StripComment(raw);
                if (string.IsNullOrWhiteSpace(stripped)) continue;
                int indent = 0;
                while (indent < stripped.Length && stripped[indent] == ' ') indent++;
                lines.Add(new Line { Indent = indent, Content = stripped.Substring(indent) });
            }

            int idx = 0;
            return ParseBlock(lines, ref idx, 0);
        }

        // Recursive block parser. Owns lines[idx..] up to (not including) the
        // first line whose indent is < minIndent. Returns a Map node when the
        // first line is a "key:" pair, a Sequence node when it's "- item", or
        // a Scalar node when it's a single value line.
        private static YamlNode ParseBlock(List<Line> lines, ref int idx, int minIndent)
        {
            if (idx >= lines.Count) return new YamlNode { Map = new Dictionary<string, YamlNode>(StringComparer.Ordinal) };

            Line first = lines[idx];
            if (first.Indent < minIndent)
                return new YamlNode { Map = new Dictionary<string, YamlNode>(StringComparer.Ordinal) };

            if (first.Content.StartsWith("- ", StringComparison.Ordinal) || first.Content == "-")
                return ParseSequence(lines, ref idx, first.Indent);

            return ParseMap(lines, ref idx, first.Indent);
        }

        private static YamlNode ParseMap(List<Line> lines, ref int idx, int blockIndent)
        {
            var map = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
            while (idx < lines.Count)
            {
                Line line = lines[idx];
                if (line.Indent < blockIndent) break;
                if (line.Indent > blockIndent)
                    throw new FormatException($"Unexpected indent jump at '{line.Content}' (line content).");

                int colon = FindKeyColon(line.Content);
                if (colon < 0)
                    throw new FormatException($"Expected 'key:' line, got '{line.Content}'.");

                string key   = line.Content.Substring(0, colon).Trim();
                string after = colon + 1 < line.Content.Length ? line.Content.Substring(colon + 1).Trim() : "";

                idx++;

                if (!string.IsNullOrEmpty(after))
                {
                    map[key] = ParseScalarOrFlow(after);
                    continue;
                }

                if (idx < lines.Count && lines[idx].Indent > blockIndent)
                    map[key] = ParseBlock(lines, ref idx, lines[idx].Indent);
                else
                    map[key] = new YamlNode { Scalar = "" };
            }
            return new YamlNode { Map = map };
        }

        private static YamlNode ParseSequence(List<Line> lines, ref int idx, int blockIndent)
        {
            var seq = new List<YamlNode>();
            while (idx < lines.Count)
            {
                Line line = lines[idx];
                if (line.Indent < blockIndent) break;
                if (line.Indent > blockIndent)
                    throw new FormatException($"Unexpected indent jump in sequence at '{line.Content}'.");
                if (!(line.Content.StartsWith("- ", StringComparison.Ordinal) || line.Content == "-"))
                    break;

                string itemContent = line.Content == "-" ? "" : line.Content.Substring(2).TrimStart();
                idx++;

                if (string.IsNullOrEmpty(itemContent))
                {
                    // Sequence item value lives on the following nested lines.
                    if (idx < lines.Count && lines[idx].Indent > blockIndent)
                        seq.Add(ParseBlock(lines, ref idx, lines[idx].Indent));
                    else
                        seq.Add(new YamlNode { Scalar = "" });
                    continue;
                }

                int colon = FindKeyColon(itemContent);
                if (colon < 0)
                {
                    // Bare scalar / flow item: "- value" or "- {flow}"
                    seq.Add(ParseScalarOrFlow(itemContent));
                    continue;
                }

                // Block-form mapping starting on the same line as the dash:
                //   - key1: a
                //     key2: b
                // Treat the "- " column as the map's parent indent; nested
                // siblings sit at blockIndent + 2 (matches the dash's two
                // characters). Synthesise a temporary line for the first kv,
                // then absorb subsequent siblings of the dash's content.
                var subMap = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
                AddInlineKv(subMap, itemContent, colon);

                int childIndent = blockIndent + 2;
                while (idx < lines.Count && lines[idx].Indent == childIndent && lines[idx].Content.IndexOf(':') >= 0
                       && !(lines[idx].Content.StartsWith("- ", StringComparison.Ordinal) || lines[idx].Content == "-"))
                {
                    Line sub = lines[idx];
                    int subColon = FindKeyColon(sub.Content);
                    if (subColon < 0) break;
                    string subKey   = sub.Content.Substring(0, subColon).Trim();
                    string subAfter = subColon + 1 < sub.Content.Length ? sub.Content.Substring(subColon + 1).Trim() : "";
                    idx++;
                    if (!string.IsNullOrEmpty(subAfter))
                    {
                        subMap[subKey] = ParseScalarOrFlow(subAfter);
                    }
                    else if (idx < lines.Count && lines[idx].Indent > childIndent)
                    {
                        subMap[subKey] = ParseBlock(lines, ref idx, lines[idx].Indent);
                    }
                    else
                    {
                        subMap[subKey] = new YamlNode { Scalar = "" };
                    }
                }
                seq.Add(new YamlNode { Map = subMap });
            }
            return new YamlNode { Seq = seq };
        }

        private static void AddInlineKv(Dictionary<string, YamlNode> map, string content, int colon)
        {
            string key   = content.Substring(0, colon).Trim();
            string after = colon + 1 < content.Length ? content.Substring(colon + 1).Trim() : "";
            map[key] = string.IsNullOrEmpty(after)
                ? new YamlNode { Scalar = "" }
                : ParseScalarOrFlow(after);
        }

        // YAML inline flow handling — the prefab schema only uses very simple
        // forms: bracketed lists of scalars (e.g. `[bolts_top, bolts_bot]`)
        // and scalar values. Quoted strings are unwrapped.
        private static YamlNode ParseScalarOrFlow(string s)
        {
            if (s.Length >= 2 && s[0] == '[' && s[s.Length - 1] == ']')
            {
                var seq = new List<YamlNode>();
                string inside = s.Substring(1, s.Length - 2).Trim();
                if (inside.Length > 0)
                {
                    foreach (var part in SplitFlow(inside, ','))
                        seq.Add(new YamlNode { Scalar = StripQuotes(part.Trim()) });
                }
                return new YamlNode { Seq = seq };
            }
            if (s.Length >= 2 && s[0] == '{' && s[s.Length - 1] == '}')
            {
                var map = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
                string inside = s.Substring(1, s.Length - 2).Trim();
                if (inside.Length > 0)
                {
                    foreach (var entry in SplitFlow(inside, ','))
                    {
                        int c = entry.IndexOf(':');
                        if (c < 0) continue;
                        string k = entry.Substring(0, c).Trim();
                        string v = c + 1 < entry.Length ? entry.Substring(c + 1).Trim() : "";
                        map[k] = ParseScalarOrFlow(v);
                    }
                }
                return new YamlNode { Map = map };
            }
            return new YamlNode { Scalar = StripQuotes(s) };
        }

        private static IEnumerable<string> SplitFlow(string s, char sep)
        {
            int depth = 0;
            bool inSingle = false, inDouble = false;
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (!inSingle && !inDouble)
                {
                    if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}') depth--;
                    else if (c == sep && depth == 0)
                    {
                        yield return s.Substring(start, i - start);
                        start = i + 1;
                    }
                }
            }
            if (start < s.Length) yield return s.Substring(start);
        }

        // Locate the colon that ends the YAML key on a "key: value" line —
        // skip colons inside quoted strings or flow collections so values
        // like `default: { x: 0, y: 0, z: 0 }` parse correctly.
        private static int FindKeyColon(string s)
        {
            int depth = 0;
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (!inSingle && !inDouble)
                {
                    if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}') depth--;
                    else if (c == ':' && depth == 0) return i;
                }
            }
            return -1;
        }

        private static string StripComment(string line)
        {
            // Detect '#' at line start or preceded by whitespace, but not inside
            // a quoted string. Prefab files only use whole-line `#` comments
            // and trailing ` # comment` markers, so a tiny quote-aware scan is
            // enough.
            bool inSingle = false, inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' && !inSingle) inDouble = !inDouble;
                else if (c == '\'' && !inDouble) inSingle = !inSingle;
                else if (c == '#' && !inSingle && !inDouble)
                {
                    if (i == 0 || char.IsWhiteSpace(line[i - 1]))
                        return line.Substring(0, i).TrimEnd();
                }
            }
            return line.TrimEnd();
        }

        private static string StripQuotes(string s)
        {
            if (s == null) return null;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') return s.Substring(1, s.Length - 2);
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') return s.Substring(1, s.Length - 2);
            return s;
        }

        private struct Line
        {
            public int    Indent;
            public string Content;
        }
    }

    /// <summary>
    /// Tagged-union YAML node: <see cref="Scalar"/> set =&gt; scalar leaf,
    /// <see cref="Map"/> set =&gt; map node, <see cref="Seq"/> set =&gt; sequence
    /// node. Exactly one of the three is populated per node.
    /// </summary>
    public sealed class YamlNode
    {
        public string Scalar;
        public Dictionary<string, YamlNode> Map;
        public List<YamlNode> Seq;

        public bool IsScalar => Scalar != null && Map == null && Seq == null;
        public bool IsMap    => Map != null;
        public bool IsSeq    => Seq != null;

        public bool TryGet(string key, out YamlNode value)
        {
            value = null;
            return Map != null && Map.TryGetValue(key, out value);
        }

        public string GetScalar(string key, string fallback = null)
        {
            return TryGet(key, out var n) && n.Scalar != null ? n.Scalar : fallback;
        }

        /// <summary>Deep-clones the subtree so the expander can mutate a per-instance copy without poisoning the cached prefab.</summary>
        public YamlNode Clone()
        {
            if (IsScalar) return new YamlNode { Scalar = Scalar };
            if (IsSeq)
            {
                var seq = new List<YamlNode>(Seq.Count);
                foreach (var item in Seq) seq.Add(item.Clone());
                return new YamlNode { Seq = seq };
            }
            if (IsMap)
            {
                var map = new Dictionary<string, YamlNode>(Map.Count, StringComparer.Ordinal);
                foreach (var kv in Map) map[kv.Key] = kv.Value.Clone();
                return new YamlNode { Map = map };
            }
            return new YamlNode { Map = new Dictionary<string, YamlNode>(StringComparer.Ordinal) };
        }

        /// <summary>Encodes the tree as JSON. Used by <see cref="PrefabExpander"/> to feed the result through <c>JsonUtility</c>.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            WriteJson(sb);
            return sb.ToString();
        }

        private void WriteJson(StringBuilder sb)
        {
            if (IsMap)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in Map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(EscapeJson(kv.Key)).Append("\":");
                    kv.Value.WriteJson(sb);
                }
                sb.Append('}');
                return;
            }
            if (IsSeq)
            {
                sb.Append('[');
                for (int i = 0; i < Seq.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Seq[i].WriteJson(sb);
                }
                sb.Append(']');
                return;
            }
            // Scalar — emit as quoted string. JsonUtility tolerates
            // string-typed numerics on numeric fields (parses on read), so we
            // don't need to detect ints/floats here.
            sb.Append('"').Append(EscapeJson(Scalar ?? string.Empty)).Append('"');
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
