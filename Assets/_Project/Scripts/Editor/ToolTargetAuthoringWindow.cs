using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime.Preview;
using OSE.UI.Root;
using UnityEditor;
using UnityEditorInternal;
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
        private const string MenuPath = "OSE/Authoring/Assembly Step Authoring";
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
        [SerializeField] private int        _pkgIdx;
        [SerializeField] private string     _pkgId;
        private MachinePackageDefinition _pkg;

        private string[]   _stepOptions;        // "(All Steps)", then "[seq] name · tool · profile"
        private string[]   _stepIds;            // null at index 0, then actual step ids
        private int[]      _stepSequenceIdxs;   // 0 at index 0, then step.sequenceIndex
        [SerializeField] private int        _stepFilterIdx;
        private bool       _suppressStepSync;   // prevent circular sync with SessionDriver
        private int        _lastPolledDriverStep = -1; // last SessionDriver step seen during poll
        private Rect       _stepNumRect;        // cached rect of the step-number field for scroll/drag detection
        private bool       _stepDragging;           // true while mouse-dragging the step number field
        private float      _stepDragAccum;          // sub-step drag accumulator
        private int        _stepDragStartVal = -1;  // step index at drag start (-1 = not dragging)
        private double     _lastDriverSyncTime;     // EditorApplication.timeSinceStartup at last SyncSessionDriverStep

        // Cached step-scene context, written by RespawnScene, read by SyncAllPartMeshesToActivePose.
        // Allows sync to apply the same past/current/future logic as RespawnScene without rebuilding.
        private bool                    _sceneBuildStepActive;
        private int                     _sceneBuildCurrentSeq;
        private Dictionary<string, int> _sceneBuildPartStepSeq        = new();
        private HashSet<string>         _sceneBuildCurrentSubassembly = new();

        // Measured height of top content (PkgPicker + StepFilter) — updated each Repaint
        // so DrawUnifiedList gets exactly the right height without overlapping the bottom bar.
        private float           _topContentHeight = 90f;

        // Active-step context (null = All Steps)
        private string          _activeStepProfile;
        private bool            _activeStepIsConnect; // true for ANY Connect-family step (covers legacy completionType)
        private HashSet<string> _activeStepTargetIds;

        // Active-task context (null = no task selected)
        private string          _activeTaskKind; // kind of the currently selected task entry

        // Serialized for domain-reload restoration (target ID survives list rebuild)
        [SerializeField] private string _selectedTargetId;

        // targetId → display name of the tool that acts on it (from requiredToolActions)
        private Dictionary<string, string> _targetToolMap;
        // targetId → toolId (raw id, for looking up ToolDefinition.persistent)
        private Dictionary<string, string> _targetToolIdMap;
        private HashSet<string> _toolActionTargetIds;
        // Dirty tracking for tool/step fields written outside the target placement flow
        private readonly HashSet<string> _dirtyToolIds         = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _dirtyStepIds         = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _dirtyPartAssetRefIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly PackageAssetResolver _assetResolver = new PackageAssetResolver();

        // SceneView part-count summary updated by RespawnScene
        private int _previewAssembled;
        private int _previewCurrent;
        private int _previewHidden;


        private TargetEditState[] _targets;
        [SerializeField] private int _selectedIdx = -1;
        private readonly HashSet<int> _multiSelected = new HashSet<int>();

        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        private bool _clickToSnapActive;

        // Undo/redo
        private readonly List<(int idx, TargetSnapshot snap)> _undoStack = new();
        private readonly List<(int idx, TargetSnapshot snap)> _redoStack = new();
        private bool _snapshotPending;

        // File backup
        private string _lastBackupPath;

        // Rotation gizmo — track drag-start baselines for batch rotation
        private bool       _rotDragActive;
        private Quaternion _rotDragStartHandle;
        private Quaternion _rotDragStartLocal;
        private Dictionary<int, Quaternion> _rotDragStartMulti;

        // Scene objects — no hidden preview root; we work directly with live spawned parts.
        // MeshColliders added to live parts for click-to-snap are tracked here and removed on Cleanup.
        private readonly List<(GameObject go, MeshCollider col)> _addedMeshColliders = new();

        // Tool preview — cyan-tinted ghost mesh parented under the spawner's PreviewRoot
        [SerializeField] private bool _showToolPreview = true;
        private ToolDefinition _toolPreviewDef;   // ToolDefinition for the previewed tool
        private GameObject _toolPreviewGO;         // instantiated tool mesh (HideAndDontSave)

        // Wire preview — LineRenderer GameObjects (HideAndDontSave) for wire width visualization
        private GameObject _wirePreviewRoot;

        // ── Parts tab ─────────────────────────────────────────────────────────
        // Part model preview panel
        private PartModelPreviewRenderer _partPreview;
        private string                   _partPreviewId;   // partId currently loaded in preview
        private const string             PrefDimUnit = "OSE.AuthoringWindow.DimUnit"; // "mm" or "in"

        private PartEditState[]          _parts;
        [SerializeField] private int    _selectedPartIdx = -1;
        [SerializeField] private string _selectedPartId;
        private readonly HashSet<int>   _multiSelectedParts = new HashSet<int>();
        [SerializeField] private bool   _editPlayPose;         // false=start, true=play
        private int _poseSwitchCooldown;  // >0 means pose just toggled; suppress false dirty for N scene frames
        private readonly List<(int idx, PartSnapshot snap)> _undoStackParts = new();
        private readonly List<(int idx, PartSnapshot snap)> _redoStackParts = new();
        private bool _snapshotPendingPart;
        private bool       _rotDragActivePart;
        private Quaternion _rotDragStartHandlePart;
        private Quaternion _rotDragStartLocalPart;
        private Dictionary<int, Quaternion> _rotDragStartMultiPart;
        private Vector2 _partListScroll;

        // ── Task sequence / add-task state ────────────────────────────────────

        // Dirty tracking for taskOrder writes
        private readonly HashSet<string> _dirtyTaskOrderStepIds = new HashSet<string>(StringComparer.Ordinal);
        // Cached derived task order for the currently selected step
        private List<TaskOrderEntry> _cachedTaskOrder;
        private string               _cachedTaskOrderForStepId;

        // Drag-and-drop reorderable list for TASK SEQUENCE
        private ReorderableList _taskSeqReorderList;
        private string          _taskSeqReorderListForStepId;
        private int             _selectedTaskSeqIdx = -1; // selected row in TASK SEQUENCE
        private readonly HashSet<int> _multiSelectedTaskSeqIdxs = new HashSet<int>();

        // Add-task inline picker (shown below task sequence)
        private enum AddTaskPicker { None, Part, ToolTarget, Wire }
        private AddTaskPicker _addTaskPicker = AddTaskPicker.None;
        private int    _addPickerPartIdx;
        private int    _addPickerTargetIdx;
        private int    _addPickerToolIdx;
        private Color  _addPickerWireColor  = new Color(0.15f, 0.15f, 0.15f, 1f);
        private float  _addPickerWireRadius = 0.003f;
        private string _addPickerPolarityA  = "";
        private string _addPickerPolarityB  = "";
        private string _addPickerConnectorA = "";
        private string _addPickerConnectorB = "";

        // New-step creation form
        private bool   _showNewStepForm;
        private string _newStepId      = "";
        private string _newStepName    = "";
        private int    _newStepFamilyIdx;  // index into _familyOptions
        private int    _newStepProfileIdx; // index into profile sub-options
        private int    _newStepAssemblyIdx;
        private int    _newStepSeqIdx;

        private static readonly string[] _familyOptions = { "Place", "Use", "Connect", "Confirm" };
        private static readonly string[][] _profileOptions =
        {
            new[] { "(none)", "AxisFit" },                               // Place
            new[] { "(none)", "Torque", "Weld", "Cut", "SquareCheck", "Measure" }, // Use
            new[] { "(none)", "Cable", "WireConnect" },                  // Connect
            new[] { "(none)" },                                          // Confirm
        };

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

        private struct PartEditState
        {
            public PartDefinition       def;
            public PartPreviewPlacement placement;
            public bool   hasPlacement, isDirty;
            public Vector3    startPosition, startScale, playPosition, playScale;
            public Quaternion startRotation, playRotation;
            public Color      color;
        }

        private struct PartSnapshot
        {
            public Vector3 startPosition; public Quaternion startRotation; public Vector3 startScale;
            public Vector3 playPosition;  public Quaternion playRotation;  public Vector3 playScale;
        }

        // ── MenuItem ──────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Open() => OpenWindow();

        private static void OpenWindow()
        {
            var w = GetWindow<ToolTargetAuthoringWindow>("Assembly Step Authoring");
            w.minSize = new Vector2(440, 580);
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // Destroy any stale PreviewRoot objects left from a previous session that
            // survived domain reload with HideFlags.HideAndDontSave.
            // (No stale preview roots to destroy — TTAW no longer creates HideAndDontSave objects.)

            RefreshPackageList();
            // Package restore after domain reload is handled by OnGUI (first frame)
            // where the AssetDatabase is guaranteed to be ready.
            // Only handle the fresh-open fallback (no _pkgId yet) here.
            if (_pkg == null && string.IsNullOrEmpty(_pkgId)
                && _packageIds != null && _packageIds.Length > 0
                && _pkgIdx >= 0 && _pkgIdx < _packageIds.Length)
            {
                LoadPkg(_packageIds[_pkgIdx]);
            }
            SceneView.duringSceneGui += OnSceneGUI;
            SessionDriver.EditModeStepChanged += OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RuntimeEventBus.Subscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SessionDriver.EditModeStepChanged -= OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            RuntimeEventBus.Unsubscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
            // Destroy scene objects but do NOT reset serialized state (_selectedIdx,
            // _selectedTargetId, etc.) — OnDisable runs BEFORE Unity serializes
            // [SerializeField] fields during domain reload, so resetting here
            // would erase the values we need to restore in OnEnable.
            _partPreview?.Dispose();
            _partPreview   = null;
            _partPreviewId = null;
            RemoveMeshCollidersFromLiveParts();
            ClearToolPreview();
            ClearWirePreview();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            // Reload the package so the window reflects any runtime changes.
            if (!string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId);
        }

        /// <summary>
        /// Fired each time <see cref="PackagePartSpawner"/> finishes a spawn cycle.
        /// Re-sync live part positions and add mesh colliders so click-to-snap still works.
        /// </summary>
        private void OnSpawnerPartsReady(SpawnerPartsReady _)
        {
            // Re-apply authoritative _pkg positions after the spawn cycle.
            // The spawn itself calls ApplyStepAwarePositions(_editModePackage) which may
            // override positions using stale StreamingAssets data — overwrite with _pkg.
            ApplySpawnerStepPositions();
            SyncAllPartMeshesToActivePose();
            AddMeshCollidersToLiveParts();
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

        /// <summary>
        /// Returns the spawner's PreviewRoot transform, used as the coordinate space
        /// for all target positions and tool preview placement.
        /// </summary>
        private static Transform GetPreviewRoot()
        {
            return ServiceRegistry.TryGet<ISpawnerQueryService>(out var s) ? s.PreviewRoot : null;
        }

        /// <summary>
        /// Adds a MeshCollider to each face of every live spawned part so the user can
        /// click directly on a mesh surface to snap a target (click-to-snap).
        /// Colliders are tracked in <see cref="_addedMeshColliders"/> and removed by
        /// <see cref="RemoveMeshCollidersFromLiveParts"/>.
        /// </summary>
        private void AddMeshCollidersToLiveParts()
        {
            RemoveMeshCollidersFromLiveParts(); // clear stale ones first
            if (!ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner) || spawner?.SpawnedParts == null)
                return;

            foreach (var go in spawner.SpawnedParts)
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    _addedMeshColliders.Add((mf.gameObject, mc));
                }
            }
        }

        /// <summary>Removes MeshColliders that were added by <see cref="AddMeshCollidersToLiveParts"/>.</summary>
        private void RemoveMeshCollidersFromLiveParts()
        {
            foreach (var (go, col) in _addedMeshColliders)
            {
                if (go != null && col != null)
                    DestroyImmediate(col);
            }
            _addedMeshColliders.Clear();
        }

        private void Cleanup()
        {
            RemoveMeshCollidersFromLiveParts();
            _partPreview?.Dispose();
            _partPreview   = null;
            _partPreviewId = null;
            RemoveMeshCollidersFromLiveParts();
            ClearToolPreview();
            ClearWirePreview();
            _targets = null;
            _selectedIdx = -1;
            _multiSelected.Clear();
            _parts = null;
            _selectedPartIdx = -1;
            _multiSelectedParts.Clear();
            _multiSelectedTaskSeqIdxs.Clear();
            // Discard unsaved dirty tracking so stale bits don't bleed into the next package load.
            _dirtyToolIds.Clear();
            _dirtyStepIds.Clear();
            _dirtyTaskOrderStepIds.Clear();
            _dirtyPartAssetRefIds.Clear();
        }

        // ── Main GUI ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Restore after domain reload: _pkgId survives via [SerializeField] but
            // _pkg (not serializable) is lost.  By the time OnGUI runs the
            // AssetDatabase is ready, so scene meshes and tool previews load correctly.
            if (_pkg == null && !string.IsNullOrEmpty(_pkgId))
            {
                LoadPkg(_pkgId, restoring: true);
                // If load failed, clear _pkgId to avoid retrying every frame
                if (_pkg == null) _pkgId = null;
            }

            EditorGUILayout.Space(4);
            DrawPkgPicker();
            if (_pkg == null) return;

            DrawStepFilter();
            EditorGUILayout.Space(2);

            // Measure where top content ends (only accurate during Repaint, cached for other events)
            if (Event.current.type == EventType.Repaint)
                _topContentHeight = GUILayoutUtility.GetLastRect().yMax + 4f;

            // ── Layout constants ──────────────────────────────────────────────
            // When a task is selected the context panel lives inside the scroll area,
            // so the pinned bottom bar shrinks to just the action buttons.
            bool taskDetailInScroll = _selectedTaskSeqIdx >= 0 && _stepFilterIdx > 0;
            float kEditH      = taskDetailInScroll ? 0f : 230f;
            const float kActionsH   =  54f;
            float kBottomBarH = kEditH + kActionsH + 10f;

            // List height: exact gap between measured top and pinned bottom bar
            float listH = Mathf.Max(position.height - _topContentHeight - kBottomBarH - 4f, 60f);
            DrawUnifiedList(listH);

            // ── Pinned bottom bar ─────────────────────────────────────────────
            Rect bottomRect = new Rect(4f, position.height - kBottomBarH,
                                       position.width - 8f, kBottomBarH);
            GUILayout.BeginArea(bottomRect);

            // Separator
            EditorGUI.DrawRect(new Rect(0f, 0f, bottomRect.width, 1f),
                               new Color(0.13f, 0.13f, 0.13f));
            EditorGUILayout.Space(3);

            // Edit fields — only shown when no task is driving the context panel
            // (in that case, detail is rendered inline in the scroll area above).
            if (!taskDetailInScroll)
            {
                _detailScroll = EditorGUILayout.BeginScrollView(
                    _detailScroll, GUILayout.Height(kEditH));
                DrawBottomEditPanel();
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(3);
            }

            // Action buttons (always visible)
            DrawUnifiedActions();

            GUILayout.EndArea();
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
            if (i != _pkgIdx)
            {
                _pkgIdx = i;
                LoadPkg(_packageIds[i]);
                // Sync EditModePreviewDriver so spawned parts match the new package.
                var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
                if (driver != null) driver.SetPackage(_packageIds[i]);
            }
            if (_pkg == null && GUILayout.Button("Load")) LoadPkg(_packageIds[_pkgIdx]);
        }

        // ── New step form ─────────────────────────────────────────────────────

        private void DrawNewStepForm()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("New Step", EditorStyles.boldLabel);

            _newStepId   = EditorGUILayout.TextField("ID",   _newStepId);
            _newStepName = EditorGUILayout.TextField("Name", _newStepName);

            _newStepFamilyIdx  = EditorGUILayout.Popup("Family",  _newStepFamilyIdx,  _familyOptions);
            _newStepProfileIdx = EditorGUILayout.Popup("Profile", _newStepProfileIdx, _profileOptions[_newStepFamilyIdx]);

            // Assembly picker
            if (_pkg?.assemblies != null && _pkg.assemblies.Length > 0)
            {
                string[] asmOpts = _pkg.assemblies.Select(a => a?.id ?? "?").ToArray();
                _newStepAssemblyIdx = Mathf.Clamp(_newStepAssemblyIdx, 0, asmOpts.Length - 1);
                _newStepAssemblyIdx = EditorGUILayout.Popup("Assembly", _newStepAssemblyIdx, asmOpts);
            }

            _newStepSeqIdx = EditorGUILayout.IntField("Sequence Index", _newStepSeqIdx);

            // Validation feedback
            bool idEmpty    = string.IsNullOrWhiteSpace(_newStepId);
            bool idConflict = !idEmpty && _pkg?.steps != null && System.Array.Exists(_pkg.steps, s => s?.id == _newStepId.Trim());
            if (idEmpty)    EditorGUILayout.HelpBox("ID is required.", MessageType.Warning);
            if (idConflict) EditorGUILayout.HelpBox($"Step ID '{_newStepId.Trim()}' already exists.", MessageType.Error);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(idEmpty || idConflict);
            if (GUILayout.Button("Create Step", GUILayout.Width(90)))
            {
                CommitNewStep();
                _showNewStepForm = false;
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _showNewStepForm = false;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void CommitNewStep()
        {
            string profile = _profileOptions[_newStepFamilyIdx][_newStepProfileIdx];
            if (profile == "(none)") profile = null;

            string assemblyId = null;
            if (_pkg?.assemblies != null && _pkg.assemblies.Length > 0 && _newStepAssemblyIdx < _pkg.assemblies.Length)
                assemblyId = _pkg.assemblies[_newStepAssemblyIdx]?.id;

            var newStep = new StepDefinition
            {
                id            = _newStepId.Trim(),
                name          = _newStepName.Trim(),
                family        = _familyOptions[_newStepFamilyIdx],
                profile       = profile,
                assemblyId    = assemblyId,
                sequenceIndex = _newStepSeqIdx,
            };

            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (string.IsNullOrEmpty(jsonPath) || !System.IO.File.Exists(jsonPath))
            {
                UnityEditor.EditorUtility.DisplayDialog("Error", "Could not locate machine.json.", "OK");
                return;
            }

            try
            {
                PackageJsonUtils.InsertStep(jsonPath, newStep);
            }
            catch (System.Exception ex)
            {
                UnityEditor.EditorUtility.DisplayDialog("Error", $"Failed to insert step:\n{ex.Message}", "OK");
                return;
            }

            // Reload and select the new step
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            if (_pkg != null)
                _assetResolver.BuildCatalog(_pkgId, _pkg.parts ?? System.Array.Empty<PartDefinition>());
            BuildStepOptions();
            BuildTargetList();
            BuildPartList();
            BuildTargetToolMap();

            // Find and select the new step in the dropdown
            if (_stepIds != null)
                for (int i = 0; i < _stepIds.Length; i++)
                    if (_stepIds[i] == newStep.id) { ApplyStepFilter(i); break; }
        }

        // ── Step filter ───────────────────────────────────────────────────────

        private void DrawStepFilter()
        {
            if (_stepOptions == null || _stepOptions.Length == 0) return;

            // Poll SessionDriver each draw — catches changes from its inspector
            // regardless of whether the static event fired.
            if (!_suppressStepSync && _stepSequenceIdxs != null)
            {
                var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
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
            if (GUILayout.Button("◄|", GUILayout.Width(28)))
                ApplyStepFilter(1);
            if (GUILayout.Button("◄", GUILayout.Width(28)))
                ApplyStepFilter(_stepFilterIdx - 1);
            EditorGUI.EndDisabledGroup();

            // Step number: drag left/right to scrub, click to type
            _stepNumRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.Width(46));
            EditorGUIUtility.AddCursorRect(_stepNumRect, MouseCursor.SlideArrow);

            var ev = Event.current;
            // Mouse-down on the field — start potential drag, don't consume (let IntField handle click-to-edit)
            if (ev.type == EventType.MouseDown && ev.button == 0 && _stepNumRect.Contains(ev.mousePosition))
            {
                _stepDragging    = false;
                _stepDragAccum   = 0f;
                _stepDragStartVal = _stepFilterIdx;
            }
            // Mouse-drag — scrub the step index; consume so the text field doesn't see it
            if (ev.type == EventType.MouseDrag && ev.button == 0 && _stepDragStartVal >= 0)
            {
                if (!_stepDragging)
                    GUIUtility.keyboardControl = 0; // release text focus so the field shows the live value
                _stepDragging  = true;
                _stepDragAccum += ev.delta.x;
                int scrubbed = Mathf.Clamp(_stepDragStartVal + Mathf.RoundToInt(_stepDragAccum / 4f), 0, stepCount);
                if (scrubbed != _stepFilterIdx) { ApplyStepFilter(scrubbed); Repaint(); }
                ev.Use();
            }
            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                bool wasDragging = _stepDragging;
                _stepDragging    = false;
                _stepDragStartVal = -1;
                // Force a final sync to the SessionDriver on drag release so the scene
                // always lands on the correct step regardless of throttling.
                if (wasDragging && !_suppressStepSync)
                    SyncSessionDriverStep();
            }

            var numStyle = new GUIStyle(EditorStyles.numberField) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            int typedVal = EditorGUI.IntField(_stepNumRect, _stepFilterIdx, numStyle);
            if (!_stepDragging)
            {
                typedVal = Mathf.Clamp(typedVal, 0, stepCount);
                if (typedVal != _stepFilterIdx) ApplyStepFilter(typedVal);
            }

            // Scroll-wheel also scrubs (delta.y > 0 = scroll down = previous step)
            if (_stepNumRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.ScrollWheel)
            {
                int delta    = Event.current.delta.y > 0f ? -1 : 1;
                int scrolled = Mathf.Clamp(_stepFilterIdx + delta, 0, stepCount);
                if (scrolled != _stepFilterIdx) ApplyStepFilter(scrolled);
                Event.current.Use();
                Repaint();
            }

            GUILayout.Label($"/{stepCount}", EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
            GUILayout.Space(6);

            // Step title — fills remaining space, clips if window is narrow
            string stepTitle = _stepFilterIdx == 0
                ? "All Steps"
                : ((_stepIds != null && _stepFilterIdx < _stepIds.Length)
                    ? (FindStep(_stepIds[_stepFilterIdx])?.GetDisplayName() ?? "")
                    : "");
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            GUILayout.Label(stepTitle, titleStyle);

            EditorGUI.BeginDisabledGroup(_stepFilterIdx >= stepCount);
            if (GUILayout.Button("►", GUILayout.Width(28)))
                ApplyStepFilter(_stepFilterIdx + 1);
            if (GUILayout.Button("|►", GUILayout.Width(28)))
                ApplyStepFilter(stepCount);
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(4);
            if (GUILayout.Button("+ New Step", GUILayout.Width(82)))
            {
                _showNewStepForm   = !_showNewStepForm;
                _newStepId         = "";
                _newStepName       = "";
                _newStepFamilyIdx  = 0;
                _newStepProfileIdx = 0;
                _newStepAssemblyIdx = 0;
                int afterSeq = _stepFilterIdx > 0 && _stepSequenceIdxs != null
                    ? _stepSequenceIdxs[_stepFilterIdx] + 1
                    : (_pkg?.GetSteps()?.Max(s => s?.sequenceIndex ?? 0) ?? 0) + 1;
                _newStepSeqIdx = afterSeq;
            }

            EditorGUILayout.EndHorizontal();

            // ── New step form ──────────────────────────────────────────────��──
            if (_showNewStepForm) DrawNewStepForm();

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
                    string stepDisplayName = step.GetDisplayName();
                    if (!string.IsNullOrEmpty(stepDisplayName))
                        EditorGUILayout.LabelField(stepDisplayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"Tool: {toolName}{profileStr}  ·  {tCount} target{(tCount == 1 ? "" : "s")}",
                        EditorStyles.miniLabel);
                    if (_previewAssembled + _previewCurrent + _previewHidden > 0)
                        EditorGUILayout.LabelField(
                            $"{_previewAssembled} assembled  ·  {_previewCurrent} at start pos  ·  {_previewHidden} hidden",
                            EditorStyles.miniLabel);

                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void ApplyStepFilter(int newIdx)
        {
            _stepFilterIdx    = newIdx;
            _selectedIdx      = -1;
            _selectedPartIdx  = -1;
            _multiSelected.Clear();
            _multiSelectedParts.Clear();
            _clickToSnapActive = false;
            _addTaskPicker          = AddTaskPicker.None;
            _selectedTaskSeqIdx     = -1;
            _multiSelectedTaskSeqIdxs.Clear();
            _activeTaskKind         = null;
            _taskSeqReorderList     = null;
            _taskSeqReorderListForStepId = null;
            InvalidateTaskOrderCache();
            UpdateActiveStep();
            BuildTargetList();
            BuildPartList();
            _editPlayPose = false;           // always land on Start Pose when switching steps
            RespawnScene();                  // uses _editPlayPose — must come AFTER the reset
            SyncAllPartMeshesToActivePose(); // second pass: ensures live GOs match after RespawnScene
            ApplySpawnerStepPositions();     // first pass: push step-aware positions before driver sync
            AutoSelectFirstTaskEntry();      // default-select first badge so a section is visible
            if (!_suppressStepSync)
                SyncSessionDriverStep();
            // Final pass: re-apply after SyncSessionDriverStep, because SetEditModeStep →
            // ApplyStepAwarePartPositions uses _editModePackage (StreamingAssets) which may
            // override the authoritative _pkg positions set above.
            ApplySpawnerStepPositions();
            SyncAllPartMeshesToActivePose();
            var currentStep = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            RefreshWirePreview(currentStep);
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// When a step is first loaded, auto-select the first task entry so the
        /// matching section (parts / targets / wire) is immediately visible.
        /// </summary>
        private void AutoSelectFirstTaskEntry()
        {
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length) return;
            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;
            var order = GetOrDeriveTaskOrder(step);
            if (order.Count == 0) return;
            _selectedTaskSeqIdx = 0;
            ApplyTaskEntrySelection(step, order[0]);
        }

        // Minimum seconds between SessionDriver pushes while dragging the step scrubber.
        // Prevents hammering ApplyStepAwarePositions (and any async loaders) on every
        // pixel of mouse movement. Value chosen to allow ~10 updates/sec during drag.
        private const double DriverSyncMinIntervalSec = 0.1;

        /// <summary>
        /// Directly tells the spawner to reposition parts for the current step filter,
        /// bypassing the SessionDriver round-trip. Called unconditionally in ApplyStepFilter
        /// so parts always land at startPosition even when _suppressStepSync is true
        /// (e.g. when OnSessionDriverStepChanged triggers the step change).
        /// </summary>
        private void ApplySpawnerStepPositions()
        {
            if (_pkg == null || _stepFilterIdx <= 0 || _stepSequenceIdxs == null
                || _stepFilterIdx >= _stepSequenceIdxs.Length) return;

            if (ServiceRegistry.TryGet<IStepAwarePositioner>(out var positioner))
            {
                int sequenceIndex = _stepSequenceIdxs[_stepFilterIdx];
                positioner.ApplyStepAwarePositions(sequenceIndex, _pkg);
            }
        }

        private void SyncSessionDriverStep()
        {
            if (_pkg == null) return;
            var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
            if (driver == null) return;
            if (_stepFilterIdx <= 0 || _stepSequenceIdxs == null || _stepFilterIdx >= _stepSequenceIdxs.Length)
                return;

            // Throttle during drag so the scene spawner isn't driven for every
            // intermediate step the mouse passes through.
            if (_stepDragging)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastDriverSyncTime < DriverSyncMinIntervalSec)
                    return;
                _lastDriverSyncTime = now;
            }
            else
            {
                _lastDriverSyncTime = EditorApplication.timeSinceStartup;
            }

            int sequenceIndex = _stepSequenceIdxs[_stepFilterIdx];
            _suppressStepSync     = true;
            _lastPolledDriverStep = sequenceIndex; // prevent poll from re-triggering
            driver.SetEditModeStep(sequenceIndex);
            _suppressStepSync = false;
        }

        private void UpdateActiveStep()
        {
            _activeStepProfile   = null;
            _activeStepIsConnect = false;
            _activeStepTargetIds = null;

            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length)
                return;

            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            _activeStepProfile   = string.IsNullOrEmpty(step.profile) ? null : step.profile;
            _activeStepIsConnect = step.ResolvedFamily == OSE.Content.StepFamily.Connect;

            // Always assign a HashSet (even empty) so hasStepFilter is true and
            // targets not belonging to this step are dimmed. Leaving it null causes
            // every target in the package to render at full brightness for steps
            // that have no targetIds (e.g. CONFIRM steps).
            _activeStepTargetIds = step.targetIds != null && step.targetIds.Length > 0
                ? new HashSet<string>(step.targetIds, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
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
                    _selectedTargetId  = (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                        ? _targets[_selectedIdx].def.id : null;
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

            // State shown only when there is no placement data yet (the context panel
            // header handles "Unsaved Changes" and "Saved" is implicit when editing works).
            if (!t.hasPlacement && !t.isDirty)
            {
                var noDataStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColNoPlacement } };
                EditorGUILayout.LabelField("No placement data", noDataStyle);
            }

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

            // ── Fields driven by TaskFieldProfile ────────────────────────────
            var fieldProfile = TaskFieldRegistry.Get(_activeTaskKind ?? "");

            if (fieldProfile.ShowPosition)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = EditorGUILayout.Vector3Field("Position (local)", t.position);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.position = newPos; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            if (fieldProfile.ShowRotation)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation (euler)", t.rotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.rotation = Quaternion.Euler(newEuler); t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            if (fieldProfile.ShowScale)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newScale = EditorGUILayout.Vector3Field("Scale", t.scale);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.scale = newScale; t.isDirty = true; EndEdit(); }
            }

            if (!fieldProfile.ShowPosition && !fieldProfile.ShowRotation && !fieldProfile.ShowScale)
                return; // nothing left to render for this task kind

            EditorGUILayout.Space(4);

            // ── Profile-gated groups ──────────────────────────────────────────

            if (fieldProfile.ShowWeldAxis && ShowWeldGroup())
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

            if (fieldProfile.ShowPortFields && ShowPortGroup())
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

            if (fieldProfile.ShowClickToSnap)
            {
                EditorGUILayout.Space(6);
                EditorGUI.BeginChangeCheck();
                _clickToSnapActive = EditorGUILayout.Toggle(
                    new GUIContent("Click-to-Snap",
                        "Enable, then left-click any mesh surface in SceneView.\n" +
                        "Target snaps to that point; rotation and weld axis auto-align to surface normal."),
                    _clickToSnapActive);
                if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            }

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

            // ── Position (per-axis, all selected) ─────────────────────────────
            // Each axis is independent — changing X only writes X to every target.
            EditorGUILayout.LabelField("Position (all selected)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            float batchX = EditorGUILayout.FloatField("X", rep.position.x);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position.x = batchX; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchY = EditorGUILayout.FloatField("Y", rep.position.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position.y = batchY; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchZ = EditorGUILayout.FloatField("Z", rep.position.z);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position.z = batchZ; t.isDirty = true; }
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

        // portA/portB are wire/pipe endpoints — visible only for Connect-family steps.
        private bool ShowPortGroup()    => _activeStepIsConnect;

        // ── Actions ───────────────────────────────────────────────────────────

        private void DrawActions()
        {
            bool anyDirty = AnyDirty();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!anyDirty);
            GUI.backgroundColor = anyDirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (GUILayout.Button("Write to machine.json", GUILayout.Height(28))) WriteJson();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(28)))
                RevertAllChanges();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Extract from GLB Anchors"))
                ExtractFromGlbAnchors();

            if (GUILayout.Button("Sync All Tool Rotations from Placements"))
                SyncAllToolRotationsFromPlacements();

            // Show unlinked count and offer auto-link for filename-matched parts
            if (_pkg?.parts != null)
            {
                int unlinked = 0;
                foreach (var pd in _pkg.parts)
                    if (pd != null && string.IsNullOrEmpty(pd.assetRef)) unlinked++;

                if (unlinked > 0)
                {
                    var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                        { normal = { textColor = new Color(1f, 0.75f, 0.2f) } };
                    EditorGUILayout.LabelField($"⚠ {unlinked} parts have no assetRef", warnStyle);
                    if (GUILayout.Button("Auto-link by filename", EditorStyles.miniButton))
                        AutoLinkPartsByFilename();
                }
            }

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write (restore backup)"))
                RevertFromBackup();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Frame in SceneView")) FrameInScene();
        }

        private void DrawPartPoseToggle()
        {
            EditorGUILayout.BeginHorizontal();
            bool wantStart = GUILayout.Toggle(!_editPlayPose, "Start Pose", EditorStyles.miniButtonLeft);
            bool wantPlay  = GUILayout.Toggle(_editPlayPose,  "Play Pose",  EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();

            bool clickedStart = wantStart && _editPlayPose;
            bool clickedPlay  = wantPlay  && !_editPlayPose;
            if (clickedStart || clickedPlay)
            {
                _editPlayPose = clickedPlay;
                _poseSwitchCooldown = 3; // suppress false dirty from handle re-init for a few scene frames
                SyncAllPartMeshesToActivePose();
                SceneView.RepaintAll();
            }
        }

        // ── Unified list ──────────────────────────────────────────────────────

        private void DrawUnifiedList(float height)
        {
            _partListScroll = EditorGUILayout.BeginScrollView(_partListScroll, GUILayout.Height(height));

            bool hasStepSelected = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length;

            if (!hasStepSelected)
            {
                // All Steps mode — show flat part + target counts only
                if (_pkg != null)
                {
                    DrawUnifiedSectionHeader($"PART PLACEMENT ({_pkg.GetParts()?.Length ?? 0})", 0);
                    EditorGUILayout.LabelField("  Select a step to view tasks.", EditorStyles.miniLabel);
                    EditorGUILayout.Space(4);
                    int allTargets = _pkg.GetTargets()?.Length ?? 0;
                    DrawUnifiedSectionHeader($"TOOL TARGETS ({allTargets})", 0);
                    EditorGUILayout.LabelField("  Select a step to view tasks.", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) { EditorGUILayout.EndScrollView(); return; }

            var order = GetOrDeriveTaskOrder(step);

            // ── TASK SEQUENCE ──────────────────────────────────────────────────
            string taskSeqHeader = _multiSelectedTaskSeqIdxs.Count > 1
                ? $"TASK SEQUENCE ({order.Count})  —  {_multiSelectedTaskSeqIdxs.Count} selected  (Ctrl+click / Shift+click)"
                : $"TASK SEQUENCE ({order.Count})";
            DrawUnifiedSectionHeader(taskSeqHeader, order.Count,
                () =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Part"),           false, () => { _addTaskPicker = AddTaskPicker.Part;       _addPickerPartIdx = 0; _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.AddItem(new GUIContent("Tool Target"),    false, () => { _addTaskPicker = AddTaskPicker.ToolTarget; _addPickerTargetIdx = 0; _addPickerToolIdx = 0; _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.AddItem(new GUIContent("Wire Connection"),false, () => { _addTaskPicker = AddTaskPicker.Wire;       _addPickerTargetIdx = 0; _addPickerWireColor = new Color(0.15f, 0.15f, 0.15f, 1f); _addPickerWireRadius = 0.003f; _addPickerPolarityA = ""; _addPickerPolarityB = ""; _addPickerConnectorA = ""; _addPickerConnectorB = ""; _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.ShowAsContext();
                });

            if (order.Count == 0)
                EditorGUILayout.LabelField("  No tasks yet. Press + to add.", EditorStyles.miniLabel);
            else
                DrawTaskSequenceDragList(step, order);

            // ── Add-task picker (shown below sequence list) ────────────────────
            if (_addTaskPicker == AddTaskPicker.Part)       DrawAddPartPicker();
            if (_addTaskPicker == AddTaskPicker.ToolTarget) DrawAddToolTargetPicker();
            if (_addTaskPicker == AddTaskPicker.Wire)       DrawAddWirePicker();

            // ── Section for selected task kind (one section at a time) ─────────
            if (_selectedTaskSeqIdx >= 0 && _selectedTaskSeqIdx < order.Count)
            {
                var selEntry = order[_selectedTaskSeqIdx];
                EditorGUILayout.Space(4);

                // Multi-selection → batch panel (parts first, then targets, then
                // fall through to the primary entry's single-item detail panel)
                if (_multiSelectedTaskSeqIdxs.Count > 1 && _multiSelectedParts.Count > 1)
                {
                    DrawUnifiedSectionHeader($"BATCH — {_multiSelectedParts.Count} parts", 0);
                    DrawPartPoseToggle();
                    DrawPartBatchPanel();
                }
                else if (_multiSelectedTaskSeqIdxs.Count > 1 && _multiSelected.Count > 1)
                {
                    DrawUnifiedSectionHeader($"BATCH — {_multiSelected.Count} targets", 0);
                    DrawBatchPanel();
                }
                else switch (selEntry.kind)
                {
                    case "part":
                    {
                        DrawUnifiedSectionHeader($"PART CONTEXT ({selEntry.id})", 0);
                        if (IsTaskEntryDirty(selEntry, step))
                        {
                            EditorGUILayout.BeginHorizontal();
                            var ds = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColDirty }, fontStyle = FontStyle.Bold };
                            EditorGUILayout.LabelField("● Unsaved Changes", ds);
                            if (GUILayout.Button("Revert", EditorStyles.miniButton, GUILayout.Width(52)))
                                RevertPartEntry(selEntry.id);
                            EditorGUILayout.EndHorizontal();
                        }
                        DrawPartPoseToggle();
                        if (_parts != null)
                            for (int i = 0; i < _parts.Length; i++)
                                if (_parts[i].def?.id == selEntry.id)
                                { DrawPartDetailPanel(ref _parts[i]); break; }
                        break;
                    }
                    case "wire":
                    {
                        DrawUnifiedSectionHeader($"WIRE CONTEXT ({selEntry.id})", 0);
                        if (IsTaskEntryDirty(selEntry, step))
                        {
                            EditorGUILayout.BeginHorizontal();
                            var ds = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColDirty }, fontStyle = FontStyle.Bold };
                            EditorGUILayout.LabelField("● Unsaved Changes", ds);
                            if (GUILayout.Button("Revert", EditorStyles.miniButton, GUILayout.Width(52)))
                                RevertTargetEntry(selEntry.id);
                            EditorGUILayout.EndHorizontal();
                        }

                        // Polarity / connector fields for the selected wire entry
                        if (step.wireConnect?.IsConfigured == true && step.wireConnect.wires != null)
                        {
                            WireConnectEntry wire = null;
                            foreach (var w in step.wireConnect.wires)
                                if (string.Equals(w.targetId, selEntry.id, StringComparison.Ordinal))
                                { wire = w; break; }

                            if (wire != null)
                            {
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Polarity A:", GUILayout.Width(74));
                                wire.portAPolarityType = EditorGUILayout.TextField(wire.portAPolarityType ?? "", GUILayout.Width(80));
                                EditorGUILayout.LabelField("Polarity B:", GUILayout.Width(74));
                                wire.portBPolarityType = EditorGUILayout.TextField(wire.portBPolarityType ?? "");
                                EditorGUILayout.EndHorizontal();

                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField("Connector A:", GUILayout.Width(74));
                                wire.portAConnectorType = EditorGUILayout.TextField(wire.portAConnectorType ?? "", GUILayout.Width(80));
                                EditorGUILayout.LabelField("Connector B:", GUILayout.Width(74));
                                wire.portBConnectorType = EditorGUILayout.TextField(wire.portBConnectorType ?? "");
                                EditorGUILayout.EndHorizontal();
                                if (EditorGUI.EndChangeCheck()) _dirtyStepIds.Add(step.id);

                                EditorGUI.BeginChangeCheck();
                                EditorGUILayout.BeginHorizontal();
                                wire.polarityOrderMatters = EditorGUILayout.ToggleLeft(
                                    "Polarity order matters", wire.polarityOrderMatters, EditorStyles.miniLabel,
                                    GUILayout.Width(150));
                                if (step.wireConnect != null)
                                    step.wireConnect.enforcePortOrder = EditorGUILayout.ToggleLeft(
                                        "Enforce port order (A first)", step.wireConnect.enforcePortOrder,
                                        EditorStyles.miniLabel);
                                EditorGUILayout.EndHorizontal();
                                if (EditorGUI.EndChangeCheck()) _dirtyStepIds.Add(step.id);

                                EditorGUILayout.EndVertical();

                                // Port positions — read/write directly from wire entry
                                EditorGUILayout.Space(2);
                                {
                                    EditorGUI.BeginChangeCheck();
                                    Vector3 newA = EditorGUILayout.Vector3Field("Port A (local)", new Vector3(wire.portA.x, wire.portA.y, wire.portA.z));
                                    Vector3 newB = EditorGUILayout.Vector3Field("Port B (local)", new Vector3(wire.portB.x, wire.portB.y, wire.portB.z));
                                    if (EditorGUI.EndChangeCheck())
                                    {
                                        wire.portA = PackageJsonUtils.ToFloat3(newA);
                                        wire.portB = PackageJsonUtils.ToFloat3(newB);
                                        if (_targets != null)
                                            for (int i = 0; i < _targets.Length; i++)
                                                if (_targets[i].def?.id == selEntry.id)
                                                { BeginEdit(); _targets[i].portA = newA; _targets[i].portB = newB; _targets[i].isDirty = true; EndEdit(); break; }
                                        _dirtyStepIds.Add(step.id);
                                        RefreshWirePreview(step);
                                        SceneView.RepaintAll();
                                    }
                                }

                                // Color + Radius + Subdivisions — wire appearance is self-contained
                                EditorGUILayout.Space(2);
                                EditorGUI.BeginChangeCheck();
                                Color wc = wire.color.a > 0
                                    ? new Color(wire.color.r, wire.color.g, wire.color.b, wire.color.a)
                                    : new Color(0.15f, 0.15f, 0.15f, 1f);
                                Color nc = EditorGUILayout.ColorField("Color", wc);
                                wire.color = new SceneFloat4 { r = nc.r, g = nc.g, b = nc.b, a = nc.a };
                                float nw = EditorGUILayout.FloatField("Radius (m)", wire.radius > 0 ? wire.radius : 0.003f);
                                wire.radius = Mathf.Max(0f, nw);
                                wire.subdivisions = Mathf.Max(1, EditorGUILayout.IntField("Subdivisions", wire.subdivisions < 1 ? 1 : wire.subdivisions));
                                float displaySag = wire.sag > 0f ? wire.sag : 1.0f;
                                float newSag = EditorGUILayout.Slider("Sag", displaySag, 0.01f, 3.0f);
                                wire.sag = newSag;
                                bool isLinear = string.Equals(wire.interpolation, "linear", StringComparison.OrdinalIgnoreCase);
                                int interpIdx = EditorGUILayout.Popup("Interpolation", isLinear ? 1 : 0, new[] { "Bezier", "Linear" });
                                wire.interpolation = interpIdx == 1 ? "linear" : "bezier";
                                if (EditorGUI.EndChangeCheck()) { _dirtyStepIds.Add(step.id); RefreshWirePreview(step); SceneView.RepaintAll(); }
                            }
                        }

                        // Wire targets: position/rotation have no meaning — skip DrawDetailPanel.
                        if (_targets != null && step.wireConnect?.IsConfigured != true)
                            for (int i = 0; i < _targets.Length; i++)
                                if (_targets[i].def?.id == selEntry.id)
                                { DrawDetailPanel(ref _targets[i]); break; }
                        break;
                    }
                    case "confirm_action":
                    {
                        DrawUnifiedSectionHeader("CONFIRM CONTEXT", 0);
                        EditorGUILayout.LabelField(
                            "  User presses the Confirm button to complete this step.",
                            EditorStyles.miniLabel);
                        break;
                    }
                    case "confirm":
                    {
                        // Confirm-family inspection points — no tool, just position reference
                        DrawUnifiedSectionHeader($"OBSERVE CONTEXT ({selEntry.id})", 0);
                        EditorGUILayout.LabelField(
                            "  Camera must frame this location before Confirm unlocks. No tool required.",
                            EditorStyles.miniLabel);
                        EditorGUILayout.Space(4);
                        if (_targets != null)
                            for (int i = 0; i < _targets.Length; i++)
                                if (_targets[i].def?.id == selEntry.id)
                                { DrawDetailPanel(ref _targets[i]); break; }
                        break;
                    }
                    default: // toolAction, target
                    {
                        DrawUnifiedSectionHeader($"TOOL CONTEXT ({selEntry.id})", 0);
                        if (IsTaskEntryDirty(selEntry, step))
                        {
                            EditorGUILayout.BeginHorizontal();
                            var ds = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColDirty }, fontStyle = FontStyle.Bold };
                            EditorGUILayout.LabelField("● Unsaved Changes", ds);
                            if (GUILayout.Button("Revert", EditorStyles.miniButton, GUILayout.Width(52)))
                                RevertTargetEntry(selEntry.id);
                            EditorGUILayout.EndHorizontal();
                        }

                        // ── Tool picker ───────────────────────────────────────
                        // Find the requiredToolAction entry for this target (may be null if not yet set)
                        ToolActionDefinition taskAction = null;
                        if (step.requiredToolActions != null)
                            foreach (var a in step.requiredToolActions)
                                if (a?.targetId == selEntry.id) { taskAction = a; break; }

                        if (_pkg?.tools != null && _pkg.tools.Length > 0)
                        {
                            // Build parallel name/id arrays (index 0 = none)
                            var toolDefs  = _pkg.tools;
                            var toolNames = new string[toolDefs.Length + 1];
                            var toolIds   = new string[toolDefs.Length + 1];
                            toolNames[0] = "(none)";
                            toolIds[0]   = "";
                            int currentToolIdx = 0;
                            for (int ti = 0; ti < toolDefs.Length; ti++)
                            {
                                toolNames[ti + 1] = toolDefs[ti]?.name ?? toolDefs[ti]?.id ?? "?";
                                toolIds[ti + 1]   = toolDefs[ti]?.id ?? "";
                                if (taskAction != null && toolIds[ti + 1] == taskAction.toolId)
                                    currentToolIdx = ti + 1;
                            }

                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Tool:", EditorStyles.miniLabel, GUILayout.Width(32));
                            int newToolIdx = EditorGUILayout.Popup(currentToolIdx, toolNames);
                            EditorGUILayout.EndHorizontal();
                            if (EditorGUI.EndChangeCheck() && newToolIdx != currentToolIdx)
                            {
                                string pickedToolId = toolIds[newToolIdx];
                                if (taskAction == null)
                                {
                                    // No action entry yet — create one
                                    string actionId = $"action_{selEntry.id}";
                                    taskAction = new ToolActionDefinition
                                        { id = actionId, toolId = pickedToolId, targetId = selEntry.id };
                                    var aList = new System.Collections.Generic.List<ToolActionDefinition>(
                                        step.requiredToolActions ?? System.Array.Empty<ToolActionDefinition>());
                                    aList.Add(taskAction);
                                    step.requiredToolActions = aList.ToArray();
                                }
                                else
                                {
                                    taskAction.toolId = pickedToolId;
                                }
                                _dirtyStepIds.Add(step.id);
                                BuildTargetToolMap();
                                if (_targets != null)
                                    for (int ti = 0; ti < _targets.Length; ti++)
                                        if (_targets[ti].def?.id == selEntry.id)
                                        { RefreshToolPreview(ref _targets[ti]); break; }
                                Repaint();
                            }

                            // Show selected tool's category as context
                            if (currentToolIdx > 0)
                            {
                                var selTool = toolDefs[currentToolIdx - 1];
                                if (!string.IsNullOrEmpty(selTool?.category))
                                    EditorGUILayout.LabelField($"Category: {selTool.category}", EditorStyles.miniLabel);
                            }
                        }

                        DrawPersistentToolRemovalRows();

                        // Target transform detail
                        string toolTargetId = selEntry.id;
                        if (_targets != null)
                            for (int i = 0; i < _targets.Length; i++)
                                if (_targets[i].def?.id == toolTargetId)
                                { DrawDetailPanel(ref _targets[i]); break; }
                        break;
                    }
                }
            }
            else
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("  Click a task to view its details.",
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Task sequence helpers ─────────────────────────────────────────────

        private List<TaskOrderEntry> GetOrDeriveTaskOrder(StepDefinition step)
        {
            if (_cachedTaskOrderForStepId == step.id && _cachedTaskOrder != null)
                return _cachedTaskOrder;

            var family2    = step.ResolvedFamily;
            bool isConfirm2 = family2 == OSE.Content.StepFamily.Confirm;

            List<TaskOrderEntry> order;
            if (step.taskOrder != null && step.taskOrder.Length > 0)
            {
                order = new List<TaskOrderEntry>(step.taskOrder);
            }
            else
            {
                order = new List<TaskOrderEntry>();
                bool isWire  = family2 == OSE.Content.StepFamily.Connect;
                bool isPlace = family2 == OSE.Content.StepFamily.Place;

                if (step.requiredPartIds != null)
                {
                    foreach (var pid in step.requiredPartIds)
                        if (!string.IsNullOrEmpty(pid))
                            order.Add(new TaskOrderEntry { kind = "part", id = pid });
                }

                // Place-family targets are snap anchors, not user-facing tasks — omit them.
                // Confirm-family targets are inspection points — shown as "confirm" kind (no tool picker).
                // Use/Connect targets are tool interaction points.
                var coveredTargetIds = new HashSet<string>(StringComparer.Ordinal);
                if (!isPlace && step.targetIds != null)
                    foreach (var tid in step.targetIds)
                        if (!string.IsNullOrEmpty(tid))
                        {
                            string kind = isWire ? "wire" : isConfirm2 ? "confirm" : "target";
                            order.Add(new TaskOrderEntry { kind = kind, id = tid });
                            coveredTargetIds.Add(tid);
                        }

                // Only add toolAction entries whose target isn't already shown via targetIds
                if (step.requiredToolActions != null)
                    foreach (var a in step.requiredToolActions)
                        if (!string.IsNullOrEmpty(a?.id) && !coveredTargetIds.Contains(a.targetId ?? ""))
                            order.Add(new TaskOrderEntry { kind = "toolAction", id = a.id });
            }

            // Confirm-family steps always end with a button press — always append as display-only terminal task
            if (isConfirm2)
                order.Add(new TaskOrderEntry { kind = "confirm_action", id = "confirm" });

            _cachedTaskOrderForStepId = step.id;
            _cachedTaskOrder = order;
            return order;
        }

        private void InvalidateTaskOrderCache() { _cachedTaskOrder = null; _cachedTaskOrderForStepId = null; }

        /// <summary>Returns true when the item backing this task entry has in-memory unsaved edits.</summary>
        private bool IsTaskEntryDirty(TaskOrderEntry entry, StepDefinition step)
        {
            if (entry == null) return false;
            if (entry.kind == "part")
            {
                if (_parts != null)
                    for (int i = 0; i < _parts.Length; i++)
                        if (_parts[i].def?.id == entry.id) return _parts[i].isDirty;
                return false;
            }
            // wire, tool, target — check the backing target
            if (_targets != null)
                for (int i = 0; i < _targets.Length; i++)
                    if (_targets[i].def?.id == entry.id) return _targets[i].isDirty;
            // Wire steps also dirty when polarity/step fields changed
            if (entry.kind == "wire") return _dirtyStepIds.Contains(step?.id ?? "");
            return false;
        }

        /// <summary>
        /// Reloads a single part's placement data from disk and clears its dirty flag.
        /// Does not affect any other part or target.
        /// </summary>
        private void RevertPartEntry(string partId)
        {
            var fresh = PackageJsonUtils.LoadPackage(_pkgId);
            if (fresh == null || _parts == null) return;

            PartPreviewPlacement pp = null;
            if (fresh.previewConfig?.partPlacements != null)
                foreach (var p in fresh.previewConfig.partPlacements)
                    if (p?.partId == partId) { pp = p; break; }

            bool hasP = pp != null;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].def?.id != partId) continue;
                _parts[i].placement     = pp;
                _parts[i].hasPlacement  = hasP;
                _parts[i].startPosition = hasP ? PackageJsonUtils.ToVector3(pp.startPosition) : Vector3.zero;
                _parts[i].startRotation = hasP ? PackageJsonUtils.ToUnityQuaternion(pp.startRotation) : Quaternion.identity;
                _parts[i].startScale    = hasP ? PackageJsonUtils.ToVector3(pp.startScale)    : Vector3.one;
                _parts[i].playPosition  = hasP ? PackageJsonUtils.ToVector3(pp.playPosition)  : Vector3.zero;
                _parts[i].playRotation  = hasP ? PackageJsonUtils.ToUnityQuaternion(pp.playRotation) : Quaternion.identity;
                _parts[i].playScale     = hasP ? PackageJsonUtils.ToVector3(pp.playScale)     : Vector3.one;
                _parts[i].isDirty       = false;
                SyncPartMeshToActivePose(ref _parts[i]);
                break;
            }
            _undoStackParts.Clear();
            _redoStackParts.Clear();
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Reloads a single target's placement data from disk and clears its dirty flag.
        /// Does not affect any other part or target.
        /// </summary>
        private void RevertTargetEntry(string targetId)
        {
            var fresh = PackageJsonUtils.LoadPackage(_pkgId);
            if (fresh == null || _targets == null) return;

            TargetPreviewPlacement placement = null;
            if (fresh.previewConfig?.targetPlacements != null)
                foreach (var p in fresh.previewConfig.targetPlacements)
                    if (p?.targetId == targetId) { placement = p; break; }

            bool hasP = placement != null;
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i].def?.id != targetId) continue;
                _targets[i].placement    = placement;
                _targets[i].hasPlacement = hasP;
                _targets[i].position     = hasP ? PackageJsonUtils.ToVector3(placement.position)        : Vector3.zero;
                _targets[i].rotation     = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.rotation) : Quaternion.identity;
                _targets[i].scale        = hasP ? PackageJsonUtils.ToVector3(placement.scale)            : Vector3.one;
                _targets[i].portA        = hasP ? PackageJsonUtils.ToVector3(placement.portA)            : Vector3.zero;
                _targets[i].portB        = hasP ? PackageJsonUtils.ToVector3(placement.portB)            : Vector3.zero;
                _targets[i].isDirty      = false;
                break;
            }
            _undoStack.Clear();
            _redoStack.Clear();
            SceneView.RepaintAll();
            Repaint();
        }

        private static readonly Color _seqColorWire    = new Color(0.2f, 0.9f, 0.9f, 1f);
        private static readonly Color _seqColorTool    = new Color(1.0f, 0.6f, 0.1f, 1f);
        private static readonly Color _seqColorPart    = new Color(0.3f, 0.9f, 0.3f, 1f);
        private static readonly Color _seqColorTarget  = new Color(0.2f, 0.8f, 0.7f, 1f);
        private static readonly Color _seqColorObserve = new Color(0.8f, 0.5f, 1.0f, 1f);  // purple — observe/inspect
        private static readonly Color _seqColorConfirm = new Color(1.0f, 0.85f, 0.2f, 1f); // gold — confirm button press

        private void DrawTaskSequenceDragList(StepDefinition step, List<TaskOrderEntry> order)
        {
            // Rebuild the ReorderableList whenever the step changes or the list is null
            if (_taskSeqReorderList == null || _taskSeqReorderListForStepId != step.id)
            {
                _taskSeqReorderList = new ReorderableList(order, typeof(TaskOrderEntry),
                    draggable: true, displayHeader: false, displayAddButton: false, displayRemoveButton: false);

                _taskSeqReorderList.elementHeight = EditorGUIUtility.singleLineHeight + 2f;

                _taskSeqReorderList.drawElementBackgroundCallback = (rect, index, isActive, isFocused) =>
                {
                    bool isPrimary = index == _selectedTaskSeqIdx;
                    bool isMulti   = _multiSelectedTaskSeqIdxs.Count > 1 && _multiSelectedTaskSeqIdxs.Contains(index);
                    if (isPrimary)
                        EditorGUI.DrawRect(rect, new Color(0.25f, 0.50f, 0.90f, 0.35f));
                    else if (isMulti)
                        EditorGUI.DrawRect(rect, new Color(0.25f, 0.50f, 0.90f, 0.20f));
                };

                _taskSeqReorderList.drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index >= order.Count) return;
                    var entry = order[index];

                    // Sequence number
                    var numRect = new Rect(rect.x, rect.y + 1f, 22f, rect.height);
                    EditorGUI.LabelField(numRect, $"{index + 1}", EditorStyles.miniLabel);

                    // Type badge — colored label (not a button; whole row is the click target)
                    Color badgeCol = entry.kind switch
                    {
                        "wire"           => _seqColorWire,
                        "part"           => _seqColorPart,
                        "confirm"        => _seqColorObserve,
                        "confirm_action" => _seqColorConfirm,
                        _                => _seqColorTool,
                    };
                    string badge = entry.kind switch
                    {
                        "wire"           => "WIRE",
                        "part"           => "PART",
                        "confirm"        => "OBSERVE",
                        "confirm_action" => "CONFIRM",
                        _                => "TOOL",
                    };
                    var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal    = { textColor = badgeCol },
                        fontStyle = FontStyle.Bold,
                        fontSize  = 9,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    var badgeRect = new Rect(rect.x + 24f, rect.y + 1f, 52f, rect.height - 2f);
                    EditorGUI.LabelField(badgeRect, badge, badgeStyle);

                    // ID label + per-task dirty dot
                    bool entryDirty = IsTaskEntryDirty(entry, step);
                    float idW = entryDirty ? rect.width - 124f : rect.width - 110f;
                    var idRect = new Rect(rect.x + 80f, rect.y + 1f, idW, rect.height);
                    EditorGUI.LabelField(idRect, entry.id ?? "—", EditorStyles.miniLabel);
                    if (entryDirty)
                    {
                        var dotStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal    = { textColor = ColDirty },
                            fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleLeft,
                        };
                        EditorGUI.LabelField(new Rect(idRect.xMax + 2f, rect.y + 1f, 14f, rect.height), "●", dotStyle);
                    }

                    // Whole-row click (excluding × button) — MouseDown so it fires before
                    // onMouseUpCallback and can call Event.current.Use() to block it.
                    var rowClickRect = new Rect(rect.x, rect.y, rect.width - 26f, rect.height);
                    if (Event.current.type == EventType.MouseDown
                        && rowClickRect.Contains(Event.current.mousePosition))
                    {
                        bool ctrl  = Event.current.control;
                        bool shift = Event.current.shift;
                        _addTaskPicker = AddTaskPicker.None;

                        if (ctrl)
                        {
                            // Toggle individual row in/out of multi-selection
                            if (_multiSelectedTaskSeqIdxs.Contains(index))
                                _multiSelectedTaskSeqIdxs.Remove(index);
                            else
                                _multiSelectedTaskSeqIdxs.Add(index);
                            _selectedTaskSeqIdx = index;
                        }
                        else if (shift && _selectedTaskSeqIdx >= 0)
                        {
                            // Range-select from last primary to this row
                            int lo = Mathf.Min(_selectedTaskSeqIdx, index);
                            int hi = Mathf.Max(_selectedTaskSeqIdx, index);
                            _multiSelectedTaskSeqIdxs.Clear();
                            for (int j = lo; j <= hi; j++) _multiSelectedTaskSeqIdxs.Add(j);
                            _selectedTaskSeqIdx = index;
                        }
                        else
                        {
                            // Plain click — single select (toggle deselect)
                            _multiSelectedTaskSeqIdxs.Clear();
                            int newIdx = (_selectedTaskSeqIdx == index) ? -1 : index;
                            _selectedTaskSeqIdx = newIdx;
                            if (newIdx >= 0)
                                ApplyTaskEntrySelection(step, order[newIdx]);
                            else
                            { _selectedPartIdx = -1; _selectedIdx = -1; _activeTaskKind = null; }
                        }

                        // For multi-select, resolve part indices for batch editing
                        if (_multiSelectedTaskSeqIdxs.Count > 1)
                            ApplyTaskMultiSelection(order);

                        Event.current.Use();
                        SceneView.RepaintAll();
                        Repaint();
                    }

                    var removeRect = new Rect(rect.xMax - 22f, rect.y + 1f, 22f, rect.height - 2f);
                    if (GUI.Button(removeRect, "×", EditorStyles.miniButton))
                    {
                        order.RemoveAt(index);
                        if (_selectedTaskSeqIdx >= order.Count) _selectedTaskSeqIdx = order.Count - 1;
                        step.taskOrder = order.ToArray();
                        _cachedTaskOrder = order;
                        _dirtyTaskOrderStepIds.Add(step.id);
                        _dirtyStepIds.Add(step.id);
                        // Force list rebuild next frame
                        _taskSeqReorderListForStepId = null;
                        Repaint();
                    }
                };

                _taskSeqReorderList.onReorderCallbackWithDetails = (list, oldIdx, newIdx) =>
                {
                    step.taskOrder = order.ToArray();
                    _cachedTaskOrder = order;
                    _dirtyTaskOrderStepIds.Add(step.id);
                    _dirtyStepIds.Add(step.id);
                    // Keep selection tracking the moved item
                    if (_selectedTaskSeqIdx == oldIdx) _selectedTaskSeqIdx = newIdx;
                    _multiSelectedTaskSeqIdxs.Clear();
                };

                // No onMouseUpCallback — row selection is handled by MouseDown inside
                // drawElementCallback. Using MouseUp via the callback would fire on the
                // MouseUp following any click (even after we Used the MouseDown) and
                // overwrite _selectedTaskSeqIdx with the stale ReorderableList index.

                _taskSeqReorderListForStepId = step.id;
            }

            // Keep the internal list reference in sync (entries may have been added)
            if (_taskSeqReorderList.list != order)
                _taskSeqReorderList.list = order;

            _taskSeqReorderList.DoLayoutList();
        }

        // ── Context panel ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves and applies editor selection state for the given task order entry.
        /// Call this from click handlers (not from draw methods) so the scene view
        /// highlights and detail panel update on the same frame.
        /// </summary>
        private void ApplyTaskEntrySelection(StepDefinition step, TaskOrderEntry entry)
        {
            if (entry == null) return;
            _activeTaskKind = entry.kind;
            _poseSwitchCooldown = 3; // suppress false dirty from handle re-init after selection change
            switch (entry.kind)
            {
                case "part":
                    _selectedIdx = -1;
                    _multiSelected.Clear();
                    _multiSelectedParts.Clear();
                    if (_parts != null)
                    {
                        // Find the exact part; fall back to first if not matched
                        int pick = 0;
                        for (int i = 0; i < _parts.Length; i++)
                            if (_parts[i].def?.id == entry.id) { pick = i; break; }
                        _selectedPartIdx = pick;
                        _selectedPartId  = _parts[pick].def?.id;
                        SyncAllPartMeshesToActivePose();
                        var liveGO = FindLivePartGO(_selectedPartId);
                        if (liveGO != null) UnityEditor.Selection.activeGameObject = liveGO;
                    }
                    break;

                case "confirm_action":
                    // Terminal button-press task — no target, clear all gizmos and selection.
                    _selectedPartIdx = -1;
                    _selectedIdx     = -1;
                    _multiSelected.Clear();
                    _multiSelectedParts.Clear();
                    ClearToolPreview();
                    UnityEditor.Selection.activeGameObject = null;
                    break;

                default: // confirm (observe), toolAction, wire, target
                {
                    string targetId = entry.id;
                    // For toolAction, resolve through requiredToolActions to get the targetId
                    if (entry.kind == "toolAction" && step.requiredToolActions != null)
                        foreach (var a in step.requiredToolActions)
                            if (a?.id == entry.id) { targetId = a.targetId; break; }

                    _selectedPartIdx = -1;
                    _multiSelectedParts.Clear();
                    _multiSelected.Clear();
                    if (_targets != null && _targets.Length > 0)
                    {
                        // Find the exact target; -1 if not matched (no fallback to index 0).
                        _selectedIdx = -1;
                        for (int i = 0; i < _targets.Length; i++)
                            if (_targets[i].def?.id == targetId) { _selectedIdx = i; break; }
                        _selectedTargetId = _selectedIdx >= 0 ? _targets[_selectedIdx].def?.id : null;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Resolves multi-selected task sequence rows into part/target index sets
        /// for batch editing. Collects all parts and all targets from the selection
        /// regardless of kind, so the batch panel always has content.
        /// </summary>
        private void ApplyTaskMultiSelection(List<TaskOrderEntry> order)
        {
            _multiSelectedParts.Clear();
            _multiSelected.Clear();

            foreach (int taskIdx in _multiSelectedTaskSeqIdxs)
            {
                if (taskIdx < 0 || taskIdx >= order.Count) continue;
                var entry = order[taskIdx];

                if (entry.kind == "part" && _parts != null)
                {
                    for (int i = 0; i < _parts.Length; i++)
                    {
                        if (_parts[i].def?.id == entry.id)
                        {
                            _multiSelectedParts.Add(i);
                            _selectedPartIdx = i;
                            _selectedPartId  = _parts[i].def?.id;
                            break;
                        }
                    }
                }
                else if (_targets != null)
                {
                    // wire, toolAction, target, confirm — resolve to target index
                    string targetId = entry.id;
                    for (int i = 0; i < _targets.Length; i++)
                    {
                        if (_targets[i].def?.id == targetId)
                        {
                            _multiSelected.Add(i);
                            _selectedIdx = i;
                            _selectedTargetId = _targets[i].def?.id;
                            break;
                        }
                    }
                }
            }
        }

        private void DrawTaskContextPanel(StepDefinition step, TaskOrderEntry entry)
        {
            switch (entry.kind)
            {
                case "part":
                    DrawPartContextPanel(step, entry.id);
                    break;
                case "toolAction":
                    DrawToolActionContextPanel(step, entry.id);
                    break;
                case "wire":
                case "target":
                    DrawWireOrTargetContextPanel(step, entry.id, entry.kind == "wire");
                    break;
            }
        }

        private void DrawPartContextPanel(StepDefinition step, string partId)
        {
            DrawUnifiedSectionHeader($"PART: {partId}", 1);

            if (_parts != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    if (_parts[i].def?.id == partId)
                    {
                        DrawPartPoseToggle();
                        ServiceRegistry.TryGet<ISpawnerQueryService>(out var chkSpawner);
                        if (chkSpawner == null)
                            EditorGUILayout.HelpBox("PackagePartSpawner not in scene — part handles unavailable.", MessageType.Warning);
                        DrawPartRowsInline();
                        return;
                    }
                }
            }
            EditorGUILayout.LabelField($"  Part '{partId}' not in current step filter.", EditorStyles.miniLabel);
        }

        private void DrawToolActionContextPanel(StepDefinition step, string actionId)
        {
            // Resolve the action's targetId
            string targetId = null;
            if (step.requiredToolActions != null)
                foreach (var a in step.requiredToolActions)
                    if (a?.id == actionId) { targetId = a.targetId; break; }

            DrawUnifiedSectionHeader($"TOOL: {actionId}", 1);

            if (targetId == null)
            {
                EditorGUILayout.LabelField($"  Action '{actionId}' not found.", EditorStyles.miniLabel);
                return;
            }

            // Find target index in _targets[] and render (selection applied by click handler)
            if (_targets != null)
            {
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i].def?.id == targetId)
                    {
                        DrawTargetRowsInline();
                        DrawPersistentToolRemovalRows();
                        return;
                    }
                }
            }
            EditorGUILayout.LabelField($"  Target '{targetId}' not in current step filter.", EditorStyles.miniLabel);
        }

        private void DrawWireOrTargetContextPanel(StepDefinition step, string targetId, bool isWire)
        {
            int count = _targets?.Length ?? 0;
            DrawUnifiedSectionHeader(isWire ? $"WIRE CONNECTIONS ({count})" : $"TARGETS ({count})", count);

            // Selectable list — same pattern as DrawPartRowsInline / DrawTargetRowsInline
            DrawTargetRowsInline();

            // Wire payload details for the currently selected target
            if (isWire && _selectedIdx >= 0 && _selectedIdx < count
                && step.wireConnect?.IsConfigured == true)
            {
                string selTargetId = _targets[_selectedIdx].def?.id;
                WireConnectEntry wire = null;
                if (selTargetId != null && step.wireConnect.wires != null)
                    foreach (var w in step.wireConnect.wires)
                        if (string.Equals(w.targetId, selTargetId, StringComparison.Ordinal))
                        { wire = w; break; }

                if (wire != null)
                {
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Polarity A:", GUILayout.Width(74));
                    wire.portAPolarityType = EditorGUILayout.TextField(wire.portAPolarityType ?? "", GUILayout.Width(80));
                    EditorGUILayout.LabelField("Polarity B:", GUILayout.Width(74));
                    wire.portBPolarityType = EditorGUILayout.TextField(wire.portBPolarityType ?? "");
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Connector A:", GUILayout.Width(74));
                    wire.portAConnectorType = EditorGUILayout.TextField(wire.portAConnectorType ?? "", GUILayout.Width(80));
                    EditorGUILayout.LabelField("Connector B:", GUILayout.Width(74));
                    wire.portBConnectorType = EditorGUILayout.TextField(wire.portBConnectorType ?? "");
                    EditorGUILayout.EndHorizontal();
                    if (EditorGUI.EndChangeCheck()) _dirtyStepIds.Add(step.id);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.BeginHorizontal();
                    wire.polarityOrderMatters = EditorGUILayout.ToggleLeft(
                        "Polarity order matters", wire.polarityOrderMatters, EditorStyles.miniLabel,
                        GUILayout.Width(150));
                    if (step.wireConnect != null)
                        step.wireConnect.enforcePortOrder = EditorGUILayout.ToggleLeft(
                            "Enforce port order (A first)", step.wireConnect.enforcePortOrder,
                            EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                    if (EditorGUI.EndChangeCheck()) _dirtyStepIds.Add(step.id);

                    // Port positions — read/write directly from wire entry
                    EditorGUILayout.Space(2);
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 newA2 = EditorGUILayout.Vector3Field("Port A (local)", new Vector3(wire.portA.x, wire.portA.y, wire.portA.z));
                        Vector3 newB2 = EditorGUILayout.Vector3Field("Port B (local)", new Vector3(wire.portB.x, wire.portB.y, wire.portB.z));
                        if (EditorGUI.EndChangeCheck())
                        {
                            wire.portA = PackageJsonUtils.ToFloat3(newA2);
                            wire.portB = PackageJsonUtils.ToFloat3(newB2);
                            if (_targets != null && _selectedIdx >= 0 && _selectedIdx < _targets.Length)
                            { BeginEdit(); _targets[_selectedIdx].portA = newA2; _targets[_selectedIdx].portB = newB2; _targets[_selectedIdx].isDirty = true; EndEdit(); }
                            _dirtyStepIds.Add(step.id);
                            RefreshWirePreview(step);
                            SceneView.RepaintAll();
                        }
                    }

                    // Color + Radius + Subdivisions
                    EditorGUI.BeginChangeCheck();
                    Color wc2 = wire.color.a > 0
                        ? new Color(wire.color.r, wire.color.g, wire.color.b, wire.color.a)
                        : new Color(0.15f, 0.15f, 0.15f, 1f);
                    Color nc2 = EditorGUILayout.ColorField("Color", wc2);
                    wire.color = new SceneFloat4 { r = nc2.r, g = nc2.g, b = nc2.b, a = nc2.a };
                    float nw2 = EditorGUILayout.FloatField("Radius (m)", wire.radius > 0 ? wire.radius : 0.003f);
                    wire.radius = Mathf.Max(0f, nw2);
                    wire.subdivisions = Mathf.Max(1, EditorGUILayout.IntField("Subdivisions", wire.subdivisions < 1 ? 1 : wire.subdivisions));
                    float displaySag2 = wire.sag > 0f ? wire.sag : 1.0f;
                    float newSag2 = EditorGUILayout.Slider("Sag", displaySag2, 0.01f, 3.0f);
                    wire.sag = newSag2;
                    bool isLinear2 = string.Equals(wire.interpolation, "linear", StringComparison.OrdinalIgnoreCase);
                    int interpIdx2 = EditorGUILayout.Popup("Interpolation", isLinear2 ? 1 : 0, new[] { "Bezier", "Linear" });
                    wire.interpolation = interpIdx2 == 1 ? "linear" : "bezier";
                    if (EditorGUI.EndChangeCheck()) { _dirtyStepIds.Add(step.id); RefreshWirePreview(step); SceneView.RepaintAll(); }

                    EditorGUILayout.EndVertical();
                }
            }
        }

        private static string BuildTaskOrderJson(List<TaskOrderEntry> entries)
        {
            if (entries == null || entries.Count == 0) return "[]";
            var rows = new System.Text.StringBuilder();
            rows.Append("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) rows.Append(",\n        ");
                rows.Append($"{{\"kind\":\"{entries[i].kind}\",\"id\":\"{entries[i].id}\"}}");
            }
            rows.Append("]");
            return rows.ToString();
        }

        // ── Add-task inline pickers ───────────────────────────────────────────

        private void DrawAddPartPicker()
        {
            if (_pkg?.GetParts() == null) return;
            var step = _stepFilterIdx > 0 ? FindStep(_stepIds[_stepFilterIdx]) : null;
            var existing = new HashSet<string>(step?.requiredPartIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            var available = new List<PartDefinition>();
            foreach (var p in _pkg.GetParts())
                if (p != null && !string.IsNullOrEmpty(p.id) && !existing.Contains(p.id))
                    available.Add(p);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add Part to Step", EditorStyles.boldLabel);
            if (available.Count == 0)
            {
                EditorGUILayout.LabelField("  All parts already assigned.", EditorStyles.miniLabel);
            }
            else
            {
                string[] opts = available.Select(p => $"{p.id}{(string.IsNullOrEmpty(p.name) ? "" : " — " + p.name)}").ToArray();
                _addPickerPartIdx = Mathf.Clamp(_addPickerPartIdx, 0, opts.Length - 1);
                _addPickerPartIdx = EditorGUILayout.Popup("Part", _addPickerPartIdx, opts);
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(available.Count == 0);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                CommitAddPart(step, available[_addPickerPartIdx].id);
                _addTaskPicker = AddTaskPicker.None;
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _addTaskPicker = AddTaskPicker.None;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawAddToolTargetPicker()
        {
            if (_pkg?.GetTargets() == null || _pkg?.GetTools() == null) return;
            var step = _stepFilterIdx > 0 ? FindStep(_stepIds[_stepFilterIdx]) : null;
            var existingT = new HashSet<string>(step?.targetIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            var availTargets = new List<TargetDefinition>();
            foreach (var t in _pkg.GetTargets())
                if (t != null && !string.IsNullOrEmpty(t.id) && !existingT.Contains(t.id))
                    availTargets.Add(t);
            var allTools = _pkg.GetTools()?.Where(t => t != null && !string.IsNullOrEmpty(t.id)).ToArray() ?? System.Array.Empty<ToolDefinition>();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add Tool Target to Step", EditorStyles.boldLabel);
            if (availTargets.Count == 0)
            {
                EditorGUILayout.LabelField("  All targets already assigned.", EditorStyles.miniLabel);
            }
            else
            {
                string[] tOpts = availTargets.Select(t => t.id).ToArray();
                _addPickerTargetIdx = Mathf.Clamp(_addPickerTargetIdx, 0, tOpts.Length - 1);
                _addPickerTargetIdx = EditorGUILayout.Popup("Target", _addPickerTargetIdx, tOpts);
                if (allTools.Length > 0)
                {
                    string[] toolOpts = allTools.Select(t => $"{t.id}{(string.IsNullOrEmpty(t.name) ? "" : " — " + t.name)}").ToArray();
                    _addPickerToolIdx = Mathf.Clamp(_addPickerToolIdx, 0, toolOpts.Length - 1);
                    _addPickerToolIdx = EditorGUILayout.Popup("Tool", _addPickerToolIdx, toolOpts);
                }
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(availTargets.Count == 0 || allTools.Length == 0);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                CommitAddToolTarget(step, availTargets[_addPickerTargetIdx].id, allTools[_addPickerToolIdx].id);
                _addTaskPicker = AddTaskPicker.None;
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _addTaskPicker = AddTaskPicker.None;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawAddWirePicker()
        {
            if (_pkg?.GetTargets() == null) return;
            var step = _stepFilterIdx > 0 ? FindStep(_stepIds[_stepFilterIdx]) : null;
            var existingT = new HashSet<string>(step?.targetIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            var availTargets = new List<TargetDefinition>();
            foreach (var t in _pkg.GetTargets())
                if (t != null && !string.IsNullOrEmpty(t.id) && !existingT.Contains(t.id))
                    availTargets.Add(t);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add Wire Connection to Step", EditorStyles.boldLabel);
            if (availTargets.Count == 0)
            {
                EditorGUILayout.LabelField("  All targets already assigned.", EditorStyles.miniLabel);
            }
            else
            {
                string[] tOpts = availTargets.Select(t => t.id).ToArray();
                _addPickerTargetIdx = Mathf.Clamp(_addPickerTargetIdx, 0, tOpts.Length - 1);
                _addPickerTargetIdx = EditorGUILayout.Popup("Target", _addPickerTargetIdx, tOpts);
            }

            // Wire appearance
            _addPickerWireColor = EditorGUILayout.ColorField("Color", _addPickerWireColor);
            _addPickerWireRadius = Mathf.Max(0f, EditorGUILayout.FloatField("Radius (m)", _addPickerWireRadius > 0 ? _addPickerWireRadius : 0.003f));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Polarity A", GUILayout.Width(72));
            _addPickerPolarityA = EditorGUILayout.TextField(_addPickerPolarityA);
            EditorGUILayout.LabelField("Polarity B", GUILayout.Width(72));
            _addPickerPolarityB = EditorGUILayout.TextField(_addPickerPolarityB);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Connector A", GUILayout.Width(72));
            _addPickerConnectorA = EditorGUILayout.TextField(_addPickerConnectorA);
            EditorGUILayout.LabelField("Connector B", GUILayout.Width(72));
            _addPickerConnectorB = EditorGUILayout.TextField(_addPickerConnectorB);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(availTargets.Count == 0);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                CommitAddWire(step, availTargets[_addPickerTargetIdx].id,
                    _addPickerWireColor, _addPickerWireRadius,
                    _addPickerPolarityA, _addPickerPolarityB, _addPickerConnectorA, _addPickerConnectorB);
                _addTaskPicker = AddTaskPicker.None;
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _addTaskPicker = AddTaskPicker.None;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── Commit helpers (modify in-memory step data + mark dirty) ──────────

        private void CommitAddPart(StepDefinition step, string partId)
        {
            if (step == null) return;
            var list = new List<string>(step.requiredPartIds ?? System.Array.Empty<string>()) { partId };
            step.requiredPartIds = list.ToArray();
            var order = GetOrDeriveTaskOrder(step);
            order.Add(new TaskOrderEntry { kind = "part", id = partId });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            _dirtyStepIds.Add(step.id);
            BuildPartList();
            Repaint();
        }

        private void CommitAddToolTarget(StepDefinition step, string targetId, string toolId)
        {
            if (step == null) return;
            var tList = new List<string>(step.targetIds ?? System.Array.Empty<string>()) { targetId };
            step.targetIds = tList.ToArray();
            var actionId = $"action_{targetId}";
            var aList = new List<ToolActionDefinition>(step.requiredToolActions ?? System.Array.Empty<ToolActionDefinition>());
            aList.Add(new ToolActionDefinition { id = actionId, toolId = toolId, targetId = targetId });
            step.requiredToolActions = aList.ToArray();
            var order = GetOrDeriveTaskOrder(step);
            order.Add(new TaskOrderEntry { kind = "toolAction", id = actionId });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            UpdateActiveStep();
            _dirtyStepIds.Add(step.id);
            BuildTargetList();
            Repaint();
        }

        private void CommitAddWire(StepDefinition step, string targetId, Color color, float radius, string polA, string polB, string conA, string conB)
        {
            if (step == null) return;
            var tList = new List<string>(step.targetIds ?? System.Array.Empty<string>()) { targetId };
            step.targetIds = tList.ToArray();
            step.wireConnect ??= new StepWireConnectPayload();
            var wList = new List<WireConnectEntry>(step.wireConnect.wires ?? System.Array.Empty<WireConnectEntry>());
            wList.Add(new WireConnectEntry
            {
                targetId           = targetId,
                color              = new SceneFloat4 { r = color.r, g = color.g, b = color.b, a = color.a },
                radius             = radius > 0f ? radius : 0.003f,
                portAPolarityType  = string.IsNullOrWhiteSpace(polA) ? null : polA.Trim(),
                portBPolarityType  = string.IsNullOrWhiteSpace(polB) ? null : polB.Trim(),
                portAConnectorType = string.IsNullOrWhiteSpace(conA) ? null : conA.Trim(),
                portBConnectorType = string.IsNullOrWhiteSpace(conB) ? null : conB.Trim(),
            });
            step.wireConnect.wires = wList.ToArray();
            var order = GetOrDeriveTaskOrder(step);
            order.Add(new TaskOrderEntry { kind = "wire", id = targetId });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            UpdateActiveStep();
            _dirtyStepIds.Add(step.id);
            BuildTargetList();
            Repaint();
        }

        private bool DrawUnifiedSectionHeader(string title, int count, System.Action onAdd = null)
        {
            EditorGUILayout.Space(2);
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight + 4f));
            EditorGUI.DrawRect(r, new Color(0.18f, 0.18f, 0.18f, 1f));

            bool addClicked = false;
            if (onAdd != null)
            {
                const float btnW = 22f;
                Rect btnRect  = new Rect(r.xMax - btnW - 2f, r.y + 1f, btnW, r.height - 2f);
                Rect lblRect  = new Rect(r.x + 4f, r.y + 2f, r.width - btnW - 10f, r.height);
                GUI.Label(lblRect, title, EditorStyles.boldLabel);
                if (GUI.Button(btnRect, "+", EditorStyles.miniButton))
                {
                    onAdd();
                    addClicked = true;
                }
            }
            else
            {
                GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 8f, r.height), title, EditorStyles.boldLabel);
            }

            EditorGUILayout.Space(1);
            return addClicked;
        }

        private void DrawPartRowsInline()
        {
            if (_parts == null) return;
            var selBg   = new Color(0.25f, 0.50f, 0.90f, 0.35f);
            var multiBg = new Color(0.25f, 0.50f, 0.90f, 0.18f);

            for (int i = 0; i < _parts.Length; i++)
            {
                ref PartEditState p = ref _parts[i];
                Color col = p.isDirty ? ColDirty : p.hasPlacement ? ColAuthored : ColNoPlacement;

                bool isPrimary  = i == _selectedPartIdx;
                bool isInMulti  = _multiSelectedParts.Count > 1 && _multiSelectedParts.Contains(i);
                bool isSelected = isPrimary || isInMulti;

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
                string badge = p.isDirty ? " ●" : p.hasPlacement ? "" : " ○";
                Rect labelRect = EditorGUILayout.GetControlRect();
                EditorGUI.LabelField(labelRect, $"  {p.def.id}{badge}", style);

                if (labelRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown)
                {
                    bool ctrl  = Event.current.control;
                    bool shift = Event.current.shift;

                    // Clear target selection — parts and targets are mutually exclusive
                    _selectedIdx = -1;
                    _multiSelected.Clear();

                    if (ctrl)
                    {
                        if (_multiSelectedParts.Contains(i)) _multiSelectedParts.Remove(i);
                        else { _multiSelectedParts.Add(i); _selectedPartIdx = i; _selectedPartId = p.def.id; }
                    }
                    else if (shift && _selectedPartIdx >= 0)
                    {
                        int lo = Mathf.Min(_selectedPartIdx, i);
                        int hi = Mathf.Max(_selectedPartIdx, i);
                        _multiSelectedParts.Clear();
                        for (int j = lo; j <= hi; j++) _multiSelectedParts.Add(j);
                        _selectedPartIdx = i; _selectedPartId = p.def.id;
                    }
                    else
                    {
                        _multiSelectedParts.Clear();
                        _selectedPartIdx = i;
                        _selectedPartId  = p.def.id;
                        // Select live GO so it highlights in hierarchy/scene (mirrors PPAW)
                        var clickedGO = FindLivePartGO(p.def.id);
                        if (clickedGO != null) Selection.activeGameObject = clickedGO;
                    }

                    if (_selectedPartIdx >= 0 && _selectedPartIdx < _parts.Length)
                        SyncAllPartMeshesToActivePose();
                    SceneView.RepaintAll();
                    Event.current.Use();
                    Repaint();
                }
            }
        }

        private void DrawPersistentToolRemovalRows()
        {
            if (_stepFilterIdx <= 0 || _stepFilterIdx >= _stepIds.Length) return;
            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            var removeIds = step.removePersistentToolIds ?? Array.Empty<string>();

            // Only show the label when there are entries or candidates to add.
            var activePersistent = GetActivePersistentToolIds(step);
            foreach (string rid in removeIds) activePersistent.Remove(rid);
            if (removeIds.Length == 0 && activePersistent.Count == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Removes persistent tools at start of step:", EditorStyles.miniLabel);

            string toRemove = null;
            foreach (string rid in removeIds)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"  · {FindToolName(rid)}", EditorStyles.miniLabel);
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(18)))
                    toRemove = rid;
                EditorGUILayout.EndHorizontal();
            }
            if (toRemove != null)
            {
                step.removePersistentToolIds = System.Array.FindAll(removeIds, r => r != toRemove);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }

            if (activePersistent.Count > 0 && GUILayout.Button("+ Add removal", EditorStyles.miniButton))
            {
                var menu = new GenericMenu();
                foreach (string toolId in activePersistent)
                {
                    string capturedId   = toolId;
                    string capturedName = FindToolName(toolId);
                    menu.AddItem(new GUIContent(capturedName), false, () =>
                    {
                        var newList = new List<string>(step.removePersistentToolIds ?? Array.Empty<string>());
                        newList.Add(capturedId);
                        step.removePersistentToolIds = newList.ToArray();
                        _dirtyStepIds.Add(step.id);
                        Repaint();
                    });
                }
                menu.ShowAsContext();
            }
        }

        /// <param name="taskSelectedIdx">
        /// When >= 0, this index is used as the primary selection highlight instead of
        /// <see cref="_selectedIdx"/>. Used by the wire context so the highlight is always
        /// driven by the task-sequence entry rather than by independent click state.
        /// </param>
        private void DrawTargetRowsInline(int taskSelectedIdx = -1)
        {
            if (_targets == null) return;
            var selBg   = new Color(0.25f, 0.50f, 0.90f, 0.35f);
            var multiBg = new Color(0.25f, 0.50f, 0.90f, 0.18f);

            int effectiveSelected = taskSelectedIdx >= 0 ? taskSelectedIdx : _selectedIdx;

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                Color col = t.isDirty ? ColDirty : t.hasPlacement ? ColAuthored : ColNoPlacement;

                bool isPrimary  = i == effectiveSelected;
                bool isInMulti  = _multiSelected.Count > 1 && _multiSelected.Contains(i);
                bool isSelected = isPrimary || isInMulti;

                var style = new GUIStyle(isSelected ? EditorStyles.boldLabel : EditorStyles.label)
                {
                    normal  = { textColor = col },
                    focused = { textColor = col }
                };

                string badge     = t.isDirty ? " ●" : t.hasPlacement ? "" : " ○";
                string toolBadge = (_targetToolMap != null && _targetToolMap.TryGetValue(t.def.id, out string tn))
                                    ? $"  [{tn}]" : "";
                string portBadge = (t.portA.sqrMagnitude > 0.00001f || t.portB.sqrMagnitude > 0.00001f) ? "  ↔"
                                 : (t.weldAxis.sqrMagnitude > 0.001f) ? "  →" : "";

                // Use GetControlRect + MouseDown (same as DrawPartRowsInline) so clicks are
                // not consumed by the ReorderableList above this content in the scrollview.
                Rect labelRect = EditorGUILayout.GetControlRect();
                if (isSelected) EditorGUI.DrawRect(labelRect, isPrimary ? selBg : multiBg);
                EditorGUI.LabelField(labelRect, $"  {t.def.id}{toolBadge}{portBadge}{badge}", style);

                if (labelRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown)
                {
                    bool ctrl  = Event.current.control;
                    bool shift = Event.current.shift;

                    // Clear part selection — mutually exclusive
                    _selectedPartIdx = -1;
                    _multiSelectedParts.Clear();

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
                    _selectedTargetId  = (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                        ? _targets[_selectedIdx].def.id : null;
                    if (_multiSelected.Count <= 1 && _selectedIdx >= 0 && _selectedIdx < _targets.Length)
                        RefreshToolPreview(ref _targets[_selectedIdx]);
                    SceneView.RepaintAll();
                    Event.current.Use();
                    Repaint();
                }
            }
        }

        /// <summary>
        /// Decides what to render in the pinned bottom edit area.
        /// When a task sequence entry is selected, it drives the panel (wire → target detail,
        /// part → part detail). Otherwise falls back to direct _selectedIdx / _selectedPartIdx.
        /// This avoids the _selectedPartIdx > _selectedIdx priority bug.
        /// </summary>
        private void DrawBottomEditPanel()
        {
            // ── Task-sequence-driven (authoritative when a task is selected) ──────
            if (_selectedTaskSeqIdx >= 0 && _stepFilterIdx > 0
                && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var step  = FindStep(_stepIds[_stepFilterIdx]);
                var order = step != null ? GetOrDeriveTaskOrder(step) : null;
                if (order != null && _selectedTaskSeqIdx < order.Count)
                {
                    var entry = order[_selectedTaskSeqIdx];
                    switch (entry.kind)
                    {
                        case "part":
                        {
                            if (_parts != null)
                                for (int i = 0; i < _parts.Length; i++)
                                    if (_parts[i].def?.id == entry.id)
                                    { DrawPartDetailPanel(ref _parts[i]); return; }
                            break;
                        }
                        default: // wire, toolAction, target
                        {
                            string targetId = entry.id;
                            if (entry.kind == "toolAction" && step?.requiredToolActions != null)
                                foreach (var a in step.requiredToolActions)
                                    if (a?.id == entry.id) { targetId = a.targetId; break; }
                            if (_targets != null)
                                for (int i = 0; i < _targets.Length; i++)
                                    if (_targets[i].def?.id == targetId)
                                    { DrawDetailPanel(ref _targets[i]); return; }
                            break;
                        }
                    }
                }
            }

            // ── Fallback: direct selection state (no task sequence active) ────────
            if (_multiSelectedParts.Count > 1)
                DrawPartBatchPanel();
            else if (_selectedPartIdx >= 0 && _parts != null && _selectedPartIdx < _parts.Length)
                DrawPartDetailPanel(ref _parts[_selectedPartIdx]);
            else if (_multiSelected.Count > 1)
                DrawBatchPanel();
            else if (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                DrawDetailPanel(ref _targets[_selectedIdx]);
            else
                EditorGUILayout.LabelField("Select a part or target in the sequence above.",
                    EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawUnifiedActions()
        {
            bool anyDirty = AnyDirty();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!anyDirty);
            GUI.backgroundColor = anyDirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (GUILayout.Button("Write to machine.json", GUILayout.Height(26))) WriteJson();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("↺", EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(26)))
                RevertAllChanges();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Extract from GLB", EditorStyles.miniButton)) ExtractFromGlbAnchors();
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write", EditorStyles.miniButton)) RevertFromBackup();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Frame in Scene", EditorStyles.miniButton)) FrameInScene();
            if (GUILayout.Button("Sync Rotations", EditorStyles.miniButton)) SyncAllToolRotationsFromPlacements();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPartModelPreview(ref PartEditState p)
        {
            if (p.def == null || string.IsNullOrEmpty(_pkgId))
                return;

            string partsFolder = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/";
            string glbFile     = ResolvePartAssetRef(p.def);
            if (string.IsNullOrEmpty(glbFile)) return;

            // Lazy-create or replace when part changes
            if (_partPreviewId != p.def.id)
            {
                _partPreview?.Dispose();
                _partPreview   = null;
                _partPreviewId = null;
                string assetPath = partsFolder + glbFile;
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                {
                    _partPreview   = new PartModelPreviewRenderer(assetPath, p.startRotation.eulerAngles);
                    _partPreviewId = p.def.id;
                }
            }
            if (_partPreview == null) return;

            // ── Header row: label + unit toggle ──────────────────────────────
            bool useMm = EditorPrefs.GetString(PrefDimUnit, "mm") == "mm";
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Model Preview", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(useMm,  "mm", EditorStyles.miniButtonLeft,  GUILayout.Width(32)))
                { if (!useMm) { EditorPrefs.SetString(PrefDimUnit, "mm"); Repaint(); } }
            if (GUILayout.Toggle(!useMm, "in", EditorStyles.miniButtonRight, GUILayout.Width(32)))
                { if (useMm) { EditorPrefs.SetString(PrefDimUnit, "in"); Repaint(); } }
            EditorGUILayout.EndHorizontal();

            // ── 3D preview rect ───────────────────────────────────────────────
            const float PreviewHeight = 220f;
            Rect previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(PreviewHeight), GUILayout.ExpandWidth(true));

            // Orbit on mouse drag inside rect
            var ev = Event.current;
            if (ev.type == EventType.MouseDrag && previewRect.Contains(ev.mousePosition))
            {
                _partPreview.Orbit(ev.delta);
                ev.Use();
                Repaint();
            }

            bool needsRepaint = _partPreview.Draw(previewRect, useMm);
            if (needsRepaint) Repaint();

            // ── Euler rotation fields ─────────────────────────────────────────
            EditorGUILayout.Space(2);
            EditorGUI.BeginChangeCheck();
            Vector3 euler    = _partPreview.ModelEuler;
            Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation", euler);
            if (EditorGUI.EndChangeCheck())
            {
                _partPreview.SetModelEuler(newEuler);
                Repaint();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset View", EditorStyles.miniButton, GUILayout.Width(68)))
            {
                _partPreview.ResetView();
                Repaint();
            }
            if (GUILayout.Button("Reset Rotation", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                _partPreview.SetModelEuler(Vector3.zero);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // ── Confirm button ────────────────────────────────────────────────
            EditorGUILayout.Space(2);
            if (GUILayout.Button("Confirm Orientation", GUILayout.Height(22)))
            {
                Quaternion confirmed = Quaternion.Euler(_partPreview.ModelEuler);
                p.startRotation = confirmed;
                p.isDirty       = true;
                // Find the part by id and sync (p is a ref but _selectedPartIdx may differ)
                if (_parts != null)
                    for (int k = 0; k < _parts.Length; k++)
                        if (_parts[k].def?.id == p.def.id)
                        { SyncPartMeshToActivePose(ref _parts[k]); break; }
                SceneView.RepaintAll();
            }
            EditorGUILayout.Space(6);
        }

        private void DrawPartDetailPanel(ref PartEditState p)
        {
            // ── Asset Ref field ───────────────────────────────────────────────
            if (p.def != null && !string.IsNullOrEmpty(_pkgId))
            {
                string partsFolder = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/";
                string explicit_    = p.def.assetRef ?? "";
                bool isAutoDiscovered = string.IsNullOrEmpty(explicit_);
                string resolvedFile;
                if (!isAutoDiscovered)
                {
                    resolvedFile = explicit_;
                }
                else
                {
                    var res = _assetResolver.Resolve(p.def.id);
                    resolvedFile = res.IsResolved ? Path.GetFileName(res.AssetPath) : null;
                }
                isAutoDiscovered = isAutoDiscovered && !string.IsNullOrEmpty(resolvedFile);

                string assetPath = !string.IsNullOrEmpty(resolvedFile)
                    ? partsFolder + resolvedFile
                    : null;
                var currentObj = assetPath != null
                    ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath)
                    : null;

                // Label shows "(auto)" when discovered via filename convention, not explicit.
                string fieldLabel = isAutoDiscovered
                    ? "Model Asset (auto)"
                    : "Model Asset";
                string tooltip = isAutoDiscovered
                    ? "Resolved by filename convention. Drag a GLB here to set an explicit reference."
                    : "Explicit assetRef in machine.json.";

                EditorGUI.BeginChangeCheck();
                var newObj = EditorGUILayout.ObjectField(
                    new GUIContent(fieldLabel, tooltip),
                    currentObj, typeof(UnityEngine.Object), false);
                if (EditorGUI.EndChangeCheck() && newObj != currentObj)
                {
                    string newPath = newObj != null ? AssetDatabase.GetAssetPath(newObj) : null;
                    string newFile = newPath != null ? Path.GetFileName(newPath) : "";
                    if (newFile != explicit_)
                    {
                        p.def.assetRef = newFile;
                        _dirtyPartAssetRefIds.Add(p.def.id);
                        _partPreview?.Dispose();
                        _partPreview   = null;
                        _partPreviewId = null;
                        WriteJson();
                    }
                }
                EditorGUILayout.Space(4);
            }

            DrawPartModelPreview(ref p);

            if (_editPlayPose)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPlayPos = EditorGUILayout.Vector3Field("Play Position", p.playPosition);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.playPosition = newPlayPos; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 playEuler = EditorGUILayout.Vector3Field("Play Rotation", p.playRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.playRotation = Quaternion.Euler(playEuler); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPlayScale = EditorGUILayout.Vector3Field("Play Scale", p.playScale);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.playScale = newPlayScale; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newStartPos = EditorGUILayout.Vector3Field("Start Position", p.startPosition);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startPosition = newStartPos; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 startEuler = EditorGUILayout.Vector3Field("Start Rotation", p.startRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startRotation = Quaternion.Euler(startEuler); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newStartScale = EditorGUILayout.Vector3Field("Start Scale", p.startScale);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startScale = newStartScale; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_undoStackParts.Count == 0);
            if (GUILayout.Button("Undo", EditorStyles.miniButtonLeft,  GUILayout.Width(60))) UndoPartPose();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redoStackParts.Count == 0);
            if (GUILayout.Button("Redo", EditorStyles.miniButtonRight, GUILayout.Width(60))) RedoPartPose();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPartBatchPanel()
        {
            int count = _multiSelectedParts.Count;
            string poseLabel = _editPlayPose ? "Play Pose" : "Start Pose";
            EditorGUILayout.LabelField($"Batch edit — {count} parts  ({poseLabel})", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Values shown are from the primary (last-clicked) part.\n" +
                "Changing a field sets that exact value on ALL selected parts.",
                MessageType.None);
            EditorGUILayout.Space(4);
            if (_selectedPartIdx < 0 || _selectedPartIdx >= _parts.Length) return;
            ref PartEditState rep = ref _parts[_selectedPartIdx];

            // ── Position (absolute, per-axis) ────────────────────────────────
            EditorGUILayout.LabelField("Position (all selected)", EditorStyles.boldLabel);
            Vector3 repPos = _editPlayPose ? rep.playPosition : rep.startPosition;

            EditorGUI.BeginChangeCheck();
            float batchX = EditorGUILayout.FloatField("X", repPos.x);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.x  = batchX;
                    else               p2.startPosition.x = batchX;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchY = EditorGUILayout.FloatField("Y", repPos.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.y  = batchY;
                    else               p2.startPosition.y = batchY;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchZ = EditorGUILayout.FloatField("Z", repPos.z);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.z  = batchZ;
                    else               p2.startPosition.z = batchZ;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Rotation (absolute) ──────────────────────────────────────────
            EditorGUILayout.LabelField("Rotation (all selected)", EditorStyles.boldLabel);
            Quaternion repRot = _editPlayPose ? rep.playRotation : rep.startRotation;
            EditorGUI.BeginChangeCheck();
            Vector3 batchEuler = EditorGUILayout.Vector3Field("Euler", repRot.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion batchRot = Quaternion.Euler(batchEuler);
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playRotation  = batchRot;
                    else               p2.startRotation = batchRot;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Scale (absolute) ─────────────────────────────────────────────
            EditorGUILayout.LabelField("Scale (all selected)", EditorStyles.boldLabel);
            Vector3 repScale = _editPlayPose ? rep.playScale : rep.startScale;
            EditorGUI.BeginChangeCheck();
            Vector3 batchScale = EditorGUILayout.Vector3Field("Scale", repScale);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playScale  = batchScale;
                    else               p2.startScale = batchScale;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(8);

            // ── Position offset (delta) ──────────────────────────────────────
            EditorGUILayout.LabelField("Position Offset (delta)", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            float dx = EditorGUILayout.FloatField("Delta X", 0f);
            if (EditorGUI.EndChangeCheck() && dx != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.x  += dx;
                    else               p2.startPosition.x += dx;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float dy = EditorGUILayout.FloatField("Delta Y", 0f);
            if (EditorGUI.EndChangeCheck() && dy != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.y  += dy;
                    else               p2.startPosition.y += dy;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float dz = EditorGUILayout.FloatField("Delta Z", 0f);
            if (EditorGUI.EndChangeCheck() && dz != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editPlayPose) p2.playPosition.z  += dz;
                    else               p2.startPosition.z += dz;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
        }

        // ── Parts tab — BuildPartList + sync ──────────────────────────────────

        private void BuildPartList()
        {
            if (_pkg?.parts == null) { _parts = Array.Empty<PartEditState>(); return; }

            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var step = FindStep(_stepIds[_stepFilterIdx]);
                // Always set filterIds when a step is selected — null means "show all", but a step
                // with no requiredPartIds (e.g. OBSERVE/CONFIRM steps) should show zero parts.
                filterIds = step?.requiredPartIds != null
                    ? new HashSet<string>(step.requiredPartIds, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var list = new List<PartEditState>();
            foreach (var def in _pkg.parts)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

                PartPreviewPlacement pp = FindPartPlacement(def.id);
                bool hasP = pp != null;

                var state = new PartEditState
                {
                    def           = def,
                    placement     = pp,
                    hasPlacement  = hasP,
                    startPosition = hasP ? PackageJsonUtils.ToVector3(pp.startPosition) : Vector3.zero,
                    startRotation = hasP ? PackageJsonUtils.ToUnityQuaternion(pp.startRotation) : Quaternion.identity,
                    startScale    = hasP ? PackageJsonUtils.ToVector3(pp.startScale)    : Vector3.one,
                    playPosition  = hasP ? PackageJsonUtils.ToVector3(pp.playPosition)  : Vector3.zero,
                    playRotation  = hasP ? PackageJsonUtils.ToUnityQuaternion(pp.playRotation) : Quaternion.identity,
                    playScale     = hasP ? PackageJsonUtils.ToVector3(pp.playScale)     : Vector3.one,
                    color         = hasP ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a) : ColAuthored,
                    isDirty       = false,
                };
                if (state.startScale.sqrMagnitude < 0.00001f) state.startScale = Vector3.one;
                if (state.playScale.sqrMagnitude  < 0.00001f) state.playScale  = Vector3.one;
                list.Add(state);
            }

            // Restore selection across rebuilds by matching part ID
            string prevSelectedId = (_selectedPartIdx >= 0 && _parts != null && _selectedPartIdx < _parts.Length)
                ? _parts[_selectedPartIdx].def.id : _selectedPartId;

            _parts          = list.ToArray();
            _selectedPartIdx = -1;
            if (prevSelectedId != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                    if (string.Equals(_parts[i].def.id, prevSelectedId, StringComparison.OrdinalIgnoreCase))
                    { _selectedPartIdx = i; break; }
            }
            _multiSelectedParts.Clear();
        }

        // ── Live spawner GO helpers (mirrors PPAW) ────────────────────────────

        private GameObject FindLivePartGO(string partId)
        {
            if (!ServiceRegistry.TryGet<ISpawnerQueryService>(out var s) || s?.SpawnedParts == null)
                return null;
            foreach (var go in s.SpawnedParts)
                if (go != null && go.name == partId) return go;
            return null;
        }

        /// <summary>
        /// Moves both the live spawner GO and the TTAW preview GO to the current
        /// active pose.  State is always the source of truth.
        /// </summary>
        /// <summary>
        /// Returns the display position/rotation/scale for a part, respecting the
        /// step-filter context cached by the last <see cref="RespawnScene"/> call.
        ///
        /// When a step is selected:
        ///   - Past parts  → always playPosition  (already assembled)
        ///   - Current part → startPosition unless <c>_editPlayPose</c> or part is a
        ///                    subassembly member (which arrives pre-assembled)
        ///   - Future parts → caller should skip; returns false
        ///
        /// When no step selected (All Steps mode): always obeys <c>_editPlayPose</c>.
        /// </summary>
        private bool TryGetStepAwarePose(ref PartEditState p,
            out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            string pid = p.def.id;

            if (_sceneBuildStepActive)
            {
                bool inMap = _sceneBuildPartStepSeq.TryGetValue(pid, out int placedAt);

                if (inMap && placedAt > _sceneBuildCurrentSeq)
                {
                    // Future part — must not be shown in the scene
                    pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
                    return false;
                }

                bool useStart = inMap
                    && placedAt == _sceneBuildCurrentSeq
                    && !_sceneBuildCurrentSubassembly.Contains(pid)
                    && !_editPlayPose;

                pos = useStart ? p.startPosition : p.playPosition;
                rot = useStart ? p.startRotation  : p.playRotation;
                scl = useStart ? p.startScale     : p.playScale;
            }
            else
            {
                pos = _editPlayPose ? p.playPosition : p.startPosition;
                rot = _editPlayPose ? p.playRotation  : p.startRotation;
                scl = _editPlayPose ? p.playScale     : p.startScale;
            }

            if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
            return true;
        }

        private void SyncPartMeshToActivePose(ref PartEditState p)
        {
            if (!TryGetStepAwarePose(ref p, out Vector3 pos, out Quaternion rot, out Vector3 scl))
                return; // future part — leave hidden

            var liveGO = FindLivePartGO(p.def.id);
            if (liveGO != null)
            {
                liveGO.transform.localPosition = pos;
                liveGO.transform.localRotation = rot;
                if (scl.sqrMagnitude > 0.00001f) liveGO.transform.localScale = scl;
            }
        }

        private void SyncAllPartMeshesToActivePose()
        {
            if (_parts == null) return;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (!_parts[i].hasPlacement) continue;

                // Compute step-aware pose; hide future parts that don't belong to this step.
                if (!TryGetStepAwarePose(ref _parts[i], out Vector3 pos, out Quaternion rot, out Vector3 scl))
                {
                    var futureGO = FindLivePartGO(_parts[i].def.id);
                    if (futureGO != null && futureGO.activeSelf)
                        futureGO.SetActive(false);
                    continue;
                }

                var liveGO = FindLivePartGO(_parts[i].def.id);
                if (liveGO != null && !liveGO.activeSelf)
                    liveGO.SetActive(true);

                SyncPartMeshToActivePose(ref _parts[i]);
            }
        }

        // ── Parts tab — Undo / Redo ───────────────────────────────────────────

        private PartSnapshot CapturePartSnapshot(ref PartEditState p) => new()
        {
            startPosition = p.startPosition,
            startRotation = p.startRotation,
            startScale    = p.startScale,
            playPosition  = p.playPosition,
            playRotation  = p.playRotation,
            playScale     = p.playScale,
        };

        private void BeginPartEdit(int forIdx)
        {
            if (_snapshotPendingPart || forIdx < 0 || _parts == null || forIdx >= _parts.Length) return;
            _undoStackParts.Add((forIdx, CapturePartSnapshot(ref _parts[forIdx])));
            if (_undoStackParts.Count > MaxUndoHistory) _undoStackParts.RemoveAt(0);
            _redoStackParts.Clear();
            _snapshotPendingPart = true;
        }

        private void EndPartEdit() => _snapshotPendingPart = false;

        private void UndoPartPose()
        {
            if (_undoStackParts.Count == 0 || _parts == null) return;
            var (idx, prev) = _undoStackParts[_undoStackParts.Count - 1];
            _undoStackParts.RemoveAt(_undoStackParts.Count - 1);
            if (idx < _parts.Length)
            {
                _redoStackParts.Add((idx, CapturePartSnapshot(ref _parts[idx])));
                ApplyPartSnapshot(idx, prev);
            }
        }

        private void RedoPartPose()
        {
            if (_redoStackParts.Count == 0 || _parts == null) return;
            var (idx, next) = _redoStackParts[_redoStackParts.Count - 1];
            _redoStackParts.RemoveAt(_redoStackParts.Count - 1);
            if (idx < _parts.Length)
            {
                _undoStackParts.Add((idx, CapturePartSnapshot(ref _parts[idx])));
                ApplyPartSnapshot(idx, next);
            }
        }

        private void ApplyPartSnapshot(int idx, PartSnapshot s)
        {
            ref PartEditState p = ref _parts[idx];
            p.startPosition = s.startPosition;
            p.startRotation = s.startRotation;
            p.startScale    = s.startScale;
            p.playPosition  = s.playPosition;
            p.playRotation  = s.playRotation;
            p.playScale     = s.playScale;
            p.isDirty            = true;
            _snapshotPendingPart = false;
            SyncPartMeshToActivePose(ref p);
            Repaint();
            SceneView.RepaintAll();
        }

        // ── Parts tab — SceneView handles ─────────────────────────────────────

        private void DrawPartSceneHandles(SceneView sv)
        {
            if (_parts == null || _parts.Length == 0) return;

            // Use spawner.PreviewRoot as the coordinate root — exactly as PPAW does.
            ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner);
            Transform root = spawner?.PreviewRoot;

            // Tick down the pose-switch cooldown.  While active, suppress the native
            // polling and custom handle change-detection so the pose flip doesn't
            // produce false dirty flags.
            bool poseCooldownActive = _poseSwitchCooldown > 0;
            if (poseCooldownActive && Event.current.type == EventType.Repaint)
                _poseSwitchCooldown--;

            if (Event.current.type == EventType.MouseUp) EndPartEdit();

            // F key → frame on selected part
            if (_selectedPartIdx >= 0 && _selectedPartIdx < _parts.Length
                && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F)
            {
                var liveGO = FindLivePartGO(_parts[_selectedPartIdx].def.id);
                if (liveGO != null) { Selection.activeGameObject = liveGO; sv.FrameSelected(); }
                Event.current.Use();
            }

            // Scene selection → window selection sync (click GO in Hierarchy to select it).
            // Skip when multi-selection is active — the user's batch selection takes priority.
            if (spawner?.SpawnedParts != null && _multiSelectedParts.Count <= 1 && _multiSelectedTaskSeqIdxs.Count <= 1)
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
                            if (_selectedPartIdx != si)
                            {
                                _selectedPartIdx = si;
                                _selectedPartId  = liveGO.name;
                                _multiSelectedParts.Clear();
                                // Clear target selection
                                _selectedIdx = -1;
                                _multiSelected.Clear();
                                Repaint();
                            }
                            break;
                        }
                        break;
                    }
                }
            }

            // Indicator dots — live GO world position, fallback to state via root.
            // Show dots for all parts in the active step (the _parts array is already
            // filtered to the step's requiredPartIds). When no step is selected
            // (_stepFilterIdx <= 0), hide dots to keep the scene uncluttered.
            bool hasStep = _stepFilterIdx > 0;
            if (root != null && hasStep)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    ref PartEditState p = ref _parts[i];
                    if (!p.hasPlacement) continue;

                    var liveGO = FindLivePartGO(p.def.id);
                    Vector3 worldPos = liveGO != null
                        ? liveGO.transform.position
                        : root.TransformPoint(_editPlayPose ? p.playPosition : p.startPosition);
                    float size = HandleUtility.GetHandleSize(worldPos) * 0.08f;

                    bool isSelected = i == _selectedPartIdx
                                   || (_multiSelectedParts.Count > 1 && _multiSelectedParts.Contains(i));
                    Color col = isSelected     ? ColSelected
                              : p.isDirty      ? ColDirty
                              : p.hasPlacement ? ColAuthored
                              :                  ColNoPlacement;
                    Handles.color = col;
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }
            }

            // Hide Unity's native transform gizmo when multi-selecting parts so only
            // our custom Handles.PositionHandle / RotationHandle is visible.
            bool isMultiPart = _multiSelectedParts.Count > 1;
            if (Tools.hidden != isMultiPart) Tools.hidden = isMultiPart;

            // Native Move-tool polling on selected part only (matches PPAW).
            // Skipped during multi-select — our custom handle drives all parts.
            // Uses TryGetStepAwarePose for comparison so the expected pose matches
            // what SyncPartMeshToActivePose set — prevents false re-dirty on past/subassembly parts.
            if (!poseCooldownActive && !isMultiPart &&
                _selectedPartIdx >= 0 && _selectedPartIdx < _parts.Length &&
                (Tools.current == Tool.Move || Tools.current == Tool.Transform) &&
                (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout))
            {
                ref PartEditState pp = ref _parts[_selectedPartIdx];
                if (TryGetStepAwarePose(ref pp, out Vector3 expectedPos, out Quaternion expectedRot, out _))
                {
                    var pollGO = FindLivePartGO(pp.def.id);
                    if (pollGO != null)
                    {
                        Vector3    goPos = pollGO.transform.localPosition;
                        Quaternion goRot = pollGO.transform.localRotation;
                        bool posChg = (goPos - expectedPos).sqrMagnitude > 1e-8f;
                        bool rotChg = Quaternion.Angle(goRot, expectedRot) > 0.005f;
                        if (posChg || rotChg)
                        {
                            BeginPartEdit(_selectedPartIdx);
                            if (_editPlayPose) { pp.playPosition = goPos;  pp.playRotation = goRot; }
                            else               { pp.startPosition = goPos; pp.startRotation = goRot; }
                            pp.isDirty = true;
                            EndPartEdit();
                            Repaint();
                        }
                    }
                }
            }

            // Position + Rotation handles for selected part (PPAW pattern)
            if (_selectedPartIdx < 0 || _selectedPartIdx >= _parts.Length || root == null) return;

            ref PartEditState sel     = ref _parts[_selectedPartIdx];
            var selectedGO = FindLivePartGO(sel.def.id);
            if (selectedGO == null) return;

            Vector3    selWorldPos = selectedGO.transform.position;
            Quaternion selWorldRot = selectedGO.transform.rotation;

            Handles.color = ColSelected;
            Handles.DrawWireDisc(selWorldPos, sv.camera.transform.forward,
                HandleUtility.GetHandleSize(selWorldPos) * 0.14f);

            // Position handle
            EditorGUI.BeginChangeCheck();
            Quaternion posHandleRot = Tools.pivotRotation == PivotRotation.Local ? selWorldRot : Quaternion.identity;
            Vector3    newWorldPos  = Handles.PositionHandle(selWorldPos, posHandleRot);
            if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldPos - selWorldPos).sqrMagnitude > 1e-10f)
            {
                BeginPartEdit(_selectedPartIdx);
                Vector3 oldLocalPos = _editPlayPose ? sel.playPosition : sel.startPosition;
                selectedGO.transform.position = newWorldPos;
                Vector3 newLocalPos = selectedGO.transform.localPosition;
                if (_editPlayPose) sel.playPosition  = newLocalPos;
                else               sel.startPosition = newLocalPos;
                sel.isDirty = true;

                // Move group as a unit — apply the same delta so offsets are preserved
                if (_multiSelectedParts.Count > 1)
                {
                    Vector3 delta = newLocalPos - oldLocalPos;
                    foreach (int midx in _multiSelectedParts)
                    {
                        if (midx == _selectedPartIdx || midx < 0 || midx >= _parts.Length) continue;
                        ref PartEditState mp = ref _parts[midx];
                        Vector3 cur = _editPlayPose ? mp.playPosition : mp.startPosition;
                        cur += delta;
                        if (_editPlayPose) mp.playPosition  = cur;
                        else               mp.startPosition = cur;
                        mp.isDirty = true;
                        var otherGO = FindLivePartGO(mp.def.id);
                        if (otherGO != null) otherGO.transform.localPosition = cur;
                    }
                }
                Repaint();
            }

            // Rotation handle
            EditorGUI.BeginChangeCheck();
            Quaternion rotOrientation = Tools.pivotRotation == PivotRotation.Local ? selWorldRot : Quaternion.identity;
            Quaternion newWorldRot    = Handles.RotationHandle(rotOrientation, selWorldPos);
            if (EditorGUI.EndChangeCheck() && !poseCooldownActive && Quaternion.Angle(newWorldRot, rotOrientation) > 0.01f)
            {
                BeginPartEdit(_selectedPartIdx);
                if (!_rotDragActivePart)
                {
                    _rotDragActivePart      = true;
                    _rotDragStartHandlePart = rotOrientation;
                    _rotDragStartLocalPart  = _editPlayPose ? sel.playRotation : sel.startRotation;
                }
                Quaternion rootRot     = root.rotation;
                Quaternion worldDelta  = newWorldRot * Quaternion.Inverse(_rotDragStartHandlePart);
                Quaternion newLocalRot = Quaternion.Inverse(rootRot) * (worldDelta * (rootRot * _rotDragStartLocalPart));

                selectedGO.transform.localRotation = newLocalRot;
                if (_editPlayPose) sel.playRotation  = newLocalRot;
                else               sel.startRotation = newLocalRot;
                sel.isDirty = true;

                // Apply same absolute rotation to all multi-selected parts
                if (_multiSelectedParts.Count > 1)
                    foreach (int midx in _multiSelectedParts)
                    {
                        if (midx == _selectedPartIdx || midx < 0 || midx >= _parts.Length) continue;
                        ref PartEditState mp = ref _parts[midx];
                        if (_editPlayPose) mp.playRotation  = newLocalRot;
                        else               mp.startRotation = newLocalRot;
                        mp.isDirty = true;
                        var otherGO = FindLivePartGO(mp.def.id);
                        if (otherGO != null) otherGO.transform.localRotation = newLocalRot;
                    }
                Repaint();
            }
            else if (_rotDragActivePart) _rotDragActivePart = false;
        }

        private void DrawConnectionsSceneOverlay()
        {
            if (_targets == null || _targets.Length == 0) return;
            Transform root = GetPreviewRoot();
            if (root == null) return;

            // Build targetId → WireConnectEntry lookup so endpoint sphere colors match the wire color.
            var wireEntryMap = new Dictionary<string, WireConnectEntry>(StringComparer.Ordinal);
            StepDefinition overlayStep = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            if (overlayStep?.wireConnect?.wires != null)
                foreach (var we in overlayStep.wireConnect.wires)
                    if (we?.targetId != null) wireEntryMap[we.targetId] = we;

            foreach (var t in _targets)
            {
                if (t.portA.sqrMagnitude < 0.000001f && t.portB.sqrMagnitude < 0.000001f) continue;

                wireEntryMap.TryGetValue(t.def?.id ?? "", out WireConnectEntry we2);
                Color wireColor = (we2 != null && we2.color.a > 0f)
                    ? new Color(we2.color.r, we2.color.g, we2.color.b, 1f) : ColPortPoint;

                // Wire tube is rendered by _wirePreviewRoot mesh (see RefreshWirePreview).
                // Here we only draw the A/B endpoint spheres and labels as Handles overlays.
                Handles.color = wireColor;
                Vector3 wA = root.TransformPoint(t.portA);
                Vector3 wB = root.TransformPoint(t.portB);
                float sA = HandleUtility.GetHandleSize(wA) * 0.08f;
                float sB = HandleUtility.GetHandleSize(wB) * 0.08f;
                Handles.SphereHandleCap(0, wA, Quaternion.identity, sA, EventType.Repaint);
                Handles.SphereHandleCap(0, wB, Quaternion.identity, sB, EventType.Repaint);
                Handles.Label(wA, " A", EditorStyles.boldLabel);
                Handles.Label(wB, " B", EditorStyles.boldLabel);
            }
        }

        // ── SceneView ─────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            Transform root = GetPreviewRoot();
            if (root == null) return;

            // Lazy-init wire preview: ApplyStepFilter may have run before the spawner
            // service was ready, so we create it here on the first valid SceneView frame.
            // Wrapped in try-catch so any failure does not abort the rest of OnSceneGUI
            // (which would hide the portA/portB PositionHandle gizmos).
            if (_wirePreviewRoot == null && _stepFilterIdx > 0
                && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                try
                {
                    var lazyStep = FindStep(_stepIds[_stepFilterIdx]);
                    RefreshWirePreview(lazyStep);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[TTAW] Wire preview init failed: {e.Message}");
                }
            }

            bool hasTaskFilter   = _selectedTaskSeqIdx >= 0;
            bool isConfirmAction = hasTaskFilter && _activeTaskKind == "confirm_action";

            DrawPartSceneHandles(sv);

            // confirm_action = terminal button-press — no targets, skip all target gizmos.
            if (isConfirmAction) return;

            DrawConnectionsSceneOverlay();
            if (_targets == null || _targets.Length == 0) return;
            bool      hasStepFilter = _activeStepTargetIds != null;
            var       sceneProfile  = TaskFieldRegistry.Get(_activeTaskKind ?? "");

            // No associated target for this task — draw nothing.
            if (hasTaskFilter && _selectedIdx < 0) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                Vector3 worldPos = root.TransformPoint(t.position);
                float   size     = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                bool isSelected  = i == _selectedIdx;
                bool inStep      = !hasStepFilter || _activeStepTargetIds.Contains(t.def.id);

                // When a step is selected but this target doesn't belong to it, skip entirely.
                if (!inStep) continue;

                // When a task is selected, only draw that task's own target.
                if (hasTaskFilter && !isSelected) continue;

                Color col = isSelected ? ColSelected
                          : t.isDirty  ? ColDirty
                          : t.hasPlacement ? ColAuthored
                          : ColNoPlacement;
                Handles.color = col;

                if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    _selectedIdx       = i;
                    _selectedTargetId  = _targets[i].def.id;
                    _clickToSnapActive = false;
                    _snapshotPending   = false;
                    RefreshToolPreview(ref _targets[i]);
                    Repaint();
                }

                if (sceneProfile.SceneWeldArrow)    DrawWeldAxisArrow(ref t, worldPos, 1f);
                if (sceneProfile.ScenePortPoints)   DrawPortPoints(ref t, root, 1f);
                if (sceneProfile.ScenePartConnector) DrawPartConnector(ref t, worldPos, 1f);
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
                if (EditorGUI.EndChangeCheck() && (newWorldPos - worldPos).sqrMagnitude > 1e-10f)
                {
                    BeginEdit();
                    Vector3 newLocal = root.InverseTransformPoint(newWorldPos);
                    Vector3 delta = newLocal - sel.position;
                    sel.position = newLocal;
                    sel.isDirty  = true;
                    if (_multiSelected.Count > 1)
                    {
                        foreach (int idx in _multiSelected)
                        {
                            if (idx == _selectedIdx) continue;
                            ref var t = ref _targets[idx];
                            t.position += delta;
                            t.isDirty = true;
                        }
                    }
                    Repaint();
                }

                if (sceneProfile.SceneRotationHandle)
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion rotHandleOrientation = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                    Quaternion newWorldRot = Handles.RotationHandle(rotHandleOrientation, worldPos);
                    if (EditorGUI.EndChangeCheck() && Quaternion.Angle(newWorldRot, rotHandleOrientation) > 0.01f)
                    {
                        BeginEdit();

                        // Snapshot baselines on first frame of drag (for batch rotation)
                        if (!_rotDragActive)
                        {
                            _rotDragActive      = true;
                            _rotDragStartHandle = rotHandleOrientation;
                            _rotDragStartLocal  = sel.rotation;
                            _rotDragStartMulti  = new Dictionary<int, Quaternion>();
                            if (_multiSelected.Count > 1)
                                foreach (int idx in _multiSelected)
                                    if (idx != _selectedIdx)
                                        _rotDragStartMulti[idx] = _targets[idx].rotation;
                        }

                        // World-space delta from the handle, applied directly (no damping).
                        Quaternion worldDelta = newWorldRot * Quaternion.Inverse(_rotDragStartHandle);
                        Quaternion newLocalRot = Quaternion.Inverse(root.rotation) * (worldDelta * (root.rotation * _rotDragStartLocal));
                        Quaternion localDelta = newLocalRot * Quaternion.Inverse(_rotDragStartLocal);
                        sel.rotation = newLocalRot;
                        sel.isDirty  = true;
                        if (_multiSelected.Count > 1)
                        {
                            foreach (int idx in _multiSelected)
                            {
                                if (idx == _selectedIdx) continue;
                                ref var t = ref _targets[idx];
                                Quaternion startRot = _rotDragStartMulti.TryGetValue(idx, out var sr) ? sr : t.rotation;
                                t.rotation = localDelta * startRot;
                                t.isDirty = true;
                            }
                        }
                        Repaint();
                    }
                    else if (_rotDragActive)
                    {
                        _rotDragActive = false;
                    }
                }

                if (Event.current.type == EventType.MouseUp)
                    EndEdit();

                // Tool preview — tracks the position/rotation gizmo in real-time
                UpdateToolPreview(ref sel);

                // portA / portB drag handles — any Connect-family step
                if (_activeStepIsConnect)
                {
                    Handles.color = ColPortPoint;

                    // Resolve the wire entry that owns this target so we can keep
                    // the wire entry, _targets, and the spline preview in sync.
                    StepDefinition dragStep = _stepFilterIdx > 0 && _stepIds != null
                        && _stepFilterIdx < _stepIds.Length
                        ? FindStep(_stepIds[_stepFilterIdx]) : null;
                    WireConnectEntry dragWire = null;
                    if (dragStep?.wireConnect?.wires != null && sel.def != null)
                        foreach (var w in dragStep.wireConnect.wires)
                            if (w?.targetId == sel.def.id) { dragWire = w; break; }

                    // Use wire entry positions as authoritative source so gizmo matches spline.
                    if (dragWire != null)
                    {
                        sel.portA = new Vector3(dragWire.portA.x, dragWire.portA.y, dragWire.portA.z);
                        sel.portB = new Vector3(dragWire.portB.x, dragWire.portB.y, dragWire.portB.z);
                    }

                    Vector3 portAWorld = root.TransformPoint(sel.portA);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortA = Handles.PositionHandle(portAWorld, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && (newPortA - portAWorld).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.portA = root.InverseTransformPoint(newPortA);
                        sel.isDirty = true;
                        if (dragWire != null) dragWire.portA = PackageJsonUtils.ToFloat3(sel.portA);
                        if (dragStep != null) { _dirtyStepIds.Add(dragStep.id); RefreshWirePreview(dragStep); }
                        Repaint();
                    }

                    Vector3 portBWorld = root.TransformPoint(sel.portB);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortB = Handles.PositionHandle(portBWorld, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && (newPortB - portBWorld).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.portB = root.InverseTransformPoint(newPortB);
                        sel.isDirty = true;
                        if (dragWire != null) dragWire.portB = PackageJsonUtils.ToFloat3(sel.portB);
                        if (dragStep != null) { _dirtyStepIds.Add(dragStep.id); RefreshWirePreview(dragStep); }
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
                    if (EditorGUI.EndChangeCheck() && (newWorldA - worldA).sqrMagnitude > 1e-10f)
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
                    if (EditorGUI.EndChangeCheck() && (newWorldB - worldB).sqrMagnitude > 1e-10f)
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
            Transform weldRoot = GetPreviewRoot();
            if (weldRoot == null) return;
            Vector3 worldAxis = weldRoot.TransformDirection(t.weldAxis.normalized);
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
            var partGo = FindLivePartGO(t.def.associatedPartId);
            if (partGo == null) return;

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
            // Show port spheres for any Connect-family step, or in All Steps mode
            if (!string.IsNullOrEmpty(_activeStepProfile) && !_activeStepIsConnect) return;
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

            // Only snap if we hit one of our live spawned part meshes
            bool hitPartMesh = false;
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var snapSpawner) && snapSpawner?.SpawnedParts != null)
            {
                foreach (var go in snapSpawner.SpawnedParts)
                    if (go != null && hit.transform.IsChildOf(go.transform))
                    { hitPartMesh = true; break; }
            }
            if (!hitPartMesh) return;

            Transform root = GetPreviewRoot();
            if (root == null) return;

            ref TargetEditState sel = ref _targets[_selectedIdx];
            BeginEdit();
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

        private void LoadPkg(string id) => LoadPkg(id, restoring: false);

        private void LoadPkg(string id, bool restoring)
        {
            Cleanup();
            _pkg   = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            if (_pkg == null) return;
            _assetResolver.BuildCatalog(_pkgId, _pkg.parts ?? System.Array.Empty<PartDefinition>());

            // When restoring after domain reload, keep the serialized _stepFilterIdx.
            // Otherwise reset to "All Steps" and try to sync from SessionDriver.
            if (!restoring)
            {
                _stepFilterIdx = 0;
            }

            BuildStepOptions();
            BuildTargetToolMap();

            if (!restoring)
            {
                // Sync initial step from SessionDriver if present
                var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
                if (driver != null && _stepSequenceIdxs != null)
                {
                    int seq = driver.PreviewStepSequenceIndex;
                    for (int k = 1; k < _stepSequenceIdxs.Length; k++)
                    {
                        if (_stepSequenceIdxs[k] == seq) { _stepFilterIdx = k; break; }
                    }
                }
            }

            // Clamp in case stored index is out of range after package edit
            if (_stepOptions != null && _stepFilterIdx >= _stepOptions.Length)
                _stepFilterIdx = 0;

            UpdateActiveStep();
            BuildTargetList();
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose(); // must come AFTER RespawnScene
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

            // Determine which targetIds to show.
            // Always assign a HashSet (even empty) when a step is selected — null means
            // "no filter" (All Steps mode) and would show every package target.
            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                string stepId = _stepIds[_stepFilterIdx];
                if (stepId != null)
                {
                    var step = FindStep(stepId);
                    filterIds = step?.targetIds != null && step.targetIds.Length > 0
                        ? new HashSet<string>(step.targetIds, StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                }
            }

            // Build a wire-entry lookup for the active step so wire targets get portA/portB
            // from the wire entry when they have no target placement.
            StepDefinition activeStep = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            var wirePortByTargetId = new Dictionary<string, (Vector3 a, Vector3 b)>(StringComparer.Ordinal);
            if (activeStep?.wireConnect?.wires != null)
                foreach (var we in activeStep.wireConnect.wires)
                    if (we?.targetId != null)
                        wirePortByTargetId[we.targetId] = (
                            new Vector3(we.portA.x, we.portA.y, we.portA.z),
                            new Vector3(we.portB.x, we.portB.y, we.portB.z));

            var list = new List<TargetEditState>();
            foreach (var def in _pkg.targets)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

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

                // Port positions: target placement first, then wire entry fallback.
                Vector3 portA = hasP ? PackageJsonUtils.ToVector3(placement.portA) : Vector3.zero;
                Vector3 portB = hasP ? PackageJsonUtils.ToVector3(placement.portB) : Vector3.zero;
                if (portA == Vector3.zero && portB == Vector3.zero && wirePortByTargetId.TryGetValue(def.id, out var wp))
                { portA = wp.a; portB = wp.b; }

                var state = new TargetEditState
                {
                    def                     = def,
                    placement               = placement,
                    hasPlacement            = hasP,
                    position                = hasP ? PackageJsonUtils.ToVector3(placement.position)         : defaultPos,
                    rotation                = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.rotation)  : Quaternion.identity,
                    scale                   = hasP ? PackageJsonUtils.ToVector3(placement.scale)             : Vector3.one * DefaultTargetScale,
                    portA                   = portA,
                    portB                   = portB,
                    weldAxis                = def.GetWeldAxisVector(),
                    weldLength              = def.weldLength,
                    useToolActionRotation   = def.useToolActionRotation,
                    toolActionRotationEuler = new Vector3(def.toolActionRotation.x, def.toolActionRotation.y, def.toolActionRotation.z),
                    isDirty                 = false,
                };
                list.Add(state);
            }

            // Preserve selection across rebuilds by matching target ID.
            // _selectedTargetId is serialized, so it survives domain reload even when _targets is null.
            string prevSelectedId = (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                ? _targets[_selectedIdx].def.id : _selectedTargetId;

            _targets     = list.ToArray();
            _selectedIdx = -1;
            if (prevSelectedId != null)
            {
                for (int i = 0; i < _targets.Length; i++)
                    if (_targets[i].def.id == prevSelectedId) { _selectedIdx = i; break; }
            }
            if (_selectedIdx < 0 && _targets.Length > 0) _selectedIdx = 0;
            _selectedTargetId = (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                ? _targets[_selectedIdx].def.id : null;
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

        /// <summary>
        /// For every part in the package that has no <c>assetRef</c>, attempts to find
        /// a GLB in the parts folder whose stem (after stripping _approved / _mesh) matches
        /// the part ID. Matched parts are written to machine.json immediately.
        /// Reports how many were linked and how many still need manual assignment.
        /// </summary>
        private void AutoLinkPartsByFilename()
        {
            if (_pkg?.parts == null || string.IsNullOrEmpty(_pkgId)) return;

            int linked = 0, skipped = 0;
            foreach (var def in _pkg.parts)
            {
                if (def == null || !string.IsNullOrEmpty(def.assetRef)) continue;
                var res = _assetResolver.Resolve(def.id);
                if (res.IsResolved)
                {
                    def.assetRef = Path.GetFileName(res.AssetPath);
                    _dirtyPartAssetRefIds.Add(def.id);
                    linked++;
                }
                else
                {
                    skipped++;
                }
            }

            if (linked > 0)
            {
                // Rebuild part states so the new assetRefs take effect in the scene
                BuildPartList();
                RespawnScene();
                SyncAllPartMeshesToActivePose();
                WriteJson();
                Debug.Log($"[PartAutoLink] Linked {linked} parts by filename. " +
                          $"{skipped} still need manual assetRef (shared/renamed meshes).");
            }
            else
            {
                Debug.Log($"[PartAutoLink] No filename matches found. " +
                          $"{skipped} parts need manual assetRef assignment via the Model Asset field.");
            }
        }

        /// <summary>
        /// Returns the effective asset filename for a part: explicit <c>assetRef</c> if set,
        /// otherwise resolved via all 3 resolver passes (filename match + GLB node search).
        /// Returns null when neither is available.
        /// </summary>
        private string ResolvePartAssetRef(PartDefinition def)
        {
            if (def == null) return null;
            if (!string.IsNullOrEmpty(def.assetRef)) return def.assetRef;
            var res = _assetResolver.Resolve(def.id);
            return res.IsResolved ? Path.GetFileName(res.AssetPath) : null;
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

        /// <summary>
        /// Returns the ToolDefinition for the first ToolActionDefinition that references
        /// <paramref name="targetId"/>. Used during one-time migration to mesh-rotation format.
        /// </summary>
        private ToolDefinition FindToolForTarget(string targetId)
        {
            if (_pkg?.steps == null || _pkg.tools == null) return null;
            foreach (var step in _pkg.steps)
            {
                if (step?.requiredToolActions == null) continue;
                foreach (var action in step.requiredToolActions)
                {
                    if (action?.targetId != targetId || string.IsNullOrEmpty(action.toolId)) continue;
                    foreach (var tool in _pkg.tools)
                        if (tool?.id == action.toolId) return tool;
                }
            }
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

        // ── Scene setup ────────────────────────────────────────────────────────

        private void RespawnScene()
        {
            // No hidden preview root — we work directly with the live spawned parts.
            // Just compute the step-aware context cache and position live parts accordingly.
            if (_pkg?.previewConfig?.partPlacements == null) return;

            _previewAssembled = 0;
            _previewCurrent   = 0;
            _previewHidden    = 0;

            bool stepSelected = _stepFilterIdx > 0 && _stepIds != null
                                && _stepFilterIdx < _stepIds.Length
                                && _stepIds[_stepFilterIdx] != null;

            int currentSeq  = int.MaxValue;
            var partStepSeq = new Dictionary<string, int>(StringComparer.Ordinal);
            var currentStepSubassemblyPartIds = new HashSet<string>(StringComparer.Ordinal);

            if (stepSelected && _pkg.steps != null)
            {
                var sel = FindStep(_stepIds[_stepFilterIdx]);
                if (sel != null) currentSeq = sel.sequenceIndex;

                if (sel != null && !string.IsNullOrEmpty(sel.requiredSubassemblyId)
                    && _pkg.TryGetSubassembly(sel.requiredSubassemblyId, out SubassemblyDefinition curSubDef)
                    && curSubDef?.partIds != null)
                {
                    foreach (string pid in curSubDef.partIds)
                        if (!string.IsNullOrEmpty(pid)) currentStepSubassemblyPartIds.Add(pid);
                }

                foreach (var step in _pkg.steps)
                {
                    if (step?.requiredPartIds != null)
                    {
                        foreach (string pid in step.requiredPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }

                    if (!string.IsNullOrEmpty(step?.requiredSubassemblyId)
                        && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subDef)
                        && subDef?.partIds != null)
                    {
                        foreach (string pid in subDef.partIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }
                }
            }

            _sceneBuildStepActive          = stepSelected;
            _sceneBuildCurrentSeq          = currentSeq;
            _sceneBuildPartStepSeq         = partStepSeq;
            _sceneBuildCurrentSubassembly  = currentStepSubassemblyPartIds;

            // Position and show/hide live parts based on step-aware context.
            SyncAllPartMeshesToActivePose();

            // Add MeshColliders to live parts so click-to-snap works on their surfaces.
            AddMeshCollidersToLiveParts();

            // Refresh tool preview using the spawner's PreviewRoot as coordinate space.
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                RefreshToolPreview(ref _targets[_selectedIdx]);
        }

        // SpawnPartMesh removed — TTAW no longer creates hidden preview objects.

        /// <summary>
        /// Clears the Unity editor selection if any of the supplied objects (or their
        /// children) are currently selected, then forces all open inspectors to rebuild
        /// so they release stale references before <see cref="DestroyImmediate"/> runs.
        /// </summary>
        private static void DeselectIfSelected(params UnityEngine.Object[] objects)
        {
            if (objects == null || objects.Length == 0) return;
            var sel = UnityEditor.Selection.objects;
            if (sel == null || sel.Length == 0) return;

            bool needsClear = false;
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                if (System.Array.IndexOf(sel, obj) >= 0) { needsClear = true; break; }
                if (obj is GameObject go)
                {
                    foreach (var s in sel)
                        if (s is GameObject sg && sg != null && sg.transform.IsChildOf(go.transform))
                        { needsClear = true; break; }
                }
                if (needsClear) break;
            }

            if (!needsClear) return;

            // Wipe the full selection array (more thorough than activeObject = null).
            UnityEditor.Selection.objects = System.Array.Empty<UnityEngine.Object>();
            // Force all open inspectors to rebuild synchronously so they release the
            // stale m_Targets references before DestroyImmediate executes.
            UnityEditor.ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        // KillPartMeshes removed — no hidden preview objects to destroy.

        // ── Tool preview ──────────────────────────────────────────────────────

        private void ClearToolPreview()
        {
            if (_toolPreviewGO != null)
            {
                DeselectIfSelected(_toolPreviewGO);
                DestroyImmediate(_toolPreviewGO);
                _toolPreviewGO = null;
            }
            _toolPreviewDef = null;
        }

        private void ClearWirePreview()
        {
            if (_wirePreviewRoot != null)
            {
                // Destroy procedural meshes before the GO — they are unmanaged assets
                // and won't be GC'd automatically when the MeshFilter is destroyed.
                foreach (var mf in _wirePreviewRoot.GetComponentsInChildren<MeshFilter>())
                    if (mf != null && mf.sharedMesh != null)
                        DestroyImmediate(mf.sharedMesh);
                DestroyImmediate(_wirePreviewRoot);
                _wirePreviewRoot = null;
            }
        }

        private void RefreshWirePreview(StepDefinition step)
        {
            ClearWirePreview();

            Transform root = GetPreviewRoot();
            if (root == null) return;

            // Collect all Connect-family steps up to and including the current step
            // so wires from previously completed steps remain visible.
            int currentSeq = step?.sequenceIndex ?? -1;
            var stepsToShow = new List<StepDefinition>();
            if (_pkg?.steps != null)
                foreach (var s in _pkg.steps)
                    if (s?.wireConnect?.IsConfigured == true && s.sequenceIndex <= currentSeq)
                        stepsToShow.Add(s);

            if (stepsToShow.Count == 0) return;

            _wirePreviewRoot = new GameObject("[TTAW] WirePreview");
            _wirePreviewRoot.hideFlags = HideFlags.HideAndDontSave;
            _wirePreviewRoot.transform.SetParent(root, false);

            foreach (var showStep in stepsToShow)
            foreach (var wire in showStep.wireConnect.wires)
            {
                if (wire == null) continue;

                Vector3 pA = new Vector3(wire.portA.x, wire.portA.y, wire.portA.z);
                Vector3 pB = new Vector3(wire.portB.x, wire.portB.y, wire.portB.z);
                if (pA == Vector3.zero && pB == Vector3.zero) continue;

                float radius = wire.radius > 0f ? wire.radius : 0.003f;
                Color col = wire.color.a > 0f
                    ? new Color(wire.color.r, wire.color.g, wire.color.b, 1f)
                    : new Color(0.1f, 0.1f, 0.1f, 1f);
                int subdivs = Mathf.Max(1, wire.subdivisions);

                // Build sag knots in local space.
                // sag=0 (unset) uses natural default (1.0). 0.01=rigid, 1=natural, 2+=heavy droop.
                float wireLength  = Vector3.Distance(pA, pB);
                float sagFactor   = wire.sag > 0f ? wire.sag : 1.0f;
                float sagDepth    = sagFactor * (wireLength * 0.12f + 0.04f);
                var knotPositions = new SceneFloat3[subdivs + 2];
                knotPositions[0]           = PackageJsonUtils.ToFloat3(pA);
                knotPositions[subdivs + 1] = PackageJsonUtils.ToFloat3(pB);
                for (int k = 0; k < subdivs; k++)
                {
                    float t = (k + 1f) / (subdivs + 1f);
                    knotPositions[k + 1] = PackageJsonUtils.ToFloat3(new Vector3(
                        Mathf.Lerp(pA.x, pB.x, t),
                        Mathf.Lerp(pA.y, pB.y, t) - sagDepth * Mathf.Sin(Mathf.PI * t),
                        Mathf.Lerp(pA.z, pB.z, t)));
                }

                // Delegate to SplinePartFactory — same path as play mode.
                var tangentMode = string.Equals(wire.interpolation, "linear",
                    System.StringComparison.OrdinalIgnoreCase)
                    ? UnityEngine.Splines.TangentMode.Linear
                    : UnityEngine.Splines.TangentMode.AutoSmooth;

                var splineDef = new SplinePathDefinition
                {
                    radius     = radius,
                    segments   = 16,
                    metallic   = 0f,
                    smoothness = 0.4f,
                    knots      = knotPositions
                };

                var wireGo = SplinePartFactory.Create(
                    $"Wire_{wire.targetId}", splineDef, col, _wirePreviewRoot.transform, tangentMode);
                wireGo.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void RefreshToolPreview(ref TargetEditState t)
        {
            ClearToolPreview();
            Transform previewRoot = GetPreviewRoot();
            if (!_showToolPreview || previewRoot == null) return;
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

            // Spawn as a child of previewRoot so it lives in the same coordinate space
            _toolPreviewGO           = Instantiate(pfb, previewRoot);
            _toolPreviewGO.name      = "[ToolTargetAuthoring] ToolPreview";
            _toolPreviewGO.hideFlags = HideFlags.HideAndDontSave;

            // Match the runtime cursor scale (ToolCursorManager.CursorUniformScale = 0.16).
            // The previewRoot may have a non-unit lossyScale (e.g. if parts use a scaled root),
            // so we divide by it to get the correct world-space size.
            const float RuntimeCursorScale = 0.16f;
            float toolCursorScale = (toolDef.scaleOverride > 0f)
                ? RuntimeCursorScale * toolDef.scaleOverride
                : RuntimeCursorScale;
            float rootS = previewRoot.lossyScale.x;
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
        /// Computes the tool's local position and rotation under previewRoot so that
        /// the tool's tipPoint sits at the target position.
        /// After migration, t.rotation is already the mesh rotation — no grip correction needed.
        /// </summary>
        private void ComputeToolLocalTransform(ref TargetEditState t,
            out Vector3 localPos, out Quaternion localRot)
        {
            // t.rotation is the single source of truth (gizmo + Euler field).
            // After migration it IS the mesh rotation (independent of gripRotation).
            localRot = t.rotation;

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
            if (!_showToolPreview || _toolPreviewGO == null || GetPreviewRoot() == null) return;

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
            Transform previewRoot = GetPreviewRoot();
            if (previewRoot == null) return;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            // Frame on selected target position if available
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                Vector3 worldPos = previewRoot.TransformPoint(_targets[_selectedIdx].position);
                float frameSize = HandleUtility.GetHandleSize(worldPos) * 0.5f;
                sv.Frame(new Bounds(worldPos, Vector3.one * frameSize), false);
            }
            else
            {
                Selection.activeGameObject = previewRoot.gameObject;
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
            if (_targets != null) foreach (var t in _targets) if (t.isDirty) return true;
            if (_parts   != null) foreach (var p in _parts)   if (p.isDirty) return true;
            return false;
        }

        /// <summary>
        /// Re-derives toolActionRotation for ALL targets from their placement.rotation quaternions.
        /// Fixes any Euler-convention mismatch introduced by external (Python) migration scripts.
        /// Does not require targets to be dirty — processes the entire previewConfig.targetPlacements array.
        /// </summary>
        private void SyncAllToolRotationsFromPlacements()
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null) return;
            if (_pkg.previewConfig?.targetPlacements == null || _pkg.previewConfig.targetPlacements.Length == 0)
            {
                Debug.LogWarning("[ToolTargetAuthoring] SyncAllToolRotations: no targetPlacements in previewConfig.");
                return;
            }

            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[ToolTargetAuthoring] machine.json not found for '{_pkgId}'"); return; }

            string json = File.ReadAllText(jsonPath);
            Transform pr = GetPreviewRoot();
            Quaternion rootRot = pr != null ? pr.rotation : Quaternion.identity;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int count = 0;

            foreach (var p in _pkg.previewConfig.targetPlacements)
            {
                if (p == null || string.IsNullOrEmpty(p.targetId)) continue;
                var sq = p.rotation;
                Quaternion localRot = new Quaternion(sq.x, sq.y, sq.z, sq.w);
                Vector3 worldEuler = (rootRot * localRot).eulerAngles;
                TryInjectBlock(ref json, p.targetId, "useToolActionRotation", "true");
                string tarJson = $"{{ \"x\": {R(worldEuler.x).ToString(inv)}, \"y\": {R(worldEuler.y).ToString(inv)}, \"z\": {R(worldEuler.z).ToString(inv)} }}";
                TryInjectBlock(ref json, p.targetId, "toolActionRotation", tarJson);
                count++;
            }

            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolTargetAuthoring] SyncAllToolRotations: result would be invalid JSON, aborting.\n{ex.Message}");
                return;
            }

            string backupDir = Path.Combine(Path.GetDirectoryName(jsonPath)!, ".pose_backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"machine_syncrot_{ts}.json");
            File.Copy(jsonPath, backupPath, true);
            _lastBackupPath = backupPath;

            File.WriteAllText(jsonPath, json);
            AssetDatabase.Refresh();
            PackageSyncTool.Sync();
            Debug.Log($"[ToolTargetAuthoring] SyncAllToolRotations: updated toolActionRotation for {count} targets (backup: {backupPath}).");

            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildTargetList();
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

            // Step 1b: Merge dirty _parts into previewConfig.partPlacements
            if (_parts != null)
            {
                var pp = _pkg.previewConfig.partPlacements != null
                    ? new List<PartPreviewPlacement>(_pkg.previewConfig.partPlacements)
                    : new List<PartPreviewPlacement>();
                foreach (ref PartEditState p in _parts.AsSpan())
                {
                    if (!p.isDirty) continue;
                    string pid = p.def.id;
                    int pidx = pp.FindIndex(e => e != null && e.partId == pid);
                    var entry = pidx >= 0 ? pp[pidx] : new PartPreviewPlacement { partId = pid };
                    entry.startPosition = PackageJsonUtils.ToFloat3(p.startPosition);
                    entry.startRotation = PackageJsonUtils.ToQuaternion(p.startRotation);
                    entry.startScale    = PackageJsonUtils.ToFloat3(p.startScale);
                    entry.playPosition  = PackageJsonUtils.ToFloat3(p.playPosition);
                    entry.playRotation  = PackageJsonUtils.ToQuaternion(p.playRotation);
                    entry.playScale     = PackageJsonUtils.ToFloat3(p.playScale);
                    entry.color = new SceneFloat4 { r = p.color.r, g = p.color.g, b = p.color.b, a = p.color.a };
                    if (pidx >= 0) pp[pidx] = entry; else pp.Add(entry);
                }
                _pkg.previewConfig.partPlacements = pp.ToArray();
            }

            // Step 2: Validate original JSON
            string json = File.ReadAllText(jsonPath);
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[ToolTargetAuthoring] machine.json is already invalid, aborting.\n{ex.Message}");
                return;
            }

            // Step 3: Write previewConfig block
            // Mark as mesh-rotation format so runtime skips the legacy grip correction.
            _pkg.previewConfig.targetRotationFormat = "mesh";
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
                Quaternion worldRot   = GetPreviewRoot() is Transform wr
                    ? wr.rotation * t.rotation
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

            // Step 5b: Inject modified step fields for dirty steps
            foreach (string stepId in _dirtyStepIds)
            {
                var step = FindStep(stepId);
                if (step == null) continue;

                // removePersistentToolIds
                string idsJson = step.removePersistentToolIds == null || step.removePersistentToolIds.Length == 0
                    ? "[]"
                    : "[ " + string.Join(", ", Array.ConvertAll(step.removePersistentToolIds, id => $"\"{id}\"")) + " ]";
                TryInjectBlock(ref json, stepId, "removePersistentToolIds", idsJson);

                // targetIds
                if (step.targetIds != null)
                {
                    string tJson = step.targetIds.Length == 0 ? "[]"
                        : "[ " + string.Join(", ", Array.ConvertAll(step.targetIds, id => $"\"{id}\"")) + " ]";
                    TryInjectBlock(ref json, stepId, "targetIds", tJson);
                }

                // requiredPartIds
                if (step.requiredPartIds != null)
                {
                    string pJson = step.requiredPartIds.Length == 0 ? "[]"
                        : "[ " + string.Join(", ", Array.ConvertAll(step.requiredPartIds, id => $"\"{id}\"")) + " ]";
                    TryInjectBlock(ref json, stepId, "requiredPartIds", pJson);
                }

                // requiredToolActions
                if (step.requiredToolActions != null)
                {
                    string aJson;
                    if (step.requiredToolActions.Length == 0)
                    {
                        aJson = "[]";
                    }
                    else
                    {
                        var rows = Array.ConvertAll(step.requiredToolActions, a =>
                        {
                            if (a == null) return "{}";
                            var sb = new System.Text.StringBuilder("{");
                            if (!string.IsNullOrEmpty(a.id))       sb.Append($"\"id\":\"{a.id}\",");
                            if (!string.IsNullOrEmpty(a.toolId))   sb.Append($"\"toolId\":\"{a.toolId}\",");
                            if (!string.IsNullOrEmpty(a.targetId)) sb.Append($"\"targetId\":\"{a.targetId}\"");
                            else if (sb[sb.Length - 1] == ',')     sb.Length--; // trim trailing comma
                            sb.Append("}");
                            return sb.ToString();
                        });
                        aJson = "[ " + string.Join(", ", rows) + " ]";
                    }
                    TryInjectBlock(ref json, stepId, "requiredToolActions", aJson);
                }

                // wireConnect
                if (step.wireConnect?.IsConfigured == true)
                {
                    var wc = step.wireConnect;
                    var inv2 = System.Globalization.CultureInfo.InvariantCulture;

                    // Sync portA/portB from TargetEditState → wire entry so drag-handle
                    // edits are captured in the wire entry (wire targets have no placement).
                    if (_targets != null)
                        foreach (ref var te in _targets.AsSpan())
                            if (te.isDirty && wc.wires != null)
                                foreach (var we in wc.wires)
                                    if (we != null && we.targetId == te.def?.id)
                                    {
                                        we.portA = PackageJsonUtils.ToFloat3(te.portA);
                                        we.portB = PackageJsonUtils.ToFloat3(te.portB);
                                    }

                    var wRows = Array.ConvertAll(wc.wires, w =>
                    {
                        if (w == null) return "{}";
                        var sb = new System.Text.StringBuilder("{");
                        if (!string.IsNullOrEmpty(w.targetId))           sb.Append($"\"targetId\":\"{w.targetId}\",");
                        // portA/portB — always write so they survive round-trips
                        sb.Append($"\"portA\":{{\"x\":{R(w.portA.x).ToString(inv2)},\"y\":{R(w.portA.y).ToString(inv2)},\"z\":{R(w.portA.z).ToString(inv2)}}},");
                        sb.Append($"\"portB\":{{\"x\":{R(w.portB.x).ToString(inv2)},\"y\":{R(w.portB.y).ToString(inv2)},\"z\":{R(w.portB.z).ToString(inv2)}}},");
                        // color — always write so the value survives round-trips
                        sb.Append($"\"color\":{{\"r\":{R(w.color.r).ToString(inv2)},\"g\":{R(w.color.g).ToString(inv2)},\"b\":{R(w.color.b).ToString(inv2)},\"a\":{R(w.color.a).ToString(inv2)}}},");
                        sb.Append($"\"radius\":{R(w.radius > 0 ? w.radius : 0.003f).ToString(inv2)},");
                        sb.Append($"\"subdivisions\":{Mathf.Max(1, w.subdivisions)},");
                        sb.Append($"\"sag\":{R(w.sag > 0f ? w.sag : 1.0f).ToString(inv2)},");
                        if (!string.IsNullOrEmpty(w.interpolation)) sb.Append($"\"interpolation\":\"{w.interpolation}\",");
                        if (!string.IsNullOrEmpty(w.portAPolarityType))  sb.Append($"\"portAPolarityType\":\"{w.portAPolarityType}\",");
                        if (!string.IsNullOrEmpty(w.portBPolarityType))  sb.Append($"\"portBPolarityType\":\"{w.portBPolarityType}\",");
                        if (!string.IsNullOrEmpty(w.portAConnectorType)) sb.Append($"\"portAConnectorType\":\"{w.portAConnectorType}\",");
                        if (!string.IsNullOrEmpty(w.portBConnectorType)) sb.Append($"\"portBConnectorType\":\"{w.portBConnectorType}\",");
                        sb.Append($"\"polarityOrderMatters\":{(w.polarityOrderMatters ? "true" : "false")}");
                        sb.Append("}");
                        return sb.ToString();
                    });
                    string wcJson = $"{{\"enforcePortOrder\":{(wc.enforcePortOrder ? "true" : "false")},\"wires\":[ {string.Join(", ", wRows)} ]}}";
                    TryInjectBlock(ref json, stepId, "wireConnect", wcJson);
                }

                // taskOrder
                if (step.taskOrder != null && step.taskOrder.Length > 0)
                    TryInjectBlock(ref json, stepId, "taskOrder", BuildTaskOrderJson(new List<TaskOrderEntry>(step.taskOrder)));
            }
            _dirtyStepIds.Clear();
            _dirtyTaskOrderStepIds.Clear();

            // Step 5c: Inject assetRef for parts whose model was changed in the authoring window
            foreach (string partId in _dirtyPartAssetRefIds)
            {
                if (_parts == null) break;
                foreach (ref PartEditState p in _parts.AsSpan())
                {
                    if (p.def?.id != partId) continue;
                    if (!string.IsNullOrEmpty(p.def.assetRef))
                        TryInjectBlock(ref json, partId, "assetRef", $"\"{p.def.assetRef}\"");
                    break;
                }
            }
            _dirtyPartAssetRefIds.Clear();

            // Step 5e: Validate result
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
            if (_pkg != null)
                _assetResolver.BuildCatalog(_pkgId, _pkg.parts ?? System.Array.Empty<PartDefinition>());
            BuildTargetToolMap();
            BuildTargetList();
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose(); // must come AFTER RespawnScene
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
            BuildPartList();
            SyncAllPartMeshesToActivePose();
        }

        /// <summary>
        /// Discards ALL unsaved in-memory edits and reloads from the current machine.json on disk.
        /// Prompts for confirmation first.
        /// </summary>
        private void RevertAllChanges()
        {
            if (string.IsNullOrEmpty(_pkgId)) return;
            if (!EditorUtility.DisplayDialog(
                "Revert All Changes",
                $"Discard all unsaved edits and reload '{_pkgId}/machine.json' from disk?",
                "Revert", "Cancel"))
                return;

            LoadPkg(_pkgId);   // Cleanup() inside clears all dirty sets; rebuilds lists from disk
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
            // Walk backward with brace depth tracking to find the '{' that directly
            // contains 'from'. Depth 0 = not yet inside any nested closer; the first
            // '{' we hit at depth 0 is the containing object start.
            // We skip string literals (including escaped quotes) so braces inside
            // strings are not counted.
            int depth = 0;
            for (int i = from - 1; i >= 0; i--)
            {
                char c = json[i];
                if (c == '"')
                {
                    // Count preceding backslashes to determine if the quote is escaped
                    int slashes = 0;
                    for (int j = i - 1; j >= 0 && json[j] == '\\'; j--) slashes++;
                    if (slashes % 2 == 0)
                    {
                        // Unescaped quote — skip backward over the string literal
                        i--;
                        while (i >= 0)
                        {
                            char sc = json[i];
                            if (sc == '"')
                            {
                                int sl2 = 0;
                                for (int j = i - 1; j >= 0 && json[j] == '\\'; j--) sl2++;
                                if (sl2 % 2 == 0) break; // found unescaped opening quote
                            }
                            i--;
                        }
                        continue;
                    }
                }
                if (c == '}') { depth++; continue; }
                if (c == '{')
                {
                    if (depth == 0) return i;
                    depth--;
                }
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
