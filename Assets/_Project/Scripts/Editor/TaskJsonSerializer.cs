using System.Text;
using OSE.Content;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Round-trip-safe JSON serializers for <see cref="ToolActionDefinition"/>
    /// and <see cref="TaskOrderEntry"/>. TTAW's save path
    /// (<c>TTAW.WriteJson.cs</c>) previously hand-rolled these with a subset
    /// of fields — every save silently stripped <c>actionType</c>,
    /// <c>interaction</c>, <c>endTransform</c>, <c>unorderedSet</c>, etc.
    /// Centralizing the serializers here and covering them with
    /// <c>TaskJsonSerializerTests</c> makes adding a new authored field a
    /// compile-time / test-time failure instead of a silent content-loss bug.
    ///
    /// <para><b>Invariant each serializer locks:</b></para>
    /// <list type="bullet">
    ///   <item>Every authored field on the source type is emitted in the
    ///   JSON (subject to null/default-skipping for readability).</item>
    ///   <item>The output round-trips through <see cref="JsonUtility.FromJson{T}"/>
    ///   into a fresh object with identical values for every authored field.</item>
    ///   <item>Nested complex types use <see cref="JsonUtility.ToJson"/> so a
    ///   new field on a child struct is picked up automatically.</item>
    /// </list>
    ///
    /// <para>If you add a field to <see cref="ToolActionDefinition"/> or
    /// <see cref="TaskOrderEntry"/>, update the matching <c>Build...</c>
    /// method here AND add a round-trip assertion to the test fixture.</para>
    /// </summary>
    public static class TaskJsonSerializer
    {
        /// <summary>
        /// Emits a single <see cref="ToolActionDefinition"/> as a JSON object.
        /// Null/empty optional fields are omitted. <see cref="ToolActionDefinition.requiredCount"/>
        /// is always emitted so "1 (default)" and "missing field" remain
        /// distinguishable in source control diffs.
        /// </summary>
        public static string BuildToolActionJson(ToolActionDefinition a)
        {
            if (a == null) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (!string.IsNullOrEmpty(a.id))             { Sep(); sb.Append($"\"id\":\"{a.id}\""); }
            if (!string.IsNullOrEmpty(a.toolId))         { Sep(); sb.Append($"\"toolId\":\"{a.toolId}\""); }
            if (!string.IsNullOrEmpty(a.actionType))     { Sep(); sb.Append($"\"actionType\":\"{a.actionType}\""); }
            if (!string.IsNullOrEmpty(a.targetId))       { Sep(); sb.Append($"\"targetId\":\"{a.targetId}\""); }
            { Sep(); sb.Append($"\"requiredCount\":{a.requiredCount}"); }
            if (!string.IsNullOrEmpty(a.successMessage)) { Sep(); sb.Append($"\"successMessage\":{JsonQuote(a.successMessage)}"); }
            if (!string.IsNullOrEmpty(a.failureMessage)) { Sep(); sb.Append($"\"failureMessage\":{JsonQuote(a.failureMessage)}"); }
            if (a.interaction != null)                   { Sep(); sb.Append($"\"interaction\":{JsonUtility.ToJson(a.interaction)}"); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Emits a single <see cref="TaskOrderEntry"/> as a JSON object.
        /// Null/empty optional fields are omitted. <c>isOptional</c> is
        /// emitted only when true so the common default row stays compact.
        /// </summary>
        public static string BuildTaskOrderEntryJson(TaskOrderEntry e)
        {
            if (e == null) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (!string.IsNullOrEmpty(e.kind))         { Sep(); sb.Append($"\"kind\":\"{e.kind}\""); }
            if (!string.IsNullOrEmpty(e.id))           { Sep(); sb.Append($"\"id\":\"{e.id}\""); }
            if (e.isOptional)                          { Sep(); sb.Append("\"isOptional\":true"); }
            if (!string.IsNullOrEmpty(e.unorderedSet)) { Sep(); sb.Append($"\"unorderedSet\":\"{e.unorderedSet}\""); }
            if (e.endTransform != null)                { Sep(); sb.Append($"\"endTransform\":{JsonUtility.ToJson(e.endTransform)}"); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a string for embedding in a JSON string literal. Handles
        /// quote, backslash, and the control-character set RFC 8259 requires.
        /// Older authoring code assumed messages never contained quotes —
        /// which failed loudly once authors wrote instruction text like
        /// <c>"tap the 3mm bit on the 'Y-Left' bolt head"</c>.
        /// </summary>
        public static string JsonQuote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                        else          sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
