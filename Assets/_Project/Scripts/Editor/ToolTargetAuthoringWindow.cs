using System;
using System.Collections.Generic;
using System.IO;
using OSE.Content;
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
    /// (Weld/Cut → weldAxis+weldLength; Measure → portA+portB;
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

        private string[]   _stepOptions;   // "(All Steps)", then "[seq] name · tool · profile"
        private string[]   _stepIds;       // null at index 0, then actual step ids
        private int        _stepFilterIdx;

        // Active-step context (null = All Steps)
        private string          _activeStepProfile;
        private HashSet<string> _activeStepTargetIds;

        private TargetEditState[] _targets;
        private int               _selectedIdx = -1;

        private Vector2 _listScroll;
        private Vector2 _detailScroll;

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
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void Cleanup()
        {
            KillPartMeshes();
            if (_previewRoot != null) DestroyImmediate(_previewRoot);
            _previewRoot = null;
            _targets = null;
            _selectedIdx = -1;
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
            if (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                DrawDetailPanel(ref _targets[_selectedIdx]);
            else
                EditorGUILayout.HelpBox("Select a target above.", MessageType.Info);
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

            int i = EditorGUILayout.Popup("Step Filter", _stepFilterIdx, _stepOptions);
            if (i != _stepFilterIdx)
                ApplyStepFilter(i);

            // Prev / Next navigation row
            if (_stepOptions.Length > 1)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginDisabledGroup(_stepFilterIdx <= 1);
                if (GUILayout.Button("◄ Prev", GUILayout.Width(70)))
                    ApplyStepFilter(_stepFilterIdx - 1);
                EditorGUI.EndDisabledGroup();

                int stepCount = _stepOptions.Length - 1;
                string navLabel = _stepFilterIdx == 0
                    ? $"All  ({stepCount} steps with targets)"
                    : $"Step {_stepFilterIdx} of {stepCount}";
                EditorGUILayout.LabelField(navLabel, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));

                EditorGUI.BeginDisabledGroup(_stepFilterIdx == 0 || _stepFilterIdx >= _stepOptions.Length - 1);
                if (GUILayout.Button("Next ►", GUILayout.Width(70)))
                    ApplyStepFilter(_stepFilterIdx + 1);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
            }
        }

        private void ApplyStepFilter(int newIdx)
        {
            _stepFilterIdx    = newIdx;
            _selectedIdx      = -1;
            _clickToSnapActive = false;
            UpdateActiveStep();
            BuildTargetList();
            RespawnScene();
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

            EditorGUILayout.LabelField($"Targets ({_targets.Length})", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(listHeight));

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                Color col = t.isDirty ? ColDirty : t.hasPlacement ? ColAuthored : ColNoPlacement;

                bool isSelected = i == _selectedIdx;
                var style = new GUIStyle(isSelected ? EditorStyles.boldLabel : EditorStyles.label)
                {
                    normal  = { textColor = col },
                    focused = { textColor = col }
                };

                string badge = t.isDirty ? " ●" : t.hasPlacement ? "" : " ○";
                string label = $"  {t.def.id}{badge}";
                if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true)))
                {
                    if (_selectedIdx != i)
                    {
                        _selectedIdx = i;
                        _clickToSnapActive = false;
                        _snapshotPending = false;
                        SceneView.RepaintAll();
                    }
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

            if (ShowMeasureGroup())
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPortA = EditorGUILayout.Vector3Field("Port A (local)", t.portA);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.portA = newPortA; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPortB = EditorGUILayout.Vector3Field("Port B (local)", t.portB);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.portB = newPortB; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            if (ShowRotationGroup())
            {
                EditorGUI.BeginChangeCheck();
                bool newUTAR = EditorGUILayout.Toggle("Use Tool Action Rotation", t.useToolActionRotation);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.useToolActionRotation = newUTAR; t.isDirty = true; EndEdit(); }

                EditorGUI.BeginDisabledGroup(!t.useToolActionRotation);
                EditorGUI.BeginChangeCheck();
                Vector3 newTAR = EditorGUILayout.Vector3Field("Tool Action Rotation", t.toolActionRotationEuler);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.toolActionRotationEuler = newTAR; t.isDirty = true; EndEdit(); }
                EditorGUI.EndDisabledGroup();
            }

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

        // Returns true when profile calls for this field group, or when no step is selected.
        private bool ShowWeldGroup()    => string.IsNullOrEmpty(_activeStepProfile)
                                          || _activeStepProfile == "Weld"
                                          || _activeStepProfile == "Cut";

        private bool ShowMeasureGroup() => string.IsNullOrEmpty(_activeStepProfile)
                                          || _activeStepProfile == "Measure";

        private bool ShowRotationGroup()=> string.IsNullOrEmpty(_activeStepProfile)
                                          || _activeStepProfile == "Torque"
                                          || _activeStepProfile == "Clamp"
                                          || _activeStepProfile == "Strike";

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
                        Repaint();
                    }
                }
                else
                {
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }

                DrawWeldAxisArrow(ref t, worldPos, alpha);
                DrawPortPoints(ref t, root, alpha);
            }

            // Handles for selected target
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                ref TargetEditState sel     = ref _targets[_selectedIdx];
                Vector3    worldPos = root.TransformPoint(sel.position);
                Quaternion worldRot = root.rotation * sel.rotation;
                float      size     = HandleUtility.GetHandleSize(worldPos) * 0.14f;

                Handles.color = ColSelected;
                Handles.DrawWireDisc(worldPos, sv.camera.transform.forward, size * 1.6f);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, worldRot);
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

        private void DrawPortPoints(ref TargetEditState t, Transform root, float alpha = 1f)
        {
            if (_activeStepProfile != "Measure") return;
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
            UpdateActiveStep();
            BuildTargetList();
            RespawnScene();
        }

        private void BuildStepOptions()
        {
            var optList = new List<string> { "(All Steps)" };
            var idList  = new List<string> { null };

            if (_pkg?.steps != null)
            {
                // Collect steps that have targets, sort by sequenceIndex
                var withTargets = new List<StepDefinition>();
                foreach (var step in _pkg.steps)
                {
                    if (step?.targetIds == null || step.targetIds.Length == 0) continue;
                    withTargets.Add(step);
                }
                withTargets.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                foreach (var step in withTargets)
                {
                    // Resolve first relevant tool name
                    string toolName = "(no tool)";
                    if (step.relevantToolIds != null && step.relevantToolIds.Length > 0 && _pkg.tools != null)
                    {
                        foreach (var td in _pkg.tools)
                        {
                            if (td != null && td.id == step.relevantToolIds[0])
                            { toolName = td.name; break; }
                        }
                    }

                    string profilePart = string.IsNullOrEmpty(step.profile) ? "" : $"  ·  {step.profile}";
                    string display     = $"[{step.sequenceIndex}] {step.name}  ·  {toolName}{profilePart}";
                    optList.Add(display);
                    idList.Add(step.id);
                }
            }

            _stepOptions = optList.ToArray();
            _stepIds     = idList.ToArray();
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

            var list = new List<TargetEditState>();
            foreach (var def in _pkg.targets)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

                TargetPreviewPlacement placement = FindPlacement(def.id);
                bool hasP = placement != null;

                var state = new TargetEditState
                {
                    def                     = def,
                    placement               = placement,
                    hasPlacement            = hasP,
                    position                = hasP ? PackageJsonUtils.ToVector3(placement.position)         : Vector3.zero,
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

            _targets     = list.ToArray();
            _selectedIdx = _targets.Length > 0 ? 0 : -1;
        }

        private TargetPreviewPlacement FindPlacement(string targetId)
        {
            var arr = _pkg?.previewConfig?.targetPlacements;
            if (arr == null) return null;
            foreach (var p in arr)
                if (p != null && p.targetId == targetId) return p;
            return null;
        }

        private StepDefinition FindStep(string stepId)
        {
            if (_pkg?.steps == null) return null;
            foreach (var s in _pkg.steps)
                if (s != null && s.id == stepId) return s;
            return null;
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

            // Build part → sequenceIndex map for step-aware placement
            bool stepSelected = _stepFilterIdx > 0 && _stepIds != null
                                && _stepFilterIdx < _stepIds.Length
                                && _stepIds[_stepFilterIdx] != null;

            int currentSeq = int.MaxValue;
            var partStepSeq = new Dictionary<string, int>(StringComparer.Ordinal);

            if (stepSelected && _pkg.steps != null)
            {
                currentSeq = 0;
                var sel = FindStep(_stepIds[_stepFilterIdx]);
                if (sel != null) currentSeq = sel.sequenceIndex;

                foreach (var step in _pkg.steps)
                {
                    if (step?.requiredPartIds == null) continue;
                    foreach (string pid in step.requiredPartIds)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                            partStepSeq[pid] = step.sequenceIndex;
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
                        SpawnPartMesh(pp.partId, partDef.assetRef,
                            PackageJsonUtils.ToVector3(pp.playPosition),
                            PackageJsonUtils.ToUnityQuaternion(pp.playRotation),
                            PackageJsonUtils.ToVector3(pp.playScale));
                        continue;
                    }

                    if (placedAt > currentSeq) continue;   // not yet assembled — hide

                    bool useStart = placedAt == currentSeq;
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
                    SpawnPartMesh(pp.partId, partDef.assetRef,
                        PackageJsonUtils.ToVector3(pp.playPosition),
                        PackageJsonUtils.ToUnityQuaternion(pp.playRotation),
                        PackageJsonUtils.ToVector3(pp.playScale));
                }
            }

            AddMeshColliders();
        }

        private void SpawnPartMesh(string partId, string assetRef, Vector3 localPos, Quaternion localRot, Vector3 localScl)
        {
            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);
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

        private void FrameInScene()
        {
            if (_previewRoot == null) return;
            Selection.activeGameObject = _previewRoot;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.FrameSelected(); sv.Repaint(); }
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

                TryInjectBlock(ref json, t.def.id, "useToolActionRotation", t.useToolActionRotation ? "true" : "false");

                string tarJson = $"{{ \"x\": {R(t.toolActionRotationEuler.x).ToString(inv)}, \"y\": {R(t.toolActionRotationEuler.y).ToString(inv)}, \"z\": {R(t.toolActionRotationEuler.z).ToString(inv)} }}";
                TryInjectBlock(ref json, t.def.id, "toolActionRotation", tarJson);
            }

            // Step 5: Validate result
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
