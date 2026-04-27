// TTAW.PrefabWizard.cs — Role-binding modal for instantiating Step
// Configuration Prefabs.
//
// Opens when an author drops a prefab from the PREFABS panel onto a step row
// in the Navigator. Reads the prefab YAML's `roles:` section (via the shared
// PrefabYamlReader), renders one part picker per role pre-filled from the
// target step's requiredPartIds, then on Confirm builds a PrefabInstance and
// hands it to ToolTargetAuthoringWindow.MergePrefabInstancePublic. The
// merge is in-memory only — disk writes happen on the next "Write to
// machine.json" press, by which point the user has had a chance to inspect
// the virtual steps in the canvas.

using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Content.Loading;
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
        private List<OptionSpec> _options;
        private string _prefix;
        private int    _startSeq;

        private readonly Dictionary<string, string>   _singleBindings = new();
        private readonly Dictionary<string, string[]> _listBindings   = new();
        // Per-instance option values keyed by option name. String values
        // are stored verbatim; vector3 values are stored as their
        // JSON-encoded SceneFloat3 shape so the in-memory representation
        // round-trips into the PrefabInstance.options array unchanged.
        private readonly Dictionary<string, string>   _stringOptionValues  = new();
        private readonly Dictionary<string, Vector3>  _vector3OptionValues = new();

        private Vector2 _scroll;
        private string  _statusMessage;
        private bool    _statusIsError;

        // ── Public entry ─────────────────────────────────────────────────────

        private PrefabExpander.Summary _summary;

        public static void Open(ToolTargetAuthoringWindow owner, string prefabYamlPath, string targetStepId,
            PrefabRoleBinding[] recordedBindings = null, int startSeqOverride = -1)
        {
            var summary = PrefabExpander.Analyze(prefabYamlPath);
            string name = Path.GetFileNameWithoutExtension(prefabYamlPath ?? "");
            string title = string.IsNullOrEmpty(name)
                ? "Instantiate Prefab"
                : $"Instantiate {name}  ·  {summary.FormatSummaryLine()}";
            var w = GetWindow<PrefabWizardWindow>(true, title, true);
            w._summary = summary;
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
            _options           = new List<OptionSpec>();
            _singleBindings.Clear();
            _listBindings.Clear();
            _stringOptionValues.Clear();
            _vector3OptionValues.Clear();
            _statusMessage     = null;

            if (!File.Exists(_prefabYamlPath))
            {
                _statusMessage = $"Prefab file not found: {_prefabYamlPath}";
                _statusIsError = true;
                return;
            }

            try
            {
                ParsePrefabRoles(PrefabYamlReader.ReadFile(_prefabYamlPath));
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
            if (_summary != null)
                EditorGUILayout.LabelField(_summary.FormatSummaryLine(), EditorStyles.miniLabel);
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

            if (_options != null && _options.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
                foreach (var opt in _options) DrawOptionRow(opt);
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
                        "Append a PrefabInstance to the active package and expand it into " +
                        "virtual steps in memory. Disk save happens on the next " +
                        "\"Write to machine.json\" press."),
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

        private void DrawOptionRow(OptionSpec opt)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"{opt.Name}  [{opt.Type}]",
                new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });
            if (!string.IsNullOrEmpty(opt.Description))
                EditorGUILayout.LabelField(opt.Description, EditorStyles.wordWrappedMiniLabel);

            if (string.Equals(opt.Type, "vector3", System.StringComparison.Ordinal))
            {
                _vector3OptionValues.TryGetValue(opt.Name, out var v);
                _vector3OptionValues[opt.Name] =
                    EditorGUILayout.Vector3Field("  value", v);
            }
            else
            {
                _stringOptionValues.TryGetValue(opt.Name, out var s);
                _stringOptionValues[opt.Name] = EditorGUILayout.TextField("  value", s ?? "");
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
            if (_owner == null)
            {
                _statusMessage = "Owning TTAW window is gone — close and re-open the wizard.";
                _statusIsError = true;
                return;
            }

            try
            {
                var instance = new PrefabInstance
                {
                    prefabId    = _prefabName,
                    instanceId  = $"{_prefabName}_{_prefix}_{System.Guid.NewGuid().ToString("N").Substring(0, 6)}",
                    prefix      = _prefix,
                    startSeq    = _startSeq,
                    assemblyId  = _targetStep?.assemblyId,
                    partGroupId = _targetStep?.partGroupId,
                    bindings    = SnapshotBindings(),
                    options     = SnapshotOptions(),
                };

                int merged = _owner.MergePrefabInstancePublic(instance);
                if (merged > 0)
                {
                    _statusMessage = $"Expanded {merged} virtual step(s). Press \"Write to machine.json\" to persist.";
                    _statusIsError = false;
                    Close();
                    return;
                }

                _statusMessage = "Merge produced 0 steps — see Console for prefab parse / role-binding errors.";
                _statusIsError = true;
            }
            catch (System.Exception ex)
            {
                _statusMessage = $"Instantiation failed: {ex.Message}";
                _statusIsError = true;
                OseLog.Warn($"[TTAW.PrefabWizard] Exception: {ex}");
            }
        }

        /// <summary>
        /// Captures every option value that differs from its prefab default
        /// into a <see cref="PrefabOptionValue"/> array. Vector3 values are
        /// JSON-encoded as <c>SceneFloat3</c>; string values pass through as
        /// JSON-quoted scalars. Defaults are omitted so the on-disk
        /// PrefabInstance stays minimal — the expander pulls the default
        /// from the prefab YAML.
        /// </summary>
        private PrefabOptionValue[] SnapshotOptions()
        {
            if (_options == null || _options.Count == 0) return null;
            var list = new List<PrefabOptionValue>();
            foreach (var opt in _options)
            {
                if (string.Equals(opt.Type, "vector3", System.StringComparison.Ordinal))
                {
                    if (!_vector3OptionValues.TryGetValue(opt.Name, out var v)) continue;
                    if (v == opt.DefaultVector3) continue;
                    var sf3 = new OSE.Content.SceneFloat3 { x = v.x, y = v.y, z = v.z };
                    list.Add(new PrefabOptionValue { key = opt.Name, valueJson = JsonUtility.ToJson(sf3) });
                }
                else
                {
                    if (!_stringOptionValues.TryGetValue(opt.Name, out var s)) continue;
                    if (string.Equals(s, opt.DefaultString, System.StringComparison.Ordinal)) continue;
                    list.Add(new PrefabOptionValue { key = opt.Name, valueJson = "\"" + (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" });
                }
            }
            return list.Count == 0 ? null : list.ToArray();
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

        // Pulls the prefab's name + description + role list + options out of
        // the shared YamlNode tree. Step + derived sections are consumed at
        // expansion time by <see cref="PrefabExpander"/>; the wizard only
        // needs roles + options to render its picker.
        private void ParsePrefabRoles(YamlNode root)
        {
            if (root == null || !root.IsMap) return;
            string prefab = root.GetScalar("prefab", null);
            if (!string.IsNullOrEmpty(prefab)) _prefabName = prefab;
            string desc = root.GetScalar("description", null);
            if (!string.IsNullOrEmpty(desc)) _prefabDescription = desc;

            if (root.TryGet("roles", out var rolesNode) && rolesNode != null && rolesNode.IsMap)
            {
                foreach (var kv in rolesNode.Map)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || !kv.Value.IsMap) continue;
                    var spec = new RoleSpec { Name = kv.Key };
                    string kind = kv.Value.GetScalar("kind", "part") ?? "part";
                    spec.IsList = string.Equals(kind, "part_list", System.StringComparison.OrdinalIgnoreCase);
                    if (int.TryParse(kv.Value.GetScalar("count", "0"), out int count)) spec.Count = count;
                    spec.Description = kv.Value.GetScalar("description", "") ?? "";
                    _roles.Add(spec);
                }
            }

            if (root.TryGet("options", out var optsNode) && optsNode != null && optsNode.IsMap)
            {
                foreach (var kv in optsNode.Map)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || !kv.Value.IsMap) continue;
                    var opt = new OptionSpec
                    {
                        Name        = kv.Key,
                        Type        = (kv.Value.GetScalar("type", "string") ?? "string").ToLowerInvariant(),
                        Description = kv.Value.GetScalar("description", "") ?? "",
                    };

                    if (string.Equals(opt.Type, "vector3", System.StringComparison.Ordinal))
                    {
                        if (kv.Value.TryGet("default", out var d) && d != null && d.IsMap)
                        {
                            opt.DefaultVector3 = new Vector3(
                                ParseFloat(d.GetScalar("x", "0")),
                                ParseFloat(d.GetScalar("y", "0")),
                                ParseFloat(d.GetScalar("z", "0")));
                        }
                        _vector3OptionValues[opt.Name] = opt.DefaultVector3;
                    }
                    else
                    {
                        opt.DefaultString = kv.Value.GetScalar("default", "") ?? "";
                        _stringOptionValues[opt.Name] = opt.DefaultString;
                    }
                    _options.Add(opt);
                }
            }
        }

        private static float ParseFloat(string s)
        {
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
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

        // Single option definition pulled from the prefab YAML's
        // `options:` section. Only string + vector3 are surfaced in
        // Slice 2's wizard UI; future types fall back to a string field.
        private sealed class OptionSpec
        {
            public string  Name;
            public string  Type;
            public string  Description;
            public string  DefaultString;
            public Vector3 DefaultVector3;
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
