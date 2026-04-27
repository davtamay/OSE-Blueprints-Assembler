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
            public StepDefinition[]      Steps;
            public PartDefinition[]      Parts;
            public PartPreviewPlacement[] Placements;
            public PartGroupDefinition[] PartGroups;
            public List<string>          Errors;
            public List<string>          Warnings;
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
                Steps      = Array.Empty<StepDefinition>(),
                Parts      = Array.Empty<PartDefinition>(),
                Placements = Array.Empty<PartPreviewPlacement>(),
                PartGroups = Array.Empty<PartGroupDefinition>(),
                Errors     = new List<string>(),
                Warnings   = new List<string>(),
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
            result ??= new Result {
                Errors = new List<string>(), Warnings = new List<string>(),
                Steps = Array.Empty<StepDefinition>(),
                Parts = Array.Empty<PartDefinition>(),
                Placements = Array.Empty<PartPreviewPlacement>(),
                PartGroups = Array.Empty<PartGroupDefinition>(),
            };

            try
            {
                Dictionary<string, object> ctx = BuildContext(instance, prefab, result.Errors);
                if (result.Errors.Count > 0) return result;

                // Slice 2 sections — additive, all optional. Walk them
                // BEFORE steps so the steps' role substitutions can refer to
                // partIds bound from partDefinitions if needed (Slice 1
                // bindings are typically the source though). The normalizer
                // merges the emitted entities into the package alongside
                // the virtual steps below.
                ExpandPartDefinitions(instance, prefab, ctx, prefabPathForLogs, result);
                ExpandPartGroupDefinition(instance, prefab, ctx, prefabPathForLogs, result);

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
            try { step.prefabRef = MakePrefabRef(instance, prefabPath); }
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

        // ── Slice 2: partDefinitions section ──────────────────────────────

        // Walks the optional `partDefinitions:` section and emits one
        // PartDefinition per role-bound partId, plus a sibling
        // PartPreviewPlacement per part. Single-part roles consume a single
        // `startPosition` / `assembledPosition`; part_list roles consume a
        // `placements:` array indexed by binding order. Each emitted
        // placement is offset by the resolved `placementOffset` option (if
        // present) so the same prefab can drop multiple identical instances
        // at distinct bench positions without the YAML changing.
        private static void ExpandPartDefinitions(
            PrefabInstance instance, YamlNode prefab, Dictionary<string, object> ctx,
            string prefabPath, Result result)
        {
            if (!prefab.TryGet("partDefinitions", out var defsNode) || defsNode == null || !defsNode.IsMap)
                return;

            Vector3 offset = ResolveVector3Option(prefab, instance, "placementOffset");
            var parts      = new List<PartDefinition>();
            var placements = new List<PartPreviewPlacement>();

            foreach (var kv in defsNode.Map)
            {
                string roleName = kv.Key;
                YamlNode def    = kv.Value;
                if (def == null || !def.IsMap)
                {
                    result.Errors.Add($"partDefinitions.{roleName}: expected a map.");
                    continue;
                }
                if (!ctx.TryGetValue(roleName, out var bound))
                {
                    result.Errors.Add($"partDefinitions.{roleName}: no role binding found for this name.");
                    continue;
                }

                string kind     = def.GetScalar("kind", "part") ?? "part";
                string category = SubstituteScalar(def.GetScalar("category", null), ctx, result.Errors);
                string material = SubstituteScalar(def.GetScalar("material", null), ctx, result.Errors);
                string assetTpl = def.GetScalar("assetRef", null);

                if (string.Equals(kind, "part_list", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(bound is ListRole list))
                    {
                        result.Errors.Add($"partDefinitions.{roleName}: kind=part_list but role binding is not a list.");
                        continue;
                    }

                    List<YamlNode> placementSeq = null;
                    if (def.TryGet("placements", out var pn) && pn != null && pn.IsSeq)
                        placementSeq = pn.Seq;

                    for (int i = 0; i < list.Items.Length; i++)
                    {
                        string pid = list.Items[i];
                        if (string.IsNullOrEmpty(pid)) continue;

                        YamlNode pNode = (placementSeq != null && i < placementSeq.Count) ? placementSeq[i] : null;
                        var placement = BuildPlacement(pid, pNode, offset, ctx, result.Errors,
                            instance.prefabId, roleName, i);
                        placements.Add(placement);
                        parts.Add(BuildPartDefinition(pid, category, material,
                            ResolveAssetRef(assetTpl, roleName, pid, ctx, result.Errors),
                            placement.startPosition, instance, prefabPath));
                    }
                }
                else
                {
                    if (!(bound is string single) || string.IsNullOrEmpty(single))
                    {
                        result.Errors.Add($"partDefinitions.{roleName}: kind=part but role binding is empty / not a string.");
                        continue;
                    }
                    var placement = BuildPlacement(single, def, offset, ctx, result.Errors,
                        instance.prefabId, roleName, -1);
                    placements.Add(placement);
                    parts.Add(BuildPartDefinition(single, category, material,
                        ResolveAssetRef(assetTpl, roleName, single, ctx, result.Errors),
                        placement.startPosition, instance, prefabPath));
                }
            }

            if (parts.Count > 0)      result.Parts      = parts.ToArray();
            if (placements.Count > 0) result.Placements = placements.ToArray();
        }

        private static PartDefinition BuildPartDefinition(
            string partId, string category, string material, string assetRef,
            SceneFloat3 startPosition, PrefabInstance instance, string prefabPath)
        {
            // Mirror the start position into stagingPose so the part
            // round-trips through Bake → save → reload: the loader's
            // BakeStagingPoses pass overwrites previewConfig.partPlacements
            // [].startPosition from this field, so authored content stays
            // anchored to where the prefab placed it.
            var part = new PartDefinition
            {
                id          = partId,
                category    = category,
                material    = material,
                assetRef    = assetRef,
                stagingPose = new StagingPose { position = startPosition },
                prefabRef   = MakePrefabRef(instance, prefabPath),
            };
            return part;
        }

        private static PartPreviewPlacement BuildPlacement(
            string partId, YamlNode source, Vector3 offset,
            Dictionary<string, object> ctx, List<string> errors,
            string prefabId, string roleName, int index)
        {
            SceneFloat3 start     = ReadFloat3(source, "startPosition",     errors, prefabId, roleName, index);
            SceneFloat3 assembled = ReadFloat3(source, "assembledPosition", errors, prefabId, roleName, index);
            start     = ApplyOffset(start, offset);
            assembled = ApplyOffset(assembled, offset);
            return new PartPreviewPlacement
            {
                partId            = partId,
                startPosition     = start,
                assembledPosition = assembled,
            };
        }

        private static SceneFloat3 ReadFloat3(
            YamlNode source, string key, List<string> errors,
            string prefabId, string roleName, int index)
        {
            if (source == null || !source.IsMap || !source.TryGet(key, out var node) || node == null || !node.IsMap)
                return new SceneFloat3();
            float x = ParseFloat(node.GetScalar("x", "0"));
            float y = ParseFloat(node.GetScalar("y", "0"));
            float z = ParseFloat(node.GetScalar("z", "0"));
            return new SceneFloat3 { x = x, y = y, z = z };
        }

        private static float ParseFloat(string s)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        private static SceneFloat3 ApplyOffset(SceneFloat3 v, Vector3 offset)
            => new SceneFloat3 { x = v.x + offset.x, y = v.y + offset.y, z = v.z + offset.z };

        private static string ResolveAssetRef(string template, string roleName, string partId,
            Dictionary<string, object> ctx, List<string> errors)
        {
            if (string.IsNullOrEmpty(template)) return null;
            // Common shape: "assets/parts/{<role>}.glb" — substitute via the
            // shared scalar substituter so {role} expands to the bound id.
            return SubstituteScalar(template, ctx, errors);
        }

        // ── Slice 2: partGroupDefinition section ──────────────────────────

        // Walks the optional `partGroupDefinition:` section and emits a
        // single PartGroupDefinition. partIds default to the union of
        // every partId resolved from `partDefinitions` (so the author
        // doesn't have to hand-list role members); explicit `partIds:`
        // overrides this.
        private static void ExpandPartGroupDefinition(
            PrefabInstance instance, YamlNode prefab, Dictionary<string, object> ctx,
            string prefabPath, Result result)
        {
            if (!prefab.TryGet("partGroupDefinition", out var defNode) || defNode == null || !defNode.IsMap)
                return;

            string id          = SubstituteScalar(defNode.GetScalar("id", null), ctx, result.Errors);
            string name        = SubstituteScalar(defNode.GetScalar("name", null), ctx, result.Errors);
            string description = SubstituteScalar(defNode.GetScalar("description", null), ctx, result.Errors);

            if (string.IsNullOrEmpty(id))
            {
                if (!string.IsNullOrEmpty(instance.partGroupId)) id = instance.partGroupId;
                else
                {
                    result.Errors.Add($"partGroupDefinition.id is empty and instance.partGroupId is unset.");
                    return;
                }
            }

            string[] partIds;
            if (defNode.TryGet("partIds", out var pidNode) && pidNode != null && pidNode.IsSeq)
            {
                var explicitIds = new List<string>(pidNode.Seq.Count);
                foreach (var e in pidNode.Seq)
                    if (e != null && !string.IsNullOrEmpty(e.Scalar))
                        explicitIds.Add(SubstituteScalar(e.Scalar, ctx, result.Errors));
                partIds = explicitIds.ToArray();
            }
            else
            {
                // Default: union of every partId emitted by partDefinitions.
                var allIds = new List<string>();
                if (result.Parts != null)
                    foreach (var p in result.Parts)
                        if (p != null && !string.IsNullOrEmpty(p.id)) allIds.Add(p.id);
                partIds = allIds.ToArray();
            }

            var group = new PartGroupDefinition
            {
                id          = id,
                name        = string.IsNullOrEmpty(name) ? id : name,
                assemblyId  = instance.assemblyId,
                description = description,
                partIds     = partIds,
                prefabRef   = MakePrefabRef(instance, prefabPath),
            };
            result.PartGroups = new[] { group };

            // Echo the group id back onto the instance so steps emitted
            // later carry the right partGroupId without the wizard having
            // to know it. Authored instances that already pin a partGroupId
            // win — author intent first.
            if (string.IsNullOrEmpty(instance.partGroupId))
                instance.partGroupId = id;
        }

        // ── Slice 2: typed option resolution ──────────────────────────────

        // Looks up an option of declared type=vector3, returning Vector3.zero
        // when the option is absent or any field fails to parse. Instance
        // values are JSON-encoded vector3 objects (`{"x":..,"y":..,"z":..}`);
        // the prefab default may be either the same JSON shape or an inline
        // YAML map — both decoded here.
        private static Vector3 ResolveVector3Option(YamlNode prefab, PrefabInstance instance, string optionName)
        {
            if (string.IsNullOrEmpty(optionName)) return Vector3.zero;

            // Instance override first.
            if (instance.options != null)
            {
                foreach (var o in instance.options)
                {
                    if (o == null || !string.Equals(o.key, optionName, StringComparison.Ordinal)) continue;
                    if (TryParseVector3Json(o.valueJson, out var v)) return v;
                }
            }

            // Prefab default.
            if (prefab.TryGet("options", out var optsNode) && optsNode != null && optsNode.IsMap
                && optsNode.Map.TryGetValue(optionName, out var optDecl) && optDecl != null && optDecl.IsMap
                && optDecl.TryGet("default", out var defNode) && defNode != null)
            {
                if (defNode.IsMap)
                {
                    return new Vector3(
                        ParseFloat(defNode.GetScalar("x", "0")),
                        ParseFloat(defNode.GetScalar("y", "0")),
                        ParseFloat(defNode.GetScalar("z", "0")));
                }
                if (defNode.IsScalar && TryParseVector3Json(defNode.Scalar, out var v))
                    return v;
            }

            return Vector3.zero;
        }

        private static bool TryParseVector3Json(string json, out Vector3 v)
        {
            v = Vector3.zero;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                var parsed = UnityEngine.JsonUtility.FromJson<SceneFloat3>(json);
                v = new Vector3(parsed.x, parsed.y, parsed.z);
                return true;
            }
            catch { return false; }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static PrefabRef MakePrefabRef(PrefabInstance instance, string prefabPath)
        {
            return new PrefabRef
            {
                prefabId    = instance.prefabId,
                instanceId  = instance.instanceId,
                bindings    = CloneBindings(instance.bindings),
                sourceMtime = !string.IsNullOrEmpty(prefabPath) && File.Exists(prefabPath)
                    ? File.GetLastWriteTimeUtc(prefabPath).ToString("o")
                    : null,
            };
        }

        // ── Summary helpers (UI scaffolding) ───────────────────────────────

        /// <summary>
        /// Layer-count breakdown for a prefab YAML. Used by editor surfaces
        /// (PREFABS panel rows, wizard title, linked banner) to show the
        /// author exactly which package layers each prefab brings without
        /// having to peek inside the YAML. Self-contained prefabs surface
        /// non-zero <see cref="PartCount"/> + <see cref="PartGroupCount"/>;
        /// procedure-only prefabs surface only <see cref="StepCount"/>.
        /// </summary>
        public sealed class Summary
        {
            public int  StepCount;
            public int  PartCount;
            public int  PartGroupCount;
            public bool ParseFailed;

            /// <summary>True when the prefab brings parts and / or a part group — not just steps.</summary>
            public bool IsSelfContained => PartCount > 0 || PartGroupCount > 0;

            /// <summary>
            /// One-line human description of the layers the prefab emits.
            /// Examples:
            ///   "1 step  ·  uses existing parts"
            ///   "7 steps  ·  uses existing parts"
            ///   "7 steps + 14 parts + 1 part group  ·  self-contained"
            /// </summary>
            public string FormatSummaryLine()
            {
                if (ParseFailed) return "(prefab failed to parse)";
                var sb = new StringBuilder();
                AppendCount(sb, StepCount, "step");
                if (PartCount > 0)
                {
                    if (sb.Length > 0) sb.Append(" + ");
                    AppendCount(sb, PartCount, "part");
                }
                if (PartGroupCount > 0)
                {
                    if (sb.Length > 0) sb.Append(" + ");
                    AppendCount(sb, PartGroupCount, "part group");
                }
                if (sb.Length == 0) sb.Append("(empty)");
                sb.Append("  ·  ");
                sb.Append(IsSelfContained ? "self-contained" : "uses existing parts");
                return sb.ToString();
            }

            private static void AppendCount(StringBuilder sb, int n, string singular)
            {
                if (n == 0) return;
                sb.Append(n).Append(' ').Append(singular);
                if (n != 1) sb.Append('s');
            }
        }

        /// <summary>
        /// Reads a prefab YAML and counts the layers it emits per instance —
        /// useful for editor UI without paying the full expansion cost.
        /// Roles + bindings are unresolved (the prefab is the data, not an
        /// instance), so list-role parts are counted by their declared
        /// <c>count:</c>; absent counts default to 1.
        /// </summary>
        public static Summary Analyze(string prefabYamlPath)
        {
            var summary = new Summary();
            if (string.IsNullOrEmpty(prefabYamlPath) || !File.Exists(prefabYamlPath))
            {
                summary.ParseFailed = true;
                return summary;
            }
            try
            {
                var root = PrefabYamlReader.ReadFile(prefabYamlPath);
                if (root == null || !root.IsMap) { summary.ParseFailed = true; return summary; }

                if (root.TryGet("steps", out var stepsNode) && stepsNode != null && stepsNode.IsSeq)
                    summary.StepCount = stepsNode.Seq.Count;

                if (root.TryGet("partGroupDefinition", out var pg) && pg != null && pg.IsMap)
                    summary.PartGroupCount = 1;

                if (root.TryGet("partDefinitions", out var defs) && defs != null && defs.IsMap)
                {
                    foreach (var kv in defs.Map)
                    {
                        if (kv.Value == null || !kv.Value.IsMap) continue;
                        string kind = kv.Value.GetScalar("kind", "part") ?? "part";
                        if (string.Equals(kind, "part_list", StringComparison.OrdinalIgnoreCase))
                        {
                            string c = kv.Value.GetScalar("count", null);
                            if (int.TryParse(c, out int n) && n > 0) summary.PartCount += n;
                            else if (kv.Value.TryGet("placements", out var pls) && pls != null && pls.IsSeq)
                                summary.PartCount += pls.Seq.Count;
                            else summary.PartCount += 1;
                        }
                        else
                        {
                            summary.PartCount += 1;
                        }
                    }
                }
            }
            catch
            {
                summary.ParseFailed = true;
            }
            return summary;
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
