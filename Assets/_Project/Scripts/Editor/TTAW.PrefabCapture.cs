// TTAW.PrefabCapture.cs — "Capture as Prefab" flow (Slice 2 of the prefab
// drag-drop feature). Walks a cross-step selection, infers role bindings by
// grouping unique partIds, and writes a draft prefab YAML to
// AgentAssistant/prefabs/<name>.yaml.
//
// **Important scope note for the draft output:** the emitter covers the
// fields that round-trip cleanly through a prefab template — id_suffix,
// family, profile, instructionText, requiredPartIds (with {role} /
// *{role} placeholders), requiredPartGroupId. It does NOT yet rewrite
// taskOrder, requiredToolActions, animationCues, or particleEffects;
// emitted prefabs include a TODO banner pointing to the original captured
// step ids so the author can finish those sections manually.
//
// This is enough to capture simple Place / Confirm / Use sequences and
// re-instantiate them with different parts. Richer captures land in a
// follow-up slice once the YAML emitter stabilises.

using System.Collections.Generic;
using System.IO;
using System.Text;
using OSE.Content;
using OSE.Core;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    internal sealed class PrefabCaptureWindow : EditorWindow
    {
        private ToolTargetAuthoringWindow _owner;
        private List<StepDefinition>      _capturedSteps;
        private string  _prefabId;
        private string  _description;
        private Vector2 _scroll;
        private string  _statusMessage;
        private bool    _statusIsError;
        // Slice 2g — optional emission of partDefinitions + partGroupDefinition
        // sections so the captured prefab is self-contained (instantiating
        // it brings the parts AND the group AND the steps). Defaults
        // follow what the captured steps reference: if every role partId
        // resolves to a real part in the package, _includeParts defaults
        // true; if every step shares a single partGroupId, _includePartGroup
        // defaults true. Author can untick either to produce a steps-only
        // prefab that depends on the target package already supplying the
        // ingredients.
        private bool _includePartDefinitions = true;
        private bool _includePartGroupDef    = true;
        // Common partGroupId across the captured steps, if any. Used as
        // the partGroupDefinition.id substitution + as the implicit
        // membership check.
        private string _commonPartGroupId;

        // Inferred role table — partId → suggested role name. Author edits
        // role names in the modal; the emitter substitutes literal partIds
        // with the chosen role placeholders.
        private List<RoleSuggestion> _roles = new();

        public static void Open(ToolTargetAuthoringWindow owner, IList<StepDefinition> steps)
        {
            var w = GetWindow<PrefabCaptureWindow>(true, "Capture as Prefab", true);
            w.minSize = new Vector2(440, 360);
            w._owner          = owner;
            w._capturedSteps  = new List<StepDefinition>(steps);
            w._prefabId       = "NewPrefab";
            w._description    = "";
            w.InferRoles();
            w.InferCommonPartGroup();
            w.Show();
            w.Focus();
        }

        // Walks the captured steps and records the partGroupId every step
        // shares — empty when steps belong to different groups (or none).
        // Drives the default value of _includePartGroupDef and supplies
        // the partGroupDefinition.id used at emission time.
        private void InferCommonPartGroup()
        {
            _commonPartGroupId = null;
            if (_capturedSteps == null) return;
            string candidate = null;
            foreach (var s in _capturedSteps)
            {
                if (s == null) continue;
                if (string.IsNullOrEmpty(s.partGroupId))
                { _commonPartGroupId = null; return; }
                if (candidate == null) candidate = s.partGroupId;
                else if (!string.Equals(candidate, s.partGroupId, System.StringComparison.Ordinal))
                { _commonPartGroupId = null; return; }
            }
            _commonPartGroupId = candidate;
            if (string.IsNullOrEmpty(_commonPartGroupId))
                _includePartGroupDef = false;
        }

        // ── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Capture as Prefab",
                new GUIStyle(EditorStyles.largeLabel) { fontStyle = FontStyle.Bold });
            EditorGUILayout.LabelField($"Selection: {_capturedSteps?.Count ?? 0} step(s)", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            _prefabId    = EditorGUILayout.TextField(new GUIContent("Prefab ID",
                "Stem of the YAML file in AgentAssistant/prefabs/. PascalCase recommended."),
                _prefabId);
            _description = EditorGUILayout.TextField(new GUIContent("Description",
                "Short summary shown in the PREFABS panel and as a YAML comment."),
                _description);
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Sections to include", EditorStyles.boldLabel);
            _includePartDefinitions = EditorGUILayout.ToggleLeft(new GUIContent(
                $"partDefinitions  ({_roles.Count} role{(_roles.Count == 1 ? "" : "s")})",
                "Emit a partDefinitions: section so instantiating this prefab brings " +
                "the parts (with category / material / assetRef + inline placements). " +
                "Untick when the target package already declares these parts."),
                _includePartDefinitions);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_commonPartGroupId)))
            {
                _includePartGroupDef = EditorGUILayout.ToggleLeft(new GUIContent(
                    string.IsNullOrEmpty(_commonPartGroupId)
                        ? "partGroupDefinition  (steps span multiple groups — disabled)"
                        : $"partGroupDefinition  ({_commonPartGroupId})",
                    "Emit a partGroupDefinition: section so instantiating this prefab " +
                    "creates the part group too. Disabled when the captured steps " +
                    "belong to multiple groups (or none)."),
                    _includePartGroupDef);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Roles (rename to suit the prefab)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each unique part referenced by the selected steps becomes a role. " +
                "Edit the role name (left column); the YAML emitter substitutes the " +
                "literal partId with {roleName} in every step's requiredPartIds.",
                MessageType.None);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _roles.Count; i++)
            {
                var r = _roles[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                r.RoleName = EditorGUILayout.TextField(r.RoleName ?? "", GUILayout.MaxWidth(180));
                EditorGUILayout.LabelField($"→ {r.PartId}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                _roles[i] = r;
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage,
                    _statusIsError ? MessageType.Error : MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(96))) Close();
            if (GUILayout.Button(new GUIContent("Write Draft YAML",
                    "Writes a starter prefab YAML to AgentAssistant/prefabs/. The " +
                    "draft covers id_suffix, family, profile, instructionText, " +
                    "requiredPartIds, and requiredPartGroupId. Finish task " +
                    "ordering, hints, validation, and cues by hand."),
                    GUILayout.Width(160)))
            {
                WriteDraft();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Role inference ───────────────────────────────────────────────────

        private void InferRoles()
        {
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            _roles.Clear();
            if (_capturedSteps == null) return;

            int idx = 0;
            foreach (var s in _capturedSteps)
            {
                if (s == null) continue;
                if (s.requiredPartIds != null)
                {
                    foreach (var pid in s.requiredPartIds) AddRole(seen, pid, ref idx);
                }
                if (s.optionalPartIds != null)
                {
                    foreach (var pid in s.optionalPartIds) AddRole(seen, pid, ref idx);
                }
            }
        }

        private void AddRole(HashSet<string> seen, string partId, ref int idx)
        {
            if (string.IsNullOrEmpty(partId) || !seen.Add(partId)) return;
            _roles.Add(new RoleSuggestion
            {
                PartId   = partId,
                RoleName = SuggestRoleName(partId, idx++),
            });
        }

        private static string SuggestRoleName(string partId, int seq)
        {
            // First-pass suggestion: strip common prefixes, collapse to a
            // short alpha-numeric stem. Author renames before save.
            string s = partId ?? "";
            int us = s.LastIndexOf('_');
            string stem = us >= 0 && us < s.Length - 1 ? s.Substring(us + 1) : s;
            if (string.IsNullOrEmpty(stem)) stem = $"role_{seq}";
            return stem;
        }

        // ── Emission ─────────────────────────────────────────────────────────

        private void WriteDraft()
        {
            if (string.IsNullOrWhiteSpace(_prefabId))
            {
                _statusMessage = "Prefab ID is required.";
                _statusIsError = true;
                return;
            }
            if (_capturedSteps == null || _capturedSteps.Count == 0)
            {
                _statusMessage = "No steps captured.";
                _statusIsError = true;
                return;
            }

            // Build partId → roleName lookup so the emitter substitutes
            // literal partIds with placeholders.
            var partToRole = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var r in _roles)
                if (!string.IsNullOrEmpty(r.PartId) && !string.IsNullOrEmpty(r.RoleName))
                    partToRole[r.PartId] = r.RoleName;

            var sb = new StringBuilder();
            sb.AppendLine($"# Step Configuration Prefab — {_prefabId}");
            sb.AppendLine("# Captured from the TTAW canvas. Roles auto-extracted from the");
            sb.AppendLine("# selected steps' partId references; rename in this file as needed.");
            sb.AppendLine("# Coverage: id_suffix, name, family, profile, requiredPartGroupId,");
            sb.AppendLine("# requiredPartIds (with role substitution), optional/visualPartIds,");
            sb.AppendLine("# targetIds, requiredToolActions, guidance / validation / feedback");
            sb.AppendLine("# payloads. Animation cues, particle effects, taskOrder, working");
            sb.AppendLine("# orientation, and reinforcement / difficulty payloads are not yet");
            sb.AppendLine("# round-tripped — finish those sections by hand if needed.");
            sb.AppendLine($"# Source steps: {string.Join(", ", _capturedSteps.ConvertAll(s => s?.id ?? "?"))}");
            sb.AppendLine();
            sb.AppendLine($"prefab: {_prefabId}");
            if (!string.IsNullOrEmpty(_description))
                sb.AppendLine($"description: \"{_description.Replace("\"", "\\\"")}\"");
            sb.AppendLine();

            // roles:
            sb.AppendLine("roles:");
            foreach (var r in _roles)
            {
                sb.AppendLine($"  {r.RoleName}:");
                sb.AppendLine($"    kind: part");
                sb.AppendLine($"    description: \"Sourced from {r.PartId} during capture.\"");
            }
            sb.AppendLine();

            // partDefinitions: (Slice 2g — self-contained capture)
            if (_includePartDefinitions && _roles.Count > 0 && _owner != null)
            {
                EmitPartDefinitions(sb);
            }

            // partGroupDefinition: (Slice 2g)
            if (_includePartGroupDef && !string.IsNullOrEmpty(_commonPartGroupId) && _owner != null)
            {
                EmitPartGroupDefinition(sb);
            }

            // steps:
            sb.AppendLine("steps:");
            foreach (var step in _capturedSteps)
            {
                if (step == null) continue;
                EmitStepBlock(sb, step, partToRole);
                sb.AppendLine();
            }

            string repoRoot  = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string prefabsDir = Path.Combine(repoRoot, "AgentAssistant", "prefabs");
            try { Directory.CreateDirectory(prefabsDir); } catch { }
            string outPath = Path.Combine(prefabsDir, _prefabId + ".yaml");

            try { File.WriteAllText(outPath, sb.ToString()); }
            catch (System.Exception ex)
            {
                _statusMessage = $"Write failed: {ex.Message}";
                _statusIsError = true;
                return;
            }

            OseLog.Info($"[TTAW.PrefabCapture] Draft prefab written: {outPath}");
            _statusMessage = $"Wrote {outPath}. Open it to finish task ordering / cues.";
            _statusIsError = false;
            EditorUtility.RevealInFinder(outPath);
        }

        // Slice 2h — emit a single step block with full payload coverage:
        //   id_suffix, name, family, profile, requiredPartGroupId, *PartIds
        //   (with role substitution), requiredToolActions, guidance,
        //   validation, feedback. Produces payload-first YAML — flat
        //   legacy fields are NEVER emitted (per CLAUDE.md "new content
        //   uses payloads, not flat fields"). The capture's job is to
        //   produce content that loads back cleanly without manual cleanup.
        private void EmitStepBlock(StringBuilder sb, StepDefinition step,
            Dictionary<string, string> partToRole)
        {
            string idSuffix = DeriveIdSuffix(step.id);
            sb.AppendLine($"  - id_suffix: {idSuffix}");
            EmitScalarIfPresent(sb, "    name",    step.name);
            EmitScalarIfPresent(sb, "    family",  step.family);
            EmitScalarIfPresent(sb, "    profile", step.profile);
            EmitScalarIfPresent(sb, "    viewMode", step.viewMode);

            if (!string.IsNullOrEmpty(step.requiredPartGroupId))
                sb.AppendLine($"    requiredPartGroupId: {step.requiredPartGroupId}");

            EmitPartIdArray(sb, "    requiredPartIds", step.requiredPartIds, partToRole);
            EmitPartIdArray(sb, "    optionalPartIds", step.optionalPartIds, partToRole);
            EmitPartIdArray(sb, "    visualPartIds",   step.visualPartIds,   partToRole);
            EmitStringArray(sb, "    targetIds",       step.targetIds);
            EmitStringArray(sb, "    relevantToolIds", step.relevantToolIds);
            EmitStringArray(sb, "    eventTags",       step.eventTags);
            EmitStringArray(sb, "    removePersistentToolIds", step.removePersistentToolIds);

            // Required tool actions — emit as YAML sequence-of-maps so the
            // expander's per-step requiredToolActions reader picks them up.
            if (step.requiredToolActions != null && step.requiredToolActions.Length > 0)
            {
                sb.AppendLine("    requiredToolActions:");
                foreach (var ta in step.requiredToolActions)
                {
                    if (ta == null) continue;
                    sb.AppendLine($"      - toolId: \"{Esc(ta.toolId)}\"");
                    EmitScalarIfPresent(sb, "        actionType",     ta.actionType);
                    EmitScalarIfPresent(sb, "        targetId",       ta.targetId);
                    EmitScalarIfPresent(sb, "        successMessage", ta.successMessage);
                    EmitScalarIfPresent(sb, "        failureMessage", ta.failureMessage);
                    if (ta.requiredCount > 1)
                        sb.AppendLine($"        requiredCount: {ta.requiredCount}");
                }
            }

            // Capability payloads (payload-first per CLAUDE.md). Use the
            // Resolved* accessors so legacy flat-field captures still
            // round-trip into payload form — fixing the schema migration
            // for free.
            string instr = step.ResolvedInstructionText;
            string why   = step.ResolvedWhyItMattersText;
            string[] hints = step.ResolvedHintIds;
            bool hasGuidance = !string.IsNullOrEmpty(instr)
                            || !string.IsNullOrEmpty(why)
                            || (hints != null && hints.Length > 0);
            if (hasGuidance)
            {
                sb.AppendLine("    guidance:");
                EmitScalarIfPresent(sb, "      instructionText",   instr);
                EmitScalarIfPresent(sb, "      whyItMattersText", why);
                EmitStringArray   (sb, "      hintIds",           hints);
            }

            string[] validationRuleIds = step.ResolvedValidationRuleIds;
            if (validationRuleIds != null && validationRuleIds.Length > 0)
            {
                sb.AppendLine("    validation:");
                EmitStringArray(sb, "      validationRuleIds", validationRuleIds);
            }

            if (step.feedback != null && (
                (step.feedback.effectTriggerIds != null && step.feedback.effectTriggerIds.Length > 0)
                || !string.IsNullOrEmpty(step.feedback.completionEffectColor)
                || !string.IsNullOrEmpty(step.feedback.completionParticleId)
                || step.feedback.completionPulseScale != 0f))
            {
                sb.AppendLine("    feedback:");
                EmitStringArray   (sb, "      effectTriggerIds",     step.feedback.effectTriggerIds);
                EmitScalarIfPresent(sb, "      completionEffectColor", step.feedback.completionEffectColor);
                EmitScalarIfPresent(sb, "      completionParticleId",  step.feedback.completionParticleId);
                if (step.feedback.completionPulseScale != 0f)
                    sb.AppendLine($"      completionPulseScale: {F((float)step.feedback.completionPulseScale)}");
            }
        }

        private static void EmitScalarIfPresent(StringBuilder sb, string indentedKey, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            sb.AppendLine($"{indentedKey}: \"{Esc(value)}\"");
        }

        private static void EmitStringArray(StringBuilder sb, string indentedKey, string[] arr)
        {
            if (arr == null || arr.Length == 0) return;
            sb.Append(indentedKey).Append(": [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(Esc(arr[i] ?? "")).Append('"');
            }
            sb.AppendLine("]");
        }

        private static void EmitPartIdArray(StringBuilder sb, string indentedKey, string[] arr,
            Dictionary<string, string> partToRole)
        {
            if (arr == null || arr.Length == 0) return;
            sb.Append(indentedKey).Append(": [");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                string pid = arr[i] ?? "";
                if (partToRole.TryGetValue(pid, out var role))
                    sb.Append('"').Append('{').Append(role).Append('}').Append('"');
                else
                    sb.Append('"').Append(Esc(pid)).Append('"');
            }
            sb.AppendLine("]");
        }

        private static string Esc(string s) =>
            (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Slice 2g — emit one partDefinitions entry per role. Each entry
        // pulls category / material / assetRef from the live PartDefinition
        // and inline startPosition / assembledPosition from the matching
        // PartPreviewPlacement. Roles that don't resolve to a real part in
        // the package are skipped with a TODO comment so the author can
        // finish them by hand.
        private void EmitPartDefinitions(StringBuilder sb)
        {
            var pkg = _owner._pkgPublic;
            if (pkg == null) return;
            sb.AppendLine("partDefinitions:");
            foreach (var r in _roles)
            {
                if (string.IsNullOrEmpty(r.RoleName) || string.IsNullOrEmpty(r.PartId)) continue;
                if (!pkg.TryGetPart(r.PartId, out var part) || part == null)
                {
                    sb.AppendLine($"  # TODO: role '{r.RoleName}' references unknown partId '{r.PartId}'.");
                    continue;
                }
                sb.AppendLine($"  {r.RoleName}:");
                sb.AppendLine($"    kind: part");
                if (!string.IsNullOrEmpty(part.category)) sb.AppendLine($"    category:    {part.category}");
                if (!string.IsNullOrEmpty(part.material)) sb.AppendLine($"    material:    {part.material}");
                if (!string.IsNullOrEmpty(part.assetRef)) sb.AppendLine($"    assetRef:    \"{part.assetRef}\"");

                // Inline placement — pull startPosition / assembledPosition
                // from previewConfig.partPlacements. No translation /
                // offset adjustment: the captured positions are PreviewRoot-
                // local and round-trip directly.
                var placement = FindPlacement(pkg, r.PartId);
                if (placement != null)
                {
                    sb.AppendLine($"    startPosition:     {{ x: {F(placement.startPosition.x)}, y: {F(placement.startPosition.y)}, z: {F(placement.startPosition.z)} }}");
                    sb.AppendLine($"    assembledPosition: {{ x: {F(placement.assembledPosition.x)}, y: {F(placement.assembledPosition.y)}, z: {F(placement.assembledPosition.z)} }}");
                }
            }
            sb.AppendLine();
        }

        private void EmitPartGroupDefinition(StringBuilder sb)
        {
            var pkg = _owner._pkgPublic;
            if (pkg == null || string.IsNullOrEmpty(_commonPartGroupId)) return;
            string id = _commonPartGroupId;
            string name = id;
            string description = "";
            if (pkg.TryGetPartGroup(_commonPartGroupId, out var group) && group != null)
            {
                if (!string.IsNullOrEmpty(group.name))         name        = group.name;
                if (!string.IsNullOrEmpty(group.description))  description = group.description;
            }
            sb.AppendLine("partGroupDefinition:");
            sb.AppendLine($"  id: \"{id}\"");
            sb.AppendLine($"  name: \"{name.Replace("\"", "\\\"")}\"");
            if (!string.IsNullOrEmpty(description))
                sb.AppendLine($"  description: \"{description.Replace("\"", "\\\"")}\"");
            sb.AppendLine();
        }

        private static OSE.Content.PartPreviewPlacement FindPlacement(MachinePackageDefinition pkg, string partId)
        {
            var placements = pkg?.previewConfig?.partPlacements;
            if (placements == null) return null;
            for (int i = 0; i < placements.Length; i++)
            {
                var pp = placements[i];
                if (pp != null && string.Equals(pp.partId, partId, System.StringComparison.Ordinal)) return pp;
            }
            return null;
        }

        // Round to 4 decimal places (matches the package's float-precision
        // policy) and emit using the invariant culture so the YAML
        // doesn't accidentally pick up a comma decimal separator on
        // non-English Windows locales.
        private static string F(float v)
            => System.Math.Round((double)v, 4)
                .ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static string DeriveIdSuffix(string stepId)
        {
            // Strip the conventional "step_<prefix>_" envelope so the suffix
            // becomes the prefab-internal id. Fall back to the raw id when
            // it doesn't fit the convention.
            string s = stepId ?? "";
            if (!s.StartsWith("step_", System.StringComparison.Ordinal)) return s;
            int dash = s.IndexOf('_', "step_".Length);
            return dash >= 0 && dash < s.Length - 1 ? s.Substring(dash + 1) : s.Substring("step_".Length);
        }

        private struct RoleSuggestion
        {
            public string RoleName;
            public string PartId;
        }
    }
}
