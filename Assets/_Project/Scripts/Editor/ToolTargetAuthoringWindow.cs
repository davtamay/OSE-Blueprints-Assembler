using System;
using System.Collections.Generic;
using System.IO;
using OSE.App;
using OSE.Content;
using OSE.Interaction;
using OSE.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Visual editor for <see cref="TargetPreviewPlacement"/> and related
    /// <see cref="TargetDefinition"/> authoring fields (weldAxis, weldLength,
    /// useToolActionRotation, toolActionRotation, portA, portB).
    ///
    /// Spawns the assembled part meshes in SceneView so authors can:
    ///   1. Click directly on a mesh surface to snap a target to that point
    ///      (auto-aligned to the surface normal).
    ///   2. Drag Position / Rotation handles to fine-tune.
    ///   3. Edit per-target fields in the detail panel.
    ///   4. Write changes back to machine.json with a timestamped backup.
    ///
    /// Step filter shows sequence number, tool name, and profile.
    /// Selecting a step sets the SceneView to the exact machine state the
    /// trainee sees at that point in the assembly — parts placed earlier are at
    /// playPosition, the current step's part is at startPosition, future parts
    /// are hidden.  Targets not belonging to the active step are dimmed.
    /// The detail panel shows only the fields relevant to the step's profile
    /// (Weld/Cut → weldAxis+weldLength; Cable → portA+portB drag handles;
    ///  Torque/Clamp/Strike → useToolActionRotation).
    ///
    /// Blender pipeline (zero extra work when correctly named):
    ///   Name a Blender Empty with the exact <c>targetId</c> string. On GLB import
    ///   <see cref="PackageAssetPostprocessor"/> auto-writes position/rotation/scale
    ///   to machine.json. Use "Extract from GLB Anchors" to re-run that logic manually.
    ///
    /// Open via: OSE > Tool Target Authoring
    /// </summary>
    public sealed class ToolTargetAuthoringWindow : EditorWindow
    {
        private const string MenuPath = "OSE/Tool Target Authoring";
        private const int MaxUndoHistory = 50;
        private const float DefaultTargetScale = 0.05f;

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color ColAuthored    = new(0f,   0.9f, 0.9f, 1f);
        private static readonly Color ColDirty       = new(1f,   0.6f, 0f,   1f);
        private static readonly Color ColNoPlacement = new(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color ColWeldAxis    = new(0.9f, 0.9f, 0.1f, 0.9f);
        private static readonly Color ColSelected    = new(1f,   1f,   1f,   1f);
        private static readonly Color ColPortPoint   = new(0.3f, 1f,   0.5f, 1f);

        // ── State ─────────────────────────────────────────────────────────────
        private string[]   _packageIds;
        private int        _pkgIdx;
        private string     _pkgId;
        private MachinePackageDefinition _pkg;

        private string[]   _stepOptions;        // "(All Steps)", then "[seq] name · tool · profile"
        private string[]   _stepIds;            // null at index 0, then actual step ids
        private int[]      _stepSequenceIdxs;   // 0 at index 0, then step.sequenceIndex
        private int        _stepFilterIdx;
        private bool       _suppressStepSync;   // prevent circular sync with SessionDriver
        private int        _lastPolledDriverStep = -1; // last SessionDriver step seen during poll

        // Active-step context (null = All Steps)
        private string          _activeStepProfile;
        private HashSet<string> _activeStepTargetIds;

        // targetId → display name of the tool that acts on it (from requiredToolActions)
        private Dictionary<string, string> _targetToolMap;
        // targetId → toolId (raw id, for looking up ToolDefinition.persistent)
        private Dictionary<string, string> _targetToolIdMap;
        // Target IDs referenced by at least one requiredToolAction (non-ghost targets)
        private HashSet<string> _toolActionTargetIds;
        // Dirty tracking for tool/step fields written outside the target placement flow
        private readonly HashSet<string> _dirtyToolIds  = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _dirtyStepIds  = new HashSet<string>(StringComparer.Ordinal);
        // When false (default), targets not linked to any tool action are hidden
        [SerializeField] private bool _showGhostTargets;

        // SceneView part-count summary updated by RespawnScene
        private int _previewAssembled;
        private int _previewCurrent;
        private int _previewHidden;

        private TargetEditState[] _targets;
        private int               _selectedIdx = -1;
        private readonly HashSet<int> _multiSelected = new HashSet<int>();

        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private Vector3 _batchPositionOffset;

        private bool _clickToSnapActive;

        // Undo/redo
        private readonly List<(int idx, TargetSnapshot snap)> _undoStack = new();
        private readonly List<(int idx, TargetSnapshot snap)> _redoStack = new();
        private bool _snapshotPending;

        // File backup
        private string _lastBackupPath;

        // Scene objects
        private GameObject _previewRoot;
        private readonly Dictionary<string, GameObject> _partMeshes = new();

        // Tool preview — cyan-tinted ghost mesh parented under _previewRoot
        [SerializeField] private bool _showToolPreview = true;
        private ToolDefinition _toolPreviewDef;   // ToolDefinition for the previewed tool
        private GameObject _toolPreviewGO;         // instantiated tool mesh (HideAndDontSave)

        // ── Nested types ──────────────────────────────────────────────────────

        private struct TargetEditState
        {
            public TargetDefinition       def;
            public TargetPreviewPlacement placement;   // null if not yet authored
            public Vector3    position;
            public Quaternion rotation;
            public Vector3    scale;
            public Vector3    weldAxis;
            public float      weldLength;
            public bool       useToolActionRotation;
            public Vector3    toolActionRotationEuler;
            public Vector3    portA;
            public Vector3    portB;
            public bool       isDirty;
            public bool       hasPlacement;
            // Weld gizmo state (editor-only, not saved to JSON)
            public bool    weldGizmoActive;
            public Vector3 weldGizmoA;   // local start of the weld line
            public Vector3 weldGizmoB;   // local end of the weld line
        }

        private struct TargetSnapshot
        {
            public Vector3 position; public Quaternion rotation; public Vector3 scale;
            public Vector3 weldAxis; public float weldLength;
            public bool useToolActionRotation; public Vector3 toolActionRotationEuler;
            public Vector3 portA; public Vector3 portB;
        }

        // ── MenuItem ──────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var w = GetWindow<ToolTargetAuthoringWindow>("Tool Target Authoring");
            w.minSize = new Vector2(420, 560);
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshPackageList();
            // Auto-reload the last-used package after domain reload or window re-open.
            // Without this, _pkg / _targetToolIdMap are null and toggles don't appear.
            if (_pkg == null && _packageIds != null && _packageIds.Length > 0
                && _pkgIdx >= 0 && _pkgIdx < _packageIds.Length)
            {
                LoadPkg(_packageIds[_pkgIdx]);
            }
            SceneView.duringSceneGui += OnSceneGUI;
            SessionDriver.EditModeStepChanged += OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SessionDriver.EditModeStepChanged -= OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Cleanup();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            // Reload the package so the window reflects any runtime changes.
            if (!string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId);
        }

        private void OnSessionDriverStepChanged(int sequenceIndex)
        {
            if (_suppressStepSync || _stepSequenceIdxs == null) return;
            // Find the filter index that matches this sequence index
            int newFilterIdx = -1;
            for (int i = 1; i < _stepSequenceIdxs.Length; i++)
            {
                if (_stepSequenceIdxs[i] == sequenceIndex) { newFilterIdx = i; break; }
            }
            if (newFilterIdx < 0 || newFilterIdx == _stepFilterIdx) return;
            _suppressStepSync = true;
            ApplyStepFilter(newFilterIdx);
            _suppressStepSync = false;
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void Cleanup()
        {
            KillPartMeshes();
            ClearToolPreview();
            if (_previewRoot != null) DestroyImmediate(_previewRoot);
            _previewRoot = null;
            _targets = null;
            _selectedIdx = -1;
            _multiSelected.Clear();
        }

        // ── Main GUI ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            DrawPkgPicker();
            if (_pkg == null) return;

            DrawStepFilter();
            EditorGUILayout.Space(2);

            float listH = Mathf.Clamp(position.height * 0.35f, 80f, 220f);
            DrawTargetList(listH);
            EditorGUILayout.Space(4);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            if (_multiSelected.Count > 1)
                DrawBatchPanel();
            else if (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                DrawDetailPanel(ref _targets[_selectedIdx]);
            else
                EditorGUILayout.HelpBox("Select a target above.\nCtrl+click or Shift+click for multi-select.", MessageType.Info);
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        // ── Package picker ────────────────────────────────────────────────────

        private void DrawPkgPicker()
        {
            if (_packageIds == null || _packageIds.Length == 0)
            {
                EditorGUILayout.HelpBox("No packages found in Assets/_Project/Data/Packages/", MessageType.Warning);
                if (GUILayout.Button("Refresh")) RefreshPackageList();
                return;
            }
            EditorGUILayout.BeginHorizontal();
            int i = EditorGUILayout.Popup("Package", _pkgIdx, _packageIds);
            if (GUILayout.Button("↺", GUILayout.Width(24))) RefreshPackageList();
            EditorGUILayout.EndHorizontal();
            if (i != _pkgIdx) { _pkgIdx = i; LoadPkg(_packageIds[i]); }
            if (_pkg == null && GUILayout.Button("Load")) LoadPkg(_packageIds[_pkgIdx]);
        }

        // ── Step filter ───────────────────────────────────────────────────────

        private void DrawStepFilter()
        {
            if (_stepOptions == null || _stepOptions.Length == 0) return;

            // Poll SessionDriver each draw — catches changes from its inspector
            // regardless of whether the static event fired.
            if (!_suppressStepSync && _stepSequenceIdxs != null)
            {
                var driver = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
                int driverSeq = driver != null ? driver.PreviewStepSequenceIndex : -1;
                if (driverSeq != _lastPolledDriverStep)
                {
                    _lastPolledDriverStep = driverSeq;
                    int matchIdx = -1;
                    for (int i = 1; i < _stepSequenceIdxs.Length; i++)
                        if (_stepSequenceIdxs[i] == driverSeq) { matchIdx = i; break; }
                    if (matchIdx >= 0 && matchIdx != _stepFilterIdx)
                    {
                        _suppressStepSync = true;
                        ApplyStepFilter(matchIdx);
                        _suppressStepSync = false;
                    }
                }
            }

            int stepCount = _stepOptions.Length - 1;

            // ── Navigation row: Prev · [Step N/M ▼] · Next ──────────────────
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(_stepFilterIdx <= 1);
            if (GUILayout.Button("◄", GUILayout.Width(28)))
                ApplyStepFilter(_stepFilterIdx - 1);
            EditorGUI.EndDisabledGroup();

            // Compact GenericMenu button — opens the full step list as a context menu
            string navLabel = _stepFilterIdx == 0
                ? $"All Steps  ({stepCount})"
                : $"Step {_stepFilterIdx} / {stepCount}";

            if (GUILayout.Button(navLabel, EditorStyles.popup))
            {
                var menu = new GenericMenu();
                for (int j = 0; j < _stepOptions.Length; j++)
                {
                    int captured = j;
                    menu.AddItem(new GUIContent(_stepOptions[j]), _stepFilterIdx == j,
                        () => ApplyStepFilter(captured));
                }
                menu.ShowAsContext();
            }

            EditorGUI.BeginDisabledGroup(_stepFilterIdx == 0 || _stepFilterIdx >= _stepOptions.Length - 1);
            if (GUILayout.Button("►", GUILayout.Width(28)))
                ApplyStepFilter(_stepFilterIdx + 1);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // ── Ghost target toggle ───────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            bool newShow = EditorGUILayout.ToggleLeft(
                "Show part-snap targets (ghost)", _showGhostTargets, EditorStyles.miniLabel);
            if (EditorGUI.EndChangeCheck() && newShow != _showGhostTargets)
            {
                _showGhostTargets = newShow;
                BuildTargetList();
                Repaint();
            }

            // ── Step info card (hidden in All Steps mode) ─────────────────────
            if (_stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length)
            {
                var step = FindStep(_stepIds[_stepFilterIdx]);
                if (step != null)
                {
                    string toolName = "(no tool)";
                    if (step.relevantToolIds != null && step.relevantToolIds.Length > 0 && _pkg?.tools != null)
                        foreach (var td in _pkg.tools)
                            if (td != null && td.id == step.relevantToolIds[0]) { toolName = td.name; break; }

                    string profileStr = string.IsNullOrEmpty(step.profile) ? "" : $"  ·  {step.profile}";
                    int    tCount     = step.targetIds?.Length ?? 0;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"[{step.sequenceIndex}] {step.name}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"Tool: {toolName}{profileStr}  ·  {tCount} target{(tCount == 1 ? "" : "s")}",
                        EditorStyles.miniLabel);
                    if (_previewAssembled + _previewCurrent + _previewHidden > 0)
                        EditorGUILayout.LabelField(
                            $"{_previewAssembled} assembled  ·  {_previewCurrent} at start pos  ·  {_previewHidden} hidden",
                            EditorStyles.miniLabel);

                    // ── removePersistentToolIds editor ────────────────────────
                    EditorGUILayout.Space(2);
                    var removeIds = step.removePersistentToolIds ?? Array.Empty<string>();

                    EditorGUILayout.LabelField("Removes persistent tools at start of step:", EditorStyles.miniLabel);

                    string toRemove = null;
                    foreach (string rid in removeIds)
                    {
                        EditorGUILayout.BeginHorizontal();
                        string rname = FindToolName(rid);
                        EditorGUILayout.LabelField($"  · {rname}", EditorStyles.miniLabel);
                        if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(18)))
                            toRemove = rid;
                        EditorGUILayout.EndHorizontal();
                    }
                    if (toRemove != null)
                    {
                        step.removePersistentToolIds = System.Array.FindAll(
                            removeIds, r => r != toRemove);
                        _dirtyStepIds.Add(step.id);
                        Repaint();
                    }

                    // Add button — only shows persistent tools that are actually active
                    // (placed by a prior step and not yet removed by an intermediate step).
                    var activePersistent = GetActivePersistentToolIds(step);
                    // Remove tools already listed in this step's removal array
                    foreach (string rid in removeIds) activePersistent.Remove(rid);

                    if (activePersistent.Count > 0 && GUILayout.Button("+ Add removal", EditorStyles.miniButton))
                    {
                        var menu = new GenericMenu();
                        foreach (string toolId in activePersistent)
                        {
                            string capturedId   = toolId;
                            string capturedName = FindToolName(toolId);
                            menu.AddItem(new GUIContent(capturedName), false, () =>
                            {
                                var newList = new List<string>(
                                    step.removePersistentToolIds ?? Array.Empty<string>());
                                newList.Add(capturedId);
                                step.removePersistentToolIds = newList.ToArray();
                                _dirtyStepIds.Add(step.id);
                                Repaint();
                            });
                        }
                        menu.ShowAsContext();
                    }

                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void ApplyStepFilter(int newIdx)
        {
            _stepFilterIdx    = newIdx;
            _selectedIdx      = -1;
            _multiSelected.Clear();
            _clickToSnapActive = false;
            UpdateActiveStep();
            BuildTargetList();
            RespawnScene();
            if (!_suppressStepSync)
                SyncSessionDriverStep();
            SceneView.RepaintAll();
            Repaint();
        }

        private void SyncSessionDriverStep()
        {
            if (_pkg == null) return;
            var driver = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
            if (driver == null) return;
            if (_stepFilterIdx <= 0 || _stepSequenceIdxs == null || _stepFilterIdx >= _stepSequenceIdxs.Length)
                return;
            int sequenceIndex = _stepSequenceIdxs[_stepFilterIdx];
            _suppressStepSync     = true;
            _lastPolledDriverStep = sequenceIndex; // prevent poll from re-triggering
            driver.SetEditModeStep(sequenceIndex);
            _suppressStepSync = false;
        }

        private void UpdateActiveStep()
        {
            _activeStepProfile   = null;
            _activeStepTargetIds = null;

            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length)
                return;

            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            _activeStepProfile = string.IsNullOrEmpty(step.profile) ? null : step.profile;

            if (step.targetIds != null && step.targetIds.Length > 0)
                _activeStepTargetIds = new HashSet<string>(step.targetIds, StringComparer.Ordinal);
        }

        // ── Target list ───────────────────────────────────────────────────────

        private void DrawTargetList(float listHeight)
        {
            if (_targets == null || _targets.Length == 0)
            {
                EditorGUILayout.HelpBox("No targets for this package/step.", MessageType.Info);
                return;
            }

            // Header: show multi-select count when more than one target is selected
            string listHeader = _multiSelected.Count > 1
                ? $"Targets ({_targets.Length})  —  {_multiSelected.Count} selected  (Ctrl+click / Shift+click)"
                : $"Targets ({_targets.Length})  —  Ctrl+click or Shift+click to multi-select";
            EditorGUILayout.LabelField(listHeader, EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(listHeight));

            // Selection highlight colours
            var selBg       = new Color(0.25f, 0.50f, 0.90f, 0.35f); // blue tint — primary
            var multiBg     = new Color(0.25f, 0.50f, 0.90f, 0.18f); // lighter blue — secondary multi

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                Color col = t.isDirty ? ColDirty : t.hasPlacement ? ColAuthored : ColNoPlacement;

                bool isPrimary   = i == _selectedIdx;
                bool isInMulti   = _multiSelected.Count > 1 && _multiSelected.Contains(i);
                bool isSelected  = isPrimary || isInMulti;

                // Draw selection background behind the row
                if (isSelected)
                {
                    Rect rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(0));
                    rowRect.height = EditorGUIUtility.singleLineHeight + 2f;
                    rowRect.y     -= 1f;
                    EditorGUI.DrawRect(rowRect, isPrimary ? selBg : multiBg);
                }

                var style = new GUIStyle(isSelected ? EditorStyles.boldLabel : EditorStyles.label)
                {
                    normal  = { textColor = col },
                    focused = { textColor = col }
                };

                string badge      = t.isDirty ? " ●" : t.hasPlacement ? "" : " ○";
                string toolBadge  = (_targetToolMap != null && _targetToolMap.TryGetValue(t.def.id, out string tn))
                                    ? $"  [{tn}]" : "";
                string xformBadge = (t.portA.sqrMagnitude > 0.00001f || t.portB.sqrMagnitude > 0.00001f) ? "  ↔"
                                  : (t.weldAxis.sqrMagnitude > 0.001f) ? "  →" : "";
                string checkMark  = isInMulti ? "✓ " : "  ";
                string label = $"{checkMark}{t.def.id}{toolBadge}{xformBadge}{badge}";
                if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true)))
                {
                    bool ctrl  = (Event.current.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
                    bool shift = (Event.current.modifiers & EventModifiers.Shift) != 0;

                    if (ctrl)
                    {
                        if (_multiSelected.Contains(i)) _multiSelected.Remove(i);
                        else _multiSelected.Add(i);
                        if (!_multiSelected.Contains(_selectedIdx)) _selectedIdx = i;
                        if (_multiSelected.Count == 1) _selectedIdx = _multiSelected.GetEnumerator().Current;
                    }
                    else if (shift && _selectedIdx >= 0)
                    {
                        _multiSelected.Clear();
                        int lo = Mathf.Min(_selectedIdx, i), hi = Mathf.Max(_selectedIdx, i);
                        for (int j = lo; j <= hi; j++) _multiSelected.Add(j);
                        _selectedIdx = i;
                    }
                    else
                    {
                        _multiSelected.Clear();
                        _selectedIdx = i;
                    }
                    _clickToSnapActive = false;
                    _snapshotPending   = false;
                    if (_multiSelected.Count <= 1 && _selectedIdx >= 0 && _selectedIdx < _targets.Length)
                        RefreshToolPreview(ref _targets[_selectedIdx]);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Detail panel ──────────────────────────────────────────────────────

        private void DrawDetailPanel(ref TargetEditState t)
        {
            EditorGUILayout.LabelField($"Target: {t.def.id}", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(t.def.name))
                EditorGUILayout.LabelField($"Name: {t.def.name}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(t.def.associatedPartId))
                EditorGUILayout.LabelField($"Part: {t.def.associatedPartId}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(_activeStepProfile))
                EditorGUILayout.LabelField($"Profile: {_activeStepProfile}", EditorStyles.miniLabel);

            // Persistent-tool toggle — shown when this target has a mapped tool
            if (_targetToolIdMap != null && _targetToolIdMap.TryGetValue(t.def.id, out string mappedToolId))
            {
                ToolDefinition toolDef = null;
                if (_pkg?.tools != null)
                    foreach (var td in _pkg.tools)
                        if (td != null && td.id == mappedToolId) { toolDef = td; break; }

                if (toolDef != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool newPersist = EditorGUILayout.ToggleLeft(
                        new GUIContent("Persistent tool (stays in scene after use)",
                            "The tool instance (e.g. clamp) remains in the scene after the action completes.\n" +
                            "Use 'Removes persistent tools' on a later step to clean it up."),
                        toolDef.persistent, EditorStyles.miniLabel);
                    if (EditorGUI.EndChangeCheck() && newPersist != toolDef.persistent)
                    {
                        toolDef.persistent = newPersist;
                        _dirtyToolIds.Add(mappedToolId);
                    }
                }
            }

            // Tool preview toggle — only shown when a tool is mapped to this target
            if (_targetToolIdMap != null && _targetToolIdMap.ContainsKey(t.def.id))
            {
                EditorGUI.BeginChangeCheck();
                bool newShow = EditorGUILayout.ToggleLeft(
                    new GUIContent("Show tool preview",
                        "Renders the tool mesh as a wireframe at its end position in SceneView.\n" +
                        "The preview moves with the position/rotation gizmo in real-time."),
                    _showToolPreview, EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck() && newShow != _showToolPreview)
                {
                    _showToolPreview = newShow;
                    RefreshToolPreview(ref t);
                    SceneView.RepaintAll();
                }
            }

            // State badge
            string stateLbl = t.isDirty ? "Unsaved changes" : t.hasPlacement ? "Authored" : "No placement data";
            Color  badgeCol = t.isDirty ? ColDirty : t.hasPlacement ? ColAuthored : ColNoPlacement;
            var badgeStyle  = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = badgeCol } };
            EditorGUILayout.LabelField(stateLbl, badgeStyle);

            EditorGUILayout.Space(4);

            // Undo / Redo
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_undoStack.Count == 0);
            if (GUILayout.Button("◄ Undo")) UndoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redoStack.Count == 0);
            if (GUILayout.Button("Redo ►")) RedoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Always: Position / Rotation / Scale ───────────────────────────

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = EditorGUILayout.Vector3Field("Position (local)", t.position);
            if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.position = newPos; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }

            EditorGUI.BeginChangeCheck();
            Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation (euler)", t.rotation.eulerAngles);
            if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.rotation = Quaternion.Euler(newEuler); t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }

            EditorGUI.BeginChangeCheck();
            Vector3 newScale = EditorGUILayout.Vector3Field("Scale", t.scale);
            if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.scale = newScale; t.isDirty = true; EndEdit(); }

            EditorGUILayout.Space(4);

            // ── Profile-gated groups ──────────────────────────────────────────

            if (ShowWeldGroup())
            {
                // Weld gizmo toggle — places two draggable handles in SceneView
                EditorGUI.BeginChangeCheck();
                bool newGizmo = EditorGUILayout.ToggleLeft(
                    new GUIContent("Use scene gizmo (drag two handles)",
                        "Places an orange (A) and yellow (B) handle in SceneView.\n" +
                        "The direction A→B defines the weld axis; the distance defines the weld length."),
                    t.weldGizmoActive);
                if (EditorGUI.EndChangeCheck() && newGizmo != t.weldGizmoActive)
                {
                    t.weldGizmoActive = newGizmo;
                    if (newGizmo) InitWeldGizmo(ref t);
                    SceneView.RepaintAll();
                }

                if (t.weldGizmoActive)
                {
                    // Show live-computed values as read-only
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Vector3Field("Weld Axis (A→B)", t.weldAxis);
                    EditorGUILayout.FloatField("Weld Length (|A→B|)", t.weldLength);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newAxis = EditorGUILayout.Vector3Field("Weld Axis (direction)", t.weldAxis);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        t.weldAxis = newAxis.sqrMagnitude > 0.001f ? newAxis.normalized : newAxis;
                        t.isDirty  = true;
                        EndEdit();
                        SceneView.RepaintAll();
                    }

                    EditorGUI.BeginChangeCheck();
                    float newLen = EditorGUILayout.FloatField("Weld Length", t.weldLength);
                    if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.weldLength = Mathf.Max(0f, newLen); t.isDirty = true; EndEdit(); }
                }
            }

            if (ShowPortGroup())
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPortA = EditorGUILayout.Vector3Field("Port A (local)", t.portA);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.portA = newPortA; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPortB = EditorGUILayout.Vector3Field("Port B (local)", t.portB);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.portB = newPortB; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            // "Use Tool Action Rotation" / "Tool Action Rotation" are no longer exposed —
            // WriteJson always derives them from the Rotation field above, so the gizmo
            // rotation is the single source of truth for both visual placement and play-mode
            // tool orientation.

            EditorGUILayout.Space(6);

            // Click-to-snap toggle
            EditorGUI.BeginChangeCheck();
            _clickToSnapActive = EditorGUILayout.Toggle(
                new GUIContent("Click-to-Snap",
                    "Enable, then left-click any mesh surface in SceneView.\n" +
                    "Target snaps to that point; rotation and weld axis auto-align to surface normal."),
                _clickToSnapActive);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            if (_clickToSnapActive)
                EditorGUILayout.HelpBox("Left-click a mesh surface in SceneView to snap.", MessageType.Info);
        }

        private void DrawBatchPanel()
        {
            int count = _multiSelected.Count;
            EditorGUILayout.LabelField($"Batch edit — {count} targets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Values shown are from the primary (last-clicked) target.\n" +
                "Any field you change is applied to ALL selected targets.",
                MessageType.None);
            EditorGUILayout.Space(4);

            // Use primary selection as representative for current values
            if (_selectedIdx < 0 || _selectedIdx >= _targets.Length) return;
            ref TargetEditState rep = ref _targets[_selectedIdx];

            // ── Position offset ───────────────────────────────────────────────
            // Each target keeps its own position; this shifts ALL selected by a delta.
            EditorGUILayout.LabelField("Position offset (added to all selected)", EditorStyles.boldLabel);
            _batchPositionOffset = EditorGUILayout.Vector3Field("Offset (X, Y, Z)", _batchPositionOffset);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply offset"))
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position += _batchPositionOffset; t.isDirty = true; }
                _batchPositionOffset = Vector3.zero;
                SceneView.RepaintAll(); Repaint();
            }
            if (GUILayout.Button("Reset"))
                _batchPositionOffset = Vector3.zero;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // ── Position (absolute, all selected) ─────────────────────────────
            // Sets every selected target to the exact same position.
            EditorGUILayout.LabelField("Position (absolute, all selected)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector3 batchPos = EditorGUILayout.Vector3Field("Position (local)", rep.position);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position = batchPos; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Rotation ──────────────────────────────────────────────────────
            // Useful for setting all clamp/tool targets to the same approach orientation.
            EditorGUILayout.LabelField("Rotation (absolute, all selected)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector3 batchEuler = EditorGUILayout.Vector3Field("Rotation (euler)", rep.rotation.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion batchRot = Quaternion.Euler(batchEuler);
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.rotation = batchRot; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Scale ─────────────────────────────────────────────────────────
            // Standardise target sphere radius across a group.
            EditorGUILayout.LabelField("Scale (all selected)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Vector3 batchScale = EditorGUILayout.Vector3Field("Scale", rep.scale);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.scale = batchScale; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Weld ──────────────────────────────────────────────────────────
            if (ShowWeldGroup())
            {
                EditorGUILayout.LabelField("Weld (all selected)", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Vector3 newAxis = EditorGUILayout.Vector3Field("Weld Axis (direction)", rep.weldAxis);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 norm = newAxis.sqrMagnitude > 0.001f ? newAxis.normalized : newAxis;
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.weldAxis = norm; t.isDirty = true; }
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                float newLen = EditorGUILayout.FloatField("Weld Length", rep.weldLength);
                if (EditorGUI.EndChangeCheck())
                {
                    float clamped = Mathf.Max(0f, newLen);
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.weldLength = clamped; t.isDirty = true; }
                }
                EditorGUILayout.Space(4);
            }

            // ── Ports ─────────────────────────────────────────────────────────
            if (ShowPortGroup())
            {
                EditorGUILayout.LabelField("Ports (all selected)", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                Vector3 newPortA = EditorGUILayout.Vector3Field("Port A (local)", rep.portA);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.portA = newPortA; t.isDirty = true; }
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newPortB = EditorGUILayout.Vector3Field("Port B (local)", rep.portB);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.portB = newPortB; t.isDirty = true; }
                    SceneView.RepaintAll();
                }
                EditorGUILayout.Space(4);
            }

            // ── Persistent tool ───────────────────────────────────────────────
            // Batch-sets the persistent flag on every tool definition mapped to the
            // selected targets. Only shown when all selected targets share a mapped tool.
            if (_targetToolIdMap != null && _pkg?.tools != null)
            {
                // Collect the set of tool IDs referenced by the selection
                var batchToolIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (int idx in _multiSelected)
                    if (_targetToolIdMap.TryGetValue(_targets[idx].def.id, out string tid))
                        batchToolIds.Add(tid);

                if (batchToolIds.Count > 0)
                {
                    // Representative value from primary target's tool
                    bool repPersist = false;
                    if (_targetToolIdMap.TryGetValue(rep.def.id, out string repToolId))
                        foreach (var td in _pkg.tools)
                            if (td != null && td.id == repToolId) { repPersist = td.persistent; break; }

                    EditorGUILayout.LabelField("Tool (all mapped tools)", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    bool newPersist = EditorGUILayout.ToggleLeft(
                        new GUIContent("Persistent tool (stays in scene after use)",
                            "Sets persistent=true on every tool definition that is mapped to any of the selected targets."),
                        repPersist, EditorStyles.miniLabel);
                    if (EditorGUI.EndChangeCheck())
                    {
                        foreach (string toolId in batchToolIds)
                            foreach (var td in _pkg.tools)
                                if (td != null && td.id == toolId)
                                { td.persistent = newPersist; _dirtyToolIds.Add(toolId); }
                    }
                    EditorGUILayout.Space(4);
                }
            }
        }

        // Returns true when profile calls for this field group, or when no step is selected.
        // Field group visibility — "All Steps" (null profile) shows everything.
        private bool ShowWeldGroup()    => string.IsNullOrEmpty(_activeStepProfile)
                                          || _activeStepProfile == "Weld"
                                          || _activeStepProfile == "Cut";

        // portA/portB are hose/cable endpoints (pipe_connection / Cable profile).
        // Measure steps (framing square) only need position+rotation.
        private bool ShowPortGroup()    => string.IsNullOrEmpty(_activeStepProfile)
                                          || _activeStepProfile == "Cable";

        // ── Actions ───────────────────────────────────────────────────────────

        private void DrawActions()
        {
            bool anyDirty = AnyDirty();

            EditorGUI.BeginDisabledGroup(!anyDirty);
            GUI.backgroundColor = anyDirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (GUILayout.Button("Write to machine.json", GUILayout.Height(28))) WriteJson();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Extract from GLB Anchors"))
                ExtractFromGlbAnchors();

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write (restore backup)"))
                RevertFromBackup();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Frame in SceneView")) FrameInScene();
        }

        // ── SceneView ─────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            if (_targets == null || _targets.Length == 0 || _previewRoot == null)
                return;

            Transform root         = _previewRoot.transform;
            bool      hasStepFilter = _activeStepTargetIds != null;

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                Vector3 worldPos = root.TransformPoint(t.position);
                float   size     = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                bool isSelected  = i == _selectedIdx;
                bool inStep      = !hasStepFilter || (_activeStepTargetIds?.Contains(t.def.id) ?? true);
                float alpha      = inStep ? 1f : 0.2f;

                Color col = isSelected ? ColSelected
                          : t.isDirty  ? ColDirty
                          : t.hasPlacement ? ColAuthored
                          : ColNoPlacement;
                col.a *= alpha;
                Handles.color = col;

                if (inStep)
                {
                    if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                    {
                        _selectedIdx       = i;
                        _clickToSnapActive = false;
                        _snapshotPending   = false;
                        RefreshToolPreview(ref _targets[i]);
                        Repaint();
                    }
                }
                else
                {
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }

                DrawWeldAxisArrow(ref t, worldPos, alpha);
                DrawPortPoints(ref t, root, alpha);
                DrawPartConnector(ref t, worldPos, alpha);
            }

            // F key → frame on selected target gizmo
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.F)
            {
                ref TargetEditState ft = ref _targets[_selectedIdx];
                Vector3 worldPos = root.TransformPoint(ft.position);
                float frameSize = HandleUtility.GetHandleSize(worldPos) * 0.5f;
                sv.Frame(new Bounds(worldPos, Vector3.one * frameSize), false);
                Event.current.Use();
            }

            // Handles for selected target
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                ref TargetEditState sel     = ref _targets[_selectedIdx];
                Vector3    worldPos = root.TransformPoint(sel.position);
                Quaternion worldRot = Quaternion.Normalize(root.rotation * sel.rotation);
                float      size     = HandleUtility.GetHandleSize(worldPos) * 0.14f;

                Handles.color = ColSelected;
                Handles.DrawWireDisc(worldPos, sv.camera.transform.forward, size * 1.6f);

                EditorGUI.BeginChangeCheck();
                Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, handleRot);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    sel.position = root.InverseTransformPoint(newWorldPos);
                    sel.isDirty  = true;
                    Repaint();
                }

                EditorGUI.BeginChangeCheck();
                Quaternion newWorldRot = Handles.RotationHandle(worldRot, worldPos);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    sel.rotation = Quaternion.Inverse(root.rotation) * newWorldRot;
                    sel.isDirty  = true;
                    Repaint();
                }

                if (Event.current.type == EventType.MouseUp)
                    EndEdit();

                // Tool preview — tracks the position/rotation gizmo in real-time
                UpdateToolPreview(ref sel);

                // portA / portB drag handles (Cable profile only)
                if (_activeStepProfile == "Cable")
                {
                    Handles.color = ColPortPoint;

                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortA = Handles.PositionHandle(root.TransformPoint(sel.portA), Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        sel.portA   = root.InverseTransformPoint(newPortA);
                        sel.isDirty = true;
                        Repaint();
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortB = Handles.PositionHandle(root.TransformPoint(sel.portB), Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        sel.portB   = root.InverseTransformPoint(newPortB);
                        sel.isDirty = true;
                        Repaint();
                    }

                    if (Event.current.type == EventType.MouseUp) EndEdit();
                }

                // Weld gizmo handles — two draggable PositionHandles defining axis + length
                if (sel.weldGizmoActive && ShowWeldGroup())
                {
                    Vector3 worldA = root.TransformPoint(sel.weldGizmoA);
                    Vector3 worldB = root.TransformPoint(sel.weldGizmoB);

                    // Handle A (orange — start)
                    Handles.color = new Color(1f, 0.5f, 0f, 1f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldA = Handles.PositionHandle(worldA, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        sel.weldGizmoA = root.InverseTransformPoint(newWorldA);
                        RecomputeWeldFromGizmo(ref sel);
                        sel.isDirty = true;
                        Repaint();
                    }

                    // Handle B (yellow — tip / direction)
                    Handles.color = new Color(1f, 0.9f, 0f, 1f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldB = Handles.PositionHandle(worldB, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        sel.weldGizmoB = root.InverseTransformPoint(newWorldB);
                        RecomputeWeldFromGizmo(ref sel);
                        sel.isDirty = true;
                        Repaint();
                    }

                    // Visual line A→B with arrow
                    Handles.color = Color.white;
                    Handles.DrawLine(worldA, worldB, 2f);
                    if ((worldB - worldA).sqrMagnitude > 0.0001f)
                    {
                        float arrowSize = HandleUtility.GetHandleSize(worldA) * 0.3f;
                        Handles.ArrowHandleCap(0, worldA,
                            Quaternion.LookRotation((worldB - worldA).normalized),
                            arrowSize, EventType.Repaint);
                    }

                    // Labels
                    Handles.Label(worldA, "A", EditorStyles.boldLabel);
                    Handles.Label(worldB, $"B  ({sel.weldLength:F3} m)", EditorStyles.boldLabel);

                    if (Event.current.type == EventType.MouseUp) EndEdit();
                }
            }

            HandleClickToSnap();
        }

        private void DrawWeldAxisArrow(ref TargetEditState t, Vector3 worldPos, float alpha = 1f)
        {
            if (t.weldAxis.sqrMagnitude < 0.001f) return;
            Vector3 worldAxis = _previewRoot.transform.TransformDirection(t.weldAxis.normalized);
            float   arrowLen  = HandleUtility.GetHandleSize(worldPos) * 1.2f;
            Color   c         = ColWeldAxis; c.a *= alpha;
            Handles.color = c;
            Handles.DrawAAPolyLine(2.5f,
                worldPos - worldAxis * arrowLen * 0.5f,
                worldPos + worldAxis * arrowLen * 0.5f);
            Handles.ConeHandleCap(0,
                worldPos + worldAxis * arrowLen * 0.5f,
                Quaternion.LookRotation(worldAxis),
                arrowLen * 0.14f,
                EventType.Repaint);
        }

        /// <summary>
        /// Draws a thin dashed line from the target sphere to the associated part's
        /// origin, so authors can visually confirm the target is in the right coordinate
        /// space relative to its part.
        /// </summary>
        private void DrawPartConnector(ref TargetEditState t, Vector3 worldPos, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(t.def.associatedPartId)) return;
            if (!_partMeshes.TryGetValue(t.def.associatedPartId, out var partGo) || partGo == null) return;

            Color c = Handles.color;
            c.a = alpha * 0.25f;
            Handles.color = c;
            Handles.DrawDottedLine(worldPos, partGo.transform.position, 3f);
        }

        private void InitWeldGizmo(ref TargetEditState t)
        {
            t.weldGizmoA = t.position;
            float   len = t.weldLength > 0.0001f ? t.weldLength : 0.05f;
            Vector3 dir = t.weldAxis.sqrMagnitude > 0.001f ? t.weldAxis.normalized : Vector3.forward;
            t.weldGizmoB = t.position + dir * len;
        }

        private static void RecomputeWeldFromGizmo(ref TargetEditState t)
        {
            Vector3 delta = t.weldGizmoB - t.weldGizmoA;
            t.weldLength  = delta.magnitude;
            t.weldAxis    = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
        }

        private void DrawPortPoints(ref TargetEditState t, Transform root, float alpha = 1f)
        {
            // Show port spheres for Cable (pipe_connection) profile, or in All Steps mode
            if (!string.IsNullOrEmpty(_activeStepProfile) && _activeStepProfile != "Cable") return;
            if (t.portA.sqrMagnitude < 0.00001f && t.portB.sqrMagnitude < 0.00001f) return;

            Color c = ColPortPoint; c.a *= alpha;
            Handles.color = c;
            float sz = HandleUtility.GetHandleSize(root.TransformPoint(t.portA)) * 0.07f;

            Vector3 wA = root.TransformPoint(t.portA);
            Vector3 wB = root.TransformPoint(t.portB);
            Handles.SphereHandleCap(0, wA, Quaternion.identity, sz, EventType.Repaint);
            Handles.SphereHandleCap(0, wB, Quaternion.identity, sz, EventType.Repaint);
            Handles.DrawDottedLine(wA, wB, 4f);
        }

        private void HandleClickToSnap()
        {
            if (!_clickToSnapActive) return;
            if (_selectedIdx < 0 || _targets == null || _selectedIdx >= _targets.Length) return;

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt || e.control || e.shift)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return;

            // Only snap if we hit one of our spawned part meshes
            bool hitPartMesh = false;
            foreach (var kv in _partMeshes)
            {
                if (kv.Value != null && hit.transform.IsChildOf(kv.Value.transform))
                { hitPartMesh = true; break; }
            }
            if (!hitPartMesh) return;

            ref TargetEditState sel = ref _targets[_selectedIdx];
            BeginEdit();
            Transform root  = _previewRoot.transform;
            sel.position    = root.InverseTransformPoint(hit.point);
            Vector3 localN  = root.InverseTransformDirection(hit.normal).normalized;
            sel.rotation    = Quaternion.FromToRotation(Vector3.up, localN);
            sel.weldAxis    = localN;
            sel.isDirty     = true;
            EndEdit();

            _clickToSnapActive = false;
            e.Use();
            Repaint();
            SceneView.RepaintAll();
        }

        // ── Package loading ───────────────────────────────────────────────────

        private void RefreshPackageList()
        {
            string root = PackageJsonUtils.AuthoringRoot;
            if (!Directory.Exists(root)) { _packageIds = Array.Empty<string>(); return; }
            var dirs = Directory.GetDirectories(root);
            var ids  = new List<string>();
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "machine.json"))) ids.Add(Path.GetFileName(d));
            _packageIds = ids.ToArray();
        }

        private void LoadPkg(string id)
        {
            Cleanup();
            _pkg   = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            if (_pkg == null) return;

            _stepFilterIdx = 0;
            BuildStepOptions();
            BuildTargetToolMap();

            // Sync initial step from SessionDriver if present
            var driver = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
            if (driver != null && _stepSequenceIdxs != null)
            {
                int seq = driver.PreviewStepSequenceIndex;
                for (int k = 1; k < _stepSequenceIdxs.Length; k++)
                {
                    if (_stepSequenceIdxs[k] == seq) { _stepFilterIdx = k; break; }
                }
            }

            UpdateActiveStep();
            BuildTargetList();
            RespawnScene();
        }

        private void BuildStepOptions()
        {
            var optList  = new List<string> { "(All Steps)" };
            var idList   = new List<string> { null };
            var seqList  = new List<int>    { 0 };

            if (_pkg?.steps != null)
            {
                // Include ALL steps so session driver and authoring window always
                // navigate the same step index. Steps without targets show empty list.
                var allSteps = new List<StepDefinition>(_pkg.steps.Length);
                foreach (var step in _pkg.steps)
                    if (step != null) allSteps.Add(step);
                allSteps.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                foreach (var step in allSteps)
                {
                    string toolName = "(no tool)";
                    if (step.relevantToolIds != null && step.relevantToolIds.Length > 0 && _pkg.tools != null)
                    {
                        foreach (var td in _pkg.tools)
                        {
                            if (td != null && td.id == step.relevantToolIds[0])
                            { toolName = td.name; break; }
                        }
                    }

                    int targetCount    = step.targetIds?.Length ?? 0;
                    string profilePart = string.IsNullOrEmpty(step.profile) ? "" : $"  ·  {step.profile}";
                    string noTargets   = targetCount == 0 ? "  ·  (no targets)" : "";
                    string display     = $"[{step.sequenceIndex}] {step.name}  ·  {toolName}{profilePart}{noTargets}";
                    optList.Add(display);
                    idList.Add(step.id);
                    seqList.Add(step.sequenceIndex);
                }
            }

            _stepOptions      = optList.ToArray();
            _stepIds          = idList.ToArray();
            _stepSequenceIdxs = seqList.ToArray();
        }

        /// <summary>
        /// Builds a reverse map: targetId → display tool name, sourced from
        /// step.requiredToolActions[].toolId → ToolDefinition.name.
        /// First match wins (one tool per target is the common case).
        /// </summary>
        private void BuildTargetToolMap()
        {
            _targetToolMap    = new Dictionary<string, string>(StringComparer.Ordinal);
            _targetToolIdMap  = new Dictionary<string, string>(StringComparer.Ordinal);
            _toolActionTargetIds = new HashSet<string>(StringComparer.Ordinal);
            if (_pkg?.steps == null) return;

            foreach (var step in _pkg.steps)
            {
                if (step?.requiredToolActions == null) continue;
                foreach (var action in step.requiredToolActions)
                {
                    if (string.IsNullOrEmpty(action?.targetId) || string.IsNullOrEmpty(action.toolId)) continue;
                    _toolActionTargetIds.Add(action.targetId);
                    if (_targetToolMap.ContainsKey(action.targetId)) continue;

                    string toolName = action.toolId;   // fallback = raw id
                    if (_pkg.tools != null)
                        foreach (var td in _pkg.tools)
                            if (td != null && td.id == action.toolId) { toolName = td.name; break; }

                    _targetToolMap[action.targetId]   = toolName;
                    _targetToolIdMap[action.targetId] = action.toolId;
                }
            }
        }

        private void BuildTargetList()
        {
            if (_pkg?.targets == null) { _targets = Array.Empty<TargetEditState>(); return; }

            // Determine which targetIds to show
            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                string stepId = _stepIds[_stepFilterIdx];
                if (stepId != null)
                {
                    var step = FindStep(stepId);
                    if (step?.targetIds != null)
                        filterIds = new HashSet<string>(step.targetIds, StringComparer.Ordinal);
                }
            }

            // Build per-step tool-action target set for ghost filtering.
            // A "ghost" target is in a step's targetIds but has no requiredToolAction in THAT step.
            // (A target can be a tool-action target in step 5 but a ghost at step 1 — global set can't catch this.)
            HashSet<string> stepToolActionIds = null;
            if (!_showGhostTargets && _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var curStep = FindStep(_stepIds[_stepFilterIdx]);
                stepToolActionIds = new HashSet<string>(StringComparer.Ordinal);
                if (curStep?.requiredToolActions != null)
                    foreach (var action in curStep.requiredToolActions)
                        if (!string.IsNullOrEmpty(action?.targetId))
                            stepToolActionIds.Add(action.targetId);
            }

            var list = new List<TargetEditState>();
            foreach (var def in _pkg.targets)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;
                // Ghost targets: in a step's targetIds but not linked to any tool action in THIS step.
                if (stepToolActionIds != null && !stepToolActionIds.Contains(def.id)) continue;

                TargetPreviewPlacement placement = FindPlacement(def.id);
                bool hasP = placement != null;

                // For unplaced targets, default position to the associated part's playPosition
                // so they appear ON the part in the SceneView rather than at world origin.
                Vector3 defaultPos = Vector3.zero;
                if (!hasP && !string.IsNullOrEmpty(def.associatedPartId))
                {
                    var pp = FindPartPlacement(def.associatedPartId);
                    if (pp != null) defaultPos = PackageJsonUtils.ToVector3(pp.playPosition);
                }

                var state = new TargetEditState
                {
                    def                     = def,
                    placement               = placement,
                    hasPlacement            = hasP,
                    position                = hasP ? PackageJsonUtils.ToVector3(placement.position)         : defaultPos,
                    rotation                = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.rotation)  : Quaternion.identity,
                    scale                   = hasP ? PackageJsonUtils.ToVector3(placement.scale)             : Vector3.one * DefaultTargetScale,
                    portA                   = hasP ? PackageJsonUtils.ToVector3(placement.portA)             : Vector3.zero,
                    portB                   = hasP ? PackageJsonUtils.ToVector3(placement.portB)             : Vector3.zero,
                    weldAxis                = def.GetWeldAxisVector(),
                    weldLength              = def.weldLength,
                    useToolActionRotation   = def.useToolActionRotation,
                    toolActionRotationEuler = new Vector3(def.toolActionRotation.x, def.toolActionRotation.y, def.toolActionRotation.z),
                    isDirty                 = false,
                };
                list.Add(state);
            }

            // Preserve selection across rebuilds by matching target ID
            string prevSelectedId = (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                ? _targets[_selectedIdx].def.id : null;

            _targets     = list.ToArray();
            _selectedIdx = -1;
            if (prevSelectedId != null)
            {
                for (int i = 0; i < _targets.Length; i++)
                    if (_targets[i].def.id == prevSelectedId) { _selectedIdx = i; break; }
            }
            if (_selectedIdx < 0 && _targets.Length > 0) _selectedIdx = 0;
            _multiSelected.Clear();
            if (_selectedIdx >= 0) RefreshToolPreview(ref _targets[_selectedIdx]);
            else ClearToolPreview();
        }

        private TargetPreviewPlacement FindPlacement(string targetId)
        {
            var arr = _pkg?.previewConfig?.targetPlacements;
            if (arr == null) return null;
            foreach (var p in arr)
                if (p != null && p.targetId == targetId) return p;
            return null;
        }

        private PartPreviewPlacement FindPartPlacement(string partId)
        {
            var arr = _pkg?.previewConfig?.partPlacements;
            if (arr == null) return null;
            foreach (var p in arr)
                if (p != null && p.partId == partId) return p;
            return null;
        }

        private StepDefinition FindStep(string stepId)
        {
            if (_pkg?.steps == null) return null;
            foreach (var s in _pkg.steps)
                if (s != null && s.id == stepId) return s;
            return null;
        }

        private string FindToolName(string toolId)
        {
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) return td.name;
            return toolId; // fallback to raw id
        }

        /// <summary>
        /// Returns the set of persistent-tool IDs that are "live" immediately before
        /// <paramref name="atStep"/> starts — i.e. placed by a prior step and not yet
        /// explicitly removed by an intermediate step's removePersistentToolIds.
        /// </summary>
        private HashSet<string> GetActivePersistentToolIds(StepDefinition atStep)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (_pkg?.steps == null) return active;

            // Walk steps in sequence order up to (but not including) atStep
            foreach (var s in _pkg.steps)
            {
                if (s == null || s.sequenceIndex >= atStep.sequenceIndex) continue;

                // Any tool action that uses a persistent tool adds it
                if (s.requiredToolActions != null)
                    foreach (var action in s.requiredToolActions)
                    {
                        if (string.IsNullOrEmpty(action?.toolId)) continue;
                        if (IsToolPersistent(action.toolId)) active.Add(action.toolId);
                    }

                // Explicit removals from this intermediate step clean it up
                if (s.removePersistentToolIds != null)
                    foreach (var id in s.removePersistentToolIds)
                        active.Remove(id);
            }

            return active;
        }

        private bool IsToolPersistent(string toolId)
        {
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) return td.persistent;
            return false;
        }

        // ── Scene spawning ────────────────────────────────────────────────────

        private void RespawnScene()
        {
            KillPartMeshes();
            if (_previewRoot != null) { DestroyImmediate(_previewRoot); _previewRoot = null; }
            if (_pkg?.previewConfig?.partPlacements == null) return;

            _previewRoot = new GameObject("[ToolTargetAuthoring] PreviewRoot")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            // Match the scene's PreviewRoot transform so target positions align with
            // the actual parts (e.g. when PreviewRoot is offset for XR testing).
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner)
                && spawner.PreviewRoot != null)
            {
                Transform sceneRoot = spawner.PreviewRoot;
                _previewRoot.transform.SetPositionAndRotation(sceneRoot.position, sceneRoot.rotation);
                _previewRoot.transform.localScale = sceneRoot.lossyScale;
            }

            _previewAssembled = 0;
            _previewCurrent   = 0;
            _previewHidden    = 0;

            // Build part → sequenceIndex map for step-aware placement
            bool stepSelected = _stepFilterIdx > 0 && _stepIds != null
                                && _stepFilterIdx < _stepIds.Length
                                && _stepIds[_stepFilterIdx] != null;

            // Default: show everything (All Steps mode or step not found)
            int currentSeq  = int.MaxValue;
            var partStepSeq = new Dictionary<string, int>(StringComparer.Ordinal);
            // Parts that belong to any subassembly always use playPosition — they are
            // pre-assembled before their step and have no meaningful individual startPosition.
            var subassemblyPartIds = new HashSet<string>(StringComparer.Ordinal);

            if (stepSelected && _pkg.steps != null)
            {
                var sel = FindStep(_stepIds[_stepFilterIdx]);
                // Keep int.MaxValue if step not found so nothing is wrongly hidden
                if (sel != null) currentSeq = sel.sequenceIndex;

                foreach (var step in _pkg.steps)
                {
                    // Map from requiredPartIds (individual placement steps)
                    if (step?.requiredPartIds != null)
                    {
                        foreach (string pid in step.requiredPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }

                    // Map from requiredSubassemblyId → subassembly member parts
                    if (!string.IsNullOrEmpty(step?.requiredSubassemblyId)
                        && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subDef)
                        && subDef?.partIds != null)
                    {
                        foreach (string pid in subDef.partIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                            subassemblyPartIds.Add(pid);
                        }
                    }
                }
            }

            foreach (var pp in _pkg.previewConfig.partPlacements)
            {
                if (pp == null || string.IsNullOrEmpty(pp.partId)) continue;
                if (!_pkg.TryGetPart(pp.partId, out PartDefinition partDef)) continue;
                if (string.IsNullOrEmpty(partDef.assetRef)) continue;

                if (stepSelected)
                {
                    bool inStepMap = partStepSeq.TryGetValue(pp.partId, out int placedAt);
                    if (!inStepMap)
                    {
                        // Part not assigned to any step — always show at playPosition
                        _previewAssembled++;
                        SpawnPartMesh(pp.partId, partDef.assetRef,
                            PackageJsonUtils.ToVector3(pp.playPosition),
                            PackageJsonUtils.ToUnityQuaternion(pp.playRotation),
                            PackageJsonUtils.ToVector3(pp.playScale));
                        continue;
                    }

                    if (placedAt > currentSeq) { _previewHidden++; continue; }

                    // Subassembly members are pre-assembled before their step so they
                    // have no meaningful individual startPosition — always use playPosition.
                    bool useStart = placedAt == currentSeq && !subassemblyPartIds.Contains(pp.partId);
                    if (placedAt == currentSeq) _previewCurrent++; else _previewAssembled++;
                    Vector3    pos  = useStart ? PackageJsonUtils.ToVector3(pp.startPosition) : PackageJsonUtils.ToVector3(pp.playPosition);
                    Quaternion rot  = useStart ? PackageJsonUtils.ToUnityQuaternion(pp.startRotation) : PackageJsonUtils.ToUnityQuaternion(pp.playRotation);
                    Vector3    sclR = PackageJsonUtils.ToVector3(useStart ? pp.startScale : pp.playScale);
                    // Fall back to playScale when startScale is zero (not authored in machine.json)
                    Vector3 scl = sclR.sqrMagnitude > 0.00001f ? sclR : PackageJsonUtils.ToVector3(pp.playScale);
                    SpawnPartMesh(pp.partId, partDef.assetRef, pos, rot, scl);
                }
                else
                {
                    // All Steps mode — show everything at play position
                    _previewAssembled++;
                    SpawnPartMesh(pp.partId, partDef.assetRef,
                        PackageJsonUtils.ToVector3(pp.playPosition),
                        PackageJsonUtils.ToUnityQuaternion(pp.playRotation),
                        PackageJsonUtils.ToVector3(pp.playScale));
                }
            }

            AddMeshColliders();

            // Refresh tool preview now that _previewRoot exists
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                RefreshToolPreview(ref _targets[_selectedIdx]);
        }

        private void SpawnPartMesh(string partId, string assetRef, Vector3 localPos, Quaternion localRot, Vector3 localScl)
        {
            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // Fallback: try assets/parts/ prefix when assetRef is a bare filename
            if (pfb == null && !assetRef.Contains("/"))
            {
                string prefixed = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/{assetRef}";
                pfb = AssetDatabase.LoadAssetAtPath<GameObject>(prefixed);
                if (pfb == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefixed))
                        if (asset is GameObject go2) { pfb = go2; break; }
                if (pfb != null) path = prefixed;
            }

            if (pfb == null)
            {
                Debug.LogWarning($"[ToolTargetAuthoring] Asset not found: {path}");
                return;
            }

            var go = Instantiate(pfb, _previewRoot.transform);
            go.name      = $"[ToolTargetAuthoring] {partId}";
            go.hideFlags = HideFlags.HideAndDontSave;

            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = localScl;

            // Remove existing colliders; MeshColliders added by AddMeshColliders
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(c);

            _partMeshes[partId] = go;
        }

        private void AddMeshColliders()
        {
            foreach (var kv in _partMeshes)
            {
                if (kv.Value == null) continue;
                foreach (var mf in kv.Value.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
        }

        private void KillPartMeshes()
        {
            foreach (var kv in _partMeshes)
                if (kv.Value != null) DestroyImmediate(kv.Value);
            _partMeshes.Clear();
        }

        // ── Tool preview ──────────────────────────────────────────────────────

        private void ClearToolPreview()
        {
            if (_toolPreviewGO != null) { DestroyImmediate(_toolPreviewGO); _toolPreviewGO = null; }
            _toolPreviewDef = null;
        }

        private void RefreshToolPreview(ref TargetEditState t)
        {
            ClearToolPreview();
            if (!_showToolPreview || _previewRoot == null) return;
            if (string.IsNullOrEmpty(_pkgId)) return;

            if (_targetToolIdMap == null || !_targetToolIdMap.TryGetValue(t.def.id, out string toolId)) return;

            ToolDefinition toolDef = null;
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) { toolDef = td; break; }

            if (toolDef == null || string.IsNullOrEmpty(toolDef.assetRef)) return;

            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{toolDef.assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // Fallback: try assets/tools/ prefix when assetRef is a bare filename
            if (pfb == null && !toolDef.assetRef.Contains("/"))
            {
                string prefixed = $"Assets/_Project/Data/Packages/{_pkgId}/assets/tools/{toolDef.assetRef}";
                pfb = AssetDatabase.LoadAssetAtPath<GameObject>(prefixed);
                if (pfb == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefixed))
                        if (asset is GameObject go2) { pfb = go2; break; }
                if (pfb != null) path = prefixed;
            }

            if (pfb == null)
            {
                Debug.LogWarning($"[ToolTargetAuthoring] Tool asset not found: {path}");
                return;
            }

            // Spawn as a child of _previewRoot so it lives in the same coordinate space
            _toolPreviewGO           = Instantiate(pfb, _previewRoot.transform);
            _toolPreviewGO.name      = "[ToolTargetAuthoring] ToolPreview";
            _toolPreviewGO.hideFlags = HideFlags.HideAndDontSave;

            // Match the runtime cursor scale (ToolCursorManager.CursorUniformScale = 0.16).
            // The previewRoot may have a non-unit lossyScale (e.g. if parts use a scaled root),
            // so we divide by it to get the correct world-space size.
            const float RuntimeCursorScale = 0.16f;
            float toolCursorScale = (toolDef.scaleOverride > 0f)
                ? RuntimeCursorScale * toolDef.scaleOverride
                : RuntimeCursorScale;
            float rootS = _previewRoot.transform.lossyScale.x;
            float localToolScale = Mathf.Approximately(rootS, 0f) ? toolCursorScale : toolCursorScale / rootS;
            _toolPreviewGO.transform.localScale = Vector3.one * localToolScale;

            // Remove colliders — preview only, must not interfere with click-to-snap raycasts
            foreach (var c in _toolPreviewGO.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(c);

            // Cyan tint via MaterialPropertyBlock to distinguish from real parts
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.35f, 0.85f, 1f, 1f));
            block.SetColor("_Color",     new Color(0.35f, 0.85f, 1f, 1f)); // Standard fallback
            foreach (var r in _toolPreviewGO.GetComponentsInChildren<Renderer>(true))
                r.SetPropertyBlock(block);

            _toolPreviewDef = toolDef;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Computes the tool's local position and rotation under _previewRoot so that
        /// the tool's tipPoint sits at the target position and gripRotation is respected.
        /// </summary>
        private void ComputeToolLocalTransform(ref TargetEditState t,
            out Vector3 localPos, out Quaternion localRot)
        {
            // t.rotation is the single source of truth (gizmo + Euler field).
            // WriteJson derives toolActionRotation from it at save time.
            Quaternion approachRot = t.rotation;

            // Undo the grip rotation so the tool sits naturally in the target orientation
            localRot = approachRot;
            if (_toolPreviewDef?.toolPose?.HasGripRotation == true)
                localRot = approachRot * Quaternion.Inverse(_toolPreviewDef.toolPose.GetGripRotation());

            // Offset so tipPoint lands exactly on the target position.
            // tipPoint is in the tool's local space; multiply by the GO's localScale
            // so the offset is correct regardless of cursor scale.
            localPos = t.position;
            if (_toolPreviewDef?.toolPose?.HasTipPoint == true)
            {
                float s = _toolPreviewGO != null ? _toolPreviewGO.transform.localScale.x : 1f;
                localPos = t.position - localRot * (_toolPreviewDef.toolPose.GetTipPoint() * s);
            }
        }

        private void UpdateToolPreview(ref TargetEditState sel)
        {
            if (!_showToolPreview || _toolPreviewGO == null || _previewRoot == null) return;

            ComputeToolLocalTransform(ref sel, out Vector3 localPos, out Quaternion localRot);
            _toolPreviewGO.transform.localPosition = localPos;
            _toolPreviewGO.transform.localRotation = localRot;

            // Yellow dot at the tip contact point
            if (_toolPreviewDef?.toolPose?.HasTipPoint == true)
            {
                Vector3 tipWorld = _toolPreviewGO.transform.TransformPoint(
                    _toolPreviewDef.toolPose.GetTipPoint());
                float tipSize = HandleUtility.GetHandleSize(tipWorld) * 0.06f;
                Handles.color = new Color(1f, 0.85f, 0.1f, 1f);
                Handles.SphereHandleCap(0, tipWorld, Quaternion.identity, tipSize, EventType.Repaint);
            }
        }

        private void FrameInScene()
        {
            if (_previewRoot == null) return;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            // Frame on selected target position if available
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                Vector3 worldPos = _previewRoot.transform.TransformPoint(_targets[_selectedIdx].position);
                float frameSize = HandleUtility.GetHandleSize(worldPos) * 0.5f;
                sv.Frame(new Bounds(worldPos, Vector3.one * frameSize), false);
            }
            else
            {
                Selection.activeGameObject = _previewRoot;
                sv.FrameSelected();
            }

            sv.Repaint();
        }

        // ── Undo / Redo ───────────────────────────────────────────────────────

        private TargetSnapshot CaptureSnapshot(ref TargetEditState t) => new()
        {
            position               = t.position,
            rotation               = t.rotation,
            scale                  = t.scale,
            weldAxis               = t.weldAxis,
            weldLength             = t.weldLength,
            useToolActionRotation  = t.useToolActionRotation,
            toolActionRotationEuler= t.toolActionRotationEuler,
            portA                  = t.portA,
            portB                  = t.portB,
        };

        private void BeginEdit()
        {
            if (_snapshotPending || _selectedIdx < 0 || _targets == null) return;
            _undoStack.Add((_selectedIdx, CaptureSnapshot(ref _targets[_selectedIdx])));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _snapshotPending = true;
        }

        private void EndEdit() => _snapshotPending = false;

        private void UndoPose()
        {
            if (_undoStack.Count == 0 || _targets == null) return;
            var (idx, prev) = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (idx < _targets.Length)
            {
                _redoStack.Add((idx, CaptureSnapshot(ref _targets[idx])));
                ApplySnapshot(idx, prev);
            }
        }

        private void RedoPose()
        {
            if (_redoStack.Count == 0 || _targets == null) return;
            var (idx, next) = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            if (idx < _targets.Length)
            {
                _undoStack.Add((idx, CaptureSnapshot(ref _targets[idx])));
                ApplySnapshot(idx, next);
            }
        }

        private void ApplySnapshot(int idx, TargetSnapshot s)
        {
            ref TargetEditState t   = ref _targets[idx];
            t.position               = s.position;
            t.rotation               = s.rotation;
            t.scale                  = s.scale;
            t.weldAxis               = s.weldAxis;
            t.weldLength             = s.weldLength;
            t.useToolActionRotation  = s.useToolActionRotation;
            t.toolActionRotationEuler= s.toolActionRotationEuler;
            t.portA                  = s.portA;
            t.portB                  = s.portB;
            t.isDirty        = true;
            _snapshotPending = false;
            Repaint();
            SceneView.RepaintAll();
        }

        // ── Write to JSON ─────────────────────────────────────────────────────

        private bool AnyDirty()
        {
            if (_dirtyToolIds.Count > 0 || _dirtyStepIds.Count > 0) return true;
            if (_targets == null) return false;
            foreach (var t in _targets) if (t.isDirty) return true;
            return false;
        }

        private void WriteJson()
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null || _targets == null) return;
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[ToolTargetAuthoring] machine.json not found for '{_pkgId}'"); return; }

            // Step 1: Update working pkg.previewConfig.targetPlacements
            if (_pkg.previewConfig == null) _pkg.previewConfig = new PackagePreviewConfig();
            var placements = _pkg.previewConfig.targetPlacements != null
                ? new List<TargetPreviewPlacement>(_pkg.previewConfig.targetPlacements)
                : new List<TargetPreviewPlacement>();

            foreach (ref TargetEditState t in _targets.AsSpan())
            {
                if (!t.isDirty) continue;
                string targetId = t.def.id;
                int    idx      = placements.FindIndex(p => p != null && p.targetId == targetId);
                TargetPreviewPlacement entry = idx >= 0
                    ? placements[idx]
                    : new TargetPreviewPlacement { targetId = t.def.id };

                entry.position = PackageJsonUtils.ToFloat3(t.position);
                entry.rotation = PackageJsonUtils.ToQuaternion(t.rotation);
                entry.scale    = PackageJsonUtils.ToFloat3(t.scale);
                entry.portA    = PackageJsonUtils.ToFloat3(t.portA);
                entry.portB    = PackageJsonUtils.ToFloat3(t.portB);

                if (idx < 0) entry.color = new SceneFloat4 { r = 0f, g = 0.9f, b = 1f, a = 0.7f };

                if (idx >= 0) placements[idx] = entry;
                else          placements.Add(entry);
            }
            _pkg.previewConfig.targetPlacements = placements.ToArray();

            // Step 2: Validate original JSON
            string json = File.ReadAllText(jsonPath);
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolTargetAuthoring] machine.json is already invalid, aborting.\n{ex.Message}");
                return;
            }

            // Step 3: Write previewConfig block
            PackageJsonUtils.WritePreviewConfig(jsonPath, _pkg.previewConfig);
            json = File.ReadAllText(jsonPath);

            // Step 4: Inject TargetDefinition fields for dirty targets
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in _targets)
            {
                if (!t.isDirty) continue;

                string axisJson = $"{{ \"x\": {R(t.weldAxis.x).ToString(inv)}, \"y\": {R(t.weldAxis.y).ToString(inv)}, \"z\": {R(t.weldAxis.z).ToString(inv)} }}";
                TryInjectBlock(ref json, t.def.id, "weldAxis", axisJson);

                if (t.weldLength > 0.0001f)
                    TryInjectBlock(ref json, t.def.id, "weldLength", R(t.weldLength).ToString(inv));

                // Placement rotation IS the tool action rotation — always enable it so play mode
                // respects whatever the author sets via the gizmo or euler field.
                Quaternion worldRot   = _previewRoot != null
                    ? _previewRoot.transform.rotation * t.rotation
                    : t.rotation;
                Vector3 worldEuler    = worldRot.eulerAngles;
                TryInjectBlock(ref json, t.def.id, "useToolActionRotation", "true");
                string tarJson = $"{{ \"x\": {R(worldEuler.x).ToString(inv)}, \"y\": {R(worldEuler.y).ToString(inv)}, \"z\": {R(worldEuler.z).ToString(inv)} }}";
                TryInjectBlock(ref json, t.def.id, "toolActionRotation", tarJson);
            }

            // Step 5a: Inject ToolDefinition.persistent for dirty tools
            foreach (string toolId in _dirtyToolIds)
            {
                ToolDefinition toolDef = null;
                if (_pkg?.tools != null)
                    foreach (var td in _pkg.tools)
                        if (td != null && td.id == toolId) { toolDef = td; break; }
                if (toolDef != null)
                    TryInjectBlock(ref json, toolId, "persistent", toolDef.persistent ? "true" : "false");
            }
            _dirtyToolIds.Clear();

            // Step 5b: Inject StepDefinition.removePersistentToolIds for dirty steps
            foreach (string stepId in _dirtyStepIds)
            {
                var step = FindStep(stepId);
                if (step == null) continue;
                string idsJson = step.removePersistentToolIds == null || step.removePersistentToolIds.Length == 0
                    ? "[]"
                    : "[ " + string.Join(", ", Array.ConvertAll(
                        step.removePersistentToolIds, id => $"\"{id}\"")) + " ]";
                TryInjectBlock(ref json, stepId, "removePersistentToolIds", idsJson);
            }
            _dirtyStepIds.Clear();

            // Step 5c: Validate result
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolTargetAuthoring] Write would produce invalid JSON, aborting.\n{ex.Message}");
                return;
            }

            // Step 6: Backup + write
            string backupDir = Path.Combine(Path.GetDirectoryName(jsonPath)!, ".pose_backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string ts         = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"machine_{ts}.json");
            File.Copy(jsonPath, backupPath, true);
            _lastBackupPath = backupPath;

            File.WriteAllText(jsonPath, json);
            AssetDatabase.Refresh();
            PackageSyncTool.Sync();
            Debug.Log($"[ToolTargetAuthoring] Written {_pkgId} (backup: {backupPath})");

            // Step 7: Reload and clear dirty flags
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildTargetList();
        }

        private void RevertFromBackup()
        {
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null || !File.Exists(_lastBackupPath)) return;
            File.Copy(_lastBackupPath, jsonPath, true);
            AssetDatabase.Refresh();
            Debug.Log($"[ToolTargetAuthoring] Reverted to backup: {_lastBackupPath}");
            _lastBackupPath = null;
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildTargetList();
        }

        // ── Extract from GLB anchors ──────────────────────────────────────────

        private void ExtractFromGlbAnchors()
        {
            if (string.IsNullOrEmpty(_pkgId) || _targets == null) return;

            string pkgFolder = $"{PackageJsonUtils.AuthoringRoot}/{_pkgId}";
            string[] guids   = AssetDatabase.FindAssets("t:GameObject", new[] { pkgFolder });
            int found = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string ext       = Path.GetExtension(assetPath);
                if (!ext.Equals(".glb",  StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".fbx",  StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;

                for (int i = 0; i < _targets.Length; i++)
                {
                    Transform node = FindNode(go.transform, _targets[i].def.id);
                    if (node == null) continue;

                    BeginEdit();
                    _targets[i].position     = node.localPosition;
                    _targets[i].rotation     = node.localRotation;
                    _targets[i].scale        = node.localScale;
                    _targets[i].isDirty      = true;
                    _targets[i].hasPlacement = true;
                    EndEdit();
                    found++;
                }
            }

            if (found > 0) Debug.Log($"[ToolTargetAuthoring] Extracted {found} target(s) from GLB anchors.");
            else           Debug.Log("[ToolTargetAuthoring] No named anchor nodes matched any targetId.");

            SceneView.RepaintAll();
            Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Depth-first node search by exact name. Source: PackageAssetPostprocessor.</summary>
        private static Transform FindNode(Transform t, string name)
        {
            if (t.name.Equals(name, StringComparison.Ordinal)) return t;
            foreach (Transform child in t)
            {
                var found = FindNode(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static float R(float v) => Mathf.Round(v * 100000f) / 100000f;

        // ── JSON injection helpers (Source: ToolPoseGizmoEditor — candidate for extraction to PackageJsonUtils) ──

        private static bool TryInjectBlock(ref string json, string id, string block, string blockJson)
        {
            blockJson = PackageJsonUtils.RoundFloatsInJson(blockJson);
            string fullPattern = $"\"id\": \"{id}\"";
            int idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            if (idPos < 0)
            {
                fullPattern = $"\"id\":\"{id}\"";
                idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            }
            if (idPos < 0)
            {
                string idNeedle  = "\"id\"";
                int searchFrom = 0;
                while (searchFrom < json.Length)
                {
                    int f = json.IndexOf(idNeedle, searchFrom, StringComparison.Ordinal);
                    if (f < 0) break;
                    int afterKey = f + idNeedle.Length;
                    int colonPos = SkipWhitespace(json, afterKey);
                    if (colonPos < json.Length && json[colonPos] == ':')
                    {
                        int valuePos = SkipWhitespace(json, colonPos + 1);
                        string idValue = $"\"{id}\"";
                        if (valuePos + idValue.Length <= json.Length
                            && json.Substring(valuePos, idValue.Length) == idValue)
                        { idPos = f; break; }
                    }
                    searchFrom = f + 1;
                }
            }
            if (idPos < 0) { Debug.LogWarning($"[ToolTargetAuthoring] TryInjectBlock: id='{id}' not found"); return false; }

            int objStart = FindObjectStart(json, idPos);
            if (objStart < 0) return false;
            int objEnd = FindMatchingClose(json, objStart);
            if (objEnd < 0) return false;

            string obj     = json.Substring(objStart, objEnd - objStart + 1);
            string cleaned = RemoveKey(obj, block);

            string indent  = "            ";
            int lineStart  = json.LastIndexOf('\n', idPos);
            if (lineStart >= 0)
            {
                int firstNonSpace = lineStart + 1;
                while (firstNonSpace < json.Length && json[firstNonSpace] == ' ') firstNonSpace++;
                indent = new string(' ', firstNonSpace - lineStart - 1);
            }

            int    lastBrace = cleaned.LastIndexOf('}');
            string before    = cleaned.Substring(0, lastBrace).TrimEnd();
            string after     = cleaned.Substring(lastBrace);
            string injected  = before + ",\n" + indent + $"\"{block}\": " + blockJson + "\n"
                             + indent.Substring(0, Math.Max(0, indent.Length - 4)) + after;

            json = json.Substring(0, objStart) + injected + json.Substring(objEnd + 1);
            return true;
        }

        private static int SkipWhitespace(string s, int from)
        {
            while (from < s.Length && char.IsWhiteSpace(s[from])) from++;
            return from;
        }

        private static int FindObjectStart(string json, int from)
        {
            for (int i = from - 1; i >= 0; i--)
            {
                char c = json[i];
                if (c == '{') return i;
                if (!char.IsWhiteSpace(c)) return -1;
            }
            return -1;
        }

        private static int FindMatchingClose(string json, int openPos)
        {
            char open  = json[openPos];
            char close = open == '{' ? '}' : ']';
            int depth  = 0; bool inStr = false;
            for (int i = openPos; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open)  depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string RemoveKey(string obj, string key)
        {
            string needle = $"\"{key}\"";
            int keyIdx = obj.IndexOf(needle, StringComparison.Ordinal);
            if (keyIdx < 0) return obj;

            int colon    = obj.IndexOf(':', keyIdx + needle.Length);
            if (colon < 0) return obj;
            int valStart = SkipWhitespace(obj, colon + 1);
            if (valStart >= obj.Length) return obj;

            int valEnd;
            char first = obj[valStart];
            if (first == '{' || first == '[')
            {
                valEnd = FindMatchingClose(obj, valStart);
                if (valEnd < 0) return obj;
            }
            else if (first == '"')
            {
                valEnd = valStart;
                for (int i = valStart + 1; i < obj.Length; i++)
                {
                    if (obj[i] == '\\') { i++; continue; }
                    if (obj[i] == '"') { valEnd = i; break; }
                }
            }
            else
            {
                valEnd = valStart;
                for (int i = valStart; i < obj.Length; i++)
                {
                    char c = obj[i];
                    if (c == ',' || c == '}' || c == ']') { valEnd = i - 1; break; }
                    valEnd = i;
                }
            }

            int removeStart = keyIdx;
            int removeEnd   = valEnd + 1;
            int ls = removeStart - 1;
            while (ls >= 0 && (obj[ls] == ' ' || obj[ls] == '\t' || obj[ls] == '\r' || obj[ls] == '\n')) ls--;
            if (ls >= 0 && obj[ls] == ',')
                removeStart = ls;
            else
            {
                int ts = removeEnd;
                while (ts < obj.Length && (obj[ts] == ' ' || obj[ts] == '\t')) ts++;
                if (ts < obj.Length && obj[ts] == ',') removeEnd = ts + 1;
            }
            while (removeEnd < obj.Length && (obj[removeEnd] == ' ' || obj[removeEnd] == '\r' || obj[removeEnd] == '\n'))
                removeEnd++;

            return obj.Substring(0, removeStart) + obj.Substring(removeEnd);
        }
    }
}
