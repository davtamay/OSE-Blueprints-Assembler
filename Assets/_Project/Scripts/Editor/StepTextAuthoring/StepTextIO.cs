using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OSE.Content;
using OSE.Core;

// ──────────────────────────────────────────────────────────────────────────────
// StepTextIO.cs  —  Markdown / JSON / YAML / Plain-text import/export for the
// text-bearing fields of a single StepDefinition. Round-trips via Markdown
// when the import was produced by Serialize(...); other inputs are best-effort.
//
// The motivating use case: drafting hint and instruction text in an external
// editor or LLM and pasting back. Markdown is the default because it survives
// LLM round-trips cleanly; JSON/YAML are for diffing and scripting; Plain is
// the fallback for paste-into-LLM workflows.
//
// Hint definition bodies (title/message/etc.) are serialized inline so the
// exported document is self-contained — editing a hint message in another tool
// and reimporting must round-trip the change.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    internal static class StepTextIO
    {
        public enum Format { Markdown, Json, Yaml, Plain }

        public static Format GuessFormatFromPath(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".md":   return Format.Markdown;
                case ".json": return Format.Json;
                case ".yaml":
                case ".yml":  return Format.Yaml;
                default:      return Format.Plain;
            }
        }

        // ── Serialize ─────────────────────────────────────────────────────────

        public static string Serialize(StepDefinition step, ToolTargetAuthoringWindow ttaw, Format fmt)
        {
            var snap = StepTextSnapshot.From(step, ttaw);
            switch (fmt)
            {
                case Format.Markdown: return ToMarkdown(snap);
                case Format.Json:     return ToJson(snap);
                case Format.Yaml:     return ToYaml(snap);
                case Format.Plain:    return ToPlain(snap);
                default:              return ToMarkdown(snap);
            }
        }

        public static bool ApplyTo(StepDefinition step, ToolTargetAuthoringWindow ttaw, string text, Format fmt)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                StepTextSnapshot snap;
                switch (fmt)
                {
                    case Format.Markdown: snap = FromMarkdown(text); break;
                    case Format.Json:     snap = FromJson(text);     break;
                    case Format.Yaml:     snap = FromYaml(text);     break;
                    case Format.Plain:    snap = FromPlain(text);    break;
                    default:              snap = FromMarkdown(text); break;
                }
                snap.WriteInto(step, ttaw);
                return true;
            }
            catch (Exception ex)
            {
                OseLog.Error($"[StepTextIO] Import failed ({fmt}): {ex.Message}");
                return false;
            }
        }

        // ── Markdown ──────────────────────────────────────────────────────────
        //
        // Section structure — heading regex on import is tolerant of
        // leading/trailing whitespace and case-insensitive matching:
        //
        //   # Step <id> · <name>
        //   ## Guidance / Instruction
        //   …multiline body…
        //   ## Guidance / Why It Matters
        //   …
        //   ## Guidance / Diagram
        //   …
        //   ## Hints
        //   ### Hint <id> (type=…, target=…, part=…, tool=…, priority=…)
        //   #### Title
        //   …
        //   #### Message
        //   …
        //   ## Feedback / Effect Color
        //   …
        //   ## Feedback / Particle Id
        //   …
        //   ## Reinforcement / Milestone
        //   …
        //   …
        //   ## Tool Action <id>
        //   ### Success
        //   …
        //   ### Failure
        //   …

        private static string ToMarkdown(StepTextSnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Step {s.StepId} · {s.StepName}");
            sb.AppendLine();
            AppendMdSection(sb, "Guidance / Instruction", s.Instruction);
            AppendMdSection(sb, "Guidance / Why It Matters", s.WhyItMatters);
            AppendMdSection(sb, "Guidance / Diagram", s.DiagramRef);

            sb.AppendLine("## Hints");
            sb.AppendLine();
            if (s.Hints.Count == 0)
            {
                sb.AppendLine("_(no hints linked)_");
                sb.AppendLine();
            }
            foreach (var h in s.Hints)
            {
                var meta = new List<string>();
                if (!string.IsNullOrEmpty(h.Type))     meta.Add($"type={h.Type}");
                if (!string.IsNullOrEmpty(h.TargetId)) meta.Add($"target={h.TargetId}");
                if (!string.IsNullOrEmpty(h.PartId))   meta.Add($"part={h.PartId}");
                if (!string.IsNullOrEmpty(h.ToolId))   meta.Add($"tool={h.ToolId}");
                if (!string.IsNullOrEmpty(h.Priority)) meta.Add($"priority={h.Priority}");
                string suffix = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";
                sb.AppendLine($"### Hint {h.Id}{suffix}");
                sb.AppendLine();
                sb.AppendLine("#### Title");
                sb.AppendLine(h.Title ?? "");
                sb.AppendLine();
                sb.AppendLine("#### Message");
                sb.AppendLine(h.Message ?? "");
                sb.AppendLine();
            }

            AppendMdSection(sb, "Feedback / Effect Color",   s.FeedbackEffectColor);
            AppendMdSection(sb, "Feedback / Pulse Scale",    s.FeedbackPulseScale);
            AppendMdSection(sb, "Feedback / Particle Id",    s.FeedbackParticleId);
            AppendMdSection(sb, "Reinforcement / Milestone",       s.MilestoneMessage);
            AppendMdSection(sb, "Reinforcement / Consequence",     s.ConsequenceText);
            AppendMdSection(sb, "Reinforcement / Safety Note",     s.SafetyNote);
            AppendMdSection(sb, "Reinforcement / Counterfactual",  s.CounterfactualText);

            foreach (var ta in s.ToolActions)
            {
                sb.AppendLine($"## Tool Action {ta.Id}");
                sb.AppendLine();
                sb.AppendLine("### Success");
                sb.AppendLine(ta.SuccessMessage ?? "");
                sb.AppendLine();
                sb.AppendLine("### Failure");
                sb.AppendLine(ta.FailureMessage ?? "");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static void AppendMdSection(StringBuilder sb, string heading, string body)
        {
            sb.AppendLine($"## {heading}");
            sb.AppendLine();
            sb.AppendLine(body ?? "");
            sb.AppendLine();
        }

        private static StepTextSnapshot FromMarkdown(string text)
        {
            var snap = new StepTextSnapshot();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            string section = null;       // "Guidance / Instruction", etc.
            string subsection = null;    // for hints / tool actions
            HintEntry currentHint = null;
            ToolActionEntry currentAction = null;
            var buf = new StringBuilder();

            void Flush()
            {
                if (section == null) return;
                string body = buf.ToString().TrimEnd('\n');
                AssignSection(snap, section, subsection, currentHint, currentAction, body);
                buf.Clear();
            }

            foreach (var raw in lines)
            {
                string line = raw;
                if (line.StartsWith("# Step ", StringComparison.Ordinal))
                {
                    Flush();
                    section = null;
                    subsection = null;
                    currentHint = null;
                    currentAction = null;
                    continue;
                }
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    Flush();
                    string h2 = line.Substring(3).Trim();
                    section = h2;
                    subsection = null;
                    currentHint = null;
                    currentAction = null;
                    if (h2.StartsWith("Tool Action ", StringComparison.OrdinalIgnoreCase))
                    {
                        currentAction = new ToolActionEntry { Id = h2.Substring("Tool Action ".Length).Trim() };
                        snap.ToolActions.Add(currentAction);
                    }
                    continue;
                }
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    Flush();
                    string h3 = line.Substring(4).Trim();
                    if (string.Equals(section, "Hints", StringComparison.OrdinalIgnoreCase)
                        && h3.StartsWith("Hint ", StringComparison.OrdinalIgnoreCase))
                    {
                        currentHint = ParseHintHeading(h3.Substring("Hint ".Length).Trim());
                        snap.Hints.Add(currentHint);
                        subsection = null;
                    }
                    else if (currentAction != null)
                    {
                        subsection = h3;
                    }
                    continue;
                }
                if (line.StartsWith("#### ", StringComparison.Ordinal))
                {
                    Flush();
                    subsection = line.Substring(5).Trim();
                    continue;
                }
                buf.AppendLine(line);
            }
            Flush();
            return snap;
        }

        private static HintEntry ParseHintHeading(string raw)
        {
            // "<id>" or "<id> (type=tip, target=t_x)"
            var entry = new HintEntry();
            int paren = raw.IndexOf('(');
            if (paren < 0)
            {
                entry.Id = raw;
                return entry;
            }
            entry.Id = raw.Substring(0, paren).Trim();
            int close = raw.LastIndexOf(')');
            string inside = close > paren ? raw.Substring(paren + 1, close - paren - 1) : raw.Substring(paren + 1);
            foreach (var rawPair in inside.Split(','))
            {
                var pair = rawPair.Trim();
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string k = pair.Substring(0, eq).Trim().ToLowerInvariant();
                string v = pair.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "type":     entry.Type     = v; break;
                    case "target":   entry.TargetId = v; break;
                    case "part":     entry.PartId   = v; break;
                    case "tool":     entry.ToolId   = v; break;
                    case "priority": entry.Priority = v; break;
                }
            }
            return entry;
        }

        private static void AssignSection(StepTextSnapshot snap,
            string section, string subsection,
            HintEntry hint, ToolActionEntry action,
            string body)
        {
            string s = section ?? "";
            switch (s.ToLowerInvariant())
            {
                case "guidance / instruction":     snap.Instruction = body;        return;
                case "guidance / why it matters":  snap.WhyItMatters = body;       return;
                case "guidance / diagram":         snap.DiagramRef = body;         return;
                case "feedback / effect color":    snap.FeedbackEffectColor = body; return;
                case "feedback / pulse scale":     snap.FeedbackPulseScale = body;  return;
                case "feedback / particle id":     snap.FeedbackParticleId = body;  return;
                case "reinforcement / milestone":      snap.MilestoneMessage = body;     return;
                case "reinforcement / consequence":    snap.ConsequenceText = body;      return;
                case "reinforcement / safety note":    snap.SafetyNote = body;           return;
                case "reinforcement / counterfactual": snap.CounterfactualText = body;   return;
            }
            if (s.StartsWith("Tool Action ", StringComparison.OrdinalIgnoreCase) && action != null)
            {
                if (string.Equals(subsection, "Success", StringComparison.OrdinalIgnoreCase)) action.SuccessMessage = body;
                else if (string.Equals(subsection, "Failure", StringComparison.OrdinalIgnoreCase)) action.FailureMessage = body;
                return;
            }
            if (string.Equals(s, "Hints", StringComparison.OrdinalIgnoreCase) && hint != null)
            {
                if (string.Equals(subsection, "Title", StringComparison.OrdinalIgnoreCase))   hint.Title   = body;
                else if (string.Equals(subsection, "Message", StringComparison.OrdinalIgnoreCase)) hint.Message = body;
            }
        }

        // ── JSON / YAML / Plain ───────────────────────────────────────────────

        private static string ToJson(StepTextSnapshot s) => UnityEngine.JsonUtility.ToJson(s, true);
        private static StepTextSnapshot FromJson(string text) => UnityEngine.JsonUtility.FromJson<StepTextSnapshot>(text);

        private static string ToYaml(StepTextSnapshot s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"stepId: {YamlScalar(s.StepId)}");
            sb.AppendLine($"stepName: {YamlScalar(s.StepName)}");
            AppendYamlBlock(sb, "instruction",        s.Instruction);
            AppendYamlBlock(sb, "whyItMatters",       s.WhyItMatters);
            AppendYamlBlock(sb, "diagramRef",         s.DiagramRef);
            AppendYamlBlock(sb, "feedbackEffectColor",s.FeedbackEffectColor);
            AppendYamlBlock(sb, "feedbackPulseScale", s.FeedbackPulseScale);
            AppendYamlBlock(sb, "feedbackParticleId", s.FeedbackParticleId);
            AppendYamlBlock(sb, "milestoneMessage",   s.MilestoneMessage);
            AppendYamlBlock(sb, "consequenceText",    s.ConsequenceText);
            AppendYamlBlock(sb, "safetyNote",         s.SafetyNote);
            AppendYamlBlock(sb, "counterfactualText", s.CounterfactualText);
            sb.AppendLine("hints:");
            foreach (var h in s.Hints)
            {
                sb.AppendLine($"  - id: {YamlScalar(h.Id)}");
                if (!string.IsNullOrEmpty(h.Type))     sb.AppendLine($"    type: {YamlScalar(h.Type)}");
                if (!string.IsNullOrEmpty(h.TargetId)) sb.AppendLine($"    targetId: {YamlScalar(h.TargetId)}");
                if (!string.IsNullOrEmpty(h.PartId))   sb.AppendLine($"    partId: {YamlScalar(h.PartId)}");
                if (!string.IsNullOrEmpty(h.ToolId))   sb.AppendLine($"    toolId: {YamlScalar(h.ToolId)}");
                if (!string.IsNullOrEmpty(h.Priority)) sb.AppendLine($"    priority: {YamlScalar(h.Priority)}");
                if (!string.IsNullOrEmpty(h.Title))    sb.AppendLine($"    title: {YamlScalar(h.Title)}");
                if (!string.IsNullOrEmpty(h.Message))  sb.AppendLine($"    message: {YamlScalar(h.Message)}");
            }
            sb.AppendLine("toolActions:");
            foreach (var ta in s.ToolActions)
            {
                sb.AppendLine($"  - id: {YamlScalar(ta.Id)}");
                if (!string.IsNullOrEmpty(ta.SuccessMessage)) sb.AppendLine($"    success: {YamlScalar(ta.SuccessMessage)}");
                if (!string.IsNullOrEmpty(ta.FailureMessage)) sb.AppendLine($"    failure: {YamlScalar(ta.FailureMessage)}");
            }
            return sb.ToString();
        }

        private static StepTextSnapshot FromYaml(string text)
        {
            // Minimal YAML reader: single-line "key: value" pairs only, plus a
            // tiny block-scalar handler ("key: |"). Lists are parsed by
            // recognising the "- id: …" pattern at indent 2 for hints and
            // toolActions. Designed for round-tripping ToYaml output, not for
            // arbitrary YAML.
            var snap = new StepTextSnapshot();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string topListKey = null; // "hints" or "toolActions"
            HintEntry curHint = null;
            ToolActionEntry curAction = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || line.TrimStart().StartsWith("#")) continue;

                int indent = 0;
                while (indent < line.Length && line[indent] == ' ') indent++;
                string body = line.Substring(indent);

                if (indent == 0)
                {
                    int colon = body.IndexOf(':');
                    if (colon < 0) continue;
                    string key = body.Substring(0, colon).Trim();
                    string val = colon + 1 < body.Length ? body.Substring(colon + 1).Trim() : "";
                    if (key == "hints" || key == "toolActions") { topListKey = key; curHint = null; curAction = null; continue; }
                    topListKey = null;
                    if (val == "|" || val == ">")
                    {
                        var sb = new StringBuilder();
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            string nl = lines[j];
                            if (nl.Length == 0) { sb.AppendLine(); continue; }
                            int ni = 0; while (ni < nl.Length && nl[ni] == ' ') ni++;
                            if (ni < 2) break;
                            sb.AppendLine(nl.Substring(2));
                            i = j;
                        }
                        AssignTopLevel(snap, key, sb.ToString().TrimEnd('\n'));
                    }
                    else
                    {
                        AssignTopLevel(snap, key, UnescapeYaml(val));
                    }
                }
                else if (indent == 2 && body.StartsWith("- "))
                {
                    // start of a list item.
                    string tail = body.Substring(2);
                    int colon = tail.IndexOf(':');
                    if (colon < 0) continue;
                    string key = tail.Substring(0, colon).Trim();
                    string val = colon + 1 < tail.Length ? tail.Substring(colon + 1).Trim() : "";
                    if (topListKey == "hints")     { curHint   = new HintEntry();       snap.Hints.Add(curHint);       AssignHint(curHint, key, UnescapeYaml(val));         curAction = null; }
                    else if (topListKey == "toolActions") { curAction = new ToolActionEntry(); snap.ToolActions.Add(curAction); AssignAction(curAction, key, UnescapeYaml(val)); curHint = null; }
                }
                else if (indent == 4)
                {
                    int colon = body.IndexOf(':');
                    if (colon < 0) continue;
                    string key = body.Substring(0, colon).Trim();
                    string val = colon + 1 < body.Length ? body.Substring(colon + 1).Trim() : "";
                    if (curHint   != null) AssignHint(curHint,   key, UnescapeYaml(val));
                    if (curAction != null) AssignAction(curAction, key, UnescapeYaml(val));
                }
            }
            return snap;
        }

        private static void AssignTopLevel(StepTextSnapshot snap, string key, string value)
        {
            switch (key)
            {
                case "stepId":               snap.StepId = value; break;
                case "stepName":             snap.StepName = value; break;
                case "instruction":          snap.Instruction = value; break;
                case "whyItMatters":         snap.WhyItMatters = value; break;
                case "diagramRef":           snap.DiagramRef = value; break;
                case "feedbackEffectColor":  snap.FeedbackEffectColor = value; break;
                case "feedbackPulseScale":   snap.FeedbackPulseScale = value; break;
                case "feedbackParticleId":   snap.FeedbackParticleId = value; break;
                case "milestoneMessage":     snap.MilestoneMessage = value; break;
                case "consequenceText":      snap.ConsequenceText = value; break;
                case "safetyNote":           snap.SafetyNote = value; break;
                case "counterfactualText":   snap.CounterfactualText = value; break;
            }
        }

        private static void AssignHint(HintEntry h, string key, string value)
        {
            switch (key)
            {
                case "id":       h.Id = value;       break;
                case "type":     h.Type = value;     break;
                case "title":    h.Title = value;    break;
                case "message":  h.Message = value;  break;
                case "targetId": h.TargetId = value; break;
                case "partId":   h.PartId = value;   break;
                case "toolId":   h.ToolId = value;   break;
                case "priority": h.Priority = value; break;
            }
        }

        private static void AssignAction(ToolActionEntry a, string key, string value)
        {
            switch (key)
            {
                case "id":      a.Id = value; break;
                case "success": a.SuccessMessage = value; break;
                case "failure": a.FailureMessage = value; break;
            }
        }

        private static void AppendYamlBlock(StringBuilder sb, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) { sb.AppendLine($"{key}: \"\""); return; }
            if (value.Contains('\n'))
            {
                sb.AppendLine($"{key}: |");
                foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
                    sb.AppendLine("  " + line);
            }
            else
            {
                sb.AppendLine($"{key}: {YamlScalar(value)}");
            }
        }

        private static string YamlScalar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // Always quote strings that contain reserved YAML chars.
            bool needsQuote = false;
            foreach (var c in s) if (c == ':' || c == '#' || c == '"' || c == '\'' || c == '\n') { needsQuote = true; break; }
            if (!needsQuote) return s;
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
        }

        private static string UnescapeYaml(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                string inner = s.Substring(1, s.Length - 2);
                var sb = new StringBuilder(inner.Length);
                for (int i = 0; i < inner.Length; i++)
                {
                    char c = inner[i];
                    if (c == '\\' && i + 1 < inner.Length)
                    {
                        char n = inner[++i];
                        if      (n == 'n') sb.Append('\n');
                        else if (n == '"') sb.Append('"');
                        else if (n == '\\') sb.Append('\\');
                        else { sb.Append('\\'); sb.Append(n); }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }
            return s;
        }

        private static string ToPlain(StepTextSnapshot s)
        {
            var sb = new StringBuilder();
            void Section(string label, string body)
            {
                sb.AppendLine($"--- {label} ---");
                sb.AppendLine(body ?? "");
                sb.AppendLine();
            }
            sb.AppendLine($"--- step {s.StepId} : {s.StepName} ---");
            sb.AppendLine();
            Section("instruction", s.Instruction);
            Section("whyItMatters", s.WhyItMatters);
            Section("diagram", s.DiagramRef);
            foreach (var h in s.Hints)
            {
                Section($"hint {h.Id} title", h.Title);
                Section($"hint {h.Id} message", h.Message);
            }
            Section("feedback effectColor", s.FeedbackEffectColor);
            Section("feedback pulseScale", s.FeedbackPulseScale);
            Section("feedback particleId", s.FeedbackParticleId);
            Section("reinforcement milestone", s.MilestoneMessage);
            Section("reinforcement consequence", s.ConsequenceText);
            Section("reinforcement safety", s.SafetyNote);
            Section("reinforcement counterfactual", s.CounterfactualText);
            foreach (var ta in s.ToolActions)
            {
                Section($"toolAction {ta.Id} success", ta.SuccessMessage);
                Section($"toolAction {ta.Id} failure", ta.FailureMessage);
            }
            return sb.ToString();
        }

        private static StepTextSnapshot FromPlain(string text)
        {
            var snap = new StepTextSnapshot();
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string section = null;
            var buf = new StringBuilder();

            void Flush()
            {
                if (section == null) return;
                string body = buf.ToString().TrimEnd('\n');
                ApplyPlainSection(snap, section, body);
                buf.Clear();
            }

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("--- ") && trimmed.EndsWith(" ---"))
                {
                    Flush();
                    section = trimmed.Substring(4, trimmed.Length - 8).Trim();
                    if (section.StartsWith("step ", StringComparison.OrdinalIgnoreCase))
                        section = null; // ignore header
                    continue;
                }
                buf.AppendLine(line);
            }
            Flush();
            return snap;
        }

        private static void ApplyPlainSection(StepTextSnapshot snap, string section, string body)
        {
            if (string.IsNullOrEmpty(section)) return;
            switch (section)
            {
                case "instruction":             snap.Instruction = body; return;
                case "whyItMatters":            snap.WhyItMatters = body; return;
                case "diagram":                 snap.DiagramRef = body; return;
                case "feedback effectColor":    snap.FeedbackEffectColor = body; return;
                case "feedback pulseScale":     snap.FeedbackPulseScale = body; return;
                case "feedback particleId":     snap.FeedbackParticleId = body; return;
                case "reinforcement milestone":      snap.MilestoneMessage = body; return;
                case "reinforcement consequence":    snap.ConsequenceText = body; return;
                case "reinforcement safety":         snap.SafetyNote = body; return;
                case "reinforcement counterfactual": snap.CounterfactualText = body; return;
            }
            // hint <id> title | hint <id> message | toolAction <id> success/failure
            string[] parts = section.Split(' ');
            if (parts.Length >= 3)
            {
                if (parts[0] == "hint")
                {
                    var h = snap.Hints.Find(x => x.Id == parts[1]) ?? AddHint(snap, parts[1]);
                    if (parts[2] == "title")   h.Title = body;
                    if (parts[2] == "message") h.Message = body;
                }
                else if (parts[0] == "toolAction")
                {
                    var a = snap.ToolActions.Find(x => x.Id == parts[1]) ?? AddAction(snap, parts[1]);
                    if (parts[2] == "success") a.SuccessMessage = body;
                    if (parts[2] == "failure") a.FailureMessage = body;
                }
            }
        }

        private static HintEntry AddHint(StepTextSnapshot s, string id) { var h = new HintEntry { Id = id }; s.Hints.Add(h); return h; }
        private static ToolActionEntry AddAction(StepTextSnapshot s, string id) { var a = new ToolActionEntry { Id = id }; s.ToolActions.Add(a); return a; }
    }

    [Serializable]
    internal sealed class StepTextSnapshot
    {
        public string StepId;
        public string StepName;
        public string Instruction;
        public string WhyItMatters;
        public string DiagramRef;
        public string FeedbackEffectColor;
        public string FeedbackPulseScale;
        public string FeedbackParticleId;
        public string MilestoneMessage;
        public string ConsequenceText;
        public string SafetyNote;
        public string CounterfactualText;
        public List<HintEntry>       Hints       = new List<HintEntry>();
        public List<ToolActionEntry> ToolActions = new List<ToolActionEntry>();

        public static StepTextSnapshot From(StepDefinition step, ToolTargetAuthoringWindow ttaw)
        {
            var s = new StepTextSnapshot
            {
                StepId   = step.id,
                StepName = step.GetDisplayName(),
            };
            if (step.guidance != null)
            {
                s.Instruction  = step.guidance.instructionText;
                s.WhyItMatters = step.guidance.whyItMattersText;
                s.DiagramRef   = step.guidance.contextualDiagramRef;
                if (step.guidance.hintIds != null)
                {
                    foreach (var hid in step.guidance.hintIds)
                    {
                        if (string.IsNullOrEmpty(hid)) continue;
                        var h = ttaw?.GetHintById(hid);
                        s.Hints.Add(new HintEntry
                        {
                            Id       = hid,
                            Type     = h?.type     ?? "",
                            Title    = h?.title    ?? "",
                            Message  = h?.message  ?? "",
                            TargetId = h?.targetId ?? "",
                            PartId   = h?.partId   ?? "",
                            ToolId   = h?.toolId   ?? "",
                            Priority = h?.priority ?? "",
                        });
                    }
                }
            }
            if (step.feedback != null)
            {
                s.FeedbackEffectColor = step.feedback.completionEffectColor;
                s.FeedbackPulseScale  = step.feedback.completionPulseScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                s.FeedbackParticleId  = step.feedback.completionParticleId;
            }
            if (step.reinforcement != null)
            {
                s.MilestoneMessage     = step.reinforcement.milestoneMessage;
                s.ConsequenceText      = step.reinforcement.consequenceText;
                s.SafetyNote           = step.reinforcement.safetyNote;
                s.CounterfactualText   = step.reinforcement.counterfactualText;
            }
            if (step.requiredToolActions != null)
            {
                foreach (var a in step.requiredToolActions)
                {
                    if (a == null) continue;
                    s.ToolActions.Add(new ToolActionEntry
                    {
                        Id             = a.id,
                        SuccessMessage = a.successMessage,
                        FailureMessage = a.failureMessage,
                    });
                }
            }
            return s;
        }

        public void WriteInto(StepDefinition step, ToolTargetAuthoringWindow ttaw)
        {
            // Guidance
            if (step.guidance == null) step.guidance = new StepGuidancePayload();
            step.guidance.instructionText      = Instruction      ?? step.guidance.instructionText;
            step.guidance.whyItMattersText     = WhyItMatters     ?? step.guidance.whyItMattersText;
            step.guidance.contextualDiagramRef = DiagramRef       ?? step.guidance.contextualDiagramRef;

            // Hints — reconcile by id. New hints get registered; existing ones updated and marked dirty.
            if (Hints != null && Hints.Count > 0)
            {
                var newHintIds = new List<string>();
                foreach (var h in Hints)
                {
                    if (h == null || string.IsNullOrEmpty(h.Id)) continue;
                    var existing = ttaw?.GetHintById(h.Id);
                    if (existing == null)
                    {
                        var def = new HintDefinition
                        {
                            id = h.Id, type = h.Type, title = h.Title, message = h.Message,
                            targetId = h.TargetId, partId = h.PartId, toolId = h.ToolId, priority = h.Priority,
                        };
                        ttaw?.RegisterNewHint(def);
                    }
                    else
                    {
                        existing.type     = h.Type     ?? existing.type;
                        existing.title    = h.Title    ?? existing.title;
                        existing.message  = h.Message  ?? existing.message;
                        existing.targetId = h.TargetId ?? existing.targetId;
                        existing.partId   = h.PartId   ?? existing.partId;
                        existing.toolId   = h.ToolId   ?? existing.toolId;
                        existing.priority = h.Priority ?? existing.priority;
                        ttaw?.MarkHintDirty(existing.id);
                    }
                    newHintIds.Add(h.Id);
                }
                step.guidance.hintIds = newHintIds.ToArray();
            }

            // Feedback
            if (step.feedback == null) step.feedback = new StepFeedbackPayload();
            if (FeedbackEffectColor != null) step.feedback.completionEffectColor = FeedbackEffectColor;
            if (FeedbackParticleId  != null) step.feedback.completionParticleId  = FeedbackParticleId;
            if (!string.IsNullOrEmpty(FeedbackPulseScale)
                && float.TryParse(FeedbackPulseScale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pulse))
                step.feedback.completionPulseScale = pulse;

            // Reinforcement
            if (step.reinforcement == null) step.reinforcement = new StepReinforcementPayload();
            if (MilestoneMessage   != null) step.reinforcement.milestoneMessage   = MilestoneMessage;
            if (ConsequenceText    != null) step.reinforcement.consequenceText    = ConsequenceText;
            if (SafetyNote         != null) step.reinforcement.safetyNote         = SafetyNote;
            if (CounterfactualText != null) step.reinforcement.counterfactualText = CounterfactualText;

            // Tool actions — match by id; missing ids are skipped (we don't add new tool actions from text).
            if (ToolActions != null && step.requiredToolActions != null)
            {
                foreach (var snap in ToolActions)
                {
                    if (snap == null || string.IsNullOrEmpty(snap.Id)) continue;
                    foreach (var a in step.requiredToolActions)
                    {
                        if (a != null && a.id == snap.Id)
                        {
                            if (snap.SuccessMessage != null) a.successMessage = snap.SuccessMessage;
                            if (snap.FailureMessage != null) a.failureMessage = snap.FailureMessage;
                            break;
                        }
                    }
                }
            }
        }
    }

    [Serializable]
    internal sealed class HintEntry
    {
        public string Id;
        public string Type;
        public string Title;
        public string Message;
        public string TargetId;
        public string PartId;
        public string ToolId;
        public string Priority;
    }

    [Serializable]
    internal sealed class ToolActionEntry
    {
        public string Id;
        public string SuccessMessage;
        public string FailureMessage;
    }
}
