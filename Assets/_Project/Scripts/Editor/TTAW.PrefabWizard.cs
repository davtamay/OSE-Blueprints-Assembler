// TTAW.PrefabWizard.cs — Role-binding modal for instantiating Step
// Configuration Prefabs (Slice 1c of the prefab drag-drop feature).
//
// Opens when an author drops a prefab from the PREFABS panel onto a step row
// in the Navigator. Reads the prefab YAML's `roles:` section, renders one
// part picker per role (pre-filled from the target step's requiredPartIds
// when the role name name-matches a part id stem), then writes a temporary
// instantiation YAML to AgentAssistant/inputs/.tmp_<guid>.yaml and runs
// instantiate_prefab.py via the existing subprocess plumbing in
// TTAW.Prefabs.cs. Output JSON lands in AgentAssistant/outputs/ for review
// and merge — auto-merge into the active assembly file is deferred (Slice
// 1d will add it alongside the linked-badge UI).

using System.Collections.Generic;
using System.IO;
using System.Text;
using OSE.Content;
using OSE.Core;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    internal sealed class PrefabWizardWindow : EditorWindow
    {
        // ── Wizard state (set by Open) ───────────────────────────────────────
        private ToolTargetAuthoringWindow _owner;
        private string  _prefabYamlPath;
        private string  _prefabName;
        private string  _prefabDescription;
        private string  _targetStepId;
        private StepDefinition _targetStep;
        private List<RoleSpec> _roles;
        private string _prefix;
        private int    _startSeq;

        private readonly Dictionary<string, string>   _singleBindings = new();
        private readonly Dictionary<string, string[]> _listBindings   = new();

        private Vector2 _scroll;
        private string  _statusMessage;
        private bool    _statusIsError;

        // ── Public entry ─────────────────────────────────────────────────────

        public static void Open(ToolTargetAuthoringWindow owner, string prefabYamlPath, string targetStepId,
            PrefabRoleBinding[] recordedBindings = null, int startSeqOverride = -1)
        {
            var w = GetWindow<PrefabWizardWindow>(true, "Instantiate Prefab", true);
            w.minSize = new Vector2(420, 320);
            w._owner             = owner;
            w._prefabYamlPath    = prefabYamlPath;
            w._targetStepId      = targetStepId;
            w._recordedBindings  = recordedBindings;
            w._startSeqOverride  = startSeqOverride;
            w.Initialise();
            w.Show();
            w.Focus();
        }

        // Bindings replayed when the author clicks Re-instantiate from the
        // linked banner — preserves the original role choices instead of
        // falling back to the heuristic name match against requiredPartIds.
        private PrefabRoleBinding[] _recordedBindings;
        // Drop-zone supplied start_seq (insert-before vs insert-after).
        // -1 = use the default (target step's sequenceIndex + 1).
        private int _startSeqOverride = -1;

        // ── Setup ────────────────────────────────────────────────────────────

        private void Initialise()
        {
            _prefabName        = Path.GetFileNameWithoutExtension(_prefabYamlPath ?? "");
            _prefabDescription = "";
            _roles             = new List<RoleSpec>();
            _singleBindings.Clear();
            _listBindings.Clear();
            _statusMessage     = null;

            if (!File.Exists(_prefabYamlPath))
            {
                _statusMessage = $"Prefab file not found: {_prefabYamlPath}";
                _statusIsError = true;
                return;
            }

            try
            {
                ParsePrefab(File.ReadAllText(_prefabYamlPath));
            }
            catch (System.Exception ex)
            {
                _statusMessage = $"Failed to parse prefab YAML: {ex.Message}";
                _statusIsError = true;
                return;
            }

            // Pre-fill from target step.
            _targetStep = _owner != null ? _owner.FindStepPublic(_targetStepId) : null;
            if (_targetStep != null)
            {
                _prefix = DerivePrefix(_targetStep.partGroupId ?? _targetStep.id ?? "");

                // Default start_seq priority:
                //   1. Explicit override (drop-divider top/bottom gestures
                //      pass the exact seq the author asked for).
                //   2. End-of-partGroup + 1 — when the target step has a
                //      partGroupId, "drop on this step" means "append to
                //      this partGroup," matching Unity's "drop on parent →
                //      add as last child" mental model.
                //   3. Fallback: target step's sequenceIndex + 1.
                if (_startSeqOverride > 0)
                {
                    _startSeq = _startSeqOverride;
                }
                else if (_owner != null && !string.IsNullOrEmpty(_targetStep.partGroupId))
                {
                    int subaMax = _owner.GetPartGroupMaxSeqPublic(_targetStep.partGroupId);
                    _startSeq = subaMax > 0 ? subaMax + 1 : _targetStep.sequenceIndex + 1;
                }
                else
                {
                    _startSeq = _targetStep.sequenceIndex + 1;
                }
            }
            else
            {
                _prefix   = "instance_" + System.Guid.NewGuid().ToString("N").Substring(0, 6);
                _startSeq = _startSeqOverride > 0 ? _startSeqOverride : 1;
            }

            // Recorded bindings (Re-instantiate flow) take precedence over the
            // heuristic — preserve the author's original role choices when
            // re-running the prefab to pull updates from the source YAML.
            Dictionary<string, PrefabRoleBinding> recorded = null;
            if (_recordedBindings != null && _recordedBindings.Length > 0)
            {
                recorded = new Dictionary<string, PrefabRoleBinding>(System.StringComparer.Ordinal);
                foreach (var b in _recordedBindings)
                    if (b != null && !string.IsNullOrEmpty(b.role)) recorded[b.role] = b;
            }

            // Heuristic role binding: scan the target step's required parts
            // for ids whose stem contains the role name (e.g. role "half_a"
            // matches "y_left_carriage_half_a"). Saves the author from
            // re-binding bolts/nuts that are already declared on the step.
            string[] candidateParts = _targetStep?.requiredPartIds ?? System.Array.Empty<string>();
            foreach (var role in _roles)
            {
                if (recorded != null && recorded.TryGetValue(role.Name, out var rec))
                {
                    if (role.IsList)
                    {
                        var src = rec.partIds ?? System.Array.Empty<string>();
                        int n = role.Count > 0 ? role.Count : src.Length;
                        var arr = new string[n];
                        for (int i = 0; i < n; i++) arr[i] = i < src.Length ? src[i] : "";
                        _listBindings[role.Name] = arr;
                    }
                    else
                    {
                        _singleBindings[role.Name] = rec.partId ?? "";
                    }
                    continue;
                }
                if (role.IsList)
                {
                    var picks = new List<string>();
                    foreach (var pid in candidateParts)
                        if (!string.IsNullOrEmpty(pid) && pid.IndexOf(role.Name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                            picks.Add(pid);
                    int n = role.Count > 0 ? role.Count : picks.Count;
                    var arr = new string[n];
                    for (int i = 0; i < n; i++) arr[i] = i < picks.Count ? picks[i] : "";
                    _listBindings[role.Name] = arr;
                }
                else
                {
                    string match = "";
                    foreach (var pid in candidateParts)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (pid.IndexOf(role.Name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        { match = pid; break; }
                    }
                    _singleBindings[role.Name] = match;
                }
            }
        }

        // ── GUI ──────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"Prefab: {_prefabName}",
                new GUIStyle(EditorStyles.largeLabel) { fontStyle = FontStyle.Bold });
            if (!string.IsNullOrEmpty(_prefabDescription))
                EditorGUILayout.LabelField(_prefabDescription, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField($"Target step: {_targetStepId ?? "(none)"}", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            _prefix = EditorGUILayout.TextField(new GUIContent("Prefix",
                "Prepended to every step ID emitted by this prefab. Step ids become " +
                "step_<prefix>_<id_suffix>. Defaults to the target step's partGroupId."),
                _prefix ?? "");
            _startSeq = EditorGUILayout.IntField(new GUIContent("Start seq",
                "First sequenceIndex assigned to the emitted steps. Defaults to target step + 1."),
                _startSeq, GUILayout.MaxWidth(180));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            EditorGUILayout.LabelField("Roles", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_roles == null || _roles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Prefab has no roles, or YAML failed to parse. See status at the bottom.",
                    MessageType.None);
            }
            else
            {
                foreach (var role in _roles) DrawRoleRow(role);
            }
            EditorGUILayout.EndScrollView();

            // Status / footer
            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage,
                    _statusIsError ? MessageType.Error : MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(96))) Close();
            using (new EditorGUI.DisabledScope(_roles == null || _roles.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Instantiate",
                        "Write a temp input YAML, run instantiate_prefab.py, and reveal " +
                        "the emitted step JSON in AgentAssistant/outputs/."),
                        GUILayout.Width(140)))
                {
                    RunInstantiation();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRoleRow(RoleSpec role)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                role.IsList ? $"{role.Name}  [list × {role.Count}]" : role.Name,
                new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });
            if (!string.IsNullOrEmpty(role.Description))
                EditorGUILayout.LabelField(role.Description, EditorStyles.wordWrappedMiniLabel);

            if (role.IsList)
            {
                if (!_listBindings.TryGetValue(role.Name, out var arr) || arr == null
                    || (role.Count > 0 && arr.Length != role.Count))
                {
                    int n = role.Count > 0 ? role.Count : 1;
                    arr = new string[n];
                    _listBindings[role.Name] = arr;
                }
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = EditorGUILayout.TextField($"  [{i}] partId", arr[i] ?? "");
            }
            else
            {
                _singleBindings.TryGetValue(role.Name, out var v);
                _singleBindings[role.Name] = EditorGUILayout.TextField("  partId", v ?? "");
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // ── Instantiation pipeline ───────────────────────────────────────────

        private void RunInstantiation()
        {
            if (string.IsNullOrEmpty(_prefix))
            {
                _statusMessage = "Prefix is required.";
                _statusIsError = true;
                return;
            }
            if (_startSeq < 1)
            {
                _statusMessage = "Start seq must be >= 1.";
                _statusIsError = true;
                return;
            }

            string tempInputPath = WriteTempInputYaml();
            if (string.IsNullOrEmpty(tempInputPath))
            {
                _statusMessage = "Failed to write temp input YAML.";
                _statusIsError = true;
                return;
            }

            // Subprocess inline (rather than going through
            // ToolTargetAuthoringWindow.RunInstantiatePrefabPublic) so the
            // wizard can capture the emitted JSON path and pipe it directly
            // into the auto-merge step below.
            string repoRoot     = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptPath   = Path.Combine(repoRoot, "Tools", "instantiate_prefab.py");
            string py           = ToolTargetAuthoringWindow.ResolvePythonExecutablePublic();
            if (string.IsNullOrEmpty(py))
            {
                _statusMessage = "Python interpreter not found on PATH.";
                _statusIsError = true;
                return;
            }
            if (!File.Exists(scriptPath))
            {
                _statusMessage = $"Engine missing: {scriptPath}";
                _statusIsError = true;
                return;
            }

            string outputPath = Path.Combine(repoRoot, "AgentAssistant", "outputs",
                Path.GetFileNameWithoutExtension(tempInputPath) + ".json");

            try
            {
                EditorUtility.DisplayProgressBar("Instantiate Prefab",
                    "Running instantiate_prefab.py …", 0.5f);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = py,
                    Arguments = $"\"{scriptPath}\" \"{tempInputPath}\"",
                    WorkingDirectory       = repoRoot,
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(30_000);
                    if (proc.ExitCode != 0)
                    {
                        _statusMessage = $"instantiate_prefab.py failed (exit {proc.ExitCode}). stderr: {stderr}";
                        _statusIsError = true;
                        OseLog.Warn($"[TTAW.PrefabWizard] Subprocess failed:\nstdout: {stdout}\nstderr: {stderr}");
                        return;
                    }
                }

                // Read emitted JSON, stamp prefabRef on each step, hand off to
                // owner for assembly-file merge + reload.
                if (!File.Exists(outputPath))
                {
                    _statusMessage = $"Subprocess succeeded but output JSON not found at: {outputPath}";
                    _statusIsError = true;
                    return;
                }
                string emitted = File.ReadAllText(outputPath);
                if (_owner != null)
                {
                    string instanceId = $"{_prefabName}_{_prefix}_{System.Guid.NewGuid().ToString("N").Substring(0, 6)}";
                    var bindings     = SnapshotBindings();
                    string mtime     = File.GetLastWriteTimeUtc(_prefabYamlPath).ToString("o");
                    int merged = _owner.MergeInstantiatedStepsPublic(
                        emitted, _prefabName, instanceId, bindings, mtime);
                    if (merged > 0)
                    {
                        _statusMessage = $"Merged {merged} step(s) into the active package. Reloading.";
                        _statusIsError = false;
                        Close();
                        return;
                    }
                    else
                    {
                        _statusMessage = "Subprocess succeeded but auto-merge produced 0 steps. See Console.";
                        _statusIsError = true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                _statusMessage = $"Instantiation failed: {ex.Message}";
                _statusIsError = true;
                OseLog.Warn($"[TTAW.PrefabWizard] Exception: {ex}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // Temp input + output kept on disk so author can inspect the
                // engine I/O. Housekeeping pass to clean .tmp_*.yaml is out
                // of scope for this slice.
            }
        }

        /// <summary>Captures the current role bindings as <see cref="PrefabRoleBinding"/> entries for the merged step's prefabRef.</summary>
        private PrefabRoleBinding[] SnapshotBindings()
        {
            var list = new List<PrefabRoleBinding>();
            foreach (var role in _roles)
            {
                if (role.IsList)
                {
                    if (!_listBindings.TryGetValue(role.Name, out var arr) || arr == null) continue;
                    list.Add(new PrefabRoleBinding { role = role.Name, partIds = arr });
                }
                else
                {
                    _singleBindings.TryGetValue(role.Name, out var v);
                    list.Add(new PrefabRoleBinding { role = role.Name, partId = v ?? "" });
                }
            }
            return list.ToArray();
        }

        private string WriteTempInputYaml()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Temp input written by TTAW PrefabWizard at {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"prefab: {_prefabName}");
            sb.AppendLine($"prefix: {_prefix}");
            sb.AppendLine($"start_seq: {_startSeq}");
            if (_targetStep != null)
            {
                if (!string.IsNullOrEmpty(_targetStep.assemblyId))
                    sb.AppendLine($"assembly: {_targetStep.assemblyId}");
                if (!string.IsNullOrEmpty(_targetStep.partGroupId))
                    sb.AppendLine($"partGroup: {_targetStep.partGroupId}");
            }
            sb.AppendLine("parts:");
            foreach (var role in _roles)
            {
                if (role.IsList)
                {
                    if (!_listBindings.TryGetValue(role.Name, out var arr) || arr == null) continue;
                    sb.Append($"  {role.Name}: [");
                    for (int i = 0; i < arr.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(arr[i] ?? "");
                    }
                    sb.AppendLine("]");
                }
                else
                {
                    _singleBindings.TryGetValue(role.Name, out var v);
                    sb.AppendLine($"  {role.Name}: {v ?? ""}");
                }
            }

            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string inputsDir = Path.Combine(repoRoot, "AgentAssistant", "inputs");
            try { Directory.CreateDirectory(inputsDir); } catch { }
            string tempPath = Path.Combine(inputsDir,
                $".tmp_{_prefabName}_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}.yaml");
            try { File.WriteAllText(tempPath, sb.ToString()); }
            catch (System.Exception ex)
            {
                OseLog.Warn($"[TTAW.PrefabWizard] Failed writing temp input '{tempPath}': {ex.Message}");
                return null;
            }
            return tempPath;
        }

        // ── Tiny YAML reader (covers just the prefab schema we need) ─────────

        private void ParsePrefab(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            bool inRoles = false;
            RoleSpec current = null;
            foreach (var rawLine in lines)
            {
                string line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line)) continue;

                int indent = CountLeadingSpaces(line);
                string trimmed = line.TrimStart();

                // Top-level keys
                if (indent == 0)
                {
                    inRoles = false; current = null;
                    if (trimmed.StartsWith("prefab:", System.StringComparison.Ordinal))
                    {
                        string v = trimmed.Substring("prefab:".Length).Trim();
                        if (!string.IsNullOrEmpty(v)) _prefabName = StripQuotes(v);
                    }
                    else if (trimmed.StartsWith("description:", System.StringComparison.Ordinal))
                    {
                        _prefabDescription = StripQuotes(trimmed.Substring("description:".Length).Trim());
                    }
                    else if (trimmed.StartsWith("roles:", System.StringComparison.Ordinal))
                    {
                        inRoles = true;
                    }
                    continue;
                }

                if (!inRoles) continue;

                // Role header line: "  <name>:"
                if (indent == 2 && trimmed.EndsWith(":", System.StringComparison.Ordinal))
                {
                    string name = trimmed.Substring(0, trimmed.Length - 1).Trim();
                    current = new RoleSpec { Name = name };
                    _roles.Add(current);
                    continue;
                }

                // Role attribute line: "    kind: part" / "    count: 4" / "    description: ..."
                if (indent == 4 && current != null)
                {
                    int colon = trimmed.IndexOf(':');
                    if (colon < 0) continue;
                    string key = trimmed.Substring(0, colon).Trim();
                    string val = trimmed.Substring(colon + 1).Trim();
                    val = StripQuotes(val);
                    switch (key)
                    {
                        case "kind":
                            current.IsList = string.Equals(val, "part_list", System.StringComparison.OrdinalIgnoreCase);
                            break;
                        case "count":
                            int.TryParse(val, out current.Count);
                            break;
                        case "description":
                            current.Description = val;
                            break;
                    }
                }
            }
        }

        private static string StripComment(string line)
        {
            // Lazy comment strip: split on ' #'. Doesn't handle '#' inside quoted strings
            // — fine for the schema we author. If a prefab description has a '#' it's
            // already enclosed in quotes; the leading char check below skips quoted lines.
            int idx = line.IndexOf(" #", System.StringComparison.Ordinal);
            if (idx < 0) return line.TrimEnd();
            return line.Substring(0, idx).TrimEnd();
        }

        private static int CountLeadingSpaces(string s)
        {
            int n = 0;
            while (n < s.Length && s[n] == ' ') n++;
            return n;
        }

        private static string StripQuotes(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') return s.Substring(1, s.Length - 2);
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') return s.Substring(1, s.Length - 2);
            return s;
        }

        private static string DerivePrefix(string idLike)
        {
            // Strip common "partGroup_" / "step_" prefixes; the prefab's
            // step ids will already prepend "step_<prefix>_<id_suffix>".
            string s = idLike ?? "";
            const string subPfx  = "partGroup_";
            const string stepPfx = "step_";
            if (s.StartsWith(subPfx,  System.StringComparison.Ordinal)) s = s.Substring(subPfx.Length);
            if (s.StartsWith(stepPfx, System.StringComparison.Ordinal)) s = s.Substring(stepPfx.Length);
            return s;
        }

        // Single role definition pulled from the prefab YAML.
        private sealed class RoleSpec
        {
            public string Name;
            public string Description;
            public bool   IsList;
            public int    Count;
        }
    }
}
