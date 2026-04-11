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
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Main GUI (canvas pane) ────────────────────────────────────────────
        //
        // PHASE 4 of the UX redesign: this method runs inside the canvas
        // IMGUIContainer (the middle pane of the three-pane shell built by
        // CreateGUI() in TTAW.Shell.cs). The toolbar above handles package
        // picker / step nav / dirty indicator. The right pane runs
        // DrawInspectorIMGUI() which renders the detail/batch context panels
        // for the current selection. This method draws only the canvas: the
        // new-step form (when toggled), the step info card, the unified task
        // list (which fills the available height), and the pinned actions
        // bar at the bottom.
        //
        // The pre-Phase-4 pinned-bottom edit panel (DrawBottomEditPanel) and
        // its taskDetailInScroll branching are gone — that detail content
        // now lives in the inspector pane on the right.

        private void DrawAuthoringIMGUI()
        {
            // Restore after domain reload: _pkgId survives via [SerializeField] but
            // _pkg (not serializable) is lost. By the time the IMGUIContainer first
            // ticks the AssetDatabase is ready, so scene meshes and tool previews
            // load correctly.
            if (_pkg == null && !string.IsNullOrEmpty(_pkgId))
            {
                LoadPkg(_pkgId, restoring: true);
                if (_pkg == null) _pkgId = null;
            }

            // Empty / no-package state — toolbar handles the package picker now,
            // so the body just shows a friendly hint.
            if (_pkg == null)
            {
                EditorGUILayout.Space(8);
                if (_packageIds == null || _packageIds.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No packages found in Assets/_Project/Data/Packages/.\n" +
                        "Use the ↺ button in the toolbar to refresh after creating one.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Select a package in the toolbar above to begin authoring.",
                        MessageType.Info);
                }
                return;
            }

            EditorGUILayout.Space(4);
            DrawStepHeaderIMGUI();
            EditorGUILayout.Space(2);

            // Measure where top content ends (only accurate during Repaint, cached for other events)
            if (Event.current.type == EventType.Repaint)
                _topContentHeight = GUILayoutUtility.GetLastRect().yMax + 4f;

            // The canvas pane only owns the unified list and the actions bar.
            // The detail/batch context panels moved to the inspector pane in
            // Phase 4. The list height is the canvas height minus the measured
            // top content and the actions bar.
            const float kActionsH = 54f;
            float listH = Mathf.Max(position.height - _topContentHeight - kActionsH - 14f, 60f);
            DrawUnifiedList(listH);

            // ── Pinned actions bar (at the bottom of the canvas pane) ─────────
            EditorGUILayout.Space(4);
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true)),
                               new Color(0.13f, 0.13f, 0.13f));
            EditorGUILayout.Space(3);
            DrawUnifiedActions();
        }

        // ── Inspector GUI (right pane) ────────────────────────────────────────
        //
        // Runs inside the inspector IMGUIContainer (right pane of the
        // three-pane shell). Hosts the detail/batch context panels for the
        // current selection — replaces the pre-Phase-4 pinned-bottom panel.
        // The selection-driven dispatch logic still lives in DrawBottomEditPanel
        // (in TTAW.UnifiedList.cs); this method just provides the scroll
        // container and a no-package fallback.

        private void DrawInspectorIMGUI()
        {
            if (_pkg == null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("(no package loaded)", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("INSPECTOR", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            DrawBottomEditPanel();
            EditorGUILayout.EndScrollView();
        }

        // ── Package picker — removed in Phase 2; lives in the UITK toolbar (TTAW.Shell.cs).

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

            // For split-layout packages insert the step into the correct assembly file.
            // For monolithic packages insert into machine.json.
            string targetJsonPath;
            if (PackageJsonUtils.IsSplitLayout(_pkgId) && !string.IsNullOrEmpty(assemblyId))
            {
                string asmFile = System.IO.Path.Combine(
                    PackageJsonUtils.AuthoringRoot, _pkgId, "assemblies", $"{assemblyId}.json");
                targetJsonPath = System.IO.File.Exists(asmFile) ? asmFile : null;
            }
            else
            {
                targetJsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            }

            if (string.IsNullOrEmpty(targetJsonPath) || !System.IO.File.Exists(targetJsonPath))
            {
                UnityEditor.EditorUtility.DisplayDialog("Error", "Could not locate the target JSON file.", "OK");
                return;
            }

            try
            {
                PackageJsonUtils.InsertStep(targetJsonPath, newStep);
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

        // ── Step header (form + info card) ────────────────────────────────────
        // The navigation row (prev/next buttons, step number scrubber, title)
        // moved to the UITK toolbar in Phase 2 (TTAW.Shell.cs). This method only
        // renders the parts that still live in IMGUI: the inline new-step form
        // (when toggled) and the per-step info card. It also performs the
        // SessionDriver poll that used to live in DrawStepFilter so external
        // step changes still reflect into the window.

        private void DrawStepHeaderIMGUI()
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

            // ── New step form (toggled by the toolbar's "+ New Step" button) ──
            if (_showNewStepForm) DrawNewStepForm();

            // ── Step info card (hidden in All Steps mode) ─────────────────────
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
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
            _editingPoseMode = PoseModeStart;           // always land on Start Pose when switching steps
            RespawnScene();                  // uses _editAssembledPose — must come AFTER the reset
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
                Vector3 newPos = Vector3FieldClip("Position (local)", t.position);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.position = newPos; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            if (fieldProfile.ShowRotation)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newEuler = Vector3FieldClip("Rotation (euler)", t.rotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.rotation = Quaternion.Euler(newEuler); t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }
            }

            if (fieldProfile.ShowScale)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newScale = Vector3FieldClip("Scale", t.scale);
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
                    Vector3FieldClip("Weld Axis (A→B)", t.weldAxis);
                    FloatFieldClip("Weld Length (|A→B|)", t.weldLength);
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newAxis = Vector3FieldClip("Weld Axis (direction)", t.weldAxis);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginEdit();
                        t.weldAxis = newAxis.sqrMagnitude > 0.001f ? newAxis.normalized : newAxis;
                        t.isDirty  = true;
                        EndEdit();
                        SceneView.RepaintAll();
                    }

                    EditorGUI.BeginChangeCheck();
                    float newLen = FloatFieldClip("Weld Length", t.weldLength);
                    if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.weldLength = Mathf.Max(0f, newLen); t.isDirty = true; EndEdit(); }
                }
            }

            if (fieldProfile.ShowPortFields && ShowPortGroup())
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPortA = Vector3FieldClip("Port A (local)", t.portA);
                if (EditorGUI.EndChangeCheck()) { BeginEdit(); t.portA = newPortA; t.isDirty = true; EndEdit(); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPortB = Vector3FieldClip("Port B (local)", t.portB);
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
            float batchX = FloatFieldClip("X", rep.position.x);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position.x = batchX; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchY = FloatFieldClip("Y", rep.position.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelected)
                { ref var t = ref _targets[idx]; t.position.y = batchY; t.isDirty = true; }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchZ = FloatFieldClip("Z", rep.position.z);
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
            Vector3 batchEuler = Vector3FieldClip("Rotation (euler)", rep.rotation.eulerAngles);
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
            Vector3 batchScale = Vector3FieldClip("Scale", rep.scale);
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
                Vector3 newAxis = Vector3FieldClip("Weld Axis (direction)", rep.weldAxis);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 norm = newAxis.sqrMagnitude > 0.001f ? newAxis.normalized : newAxis;
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.weldAxis = norm; t.isDirty = true; }
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                float newLen = FloatFieldClip("Weld Length", rep.weldLength);
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
                Vector3 newPortA = Vector3FieldClip("Port A (local)", rep.portA);
                if (EditorGUI.EndChangeCheck())
                {
                    foreach (int idx in _multiSelected)
                    { ref var t = ref _targets[idx]; t.portA = newPortA; t.isDirty = true; }
                    SceneView.RepaintAll();
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newPortB = Vector3FieldClip("Port B (local)", rep.portB);
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

        /// <summary>Returns the step ID for the currently selected step, or null if "All Steps".</summary>
        private string GetCurrentStepId()
        {
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                return _stepIds[_stepFilterIdx];
            return null;
        }

        private void DrawPartPoseToggle()
        {
            string currentStepId = GetCurrentStepId();
            List<StepPoseEntry> poses = null;
            bool hasPart = _selectedPartIdx >= 0 && _selectedPartIdx < (_parts?.Length ?? 0);
            if (hasPart) poses = _parts[_selectedPartIdx].stepPoses;
            int poseCount = poses?.Count ?? 0;

            EditorGUILayout.BeginHorizontal();

            // [Start Pose]
            bool isStart = _editingPoseMode == PoseModeStart;
            if (GUILayout.Toggle(isStart, "Start Pose", EditorStyles.miniButtonLeft) && !isStart)
                ApplyPoseMode(PoseModeStart);

            // [Custom 1] [Custom 2] … — all intermediate poses
            for (int i = 0; i < poseCount; i++)
            {
                string btnLabel = !string.IsNullOrEmpty(poses[i].label) ? poses[i].label : $"Custom {i + 1}";
                bool isSel = _editingPoseMode == i;
                if (GUILayout.Toggle(isSel, btnLabel, EditorStyles.miniButtonMid) && !isSel)
                    ApplyPoseMode(i);
            }

            // [Assembled Pose]
            bool isAssembled = _editingPoseMode == PoseModeAssembled;
            GUIStyle assembledStyle = (poseCount > 0 || hasPart) ? EditorStyles.miniButtonMid : EditorStyles.miniButtonRight;
            if (GUILayout.Toggle(isAssembled, "Assembled Pose", assembledStyle) && !isAssembled)
                ApplyPoseMode(PoseModeAssembled);

            // [+] add new custom pose
            if (hasPart)
            {
                if (GUILayout.Button("+", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    AddStepPoseForCurrentStep(_selectedPartIdx, currentStepId ?? "");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyPoseMode(int newMode)
        {
            _editingPoseMode = newMode;
            _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5;
            // SyncAllPartMeshesToActivePose is the final authority — it reads
            // _editingPoseMode and positions every live GO accordingly.
            // Do NOT call ApplySpawnerStepPositions here — that triggers the
            // runtime spawner pipeline which asynchronously overrides positions
            // (from StreamingAssets data) before OnSpawnerPartsReady corrects them,
            // causing a visible flicker/delay.
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
        }

        private void AddStepPoseForCurrentStep(int partIdx, string stepId)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[partIdx];

            // Initialize from whichever pose is currently active
            Vector3 initPos; Quaternion initRot; Vector3 initScl;
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                var src = p.stepPoses[_editingPoseMode];
                initPos = PackageJsonUtils.ToVector3(src.position);
                initRot = PackageJsonUtils.ToUnityQuaternion(src.rotation);
                initScl = PackageJsonUtils.ToVector3(src.scale);
            }
            else if (_editAssembledPose)
            {
                initPos = p.assembledPosition;
                initRot = p.assembledRotation;
                initScl = p.assembledScale;
            }
            else
            {
                initPos = p.startPosition;
                initRot = p.startRotation;
                initScl = p.startScale;
            }

            BeginPartEdit(partIdx);
            if (p.stepPoses == null) p.stepPoses = new List<StepPoseEntry>();
            p.stepPoses.Add(new StepPoseEntry
            {
                stepId   = stepId,
                position = PackageJsonUtils.ToFloat3(initPos),
                rotation = PackageJsonUtils.ToQuaternion(initRot),
                scale    = PackageJsonUtils.ToFloat3(initScl),
            });
            p.isDirty = true;
            EndPartEdit();
            _editingPoseMode = p.stepPoses.Count - 1; // select the newly added pose
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ShowStepIdPickerMenu(int partIdx, int poseIndex)
        {
            var menu = new GenericMenu();
            var steps = _pkg?.steps;
            if (steps == null) { menu.AddDisabledItem(new GUIContent("No steps available")); menu.ShowAsContext(); return; }

            foreach (var step in steps)
            {
                if (step == null) continue;
                string sid = step.id;
                string label = step.GetDisplayName() ?? sid;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
                    ref PartEditState pp = ref _parts[partIdx];
                    if (pp.stepPoses == null || poseIndex < 0 || poseIndex >= pp.stepPoses.Count) return;
                    BeginPartEdit(partIdx);
                    pp.stepPoses[poseIndex].stepId = sid;
                    pp.isDirty = true;
                    EndPartEdit();
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void RemoveStepPose(int partIdx, int poseIndex)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[partIdx];
            if (p.stepPoses == null || poseIndex < 0 || poseIndex >= p.stepPoses.Count) return;
            BeginPartEdit(partIdx);
            p.stepPoses.RemoveAt(poseIndex);
            p.isDirty = true;
            EndPartEdit();
            _editingPoseMode = PoseModeAssembled;
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
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
            Vector3 newEuler = Vector3FieldClip("Rotation", euler);
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

            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                // ── Custom pose fields ───────────────────────────────────────
                var sp = p.stepPoses[_editingPoseMode];

                // Label (display name)
                EditorGUI.BeginChangeCheck();
                string newLabel = EditorGUILayout.TextField("Name", sp.label ?? "");
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.label = newLabel; p.isDirty = true; EndPartEdit(); Repaint(); }

                // Step ID — which step activates this pose
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                string newStepId = EditorGUILayout.TextField("Step ID", sp.stepId ?? "");
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.stepId = newStepId; p.isDirty = true; EndPartEdit(); }
                if (GUILayout.Button("Pick", EditorStyles.miniButton, GUILayout.Width(40)))
                    ShowStepIdPickerMenu(_selectedPartIdx, _editingPoseMode);
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                Vector3 spPos = Vector3FieldClip("Position", PackageJsonUtils.ToVector3(sp.position));
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.position = PackageJsonUtils.ToFloat3(spPos); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 spEuler = Vector3FieldClip("Rotation", PackageJsonUtils.ToUnityQuaternion(sp.rotation).eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.rotation = PackageJsonUtils.ToQuaternion(Quaternion.Euler(spEuler)); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 spScl = Vector3FieldClip("Scale", PackageJsonUtils.ToVector3(sp.scale));
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.scale = PackageJsonUtils.ToFloat3(spScl); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove Pose", EditorStyles.miniButton, GUILayout.Width(100)))
                    RemoveStepPose(_selectedPartIdx, _editingPoseMode);
                EditorGUILayout.EndHorizontal();
            }
            else if (_editAssembledPose)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newPlayPos = Vector3FieldClip("Play Position", p.assembledPosition);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledPosition = newPlayPos; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 playEuler = Vector3FieldClip("Play Rotation", p.assembledRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledRotation = Quaternion.Euler(playEuler); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPlayScale = Vector3FieldClip("Play Scale", p.assembledScale);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledScale = newPlayScale; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newStartPos = Vector3FieldClip("Start Position", p.startPosition);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startPosition = newStartPos; p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 startEuler = Vector3FieldClip("Start Rotation", p.startRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startRotation = Quaternion.Euler(startEuler); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newStartScale = Vector3FieldClip("Start Scale", p.startScale);
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
            string poseLabel = _editingPoseMode >= 0 ? "Step Pose" : (_editAssembledPose ? "Assembled Pose" : "Start Pose");
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
            Vector3 repPos = _editAssembledPose ? rep.assembledPosition : rep.startPosition;

            EditorGUI.BeginChangeCheck();
            float batchX = FloatFieldClip("X", repPos.x);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.x  = batchX;
                    else               p2.startPosition.x = batchX;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchY = FloatFieldClip("Y", repPos.y);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.y  = batchY;
                    else               p2.startPosition.y = batchY;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float batchZ = FloatFieldClip("Z", repPos.z);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.z  = batchZ;
                    else               p2.startPosition.z = batchZ;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Rotation (absolute) ──────────────────────────────────────────
            EditorGUILayout.LabelField("Rotation (all selected)", EditorStyles.boldLabel);
            Quaternion repRot = _editAssembledPose ? rep.assembledRotation : rep.startRotation;
            EditorGUI.BeginChangeCheck();
            Vector3 batchEuler = Vector3FieldClip("Euler", repRot.eulerAngles);
            if (EditorGUI.EndChangeCheck())
            {
                Quaternion batchRot = Quaternion.Euler(batchEuler);
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledRotation  = batchRot;
                    else               p2.startRotation = batchRot;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(4);

            // ── Scale (absolute) ─────────────────────────────────────────────
            EditorGUILayout.LabelField("Scale (all selected)", EditorStyles.boldLabel);
            Vector3 repScale = _editAssembledPose ? rep.assembledScale : rep.startScale;
            EditorGUI.BeginChangeCheck();
            Vector3 batchScale = Vector3FieldClip("Scale", repScale);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledScale  = batchScale;
                    else               p2.startScale = batchScale;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
            EditorGUILayout.Space(8);

            // ── Position offset (delta) ──────────────────────────────────────
            EditorGUILayout.LabelField("Position Offset (delta)", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            float dx = FloatFieldClip("Delta X", 0f);
            if (EditorGUI.EndChangeCheck() && dx != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.x  += dx;
                    else               p2.startPosition.x += dx;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float dy = FloatFieldClip("Delta Y", 0f);
            if (EditorGUI.EndChangeCheck() && dy != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.y  += dy;
                    else               p2.startPosition.y += dy;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }

            EditorGUI.BeginChangeCheck();
            float dz = FloatFieldClip("Delta Z", 0f);
            if (EditorGUI.EndChangeCheck() && dz != 0f)
            {
                foreach (int idx in _multiSelectedParts)
                {
                    ref PartEditState p2 = ref _parts[idx];
                    if (_editAssembledPose) p2.assembledPosition.z  += dz;
                    else               p2.startPosition.z += dz;
                    p2.isDirty = true; SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
        }
    }
}
