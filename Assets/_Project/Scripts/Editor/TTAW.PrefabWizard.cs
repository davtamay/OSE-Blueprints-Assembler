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

        // Slice 2e — section toggles for selective import. Each maps to
        // PrefabInstance.skip* on Confirm. Default unchecked = include
        // (matches user intent: drag a self-contained prefab → bring in
        // everything by default; tick off layers the package already has).
        private bool _includeParts     = true;
        private bool _includePartGroup = true;
        private bool _includeSteps     = true;
        private bool _previewExpanded  = true;
        // Read-only preview rows, rebuilt from the parsed YAML in Initialise.
        private List<string> _previewPartRows  = new();
        private List<string> _previewGroupRows = new();
        private List<string> _previewStepRows  = new();

        private readonly Dictionary<string, string>   _singleBindings = new();
        private readonly Dictionary<string, string[]> _listBindings   = new();
        // Per-instance option values keyed by option name. String values
        // are stored verbatim; vector3 values are stored as their
        // JSON-encoded SceneFloat3 shape so the in-memory representation
        // round-trips into the PrefabInstance.options array unchanged.
        private readonly Dictionary<string, string>   _stringOptionValues  = new();
        private readonly Dictionary<string, Vector3>  _vector3OptionValues = new();

        private Vector2 _scroll;
        private Vector2 _identityScroll;
        private string  _statusMessage;
        private bool    _statusIsError;

        // Right-pane tab selection. Identity (prefix/start_seq/etc.) lives in
        // the LEFT column always-visible; the right column rotates between
        // these four tabs so a 50-task prefab doesn't push roles off-screen.
        private enum WizardTab { Layers, Roles, Options, Preview }
        private WizardTab _activeTab = WizardTab.Layers;

        // Per-binding validation outcome — recomputed when bindings change or
        // the package's parts catalog mutates. Keeps the badge draw cheap.
        private enum BindingHealth { Unknown, Resolved, Heuristic, Missing, Empty }
        private readonly Dictionary<string, BindingHealth> _bindingHealth = new();

        // Dry-run expansion result — null until the Preview tab is opened.
        // Marked dirty whenever the author edits prefix / start_seq / a
        // binding so the preview re-runs lazily on the next GUI pass.
        private OSE.Content.Loading.PrefabExpander.Result _dryRunResult;
        private bool _dryRunDirty = true;
        private Vector2 _previewScroll;

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
            // Two-column tabbed shell needs ~720×500 to keep the identity
            // column readable AND give the right-pane tabs (Layers/Roles/
            // Options/Preview) enough width that a 50-task prefab still
            // shows its rows without horizontal squish.
            w.minSize = new Vector2(720, 500);
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
                var root = PrefabYamlReader.ReadFile(_prefabYamlPath);
                ParsePrefabRoles(root);
                BuildPreviewRows(root);
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
                _prefix = DerivePrefix(_prefabName, _targetStep.partGroupId ?? _targetStep.id ?? "");

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
                _prefix   = DerivePrefix(_prefabName, "instance_" + System.Guid.NewGuid().ToString("N").Substring(0, 6));
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
            // Header row — full-width title + summary + description so the
            // identity of the prefab is always visible regardless of which
            // tab is open.
            DrawHeader();

            // Two-column body. LEFT = identity / settings / status (always
            // visible). RIGHT = tabbed pane (Layers / Roles / Options /
            // Preview). Footer (Cancel / Instantiate) is below the body.
            float leftWidth = Mathf.Max(220f, position.width * 0.32f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            DrawIdentityColumn();
            EditorGUILayout.EndVertical();

            // Vertical separator — 1px line between columns so the split is
            // visible even when both sides scroll.
            var sep = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandHeight(true), GUILayout.Width(1f));
            EditorGUI.DrawRect(sep, new Color(1f, 1f, 1f, 0.06f));

            EditorGUILayout.BeginVertical();
            DrawTabBar();
            DrawTabBody();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage,
                    _statusIsError ? MessageType.Error : MessageType.Info);

            DrawFooter();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField($"Prefab: {_prefabName}",
                new GUIStyle(EditorStyles.largeLabel) { fontStyle = FontStyle.Bold });
            if (_summary != null)
                EditorGUILayout.LabelField(_summary.FormatSummaryLine(), EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_prefabDescription))
                EditorGUILayout.LabelField(_prefabDescription, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawIdentityColumn()
        {
            _identityScroll = EditorGUILayout.BeginScrollView(_identityScroll);

            EditorGUILayout.LabelField("IDENTITY", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Target step:",  EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(_targetStepId ?? "(none)",
                EditorStyles.miniLabel, GUILayout.Height(16));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("EMITTED IDs", EditorStyles.miniBoldLabel);

            string newPrefix = EditorGUILayout.TextField(new GUIContent("Prefix",
                "Prepended to every emitted step / part / partGroup id. " +
                "Defaults to prefab_<prefabName>_<derived> so emitted ids " +
                "carry their prefab origin."),
                _prefix ?? "");
            if (newPrefix != _prefix)
            {
                _prefix = newPrefix;
                _dryRunDirty = true;
            }

            int newStartSeq = EditorGUILayout.IntField(new GUIContent("Start seq",
                "First sequenceIndex assigned to the emitted steps. Defaults to target step + 1."),
                _startSeq);
            if (newStartSeq != _startSeq)
            {
                _startSeq = newStartSeq;
                _dryRunDirty = true;
            }

            // Live preview of the first emitted step id so the author can
            // see whether the prefix is overlong / readable before clicking
            // Instantiate.
            EditorGUILayout.LabelField("Sample step id:", EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel($"step_{_prefix}_<id_suffix>",
                EditorStyles.miniLabel, GUILayout.Height(16));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("BINDING HEALTH", EditorStyles.miniBoldLabel);
            DrawBindingHealthSummary();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();
            DrawTabButton(WizardTab.Layers,  "Layers",          _previewPartRows.Count + _previewGroupRows.Count + _previewStepRows.Count);
            DrawTabButton(WizardTab.Roles,   "Roles",           _roles?.Count ?? 0);
            DrawTabButton(WizardTab.Options, "Options",         _options?.Count ?? 0);
            DrawTabButton(WizardTab.Preview, "Preview",         -1);
            EditorGUILayout.EndHorizontal();

            // 1px underline under the active tab to anchor it visually.
            var underline = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(underline, new Color(1f, 1f, 1f, 0.10f));
        }

        private void DrawTabButton(WizardTab tab, string label, int count)
        {
            bool active = _activeTab == tab;
            string text = count >= 0 ? $"{label}  ({count})" : label;
            var style = new GUIStyle(EditorStyles.toolbarButton)
            {
                fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = active ? new Color(0.85f, 0.85f, 0.95f) : new Color(0.65f, 0.65f, 0.7f) },
            };
            if (GUILayout.Button(text, style, GUILayout.MinWidth(80)))
                _activeTab = tab;
        }

        private void DrawTabBody()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_activeTab)
            {
                case WizardTab.Layers:  DrawLayersTab();  break;
                case WizardTab.Roles:   DrawRolesTab();   break;
                case WizardTab.Options: DrawOptionsTab(); break;
                case WizardTab.Preview: DrawPreviewTab(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawLayersTab()
        {
            DrawSectionPreview();
        }

        private void DrawRolesTab()
        {
            if (_roles == null || _roles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Prefab has no roles, or YAML failed to parse. See status at the bottom.",
                    MessageType.None);
                return;
            }

            // Two groups: bindings that need attention vs. ones that look
            // good. Authors can scan the "needs review" group first and
            // ignore the rest.
            RecomputeBindingHealth();
            var needsReview = new List<RoleSpec>();
            var resolved   = new List<RoleSpec>();
            foreach (var role in _roles)
            {
                var h = AggregateRoleHealth(role);
                if (h == BindingHealth.Resolved) resolved.Add(role);
                else                              needsReview.Add(role);
            }

            if (needsReview.Count > 0)
            {
                EditorGUILayout.LabelField($"NEEDS REVIEW  ({needsReview.Count})",
                    EditorStyles.miniBoldLabel);
                foreach (var role in needsReview) DrawRoleRow(role);
                EditorGUILayout.Space(8);
            }
            if (resolved.Count > 0)
            {
                EditorGUILayout.LabelField($"AUTO-BOUND  ({resolved.Count})",
                    EditorStyles.miniBoldLabel);
                foreach (var role in resolved) DrawRoleRow(role);
            }
        }

        private void DrawOptionsTab()
        {
            if (_options == null || _options.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Prefab has no authored options. Optional values like " +
                    "torque settings, offsets, or size variants would appear here.",
                    MessageType.None);
                return;
            }
            foreach (var opt in _options) DrawOptionRow(opt);
        }

        /// <summary>
        /// Dry-run expansion showing what the prefab will emit with the
        /// current bindings + prefix + start_seq, before the author commits.
        /// Re-runs lazily when <see cref="_dryRunDirty"/> flips. Errors and
        /// warnings from the expander surface inline so the author can fix
        /// bindings without committing first.
        /// </summary>
        private void DrawPreviewTab()
        {
            EditorGUILayout.LabelField(
                "Dry-run of the prefab with the current bindings. Nothing is " +
                "written to the package until you press Instantiate.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("↻  Refresh dry-run",
                    "Re-runs the prefab expander with the current bindings."),
                EditorStyles.miniButton, GUILayout.Width(160)))
            {
                _dryRunDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (_dryRunDirty)
            {
                _dryRunResult = TryRunDryRun();
                _dryRunDirty = false;
            }

            if (_dryRunResult == null)
            {
                EditorGUILayout.HelpBox(
                    "Dry-run unavailable — the prefab YAML couldn't be parsed " +
                    "or the package context is missing.",
                    MessageType.Warning);
                return;
            }

            // Errors → red banner; warnings → yellow banner. Both come from
            // PrefabExpander.Result so the messages match what RunInstantiation
            // would surface.
            if (_dryRunResult.Errors != null && _dryRunResult.Errors.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "• " + string.Join("\n• ", _dryRunResult.Errors),
                    MessageType.Error);
            }
            if (_dryRunResult.Warnings != null && _dryRunResult.Warnings.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "• " + string.Join("\n• ", _dryRunResult.Warnings),
                    MessageType.Warning);
            }

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll);

            int partCount = _dryRunResult.Parts?.Length ?? 0;
            int groupCount = _dryRunResult.PartGroups?.Length ?? 0;
            int stepCount = _dryRunResult.Steps?.Length ?? 0;

            if (partCount > 0)
            {
                EditorGUILayout.LabelField($"PARTS  ({partCount})", EditorStyles.miniBoldLabel);
                foreach (var p in _dryRunResult.Parts)
                {
                    if (p == null) continue;
                    EditorGUILayout.LabelField($"  · {p.id}",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.Space(6);
            }
            if (groupCount > 0)
            {
                EditorGUILayout.LabelField($"PART GROUP  ({groupCount})", EditorStyles.miniBoldLabel);
                foreach (var g in _dryRunResult.PartGroups)
                {
                    if (g == null) continue;
                    int memberCount = g.partIds?.Length ?? 0;
                    EditorGUILayout.LabelField($"  · {g.id}  ({memberCount} parts)",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.Space(6);
            }
            if (stepCount > 0)
            {
                EditorGUILayout.LabelField($"PROCEDURE STEPS  ({stepCount})", EditorStyles.miniBoldLabel);
                foreach (var s in _dryRunResult.Steps)
                {
                    if (s == null) continue;
                    string family = string.IsNullOrEmpty(s.family) ? "?" : s.family;
                    int reqs = s.requiredPartIds?.Length ?? 0;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  · seq {s.sequenceIndex}  ·  [{family}]  {s.id}",
                        EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(reqs > 0 ? $"{reqs} req parts" : "—",
                        EditorStyles.miniLabel, GUILayout.Width(80));
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (partCount == 0 && groupCount == 0 && stepCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "Dry-run produced 0 entries. Either every section is unchecked " +
                    "in the Layers tab or the prefab has nothing to emit.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private OSE.Content.Loading.PrefabExpander.Result TryRunDryRun()
        {
            try
            {
                var instance = new PrefabInstance
                {
                    prefabId      = _prefabName,
                    instanceId    = $"{_prefabName}_dryrun",
                    prefix        = _prefix ?? "",
                    startSeq      = _startSeq,
                    assemblyId    = _targetStep?.assemblyId,
                    partGroupId   = _targetStep?.partGroupId,
                    bindings      = SnapshotBindings(),
                    options       = SnapshotOptions(),
                    skipParts     = !_includeParts,
                    skipPartGroup = !_includePartGroup,
                    skipSteps     = !_includeSteps,
                };

                string prefabsDir = OSE.Content.Loading.PrefabExpander.GetPrefabsDir();
                if (string.IsNullOrEmpty(prefabsDir)) return null;
                return OSE.Content.Loading.PrefabExpander.Expand(instance, prefabsDir);
            }
            catch (System.Exception ex)
            {
                OseLog.Warn($"[TTAW.PrefabWizard] Dry-run threw: {ex.Message}");
                return null;
            }
        }

        private void DrawFooter()
        {
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
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                role.IsList ? $"{role.Name}  [list × {role.Count}]" : role.Name,
                new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });
            DrawBindingBadge(AggregateRoleHealth(role));
            EditorGUILayout.EndHorizontal();

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

                int targetCount = role.Count > 0 ? role.Count : arr.Length;
                for (int i = 0; i < targetCount; i++)
                {
                    string current = i < arr.Length ? (arr[i] ?? "") : "";
                    string updated = DrawPartPickerField($"  [{i}] partId", current,
                        $"role:{role.Name}#{i}");
                    if (updated != current)
                    {
                        if (i >= arr.Length)
                        {
                            var grown = new string[i + 1];
                            System.Array.Copy(arr, grown, arr.Length);
                            arr = grown;
                            _listBindings[role.Name] = arr;
                        }
                        arr[i] = updated;
                        _dryRunDirty = true;
                    }
                }

                // +/- buttons for variable-count list roles. Fixed-count
                // roles (role.Count > 0) skip these because the prefab YAML
                // dictates the exact count.
                if (role.Count <= 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("− remove last", EditorStyles.miniButton, GUILayout.Width(96)))
                    {
                        if (arr.Length > 0)
                        {
                            var trimmed = new string[arr.Length - 1];
                            System.Array.Copy(arr, trimmed, trimmed.Length);
                            _listBindings[role.Name] = trimmed;
                            _dryRunDirty = true;
                        }
                    }
                    if (GUILayout.Button("+ add slot", EditorStyles.miniButton, GUILayout.Width(96)))
                    {
                        var grown = new string[arr.Length + 1];
                        System.Array.Copy(arr, grown, arr.Length);
                        grown[arr.Length] = "";
                        _listBindings[role.Name] = grown;
                        _dryRunDirty = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                _singleBindings.TryGetValue(role.Name, out var v);
                string updated = DrawPartPickerField("  partId", v ?? "", $"role:{role.Name}");
                if (updated != (v ?? ""))
                {
                    _singleBindings[role.Name] = updated;
                    _dryRunDirty = true;
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        // ── Part picker, validation, dry-run helpers ─────────────────────────

        /// <summary>
        /// One row: text field + small "Pick…" button that opens a searchable
        /// dropdown of the package's parts catalog. Author can still type a
        /// raw partId for ids that don't exist yet (the prefab might emit
        /// the part itself). The badge after the row signals whether the
        /// id resolves against the package's parts[] today.
        /// </summary>
        private string DrawPartPickerField(string label, string current, string controlId)
        {
            EditorGUILayout.BeginHorizontal();
            string updated = EditorGUILayout.TextField(label, current ?? "");
            if (GUILayout.Button(new GUIContent("Pick…",
                    "Browse the package's parts catalog and pick by name."),
                EditorStyles.miniButton, GUILayout.Width(54)))
            {
                ShowPartPickerPopup(controlId, current);
            }
            DrawBindingBadge(ClassifyBinding(updated));
            EditorGUILayout.EndHorizontal();

            // Picker popup writes its result back here on its next paint.
            if (_pickerResultControlId == controlId && _pickerResultPartId != null)
            {
                updated = _pickerResultPartId;
                _pickerResultPartId    = null;
                _pickerResultControlId = null;
            }
            return updated;
        }

        // Dropdown -> picker handshake. The popup runs as a separate window
        // and writes its selection here; the next OnGUI tick reads + clears.
        private string _pickerResultControlId;
        private string _pickerResultPartId;

        private void ShowPartPickerPopup(string controlId, string current)
        {
            var pkg = _owner != null ? _owner._pkgPublic : null;
            var parts = pkg?.GetParts();
            if (parts == null || parts.Length == 0)
            {
                _statusMessage = "Package has no parts catalog loaded — type the partId by hand.";
                _statusIsError = false;
                return;
            }

            // Snapshot the parts catalog into a sorted display list so the
            // popup doesn't allocate per keystroke.
            var entries = new List<string>(parts.Length);
            foreach (var p in parts)
                if (p != null && !string.IsNullOrEmpty(p.id)) entries.Add(p.id);
            entries.Sort(System.StringComparer.Ordinal);

            PartPickerPopup.Open(this, controlId, entries, current);
        }

        // Called by PartPickerPopup when the author confirms a pick.
        internal void OnPartPicked(string controlId, string partId)
        {
            _pickerResultControlId = controlId;
            _pickerResultPartId    = partId ?? "";
            _dryRunDirty = true;
            Repaint();
        }

        private void DrawBindingBadge(BindingHealth h)
        {
            string glyph; string tip; Color color;
            switch (h)
            {
                case BindingHealth.Resolved:  glyph = "✓"; color = new Color(0.55f, 0.85f, 0.55f);
                    tip = "Resolves to a part in the package's parts[]."; break;
                case BindingHealth.Heuristic: glyph = "≈"; color = new Color(0.95f, 0.85f, 0.45f);
                    tip = "Auto-bound by name match — verify it's the right part."; break;
                case BindingHealth.Missing:   glyph = "✗"; color = new Color(0.95f, 0.55f, 0.55f);
                    tip = "PartId is not in the package. Typo, or this prefab will emit the part itself."; break;
                case BindingHealth.Empty:     glyph = "○"; color = new Color(0.7f, 0.7f, 0.75f);
                    tip = "No binding yet. Pick a partId or leave blank if optional."; break;
                default:                      return;
            }
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = color },
                alignment = TextAnchor.MiddleCenter,
            };
            GUILayout.Label(new GUIContent(glyph, tip), style, GUILayout.Width(16));
        }

        /// <summary>
        /// Recomputes <see cref="_bindingHealth"/> from the current bindings
        /// and the package's parts catalog. Call once per OnGUI tick before
        /// rendering the Roles tab so the badges are consistent.
        /// </summary>
        private void RecomputeBindingHealth()
        {
            _bindingHealth.Clear();
            foreach (var role in _roles ?? new List<RoleSpec>())
            {
                if (role.IsList)
                {
                    if (!_listBindings.TryGetValue(role.Name, out var arr) || arr == null) continue;
                    for (int i = 0; i < arr.Length; i++)
                        _bindingHealth[$"role:{role.Name}#{i}"] = ClassifyBinding(arr[i]);
                }
                else
                {
                    _singleBindings.TryGetValue(role.Name, out var v);
                    _bindingHealth[$"role:{role.Name}"] = ClassifyBinding(v);
                }
            }
        }

        private BindingHealth AggregateRoleHealth(RoleSpec role)
        {
            // Worst-state-wins so a role with one Missing slot doesn't read
            // as "Resolved" overall.
            BindingHealth worst = BindingHealth.Resolved;
            int seen = 0;
            if (role.IsList && _listBindings.TryGetValue(role.Name, out var arr) && arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (_bindingHealth.TryGetValue($"role:{role.Name}#{i}", out var h))
                    {
                        if (Worse(h, worst)) worst = h;
                        seen++;
                    }
                }
            }
            else if (!role.IsList && _bindingHealth.TryGetValue($"role:{role.Name}", out var h))
            {
                worst = h;
                seen = 1;
            }
            return seen == 0 ? BindingHealth.Empty : worst;
        }

        private static bool Worse(BindingHealth a, BindingHealth b)
        {
            // Severity: Missing > Empty > Heuristic > Resolved > Unknown.
            int Score(BindingHealth h) => h switch
            {
                BindingHealth.Missing   => 4,
                BindingHealth.Empty     => 3,
                BindingHealth.Heuristic => 2,
                BindingHealth.Resolved  => 1,
                _                       => 0,
            };
            return Score(a) > Score(b);
        }

        private BindingHealth ClassifyBinding(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId)) return BindingHealth.Empty;
            var pkg = _owner != null ? _owner._pkgPublic : null;
            if (pkg == null) return BindingHealth.Unknown;

            // Does the partId resolve to an existing PartDefinition?
            foreach (var p in pkg.GetParts())
            {
                if (p != null && string.Equals(p.id, partId, System.StringComparison.Ordinal))
                    return BindingHealth.Resolved;
            }
            // The prefab itself may emit a part with this id at expansion
            // time (Slice 2's partDefinitions). Treat ids that match a
            // role-emitted part suffix as Heuristic so they don't read as
            // hard Missing.
            return BindingHealth.Missing;
        }

        private void DrawBindingHealthSummary()
        {
            // Re-classify on every paint — cheap and ensures the LEFT
            // identity column stays in sync without explicit invalidation
            // hooks.
            RecomputeBindingHealth();
            int resolved = 0, heuristic = 0, missing = 0, empty = 0;
            foreach (var kv in _bindingHealth)
            {
                switch (kv.Value)
                {
                    case BindingHealth.Resolved:  resolved++;  break;
                    case BindingHealth.Heuristic: heuristic++; break;
                    case BindingHealth.Missing:   missing++;   break;
                    case BindingHealth.Empty:     empty++;     break;
                }
            }
            EditorGUILayout.LabelField($"  ✓ Resolved: {resolved}",  EditorStyles.miniLabel);
            if (heuristic > 0) EditorGUILayout.LabelField($"  ≈ Heuristic: {heuristic}", EditorStyles.miniLabel);
            if (missing > 0)   EditorGUILayout.LabelField($"  ✗ Missing: {missing}",   EditorStyles.miniLabel);
            if (empty > 0)     EditorGUILayout.LabelField($"  ○ Empty: {empty}",       EditorStyles.miniLabel);
        }

        // Slice 2e — visible-by-design preview of what the prefab will
        // emit. Three section toggles (Parts / PartGroup / Steps) drive
        // the corresponding skip flags on the PrefabInstance. The body
        // of each section lists its leaves (read-only) so the author can
        // confirm the layer's contents at a glance without re-opening
        // the YAML. Sections that the prefab doesn't include are grayed
        // and locked off — there's nothing to skip for those layers.
        private void DrawSectionPreview()
        {
            bool hasParts  = _previewPartRows.Count  > 0;
            bool hasGroup  = _previewGroupRows.Count > 0;
            bool hasSteps  = _previewStepRows.Count  > 0;
            if (!hasParts && !hasGroup && !hasSteps) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _previewExpanded = EditorGUILayout.Foldout(_previewExpanded,
                "Will create — uncheck a section to skip it on import", true);
            if (_previewExpanded)
            {
                if (hasParts)  DrawSectionToggleAndLeaves(
                    new GUIContent("Parts",
                        "PartDefinition entries this prefab will add to the package's parts[]."),
                    ref _includeParts, _previewPartRows);
                if (hasGroup)  DrawSectionToggleAndLeaves(
                    new GUIContent("Part Group",
                        "PartGroupDefinition entry this prefab will add to the package's partGroups[]."),
                    ref _includePartGroup, _previewGroupRows);
                if (hasSteps)  DrawSectionToggleAndLeaves(
                    new GUIContent("Procedure Steps",
                        "Each entry below becomes one StepDefinition (a sequenceIndex slot in " +
                        "the assembly's step list). Inside each procedure step the runtime " +
                        "may have several sub-tasks (taskOrder entries) — those are NOT " +
                        "shown here. If you came from \"task\", read this as the parent." ),
                    ref _includeSteps, _previewStepRows);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawSectionToggleAndLeaves(GUIContent sectionLabel, ref bool included, List<string> leaves)
        {
            EditorGUILayout.BeginHorizontal();
            var labelText = $"{sectionLabel.text}  ({leaves.Count})";
            included = EditorGUILayout.ToggleLeft(
                new GUIContent(labelText, sectionLabel.tooltip),
                included,
                new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(!included))
            {
                foreach (var row in leaves)
                    EditorGUILayout.LabelField("    └ " + row, EditorStyles.miniLabel);
            }
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
                    prefabId      = _prefabName,
                    instanceId    = $"{_prefabName}_{_prefix}_{System.Guid.NewGuid().ToString("N").Substring(0, 6)}",
                    prefix        = _prefix,
                    startSeq      = _startSeq,
                    assemblyId    = _targetStep?.assemblyId,
                    partGroupId   = _targetStep?.partGroupId,
                    bindings      = SnapshotBindings(),
                    options       = SnapshotOptions(),
                    // Section toggles in the "Will create" preview map to
                    // expander skip flags so layers the author unchecked
                    // don't get emitted at expansion time.
                    skipParts     = !_includeParts,
                    skipPartGroup = !_includePartGroup,
                    skipSteps     = !_includeSteps,
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

        // Builds the read-only preview rows surfaced under each section
        // checkbox. Walks the YAML once and produces human-readable
        // bullets for the wizard. The actual partIds aren't resolved here
        // (that requires running the expander) — role + count is enough
        // for the author to spot which layers belong to which sections.
        private void BuildPreviewRows(YamlNode root)
        {
            _previewPartRows.Clear();
            _previewGroupRows.Clear();
            _previewStepRows.Clear();
            if (root == null || !root.IsMap) return;

            if (root.TryGet("partDefinitions", out var defs) && defs != null && defs.IsMap)
            {
                foreach (var kv in defs.Map)
                {
                    if (kv.Value == null || !kv.Value.IsMap) continue;
                    string kind = kv.Value.GetScalar("kind", "part") ?? "part";
                    if (string.Equals(kind, "part_list", System.StringComparison.OrdinalIgnoreCase))
                    {
                        int count = 1;
                        string c = kv.Value.GetScalar("count", null);
                        if (int.TryParse(c, out int n) && n > 0) count = n;
                        else if (kv.Value.TryGet("placements", out var pls) && pls != null && pls.IsSeq) count = pls.Seq.Count;
                        _previewPartRows.Add($"{kv.Key}  ·  {count} part{(count == 1 ? "" : "s")}");
                    }
                    else
                    {
                        _previewPartRows.Add($"{kv.Key}  ·  1 part");
                    }
                }
            }

            if (root.TryGet("partGroupDefinition", out var pg) && pg != null && pg.IsMap)
            {
                string id   = pg.GetScalar("id",   "(derived)") ?? "(derived)";
                string name = pg.GetScalar("name", id)          ?? id;
                _previewGroupRows.Add($"{id}  ·  {name}");
            }

            if (root.TryGet("steps", out var steps) && steps != null && steps.IsSeq)
            {
                foreach (var s in steps.Seq)
                {
                    if (s == null || !s.IsMap) continue;
                    string suffix = s.GetScalar("id_suffix", "") ?? "";
                    string family = s.GetScalar("family", "") ?? "";
                    string label  = string.IsNullOrEmpty(family) ? suffix : $"{family}  ·  {suffix}";
                    if (!string.IsNullOrEmpty(label)) _previewStepRows.Add(label);
                }
            }
        }

        private static string DerivePrefix(string prefabName, string idLike)
        {
            // Strip common "partGroup_" / "step_" prefixes from the seed
            // (the prefab's step ids prepend "step_<prefix>_<id_suffix>"
            // already, so adding them here would duplicate).
            string s = idLike ?? "";
            const string subPfx  = "partGroup_";
            const string stepPfx = "step_";
            const string prefabPfx = "prefab_";
            if (s.StartsWith(subPfx,    System.StringComparison.Ordinal)) s = s.Substring(subPfx.Length);
            if (s.StartsWith(stepPfx,   System.StringComparison.Ordinal)) s = s.Substring(stepPfx.Length);
            // Author's seed may already start with "prefab_" if they pasted
            // an existing prefix back in — don't double-stamp. Otherwise
            // prepend "prefab_<prefabName>_" so the emitted IDs scream their
            // origin (e.g. step_prefab_carriage_left_frame_side_place_bearings).
            if (!s.StartsWith(prefabPfx, System.StringComparison.Ordinal) && !string.IsNullOrEmpty(prefabName))
                s = $"{prefabPfx}{prefabName}_{s}";
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

    /// <summary>
    /// Searchable picker popup for selecting a partId from the package's
    /// parts catalog. Opened by <see cref="PrefabWizardWindow"/>'s role-row
    /// "Pick…" button. Writes the selection back to the owning wizard via
    /// <see cref="PrefabWizardWindow.OnPartPicked"/>.
    /// </summary>
    internal sealed class PartPickerPopup : EditorWindow
    {
        private PrefabWizardWindow _owner;
        private string _controlId;
        private List<string> _entries;
        private string _filter = "";
        private Vector2 _scroll;

        public static void Open(PrefabWizardWindow owner, string controlId,
            List<string> entries, string current)
        {
            var w = CreateInstance<PartPickerPopup>();
            w.titleContent = new GUIContent("Pick part");
            w._owner = owner;
            w._controlId = controlId;
            w._entries = entries;
            w._filter = current ?? "";
            w.ShowAuxWindow();
            w.minSize = new Vector2(320, 360);
            w.Focus();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Filter:", EditorStyles.miniBoldLabel);
            _filter = EditorGUILayout.TextField(_filter ?? "");
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int matched = 0;
            foreach (var id in _entries)
            {
                if (!string.IsNullOrEmpty(_filter)
                    && id.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (GUILayout.Button(id, EditorStyles.miniButton))
                {
                    _owner?.OnPartPicked(_controlId, id);
                    Close();
                    return;
                }
                matched++;
            }
            if (matched == 0)
                EditorGUILayout.LabelField("No parts match the filter.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(72)))
            {
                _owner?.OnPartPicked(_controlId, "");
                Close();
            }
            if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(72)))
                Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
