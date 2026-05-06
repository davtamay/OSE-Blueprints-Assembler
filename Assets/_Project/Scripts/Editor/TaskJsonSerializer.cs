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
            if (a.previewConfig != null)                 { Sep(); sb.Append($"\"previewConfig\":{JsonUtility.ToJson(a.previewConfig)}"); }
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
            if (e.awaitCues)                           { Sep(); sb.Append("\"awaitCues\":true"); }
            if (!string.IsNullOrEmpty(e.unorderedSet)) { Sep(); sb.Append($"\"unorderedSet\":\"{e.unorderedSet}\""); }
            if (e.endTransform != null)                { Sep(); sb.Append($"\"endTransform\":{JsonUtility.ToJson(e.endTransform)}"); }
            // Skip startTransform when null OR when it looks like a
            // JsonUtility-default-inflated empty payload (rotation 0,0,0,0
            // is an invalid quaternion — no real authored override would
            // produce that). Belt-and-suspenders: covers the case where a
            // prior save round-tripped through JsonUtility and surfaced an
            // empty TaskEndTransform that the author never opted in to.
            if (e.startTransform != null && !IsDefaultInflatedTaskEndTransform(e.startTransform))
            { Sep(); sb.Append($"\"startTransform\":{JsonUtility.ToJson(e.startTransform)}"); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// True when the payload is the default-zero shape JsonUtility
        /// produces for missing nested objects (rotation w=0 is the tell —
        /// every authored rotation has a unit-magnitude quaternion).
        /// </summary>
        public static bool IsDefaultInflatedTaskEndTransform(TaskEndTransform t)
        {
            if (t == null) return true;
            return t.position.x == 0f && t.position.y == 0f && t.position.z == 0f
                && t.rotation.x == 0f && t.rotation.y == 0f && t.rotation.z == 0f && t.rotation.w == 0f
                && t.scale.x == 0f && t.scale.y == 0f && t.scale.z == 0f;
        }

        // ── Step text payloads ─────────────────────────────────────────────
        //
        // The four payload blocks below carry every authored TEXT field on a
        // step: instruction, why-it-matters, hint refs, validation rule refs,
        // feedback messages, and post-completion reinforcement. Until these
        // builders existed, TTAW.WriteJson silently skipped these blocks on
        // every save — guidance/validation/feedback/reinforcement edits made
        // anywhere were dropped on disk. See the "no empty payloads" rule in
        // CLAUDE.md and the JsonUtility default-instance trap captured by
        // feedback_jsonutility_inflates_empty_payloads.md: every builder
        // returns null when its payload carries no authored content so the
        // caller can omit the key entirely instead of writing `"x": {}`.

        public static bool IsEmpty(StepGuidancePayload g) =>
            g == null
            || (string.IsNullOrEmpty(g.instructionText)
                && string.IsNullOrEmpty(g.whyItMattersText)
                && string.IsNullOrEmpty(g.contextualDiagramRef)
                && (g.hintIds == null || g.hintIds.Length == 0));

        public static bool IsEmpty(StepValidationPayload v) =>
            v == null || v.validationRuleIds == null || v.validationRuleIds.Length == 0;

        public static bool IsEmpty(StepFeedbackPayload f) =>
            f == null
            || ((f.effectTriggerIds == null || f.effectTriggerIds.Length == 0)
                && string.IsNullOrEmpty(f.completionEffectColor)
                && f.completionPulseScale == 0f
                && string.IsNullOrEmpty(f.completionParticleId));

        public static bool IsEmpty(StepReinforcementPayload r) =>
            r == null
            || (string.IsNullOrEmpty(r.milestoneMessage)
                && string.IsNullOrEmpty(r.consequenceText)
                && string.IsNullOrEmpty(r.safetyNote)
                && string.IsNullOrEmpty(r.counterfactualText));

        /// <summary>Returns null when the payload is empty (caller should omit the key).</summary>
        public static string BuildGuidanceJson(StepGuidancePayload g)
        {
            if (IsEmpty(g)) return null;
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (!string.IsNullOrEmpty(g.instructionText))      { Sep(); sb.Append("\"instructionText\":").Append(JsonQuote(g.instructionText)); }
            if (!string.IsNullOrEmpty(g.whyItMattersText))     { Sep(); sb.Append("\"whyItMattersText\":").Append(JsonQuote(g.whyItMattersText)); }
            if (g.hintIds != null && g.hintIds.Length > 0)
            {
                Sep();
                sb.Append("\"hintIds\":[");
                for (int i = 0; i < g.hintIds.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(JsonQuote(g.hintIds[i] ?? string.Empty));
                }
                sb.Append("]");
            }
            if (!string.IsNullOrEmpty(g.contextualDiagramRef)) { Sep(); sb.Append("\"contextualDiagramRef\":").Append(JsonQuote(g.contextualDiagramRef)); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>Returns null when the payload is empty.</summary>
        public static string BuildValidationJson(StepValidationPayload v)
        {
            if (IsEmpty(v)) return null;
            var sb = new StringBuilder("{\"validationRuleIds\":[");
            for (int i = 0; i < v.validationRuleIds.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(JsonQuote(v.validationRuleIds[i] ?? string.Empty));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Returns null when the payload is empty.</summary>
        public static string BuildFeedbackJson(StepFeedbackPayload f)
        {
            if (IsEmpty(f)) return null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (f.effectTriggerIds != null && f.effectTriggerIds.Length > 0)
            {
                Sep();
                sb.Append("\"effectTriggerIds\":[");
                for (int i = 0; i < f.effectTriggerIds.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(JsonQuote(f.effectTriggerIds[i] ?? string.Empty));
                }
                sb.Append("]");
            }
            if (!string.IsNullOrEmpty(f.completionEffectColor))  { Sep(); sb.Append("\"completionEffectColor\":").Append(JsonQuote(f.completionEffectColor)); }
            if (f.completionPulseScale != 0f)                    { Sep(); sb.Append("\"completionPulseScale\":").Append(f.completionPulseScale.ToString("0.####", inv)); }
            if (!string.IsNullOrEmpty(f.completionParticleId))   { Sep(); sb.Append("\"completionParticleId\":").Append(JsonQuote(f.completionParticleId)); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>Returns null when the payload is empty.</summary>
        public static string BuildReinforcementJson(StepReinforcementPayload r)
        {
            if (IsEmpty(r)) return null;
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (!string.IsNullOrEmpty(r.milestoneMessage))    { Sep(); sb.Append("\"milestoneMessage\":").Append(JsonQuote(r.milestoneMessage)); }
            if (!string.IsNullOrEmpty(r.consequenceText))     { Sep(); sb.Append("\"consequenceText\":").Append(JsonQuote(r.consequenceText)); }
            if (!string.IsNullOrEmpty(r.safetyNote))          { Sep(); sb.Append("\"safetyNote\":").Append(JsonQuote(r.safetyNote)); }
            if (!string.IsNullOrEmpty(r.counterfactualText))  { Sep(); sb.Append("\"counterfactualText\":").Append(JsonQuote(r.counterfactualText)); }
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Emits a single <see cref="HintDefinition"/> as a JSON object suitable
        /// for inserting into a <c>"hints"</c> array. Empty/default fields are
        /// omitted.
        /// </summary>
        public static string BuildHintJson(HintDefinition h)
        {
            if (h == null) return "{}";
            var sb = new StringBuilder("{");
            bool first = true;
            void Sep() { if (!first) sb.Append(","); first = false; }

            if (!string.IsNullOrEmpty(h.id))       { Sep(); sb.Append("\"id\":").Append(JsonQuote(h.id)); }
            if (!string.IsNullOrEmpty(h.type))     { Sep(); sb.Append("\"type\":").Append(JsonQuote(h.type)); }
            if (!string.IsNullOrEmpty(h.title))    { Sep(); sb.Append("\"title\":").Append(JsonQuote(h.title)); }
            if (!string.IsNullOrEmpty(h.message))  { Sep(); sb.Append("\"message\":").Append(JsonQuote(h.message)); }
            if (!string.IsNullOrEmpty(h.targetId)) { Sep(); sb.Append("\"targetId\":").Append(JsonQuote(h.targetId)); }
            if (!string.IsNullOrEmpty(h.partId))   { Sep(); sb.Append("\"partId\":").Append(JsonQuote(h.partId)); }
            if (!string.IsNullOrEmpty(h.toolId))   { Sep(); sb.Append("\"toolId\":").Append(JsonQuote(h.toolId)); }
            if (!string.IsNullOrEmpty(h.priority)) { Sep(); sb.Append("\"priority\":").Append(JsonQuote(h.priority)); }
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
