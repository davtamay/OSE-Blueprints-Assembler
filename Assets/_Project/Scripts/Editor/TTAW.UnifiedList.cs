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

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.UnifiedList.cs  —  DrawUnifiedList, task-sequence drag list, add-task
//                         pickers, commit helpers, working-orientation UI,
//                         and inline row-drawing helpers.
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
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

            // ── WORKING ORIENTATION (subassembly steps only) ──────────────────
            if (!string.IsNullOrWhiteSpace(step.subassemblyId) || !string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                DrawWorkingOrientationUI(step);

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

            // ── Section for selected task kind (rich detail body) ─────────────
            //
            // PHASE 4: when the inspector pane is visible the canvas shows a
            // hint pointing right; when hidden the canvas renders the rich
            // body inline so authors who collapsed the inspector still get
            // the full detail surface. Both paths share DrawTaskInspectorBody.
            if (_inspectorVisible)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("  Selection details are in the Inspector pane →",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                DrawTaskInspectorBody(step, order);
            }

            // ── SUBASSEMBLY (Phase 6 MVP) ─────────────────────────────────────
            EditorGUILayout.Space(8);
            DrawSubassemblySection(step);

            // ── ANIMATION CUES — step-level, always shown below task panels ───
            EditorGUILayout.Space(8);
            DrawAnimationCuesSection(step);

            // ── PARTICLE EFFECTS ─────────────────────────────────────────────
            EditorGUILayout.Space(8);
            DrawParticleEffectsSection(step);

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
        /// Writes only the currently-dirty state of the given task entry to machine.json.
        /// Leaves other dirty items untouched. Skips the post-write reload/respawn so the
        /// user's editing flow (selection, gizmos, scroll position) is preserved.
        /// </summary>
        private void SaveTaskEntry(TaskOrderEntry entry, StepDefinition step)
        {
            if (entry == null) return;

            // Snapshot dirty flags of items we want to KEEP dirty (everything except this task).
            // WriteJson iterates all dirty items, so we temporarily un-dirty everything else,
            // call WriteJson(reloadAfter:false), then restore the dirty flags on the items we
            // didn't mean to save. This is the cheapest way to get "save just this task" without
            // duplicating WriteJson's field-injection logic.
            var keepTargetDirty = new List<int>();
            var keepPartDirty   = new List<int>();
            var keepStepDirty   = new List<string>();
            var keepToolDirty   = new List<string>();

            string thisTargetId = null;
            string thisPartId   = null;

            if (entry.kind == "part") thisPartId = entry.id;
            else if (entry.kind == "toolAction" && step?.requiredToolActions != null)
            {
                foreach (var a in step.requiredToolActions)
                    if (a?.id == entry.id) { thisTargetId = a.targetId; break; }
            }
            else thisTargetId = entry.id; // target, wire, confirm

            if (_targets != null)
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (!_targets[i].isDirty) continue;
                    if (_targets[i].def?.id != thisTargetId)
                    {
                        keepTargetDirty.Add(i);
                        _targets[i].isDirty = false;
                    }
                }

            if (_parts != null)
                for (int i = 0; i < _parts.Length; i++)
                {
                    if (!_parts[i].isDirty) continue;
                    if (_parts[i].def?.id != thisPartId)
                    {
                        keepPartDirty.Add(i);
                        _parts[i].isDirty = false;
                    }
                }

            // Wire/tool task edits also dirty the step. Only persist the step block when the
            // current task is a wire (which owns wireConnect changes); otherwise hold it back.
            if (_dirtyStepIds.Count > 0)
            {
                bool isWireOrToolAction = entry.kind == "wire" || entry.kind == "toolAction";
                if (!isWireOrToolAction)
                {
                    foreach (var id in _dirtyStepIds) keepStepDirty.Add(id);
                    _dirtyStepIds.Clear();
                }
            }

            // Tool persistence edits are global; keep them held back unless the task is a toolAction.
            if (_dirtyToolIds.Count > 0 && entry.kind != "toolAction")
            {
                foreach (var id in _dirtyToolIds) keepToolDirty.Add(id);
                _dirtyToolIds.Clear();
            }

            WriteJson(reloadAfter: false);

            // Restore dirty flags on items we deliberately held back so the user can still save them later.
            foreach (int i in keepTargetDirty) if (i < _targets.Length) _targets[i].isDirty = true;
            foreach (int i in keepPartDirty)   if (i < _parts.Length)   _parts[i].isDirty   = true;
            foreach (var id in keepStepDirty)  _dirtyStepIds.Add(id);
            foreach (var id in keepToolDirty)  _dirtyToolIds.Add(id);
        }

        private void RevertPartEntry(string partId)
        {
            var fresh = PackageJsonUtils.LoadPackage(_pkgId);
            if (fresh == null || _parts == null) return;

            PartPreviewPlacement pp = null;
            if (fresh.previewConfig?.partPlacements != null)
                foreach (var p in fresh.previewConfig.partPlacements)
                    if (p?.partId == partId) { pp = p; break; }

            PartDefinition freshDef = null;
            if (fresh.parts != null)
                foreach (var pd in fresh.parts)
                    if (pd?.id == partId) { freshDef = pd; break; }

            bool hasP = pp != null;
            StagingPose sp = freshDef?.stagingPose;

            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].def?.id != partId) continue;
                _parts[i].placement     = pp;
                _parts[i].hasPlacement  = hasP;
                _parts[i].startPosition = sp != null ? PackageJsonUtils.ToVector3(sp.position)
                                        : hasP ? PackageJsonUtils.ToVector3(pp.startPosition) : Vector3.zero;
                _parts[i].startRotation = sp != null ? PackageJsonUtils.ToUnityQuaternion(sp.rotation)
                                        : hasP ? PackageJsonUtils.ToUnityQuaternion(pp.startRotation) : Quaternion.identity;
                _parts[i].startScale    = sp != null && (sp.scale.x != 0f || sp.scale.y != 0f || sp.scale.z != 0f)
                                        ? PackageJsonUtils.ToVector3(sp.scale)
                                        : hasP ? PackageJsonUtils.ToVector3(pp.startScale) : Vector3.one;
                _parts[i].assembledPosition  = hasP ? PackageJsonUtils.ToVector3(pp.assembledPosition)  : Vector3.zero;
                _parts[i].assembledRotation  = hasP ? PackageJsonUtils.ToUnityQuaternion(pp.assembledRotation) : Quaternion.identity;
                _parts[i].assembledScale     = hasP ? PackageJsonUtils.ToVector3(pp.assembledScale)     : Vector3.one;
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
            _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5; // suppress false dirty from handle re-init after selection change
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
                        Vector3 newA2 = Vector3FieldClip("Port A (local)", new Vector3(wire.portA.x, wire.portA.y, wire.portA.z));
                        Vector3 newB2 = Vector3FieldClip("Port B (local)", new Vector3(wire.portB.x, wire.portB.y, wire.portB.z));
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
                    float nw2 = FloatFieldClip("Radius (m)", wire.radius > 0 ? wire.radius : 0.003f);
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
            _addPickerWireRadius = Mathf.Max(0f, FloatFieldClip("Radius (m)", _addPickerWireRadius > 0 ? _addPickerWireRadius : 0.003f));

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

        // ── Working Orientation UI ────────────────────────────────────────────

        private void DrawWorkingOrientationUI(StepDefinition step)
        {
            _showWorkingOrientation = EditorGUILayout.Foldout(_showWorkingOrientation, "Working Orientation", true);
            if (!_showWorkingOrientation)
                return;

            EditorGUI.indentLevel++;

            bool hasWO = step.workingOrientation != null;
            Vector3 rot = hasWO
                ? new Vector3(step.workingOrientation.subassemblyRotation.x,
                              step.workingOrientation.subassemblyRotation.y,
                              step.workingOrientation.subassemblyRotation.z)
                : Vector3.zero;
            Vector3 posOffset = hasWO
                ? new Vector3(step.workingOrientation.subassemblyPositionOffset.x,
                              step.workingOrientation.subassemblyPositionOffset.y,
                              step.workingOrientation.subassemblyPositionOffset.z)
                : Vector3.zero;
            string hint = hasWO ? (step.workingOrientation.hint ?? "") : "";

            // Preset buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Flip 180 X", EditorStyles.miniButton))
            { rot = new Vector3(180, 0, 0); posOffset = Vector3.zero; GUI.changed = true; }
            if (GUILayout.Button("Flip 180 Z", EditorStyles.miniButton))
            { rot = new Vector3(0, 0, 180); posOffset = Vector3.zero; GUI.changed = true; }
            if (GUILayout.Button("Tilt 90 X", EditorStyles.miniButton))
            { rot = new Vector3(90, 0, 0); posOffset = Vector3.zero; GUI.changed = true; }
            if (GUILayout.Button("Clear", EditorStyles.miniButton))
            {
                if (hasWO)
                {
                    step.workingOrientation = null;
                    _dirtyStepIds.Add(step.id);
                    RespawnScene();
                    SceneView.RepaintAll();
                }
                EditorGUI.indentLevel--;
                return;
            }
            EditorGUILayout.EndHorizontal();

            // Editable fields
            EditorGUI.BeginChangeCheck();
            rot = Vector3FieldClip("Rotation (\u00b0)", rot);
            posOffset = Vector3FieldClip("Position Offset", posOffset);
            hint = EditorGUILayout.TextField("Hint Text", hint);
            bool changed = EditorGUI.EndChangeCheck() || GUI.changed;

            if (changed)
            {
                if (step.workingOrientation == null)
                    step.workingOrientation = new StepWorkingOrientationPayload();

                step.workingOrientation.subassemblyRotation = new SceneFloat3 { x = rot.x, y = rot.y, z = rot.z };
                step.workingOrientation.subassemblyPositionOffset = new SceneFloat3 { x = posOffset.x, y = posOffset.y, z = posOffset.z };
                step.workingOrientation.hint = string.IsNullOrWhiteSpace(hint) ? null : hint;
                _dirtyStepIds.Add(step.id);
                SyncAllPartMeshesToActivePose();
                SceneView.RepaintAll();
            }

            if (hasWO)
            {
                EditorGUILayout.LabelField("Active", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
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

        // ── Inspector body (single source of truth, called from canvas + inspector) ──
        //
        // PHASE 4: this method renders the rich detail UI for the currently
        // selected task or selection state. It is called from two sites:
        //   • DrawUnifiedList (when the inspector is hidden via the toolbar)
        //   • DrawBottomEditPanel → which is hosted in the inspector pane
        // Both sites pass the same step + task order so behaviour is identical.

        private void DrawTaskInspectorBody(StepDefinition step, List<TaskOrderEntry> order)
        {
            if (step == null || order == null) return;

            if (_selectedTaskSeqIdx < 0 || _selectedTaskSeqIdx >= order.Count)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("  Click a task to view its details.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var selEntry = order[_selectedTaskSeqIdx];
            EditorGUILayout.Space(4);

            // Multi-selection → batch panel (parts first, then targets, then
            // fall through to the primary entry's single-item detail panel)
            if (_multiSelectedTaskSeqIdxs.Count > 1 && _multiSelectedParts.Count > 1)
            {
                DrawUnifiedSectionHeader($"BATCH — {_multiSelectedParts.Count} parts", 0);
                DrawPartPoseToggle();
                DrawPartBatchPanel();
                return;
            }
            if (_multiSelectedTaskSeqIdxs.Count > 1 && _multiSelected.Count > 1)
            {
                DrawUnifiedSectionHeader($"BATCH — {_multiSelected.Count} targets", 0);
                DrawBatchPanel();
                return;
            }

            switch (selEntry.kind)
            {
                case "part":
                {
                    DrawUnifiedSectionHeader($"PART CONTEXT ({selEntry.id})", 0);
                    if (IsTaskEntryDirty(selEntry, step))
                    {
                        EditorGUILayout.BeginHorizontal();
                        var ds = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColDirty }, fontStyle = FontStyle.Bold };
                        EditorGUILayout.LabelField("● Unsaved Changes", ds);
                        if (GUILayout.Button("Save", EditorStyles.miniButton, GUILayout.Width(42)))
                            SaveTaskEntry(selEntry, step);
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
                        if (GUILayout.Button("Save", EditorStyles.miniButton, GUILayout.Width(42)))
                            SaveTaskEntry(selEntry, step);
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
                                Vector3 newA = Vector3FieldClip("Port A (local)", new Vector3(wire.portA.x, wire.portA.y, wire.portA.z));
                                Vector3 newB = Vector3FieldClip("Port B (local)", new Vector3(wire.portB.x, wire.portB.y, wire.portB.z));
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
                            float nw = FloatFieldClip("Radius (m)", wire.radius > 0 ? wire.radius : 0.003f);
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
                        if (GUILayout.Button("Save", EditorStyles.miniButton, GUILayout.Width(42)))
                            SaveTaskEntry(selEntry, step);
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

        // ── Bottom edit panel + unified actions ───────────────────────────────

        private void DrawBottomEditPanel()
        {
            // ── Task-sequence-driven (authoritative when a task is selected) ──────
            // Both the canvas (when inspector is hidden) and the inspector pane
            // route through DrawTaskInspectorBody so the rich detail UI has a
            // single source of truth.
            if (_selectedTaskSeqIdx >= 0 && _stepFilterIdx > 0
                && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var step  = FindStep(_stepIds[_stepFilterIdx]);
                var order = step != null ? GetOrDeriveTaskOrder(step) : null;
                if (step != null && order != null)
                {
                    DrawTaskInspectorBody(step, order);
                    return;
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
    }
}
