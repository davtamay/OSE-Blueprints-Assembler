// ToolTargetAuthoringWindow.cs — Root partial class file.
// Contains: usings, namespace/class declaration, constants, colors, all field
// declarations, all nested type definitions, and MenuItem + OpenWindow only.
// All methods live in the TTAW.*.cs partial files in this directory.
// ──────────────────────────────────────────────────────────────────────────────

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
    /// assembledPosition, the current step's part is at startPosition, future parts
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
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
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
        [SerializeField] private int        _navigatorViewMode;     // 0 = tree, 1 = flat
        [SerializeField] private bool       _inspectorVisible = true; // toolbar toggle for the right pane
        private bool       _suppressStepSync;   // prevent circular sync with SessionDriver
        private int        _lastPolledDriverStep = -1; // last SessionDriver step seen during poll
        // Note: the IMGUI scrub-drag fields (_stepNumRect, _stepDragging, _stepDragAccum,
        // _stepDragStartVal, _lastDriverSyncTime) were removed in Phase 2 — the toolbar's
        // UITK IntegerField uses isDelayed and discrete prev/next buttons, so per-pixel
        // throttling is no longer needed.

        // Cached step-scene context, written by RespawnScene, read by SyncAllPartMeshesToActivePose.
        // Allows sync to apply the same past/current/future logic as RespawnScene without rebuilding.
        private bool                    _sceneBuildStepActive;
        private int                     _sceneBuildCurrentSeq;
        private Dictionary<string, int> _sceneBuildPartStepSeq        = new();
        private HashSet<string>         _sceneBuildCurrentSubassembly = new();
        private StepWorkingOrientationPayload _sceneBuildWorkingOrientation;
        private Vector3                       _sceneBuildSubassemblyFramePos;
        private HashSet<string>               _sceneBuildWorkingOrientationParts = new();

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
        private readonly HashSet<string> _dirtyPartToolIds     = new HashSet<string>(StringComparer.Ordinal); // Phase 7b — Part × Tool affinity
        private readonly HashSet<string> _dirtySubassemblyIds  = new HashSet<string>(StringComparer.Ordinal); // Phase 7e — Subassembly writes
        private readonly HashSet<string> _dirtyPartIds         = new HashSet<string>(StringComparer.Ordinal); // Generic part-level edits (animationCues, subassemblyIds, etc.)
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

        // Working orientation foldout
        private bool _showWorkingOrientation;

        // Phase A2 — subassembly root GOs that member parts are parented under.
        // One root per group that has visible members in the current step.
        // Created in RespawnScene; destroyed on step change or cleanup.
        // The root's localRotation IS the working orientation.
        private readonly Dictionary<string, GameObject> _subassemblyRootGOs = new(StringComparer.Ordinal);

        // Set during EnsureAllSubassemblyRoots if a visible group member had no
        // live GO yet (spawner hadn't registered it). RespawnScene schedules a
        // delayed second pass so the first step click populates correctly
        // without the user having to click away and back.
        private bool _pendingRespawnForMissingMembers;
        private bool _respawnRetryScheduled;

        // ── Parts tab ─────────────────────────────────────────────────────────
        // Part model preview panel
        private PartModelPreviewRenderer _partPreview;
        private string                   _partPreviewId;   // partId currently loaded in preview
        private const string             PrefDimUnit = "OSE.AuthoringWindow.DimUnit"; // "mm" or "in"

        private PartEditState[]          _parts;
        [SerializeField] private int    _selectedPartIdx = -1;
        [SerializeField] private string _selectedPartId;
        private readonly HashSet<int>   _multiSelectedParts = new HashSet<int>();

        /// <summary>
        /// Cache of per-partId ownership data (Place-owner, Required/Optional/
        /// Visual step lists, subassembly, conflicts). Built once per
        /// <c>BuildPartList</c> rebuild; consumed by the four proactive-
        /// guidance surfaces so every scan runs off this single answer
        /// instead of walking <c>_pkg.steps</c> on every repaint.
        /// </summary>
        private PartOwnershipIndex       _ownership = PartOwnershipIndex.Empty;
        // Pose-mode selector: PoseModeStart = start pose, PoseModeAssembled = assembled pose,
        // 0..N = stepPose index (intermediate poses).
        private const int PoseModeStart     = -1;
        private const int PoseModeAssembled = -2;
        [SerializeField] private int    _editingPoseMode = PoseModeStart;
        private bool _editAssembledPose => _editingPoseMode == PoseModeAssembled;
        private double _poseSwitchCooldownUntil; // EditorApplication.timeSinceStartup deadline; suppress false dirty until then
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
        /// <summary>
        /// Ids in the cached taskOrder that were NOT in the persisted
        /// <c>step.taskOrder</c> — i.e. reconstructed from
        /// requiredPartIds / optionalPartIds / visualPartIds because the
        /// persisted order was missing them. The row renderer flags these so
        /// the author sees "this part is Required by the step but isn't in
        /// the authored task order" instead of the part being invisible.
        /// </summary>
        private readonly HashSet<string> _cachedOrphanTaskIds = new HashSet<string>(StringComparer.Ordinal);

        // Drag-and-drop reorderable list for TASK SEQUENCE
        private ReorderableList _taskSeqReorderList;
        private string          _taskSeqReorderListForStepId;
        private int             _selectedTaskSeqIdx = -1; // selected row in TASK SEQUENCE
        private readonly HashSet<int> _multiSelectedTaskSeqIdxs = new HashSet<int>();

        // Add-task inline picker (shown below task sequence)
        private enum AddTaskPicker { None, Part, ToolTarget, Wire, Group }
        private int _addPickerGroupIdx;
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

        // ── Animation Cue authoring & editor preview ──────────────────────────
        private readonly List<bool> _cueFoldouts = new List<bool>();
        private int                 _previewingCueIdx    = -1;   // -1 = none
        private IAnimationCuePlayer _previewPlayer;              // one active at a time
        private double              _previewStartTime;           // EditorApplication.timeSinceStartup
        private double              _previewLastTickTime;
        private bool                _previewUpdateRegistered;
        private string              _previewingForStepId;

        private static readonly string[] _cueTypes =
            { "transform", "shake", "pulse", "demonstratePlacement", "poseTransition", "orientSubassembly", "animationClip" };
        // Trigger values written to machine.json (must stay in sync with _cueTriggerLabels)
        private static readonly string[] _cueTriggers =
            { "onActivate", "afterDelay", "afterPartsShown", "onStepComplete", "onFirstInteraction", "onTaskComplete" };
        // Human-readable labels shown in the dropdown (parallel to _cueTriggers)
        private static readonly string[] _cueTriggerLabels =
        {
            "On Activate — immediately when step opens",
            "After Delay — N seconds after step opens",
            "After Parts Shown — once all previews are spawned",
            "On Step Complete — when all tasks are validated",
            "On First Interaction — first tool contact this step",
            "On Task Complete — when a specific task is validated",
        };
        private static readonly string[] _cueEasings   = { "smoothStep", "linear", "easeInOut" };
        private static readonly string[] _cueTargetModes = { "part", "ghost" };

        // ── Particle Effect authoring & editor preview ────────────────────────
        private readonly List<bool> _particleFoldouts = new List<bool>();
        private int          _previewingParticleIdx    = -1; // -1 = none active
        private string       _previewingParticleStepId;
        private GameObject   _previewParticleGO;             // spawned by TrySpawnContinuous
        private double       _particleLastTickTime;
        private float        _particleSimTime;               // accumulated simulation time
        private bool         _particleUpdateRegistered;

        private static readonly string[] _particlePresets =
            { "torque_sparks", "weld_glow", "weld_arc" };
        private static readonly string[] _particleTriggers =
            { "onActivate", "afterDelay" };
        private static readonly string[] _particleTriggerLabels =
        {
            "On Activate — immediately when step opens",
            "After Delay — N seconds after step opens",
        };

        private static readonly string[] _familyOptions = { "Place", "Use", "Connect", "Confirm" };
        private static readonly string[][] _profileOptions =
        {
            new[] { "(none)", "AxisFit" },                               // Place
            new[] { "(none)", "Torque", "Weld", "Cut", "SquareCheck", "Measure" }, // Use
            new[] { "(none)", "Cable", "WireConnect" },                  // Connect
            new[] { "(none)" },                                          // Confirm
        };

        // ── Unified selection (Phase 5) ───────────────────────────────────────
        //
        // Derived from the 5 legacy selection fields every 100 ms by
        // SyncEditorSelection(). New code can read from _selection instead
        // of juggling the 5 fields directly. Legacy write sites still mutate
        // the fields directly — SyncEditorSelection picks up changes the
        // next tick. A future pass can migrate writes to SetSelection() one
        // at a time without risk.
        private EditorSelection _selection;

        // ── Nested types ──────────────────────────────────────────────────────

        /// <summary>
        /// Immutable snapshot of what the author currently has selected.
        /// Built every 100 ms from the legacy fields by SyncEditorSelection.
        /// </summary>
        public readonly struct EditorSelection
        {
            public enum Kind { None, Part, Target, Task }

            public readonly Kind   SelectionKind;
            public readonly string Id;           // partId, targetId, or taskOrderEntry id
            public readonly string StepId;       // id of the step that owns this selection
            public readonly int    PoseMode;     // PoseModeStart, PoseModeAssembled, or step-pose index
            public readonly int    MultiCount;   // total multi-selected count (parts + targets)

            public EditorSelection(Kind kind, string id, string stepId, int poseMode, int multiCount)
            {
                SelectionKind = kind;
                Id            = id;
                StepId        = stepId;
                PoseMode      = poseMode;
                MultiCount    = multiCount;
            }

            public static readonly EditorSelection Empty = new(Kind.None, null, null, -1, 0);

            public string DisplayLabel
            {
                get
                {
                    if (MultiCount > 1) return $"{MultiCount} selected";
                    return SelectionKind switch
                    {
                        Kind.Part   => $"Part: {Id ?? "?"}",
                        Kind.Target => $"Target: {Id ?? "?"}",
                        Kind.Task   => $"Task: {Id ?? "?"}",
                        _           => string.Empty,
                    };
                }
            }
        }

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
            public Vector3    startPosition, startScale, assembledPosition, assembledScale;
            public Quaternion startRotation, assembledRotation;
            public Color      color;
            public List<StepPoseEntry> stepPoses; // intermediate poses (may be null)
        }

        private struct PartSnapshot
        {
            public Vector3 startPosition; public Quaternion startRotation; public Vector3 startScale;
            public Vector3 assembledPosition;  public Quaternion assembledRotation;  public Vector3 assembledScale;
            public List<StepPoseEntry> stepPoses; // deep-copied for undo
        }

        // ── Group pose state (Phase G1) ──────────────────────────────────────
        // Mirrors PartEditState but for subassembly/group root GOs.
        // Populated by BuildGroupList from SubassemblyPreviewPlacement.

        private struct GroupEditState
        {
            public SubassemblyDefinition    def;
            public SubassemblyPreviewPlacement placement;
            public bool      hasPlacement, isDirty;
            public Vector3    startPosition,    assembledPosition;
            public Quaternion startRotation,    assembledRotation;
            public Vector3    startScale,       assembledScale;
            public List<StepPoseEntry> stepPoses;
        }

        private GroupEditState[]       _groups;
        private int                    _selectedGroupIdx = -1;
        private int                    _editingGroupPoseMode = PoseModeStart;

        // ── MenuItem ──────────────────────────────────────────────────────────

        [MenuItem(MenuPath)]
        public static void Open() => OpenWindow();

        private static void OpenWindow()
        {
            var w = GetWindow<ToolTargetAuthoringWindow>("Assembly Step Authoring");
            // Phase 4 — three-pane layout (nav 240 + canvas 320 + inspector 280) needs ~880 wide.
            // The inspector can be hidden via the toolbar to recover horizontal space.
            w.minSize = new Vector2(720, 580);
            if (w.position.width < 880f)
                w.position = new Rect(w.position.x, w.position.y, 880f, Mathf.Max(w.position.height, 720f));
            w.Show();
        }
    }
}
