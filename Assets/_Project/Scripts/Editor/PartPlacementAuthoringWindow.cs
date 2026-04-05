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
    /// Visual editor for <see cref="PartPreviewPlacement"/> transforms.
    ///
    /// Uses the parts already in the scene (spawned by PackagePartSpawner).
    /// This window spawns NO extra GOs — it only moves and reads the
    /// existing spawner GOs to author start/play poses.
    ///
    /// Workflow:
    ///   1. Navigate to a step.  The window moves each spawner GO to
    ///      the active pose (start or play, per the toggle) so the
    ///      scene reflects what you are editing.
    ///   2. Select a part in the list.  A position + rotation handle
    ///      appears on the live scene GO.
    ///   3. Drag the handle (or type in the fields) to reposition.
    ///   4. Click "Write to machine.json".
    ///
    /// Open via: OSE > Authoring > Part Placement Authoring
    /// </summary>
    public sealed class PartPlacementAuthoringWindow : EditorWindow
    {
        private const string MenuPath       = "OSE/Authoring/Part Placement Authoring";
        private const int    MaxUndoHistory = 50;

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color ColAuthored    = new(0f,   0.9f, 0.9f, 1f);
        private static readonly Color ColDirty       = new(1f,   0.6f, 0f,   1f);
        private static readonly Color ColNoPlacement = new(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color ColSelected    = new(1f,   1f,   1f,   1f);



        // ── Package state ─────────────────────────────────────────────────────
        private string[]                 _packageIds;
        [SerializeField] private int     _pkgIdx;
        [SerializeField] private string  _pkgId;
        private MachinePackageDefinition _pkg;

        // ── Step navigation ───────────────────────────────────────────────────
        private string[] _stepOptions;
        private string[] _stepIds;
        private int[]    _stepSequenceIdxs;
        [SerializeField] private int  _stepFilterIdx;
        private bool _suppressStepSync;
        private int  _lastPolledDriverStep = -1;

        // ── Part list ─────────────────────────────────────────────────────────
        private PartEditState[]         _parts;
        [SerializeField] private int    _selectedIdx = -1;
        [SerializeField] private string _selectedPartId;
        private readonly HashSet<int>   _multiSelected = new();

        // ── Pose toggle ───────────────────────────────────────────────────────
        /// <summary>false = edit startPose; true = edit playPose.</summary>
        [SerializeField] private bool _editPlayPose;

        // ── Undo / redo ───────────────────────────────────────────────────────
        private readonly List<(int idx, PartSnapshot snap)> _undoStack = new();
        private readonly List<(int idx, PartSnapshot snap)> _redoStack = new();
        private bool _snapshotPending;

        // ── Rotation drag state ───────────────────────────────────────────────
        private bool       _rotDragActive;
        private Quaternion _rotDragStartHandle;
        private Quaternion _rotDragStartLocal;
        private Dictionary<int, Quaternion> _rotDragStartMulti;

        // ── File backup ───────────────────────────────────────────────────────
        private string _lastBackupPath;




        // ── Scroll positions ──────────────────────────────────────────────────
        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        // ── Nested types ──────────────────────────────────────────────────────

        private struct PartEditState
        {
            public PartDefinition       def;
            public PartPreviewPlacement placement;
            public bool   hasPlacement;
            public bool   isDirty;
            public Vector3    startPosition;
            public Quaternion startRotation;
            public Vector3    startScale;
            public Vector3    playPosition;
            public Quaternion playRotation;
            public Vector3    playScale;
            public Color      color;
        }

        private struct PartSnapshot
        {
            public Vector3 startPosition; public Quaternion startRotation; public Vector3 startScale;
            public Vector3 playPosition;  public Quaternion playRotation;  public Vector3 playScale;
        }

        // ── MenuItem ──────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var w = GetWindow<PartPlacementAuthoringWindow>("Part Placement Authoring");
            w.minSize = new Vector2(400, 520);
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshPackageList();
            if (_pkg == null && !string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId, restoring: true);
            else if (_pkg == null && _packageIds != null && _packageIds.Length > 0
                && _pkgIdx >= 0 && _pkgIdx < _packageIds.Length)
                LoadPkg(_packageIds[_pkgIdx]);

            SceneView.duringSceneGui += OnSceneGUI;
            SessionDriver.EditModeStepChanged += OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SessionDriver.EditModeStepChanged -= OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(_pkgId)) LoadPkg(_pkgId);
        }

        private void OnSessionDriverStepChanged(int sequenceIndex)
        {
            if (_suppressStepSync || _stepSequenceIdxs == null) return;
            int idx = StepAuthoringUtils.FindStepFilterIdx(_stepSequenceIdxs, sequenceIndex);
            if (idx < 0 || idx == _stepFilterIdx) return;
            _suppressStepSync = true;
            ApplyStepFilter(idx);
            _suppressStepSync = false;
        }

        // ── Main GUI ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_pkg == null && !string.IsNullOrEmpty(_pkgId))
            {
                LoadPkg(_pkgId, restoring: true);
                if (_pkg == null) _pkgId = null;
            }

            EditorGUILayout.Space(4);
            DrawPkgPicker();
            if (_pkg == null) return;

            DrawStepFilter();
            DrawPoseToggle();
            DrawSpawnerStatus();
            EditorGUILayout.Space(2);

            float listH = Mathf.Clamp(position.height * 0.35f, 80f, 200f);
            DrawPartList(listH);
            EditorGUILayout.Space(4);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            if (_multiSelected.Count > 1)
                DrawBatchPanel();
            else if (_selectedIdx >= 0 && _parts != null && _selectedIdx < _parts.Length)
                DrawDetailPanel(ref _parts[_selectedIdx]);
            else
                EditorGUILayout.HelpBox(
                    "Select a part in the list above.\nCtrl+click or Shift+click to multi-select.",
                    MessageType.Info);
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

        // ── Spawner status ────────────────────────────────────────────────────

        private void DrawSpawnerStatus()
        {
            var spawner = GetSpawner();
            if (spawner == null)
            {
                EditorGUILayout.HelpBox(
                    "PackagePartSpawner not found in scene.\n" +
                    "Parts cannot be moved until the scene hierarchy is restored.",
                    MessageType.Warning);
            }
            else if (spawner.SpawnedParts == null || spawner.SpawnedParts.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Spawner found but no parts are loaded yet.\n" +
                    "Make sure a package is loaded via SessionDriver.",
                    MessageType.Info);
            }
        }

        // ── Step filter ───────────────────────────────────────────────────────

        private void DrawStepFilter()
        {
            if (_stepOptions == null || _stepOptions.Length == 0) return;

            if (!_suppressStepSync && _stepSequenceIdxs != null)
            {
                var driver    = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
                int driverSeq = driver != null ? driver.PreviewStepSequenceIndex : -1;
                if (driverSeq != _lastPolledDriverStep)
                {
                    _lastPolledDriverStep = driverSeq;
                    int matchIdx = StepAuthoringUtils.FindStepFilterIdx(_stepSequenceIdxs, driverSeq);
                    if (matchIdx >= 0 && matchIdx != _stepFilterIdx)
                    {
                        _suppressStepSync = true;
                        ApplyStepFilter(matchIdx);
                        _suppressStepSync = false;
                    }
                }
            }

            int stepCount = _stepOptions.Length - 1;
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_stepFilterIdx <= 1);
            if (GUILayout.Button("◄", GUILayout.Width(28))) ApplyStepFilter(_stepFilterIdx - 1);
            EditorGUI.EndDisabledGroup();

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
            if (GUILayout.Button("►", GUILayout.Width(28))) ApplyStepFilter(_stepFilterIdx + 1);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var step = FindStep(_stepIds[_stepFilterIdx]);
                if (step != null)
                {
                    int reqCount = step.requiredPartIds?.Length ?? 0;
                    int optCount = step.optionalPartIds?.Length ?? 0;
                    string family = string.IsNullOrEmpty(step.family)
                        ? (string.IsNullOrEmpty(step.profile) ? "" : step.profile) : step.family;
                    string countStr = $"{reqCount} required part{(reqCount == 1 ? "" : "s")}";
                    if (optCount > 0) countStr += $" + {optCount} optional";
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"[{step.sequenceIndex}] {step.name}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"{(string.IsNullOrEmpty(family) ? "" : family + "  ·  ")}{countStr}",
                        EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void ApplyStepFilter(int newIdx)
        {
            _stepFilterIdx = newIdx;
            _selectedIdx   = -1;
            _multiSelected.Clear();
            BuildPartList();
            SyncAllLiveGOsToActivePose();
            if (!_suppressStepSync) SyncSessionDriverStep();
            SceneView.RepaintAll();
            Repaint();
        }

        private void SyncSessionDriverStep()
        {
            if (_pkg == null) return;
            var driver = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
            if (driver == null) return;
            if (_stepFilterIdx <= 0 || _stepSequenceIdxs == null
                || _stepFilterIdx >= _stepSequenceIdxs.Length) return;
            int seq = _stepSequenceIdxs[_stepFilterIdx];
            _suppressStepSync     = true;
            _lastPolledDriverStep = seq;
            driver.SetEditModeStep(seq);
            _suppressStepSync = false;
        }

        // ── Pose toggle ───────────────────────────────────────────────────────

        private void DrawPoseToggle()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Edit Pose");
            // Read both toggles BEFORE mutating state to avoid same-frame side effects.
            bool wantStart = GUILayout.Toggle(!_editPlayPose, "Start Pose", EditorStyles.miniButtonLeft);
            bool wantPlay  = GUILayout.Toggle( _editPlayPose, "Play Pose",  EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();

            // "clickedStart" = Start toggle became true while we were on Play.
            // "clickedPlay"  = Play toggle became true while we were on Start.
            bool clickedStart = wantStart &&  _editPlayPose;
            bool clickedPlay  = wantPlay  && !_editPlayPose;

            if (clickedStart)
            {
                _editPlayPose = false;
                SyncAllLiveGOsToActivePose();
                SceneView.RepaintAll();
            }
            else if (clickedPlay)
            {
                _editPlayPose = true;
                SyncAllLiveGOsToActivePose();
                SceneView.RepaintAll();
            }
        }

        // ── Part list ─────────────────────────────────────────────────────────

        private void DrawPartList(float listHeight)
        {
            if (_parts == null || _parts.Length == 0)
            {
                EditorGUILayout.HelpBox("No parts for this step.", MessageType.Info);
                return;
            }

            string listHeader = _multiSelected.Count > 1
                ? $"Parts ({_parts.Length})  —  {_multiSelected.Count} selected  (Ctrl / Shift)"
                : $"Parts ({_parts.Length})  —  Ctrl+click or Shift+click to multi-select";
            EditorGUILayout.LabelField(listHeader, EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(listHeight));

            var selBg   = new Color(0.25f, 0.50f, 0.90f, 0.35f);
            var multiBg = new Color(0.25f, 0.50f, 0.90f, 0.18f);

            for (int i = 0; i < _parts.Length; i++)
            {
                ref PartEditState p = ref _parts[i];
                Color col = p.isDirty ? ColDirty : p.hasPlacement ? ColAuthored : ColNoPlacement;
                bool isPrimary  = i == _selectedIdx;
                bool isInMulti  = _multiSelected.Count > 1 && _multiSelected.Contains(i);
                bool isSelected = isPrimary || isInMulti;

                if (isSelected)
                {
                    Rect rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(0));
                    rowRect.height = EditorGUIUtility.singleLineHeight + 2f;
                    rowRect.y -= 1f;
                    EditorGUI.DrawRect(rowRect, isPrimary ? selBg : multiBg);
                }

                var style = new GUIStyle(isSelected ? EditorStyles.boldLabel : EditorStyles.label)
                    { normal = { textColor = col }, focused = { textColor = col } };

                string badge = p.isDirty ? " ●" : p.hasPlacement ? "" : " ○";
                string check = isInMulti ? "✓ " : "  ";
                if (GUILayout.Button($"{check}{p.def.id}{badge}", style, GUILayout.ExpandWidth(true)))
                {
                    bool ctrl  = (Event.current.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
                    bool shift = (Event.current.modifiers & EventModifiers.Shift) != 0;
                    if (ctrl)
                    {
                        if (_multiSelected.Count == 0 && _selectedIdx >= 0) _multiSelected.Add(_selectedIdx);
                        if (_multiSelected.Contains(i)) _multiSelected.Remove(i);
                        else _multiSelected.Add(i);
                        if (_multiSelected.Count == 1)
                        {
                            foreach (int x in _multiSelected) { _selectedIdx = x; break; }
                            _multiSelected.Clear();
                        }
                    }
                    else if (shift && _selectedIdx >= 0)
                    {
                        int lo = Mathf.Min(_selectedIdx, i), hi = Mathf.Max(_selectedIdx, i);
                        _multiSelected.Clear();
                        for (int k = lo; k <= hi; k++) _multiSelected.Add(k);
                    }
                    else
                    {
                        _selectedIdx     = i;
                        _selectedPartId  = p.def.id;
                        _multiSelected.Clear();
                        _snapshotPending = false;
                        // Select the live GO so it highlights in scene
                        var liveGO = FindLivePartGO(p.def.id);
                        if (liveGO != null) Selection.activeGameObject = liveGO;
                    }
                    Repaint();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Detail panel ──────────────────────────────────────────────────────

        private void DrawDetailPanel(ref PartEditState p)
        {
            EditorGUILayout.LabelField(p.def.id, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_editPlayPose)
            {
                EditorGUILayout.LabelField("Play Pose", EditorStyles.boldLabel);
                DrawPoseFields(ref p, play: true);
                EditorGUILayout.Space(4);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("Start Pose (read-only)", EditorStyles.miniLabel);
                DrawPoseFields(ref p, play: false);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUILayout.LabelField("Start Pose", EditorStyles.boldLabel);
                DrawPoseFields(ref p, play: false);
                EditorGUILayout.Space(4);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("Play Pose (read-only)", EditorStyles.miniLabel);
                DrawPoseFields(ref p, play: true);
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField("Color", p.color);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                p.color   = newColor;
                p.isDirty = true;
                EndEdit();
                Repaint();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_undoStack.Count == 0);
            if (GUILayout.Button("Undo", EditorStyles.miniButton)) UndoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redoStack.Count == 0);
            if (GUILayout.Button("Redo", EditorStyles.miniButton)) RedoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPoseFields(ref PartEditState p, bool play)
        {
            Vector3    pos = play ? p.playPosition : p.startPosition;
            Quaternion rot = play ? p.playRotation  : p.startRotation;
            Vector3    scl = play ? p.playScale     : p.startScale;

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = EditorGUILayout.Vector3Field("Position", pos);
            Vector3 newEul = EditorGUILayout.Vector3Field("Rotation", rot.eulerAngles);
            Vector3 newScl = EditorGUILayout.Vector3Field("Scale",    scl);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                Quaternion newRot = Quaternion.Euler(newEul);
                if (play) { p.playPosition = newPos; p.playRotation = newRot; p.playScale = newScl; }
                else      { p.startPosition = newPos; p.startRotation = newRot; p.startScale = newScl; }
                p.isDirty = true;
                EndEdit();
                SyncVisuals(ref p);
                Repaint();
                SceneView.RepaintAll();
            }
        }

        // ── Batch panel ───────────────────────────────────────────────────────

        private void DrawBatchPanel()
        {
            EditorGUILayout.LabelField($"Batch Edit — {_multiSelected.Count} parts selected", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Applies as a delta to ALL selected parts' active pose.", MessageType.None);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(_editPlayPose ? "Play Position Offset" : "Start Position Offset", EditorStyles.miniLabel);

            foreach (var axis in new[] { ("Δ X", new Vector3(1,0,0)), ("Δ Y", new Vector3(0,1,0)), ("Δ Z", new Vector3(0,0,1)) })
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(axis.Item1);
                EditorGUI.BeginChangeCheck();
                float v = EditorGUILayout.FloatField(0f);
                if (EditorGUI.EndChangeCheck()) ApplyBatchPositionDelta(axis.Item2 * v);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            float s = EditorGUILayout.FloatField("Uniform Scale", 1f);
            if (EditorGUI.EndChangeCheck() && s > 0.0001f) ApplyBatchScale(Vector3.one * s);
        }

        private void ApplyBatchPositionDelta(Vector3 delta)
        {
            if (_parts == null || delta.sqrMagnitude < 1e-8f) return;
            BeginEdit();
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _parts.Length) continue;
                ref PartEditState p = ref _parts[idx];
                if (_editPlayPose) p.playPosition  += delta;
                else               p.startPosition += delta;
                p.isDirty = true;
                SyncVisuals(ref p);
            }
            EndEdit();
            SceneView.RepaintAll();
            Repaint();
        }

        private void ApplyBatchScale(Vector3 scale)
        {
            if (_parts == null) return;
            BeginEdit();
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _parts.Length) continue;
                ref PartEditState p = ref _parts[idx];
                if (_editPlayPose) p.playScale  = scale;
                else               p.startScale = scale;
                p.isDirty = true;
                SyncVisuals(ref p);
            }
            EndEdit();
            SceneView.RepaintAll();
            Repaint();
        }

        // ── Actions bar ───────────────────────────────────────────────────────

        private void DrawActions()
        {
            bool anyDirty = AnyDirty();
            EditorGUI.BeginDisabledGroup(!anyDirty);
            GUI.backgroundColor = anyDirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (GUILayout.Button("Write to machine.json", GUILayout.Height(28))) WriteJson();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write (restore backup)")) RevertFromBackup();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Frame in SceneView")) FrameInScene();
        }

        // ── SceneView ─────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            if (_parts == null || _parts.Length == 0) return;

            var spawner = GetSpawner();
            Transform root = spawner?.PreviewRoot;

            if (Event.current.type == EventType.MouseUp) EndEdit();

            // F key
            if (_selectedIdx >= 0 && _selectedIdx < _parts.Length
                && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F)
            {
                FrameInScene();
                Event.current.Use();
            }

            // ── Sync scene selection → window selection ──────────────────────
            if (spawner?.SpawnedParts != null)
            {
                var activeGO = Selection.activeGameObject;
                if (activeGO != null)
                {
                    foreach (var liveGO in spawner.SpawnedParts)
                    {
                        if (liveGO == null) continue;
                        if (activeGO != liveGO && !activeGO.transform.IsChildOf(liveGO.transform)) continue;
                        for (int si = 0; si < _parts.Length; si++)
                        {
                            if (_parts[si].def.id != liveGO.name) continue;
                            if (_selectedIdx != si)
                            {
                                _selectedIdx    = si;
                                _selectedPartId = liveGO.name;
                                _multiSelected.Clear();
                                Repaint();
                            }
                            break;
                        }
                        break;
                    }
                }
            }

            // ── Indicator dots (non-interactive, show active pose position) ──
            if (root != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    ref PartEditState p = ref _parts[i];
                    // Use the live GO's actual world position so dot always matches the mesh
                    var liveGO  = FindLivePartGO(p.def.id);
                    Vector3 worldPos = liveGO != null
                        ? liveGO.transform.position
                        : root.TransformPoint(_editPlayPose ? p.playPosition : p.startPosition);
                    float size = HandleUtility.GetHandleSize(worldPos) * 0.07f;

                    Color col = i == _selectedIdx ? ColSelected
                              : p.isDirty         ? ColDirty
                              : p.hasPlacement     ? ColAuthored : ColNoPlacement;
                    Handles.color = col;
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }
            }

            // ── Poll selected GO for moves made with Unity's native Move tool ──
            // Fires on Repaint/Layout so it catches every frame the user drags.
            if (_selectedIdx >= 0 && _selectedIdx < _parts.Length &&
                (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout))
            {
                ref PartEditState pp = ref _parts[_selectedIdx];
                var pollGO = FindLivePartGO(pp.def.id);
                if (pollGO != null)
                {
                    Vector3    goPos    = pollGO.transform.localPosition;
                    Quaternion goRot    = pollGO.transform.localRotation;
                    Vector3    statePos = _editPlayPose ? pp.playPosition : pp.startPosition;
                    Quaternion stateRot = _editPlayPose ? pp.playRotation  : pp.startRotation;

                    bool posChg = (goPos - statePos).sqrMagnitude > 1e-8f;
                    bool rotChg = Quaternion.Angle(goRot, stateRot) > 0.005f;
                    if (posChg || rotChg)
                    {
                        if (!_snapshotPending)
                        {
                            _undoStack.Add((_selectedIdx, CaptureSnapshot(ref pp)));
                            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
                            _redoStack.Clear();
                            _snapshotPending = true;
                        }
                        if (_editPlayPose) { pp.playPosition = goPos; pp.playRotation = goRot; }
                        else               { pp.startPosition = goPos; pp.startRotation = goRot; }
                        pp.isDirty = true;
                        Repaint();
                    }
                }
            }

            // ── Position + Rotation handle for selected part ─────────────────
            if (_selectedIdx < 0 || _selectedIdx >= _parts.Length || root == null) return;

            ref PartEditState sel = ref _parts[_selectedIdx];
            var selectedGO = FindLivePartGO(sel.def.id);
            if (selectedGO == null) return;

            Vector3    worldPos3 = selectedGO.transform.position;
            Quaternion worldRot3 = selectedGO.transform.rotation;

            // ── Position handle ──────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Quaternion posHandleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot3 : Quaternion.identity;
            Vector3    newWorldPos  = Handles.PositionHandle(worldPos3, posHandleRot);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit(_selectedIdx);
                selectedGO.transform.position = newWorldPos;
                Vector3 newLocalPos = selectedGO.transform.localPosition;
                Vector3 delta       = newLocalPos - (_editPlayPose ? sel.playPosition : sel.startPosition);

                if (_editPlayPose) sel.playPosition  = newLocalPos;
                else               sel.startPosition = newLocalPos;
                sel.isDirty = true;

                if (_multiSelected.Count > 1)
                    foreach (int idx in _multiSelected)
                    {
                        if (idx == _selectedIdx || idx < 0 || idx >= _parts.Length) continue;
                        ref PartEditState pt = ref _parts[idx];
                        if (_editPlayPose) pt.playPosition  += delta;
                        else               pt.startPosition += delta;
                        pt.isDirty = true;
                        var otherGO = FindLivePartGO(pt.def.id);
                        if (otherGO != null) otherGO.transform.localPosition += delta;
                    }

                Repaint();
            }

            // ── Rotation handle ─────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            Quaternion rotOrientation = Tools.pivotRotation == PivotRotation.Local ? worldRot3 : Quaternion.identity;
            Quaternion newWorldRot    = Handles.RotationHandle(rotOrientation, worldPos3);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit(_selectedIdx);
                if (!_rotDragActive)
                {
                    _rotDragActive      = true;
                    _rotDragStartHandle = rotOrientation;
                    _rotDragStartLocal  = _editPlayPose ? sel.playRotation : sel.startRotation;
                    _rotDragStartMulti  = new Dictionary<int, Quaternion>();
                    if (_multiSelected.Count > 1)
                        foreach (int idx in _multiSelected)
                            if (idx != _selectedIdx)
                                _rotDragStartMulti[idx] = _editPlayPose
                                    ? _parts[idx].playRotation : _parts[idx].startRotation;
                }

                Quaternion rootRot    = root.rotation;
                Quaternion worldDelta = newWorldRot * Quaternion.Inverse(_rotDragStartHandle);
                Quaternion newLocalRot = Quaternion.Inverse(rootRot)
                    * (worldDelta * (rootRot * _rotDragStartLocal));
                Quaternion localDelta = newLocalRot * Quaternion.Inverse(_rotDragStartLocal);

                selectedGO.transform.localRotation = newLocalRot;
                if (_editPlayPose) sel.playRotation  = newLocalRot;
                else               sel.startRotation = newLocalRot;
                sel.isDirty = true;

                if (_multiSelected.Count > 1)
                    foreach (int idx in _multiSelected)
                    {
                        if (idx == _selectedIdx || idx < 0 || idx >= _parts.Length) continue;
                        ref PartEditState pt = ref _parts[idx];
                        Quaternion startR = _rotDragStartMulti.TryGetValue(idx, out var sr) ? sr
                            : (_editPlayPose ? pt.playRotation : pt.startRotation);
                        Quaternion newR = localDelta * startR;
                        if (_editPlayPose) pt.playRotation  = newR;
                        else               pt.startRotation = newR;
                        pt.isDirty = true;
                        var otherGO = FindLivePartGO(pt.def.id);
                        if (otherGO != null) otherGO.transform.localRotation = newR;
                    }

                Repaint();
            }
            else if (_rotDragActive) _rotDragActive = false;
        }

        // ── Service access ────────────────────────────────────────────────────

        private ISpawnerQueryService GetSpawner()
            => ServiceRegistry.TryGet<ISpawnerQueryService>(out var s) ? s : null;

        private GameObject FindLivePartGO(string partId)
        {
            var spawner = GetSpawner();
            if (spawner?.SpawnedParts == null) return null;
            foreach (var go in spawner.SpawnedParts)
                if (go != null && go.name == partId) return go;
            return null;
        }

        // ── Package loading ───────────────────────────────────────────────────

        private void RefreshPackageList() => _packageIds = StepAuthoringUtils.DiscoverPackageIds();

        private void LoadPkg(string id) => LoadPkg(id, restoring: false);

        private void LoadPkg(string id, bool restoring)
        {
            _parts       = null;
            _selectedIdx = -1;
            _multiSelected.Clear();

            _pkg   = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            if (_pkg == null) return;

            if (!restoring) _stepFilterIdx = 0;

            StepAuthoringUtils.BuildStepOptions(_pkg,
                out _stepOptions, out _stepIds, out _stepSequenceIdxs);

            if (!restoring)
            {
                var driver = UnityEngine.Object.FindFirstObjectByType<SessionDriver>();
                if (driver != null && _stepSequenceIdxs != null)
                {
                    int matchIdx = StepAuthoringUtils.FindStepFilterIdx(
                        _stepSequenceIdxs, driver.PreviewStepSequenceIndex);
                    if (matchIdx >= 0) _stepFilterIdx = matchIdx;
                }
            }

            if (_stepOptions != null && _stepFilterIdx >= _stepOptions.Length) _stepFilterIdx = 0;
            BuildPartList();
            SyncAllLiveGOsToActivePose();
        }

        // ── Part list building ────────────────────────────────────────────────

        private void BuildPartList()
        {
            if (_pkg?.parts == null) { _parts = Array.Empty<PartEditState>(); return; }

            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                && _stepIds[_stepFilterIdx] != null)
            {
                var step = FindStep(_stepIds[_stepFilterIdx]);
                if (step?.requiredPartIds != null)
                    filterIds = new HashSet<string>(step.requiredPartIds, StringComparer.Ordinal);
            }

            var list = new List<PartEditState>();
            foreach (var def in _pkg.parts)
            {
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

                PartPreviewPlacement placement = FindPartPlacement(def.id);
                bool hasP    = placement != null;
                Vector3 defScl = Vector3.one;

                var state = new PartEditState
                {
                    def           = def,
                    placement     = placement,
                    hasPlacement  = hasP,
                    isDirty       = false,
                    startPosition = hasP ? PackageJsonUtils.ToVector3(placement.startPosition) : Vector3.zero,
                    startRotation = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.startRotation) : Quaternion.identity,
                    startScale    = hasP && placement.startScale.x != 0
                                  ? PackageJsonUtils.ToVector3(placement.startScale) : defScl,
                    playPosition  = hasP ? PackageJsonUtils.ToVector3(placement.playPosition) : Vector3.zero,
                    playRotation  = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.playRotation) : Quaternion.identity,
                    playScale     = hasP ? PackageJsonUtils.ToVector3(placement.playScale) : defScl,
                    color         = hasP
                                  ? new Color(placement.color.r, placement.color.g, placement.color.b, placement.color.a)
                                  : new Color(0.7f, 0.7f, 0.7f, 1f),
                };
                list.Add(state);
            }

            string prevId = (_selectedIdx >= 0 && _parts != null && _selectedIdx < _parts.Length)
                ? _parts[_selectedIdx].def.id : _selectedPartId;
            _parts       = list.ToArray();
            _selectedIdx = -1;
            if (prevId != null)
                for (int i = 0; i < _parts.Length; i++)
                    if (_parts[i].def.id == prevId) { _selectedIdx = i; break; }
            if (_selectedIdx < 0 && _parts.Length > 0) _selectedIdx = 0;
            _selectedPartId = _selectedIdx >= 0 ? _parts[_selectedIdx].def.id : null;
            _multiSelected.Clear();
        }

        private PartPreviewPlacement FindPartPlacement(string partId)
        {
            var arr = _pkg?.previewConfig?.partPlacements;
            if (arr == null) return null;
            foreach (var p in arr) if (p != null && p.partId == partId) return p;
            return null;
        }

        private StepDefinition FindStep(string stepId)
        {
            if (_pkg?.steps == null) return null;
            foreach (var s in _pkg.steps) if (s != null && s.id == stepId) return s;
            return null;
        }

        // ── Live GO sync (moves spawner GOs to active pose) ───────────────────

        /// <summary>
        /// Moves every live spawner GO to the active pose so the scene matches
        /// what we are editing.  Called on load, step change, and pose toggle.
        /// </summary>
        private void SyncAllLiveGOsToActivePose()
        {
            if (_parts == null) return;
            foreach (ref PartEditState p in _parts.AsSpan())
            {
                if (!p.hasPlacement) continue;
                var liveGO = FindLivePartGO(p.def.id);
                if (liveGO == null) continue;
                Vector3    pos = _editPlayPose ? p.playPosition  : p.startPosition;
                Quaternion rot = _editPlayPose ? p.playRotation   : p.startRotation;
                Vector3    scl = _editPlayPose ? p.playScale      : p.startScale;
                liveGO.transform.localPosition = pos;
                liveGO.transform.localRotation = rot;
                if (scl.sqrMagnitude > 0.00001f) liveGO.transform.localScale = scl;
            }
        }

        /// <summary>Moves the live spawner GO to the active pose. State is source of truth.</summary>
        private void SyncVisuals(ref PartEditState p)
        {
            var liveGO = FindLivePartGO(p.def.id);
            if (liveGO == null) return;
            Vector3    pos = _editPlayPose ? p.playPosition  : p.startPosition;
            Quaternion rot = _editPlayPose ? p.playRotation   : p.startRotation;
            Vector3    scl = _editPlayPose ? p.playScale      : p.startScale;
            liveGO.transform.localPosition = pos;
            liveGO.transform.localRotation = rot;
            if (scl.sqrMagnitude > 0.00001f) liveGO.transform.localScale = scl;
        }

        private void FrameInScene()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            if (_selectedIdx >= 0 && _selectedIdx < _parts.Length)
            {
                var liveGO = FindLivePartGO(_parts[_selectedIdx].def.id);
                if (liveGO != null)
                {
                    Selection.activeGameObject = liveGO;
                    sv.FrameSelected();
                    return;
                }
            }
            sv.Repaint();
        }

        // ── Undo / redo ───────────────────────────────────────────────────────

        private PartSnapshot CaptureSnapshot(ref PartEditState p) => new()
        {
            startPosition = p.startPosition, startRotation = p.startRotation, startScale = p.startScale,
            playPosition  = p.playPosition,  playRotation  = p.playRotation,  playScale  = p.playScale,
        };

        private void BeginEdit(int forIdx = -1)
        {
            if (_snapshotPending || _parts == null) return;
            int idx = forIdx >= 0 ? forIdx : _selectedIdx;
            if (idx < 0 || idx >= _parts.Length) return;
            _undoStack.Add((idx, CaptureSnapshot(ref _parts[idx])));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _snapshotPending = true;
        }

        private void EndEdit() => _snapshotPending = false;

        private void UndoPose()
        {
            if (_undoStack.Count == 0 || _parts == null) return;
            var (idx, prev) = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (idx < _parts.Length) { _redoStack.Add((idx, CaptureSnapshot(ref _parts[idx]))); ApplySnapshot(idx, prev); }
        }

        private void RedoPose()
        {
            if (_redoStack.Count == 0 || _parts == null) return;
            var (idx, next) = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            if (idx < _parts.Length) { _undoStack.Add((idx, CaptureSnapshot(ref _parts[idx]))); ApplySnapshot(idx, next); }
        }

        private void ApplySnapshot(int idx, PartSnapshot s)
        {
            ref PartEditState p = ref _parts[idx];
            p.startPosition = s.startPosition; p.startRotation = s.startRotation; p.startScale = s.startScale;
            p.playPosition  = s.playPosition;  p.playRotation  = s.playRotation;  p.playScale  = s.playScale;
            p.isDirty       = true;
            _snapshotPending = false;
            SyncVisuals(ref p);
            Repaint();
            SceneView.RepaintAll();
        }

        // ── JSON write ────────────────────────────────────────────────────────

        private bool AnyDirty()
        {
            if (_parts == null) return false;
            foreach (var p in _parts) if (p.isDirty) return true;
            return false;
        }

        private void WriteJson()
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null || _parts == null) return;
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[PartPlacementAuthoring] machine.json not found for '{_pkgId}'"); return; }

            if (_pkg.previewConfig == null) _pkg.previewConfig = new PackagePreviewConfig();
            var placements = _pkg.previewConfig.partPlacements != null
                ? new List<PartPreviewPlacement>(_pkg.previewConfig.partPlacements)
                : new List<PartPreviewPlacement>();

            foreach (ref PartEditState p in _parts.AsSpan())
            {
                if (!p.isDirty) continue;
                string pid = p.def.id;
                int    idx = placements.FindIndex(pp => pp != null && pp.partId == pid);
                PartPreviewPlacement entry = idx >= 0 ? placements[idx] : new PartPreviewPlacement { partId = pid };

                entry.startPosition = PackageJsonUtils.ToFloat3(p.startPosition);
                entry.startRotation = PackageJsonUtils.ToQuaternion(p.startRotation);
                entry.startScale    = PackageJsonUtils.ToFloat3(p.startScale);
                entry.playPosition  = PackageJsonUtils.ToFloat3(p.playPosition);
                entry.playRotation  = PackageJsonUtils.ToQuaternion(p.playRotation);
                entry.playScale     = PackageJsonUtils.ToFloat3(p.playScale);
                entry.color         = new SceneFloat4 { r = p.color.r, g = p.color.g, b = p.color.b, a = p.color.a };

                if (idx >= 0) placements[idx] = entry;
                else          placements.Add(entry);
            }
            _pkg.previewConfig.partPlacements = placements.ToArray();

            // Validate original before touching anything
            string originalJson = File.ReadAllText(jsonPath);
            try { JsonUtility.FromJson<MachinePackageDefinition>(originalJson); }
            catch (Exception ex) { Debug.LogError($"[PartPlacementAuthoring] machine.json already invalid.\n{ex.Message}"); return; }

            // Backup original BEFORE write
            string backupDir  = Path.Combine(Path.GetDirectoryName(jsonPath)!, ".pose_backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string ts         = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"machine_{ts}.json");
            File.Copy(jsonPath, backupPath, true);
            _lastBackupPath = backupPath;

            PackageJsonUtils.WritePreviewConfig(jsonPath, _pkg.previewConfig);
            string json = File.ReadAllText(jsonPath);

            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (Exception ex)
            {
                File.Copy(backupPath, jsonPath, true);
                Debug.LogError($"[PartPlacementAuthoring] Write produced invalid JSON — restored backup.\n{ex.Message}");
                return;
            }

            AssetDatabase.Refresh();
            PackageSyncTool.Sync();
            Debug.Log($"[PartPlacementAuthoring] Written {_pkgId} (backup: {backupPath})");

            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildPartList();
            SyncAllLiveGOsToActivePose();
        }

        private void RevertFromBackup()
        {
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null || !File.Exists(_lastBackupPath)) return;
            File.Copy(_lastBackupPath, jsonPath, true);
            AssetDatabase.Refresh();
            Debug.Log($"[PartPlacementAuthoring] Reverted to backup: {_lastBackupPath}");
            _lastBackupPath = null;
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildPartList();
            SyncAllLiveGOsToActivePose();
        }
    }
}
