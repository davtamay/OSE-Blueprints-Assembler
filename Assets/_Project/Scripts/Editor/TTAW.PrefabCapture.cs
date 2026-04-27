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
            w.Show();
            w.Focus();
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
            sb.AppendLine("# selected steps' requiredPartIds; rename in this file as needed.");
            sb.AppendLine("# TODO: finish taskOrder, requiredToolActions, hints, validationRules,");
            sb.AppendLine("# animationCues, particleEffects sections by hand for each step block.");
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

            // steps:
            sb.AppendLine("steps:");
            foreach (var step in _capturedSteps)
            {
                if (step == null) continue;
                string idSuffix = DeriveIdSuffix(step.id);
                sb.AppendLine($"  - id_suffix: {idSuffix}");
                if (!string.IsNullOrEmpty(step.family))  sb.AppendLine($"    family: {step.family}");
                if (!string.IsNullOrEmpty(step.profile)) sb.AppendLine($"    profile: {step.profile}");
                string instr = step.ResolvedInstructionText;
                if (!string.IsNullOrEmpty(instr))
                {
                    string escaped = instr.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"    instructionText: \"{escaped}\"");
                }
                if (!string.IsNullOrEmpty(step.requiredPartGroupId))
                    sb.AppendLine($"    requiredPartGroupId: {step.requiredPartGroupId}");

                if (step.requiredPartIds != null && step.requiredPartIds.Length > 0)
                {
                    sb.Append("    requiredPartIds: [");
                    for (int i = 0; i < step.requiredPartIds.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        string pid = step.requiredPartIds[i] ?? "";
                        sb.Append(partToRole.TryGetValue(pid, out var role) ? $"\"{{{role}}}\"" : pid);
                    }
                    sb.AppendLine("]");
                }
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
