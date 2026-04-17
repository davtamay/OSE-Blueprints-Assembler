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
using UnityEditor.IMGUI.Controls;
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

            // Entity-level detail (part transform, target transform, wire, batch)
            DrawBottomEditPanel();

            // ── Contextual sections (moved from canvas in the redesign) ───────
            // These appear BELOW the entity detail so the primary authoring
            // surface (position/rotation/scale) is always visible without
            // scrolling. The sections that appear depend on what's selected.
            DrawInspectorContextualSections();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws the sections that moved from the canvas to the inspector:
        /// tool toggles, group membership, animation cues, particle effects.
        /// Dispatches by selection kind so only relevant sections appear.
        /// </summary>
        private void DrawInspectorContextualSections()
        {
            // Resolve the current step
            StepDefinition step = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                step = FindStep(_stepIds[_stepFilterIdx]);

            // ── Per-selection sections ────────────────────────────────────────
            if (_selectedTaskSeqIdx >= 0 && step != null)
            {
                var order = GetOrDeriveTaskOrder(step);
                if (order != null && _selectedTaskSeqIdx < order.Count)
                {
                    var entry = order[_selectedTaskSeqIdx];
                    switch (entry.kind)
                    {
                        case "part":
                        {
                            // Group entries reuse the "part" kind. Their own cue
                            // strip is rendered by DrawTaskInspectorBody — don't
                            // double up with a part-scope strip here.
                            bool entryIsGroup = _pkg != null
                                && _pkg.TryGetSubassembly(entry.id, out var _grp)
                                && _grp != null;

                            if (!entryIsGroup)
                            {
                                // Inline tool toggles (replaces the Part×Tool matrix)
                                DrawInlineToolToggles(entry.id);

                                // Group membership
                                DrawInspectorGroupLabel(entry.id);

                                // Animation cues for this part live in the step
                                // task-sequence via DrawCuesForPart — same
                                // timing-panels UI used for groups and tools.
                                // Host-owned storage (part.animationCues) is
                                // the single source of truth.
                                DrawCuesForPart(step, entry.id);
                            }
                            break;
                        }
                        default: // toolAction, target, wire, confirm
                        {
                            // Animation cues for the tool (if wired)
                            if (step.requiredToolActions != null)
                            {
                                foreach (var a in step.requiredToolActions)
                                {
                                    if (a?.targetId == entry.id && !string.IsNullOrEmpty(a.toolId))
                                    { DrawCuesForTool(step, a.toolId); break; }
                                }
                            }

                            // Particle effects
                            EditorGUILayout.Space(6);
                            DrawParticleEffectsSection(step);
                            break;
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(_canvasSelectedSubId) && step != null)
            {
                // A subassembly is selected in the canvas list
                DrawInspectorForSubassembly(step, _canvasSelectedSubId);
            }
            else if (step != null)
            {
                // Step-level animation authoring is removed. Cues now live
                // on the host (part / subassembly / aggregate) — select a
                // part or group to author its Animations & Effects. The
                // legacy step.animationCues / particle-effects blocks
                // remain readable at runtime as a fallback for unmigrated
                // packages, but new authoring goes through the
                // selection-scoped inspector.
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "Select a part or group to author its Animations & Effects.",
                    MessageType.Info);
            }
        }

        /// <summary>
        /// Draws inline tool toggles for a part: one toggle per package tool.
        /// Replaces the canvas-level Part×Tool matrix with a simple,
        /// zero-cognitive-overhead set of checkboxes.
        /// </summary>
        private void DrawInlineToolToggles(string partId)
        {
            if (_pkg?.tools == null || _pkg.tools.Length == 0 || string.IsNullOrEmpty(partId))
                return;

            var part = FindPartById(partId);
            if (part == null) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Tools for this part:", EditorStyles.miniBoldLabel);

            var currentToolIds = part.toolIds != null
                ? new HashSet<string>(part.toolIds, System.StringComparer.Ordinal)
                : new HashSet<string>(System.StringComparer.Ordinal);

            EditorGUILayout.BeginHorizontal();
            int col = 0;
            foreach (var tool in _pkg.tools)
            {
                if (tool == null || string.IsNullOrEmpty(tool.id)) continue;

                bool isOn  = currentToolIds.Contains(tool.id);
                var style  = new GUIStyle(EditorStyles.miniButton)
                {
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = isOn
                        ? new Color(0.20f, 0.62f, 0.95f)  // blue accent
                        : new Color(0.50f, 0.50f, 0.55f) },
                };
                string label = (isOn ? "▣ " : "☐ ") + tool.GetDisplayName();
                if (GUILayout.Button(label, style, GUILayout.Height(18)))
                    ToggleToolForPart(part, tool.id, !isOn);

                col++;
                // Wrap to next row after every 2 tools to fit narrow inspector
                if (col % 2 == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Full inspector panel for a selected subassembly from the canvas list.
        /// Shows: name, orientation (read-only values from the gizmo), part
        /// membership, step membership, and inline editor for name/description.
        /// </summary>
        private void DrawInspectorForSubassembly(StepDefinition step, string subId)
        {
            if (!_pkg.TryGetSubassembly(subId, out SubassemblyDefinition sub) || sub == null)
            {
                EditorGUILayout.LabelField($"Subassembly '{subId}' not found.", EditorStyles.miniLabel);
                return;
            }

            // Header
            var headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                fontSize = 12,
            };
            EditorGUILayout.LabelField($"Group: {sub.GetDisplayName()}", headerStyle);
            EditorGUILayout.Space(4);

            // Orientation readout (from the gizmo — read-only display)
            if (step.workingOrientation != null)
            {
                var wo = step.workingOrientation;
                var rotStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.65f, 0.70f, 0.80f) },
                };
                string rotText = $"Orientation: ({wo.subassemblyRotation.x:F1}°, {wo.subassemblyRotation.y:F1}°, {wo.subassemblyRotation.z:F1}°)";
                string ofsText = $"Offset: ({wo.subassemblyPositionOffset.x:F3}, {wo.subassemblyPositionOffset.y:F3}, {wo.subassemblyPositionOffset.z:F3})";
                EditorGUILayout.LabelField(rotText, rotStyle);
                EditorGUILayout.LabelField(ofsText, rotStyle);
                EditorGUILayout.LabelField("Use the gizmo in the SceneView to adjust.",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic,
                        normal = { textColor = new Color(0.55f, 0.55f, 0.60f) } });
            }
            else
            {
                EditorGUILayout.LabelField("No working orientation on this step.",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4);

            // Inline editor (name, description, parts, steps)
            DrawSubassemblyInlineEditor(sub, step);

            // Animation cue authoring for the selected subassembly is handled
            // by DrawCuesForSubassembly in the task-sequence inspector
            // (TTAW.UnifiedList.cs:DrawTaskInspectorBody). No host-section
            // editor here — one authoring surface keeps cues off the "two
            // systems" split.
        }

        /// <summary>
        /// Shows a compact "Group: X" label with the part's subassembly
        /// membership. Expandable on click to show the full group detail.
        /// </summary>
        private void DrawInspectorGroupLabel(string partId)
        {
            if (_pkg == null || string.IsNullOrEmpty(partId)) return;

            var allSubs = _pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0) return;

            // Find which subassembly contains this part
            SubassemblyDefinition ownerSub = null;
            foreach (var sub in allSubs)
            {
                if (sub?.partIds == null) continue;
                foreach (var pid in sub.partIds)
                {
                    if (string.Equals(pid, partId, System.StringComparison.Ordinal))
                    { ownerSub = sub; break; }
                }
                if (ownerSub != null) break;
            }

            EditorGUILayout.Space(4);
            if (ownerSub == null)
            {
                EditorGUILayout.LabelField("Group: (none — standalone part)",
                    EditorStyles.miniLabel);
                return;
            }

            int parts = ownerSub.partIds?.Length ?? 0;
            int steps = ownerSub.stepIds?.Length ?? 0;
            var groupStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.20f, 0.62f, 0.95f) }, // blue
                fontStyle = FontStyle.Bold,
            };
            EditorGUILayout.LabelField(
                $"Group: {ownerSub.GetDisplayName()}  ·  {parts}p · {steps}s",
                groupStyle);
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
                    // Family row — always visible so the author can see at a
                    // glance whether this step is "Place" (placement-owning)
                    // vs "Use / Confirm / Connect". Matches the Rule-2
                    // constraint surfaced by PartOwnershipExclusivityPass.
                    var familyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = step.ResolvedFamily == StepFamily.Place
                            ? new Color(0.30f, 0.78f, 0.36f) : new Color(0.72f, 0.72f, 0.78f) },
                    };
                    EditorGUILayout.LabelField($"Family: {step.ResolvedFamily}", familyStyle);
                    EditorGUILayout.LabelField(
                        $"Tool: {toolName}{profileStr}  ·  {tCount} target{(tCount == 1 ? "" : "s")}",
                        EditorStyles.miniLabel);
                    // "Owns" row — only on Place-family steps, lists the parts
                    // this step is the Place owner of, with an inline red chip
                    // for any part that also appears in another Place step
                    // (Rule-2 collision caught live).
                    if (step.ResolvedFamily == StepFamily.Place && _ownership != null
                        && step.requiredPartIds != null && step.requiredPartIds.Length > 0)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Owns:", EditorStyles.miniLabel, GUILayout.Width(40));
                        bool any = false;
                        foreach (var pid in step.requiredPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            var o = _ownership.ForPart(pid);
                            // List the part under every Place step that
                            // requires it — multi-placement is supported, so
                            // this step may be one of several owners.
                            bool isOwnedHere = false;
                            if (o.placeStepIds != null)
                                foreach (var sid in o.placeStepIds)
                                    if (string.Equals(sid, step.id, StringComparison.Ordinal)) { isOwnedHere = true; break; }
                            if (!isOwnedHere) continue;
                            var chipStyle = new GUIStyle(EditorStyles.miniLabel);
                            if (o.HasMultiplePlaces)
                            {
                                // Info styling (blue), not error — multiple
                                // placements of the same part is a supported
                                // authoring pattern.
                                chipStyle.normal.textColor = new Color(0.55f, 0.78f, 0.95f);
                                GUILayout.Label(new GUIContent("↺ " + pid,
                                    "Multi-placed in Place steps: " + string.Join(", ", o.placeStepIds)),
                                    chipStyle);
                            }
                            else
                            {
                                GUILayout.Label(pid, chipStyle);
                            }
                            GUILayout.Space(6);
                            any = true;
                        }
                        if (!any) GUILayout.Label("(none)", new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic });
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();
                    }
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
            // Clear group selection on step change — a stale selection from
            // an earlier step can drive the first pose-sync pass before the
            // current step's context settles, producing "Start pose on jump,
            // corrects on re-click." The author re-selects when needed.
            _selectedGroupIdx     = -1;
            _canvasSelectedSubId  = null;
            InvalidateTaskOrderCache();
            UpdateActiveStep();
            BuildTargetList();
            BuildPartList();
            _editingPoseMode = PoseModeStart;           // always land on Start Pose when switching steps
            RespawnScene();                  // uses _editAssembledPose — must come AFTER the reset
            SyncAllPartMeshesToActivePose(); // second pass: ensures live GOs match after RespawnScene
            // Spawner writes member localPositions against PreviewRoot. Snap
            // group roots back to origin (preserving children's world pose) so
            // the spawner's local writes don't compound with a centroid offset.
            ResetAllGroupRootsToOriginPreservingChildren();
            ApplySpawnerStepPositions();     // first pass: push step-aware positions before driver sync
            AutoSelectFirstTaskEntry();      // default-select first badge so a section is visible
            if (!_suppressStepSync)
                SyncSessionDriverStep();
            // Final pass: re-apply after SyncSessionDriverStep, because SetEditModeStep →
            // ApplyStepAwarePartPositions uses _editModePackage (StreamingAssets) which may
            // override the authoritative _pkg positions set above.
            ResetAllGroupRootsToOriginPreservingChildren();
            ApplySpawnerStepPositions();
            SyncAllPartMeshesToActivePose();
            // Final authoring override: re-activate & reparent group members
            // that the spawner may have hidden as ghost-replaced. Must run AFTER
            // every spawner/driver positioning pass above.
            ActivateAllVisibleGroupMembers();
            // Re-apply centroid centering for the selected group now that all
            // spawner passes are complete and safe from local-position compounding.
            SyncAllGroupRootsToActivePose();
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

        /// <summary>
        /// Ownership summary for the Part Context inspector. Answers
        /// "where does this part live?" at a glance: which Place-family
        /// step physically places it, which subassembly owns it, and every
        /// step that Requires / makes Optional / renders it as a visual.
        /// Each seq chip is clickable and jumps the step filter.
        ///
        /// Conflict banners (Rule 1 / Rule 2 from
        /// <see cref="OSE.Content.Validation.PartOwnershipExclusivityPass"/>)
        /// only appear when a violation is actually present — clean data
        /// renders a tidy summary and nothing else, per the "quiet when
        /// clean" directive.
        /// </summary>
        private void DrawPartOwnershipSection(string partId)
        {
            if (string.IsNullOrEmpty(partId) || _ownership == null) return;
            var own = _ownership.ForPart(partId);
            if (!own.IsReferenced && !own.HasAnyConflict && string.IsNullOrEmpty(own.subassemblyId))
                return; // nothing to show — part authored but unused.

            EditorGUILayout.Space(4);
            var hdr = new GUIStyle(EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("Ownership", hdr);

            var lblStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            var valStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };

            // Subassembly
            if (!string.IsNullOrEmpty(own.subassemblyId))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("  Subassembly:", lblStyle, GUILayout.Width(110));
                GUILayout.Label(own.subassemblyId, valStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Place owner
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("  Place owner:", lblStyle, GUILayout.Width(110));
            if (own.HasPlaceOwner)
                DrawStepChip(own.placeStepSeq, own.placeStepId);
            else
                GUILayout.Label("  (not yet placed)", new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic });
            EditorGUILayout.EndHorizontal();

            // Required / Optional / Visual step lists — each rendered on its own row.
            DrawOwnershipSeqList("Required in:",  own.requiredAtSeqs);
            DrawOwnershipSeqList("Optional in:",  own.optionalAtSeqs);
            DrawOwnershipSeqList("Visual in:",    own.visualAtSeqs);

            // Informational "multi-placement" notice — not a conflict. The
            // runtime handles multiple Place owners by walking the seq-sorted
            // list and using the most recent one ≤ current step. Shown so
            // the author can verify the content is intentional.
            if (own.HasMultiplePlaces)
            {
                var info = new GUIStyle(EditorStyles.helpBox)
                {
                    normal    = { textColor = new Color(0.55f, 0.78f, 0.95f) },
                    wordWrap  = true,
                };
                EditorGUILayout.LabelField(
                    "  ↺ Multi-placement — Required in " + own.placeStepSeqs.Length +
                    " Place steps: " + string.Join(", ", own.placeStepIds) +
                    ". The runtime picks the most recent placement ≤ current step.",
                    info);
            }
            if (own.HasSubConflict)
            {
                var warn = new GUIStyle(EditorStyles.helpBox)
                {
                    normal    = { textColor = new Color(0.90f, 0.68f, 0.25f) },
                    fontStyle = FontStyle.Bold,
                    wordWrap  = true,
                };
                EditorGUILayout.LabelField(
                    "  ⚠ Subassembly conflict: claimed by " +
                    string.Join(", ", own.conflictingSubassemblyIds) +
                    ". Each partId must belong to exactly one non-aggregate subassembly.",
                    warn);
            }
        }

        /// <summary>
        /// Row showing "Label: [#seq] [#seq] ... +N" with clickable chips.
        /// Caps visible chips so the inspector's narrow width doesn't get
        /// blown out by parts that appear in 10+ steps; overflow goes into
        /// a tooltip on the "+N" badge.
        /// </summary>
        private void DrawOwnershipSeqList(string label, int[] seqs)
        {
            if (seqs == null || seqs.Length == 0) return;
            const int MaxVisibleChips = 6;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"  {label}", new GUIStyle(EditorStyles.miniLabel), GUILayout.Width(90));
            int visible = Mathf.Min(seqs.Length, MaxVisibleChips);
            for (int i = 0; i < visible; i++)
                DrawStepChip(seqs[i], _ownership?.StepIdForSeq(seqs[i]));
            if (seqs.Length > MaxVisibleChips)
            {
                var extra = new System.Text.StringBuilder();
                for (int i = MaxVisibleChips; i < seqs.Length; i++)
                {
                    if (i > MaxVisibleChips) extra.Append(", ");
                    string sid = _ownership?.StepIdForSeq(seqs[i]);
                    extra.Append(string.IsNullOrEmpty(sid) ? $"#{seqs[i]}" : $"#{seqs[i]} {sid}");
                }
                GUILayout.Label(new GUIContent($"+{seqs.Length - MaxVisibleChips}",
                    "Also in: " + extra),
                    EditorStyles.miniLabel, GUILayout.Width(28f));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// A compact clickable step chip (just "#seq") with the full stepId
        /// in the hover tooltip. Narrow by design so many chips fit in the
        /// inspector's cramped width without squishing the pose toggle row
        /// or overflowing the Part Context panel.
        /// </summary>
        private void DrawStepChip(int seq, string stepId)
        {
            string tip = !string.IsNullOrEmpty(stepId)
                ? $"Jump to step {stepId} (#{seq})"
                : $"Jump to step #{seq}";
            if (GUILayout.Button(new GUIContent($"#{seq}", tip),
                    EditorStyles.miniButton, GUILayout.Width(36f)))
                JumpStepFilterToSeq(seq);
        }

        /// <summary>Route a click on a step chip through the shared step-filter hook.</summary>
        private void JumpStepFilterToSeq(int seq)
        {
            if (_stepSequenceIdxs == null) return;
            for (int i = 0; i < _stepSequenceIdxs.Length; i++)
            {
                if (_stepSequenceIdxs[i] != seq) continue;
                ApplyStepFilter(i);
                return;
            }
        }

        private void DrawPartPoseToggle()
        {
            string currentStepId = GetCurrentStepId();
            List<StepPoseEntry> poses = null;
            bool hasPart = _selectedPartIdx >= 0 && _selectedPartIdx < (_parts?.Length ?? 0);
            if (hasPart) poses = _parts[_selectedPartIdx].stepPoses;
            int poseCount = poses?.Count ?? 0;

            // Detect NO TASK on current step — used to surface a dedicated
            // "NO TASK pose" toggle alongside Start / Assembled / Custom so
            // the author can switch between every relevant pose mode in one
            // place.
            int noTaskAutoIdx = -1;
            if (hasPart && !string.IsNullOrEmpty(currentStepId))
            {
                var curStep = FindStep(currentStepId);
                if (curStep != null && curStep.visualPartIds != null
                    && Array.IndexOf(curStep.visualPartIds, _parts[_selectedPartIdx].def?.id) >= 0
                    && poses != null)
                {
                    for (int i = 0; i < poses.Count; i++)
                    {
                        var e = poses[i];
                        if (e == null) continue;
                        if (string.Equals(e.stepId, currentStepId, StringComparison.Ordinal)) { noTaskAutoIdx = i; break; }
                    }
                }
            }

            // For NO TASK parts the Start/Assembled toggles are irrelevant
            // (NO TASK pose IS startPosition; assembledPosition has no role
            // here). Only show NO TASK + Custom toggles. For Required/
            // Optional parts, the full Start/Custom/Assembled set is shown.
            bool isNoTaskRow = noTaskAutoIdx >= 0;

            EditorGUILayout.BeginHorizontal();

            if (!isNoTaskRow)
            {
                // [Start Pose]
                bool isStart = _editingPoseMode == PoseModeStart;
                if (GUILayout.Toggle(isStart, "Start Pose", EditorStyles.miniButtonLeft) && !isStart)
                    ApplyPoseMode(PoseModeStart);
            }
            else
            {
                // [NO TASK pose] — anchor toggle for NO TASK rows.
                bool isNoTask = _editingPoseMode == noTaskAutoIdx;
                if (GUILayout.Toggle(isNoTask, "NO TASK pose", EditorStyles.miniButtonLeft) && !isNoTask)
                    ApplyPoseMode(noTaskAutoIdx);
            }

            // [Custom 1] [×] [Custom 2] [×] … — author-authored intermediate
            // poses with an inline delete button. Synthetic NO-TASK waypoints
            // (label starts with AutoNoTaskLabel) are computed every load and
            // never persist; they shouldn't surface as toggleable Custom
            // entries. The × renders alongside its toggle so removal is one
            // click — no need to select-then-scroll-to-Remove-Pose.
            int customCounter = 0;
            int pendingRemoveIdx = -1;
            var xStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize  = 9,
                normal    = { textColor = new Color(0.95f, 0.55f, 0.55f) },
                fontStyle = FontStyle.Bold,
                padding   = new RectOffset(0, 0, 0, 0),
            };
            for (int i = 0; i < poseCount; i++)
            {
                var entry = poses[i];
                if (entry == null) continue;
                if (i == noTaskAutoIdx) continue; // already surfaced as NO TASK toggle
                if (!string.IsNullOrEmpty(entry.label)
                    && entry.label.StartsWith(OSE.Content.Loading.MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                    continue;
                customCounter++;
                string btnLabel = !string.IsNullOrEmpty(entry.label) ? entry.label : $"Custom {customCounter}";
                bool isSel = _editingPoseMode == i;
                if (GUILayout.Toggle(isSel, btnLabel, EditorStyles.miniButtonMid) && !isSel)
                    ApplyPoseMode(i);
                if (GUILayout.Button(new GUIContent("×", $"Delete '{btnLabel}'"),
                        xStyle, GUILayout.Width(16)))
                    pendingRemoveIdx = i;
            }
            if (pendingRemoveIdx >= 0)
            {
                RemoveStepPose(_selectedPartIdx, pendingRemoveIdx);
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (!isNoTaskRow)
            {
                // [Assembled Pose] — task parts only.
                bool isAssembled = _editingPoseMode == PoseModeAssembled;
                GUIStyle assembledStyle = (poseCount > 0 || hasPart) ? EditorStyles.miniButtonMid : EditorStyles.miniButtonRight;
                if (GUILayout.Toggle(isAssembled, "Assembled Pose", assembledStyle) && !isAssembled)
                    ApplyPoseMode(PoseModeAssembled);
            }

            // [+] add new custom pose
            if (hasPart)
            {
                if (GUILayout.Button("+", EditorStyles.miniButtonRight, GUILayout.Width(24)))
                    AddStepPoseForCurrentStep(_selectedPartIdx, currentStepId ?? "");
            }

            EditorGUILayout.EndHorizontal();

            // Propagation UI removed — confusing and rarely needed. The
            // underlying span fields remain in the data so the synthetic NO
            // TASK auto-bake can bound itself before the part's first task
            // step. Authors interact only with Start / Assembled / Custom /
            // NO TASK toggles + Position/Rotation/Scale fields.
            // Transform fields for Required / Optional / Custom poses are
            // rendered by DrawPartDetailPanel below — don't duplicate them
            // up here or the inspector shows two identical blocks.
        }

        /// <summary>
        /// Compact strip beneath the pose-toggle row: shows the selected
        /// stepPose's anchor step, a preset dropdown that writes
        /// <c>propagateFromStep</c>/<c>propagateThroughStep</c> in one click,
        /// a resolved-range chip (e.g. "steps 5–11"), and a remove button.
        /// Mirrors the same data on groups via <see cref="DrawGroupStepPoseDetailRow"/>.
        /// </summary>
        private void DrawStepPoseDetailRow(ref PartEditState p, int poseIdx)
        {
            if (p.stepPoses == null || poseIdx < 0 || poseIdx >= p.stepPoses.Count) return;
            var pose = p.stepPoses[poseIdx];

            EditorGUILayout.BeginHorizontal();
            var smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("Anchor", smallLabel, GUILayout.Width(46));
            string anchorLabel = string.IsNullOrEmpty(pose.stepId) ? "(none)" : StepShortLabel(pose.stepId);
            if (GUILayout.Button(anchorLabel, EditorStyles.miniButton, GUILayout.MinWidth(80)))
                ShowStepIdPickerMenu(_selectedPartIdx, poseIdx);

            GUILayout.Label("Apply to", smallLabel, GUILayout.Width(52));
            string applyLabel = DescribeSpan(pose);
            if (GUILayout.Button(applyLabel, EditorStyles.popup, GUILayout.MinWidth(130)))
                ShowStepPoseSpanMenuForPart(_selectedPartIdx, poseIdx);

            GUILayout.FlexibleSpace();

            // Resolved range chip
            string rangeTxt = ResolveSpanChip(pose);
            if (!string.IsNullOrEmpty(rangeTxt))
            {
                var chipStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.78f, 0.95f) },
                    alignment = TextAnchor.MiddleRight,
                };
                GUILayout.Label(rangeTxt, chipStyle, GUILayout.MinWidth(80));
            }

            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
                RemoveStepPose(_selectedPartIdx, poseIdx);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Short human-friendly label for a step id (sequence # + display name trimmed).</summary>
        private string StepShortLabel(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || _pkg?.steps == null) return stepId ?? "";
            foreach (var s in _pkg.steps)
            {
                if (s == null) continue;
                if (!string.Equals(s.id, stepId, StringComparison.Ordinal)) continue;
                string name = s.GetDisplayName() ?? s.id;
                return $"[{s.sequenceIndex}] {name}";
            }
            return stepId;
        }

        /// <summary>Label for the "Apply to" dropdown — describes the current span.</summary>
        private string DescribeSpan(StepPoseEntry pose)
        {
            bool fromEmpty    = string.IsNullOrEmpty(pose.propagateFromStep);
            bool throughEmpty = string.IsNullOrEmpty(pose.propagateThroughStep);
            if (fromEmpty && throughEmpty) return "This step → end (default)";

            string first  = FirstStepId();
            string last   = LastStepId();
            bool fromIsAnchor    = !fromEmpty    && string.Equals(pose.propagateFromStep,    pose.stepId, StringComparison.Ordinal);
            bool throughIsAnchor = !throughEmpty && string.Equals(pose.propagateThroughStep, pose.stepId, StringComparison.Ordinal);
            bool fromIsFirst     = !fromEmpty    && !string.IsNullOrEmpty(first) && string.Equals(pose.propagateFromStep,    first, StringComparison.Ordinal);
            bool throughIsLast   = !throughEmpty && !string.IsNullOrEmpty(last)  && string.Equals(pose.propagateThroughStep, last,  StringComparison.Ordinal);

            if (fromIsAnchor && throughIsAnchor) return "Just this step";
            if (fromIsFirst && throughIsAnchor)  return "From start → this step";
            if (fromIsAnchor && throughIsLast)   return "This step → end";
            if (fromIsFirst && throughIsLast)    return "All steps";
            return "Fixed range…";
        }

        /// <summary>Visible chip showing the resolved span as sequence numbers.</summary>
        private string ResolveSpanChip(StepPoseEntry pose)
        {
            if (_pkg?.steps == null || _pkg.steps.Length == 0) return "";
            int fromSeq = SeqIndexOf(pose.propagateFromStep, pose.stepId, useAnchorIfMissing: true);
            int throughSeq = SeqIndexOf(pose.propagateThroughStep, pose.stepId, useAnchorIfMissing: false);
            if (fromSeq < 0 && throughSeq < 0) return "";
            string fromTxt    = fromSeq    >= 0 ? fromSeq.ToString()    : "start";
            string throughTxt = throughSeq >= 0 ? throughSeq.ToString() : "end";
            return $"steps {fromTxt}–{throughTxt}";
        }

        private int SeqIndexOf(string stepId, string anchorFallback, bool useAnchorIfMissing)
        {
            string id = string.IsNullOrEmpty(stepId) && useAnchorIfMissing ? anchorFallback : stepId;
            if (string.IsNullOrEmpty(id) || _pkg?.steps == null) return -1;
            foreach (var s in _pkg.steps)
                if (s != null && string.Equals(s.id, id, StringComparison.Ordinal))
                    return s.sequenceIndex;
            return -1;
        }

        // Authoring preference: when picking propagation span, show only the
        // steps that reference the currently-selected part by default. The
        // author flips this off to pick from every step in the package.
        private bool _propagationFilterSamePart = true;

        /// <summary>
        /// Inline "From / Through" picker row. Replaces the old "Apply to range…"
        /// preset menu with two searchable step pickers — the author picks the
        /// two endpoints directly. Same-part filtering defaults ON so the pickers
        /// show only the relevant slice of a 300+ step package.
        /// </summary>
        private void DrawPartPropagationRow(string currentStepId, List<StepPoseEntry> poses)
        {
            ref PartEditState p = ref _parts[_selectedPartIdx];

            // Live "from" / "through" string values come from the active entry
            // if one exists for the current step; otherwise blanks (we'll
            // materialize on first pick).
            int activeIdx = _editingPoseMode >= 0 && poses != null && _editingPoseMode < poses.Count
                ? _editingPoseMode : -1;
            string fromId    = activeIdx >= 0 ? poses[activeIdx].propagateFromStep    : "";
            string throughId = activeIdx >= 0 ? poses[activeIdx].propagateThroughStep : "";

            var header = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
            string poseName = _editingPoseMode == PoseModeStart     ? "Start pose"
                            : _editingPoseMode == PoseModeAssembled ? "Assembled pose"
                            : (activeIdx >= 0
                                ? (string.IsNullOrEmpty(poses[activeIdx].label) ? $"Custom {activeIdx + 1}" : poses[activeIdx].label)
                                : "Pose");

            EditorGUILayout.LabelField($"Propagate {poseName}", header);

            // Filter toggle
            string partId = p.def?.id;
            _propagationFilterSamePart = EditorGUILayout.ToggleLeft(
                new GUIContent("Only steps using this part",
                    "When on, the From/Through pickers list only steps whose requiredPartIds, visualPartIds, optionalPartIds, or requiredSubassemblyId reference this part. Flip off to pick from every step."),
                _propagationFilterSamePart, EditorStyles.miniLabel);

            // From / Through buttons — each opens a searchable StepPickerDropdown.
            EditorGUILayout.BeginHorizontal();
            var smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("From", smallLabel, GUILayout.Width(40));
            if (GUILayout.Button(FormatStepButtonLabel(fromId, emptyFallback: "(start of package)"),
                    EditorStyles.popup, GUILayout.MinWidth(150)))
            {
                Func<StepDefinition, bool> filter = _propagationFilterSamePart ? (Func<StepDefinition, bool>)(s => StepTouchesPart(s, partId)) : null;
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, filter, currentStepId,
                        sid => SetPartPropagationEndpoint(currentStepId, fromEndpoint: true, stepId: sid));
            }

            GUILayout.Label("Through", smallLabel, GUILayout.Width(58));
            if (GUILayout.Button(FormatStepButtonLabel(throughId, emptyFallback: "(end of package)"),
                    EditorStyles.popup, GUILayout.MinWidth(150)))
            {
                Func<StepDefinition, bool> filter = _propagationFilterSamePart ? (Func<StepDefinition, bool>)(s => StepTouchesPart(s, partId)) : null;
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, filter, currentStepId,
                        sid => SetPartPropagationEndpoint(currentStepId, fromEndpoint: false, stepId: sid));
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// True when <paramref name="step"/> references <paramref name="partId"/>
        /// through any of its part-touching fields — drives the "same part"
        /// filter in the propagation pickers.
        /// </summary>
        private bool StepTouchesPart(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return false;
            if (step.requiredPartIds != null)  foreach (var pid in step.requiredPartIds)  if (string.Equals(pid, partId, StringComparison.Ordinal)) return true;
            if (step.visualPartIds != null)    foreach (var pid in step.visualPartIds)    if (string.Equals(pid, partId, StringComparison.Ordinal)) return true;
            if (step.optionalPartIds != null)  foreach (var pid in step.optionalPartIds)  if (string.Equals(pid, partId, StringComparison.Ordinal)) return true;
            // Subassembly-member reach: a step placing a group covers every member part.
            if (!string.IsNullOrEmpty(step.requiredSubassemblyId) && _pkg != null
                && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out var sub) && sub?.partIds != null)
            {
                foreach (var pid in sub.partIds)
                    if (string.Equals(pid, partId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Compact label like "[27] Place frame bar" or the fallback when empty.</summary>
        private string FormatStepButtonLabel(string stepId, string emptyFallback)
        {
            if (string.IsNullOrEmpty(stepId)) return emptyFallback;
            if (_pkg?.steps != null)
                foreach (var s in _pkg.steps)
                    if (s != null && string.Equals(s.id, stepId, StringComparison.Ordinal))
                        return $"[{s.sequenceIndex}] {s.GetDisplayName()}";
            return stepId;
        }

        /// <summary>
        /// Writes one endpoint of the propagation span. If the current pose is
        /// Start/Assembled, materializes a new stepPose anchored to the current
        /// step before writing the endpoint — so the author never has to click
        /// "+" first. Leaves the other endpoint unchanged.
        /// </summary>
        private void SetPartPropagationEndpoint(string anchorStepId, bool fromEndpoint, string stepId)
        {
            if (_selectedPartIdx < 0 || _parts == null || _selectedPartIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[_selectedPartIdx];

            BeginPartEdit(_selectedPartIdx);

            StepPoseEntry target;
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                target = p.stepPoses[_editingPoseMode];
            }
            else
            {
                Vector3 capPos; Quaternion capRot; Vector3 capScl;
                if (_editingPoseMode == PoseModeAssembled)
                {
                    capPos = p.assembledPosition; capRot = p.assembledRotation; capScl = p.assembledScale;
                }
                else
                {
                    capPos = p.startPosition; capRot = p.startRotation; capScl = p.startScale;
                }
                if (p.stepPoses == null) p.stepPoses = new List<StepPoseEntry>();
                target = new StepPoseEntry
                {
                    stepId   = anchorStepId,
                    position = PackageJsonUtils.ToFloat3(capPos),
                    rotation = PackageJsonUtils.ToQuaternion(capRot),
                    scale    = PackageJsonUtils.ToFloat3(capScl),
                };
                p.stepPoses.Add(target);
                _editingPoseMode = p.stepPoses.Count - 1;
            }

            if (fromEndpoint) target.propagateFromStep    = stepId;
            else              target.propagateThroughStep = stepId;

            p.isDirty = true;
            EndPartEdit();
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// One-click "Apply to range" menu that works for any pose mode. When
        /// Start/Assembled is active, this materializes a new stepPose entry
        /// at the current step using those field values as the captured pose,
        /// then writes the chosen span. When a Custom entry is active, it
        /// just rewrites the active entry's span.
        /// </summary>
        private void ShowPartPosePropagationMenu(int partIdx, string anchorStepId)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("From start → this step"),   false, () => PropagatePartPose(partIdx, anchorStepId, SpanPreset.StartToAnchor, null));
            menu.AddItem(new GUIContent("This step → end"),          false, () => PropagatePartPose(partIdx, anchorStepId, SpanPreset.AnchorToEnd, null));
            menu.AddItem(new GUIContent("All steps"),                false, () => PropagatePartPose(partIdx, anchorStepId, SpanPreset.AllSteps, null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Through step… (search)"),  false, () =>
            {
                // Deferred to the next editor frame so the GenericMenu has
                // time to dismiss before the searchable popup appears.
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, sid => PropagatePartPose(partIdx, anchorStepId, SpanPreset.FixedThrough, sid));
            });
            menu.ShowAsContext();
        }

        /// <summary>
        /// Captures the currently-active pose (Start / Assembled / selected
        /// Custom) and either edits the active Custom entry's span OR creates
        /// a new stepPose at <paramref name="anchorStepId"/> with that pose.
        /// </summary>
        private void PropagatePartPose(int partIdx, string anchorStepId, SpanPreset preset, string throughStepIdOpt)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[partIdx];

            BeginPartEdit(partIdx);

            StepPoseEntry target;
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                target = p.stepPoses[_editingPoseMode];
            }
            else
            {
                // Materialize Start/Assembled as a new stepPose entry.
                Vector3 capPos; Quaternion capRot; Vector3 capScl;
                if (_editingPoseMode == PoseModeAssembled)
                {
                    capPos = p.assembledPosition;
                    capRot = p.assembledRotation;
                    capScl = p.assembledScale;
                }
                else // PoseModeStart (or unset)
                {
                    capPos = p.startPosition;
                    capRot = p.startRotation;
                    capScl = p.startScale;
                }
                if (p.stepPoses == null) p.stepPoses = new List<StepPoseEntry>();
                target = new StepPoseEntry
                {
                    stepId   = anchorStepId,
                    position = PackageJsonUtils.ToFloat3(capPos),
                    rotation = PackageJsonUtils.ToQuaternion(capRot),
                    scale    = PackageJsonUtils.ToFloat3(capScl),
                };
                p.stepPoses.Add(target);
                _editingPoseMode = p.stepPoses.Count - 1;
            }

            ApplyPreset(target, preset, throughStepIdOpt);
            p.isDirty = true;
            EndPartEdit();
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>Preset menu for the span dropdown — one-click authoring.</summary>
        private void ShowStepPoseSpanMenuForPart(int partIdx, int poseIdx)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("From start → this step"),   false, () => SetPartSpan(partIdx, poseIdx, SpanPreset.StartToAnchor, null));
            menu.AddItem(new GUIContent("This step → end"),          false, () => SetPartSpan(partIdx, poseIdx, SpanPreset.AnchorToEnd, null));
            menu.AddItem(new GUIContent("All steps"),                false, () => SetPartSpan(partIdx, poseIdx, SpanPreset.AllSteps, null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Through step… (search)"),  false, () =>
            {
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, sid => SetPartSpan(partIdx, poseIdx, SpanPreset.FixedThrough, sid));
            });
            menu.ShowAsContext();
        }

        private enum SpanPreset { JustThisStep, StartToAnchor, AnchorToEnd, AllSteps, FixedThrough }

        private void SetPartSpan(int partIdx, int poseIdx, SpanPreset preset, string throughStepIdOpt)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[partIdx];
            if (p.stepPoses == null || poseIdx < 0 || poseIdx >= p.stepPoses.Count) return;
            BeginPartEdit(partIdx);
            var e = p.stepPoses[poseIdx];
            ApplyPreset(e, preset, throughStepIdOpt);
            p.isDirty = true;
            EndPartEdit();
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        private void ApplyPreset(StepPoseEntry e, SpanPreset preset, string throughStepIdOpt)
        {
            // Use EXPLICIT step IDs for the boundary presets so they don't
            // collide with legacy "empty = anchor forward" semantics the
            // runtime must preserve for pre-existing content.
            string firstStepId = FirstStepId();
            string lastStepId  = LastStepId();
            switch (preset)
            {
                case SpanPreset.JustThisStep:
                    e.propagateFromStep    = e.stepId;
                    e.propagateThroughStep = e.stepId;
                    break;
                case SpanPreset.StartToAnchor:
                    e.propagateFromStep    = firstStepId ?? "";
                    e.propagateThroughStep = e.stepId;
                    break;
                case SpanPreset.AnchorToEnd:
                    e.propagateFromStep    = e.stepId;
                    e.propagateThroughStep = lastStepId ?? "";
                    break;
                case SpanPreset.AllSteps:
                    e.propagateFromStep    = firstStepId ?? "";
                    e.propagateThroughStep = lastStepId ?? "";
                    break;
                case SpanPreset.FixedThrough:
                    e.propagateFromStep    = e.stepId;
                    e.propagateThroughStep = throughStepIdOpt ?? "";
                    break;
            }
        }

        private string FirstStepId()
        {
            if (_pkg?.steps == null || _pkg.steps.Length == 0) return null;
            StepDefinition earliest = null;
            foreach (var s in _pkg.steps)
                if (s != null && (earliest == null || s.sequenceIndex < earliest.sequenceIndex))
                    earliest = s;
            return earliest?.id;
        }

        private string LastStepId()
        {
            if (_pkg?.steps == null || _pkg.steps.Length == 0) return null;
            StepDefinition latest = null;
            foreach (var s in _pkg.steps)
                if (s != null && (latest == null || s.sequenceIndex > latest.sequenceIndex))
                    latest = s;
            return latest?.id;
        }

        /// <summary>
        /// Single-pose authoring block for parts marked NO TASK at the current
        /// step. Reads/writes the stepPose entry keyed by (partId, stepId) —
        /// one captured when the author toggled to N. Exposes position,
        /// rotation (Euler), and scale fields directly, plus the From/Through
        /// propagation pickers so the same pose can span other steps.
        /// </summary>
        private void DrawNoTaskPoseInline(StepDefinition curStep)
        {
            if (_selectedPartIdx < 0 || _parts == null || _selectedPartIdx >= _parts.Length) return;
            ref PartEditState p = ref _parts[_selectedPartIdx];
            string partId = p.def?.id;
            if (string.IsNullOrEmpty(partId)) return;

            // Find an existing backing stepPose entry without lazy-creating.
            // Creating on every redraw would dirty the part after Revert.
            // The R→N toggle's CaptureCurrentPoseAsStepPose is the only path
            // that should materialise the entry.
            int entryIdx = -1;
            if (p.stepPoses != null)
            {
                for (int i = 0; i < p.stepPoses.Count; i++)
                {
                    var e = p.stepPoses[i];
                    if (e != null && string.Equals(e.stepId, curStep.id, StringComparison.Ordinal))
                    { entryIdx = i; break; }
                }
            }

            if (entryIdx >= 0)
            {
                _editingPoseMode = entryIdx;
                DrawPartPropagationRow(curStep.id, p.stepPoses);
            }
            else
            {
                // No entry yet — render a hint instead of quietly mutating
                // state. This happens after Revert if the N toggle's changes
                // were never saved.
                var hint = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = FontStyle.Italic,
                    normal    = { textColor = new Color(0.55f, 0.78f, 0.95f) },
                };
                EditorGUILayout.LabelField("  Cycle R → O → N on this row to capture the current pose.", hint);
            }
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
            // Default label = "Custom" so legitimate author-created entries are
            // distinguishable from legacy auto-promote artifacts (which have
            // empty label). The load-time cleanup strips empty-label entries
            // silently; labelled entries are preserved.
            p.stepPoses.Add(new StepPoseEntry
            {
                stepId            = stepId,
                label             = "Custom",
                position          = PackageJsonUtils.ToFloat3(initPos),
                rotation          = PackageJsonUtils.ToQuaternion(initRot),
                scale             = PackageJsonUtils.ToFloat3(initScl),
                propagateFromStep = stepId, // default span: this step → end
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
            MirrorStepPosesToPreviewConfig(p);
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

            // EditorPrefs-backed toggles for the bounding box + grid ticks
            // overlays. Default ON so authors discover the feature; persisted
            // per-user so the choice survives domain reloads and restarts.
            const string PrefShowBounds    = "OSE.PartPreview.ShowBounds";
            const string PrefShowGridTicks = "OSE.PartPreview.ShowGridTicks";
            bool showBounds    = EditorPrefs.GetBool(PrefShowBounds,    true);
            bool showGridTicks = EditorPrefs.GetBool(PrefShowGridTicks, true);

            var drawOpts = new PartModelPreviewRenderer.DrawOptions
            {
                useMm         = useMm,
                showBounds    = showBounds,
                showGridTicks = showGridTicks,
            };
            bool needsRepaint = _partPreview.Draw(previewRect, drawOpts);
            if (needsRepaint) Repaint();

            // Floating toolbar overlay (top-right of the preview rect) —
            // compact toggle buttons for the new annotations. Drawn AFTER
            // Draw() so the buttons sit above the rendered texture.
            var toolbarStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(4, 4, 2, 2),
            };
            float btnW = 62f, btnH = 18f, pad = 4f;
            var boundsRect = new Rect(previewRect.xMax - btnW - pad, previewRect.y + pad, btnW, btnH);
            var ticksRect  = new Rect(previewRect.xMax - btnW - pad, boundsRect.yMax + 2f, btnW, btnH);

            bool newShowBounds = GUI.Toggle(boundsRect,
                showBounds,
                new GUIContent(showBounds ? "⧉ Bounds" : "⧉ Bounds",
                    "Show/hide the wireframe bounding box and L×W×H edge labels."),
                toolbarStyle);
            if (newShowBounds != showBounds)
            {
                EditorPrefs.SetBool(PrefShowBounds, newShowBounds);
                Repaint();
            }
            bool newShowTicks = GUI.Toggle(ticksRect,
                showGridTicks,
                new GUIContent(showGridTicks ? "⌗ Ticks" : "⌗ Ticks",
                    "Show/hide distance labels on the major grid lines."),
                toolbarStyle);
            if (newShowTicks != showGridTicks)
            {
                EditorPrefs.SetBool(PrefShowGridTicks, newShowTicks);
                Repaint();
            }

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

            // Always-on waypoint audit — renders directly under the preview
            // whenever the part has any stepPose entries, regardless of
            // role (Required / Optional / No Task). This is the single
            // prominent diagnostic for "why does the transform differ on
            // step N?" questions.
            DrawAllStepPosesAuditStrip(ref p);

            // NO TASK auto-selection: when the current step classifies this
            // part as NO TASK (id lives in visualPartIds) and the selected
            // pose mode is the generic Start/Assembled default, pivot the
            // inspector to the step-scoped NO TASK pose entry so the author
            // sees the transform fields directly without toggling anything.
            string __curStepIdForNoTask = GetCurrentStepId();
            if (!string.IsNullOrEmpty(__curStepIdForNoTask)
                && p.def != null
                && p.stepPoses != null)
            {
                var __curStep = FindStep(__curStepIdForNoTask);
                bool __isNoTask = __curStep != null
                                   && __curStep.visualPartIds != null
                                   && Array.IndexOf(__curStep.visualPartIds, p.def.id) >= 0;
                if (__isNoTask)
                {
                    int __foundIdx = -1;
                    for (int __i = 0; __i < p.stepPoses.Count; __i++)
                    {
                        var __e = p.stepPoses[__i];
                        if (__e != null && string.Equals(__e.stepId, __curStepIdForNoTask, StringComparison.Ordinal))
                        { __foundIdx = __i; break; }
                    }
                    // Do NOT lazy-create a stepPose entry here — that would
                    // mark the part dirty on every inspector redraw, which
                    // breaks the Revert button (the "Write to machine.json"
                    // button lights green again the instant the inspector
                    // redraws after revert). The R→N toggle in the task
                    // sequence already materialises the entry via
                    // CaptureCurrentPoseAsStepPose. If nothing exists yet,
                    // fall through to the Start/Assembled fields until the
                    // author explicitly creates one.
                    if (__foundIdx >= 0)
                    {
                        if (_editingPoseMode != __foundIdx) _editingPoseMode = __foundIdx;

                        var __h = new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = 11,
                            normal   = { textColor = new Color(0.70f, 0.88f, 1f) },
                        };
                        EditorGUILayout.LabelField("NO TASK pose", __h);
                    }
                }
            }

            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                // ── Custom pose fields ───────────────────────────────────────
                var sp = p.stepPoses[_editingPoseMode];

                // Label (display name)
                EditorGUI.BeginChangeCheck();
                string newLabel = EditorGUILayout.TextField("Name", sp.label ?? "");
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.label = newLabel; p.isDirty = true; MirrorStepPosesToPreviewConfig(p); EndPartEdit(); Repaint(); }

                // Step ID — which step activates this pose
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                string newStepId = EditorGUILayout.TextField("Step ID", sp.stepId ?? "");
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.stepId = newStepId; p.isDirty = true; MirrorStepPosesToPreviewConfig(p); EndPartEdit(); }
                if (GUILayout.Button("Pick", EditorStyles.miniButton, GUILayout.Width(40)))
                    ShowStepIdPickerMenu(_selectedPartIdx, _editingPoseMode);
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                Vector3 spPos = Vector3FieldClip("Position", PackageJsonUtils.ToVector3(sp.position));
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.position = PackageJsonUtils.ToFloat3(spPos); p.isDirty = true; MirrorStepPosesToPreviewConfig(p); EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 spEuler = Vector3FieldClip("Rotation", PackageJsonUtils.ToUnityQuaternion(sp.rotation).eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.rotation = PackageJsonUtils.ToQuaternion(Quaternion.Euler(spEuler)); p.isDirty = true; MirrorStepPosesToPreviewConfig(p); EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 spScl = Vector3FieldClip("Scale", PackageJsonUtils.ToVector3(sp.scale));
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); sp.scale = PackageJsonUtils.ToFloat3(spScl); p.isDirty = true; MirrorStepPosesToPreviewConfig(p); EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

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
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledPosition = newPlayPos; if (p.placement != null) p.placement.assembledPosition = PackageJsonUtils.ToFloat3(newPlayPos); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 playEuler = Vector3FieldClip("Play Rotation", p.assembledRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledRotation = Quaternion.Euler(playEuler); if (p.placement != null) p.placement.assembledRotation = PackageJsonUtils.ToQuaternion(p.assembledRotation); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newPlayScale = Vector3FieldClip("Play Scale", p.assembledScale);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.assembledScale = newPlayScale; if (p.placement != null) p.placement.assembledScale = PackageJsonUtils.ToFloat3(newPlayScale); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newStartPos = Vector3FieldClip("Start Position", p.startPosition);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startPosition = newStartPos; if (p.placement != null) p.placement.startPosition = PackageJsonUtils.ToFloat3(newStartPos); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 startEuler = Vector3FieldClip("Start Rotation", p.startRotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startRotation = Quaternion.Euler(startEuler); if (p.placement != null) p.placement.startRotation = PackageJsonUtils.ToQuaternion(p.startRotation); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }

                EditorGUI.BeginChangeCheck();
                Vector3 newStartScale = Vector3FieldClip("Start Scale", p.startScale);
                if (EditorGUI.EndChangeCheck()) { BeginPartEdit(_selectedPartIdx); p.startScale = newStartScale; if (p.placement != null) p.placement.startScale = PackageJsonUtils.ToFloat3(newStartScale); p.isDirty = true; EndPartEdit(); SyncPartMeshToActivePose(ref p); SceneView.RepaintAll(); }
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

        /// <summary>
        /// Finds the subassembly that owns this part (first match in
        /// <c>sub.partIds</c>). Prefers non-aggregate groups so stripping
        /// affects the actual integrated-placement subassembly rather than a
        /// phase scope. Returns null if the part isn't in any group.
        /// </summary>
        private string ResolveOwningSubassemblyId(PartEditState p)
        {
            if (p.def == null || _pkg == null) return null;
            var subs = _pkg.GetSubassemblies();
            if (subs == null) return null;
            string aggregateMatch = null;
            foreach (var s in subs)
            {
                if (s?.partIds == null) continue;
                foreach (var pid in s.partIds)
                {
                    if (!string.Equals(pid, p.def.id, StringComparison.Ordinal)) continue;
                    if (!s.isAggregate) return s.id;
                    if (aggregateMatch == null) aggregateMatch = s.id;
                }
            }
            return aggregateMatch;
        }

        private int CountIntegratedPlacementsForSubassembly(string subId)
        {
            if (string.IsNullOrEmpty(subId)) return 0;
            var placements = _pkg?.previewConfig?.integratedSubassemblyPlacements;
            if (placements == null) return 0;
            int n = 0;
            foreach (var pl in placements)
                if (pl != null && string.Equals(pl.subassemblyId, subId, StringComparison.Ordinal))
                    n++;
            return n;
        }

        /// <summary>
        /// Removes every <c>integratedSubassemblyPlacements</c> entry whose
        /// <c>subassemblyId</c> matches <paramref name="subId"/>. Dirties the
        /// package so Write-to-JSON persists the removal. The runtime spawner
        /// will then skip the integrated lookup and fall through to
        /// startPosition / assembledPosition for the group's members.
        /// </summary>
        private void StripIntegratedPlacementsForSubassembly(string subId)
        {
            if (string.IsNullOrEmpty(subId)) return;
            var pc = _pkg?.previewConfig;
            if (pc?.integratedSubassemblyPlacements == null) return;
            var kept = new List<IntegratedSubassemblyPreviewPlacement>();
            int removed = 0;
            foreach (var pl in pc.integratedSubassemblyPlacements)
            {
                if (pl != null && string.Equals(pl.subassemblyId, subId, StringComparison.Ordinal))
                { removed++; continue; }
                kept.Add(pl);
            }
            if (removed == 0) return;

            pc.integratedSubassemblyPlacements = kept.ToArray();
            _dirtySubassemblyIds.Add(subId);
            // Also mark any step that referenced this group for re-serialisation.
            if (_pkg?.steps != null)
            {
                foreach (var s in _pkg.steps)
                {
                    if (s == null) continue;
                    if (string.Equals(s.requiredSubassemblyId, subId, StringComparison.Ordinal)
                        || string.Equals(s.subassemblyId, subId, StringComparison.Ordinal))
                        _dirtyStepIds.Add(s.id);
                }
            }
            ShowNotification(new GUIContent($"Stripped {removed} integrated placement(s) for '{subId}'."));
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Removes every stepPose on the part except the one anchored to
        /// <paramref name="keepStepId"/>. Also mirrors the change into the
        /// backing <c>PartPreviewPlacement.stepPoses</c> so a subsequent
        /// <see cref="BuildPartList"/> doesn't resurrect them.
        /// </summary>
        private void RemoveStepPosesExcept(int partIdx, string keepStepId)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            if (_parts[partIdx].stepPoses == null || _parts[partIdx].stepPoses.Count == 0) return;
            BeginPartEdit(partIdx);
            var list = _parts[partIdx].stepPoses;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || !string.Equals(e.stepId, keepStepId, StringComparison.Ordinal))
                    list.RemoveAt(i);
            }
            _parts[partIdx].isDirty = true;
            MirrorStepPosesToPreviewConfig(_parts[partIdx]);
            EndPartEdit();
            _editingPoseMode = _parts[partIdx].stepPoses.Count > 0 ? 0 : PoseModeStart;
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Removes every stepPose on the part whose anchor step's
        /// sequenceIndex is strictly greater than <paramref name="afterSeq"/>.
        /// </summary>
        private void RemoveStepPosesAfter(int partIdx, int afterSeq)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            if (_parts[partIdx].stepPoses == null || _parts[partIdx].stepPoses.Count == 0) return;
            BeginPartEdit(partIdx);
            var list = _parts[partIdx].stepPoses;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null) { list.RemoveAt(i); continue; }
                int s = SeqIndexForStepId(e.stepId);
                if (s > afterSeq) list.RemoveAt(i);
            }
            _parts[partIdx].isDirty = true;
            MirrorStepPosesToPreviewConfig(_parts[partIdx]);
            EndPartEdit();
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Copies the in-memory <c>stepPoses</c> list to the underlying
        /// <c>PartPreviewPlacement.stepPoses</c> array so future
        /// <see cref="BuildPartList"/> rebuilds pick it up and the save
        /// path serialises the pruned list.
        /// </summary>
        private void MirrorStepPosesToPreviewConfig(PartEditState p)
        {
            if (p.def == null) return;
            var ppRef = FindPartPlacement(p.def.id);
            if (ppRef == null) return;
            ppRef.stepPoses = p.stepPoses != null && p.stepPoses.Count > 0
                ? p.stepPoses.ToArray()
                : null;
        }

        private void DrawAllStepPosesAuditStrip(ref PartEditState p)
        {
            // ALWAYS render the header so the author can tell the panel is
            // reachable even when no waypoints exist yet. Early-returning on
            // empty hid the affordance so the author couldn't tell whether
            // the absence meant "zero waypoints" or "the panel isn't drawing."
            // Count author-authored entries only — synthetic NO-TASK
            // waypoints aren't surfaced in the audit list.
            int count = 0;
            if (p.stepPoses != null)
            {
                foreach (var spe in p.stepPoses)
                {
                    if (spe == null) continue;
                    if (!string.IsNullOrEmpty(spe.label)
                        && spe.label.StartsWith(OSE.Content.Loading.MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                        continue;
                    count++;
                }
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            var hdrStyleTop = new GUIStyle(EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Waypoints on this part ({count})", hdrStyleTop);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (count == 0)
            {
                var emptyStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
                EditorGUILayout.LabelField("  (none — transforms come from startPosition / assembledPosition)", emptyStyle);
                return;
            }

            // Prune buttons removed alongside the propagation UI. Per-row
            // × buttons below are sufficient for one-off cleanup.

            // Strip integrated placements for this part's owning subassembly.
            // Lets the author remove the per-target integrated poses that
            // otherwise override NO TASK's startPosition on later steps.
            string ownerSubId = ResolveOwningSubassemblyId(p);
            int integratedCount = CountIntegratedPlacementsForSubassembly(ownerSubId);
            if (integratedCount > 0)
            {
                EditorGUILayout.BeginHorizontal();
                var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.95f, 0.65f, 0.30f) },
                    fontStyle = FontStyle.Italic,
                };
                GUILayout.Label($"  {integratedCount} integrated placement(s) on group '{ownerSubId}' can override later steps", warnStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Strip integrated",
                        "Removes every integratedSubassemblyPlacements entry for this part's owning subassembly. The spawner will then fall back to startPosition / assembledPosition, letting NO TASK pose dominate."),
                    EditorStyles.miniButton, GUILayout.Width(112)))
                {
                    StripIntegratedPlacementsForSubassembly(ownerSubId);
                    return;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Sort indices by the anchor step's sequenceIndex so the list
            // reads front-to-back in the package. Bad entries (unknown
            // stepId) sort last. Capture the list into a local so the sort
            // lambda doesn't close over the ref parameter (C# disallows).
            var posesLocal = p.stepPoses;
            var order = new List<int>();
            for (int i = 0; i < posesLocal.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int sa = SeqIndexForStepId(posesLocal[a]?.stepId ?? "");
                int sb = SeqIndexForStepId(posesLocal[b]?.stepId ?? "");
                if (sa < 0) sa = int.MaxValue;
                if (sb < 0) sb = int.MaxValue;
                return sa.CompareTo(sb);
            });

            string currentStepId = GetCurrentStepId();
            foreach (int idx in order)
            {
                var e = p.stepPoses[idx];
                if (e == null) continue;
                // Hide synthetic NO-TASK waypoints from the audit list —
                // they're recomputed on every load and never persisted, so
                // there's nothing the author needs to see or prune.
                if (!string.IsNullOrEmpty(e.label)
                    && e.label.StartsWith(OSE.Content.Loading.MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                    continue;

                bool isCurrent = !string.IsNullOrEmpty(currentStepId)
                                  && string.Equals(e.stepId, currentStepId, StringComparison.Ordinal);
                int anchorSeq = SeqIndexForStepId(e.stepId);
                string anchorLabel = anchorSeq >= 0
                    ? $"[{anchorSeq}] {StepShortLabel(e.stepId)}"
                    : $"(unknown: {e.stepId ?? "∅"})";
                string span = ResolveSpanChip(e);

                EditorGUILayout.BeginHorizontal();
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal,
                    normal    = { textColor = isCurrent ? new Color(0.70f, 0.88f, 1f) : new Color(0.78f, 0.78f, 0.80f) },
                };
                GUILayout.Label(anchorLabel, labelStyle, GUILayout.MinWidth(140));
                var spanStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.78f, 0.95f) },
                    alignment = TextAnchor.MiddleLeft,
                };
                GUILayout.Label(string.IsNullOrEmpty(span) ? "" : span, spanStyle, GUILayout.MinWidth(90));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Go", EditorStyles.miniButtonLeft, GUILayout.Width(28)) && anchorSeq >= 0)
                {
                    // Jump the step filter to this pose's anchor step so the
                    // author can inspect it in context.
                    if (_stepIds != null)
                        for (int k = 0; k < _stepIds.Length; k++)
                            if (string.Equals(_stepIds[k], e.stepId, StringComparison.Ordinal))
                            { ApplyStepFilter(k); break; }
                }
                if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                {
                    RemoveStepPose(_selectedPartIdx, idx);
                    break; // indices shifted; re-render next frame
                }
                EditorGUILayout.EndHorizontal();
            }
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
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
                    p2.isDirty = true;
                    MirrorPartStateToPlacement(ref p2); // keep PoseResolver in sync with the cached edit
                    SyncPartMeshToActivePose(ref p2);
                }
                SceneView.RepaintAll(); Repaint();
            }
        }
    }
}
