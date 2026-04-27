using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OSE.Core;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Pure-C# expander that turns a <see cref="PrefabInstance"/> + a
    /// Step Configuration Prefab YAML into an array of
    /// <see cref="StepDefinition"/> objects, each tagged with a matching
    /// <see cref="PrefabRef"/>. Mirrors the substitution semantics of the
    /// Python engine at <c>Tools/instantiate_prefab.py</c>; both must accept
    /// the same prefab schema and produce the same step JSON for identical
    /// inputs.
    ///
    /// <para>Slice 1 supports steps-only prefabs (no
    /// <c>partDefinitions:</c> / <c>partGroupDefinition:</c> sections).
    /// Slice 2 will extend the section walker — the substitution and JSON
    /// emission below are already general-purpose.</para>
    ///
    /// <para>Determinism: this method is a pure function of the prefab YAML
    /// and the instance bindings + options. No I/O beyond reading the YAML
    /// passed in by the caller. The normalizer caches the parsed YAML keyed
    /// on path so repeated expansions don't re-parse.</para>
    /// </summary>
    public static class PrefabExpander
    {
        /// <summary>Result of expanding a single instance.</summary>
        public sealed class Result
        {
            public StepDefinition[] Steps;
            public List<string>     Errors;
            public List<string>     Warnings;
        }

        /// <summary>
        /// Locate, parse, and expand <paramref name="instance"/>.
        /// <paramref name="prefabsDir"/> is the absolute path to the
        /// <c>AgentAssistant/prefabs/</c> folder. Returns a non-null
        /// <see cref="Result"/>; <c>Steps</c> may be empty when the prefab
        /// failed to parse or had zero step templates (errors recorded).
        /// </summary>
        public static Result Expand(PrefabInstance instance, string prefabsDir)
        {
            var result = new Result
            {
                Steps    = Array.Empty<StepDefinition>(),
                Errors   = new List<string>(),
                Warnings = new List<string>(),
            };

            if (instance == null || string.IsNullOrEmpty(instance.prefabId))
            {
                result.Errors.Add("PrefabInstance is null or has empty prefabId.");
                return result;
            }
            if (string.IsNullOrEmpty(instance.instanceId))
            {
                result.Errors.Add($"PrefabInstance for '{instance.prefabId}' has empty instanceId.");
                return result;
            }

            string prefabPath = ResolvePrefabPath(prefabsDir, instance.prefabId);
            if (string.IsNullOrEmpty(prefabPath))
            {
                result.Errors.Add(
                    $"Prefab '{instance.prefabId}' not found in '{prefabsDir}'. " +
                    "Drop the YAML there or fix the prefabId on the PrefabInstance.");
                return result;
            }

            YamlNode prefab;
            try
            {
                prefab = PrefabYamlReader.ReadFile(prefabPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Prefab '{instance.prefabId}' failed to parse: {ex.Message}");
                return result;
            }

            return ExpandParsed(instance, prefab, prefabPath, result);
        }

        /// <summary>Same as <see cref="Expand"/> but takes a pre-parsed YAML tree (used by the normalizer's parse cache).</summary>
        public static Result ExpandParsed(PrefabInstance instance, YamlNode prefab, string prefabPathForLogs, Result result = null)
        {
            result ??= new Result { Errors = new List<string>(), Warnings = new List<string>(), Steps = Array.Empty<StepDefinition>() };

            try
            {
                Dictionary<string, object> ctx = BuildContext(instance, prefab, result.Errors);
                if (result.Errors.Count > 0) return result;

                if (!prefab.TryGet("steps", out var stepsNode) || stepsNode == null || !stepsNode.IsSeq || stepsNode.Seq.Count == 0)
                {
                    result.Errors.Add($"Prefab '{instance.prefabId}' has no steps defined.");
                    return result;
                }

                if (string.IsNullOrEmpty(instance.prefix))
                {
                    result.Errors.Add($"Prefab instance '{instance.instanceId}' has empty prefix; step ids would collide.");
                    return result;
                }

                var emitted = new List<StepDefinition>(stepsNode.Seq.Count);
                int seq = instance.startSeq > 0 ? instance.startSeq : 1;

                for (int i = 0; i < stepsNode.Seq.Count; i++)
                {
                    YamlNode template = stepsNode.Seq[i];
                    if (template == null || !template.IsMap)
                    {
                        result.Errors.Add($"Prefab '{instance.prefabId}' step #{i} is not a map.");
                        continue;
                    }

                    string idSuffix = template.GetScalar("id_suffix", null);
                    if (string.IsNullOrEmpty(idSuffix))
                    {
                        result.Errors.Add($"Prefab '{instance.prefabId}' step #{i} missing 'id_suffix'.");
                        continue;
                    }

                    YamlNode rendered = RenderStep(template, ctx, instance, seq + i, result.Errors);
                    if (rendered == null) continue;

                    string json = rendered.ToJson();
                    StepDefinition step;
                    try
                    {
                        step = JsonUtility.FromJson<StepDefinition>(json);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Prefab '{instance.prefabId}' step '{idSuffix}' JSON parse failed: {ex.Message}\nJSON: {json}");
                        continue;
                    }
                    if (step == null)
                    {
                        result.Errors.Add($"Prefab '{instance.prefabId}' step '{idSuffix}' deserialised to null.");
                        continue;
                    }

                    StampProvenance(step, instance, prefabPathForLogs);
                    emitted.Add(step);
                }

                result.Steps = emitted.ToArray();
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Prefab '{instance.prefabId}' expansion threw: {ex.Message}");
                return result;
            }
        }

        // ── Context / role binding ────────────────────────────────────────

        private static Dictionary<string, object> BuildContext(
            PrefabInstance instance, YamlNode prefab, List<string> errors)
        {
            var ctx = new Dictionary<string, object>(StringComparer.Ordinal);

            // Bindings keyed by role name from the instance.
            var bindingsByRole = new Dictionary<string, PrefabRoleBinding>(StringComparer.Ordinal);
            if (instance.bindings != null)
            {
                foreach (var b in instance.bindings)
                {
                    if (b == null || string.IsNullOrEmpty(b.role)) continue;
                    bindingsByRole[b.role] = b;
                }
            }

            if (prefab.TryGet("roles", out var rolesNode) && rolesNode != null && rolesNode.IsMap)
            {
                foreach (var kv in rolesNode.Map)
                {
                    string roleName = kv.Key;
                    string kind     = kv.Value?.GetScalar("kind", "part") ?? "part";

                    if (!bindingsByRole.TryGetValue(roleName, out var binding))
                    {
                        errors.Add($"Instantiation '{instance.instanceId}' is missing role '{roleName}' (kind={kind}).");
                        continue;
                    }

                    if (string.Equals(kind, "part_list", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] items = binding.partIds ?? Array.Empty<string>();
                        int countLimit = 0;
                        string countStr = kv.Value?.GetScalar("count", null);
                        if (!string.IsNullOrEmpty(countStr) && int.TryParse(countStr, out int countParsed))
                            countLimit = countParsed;
                        if (countLimit > 0 && items.Length != countLimit)
                            errors.Add($"Role '{roleName}' expects {countLimit} entries, got {items.Length}.");
                        ctx[roleName] = new ListRole(items);
                    }
                    else
                    {
                        ctx[roleName] = binding.partId ?? string.Empty;
                    }
                }
            }

            // Options: instance overrides win, otherwise prefab default.
            var overrideByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            if (instance.options != null)
            {
                foreach (var o in instance.options)
                {
                    if (o == null || string.IsNullOrEmpty(o.key)) continue;
                    overrideByKey[o.key] = o.valueJson ?? string.Empty;
                }
            }

            if (prefab.TryGet("options", out var optsNode) && optsNode != null && optsNode.IsMap)
            {
                foreach (var kv in optsNode.Map)
                {
                    string optName = kv.Key;
                    if (overrideByKey.TryGetValue(optName, out string ov))
                    {
                        ctx[optName] = StripOptionalJsonQuotes(ov);
                    }
                    else
                    {
                        string def = kv.Value?.GetScalar("default", null);
                        if (def == null)
                        {
                            errors.Add($"Option '{optName}' has no default and no instantiation value.");
                            continue;
                        }
                        ctx[optName] = def;
                    }
                }
            }

            // Derived roles (concatenated lists). Slice 1 supports kind=part_list / combine: [...] only.
            if (prefab.TryGet("derived", out var derivedNode) && derivedNode != null && derivedNode.IsMap)
            {
                foreach (var kv in derivedNode.Map)
                {
                    string name = kv.Key;
                    string kind = kv.Value?.GetScalar("kind", "part_list") ?? "part_list";
                    if (!string.Equals(kind, "part_list", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Derived role '{name}' has unsupported kind '{kind}'.");
                        continue;
                    }
                    var items = new List<string>();
                    if (kv.Value != null && kv.Value.TryGet("combine", out var combineNode) && combineNode != null && combineNode.IsSeq)
                    {
                        foreach (var src in combineNode.Seq)
                        {
                            if (src == null || src.Scalar == null) continue;
                            if (!ctx.TryGetValue(src.Scalar, out var val))
                            {
                                errors.Add($"Derived role '{name}' references unknown role '{src.Scalar}'.");
                                continue;
                            }
                            if (val is ListRole lr) items.AddRange(lr.Items);
                            else if (val is string s) items.Add(s);
                            else items.Add(val?.ToString() ?? "");
                        }
                    }
                    ctx[name] = new ListRole(items.ToArray());
                }
            }

            return ctx;
        }

        // ── Step rendering ────────────────────────────────────────────────

        private static YamlNode RenderStep(
            YamlNode template, Dictionary<string, object> ctx, PrefabInstance instance, int seq, List<string> errors)
        {
            var rendered = template.Clone();
            // Strip the templating-only key — `id` and `sequenceIndex` are
            // derived from the instance + counter and inserted below.
            rendered.Map.Remove("id_suffix");

            // Substitute every value in-place. Map walking is depth-first so
            // arrays-of-objects get their inner scalars rewritten too.
            SubstituteInPlace(rendered, ctx, errors);

            // Inject derived id + sequenceIndex.
            string idSuffix = template.GetScalar("id_suffix", "");
            string id = $"step_{instance.prefix}_{idSuffix}";
            rendered.Map["id"]            = new YamlNode { Scalar = id };
            rendered.Map["sequenceIndex"] = new YamlNode { Scalar = seq.ToString() };

            // Echo the assembly + part-group from the instance — the runtime
            // groups steps by these and the navigator wants them filled.
            if (!string.IsNullOrEmpty(instance.assemblyId))
                rendered.Map["assemblyId"] = new YamlNode { Scalar = instance.assemblyId };
            if (!string.IsNullOrEmpty(instance.partGroupId))
                rendered.Map["partGroupId"] = new YamlNode { Scalar = instance.partGroupId };

            return rendered;
        }

        private static void SubstituteInPlace(YamlNode node, Dictionary<string, object> ctx, List<string> errors)
        {
            if (node == null) return;
            if (node.IsScalar)
            {
                node.Scalar = SubstituteScalar(node.Scalar, ctx, errors);
                return;
            }
            if (node.IsMap)
            {
                foreach (var kv in node.Map) SubstituteInPlace(kv.Value, ctx, errors);
                return;
            }
            if (node.IsSeq)
            {
                // Two-pass: first expand `{role}` / `*{role}` array-context
                // markers, then recurse into surviving elements for nested
                // substitution.
                var expanded = new List<YamlNode>(node.Seq.Count);
                foreach (var item in node.Seq)
                {
                    if (item != null && item.IsScalar)
                    {
                        string s = (item.Scalar ?? string.Empty).Trim();
                        if (s.StartsWith("*{", StringComparison.Ordinal) && s.EndsWith("}", StringComparison.Ordinal)
                            && s.IndexOf('{', 2) < 0)
                        {
                            string role = s.Substring(2, s.Length - 3);
                            if (!ctx.TryGetValue(role, out var val))
                            {
                                errors.Add($"Unknown role '{role}' in *{{{role}}} expansion.");
                                continue;
                            }
                            if (val is ListRole lr)
                            {
                                foreach (var e in lr.Items) expanded.Add(new YamlNode { Scalar = e });
                            }
                            else
                            {
                                errors.Add($"Role '{role}' is not a list — use {{{role}}} not *{{{role}}}.");
                            }
                            continue;
                        }
                        if (s.StartsWith("{", StringComparison.Ordinal) && s.EndsWith("}", StringComparison.Ordinal)
                            && s.IndexOf('{', 1) < 0)
                        {
                            string role = s.Substring(1, s.Length - 2);
                            if (!ctx.TryGetValue(role, out var val))
                            {
                                errors.Add($"Unknown role '{role}' in {{{role}}}.");
                                continue;
                            }
                            if (val is ListRole)
                            {
                                errors.Add($"Role '{role}' is a list — use *{{{role}}} to expand it in array context.");
                                continue;
                            }
                            expanded.Add(new YamlNode { Scalar = val?.ToString() ?? string.Empty });
                            continue;
                        }
                    }
                    expanded.Add(item);
                }
                node.Seq.Clear();
                node.Seq.AddRange(expanded);
                foreach (var item in node.Seq) SubstituteInPlace(item, ctx, errors);
            }
        }

        private static string SubstituteScalar(string s, Dictionary<string, object> ctx, List<string> errors)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('{') < 0) return s;
            var sb = new StringBuilder(s.Length + 16);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '{')
                {
                    int close = s.IndexOf('}', i + 1);
                    if (close < 0) { sb.Append(s, i, s.Length - i); break; }
                    string token = s.Substring(i + 1, close - i - 1);
                    int dot = token.IndexOf('.');
                    string role = dot < 0 ? token : token.Substring(0, dot);
                    string attr = dot < 0 ? null  : token.Substring(dot + 1);
                    if (!ctx.TryGetValue(role, out var val))
                    {
                        errors.Add($"Unknown role '{role}' in template string: {s}");
                        sb.Append(s, i, close - i + 1);
                    }
                    else if (string.IsNullOrEmpty(attr))
                    {
                        if (val is ListRole lr) sb.Append(string.Join(", ", lr.Items));
                        else sb.Append(val?.ToString() ?? string.Empty);
                    }
                    else if (string.Equals(attr, "count", StringComparison.Ordinal))
                    {
                        if (val is ListRole lr) sb.Append(lr.Count);
                        else { errors.Add($"Role '{role}' is not a list — `.count` undefined."); sb.Append('?'); }
                    }
                    else
                    {
                        errors.Add($"Unsupported attribute '.{attr}' on role '{role}'.");
                        sb.Append(s, i, close - i + 1);
                    }
                    i = close + 1;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        // ── Provenance + helpers ──────────────────────────────────────────

        private static void StampProvenance(StepDefinition step, PrefabInstance instance, string prefabPath)
        {
            try
            {
                step.prefabRef = new PrefabRef
                {
                    prefabId    = instance.prefabId,
                    instanceId  = instance.instanceId,
                    bindings    = CloneBindings(instance.bindings),
                    sourceMtime = !string.IsNullOrEmpty(prefabPath) && File.Exists(prefabPath)
                        ? File.GetLastWriteTimeUtc(prefabPath).ToString("o")
                        : null,
                };
            }
            catch (Exception ex)
            {
                OseLog.Warn($"[PrefabExpander] Failed stamping prefabRef on '{step?.id}': {ex.Message}");
            }
        }

        private static PrefabRoleBinding[] CloneBindings(PrefabRoleBinding[] src)
        {
            if (src == null) return null;
            var clone = new PrefabRoleBinding[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var b = src[i];
                if (b == null) continue;
                clone[i] = new PrefabRoleBinding
                {
                    role    = b.role,
                    partId  = b.partId,
                    partIds = b.partIds == null ? null : (string[])b.partIds.Clone(),
                };
            }
            return clone;
        }

        public static string ResolvePrefabPath(string prefabsDir, string prefabId)
        {
            if (string.IsNullOrEmpty(prefabsDir) || string.IsNullOrEmpty(prefabId)) return null;
            string yaml = Path.Combine(prefabsDir, prefabId + ".yaml");
            if (File.Exists(yaml)) return yaml;
            string yml = Path.Combine(prefabsDir, prefabId + ".yml");
            if (File.Exists(yml)) return yml;
            return null;
        }

        /// <summary>
        /// Returns the absolute path to the <c>AgentAssistant/prefabs/</c>
        /// folder. Editor-time only; in builds prefabs aren't loaded
        /// dynamically (instances are pre-baked or shipped via the same
        /// folder under the build root).
        /// </summary>
        public static string GetPrefabsDir()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AgentAssistant", "prefabs"));

        // Strip a single layer of JSON quoting around a string value so the
        // wizard's "milestone": "\"text\"" round-trips into the substituted
        // step as the bare text. Vector3 / object shapes (Slice 2) will be
        // parsed by their own typed handlers.
        private static string StripOptionalJsonQuotes(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private sealed class ListRole
        {
            public string[] Items { get; }
            public int      Count => Items?.Length ?? 0;
            public ListRole(string[] items) { Items = items ?? Array.Empty<string>(); }
            public override string ToString() => Items == null ? string.Empty : string.Join(", ", Items);
        }
    }
}
