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
                    menu.AddItem(new GUIContent("Part (Group)"), false, () => { _addTaskPicker = AddTaskPicker.Group; _addPickerGroupIdx = 0; _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.AddSeparator("");
                    menu.AddItem(new GUIContent("Confirm (button press)"), false, () => { CommitAddConfirmAction(step); _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.AddItem(new GUIContent("Observe (target position)"), false, () => { _addTaskPicker = AddTaskPicker.ToolTarget; _addPickerTargetIdx = 0; _addPickerToolIdx = 0; _selectedTaskSeqIdx = -1; _multiSelectedTaskSeqIdxs.Clear(); });
                    menu.ShowAsContext();
                });

            if (order.Count == 0)
            {
                EditorGUILayout.LabelField("  No tasks yet. Press + to add or drag a part below.",
                    EditorStyles.miniLabel);
                // Always render the drop zone — otherwise an empty sequence
                // becomes un-droppable and the only way to add is via the +
                // menu.
                DrawTaskSequenceDropZone(step, order);
            }
            else
            {
                DrawTaskSequenceDragList(step, order);
            }

            // ── Add-task picker (shown below sequence list) ────────────────────
            if (_addTaskPicker == AddTaskPicker.Part)       DrawAddPartPicker();
            if (_addTaskPicker == AddTaskPicker.ToolTarget) DrawAddToolTargetPicker();
            if (_addTaskPicker == AddTaskPicker.Wire)       DrawAddWirePicker();
            if (_addTaskPicker == AddTaskPicker.Group)      DrawAddGroupPicker(step);

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

            // ── GROUPS (Phase A4) ──────────────────────────────────────────────
            // Clickable list of groups (subassemblies) in this step. Clicking
            // one selects it — the inspector shows its properties and the
            // SceneView shows the rotation/position gizmo on its root GO.
            EditorGUILayout.Space(4);
            DrawCanvasSubassemblyList(step);

            // ── WHAT'S SHOWING — collapsed by default, just count pills ─────
            // Full bucket detail + add picker open on click. Most authors
            // only need the pills to confirm "yes, 12 parts are on screen."
            EditorGUILayout.Space(4);
            DrawVisibilitySection(step);

            // ── WHAT'S CHANGING — diagnostic delta vs the previous step ─────
            DrawWhatsChangingSection(step);

            // Groups, Part×Tool, Animation Cues, and Particle Effects moved
            // to the inspector pane (right side) as of the canvas redesign.
            // The inspector dispatches them by selection kind so they appear
            // only when relevant to what the author is currently editing.

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
                // Dedup accidentally-accumulated confirm_action entries from
                // older versions of the toggle-persist path. Keep the first
                // occurrence; drop the rest.
                order = new List<TaskOrderEntry>(step.taskOrder.Length);
                bool seenConfirmAction = false;
                for (int k = 0; k < step.taskOrder.Length; k++)
                {
                    var e = step.taskOrder[k];
                    if (e == null) continue;
                    if (e.kind == "confirm_action")
                    {
                        if (seenConfirmAction) continue;
                        seenConfirmAction = true;
                    }
                    order.Add(e);
                }
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
                if (step.optionalPartIds != null)
                {
                    foreach (var pid in step.optionalPartIds)
                        if (!string.IsNullOrEmpty(pid))
                            order.Add(new TaskOrderEntry { kind = "part", id = pid, isOptional = true });
                }
                // visualPartIds → no-task PART rows (rendered with the NO TASK
                // tag). Kept in the same sequence so the author sees context
                // inline with real tasks.
                if (step.visualPartIds != null)
                {
                    foreach (var pid in step.visualPartIds)
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

            // Auto-derive a group task entry from requiredSubassemblyId if not
            // already present in the order. This ensures the [G] row shows up
            // for steps that have a group placement but no explicit taskOrder.
            if (!string.IsNullOrEmpty(step.requiredSubassemblyId))
            {
                string subId = step.requiredSubassemblyId;
                bool found = false;
                foreach (var e in order)
                    if (e.kind == "part" && string.Equals(e.id, subId, StringComparison.Ordinal))
                    { found = true; break; }
                if (!found)
                    order.Insert(0, new TaskOrderEntry { kind = "part", id = subId });
            }

            // Confirm-family steps always end with a button press — append a
            // single terminal confirm_action. Guard against duplicates: the
            // row's toggle persists step.taskOrder on every click, which on
            // the next derivation pass would re-trigger this append and grow
            // the confirm tail every edit. Only append when no confirm_action
            // already exists in the order.
            if (isConfirm2)
            {
                bool hasConfirmAction = false;
                for (int k = 0; k < order.Count; k++)
                    if (order[k] != null && order[k].kind == "confirm_action") { hasConfirmAction = true; break; }
                if (!hasConfirmAction)
                    order.Add(new TaskOrderEntry { kind = "confirm_action", id = "confirm" });
            }

            // ── Orphan reconciliation ─────────────────────────────────────
            // If step.taskOrder was authored but a role list (required /
            // optional / visual) got a partId added without a matching
            // taskOrder row, the part becomes "invisibly Required" — it
            // drives the PoseTable and the runtime, but the authoring UI
            // never shows it. Append those missing partIds as orphan rows
            // so the author sees them and can reorder, remove, or reconcile.
            _cachedOrphanTaskIds.Clear();
            var presentPartIds = new HashSet<string>(StringComparer.Ordinal);
            // Members of any group [G] entry already in taskOrder are
            // suppressed — the [G] row IS the representation. Without this,
            // dragging a group spawned a [G] row PLUS one orphan row per
            // member (14 rows for a carriage); the author wants ONE row.
            var suppressedByGroup = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in order)
            {
                if (e == null) continue;
                if (e.kind == "part" && !string.IsNullOrEmpty(e.id)) presentPartIds.Add(e.id);
                if (e.kind == "part" && _pkg != null
                    && _pkg.TryGetSubassembly(e.id, out var subDef) && subDef?.partIds != null)
                {
                    foreach (var mpid in subDef.partIds)
                        if (!string.IsNullOrEmpty(mpid)) suppressedByGroup.Add(mpid);
                }
            }

            void AppendOrphans(string[] ids, bool optional)
            {
                if (ids == null) return;
                foreach (var pid in ids)
                {
                    if (string.IsNullOrEmpty(pid) || presentPartIds.Contains(pid)) continue;
                    if (suppressedByGroup.Contains(pid)) continue; // member shown via [G] row
                    order.Add(new TaskOrderEntry { kind = "part", id = pid, isOptional = optional });
                    presentPartIds.Add(pid);
                    _cachedOrphanTaskIds.Add(pid);
                }
            }
            AppendOrphans(step.requiredPartIds, optional: false);
            AppendOrphans(step.optionalPartIds, optional: true);
            AppendOrphans(step.visualPartIds,   optional: false);

            _cachedTaskOrderForStepId = step.id;
            _cachedTaskOrder = order;
            return order;
        }

        private void InvalidateTaskOrderCache()
        {
            _cachedTaskOrder = null;
            _cachedTaskOrderForStepId = null;
            _cachedOrphanTaskIds.Clear();
        }

        /// <summary>
        /// Forces <c>step.taskOrder</c> to stay a faithful projection of
        /// <c>requiredPartIds</c> / <c>optionalPartIds</c> / <c>visualPartIds</c>:
        /// <list type="bullet">
        ///   <item>Every partId present in a role list is in taskOrder exactly once.</item>
        ///   <item>No taskOrder part entry references a partId absent from every role list.</item>
        ///   <item>Each kept entry's <c>isOptional</c> flag matches its current role.</item>
        ///   <item>Non-part entries (targets, toolAction, wire, confirm_action) pass through.</item>
        /// </list>
        /// Existing author-chosen ordering is preserved for partIds that were
        /// already in taskOrder; newly-added partIds append at the end. Call
        /// this from every mutator that touches the role arrays so the "invisibly
        /// Required" bug (requiredPartIds updated, taskOrder forgotten) can't
        /// happen — it's also the guarantee the save/load paths rely on.
        /// </summary>
        private void ReconcileStepTaskOrder(StepDefinition step) => ReconcileStepTaskOrder(step, markDirty: true);

        /// <param name="markDirty">
        /// When true (the default), flags the step as dirty so Save picks it
        /// up — appropriate for every mutator path. Set false at load time:
        /// healing historical drift between taskOrder and the role arrays
        /// is a baseline correction, not an author change, and dirtying on
        /// load makes "Revert All" loop forever (Revert → Load → Reconcile →
        /// Dirty → Revert button stays active).
        /// </param>
        private void ReconcileStepTaskOrder(StepDefinition step, bool markDirty)
        {
            if (step == null) return;

            var required = new HashSet<string>(step.requiredPartIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            var optional = new HashSet<string>(step.optionalPartIds ?? System.Array.Empty<string>(), StringComparer.Ordinal);
            var visual   = new HashSet<string>(step.visualPartIds   ?? System.Array.Empty<string>(), StringComparer.Ordinal);

            var newOrder = new List<TaskOrderEntry>();
            var seenParts = new HashSet<string>(StringComparer.Ordinal);
            // Members of any group [G] entry already in taskOrder are
            // suppressed from the appended-orphans pass — the [G] row
            // represents them. Without this, AppendMissing would emit one
            // row per visualPartIds member and the author would see a [G]
            // row PLUS 14 individual member rows.
            var suppressedByGroup = new HashSet<string>(StringComparer.Ordinal);
            if (step.taskOrder != null && _pkg != null)
            {
                foreach (var e in step.taskOrder)
                {
                    if (e == null || e.kind != "part" || string.IsNullOrEmpty(e.id)) continue;
                    if (_pkg.TryGetSubassembly(e.id, out var subDef) && subDef?.partIds != null)
                        foreach (var mpid in subDef.partIds)
                            if (!string.IsNullOrEmpty(mpid)) suppressedByGroup.Add(mpid);
                }
            }
            bool changed = false;
            int originalCount = step.taskOrder?.Length ?? 0;

            if (step.taskOrder != null)
            {
                foreach (var e in step.taskOrder)
                {
                    if (e == null) continue;
                    if (e.kind != "part") { newOrder.Add(e); continue; }
                    if (string.IsNullOrEmpty(e.id)) { changed = true; continue; }
                    if (!seenParts.Add(e.id)) { changed = true; continue; } // drop duplicate

                    if (required.Contains(e.id))
                    {
                        if (e.isOptional) { e.isOptional = false; changed = true; }
                        newOrder.Add(e);
                    }
                    else if (optional.Contains(e.id))
                    {
                        if (!e.isOptional) { e.isOptional = true; changed = true; }
                        newOrder.Add(e);
                    }
                    else if (visual.Contains(e.id))
                    {
                        if (e.isOptional) { e.isOptional = false; changed = true; }
                        newOrder.Add(e);
                    }
                    else if (_pkg != null && _pkg.TryGetSubassembly(e.id, out _))
                    {
                        // Group [G] entries: id is a subassemblyId, not a
                        // partId, so it never lives in the role arrays. Keep
                        // the row — it's how NO TASK group drag-drops surface.
                        newOrder.Add(e);
                    }
                    else
                    {
                        // partId removed from every role list → drop the stale row.
                        changed = true;
                    }
                }
            }

            // Append parts that are in a role list but weren't in taskOrder.
            // Order matches the role lists so re-adds have a deterministic home;
            // the author can reorder afterwards.
            void AppendMissing(string[] ids, bool isOptional)
            {
                if (ids == null) return;
                foreach (var pid in ids)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (suppressedByGroup.Contains(pid)) continue; // member shown via [G] row
                    if (!seenParts.Add(pid)) continue;
                    newOrder.Add(new TaskOrderEntry { kind = "part", id = pid, isOptional = isOptional });
                    changed = true;
                }
            }
            AppendMissing(step.requiredPartIds, false);
            AppendMissing(step.optionalPartIds, true);
            AppendMissing(step.visualPartIds,   false);

            if (changed || newOrder.Count != originalCount)
            {
                step.taskOrder = newOrder.ToArray();
                if (markDirty) _dirtyStepIds.Add(step.id);
                InvalidateTaskOrderCache();
            }
        }

        /// <summary>
        /// Runs <see cref="ReconcileStepTaskOrder"/> on every step in the
        /// currently-loaded package. Pass <paramref name="markDirty"/>=false
        /// at load time so historical-drift healing doesn't leave every step
        /// in a dirty state (which would permanently disable Revert).
        /// </summary>
        private void ReconcileAllStepTaskOrders(bool markDirty = true)
        {
            if (_pkg?.steps == null) return;
            foreach (var s in _pkg.steps)
                ReconcileStepTaskOrder(s, markDirty);
        }

        /// <summary>
        /// Strips legacy empty-label stepPose entries from every part's
        /// <c>previewConfig.partPlacements[].stepPoses</c>. Empty-label
        /// entries are artifacts from the old AutoPromoteAlien flow that
        /// silently created per-step poses on gizmo drag; they persist in
        /// <c>preview_config.json</c> until explicitly removed.
        ///
        /// Author-created Customs now carry <c>label="Custom"</c> (see
        /// <see cref="AddStepPoseForCurrentStep"/>), so an empty label is
        /// unambiguously "legacy / not author-intended". Called silently at
        /// load time — no dirty marking, no save until the author makes a
        /// real change (mirrors the taskOrder reconciler's contract).
        /// </summary>
        private void StripEmptyLabelStepPoses()
        {
            var placements = _pkg?.previewConfig?.partPlacements;
            if (placements == null) return;
            foreach (var pp in placements)
            {
                if (pp == null || pp.stepPoses == null || pp.stepPoses.Length == 0) continue;
                var keep = new List<StepPoseEntry>(pp.stepPoses.Length);
                foreach (var sp in pp.stepPoses)
                {
                    if (sp == null) continue;
                    // Keep if label is non-empty (author-created) — legacy
                    // empty-label entries get dropped.
                    if (!string.IsNullOrEmpty(sp.label)) keep.Add(sp);
                }
                if (keep.Count != pp.stepPoses.Length)
                    pp.stepPoses = keep.ToArray();
            }
        }

        /// <summary>
        /// Removes <paramref name="partId"/> from every role array on
        /// <paramref name="step"/> (requiredPartIds, optionalPartIds,
        /// visualPartIds). Used by the task-row × button so deleting a row
        /// genuinely removes the part from the step — not just the authored
        /// ordering — and therefore can't be resurrected by the reconciler.
        /// Returns true if any array was modified.
        /// </summary>
        private bool RemovePartFromStepRoleArrays(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return false;
            bool changed = false;
            if (step.requiredPartIds != null)
            {
                var list = new List<string>(step.requiredPartIds);
                if (list.Remove(partId))
                {
                    step.requiredPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                    changed = true;
                }
            }
            if (step.optionalPartIds != null)
            {
                var list = new List<string>(step.optionalPartIds);
                if (list.Remove(partId))
                {
                    step.optionalPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                    changed = true;
                }
            }
            if (step.visualPartIds != null)
            {
                var list = new List<string>(step.visualPartIds);
                if (list.Remove(partId))
                {
                    step.visualPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                    changed = true;
                }
            }
            if (changed) _dirtyStepIds.Add(step.id);
            return changed;
        }

        /// <summary>
        /// Strips <paramref name="partId"/> from <c>visualPartIds</c> only.
        /// Used when a group [G] row is deleted: members were added by
        /// <c>CommitAddGroupAsNoTask</c> as NO TASK visuals, so undoing the
        /// group should only remove them from that array — not from
        /// requiredPartIds / optionalPartIds (where they may live as
        /// explicit, author-intended Task rows).
        /// </summary>
        private bool RemovePartFromVisualOnly(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId) || step.visualPartIds == null) return false;
            var list = new List<string>(step.visualPartIds);
            if (!list.Remove(partId)) return false;
            step.visualPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
            _dirtyStepIds.Add(step.id);
            return true;
        }

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

        // Cached per-part group lookups, rebuilt when the list rebuilds.
        // _partToGroupName — first group's display chain (kept for callers that
        // only need one line of context, e.g. step 3's dirty map).
        // _partToLeafGroupId — first leaf group id, used for the "currently
        // editing this group" indicator.
        // _partToGroupsAll — every leaf group the part belongs to, in the order
        // groups are enumerated. Enables the multi-pill row rendering so an
        // author can see at a glance when a part is shared across groups.
        private Dictionary<string, string> _partToGroupName;
        private Dictionary<string, string> _partToLeafGroupId;
        private Dictionary<string, List<PartGroupRef>> _partToGroupsAll;

        internal readonly struct PartGroupRef
        {
            public readonly string GroupId;
            public readonly string DisplayName;
            public readonly string Chain;    // parent > leaf when nested
            public PartGroupRef(string id, string name, string chain)
            { GroupId = id; DisplayName = name; Chain = chain; }
        }

        private void DrawTaskSequenceDragList(StepDefinition step, List<TaskOrderEntry> order)
        {
            // Rebuild the ReorderableList whenever the step changes or the list is null
            if (_taskSeqReorderList == null || _taskSeqReorderListForStepId != step.id)
            {
                // Build part→group-chain lookup for group tags on rows.
                // Shows the full nesting path: "Parent > Child" so the author
                // can see which groups a part belongs to at a glance.
                _partToGroupName   = new Dictionary<string, string>(StringComparer.Ordinal);
                _partToLeafGroupId = new Dictionary<string, string>(StringComparer.Ordinal);
                _partToGroupsAll   = new Dictionary<string, List<PartGroupRef>>(StringComparer.Ordinal);
                var allSubs = _pkg?.GetSubassemblies();
                if (allSubs != null)
                {
                    // Build child→parent map from memberSubassemblyIds
                    var childToParent = new Dictionary<string, SubassemblyDefinition>(StringComparer.Ordinal);
                    foreach (var sub in allSubs)
                    {
                        if (sub?.memberSubassemblyIds == null) continue;
                        foreach (var childId in sub.memberSubassemblyIds)
                        {
                            if (!string.IsNullOrEmpty(childId) && !childToParent.ContainsKey(childId))
                                childToParent[childId] = sub;
                        }
                    }

                    foreach (var sub in allSubs)
                    {
                        if (sub?.partIds == null || sub.isAggregate) continue;
                        // Build the chain: walk up parent links
                        string chain = sub.GetDisplayName();
                        if (childToParent.TryGetValue(sub.id, out var parent))
                            chain = parent.GetDisplayName() + " > " + chain;
                        foreach (var pid in sub.partIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            // First group wins for the single-value caches that
                            // only need a representative (step-3 dirty map, etc.)
                            if (!_partToGroupName.ContainsKey(pid))
                            {
                                _partToGroupName[pid]   = chain;
                                _partToLeafGroupId[pid] = sub.id;
                            }
                            // Multi-value cache collects EVERY leaf group for
                            // the part so the row renderer can show one pill
                            // per membership.
                            if (!_partToGroupsAll.TryGetValue(pid, out var list))
                                _partToGroupsAll[pid] = list = new List<PartGroupRef>();
                            bool already = false;
                            foreach (var r in list)
                                if (string.Equals(r.GroupId, sub.id, StringComparison.Ordinal))
                                { already = true; break; }
                            if (!already)
                                list.Add(new PartGroupRef(sub.id, sub.GetDisplayName(), chain));
                        }
                    }
                }
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

                    // No-task detection — a PART row whose id lives in
                    // visualPartIds. Not a task; numbered column gets a "·".
                    bool isNoTask = entry.kind == "part"
                                     && step.visualPartIds != null
                                     && Array.IndexOf(step.visualPartIds, entry.id) >= 0;

                    // Sequence number — computed from task rows only so
                    // "N." counts real tasks and "·" marks a no-task row.
                    int taskSeq = 0;
                    for (int ri = 0; ri <= index && ri < order.Count; ri++)
                    {
                        var other = order[ri];
                        bool otherNoTask = other.kind == "part"
                                            && step.visualPartIds != null
                                            && Array.IndexOf(step.visualPartIds, other.id) >= 0;
                        if (!otherNoTask) taskSeq++;
                    }
                    var numRect = new Rect(rect.x, rect.y + 1f, 22f, rect.height);
                    if (isNoTask)
                    {
                        var dotStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                        };
                        EditorGUI.LabelField(numRect, "·", dotStyle);
                    }
                    else
                    {
                        EditorGUI.LabelField(numRect, $"{taskSeq}", EditorStyles.miniLabel);
                    }

                    // Type badge — colored label (not a button; whole row is the click target)
                    Color badgeCol = entry.kind switch
                    {
                        "wire"           => _seqColorWire,
                        "part"           => _seqColorPart,
                        "confirm"        => _seqColorObserve,
                        "confirm_action" => _seqColorConfirm,
                        _                => _seqColorTool,
                    };
                    // Detect group-type part tasks
                    bool isGroup = entry.kind == "part"
                        && _pkg != null
                        && _pkg.TryGetSubassembly(entry.id, out _);

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
                        normal    = { textColor = badgeCol }, // always the kind's own colour
                        fontStyle = FontStyle.Bold,
                        fontSize  = 9,
                        alignment = TextAnchor.MiddleCenter,
                    };
                    float badgeW = isGroup ? 36f : 52f; // narrower to make room for [G]
                    var badgeRect = new Rect(rect.x + 24f, rect.y + 1f, badgeW, rect.height - 2f);
                    EditorGUI.LabelField(badgeRect, badge, badgeStyle);

                    // [G] indicator in blue, right after the green PART badge
                    if (isGroup)
                    {
                        var gStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal    = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                            fontStyle = FontStyle.Bold,
                            fontSize  = 9,
                            alignment = TextAnchor.MiddleLeft,
                        };
                        var gRect = new Rect(badgeRect.xMax, rect.y + 1f, 16f, rect.height - 2f);
                        GUI.Label(gRect, "[G]", gStyle);
                    }

                    // Tri-state role indicator — cycles R → O → I → R on click.
                    // For part rows, this is the fast way to move the partId
                    // between requiredPartIds / optionalPartIds / visualPartIds.
                    // Non-part kinds still use the legacy R↔O boolean toggle.
                    float reqOptW = 18f;
                    bool isOptional = entry.isOptional;
                    {
                        string roLabel = isNoTask ? "N" : (isOptional ? "O" : "R");
                        Color roColor  = isNoTask
                            ? new Color(0.55f, 0.78f, 0.95f)  // pale cyan (No Task)
                            : isOptional
                                ? new Color(0.95f, 0.70f, 0.20f)  // amber (Optional)
                                : new Color(0.30f, 0.78f, 0.36f); // green  (Required)
                        var roStyle = new GUIStyle(EditorStyles.miniButton)
                        {
                            fontSize  = 8,
                            fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleCenter,
                            normal    = { textColor = roColor },
                        };
                        var roRect = new Rect(rect.x + 78f, rect.y + 2f, 16f, rect.height - 4f);
                        string tooltip = isNoTask
                            ? "No Task — click to cycle back to Required (R → O → N → R)"
                            : (isOptional
                                ? "Optional — click to cycle to No Task"
                                : "Required — click to cycle to Optional");
                        if (GUI.Button(roRect, new GUIContent(roLabel, tooltip), roStyle))
                        {
                            if (entry.kind == "part")
                            {
                                // Cycle R → O → I → R. State is implied by
                                // which step array the partId lives in.
                                PartRole next;
                                if      (isNoTask)   next = PartRole.Required;
                                else if (isOptional) next = PartRole.NoTask;
                                else                 next = PartRole.Optional;
                                SetPartRoleForStep(step, entry.id, next);
                                entry.isOptional = (next == PartRole.Optional);
                            }
                            else
                            {
                                // Non-part kinds keep the legacy R↔O toggle.
                                entry.isOptional = !isOptional;
                            }
                            step.taskOrder = order.ToArray();
                            _dirtyStepIds.Add(step.id);
                            _dirtyTaskOrderStepIds.Add(step.id);
                            Repaint();
                        }
                    }


                    // ID label + group tag + per-task dirty dot
                    bool entryDirty = IsTaskEntryDirty(entry, step);

                    // Group tags — every leaf group the part belongs to renders
                    // as its own compact pill, so shared-across-groups parts are
                    // visible at a glance. First two pills render; the rest
                    // collapse into a "+N" overflow with the full list in the
                    // tooltip. Total strip width tracked for layout.
                    List<PartGroupRef> partGroups = null;
                    if (entry.kind == "part" && _partToGroupsAll != null)
                        _partToGroupsAll.TryGetValue(entry.id, out partGroups);
                    int totalGroups = partGroups?.Count ?? 0;
                    int visiblePills = Mathf.Min(totalGroups, 2);
                    int overflow     = totalGroups - visiblePills;

                    float tagW = 0f;
                    const int pillMaxChars = 16;
                    string[] pillLabels = visiblePills > 0 ? new string[visiblePills] : System.Array.Empty<string>();
                    float[]  pillWidths = visiblePills > 0 ? new float[visiblePills]  : System.Array.Empty<float>();
                    for (int p2 = 0; p2 < visiblePills; p2++)
                    {
                        string nm = partGroups[p2].DisplayName ?? "";
                        pillLabels[p2] = nm.Length > pillMaxChars ? nm.Substring(0, pillMaxChars - 1) + "…" : nm;
                        pillWidths[p2] = 8f + pillLabels[p2].Length * 5.2f;
                        tagW += pillWidths[p2] + (p2 > 0 ? 2f : 0f);
                    }
                    float overflowW = 0f;
                    string overflowLabel = null;
                    string overflowTip = null;
                    if (overflow > 0)
                    {
                        overflowLabel = $"+{overflow}";
                        overflowW = 8f + overflowLabel.Length * 5.5f;
                        var extras = new System.Text.StringBuilder();
                        for (int p2 = visiblePills; p2 < totalGroups; p2++)
                        {
                            if (p2 > visiblePills) extras.Append('\n');
                            extras.Append("• ").Append(partGroups[p2].Chain ?? partGroups[p2].DisplayName);
                        }
                        overflowTip = extras.ToString();
                        tagW += overflowW + (visiblePills > 0 ? 2f : 0f);
                    }

                    float dirtyW = entryDirty ? 14f : 0f;

                    // Reserve a dedicated slot for the "NO TASK" pill so it
                    // never overlaps the ID label. Drawn in the gap between
                    // the R|O|N toggle and the part-id text.
                    const float noTaskW = 58f;
                    float leadPad = isNoTask ? (noTaskW + 4f) : 0f;
                    if (isNoTask)
                    {
                        float ntX = rect.x + 80f + reqOptW - 18f; // sits right after the toggle
                        var ntRect = new Rect(ntX, rect.y + 3f, noTaskW, rect.height - 6f);
                        EditorGUI.DrawRect(ntRect, new Color(0.55f, 0.78f, 0.95f, 0.28f));
                        var ntStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal    = { textColor = new Color(0.70f, 0.88f, 1f) },
                            fontSize  = 8,
                            alignment = TextAnchor.MiddleCenter,
                            fontStyle = FontStyle.Bold,
                        };
                        GUI.Label(ntRect,
                            new GUIContent("NO TASK",
                                "Introduced here — no task attached. This part becomes visible at this step but the trainee is not required to interact with it."),
                            ntStyle);
                    }

                    float idX    = rect.x + 80f + reqOptW + leadPad;
                    float idW    = rect.width - 110f - tagW - dirtyW - reqOptW - leadPad;
                    var idRect   = new Rect(idX, rect.y + 1f, idW, rect.height);
                    // Show group display name + member count for [G] tasks, raw id for everything else.
                    // The count badge ("14 parts") makes group scope visible at a glance so authors
                    // don't need to expand members individually to know what the group contains.
                    string displayId = entry.id ?? "—";
                    if (isGroup && _pkg != null && _pkg.TryGetSubassembly(entry.id, out var dispSub) && dispSub != null)
                    {
                        int memberCount = dispSub.partIds?.Length ?? 0;
                        displayId = memberCount > 0
                            ? $"{dispSub.GetDisplayName()}  ({memberCount} part{(memberCount == 1 ? "" : "s")})"
                            : dispSub.GetDisplayName();
                    }
                    EditorGUI.LabelField(idRect, displayId, EditorStyles.miniLabel);

                    // ── Ownership-conflict / orphan badge (quiet when clean) ──
                    // Three possible states, one badge slot:
                    //   • red ⚠ — Rule-2 Place-family ownership conflict
                    //   • amber ⚠ — Rule-1 subassembly conflict
                    //   • blue ↺ — "orphan": part is Required/Optional/Visual
                    //     but not in step.taskOrder (shown because taskOrder
                    //     drifted out of sync with role arrays; author needs
                    //     to reorder or remove to commit)
                    float ownBadgeW = 0f;
                    float ownBadgeX = idRect.xMax + 2f;
                    bool isOrphanRow = entry.kind == "part"
                                       && _cachedOrphanTaskIds.Contains(entry.id);
                    if (entry.kind == "part" && !isGroup)
                    {
                        Color badgeFg = default;
                        Color badgeBg = default;
                        string badgeText = null;
                        string badgeTip  = null;
                        if (_ownership != null)
                        {
                            var own = _ownership.ForPart(entry.id);
                            if (own.HasMultiplePlaces)
                            {
                                // Info, not error — multi-placement is
                                // supported. Blue ↺ signals "this part is
                                // placed across multiple steps" so authors
                                // can verify intent.
                                badgeFg   = new Color(0.55f, 0.78f, 0.95f);
                                badgeBg   = new Color(0.55f, 0.78f, 0.95f, 0.28f);
                                badgeText = "↺";
                                badgeTip  = "Multi-placed: Required by Place step(s) "
                                          + string.Join(", ", own.placeStepIds)
                                          + " — runtime uses the most recent placement ≤ current step.";
                            }
                            else if (own.HasSubConflict)
                            {
                                badgeFg   = new Color(0.90f, 0.68f, 0.25f);
                                badgeBg   = new Color(0.90f, 0.68f, 0.25f, 0.28f);
                                badgeText = "⚠";
                                badgeTip  = "Subassembly conflict: also claimed by "
                                          + string.Join(", ", own.conflictingSubassemblyIds);
                            }
                        }
                        if (badgeText == null && isOrphanRow)
                        {
                            badgeFg   = new Color(0.55f, 0.78f, 0.95f);
                            badgeBg   = new Color(0.55f, 0.78f, 0.95f, 0.28f);
                            badgeText = "↺";
                            badgeTip  = "Orphan task row — this part is in the step's Required/"
                                      + "Optional/Visual list but missing from taskOrder. Drag to "
                                      + "position or remove; saving will commit the row.";
                        }
                        if (badgeText != null)
                        {
                            ownBadgeW = 18f;
                            var badgeBgRect = new Rect(ownBadgeX, rect.y + 3f, ownBadgeW, rect.height - 4f);
                            EditorGUI.DrawRect(badgeBgRect, badgeBg);
                            var warnStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal    = { textColor = badgeFg },
                                fontStyle = FontStyle.Bold,
                                alignment = TextAnchor.MiddleCenter,
                                fontSize  = 10,
                            };
                            GUI.Label(badgeBgRect, new GUIContent(badgeText, badgeTip), warnStyle);
                        }
                    }

                    // "Editing this group" membership is handled per-pill
                    // below: each pill computes its own isSelected against
                    // _canvasSelectedSubId, so no row-level flag is needed.
                    // Draw each visible group pill. Pill for the currently-
                    // selected group pops brighter + bolded so the author sees
                    // which membership the GROUPS-panel selection refers to.
                    if (totalGroups > 0)
                    {
                        float cursorX = idRect.xMax + 2f + ownBadgeW + (ownBadgeW > 0 ? 2f : 0f);
                        for (int p2 = 0; p2 < visiblePills; p2++)
                        {
                            var g = partGroups[p2];
                            bool isSelected = !string.IsNullOrEmpty(_canvasSelectedSubId)
                                              && string.Equals(g.GroupId, _canvasSelectedSubId, StringComparison.Ordinal);
                            var pillRect = new Rect(cursorX, rect.y + 3f, pillWidths[p2], rect.height - 4f);
                            Color pillBg = isSelected
                                ? new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.45f)
                                : new Color(0.20f, 0.62f, 0.95f, 0.18f);
                            EditorGUI.DrawRect(pillRect, pillBg);
                            var pillStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal    = { textColor = isSelected ? Color.white : new Color(0.50f, 0.78f, 0.98f) },
                                fontSize  = 8,
                                alignment = TextAnchor.MiddleCenter,
                                fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                            };
                            GUI.Label(pillRect,
                                new GUIContent(pillLabels[p2], isSelected ? g.Chain + "  (editing)" : g.Chain),
                                pillStyle);
                            cursorX += pillWidths[p2] + 2f;
                        }

                        if (overflow > 0)
                        {
                            var ovRect = new Rect(cursorX, rect.y + 3f, overflowW, rect.height - 4f);
                            EditorGUI.DrawRect(ovRect, new Color(0.35f, 0.55f, 0.80f, 0.22f));
                            var ovStyle = new GUIStyle(EditorStyles.miniLabel)
                            {
                                normal    = { textColor = new Color(0.70f, 0.82f, 0.95f) },
                                fontSize  = 8,
                                alignment = TextAnchor.MiddleCenter,
                                fontStyle = FontStyle.Bold,
                            };
                            GUI.Label(ovRect,
                                new GUIContent(overflowLabel,
                                    $"Also in:\n{overflowTip}"),
                                ovStyle);
                        }
                    }


                    if (entryDirty)
                    {
                        var dotStyle = new GUIStyle(EditorStyles.miniLabel)
                        {
                            normal    = { textColor = ColDirty },
                            fontStyle = FontStyle.Bold,
                            alignment = TextAnchor.MiddleLeft,
                        };
                        float dotX = totalGroups > 0 ? idRect.xMax + tagW + 4f : idRect.xMax + 2f;
                        EditorGUI.LabelField(new Rect(dotX, rect.y + 1f, 14f, rect.height), "●", dotStyle);
                    }

                    // Whole-row click (excluding × button) — MouseDown so it fires before
                    // onMouseUpCallback and can call Event.current.Use() to block it.
                    var rowClickRect = new Rect(rect.x, rect.y, rect.width - 26f, rect.height);
                    if (Event.current.type == EventType.MouseDown
                        && Event.current.button == 0 // left-click only; right-click → context menu
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

                    // Right-click context menu on task sequence rows
                    if (Event.current.type == EventType.ContextClick
                        && rowClickRect.Contains(Event.current.mousePosition))
                    {
                        // Resolve the row indices the menu should act on:
                        // multi-selection if the right-clicked row is part of it,
                        // otherwise just the single right-clicked row.
                        var contextRowIdxs = new List<int>();
                        if (_multiSelectedTaskSeqIdxs.Count > 1
                            && _multiSelectedTaskSeqIdxs.Contains(index))
                        {
                            foreach (int tidx in _multiSelectedTaskSeqIdxs)
                                if (tidx >= 0 && tidx < order.Count) contextRowIdxs.Add(tidx);
                            contextRowIdxs.Sort();
                        }
                        else
                        {
                            contextRowIdxs.Add(index);
                        }

                        // Part-only subset for group/membership actions (only
                        // those make sense for non-part rows).
                        var contextPartIds = new List<string>();
                        foreach (int tidx in contextRowIdxs)
                            if (order[tidx].kind == "part")
                                contextPartIds.Add(order[tidx].id);

                        {
                            var menu = new GenericMenu();

                            // ── Delete row(s) — works for any kind ────────────
                            var capturedOrder    = order;
                            var capturedRowIdxs  = new List<int>(contextRowIdxs);
                            var capturedDelStep  = step;

                            // Label distinguishes task vs no-task for clarity.
                            string delLabel;
                            if (capturedRowIdxs.Count == 1)
                            {
                                var e = order[capturedRowIdxs[0]];
                                bool isNoTaskRow = e.kind == "part"
                                    && step.visualPartIds != null
                                    && Array.IndexOf(step.visualPartIds, e.id) >= 0;
                                delLabel = isNoTaskRow ? $"Delete no-task '{e.id}'"
                                                       : $"Delete task '{e.id}'";
                            }
                            else
                            {
                                delLabel = $"Delete {capturedRowIdxs.Count} rows";
                            }

                            menu.AddItem(new GUIContent(delLabel), false,
                                () => RemoveTaskRowsAt(capturedDelStep, capturedOrder, capturedRowIdxs));

                            // The remaining items are part-scoped (group ops).
                            // Bail out of the menu early if no part rows were
                            // selected — but still show Delete above.
                            if (contextPartIds.Count == 0)
                            {
                                menu.ShowAsContext();
                                Event.current.Use();
                                return;
                            }

                            menu.AddSeparator("");
                            var capturedParts = contextPartIds;
                            var capturedStep  = step;
                            string countLabel = capturedParts.Count == 1
                                ? $"'{capturedParts[0]}'"
                                : $"{capturedParts.Count} selected parts";

                            // "Create new group from..."
                            menu.AddItem(
                                new GUIContent($"Create new group from {countLabel}"),
                                false,
                                () => CreateGroupFromSelection(capturedStep, capturedParts));

                            // "Add to [existing group]" — split into relevant (top) + rest (submenu)
                            var allSubs = _pkg?.GetSubassemblies();
                            if (allSubs != null && allSubs.Length > 0)
                            {
                                // Relevant = same assembly OR already contains a selected part
                                var partSet = new HashSet<string>(capturedParts, StringComparer.Ordinal);
                                string stepAsm = capturedStep.assemblyId ?? "";
                                var relevant = new List<SubassemblyDefinition>();
                                var others   = new List<SubassemblyDefinition>();

                                foreach (var sub in allSubs)
                                {
                                    if (sub == null || string.IsNullOrEmpty(sub.id)) continue;
                                    bool sameAsm = !string.IsNullOrEmpty(stepAsm)
                                                   && string.Equals(sub.assemblyId, stepAsm, StringComparison.Ordinal);
                                    bool hasPart = false;
                                    if (sub.partIds != null)
                                        foreach (var pid in sub.partIds)
                                            if (partSet.Contains(pid)) { hasPart = true; break; }
                                    if (sameAsm || hasPart) relevant.Add(sub);
                                    else                    others.Add(sub);
                                }

                                System.Action<SubassemblyDefinition, string> addMenuItem = (capturedSub, path) =>
                                {
                                    int existing = capturedSub.partIds?.Length ?? 0;
                                    menu.AddItem(
                                        new GUIContent($"{path}{capturedSub.GetDisplayName()}  ({existing}p)"),
                                        false,
                                        () =>
                                        {
                                            var currentSet = new HashSet<string>(
                                                capturedSub.partIds ?? Array.Empty<string>(),
                                                StringComparer.Ordinal);
                                            int added = 0;
                                            foreach (var pid in capturedParts)
                                                if (currentSet.Add(pid)) added++;
                                            if (added > 0)
                                            {
                                                capturedSub.partIds = currentSet.ToArray();
                                                _dirtySubassemblyIds.Add(capturedSub.id);
                                                ShowNotification(new GUIContent(
                                                    $"Added {added} part(s) to {capturedSub.GetDisplayName()}"));
                                                Repaint();
                                            }
                                            else
                                            {
                                                ShowNotification(new GUIContent("All parts already in group"));
                                            }
                                        });
                                };

                                if (relevant.Count > 0)
                                {
                                    menu.AddSeparator("");
                                    foreach (var sub in relevant)
                                        addMenuItem(sub, "Add to group/");
                                }

                                if (others.Count > 0)
                                {
                                    if (relevant.Count > 0)
                                        menu.AddSeparator("Add to group/");
                                    foreach (var sub in others)
                                        addMenuItem(sub, "Add to group/Other/");
                                }
                            }

                            menu.ShowAsContext();
                            Event.current.Use();
                        }
                    }

                    var removeRect = new Rect(rect.xMax - 22f, rect.y + 1f, 22f, rect.height - 2f);
                    if (GUI.Button(removeRect, "×", EditorStyles.miniButton))
                    {
                        RemoveTaskRowsAt(step, order, new List<int> { index });
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

            // ── Drop zone — Phase 7d ──────────────────────────────────────────
            // Drag a spawned part GameObject (or any GO whose name matches a
            // package part / target id) from the Hierarchy into this strip
            // and the editor adds it to the task sequence in one motion.
            DrawTaskSequenceDropZone(step, order);
        }

        /// <summary>
        /// Removes one or more rows from a step's task sequence. For part rows,
        /// also evicts the partId from the step's role arrays
        /// (required/optional/visual) so the reconciler doesn't re-add the
        /// row on the next pass. Group [G] rows additionally strip member
        /// partIds. Used by both the row's × button and the right-click
        /// "Delete row(s)" context menu item.
        /// </summary>
        private void RemoveTaskRowsAt(StepDefinition step, List<TaskOrderEntry> order, List<int> indices)
        {
            if (step == null || order == null || indices == null || indices.Count == 0) return;

            // Remove highest first so earlier indices stay valid.
            var sorted = new List<int>(indices);
            sorted.Sort();
            sorted.Reverse();

            foreach (int idx in sorted)
            {
                if (idx < 0 || idx >= order.Count) continue;
                var doomed = order[idx];
                if (doomed != null && doomed.kind == "part" && !string.IsNullOrEmpty(doomed.id))
                {
                    // Group [G] rows no longer own any member visualPartIds
                    // (CommitAddGroupAsNoTask stopped adding them), so
                    // deleting a group just removes the [G] row. Individual
                    // member rows the author added separately stay
                    // untouched. Plain part rows still evict from role
                    // arrays so the reconciler doesn't re-add them.
                    RemovePartFromStepRoleArrays(step, doomed.id);
                }
                order.RemoveAt(idx);
            }

            if (_selectedTaskSeqIdx >= order.Count) _selectedTaskSeqIdx = order.Count - 1;
            _multiSelectedTaskSeqIdxs.Clear();
            step.taskOrder = order.ToArray();
            _cachedTaskOrder = order;
            _dirtyTaskOrderStepIds.Add(step.id);
            _dirtyStepIds.Add(step.id);
            ReconcileStepTaskOrder(step);
            _taskSeqReorderListForStepId = null;
            Repaint();
        }

        // ── Drag-drop entry for the task sequence (Phase 7d) ──────────────────

        /// <summary>
        /// Renders a drop target below the reorderable list. Accepts
        /// GameObjects via DragAndDrop.objectReferences and resolves their
        /// names against the active package's parts → adds as a part task,
        /// or targets → adds as a toolAction task with the first wired tool
        /// (or no tool if none is available). Multi-drop is supported.
        /// </summary>
        private void DrawTaskSequenceDropZone(StepDefinition step, List<TaskOrderEntry> order)
        {
            EditorGUILayout.Space(2);

            var dropRect = GUILayoutUtility.GetRect(0, 28f, GUILayout.ExpandWidth(true));
            var ev       = Event.current;
            bool isHover = dropRect.Contains(ev.mousePosition);
            bool isDrag  = (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                           && DragAndDrop.objectReferences != null
                           && DragAndDrop.objectReferences.Length > 0;

            // ── Pre-drag probe ────────────────────────────────────────────────
            // Peek at every dragged object's acceptance WITHOUT mutating state
            // so the drop zone can warn pre-release. Outcomes:
            //   • Added + no reason → clean, green
            //   • Added + reason    → warning (Place-ownership collision that
            //                         the save-time dialog will auto-fix) —
            //                         amber, show reason, drop STILL succeeds
            //   • MatchedRejected   → hard "already required in THIS step"
            //                         duplicate — red, drop succeeds but toasts
            //   • Unmatched         → GO doesn't map to any partId/targetId
            bool probeAnyRejected = false;
            bool probeAnyUnmatched = false;
            bool probeAnyAccepted  = false;
            bool probeAnyWarning   = false;
            string probeFirstRejectReason = null;
            string probeFirstWarningReason = null;
            if (isHover && isDrag)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;
                    string nm = obj.name;
                    if (string.IsNullOrEmpty(nm)) continue;
                    switch (ProbeDropOutcome(step, nm, out string why))
                    {
                        case DropOutcome.Added:
                            probeAnyAccepted = true;
                            if (!string.IsNullOrEmpty(why))
                            {
                                probeAnyWarning = true;
                                if (probeFirstWarningReason == null) probeFirstWarningReason = why;
                            }
                            break;
                        case DropOutcome.MatchedRejected:
                            probeAnyRejected = true;
                            if (probeFirstRejectReason == null) probeFirstRejectReason = why;
                            break;
                        case DropOutcome.Unmatched:
                            probeAnyUnmatched = true;
                            break;
                    }
                }
            }

            // Visual: green when drop will succeed for every item, red when
            // at least one item would be rejected, amber when nothing matches
            // the package at all, muted when no drag is active.
            Color accent, bgColor;
            if (isHover && isDrag && probeAnyRejected)
            {
                accent  = new Color(0.95f, 0.45f, 0.35f);       // red — hard reject (duplicate in this step)
                bgColor = new Color(0.95f, 0.45f, 0.35f, 0.18f);
            }
            else if (isHover && isDrag && probeAnyWarning)
            {
                accent  = new Color(0.90f, 0.68f, 0.25f);       // amber — accepted with warning (resolve at save)
                bgColor = new Color(0.90f, 0.68f, 0.25f, 0.18f);
            }
            else if (isHover && isDrag && !probeAnyAccepted && probeAnyUnmatched)
            {
                accent  = new Color(0.90f, 0.68f, 0.25f);       // amber — unmatched
                bgColor = new Color(0.90f, 0.68f, 0.25f, 0.18f);
            }
            else if (isHover && isDrag)
            {
                accent  = new Color(0.30f, 0.78f, 0.36f);       // green — accept
                bgColor = new Color(0.30f, 0.78f, 0.36f, 0.18f);
            }
            else
            {
                accent  = new Color(0.45f, 0.45f, 0.50f);       // muted — idle
                bgColor = new Color(0f, 0f, 0f, 0.18f);
            }
            EditorGUI.DrawRect(dropRect, bgColor);
            // 1-px borders on all four edges
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.y, dropRect.width, 1f), accent);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.yMax - 1f, dropRect.width, 1f), accent);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.y, 1f, dropRect.height), accent);
            EditorGUI.DrawRect(new Rect(dropRect.xMax - 1f, dropRect.y, 1f, dropRect.height), accent);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isHover && isDrag ? FontStyle.Bold : FontStyle.Italic,
                wordWrap  = true,
            };
            string label;
            if (isHover && isDrag && probeAnyRejected)
                label = "✖ " + (probeFirstRejectReason ?? "One or more items rejected.");
            else if (isHover && isDrag && probeAnyWarning)
                label = "⚠ " + (probeFirstWarningReason ?? "Will add; resolve at save.");
            else if (isHover && isDrag && !probeAnyAccepted && probeAnyUnmatched)
                label = "? No dropped item matches a package part or target.";
            else if (isHover && isDrag)
                label = $"Drop {DragAndDrop.objectReferences.Length} item{(DragAndDrop.objectReferences.Length == 1 ? "" : "s")} here to add to the task sequence";
            else
                label = "Drag part / target GameObjects here to add them as tasks";
            GUI.Label(dropRect, label, labelStyle);

            // ── Event handling ────────────────────────────────────────────────
            if (!isHover) return;

            if (ev.type == EventType.DragUpdated)
            {
                // We WANT the red border + specific reason label to warn the
                // author pre-release, but we must NOT set visualMode=Rejected
                // on a mere Place-conflict: that disables the OS drag-drop
                // handshake entirely, and the author loses every other path
                // (e.g. drop anyway to confirm with a toast). Only refuse the
                // cursor when nothing matched the package at all — at that
                // point there's nothing to commit even if the author insists.
                bool nothingActionable = !probeAnyAccepted && !probeAnyRejected && probeAnyUnmatched;
                DragAndDrop.visualMode = (isDrag && !nothingActionable)
                    ? DragAndDropVisualMode.Copy
                    : DragAndDropVisualMode.Rejected;
                ev.Use();
                return;
            }

            if (ev.type == EventType.DragPerform && isDrag)
            {
                DragAndDrop.AcceptDrag();

                int added = 0;
                int matchedButRejected = 0;   // e.g. duplicate or Place-family conflict
                int unmatched = 0;            // name didn't resolve to any partId/targetId
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;
                    string name = obj.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    var outcome = TryDropResolveAndAddDetailed(step, name);
                    switch (outcome)
                    {
                        case DropOutcome.Added:            added++;              break;
                        case DropOutcome.MatchedRejected:  matchedButRejected++; break;
                        case DropOutcome.Unmatched:        unmatched++;          break;
                    }
                }

                if (added > 0)
                {
                    ShowNotification(new GUIContent($"Added {added} task{(added == 1 ? "" : "s")} from drop"));
                    _taskSeqReorderListForStepId = null; // force list rebuild
                    Repaint();
                }
                else if (matchedButRejected > 0)
                {
                    // The matching path already surfaced a specific reason
                    // (duplicate / Place conflict / etc.) via ShowNotification.
                    // Don't overwrite it with a generic "no match" — that was
                    // the drag-drop bug that inspired this refactor.
                }
                else if (unmatched > 0)
                {
                    ShowNotification(new GUIContent("No drop items matched a package part or target id"));
                }
                ev.Use();
            }
        }

        /// <summary>
        /// Outcome of a drag-drop resolution attempt. Callers use this to
        /// distinguish "added", "matched but rejected" (duplicate / Place
        /// conflict — the specific reason is shown via <see cref="ShowNotification"/>),
        /// and "unmatched" (the dragged GO's name doesn't correspond to any
        /// partId or targetId in the package).
        /// </summary>
        private enum DropOutcome { Added, MatchedRejected, Unmatched }

        /// <summary>
        /// Detailed version of the drop resolver so the caller can tell a
        /// "didn't match anything" from a "matched but refused" — the
        /// latter needs no extra toast because the rejection path already
        /// surfaced one. Without this split the generic "No drop items
        /// matched..." message overwrote the specific Place-conflict toast.
        /// </summary>
        private DropOutcome TryDropResolveAndAddDetailed(StepDefinition step, string name)
        {
            if (step == null || _pkg == null || string.IsNullOrEmpty(name)) return DropOutcome.Unmatched;

            if (_pkg.parts != null)
            {
                for (int i = 0; i < _pkg.parts.Length; i++)
                {
                    var p = _pkg.parts[i];
                    if (p == null || !string.Equals(p.id, name, StringComparison.Ordinal)) continue;

                    bool already = step.requiredPartIds != null
                                   && Array.IndexOf(step.requiredPartIds, p.id) >= 0;
                    if (already)
                    {
                        ShowNotification(new GUIContent($"'{p.id}' is already required by this step."));
                        return DropOutcome.MatchedRejected;
                    }
                    return CommitAddPart(step, p.id) ? DropOutcome.Added : DropOutcome.MatchedRejected;
                }
            }

            if (_pkg.GetTargets() != null)
            {
                foreach (var t in _pkg.GetTargets())
                {
                    if (t == null || !string.Equals(t.id, name, StringComparison.Ordinal)) continue;

                    bool already = step.targetIds != null
                                   && Array.IndexOf(step.targetIds, t.id) >= 0;
                    if (already)
                    {
                        ShowNotification(new GUIContent($"'{t.id}' is already a target of this step."));
                        return DropOutcome.MatchedRejected;
                    }

                    var firstTool = _pkg.GetTools()?.FirstOrDefault(td => td != null && !string.IsNullOrEmpty(td.id));
                    if (firstTool != null)
                    {
                        CommitAddToolTarget(step, t.id, firstTool.id);
                    }
                    else
                    {
                        var tList = new List<string>(step.targetIds ?? System.Array.Empty<string>()) { t.id };
                        step.targetIds = tList.ToArray();
                        var orderLocal = GetOrDeriveTaskOrder(step);
                        orderLocal.Add(new TaskOrderEntry { kind = "target", id = t.id });
                        step.taskOrder = orderLocal.ToArray();
                        InvalidateTaskOrderCache();
                        UpdateActiveStep();
                        _dirtyStepIds.Add(step.id);
                        BuildTargetList();
                    }
                    return DropOutcome.Added;
                }
            }

            // Subassembly (group) drop — adds a [G] task row + introduces every
            // member as NO TASK (visualPartIds) so the runtime renders them at
            // this step. Group GOs are named "Group_{displayName}", not the
            // subassembly id; resolve via the live root-GO dictionary first,
            // then by display name as a fallback.
            var subResolved = ResolveDroppedSubassembly(name);
            if (subResolved != null)
            {
                bool already = false;
                if (step.taskOrder != null)
                    foreach (var e in step.taskOrder)
                        if (e != null && e.kind == "part" && string.Equals(e.id, subResolved.id, StringComparison.Ordinal))
                        { already = true; break; }
                if (already)
                {
                    ShowNotification(new GUIContent($"'{subResolved.GetDisplayName()}' group is already in this step's task sequence."));
                    return DropOutcome.MatchedRejected;
                }
                CommitAddGroupAsNoTask(step, subResolved);
                return DropOutcome.Added;
            }

            return DropOutcome.Unmatched;
        }

        /// <summary>
        /// Resolves a dragged GameObject's name to a <see cref="SubassemblyDefinition"/>.
        /// Tries the live group-root-GO dictionary first (where keys are
        /// subassembly ids and values are the spawned GO that the user
        /// actually dragged), then falls back to matching by id and finally
        /// by display name. Returns null when nothing matches.
        /// </summary>
        private SubassemblyDefinition ResolveDroppedSubassembly(string droppedName)
        {
            if (string.IsNullOrEmpty(droppedName) || _pkg?.subassemblies == null) return null;

            // Reverse-lookup: which subassembly id has a root GO with this name?
            if (_subassemblyRootGOs != null)
            {
                foreach (var kvp in _subassemblyRootGOs)
                {
                    if (kvp.Value == null) continue;
                    if (!string.Equals(kvp.Value.name, droppedName, StringComparison.Ordinal)) continue;
                    if (_pkg.TryGetSubassembly(kvp.Key, out var found) && found != null) return found;
                }
            }

            foreach (var sub in _pkg.subassemblies)
            {
                if (sub == null) continue;
                if (string.Equals(sub.id, droppedName, StringComparison.Ordinal)) return sub;
                // "Group_{displayName}" naming convention from EnsureAllSubassemblyRoots
                if (string.Equals("Group_" + sub.GetDisplayName(), droppedName, StringComparison.Ordinal)) return sub;
                if (string.Equals(sub.GetDisplayName(), droppedName, StringComparison.Ordinal)) return sub;
            }
            return null;
        }

        /// <summary>
        /// Adds <paramref name="sub"/> to <paramref name="step"/> as a NO TASK
        /// group: every member partId joins <c>step.visualPartIds</c> (so the
        /// runtime shows them at this step without a placement task) and a
        /// task-order entry with the subassembly id is appended so the [G]
        /// row renders in authoring. Does NOT set
        /// <c>step.requiredSubassemblyId</c> — that field means "this step
        /// stacks the group onto a target", which is a placement action, not
        /// a NO TASK display.
        /// </summary>
        private void CommitAddGroupAsNoTask(StepDefinition step, SubassemblyDefinition sub)
        {
            if (step == null || sub == null || string.IsNullOrEmpty(sub.id)) return;

            // Group is a TASK HANDLE, not a visibility expander. Dropping a
            // group adds ONLY the [G] row — members are NOT auto-added to
            // visualPartIds. Authors introduce individual parts by dragging
            // them in directly (Q2 design: "individual parts in steps mark
            // visibility; groups don't define it"). Animation cues targeting
            // this group still operate over all live members at runtime via
            // the transient-anim-group promotion path.
            var order = GetOrDeriveTaskOrder(step);
            order.Add(new TaskOrderEntry { kind = "part", id = sub.id });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            _dirtyStepIds.Add(step.id);
            ReconcileStepTaskOrder(step);
            BuildPartList();
            Repaint();
        }

        /// <summary>Legacy wrapper for callers that only want a success bool.</summary>
        private bool TryDropResolveAndAdd(StepDefinition step, string name)
            => TryDropResolveAndAddDetailed(step, name) == DropOutcome.Added;

        /// <summary>
        /// Pure probe: runs the same acceptance logic as
        /// <see cref="TryDropResolveAndAddDetailed"/> without mutating state
        /// or firing notifications. Called from the DragUpdated handler so
        /// the drop zone can paint its rejection reason inline BEFORE the
        /// author commits the drop. <paramref name="reason"/> is populated on
        /// <see cref="DropOutcome.MatchedRejected"/> with a short, specific
        /// explanation the drop zone can echo.
        /// </summary>
        private DropOutcome ProbeDropOutcome(StepDefinition step, string name, out string reason)
        {
            reason = null;
            if (step == null || _pkg == null || string.IsNullOrEmpty(name)) return DropOutcome.Unmatched;

            if (_pkg.parts != null)
            {
                for (int i = 0; i < _pkg.parts.Length; i++)
                {
                    var p = _pkg.parts[i];
                    if (p == null || !string.Equals(p.id, name, StringComparison.Ordinal)) continue;

                    if (step.requiredPartIds != null && Array.IndexOf(step.requiredPartIds, p.id) >= 0)
                    {
                        reason = $"'{p.id}' is already required by this step.";
                        return DropOutcome.MatchedRejected;
                    }
                    if (step.ResolvedFamily == StepFamily.Place)
                    {
                        // Pre-surface the Place-owner collision so the author
                        // sees it in the drop-zone label before release, but
                        // return Added so the drop commits. The commit path
                        // will warn again; the save-time dialog is the final
                        // enforcer with an auto-fix button.
                        var o = _ownership.ForPart(p.id);
                        if (o.HasPlaceOwner
                            && !string.Equals(o.placeStepId, step.id, StringComparison.Ordinal))
                        {
                            reason = $"'{p.id}' is also placed by {o.placeStepId} (#{o.placeStepSeq}). Add anyway — resolve at save.";
                        }
                    }
                    return DropOutcome.Added;
                }
            }

            if (_pkg.GetTargets() != null)
            {
                foreach (var t in _pkg.GetTargets())
                {
                    if (t == null || !string.Equals(t.id, name, StringComparison.Ordinal)) continue;
                    if (step.targetIds != null && Array.IndexOf(step.targetIds, t.id) >= 0)
                    {
                        reason = $"'{t.id}' is already a target of this step.";
                        return DropOutcome.MatchedRejected;
                    }
                    return DropOutcome.Added;
                }
            }

            // Subassembly (group) drop probe — same resolution as the commit
            // path so the drop zone shows the right colour pre-release.
            var subResolved = ResolveDroppedSubassembly(name);
            if (subResolved != null)
            {
                if (step.taskOrder != null)
                    foreach (var e in step.taskOrder)
                        if (e != null && e.kind == "part" && string.Equals(e.id, subResolved.id, StringComparison.Ordinal))
                        {
                            reason = $"'{subResolved.GetDisplayName()}' group is already in this step's task sequence.";
                            return DropOutcome.MatchedRejected;
                        }
                return DropOutcome.Added;
            }

            return DropOutcome.Unmatched;
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
            _canvasSelectedSubId = null; // clear subassembly selection when a task is clicked
            _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5; // suppress false dirty from handle re-init after selection change
            switch (entry.kind)
            {
                case "part":
                    _selectedIdx = -1;
                    _multiSelected.Clear();
                    _multiSelectedParts.Clear();

                    // Check if this "part" entry is actually a group (subassembly)
                    bool isGroupEntry = _pkg != null && _pkg.TryGetSubassembly(entry.id, out _);
                    if (isGroupEntry)
                    {
                        // Group task — keep _selectedTaskSeqIdx active (set by caller)
                        // so DrawTaskInspectorBody shows the group pose fields.
                        // Do NOT set _canvasSelectedSubId — that's for the canvas
                        // GROUPS section click, not for task sequence selection.
                        _selectedPartIdx = -1;
                        _selectedPartId  = null;
                        _selectedGroupIdx = FindGroupIdx(entry.id);
                        // Ping the root in Hierarchy without selecting it
                        if (_subassemblyRootGOs.TryGetValue(entry.id, out var rootGO) && rootGO != null)
                            EditorGUIUtility.PingObject(rootGO);
                        Selection.activeGameObject = null;
                    }
                    else if (_parts != null && _parts.Length > 0)
                    {
                        // Individual part — find it in _parts[]
                        int pick = -1;
                        for (int i = 0; i < _parts.Length; i++)
                            if (_parts[i].def?.id == entry.id) { pick = i; break; }
                        if (pick >= 0)
                        {
                            _selectedPartIdx = pick;
                            _selectedPartId  = _parts[pick].def?.id;
                            SyncAllPartMeshesToActivePose();
                            var liveGO = FindLivePartGO(_selectedPartId);
                            if (liveGO != null) UnityEditor.Selection.activeGameObject = liveGO;
                        }
                        else
                        {
                            _selectedPartIdx = -1;
                            _selectedPartId  = null;
                        }
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
                        // Align _selectedPartIdx with the part we're actually
                        // rendering so DrawPartPoseToggle's NO TASK detection
                        // (which reads _selectedPartIdx) resolves against the
                        // correct part. Without this, the inspector showed
                        // Start/Assembled buttons for parts that should
                        // display the NO TASK transform block.
                        if (_selectedPartIdx != i)
                        {
                            _selectedPartIdx = i;
                            _selectedPartId  = _parts[i].def?.id;
                        }
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

        private void DrawAddGroupPicker(StepDefinition step)
        {
            if (_pkg == null) return;
            var allSubs = _pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0) { _addTaskPicker = AddTaskPicker.None; return; }

            // Filter: non-aggregate groups only, not already set as requiredSubassemblyId
            var candidates = new List<SubassemblyDefinition>();
            foreach (var sub in allSubs)
            {
                if (sub == null || sub.isAggregate || string.IsNullOrEmpty(sub.id)) continue;
                candidates.Add(sub);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Add Group to Step", EditorStyles.boldLabel);
            if (candidates.Count == 0)
            {
                EditorGUILayout.LabelField("  No groups available.", EditorStyles.miniLabel);
            }
            else
            {
                string[] opts = candidates.ConvertAll(s => $"{s.GetDisplayName()}  ({s.partIds?.Length ?? 0}p)").ToArray();
                _addPickerGroupIdx = Mathf.Clamp(_addPickerGroupIdx, 0, opts.Length - 1);
                _addPickerGroupIdx = EditorGUILayout.Popup("Group", _addPickerGroupIdx, opts);
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(candidates.Count == 0);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                CommitAddGroup(step, candidates[_addPickerGroupIdx].id);
                _addTaskPicker = AddTaskPicker.None;
            }
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(60))) _addTaskPicker = AddTaskPicker.None;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void CommitAddGroup(StepDefinition step, string subId)
        {
            if (step == null || string.IsNullOrEmpty(subId)) return;
            step.requiredSubassemblyId = subId;
            var order = GetOrDeriveTaskOrder(step);
            // Only add if not already present
            bool exists = false;
            foreach (var e in order)
                if (e.kind == "part" && e.id == subId) { exists = true; break; }
            if (!exists)
            {
                order.Add(new TaskOrderEntry { kind = "part", id = subId });
                step.taskOrder = order.ToArray();
            }
            InvalidateTaskOrderCache();
            _dirtyStepIds.Add(step.id);
            _taskSeqReorderListForStepId = null;
            Repaint();
        }

        private void CommitAddConfirmAction(StepDefinition step)
        {
            if (step == null) return;
            var order = GetOrDeriveTaskOrder(step);
            // Only add one confirm_action per step
            foreach (var e in order)
                if (e.kind == "confirm_action") return;
            order.Add(new TaskOrderEntry { kind = "confirm_action", id = "confirm" });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            _dirtyStepIds.Add(step.id);
            _dirtyTaskOrderStepIds.Add(step.id);
            _taskSeqReorderListForStepId = null; // force rebuild
            Repaint();
        }

        // ── Required/Optional toggle for part tasks ──────────────────────────

        /// <summary>
        /// Toggles a part between requiredPartIds and optionalPartIds.
        /// If currently optional → move to required. If required → move to optional.
        /// </summary>
        private void TogglePartRequiredOptional(StepDefinition step, string partId, bool isCurrentlyOptional)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;

            if (isCurrentlyOptional)
            {
                // Move from optional → required
                var optList = new List<string>(step.optionalPartIds ?? Array.Empty<string>());
                optList.Remove(partId);
                step.optionalPartIds = optList.Count > 0 ? optList.ToArray() : Array.Empty<string>();

                var reqList = new List<string>(step.requiredPartIds ?? Array.Empty<string>());
                if (!reqList.Contains(partId)) reqList.Add(partId);
                step.requiredPartIds = reqList.ToArray();
            }
            else
            {
                // Move from required → optional
                var reqList = new List<string>(step.requiredPartIds ?? Array.Empty<string>());
                reqList.Remove(partId);
                step.requiredPartIds = reqList.Count > 0 ? reqList.ToArray() : Array.Empty<string>();

                var optList = new List<string>(step.optionalPartIds ?? Array.Empty<string>());
                if (!optList.Contains(partId)) optList.Add(partId);
                step.optionalPartIds = optList.ToArray();
            }

            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        // ── Commit helpers (modify in-memory step data + mark dirty) ──────────

        /// <summary>
        /// Adds <paramref name="partId"/> to <paramref name="step"/>'s
        /// requiredPartIds. Returns true if the add happened; false if
        /// rejected (e.g. Place-family ownership conflict). Callers that
        /// surface a "Part added" toast MUST check the return value —
        /// showing the toast on false was the "said added but not in list"
        /// bug from the pose rewrite.
        /// </summary>
        private bool CommitAddPart(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return false;

            // Warn (don't block) when another Place-family step also requires
            // this partId. The runtime rule still says "one Place owner per
            // partId" (PartOwnershipExclusivityPass) and the save-time dialog
            // offers auto-fix, but blocking at input time was too aggressive
            // for authoring — the user may be moving ownership from one step
            // to another and needs the add to land first. Rule-1 multi-
            // subassembly already behaves this way (warn-only at input, fire
            // at save); this brings Rule 2 in line.
            if (step.ResolvedFamily == StepFamily.Place && _pkg?.steps != null)
            {
                foreach (var other in _pkg.steps)
                {
                    if (other == null || other == step) continue;
                    if (other.ResolvedFamily != StepFamily.Place) continue;
                    if (other.requiredPartIds == null) continue;
                    foreach (var op in other.requiredPartIds)
                    {
                        if (string.Equals(op, partId, StringComparison.Ordinal))
                        {
                            ShowNotification(new GUIContent(
                                $"⚠ '{partId}' is also Required in Place step '{other.id}'. Runtime expects one Place owner — resolve at save time (auto-fix) or remove from the other step."));
                            goto PlaceConflictHandled;
                        }
                    }
                }
            }
            PlaceConflictHandled:

            var list = new List<string>(step.requiredPartIds ?? System.Array.Empty<string>()) { partId };
            step.requiredPartIds = list.ToArray();
            var order = GetOrDeriveTaskOrder(step);
            order.Add(new TaskOrderEntry { kind = "part", id = partId });
            step.taskOrder = order.ToArray();
            InvalidateTaskOrderCache();
            _dirtyStepIds.Add(step.id);
            BuildPartList();
            Repaint();
            return true;
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

                // Cue-count badges — at-a-glance signal of which parts host
                // animation / particle cues. Drawn right-aligned inside the
                // same row so the label remains the primary read.
                var cueArea = new Rect(labelRect.xMax - 80f, labelRect.y, 78f, labelRect.height);
                DrawCueCountBadges(cueArea, p.def);

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
                    // Detect if this "part" task is actually a group (subassembly)
                    SubassemblyDefinition groupDef = null;
                    bool isGroupTask = _pkg != null
                        && _pkg.TryGetSubassembly(selEntry.id, out groupDef)
                        && groupDef != null;

                    if (isGroupTask)
                    {
                        // Show group pose fields + group detail
                        DrawUnifiedSectionHeader($"GROUP: {groupDef.GetDisplayName()}", 0);

                        // Find the GroupEditState for this subassembly
                        int gIdx = FindGroupIdx(selEntry.id);
                        if (gIdx >= 0 && _groups != null && gIdx < _groups.Length)
                        {
                            DrawGroupPoseFields(ref _groups[gIdx], step);
                        }

                        // Group membership editor (parts, steps, name, description)
                        DrawSubassemblyInlineEditor(groupDef, step);

                        // Per-group cue authoring strip — adds the "+ Add cue"
                        // affordance with a Rotate / Shake picker, and lists
                        // existing cues whose targetSubassemblyId matches this
                        // group. Mirrors the part/tool patterns in TTAW.CueContext.cs.
                        DrawCuesForSubassembly(step, selEntry.id);
                    }
                    else
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
                        DrawPartOwnershipSection(selEntry.id);
                        if (_parts != null)
                            for (int i = 0; i < _parts.Length; i++)
                                if (_parts[i].def?.id == selEntry.id)
                                { DrawPartDetailPanel(ref _parts[i]); break; }
                    }

                    // Phase 7c cue affordance moved to DrawInspectorContextualSections
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

                    // Phase 7c cue affordance moved to DrawInspectorContextualSections
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
