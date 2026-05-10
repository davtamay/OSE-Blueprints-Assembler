// TTAW.InteractionPosePill.cs — Start / End pose pill row + per-task pose
// locks + followPart authoring sync for kind="toolAction" tasks with an
// interaction payload (lerp / thread_in archetypes).
// ──────────────────────────────────────────────────────────────────────────────
// What renders:
//   - Live part GO at its inherited start pose (untouched here unless the
//     task is locked or followPart is on and the target is being dragged).
//   - Live tool GO at its working pose (driven by TTAW.Preview.cs).
//   - No ghost overlay: the live tool + live part are the visualization.
//     Earlier iterations rendered a translucent "where the bolt ends up"
//     ghost via Graphics.DrawMesh, but it duplicated the live bolt visually
//     (especially with followPart=true making the live bolt track the
//     target gizmo) and the user removed it as redundant.
//
// What this partial owns:
//   - The Start | End pill row in the InteractionPanel — primarily a
//     selector for the lock-target pose and a hint for which pose the
//     end-transform represents.
//   - Per-task pose locks scoped to a step. Lock 🔒/🔓 in the pill row.
//     ApplyAllLockedPoses re-applies every render so SyncAllPartMeshesToActivePose
//     and other movers don't override.
//   - followPart authoring sync. When followPart=true on a tool×part task,
//     dragging the target gizmo also moves entry.endTransform AND the live
//     part GO by the same delta (so the bolt under the drill follows).
//
// Edit-mode only — gated by the existing !Application.isPlaying guard in
// OnSceneGUI per feedback_ttaw_play_isolation_must_preserve_readonly_display.

using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private enum InteractionPosePill { Start, End }

        private InteractionPosePill _activePosePill = InteractionPosePill.Start;

        // Identity of the (step, taskOrder entry) currently rendering a ghost.
        // Used by RestoreLiveMeshToInheritedPose to detect "the selection moved
        // to a different task" and by the SceneView gizmo to find the entry.
        private string _activePosePillStepId;
        private string _activePosePillEntryRef;   // entry.id (action id)

        // ── Per-task pose locks ──────────────────────────────────────────────
        //
        // When a task is locked, its part GO is snapped to the chosen pose
        // (Start = the originals captured at lock time = inherited;
        // End = entry.endTransform) and stays there as the author navigates
        // to other tasks. Lets the author compose multi-task visualizations:
        // lock task 1's bolt at End, click task 2, lock task 2 at End, etc.,
        // to see all four bolts at their tightened positions simultaneously.
        //
        // All locks reset when the user crosses to a different step
        // (_locksStepId mismatch) so locks don't leak across step boundaries.
        // Tools are not locked — _toolPreviewGO is per-active-target and
        // respawns on selection change; we'd need a parallel pool to keep
        // multiple tools visible, out of scope for this slice.
        private sealed class LockedPose
        {
            public string TaskEntryId;
            public string PartId;
            public GameObject PartGO;
            public Vector3 OriginalLocalPos;
            public Quaternion OriginalLocalRot;
            public Vector3 OriginalLocalScale;
            public InteractionPosePill Pill;
        }
        private string _locksStepId;
        private readonly System.Collections.Generic.List<LockedPose> _locks =
            new System.Collections.Generic.List<LockedPose>();

        // ── Inherited-start cache ────────────────────────────────────────────
        //
        // For each task entry we render a pill row for, capture the part's
        // local TRS the first time we see the entry. That pose IS the
        // inherited start (the part hasn't been authored-dragged yet — the
        // pill row only renders after task selection, and the followPart
        // sync only fires on subsequent drags). Cache lets "Start" pill
        // click visually return the part there even after a drag has moved
        // the live mesh to the end pose.
        //
        // Cleared on step boundary by EnsureLocksFreshForStep.
        private struct InheritedPose { public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; }
        private readonly System.Collections.Generic.Dictionary<string, InheritedPose>
            _inheritedStartCache =
                new System.Collections.Generic.Dictionary<string, InheritedPose>();


        // ── UI: pill row ──────────────────────────────────────────────────────

        /// <summary>
        /// Renders the Start | End pill row above the existing inline-end-transform
        /// numeric fields. Records which (step, entry) is active so the SceneView
        /// gizmo + ghost overlays can find them. Does NOT move the live mesh —
        /// the live part is always at its inherited start; the ghost is rendered
        /// over the scene at the end pose.
        /// </summary>
        internal void DrawInteractionPosePillRow(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (step == null || action == null || entry == null) return;
            if (entry.endTransform == null) return;

            // Cross-step boundary check — if the active step differs from the
            // step the existing locks belong to, restore all locked parts and
            // clear the lock list before doing anything else this frame.
            EnsureLocksFreshForStep(step.id);

            // Single-select pill state is keyed by entry.id every render —
            // _pillByEntry is the source of truth. This is unconditional
            // (not gated on entryChanged) so any code path that bypasses
            // ApplyTaskEntrySelection (scene-icon click, programmatic
            // selection, IMGUI re-entry) still self-corrects. Multi-select
            // ignores _activePosePill and computes its own shared/mixed
            // state below, so this assignment is harmless there.
            bool inMultiSelect = _multiSelected.Count > 1 && _activeTaskKind == "toolAction";
            if (!inMultiSelect)
            {
                _activePosePill = _pillByEntry.TryGetValue(entry.id, out var remembered)
                    ? remembered
                    : InteractionPosePill.Start;
            }

            // Track the active task so the OnSceneGUI gizmo + ghost can resolve it.
            _activePosePillStepId   = step.id;
            _activePosePillEntryRef = entry.id;

            // Capture the inherited start for the active task on first sight.
            // For multi-select, also capture each selected task's start —
            // they're guaranteed to be at start the moment they enter the
            // selection (no drag has happened yet for that task).
            CaptureInheritedStartIfMissing(action, entry);
            if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction" && _targets != null)
            {
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    if (TryResolveFollowedInteractionForTargetIdx(step, idx, out var sa, out var se))
                        CaptureInheritedStartIfMissing(sa, se);
                }
            }

            // Re-apply every lock every render (cheap; idempotent). This
            // covers code paths elsewhere in TTAW that move part GOs (e.g.
            // SyncAllPartMeshesToActivePose) — without re-application a
            // locked part would silently drift back to the inherited pose.
            ApplyAllLockedPoses();

            // Re-apply pose pill every render. SyncAllPartMeshesToActivePose
            // can override the snap on its own triggers — re-applying keeps
            // the pill view stable. Both Start and End re-apply when the
            // pill targets an authored pose (entry.startTransform on Start
            // pill, entry.endTransform always for End). Without the Start
            // re-apply, step navigation parks parts via poseTable (which
            // doesn't track entry.startTransform), and re-entering the
            // step shows the authored start being silently overridden by
            // the assembled-pose poseTable result — fixed only by manually
            // flipping End→Start to trigger an explicit SnapPartToPill.
            bool startHasOverrideForRedraw = entry != null && entry.startTransform != null;
            bool shouldReapply =
                (_activePosePill == InteractionPosePill.End)
                || (_activePosePill == InteractionPosePill.Start && startHasOverrideForRedraw);
            if (shouldReapply)
            {
                if (!IsTaskLocked(entry.id))
                    SnapPartToPill(action, entry);
                if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction" && _targets != null)
                {
                    foreach (int idx in _multiSelected)
                    {
                        if (idx < 0 || idx >= _targets.Length) continue;
                        if (TryResolveFollowedInteractionForTargetIdx(step, idx, out var sa, out var se)
                            && !IsTaskLocked(se.id))
                        {
                            // Skip per-task snap on Start pill when this
                            // sibling has no authored override — would snap
                            // to inherited (assembled) which the poseTable
                            // already produced. Avoids unnecessary churn.
                            if (_activePosePill == InteractionPosePill.Start
                                && (se == null || se.startTransform == null))
                                continue;
                            SnapPartToPill(sa, se);
                        }
                    }
                }
            }

            // Multi-select context: when several tool tasks are shift-selected,
            // the pill + lock act on the whole selection. "Set" semantics: pill
            // chooses target pose; Lock button locks/unlocks ALL selected at
            // that pose; switching pill while locked re-snaps every lock.
            bool isMulti = _multiSelected.Count > 1 && _activeTaskKind == "toolAction";
            int multiCount = isMulti ? CountSelectedFollowedToolTasks(step) : 0;
            isMulti = isMulti && multiCount > 1;

            bool taskIsLocked = isMulti
                ? AreAllSelectedToolTasksLocked(step)
                : IsTaskLocked(entry.id);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Pose:", GUILayout.Width(40));

            // Multi-select: derive shared/mixed state from per-entry memory so
            // the pill reflects all selected tasks, not just the active one.
            // _activePosePill drives single-select rendering and is also the
            // "anchor" for highlighting when multi-select is unanimous; mixed
            // state highlights neither pill and any click promotes the whole
            // selection to that pose.
            bool multiMixed       = false;
            int multiStartCount   = 0;
            int multiEndCount     = 0;
            if (isMulti)
            {
                ComputeMultiSelectPillState(step, out multiMixed, out multiStartCount, out multiEndCount);
            }

            bool isStart = isMulti
                ? (!multiMixed && multiStartCount > 0)
                : _activePosePill == InteractionPosePill.Start;
            bool isEnd   = isMulti
                ? (!multiMixed && multiEndCount > 0)
                : _activePosePill == InteractionPosePill.End;

            // Pill toggles are only disabled in single-select while locked
            // (the lock pinned the pose; user must unlock to change). In
            // multi-select they stay live so the user can flip pose for the
            // whole selection without unlock/relock churn.
            using (new EditorGUI.DisabledScope(taskIsLocked && !isMulti))
            {
                bool startHasOverride = !isMulti && entry != null && entry.startTransform != null;
                string startLabel = startHasOverride ? "● Start" : "Start";
                if (GUILayout.Toggle(isStart, new GUIContent(startLabel,
                        startHasOverride
                            ? "Per-task start override is set on this task. Click ⟲ Revert to clear it."
                            : "Inherited start pose (chain-derived from upstream tool task)."),
                        PoseToggleStyle(isStart, PoseAccentStart, EditorStyles.miniButtonLeft),
                        GUILayout.Height(18)))
                {
                    bool clickActivates = isMulti ? (multiMixed || !isStart) : !isStart;
                    if (clickActivates)
                    {
                        _activePosePill = InteractionPosePill.Start;
                        if (isMulti) { RecordPillForSelection(step, InteractionPosePill.Start); SnapAllSelectedPartsToPill(step); RetargetSelectedLocksToActivePill(step); }
                        else         { RecordPillForEntry(entry.id, InteractionPosePill.Start); SnapPartToPill(action, entry); }
                        SceneView.RepaintAll();
                    }
                }
                if (GUILayout.Toggle(isEnd, "End",
                        PoseToggleStyle(isEnd, PoseAccentAssembled, EditorStyles.miniButtonRight),
                        GUILayout.Height(18)))
                {
                    bool clickActivates = isMulti ? (multiMixed || !isEnd) : !isEnd;
                    if (clickActivates)
                    {
                        _activePosePill = InteractionPosePill.End;
                        if (isMulti) { RecordPillForSelection(step, InteractionPosePill.End); SnapAllSelectedPartsToPill(step); RetargetSelectedLocksToActivePill(step); }
                        else         { RecordPillForEntry(entry.id, InteractionPosePill.End); SnapPartToPill(action, entry); }
                        SceneView.RepaintAll();
                    }
                }
                if (isMulti && multiMixed)
                {
                    var mixedStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = PoseAccentAssembled },
                        fontStyle = FontStyle.Bold,
                    };
                    EditorGUILayout.LabelField(
                        new GUIContent("Mixed",
                            $"{multiStartCount} on Start, {multiEndCount} on End. " +
                            "Click either pill to set every selected task to that pose."),
                        mixedStyle, GUILayout.Width(48));
                }
            }

            // Lock toggle — captures the part's current local TRS and snaps
            // to the chosen pill's pose; persists across task selection
            // within the current step. Tooltip explains the cross-step
            // reset so the author isn't surprised when locks vanish.
            string lockGlyph = taskIsLocked ? "🔒" : "🔓";
            string lockTip;
            if (isMulti)
                lockTip = taskIsLocked
                    ? $"Unlock all {multiCount} selected task poses."
                    : $"Lock all {multiCount} selected task parts at the chosen pose.";
            else
                lockTip = taskIsLocked
                    ? "Unlock this task's pose. The part returns to its inherited pose."
                    : "Lock this task's part at the selected pose. Stays locked while you " +
                      "navigate other tasks; auto-clears when you switch steps.";
            if (GUILayout.Button(new GUIContent(lockGlyph, lockTip),
                    EditorStyles.miniButton, GUILayout.Width(28), GUILayout.Height(18)))
            {
                if (isMulti)
                {
                    if (taskIsLocked) UnlockAllSelectedToolTasks(step);
                    else              LockAllSelectedToolTasks(step);
                }
                else
                {
                    if (taskIsLocked) UnlockTask(entry.id);
                    else              LockTask(step, action, entry);
                }
                SceneView.RepaintAll();
            }

            // Reset → Start: rewrites endTransform to the inherited start
            // pose (in PreviewRoot frame). Use when a stale end pose drifted
            // from where the part actually starts the step (e.g. group root
            // moved since the end pose was authored).
            string resetTip = isMulti
                ? $"Reset endTransform on all {multiCount} selected tasks to each part's inherited start pose. " +
                  "Then drag the gizmo to author a new end pose."
                : "Reset endTransform to this part's inherited start pose. " +
                  "Then drag the gizmo to author a new end pose.";
            if (GUILayout.Button(new GUIContent("⟲", resetTip),
                    EditorStyles.miniButton, GUILayout.Width(28), GUILayout.Height(18)))
            {
                if (isMulti) ResetAllSelectedEndsToStart(step);
                else         ResetEndToStart(step, action, entry);
                SceneView.RepaintAll();
            }

            GUILayout.FlexibleSpace();
            string hint;
            if (isMulti && multiMixed)
                hint = $"{multiCount} selected — mixed ({multiStartCount} Start, {multiEndCount} End)";
            else if (isMulti)
                hint = taskIsLocked
                    ? $"🔒 {multiCount} selected — all on {(isEnd ? "End" : "Start")}"
                    : $"🔗 {multiCount} selected — all on {(isEnd ? "End" : "Start")}";
            else if (taskIsLocked)
                hint = "🔒 locked — pose persists across task switches";
            else if (isEnd)
                hint = "(drag the gizmo to author endTransform)";
            else if (!isMulti && entry != null && entry.startTransform != null)
                hint = "● override (drag the gizmo to edit start pose)";
            else
                hint = "(start inherited — read-only; click Author override to edit)";
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            // Inheritance banner: when on Start AND no override is set,
            // surface the upstream step whose endTransform is inherited as
            // this task's start pose. When an override IS set, the banner
            // is suppressed — the inherited source isn't the active value
            // anymore, so jumping to it would mislead the author.
            if (!isMulti && _activePosePill == InteractionPosePill.Start
                && (entry == null || entry.startTransform == null))
            {
                if (TryFindInheritedStartSource(step, action, out string srcStepId, out string srcEntryId, out int srcSeq))
                    DrawInheritedStartBanner(srcStepId, srcEntryId, srcSeq);
            }
        }

        // ── Inherited-start banner ───────────────────────────────────────────

        private void DrawInheritedStartBanner(string srcStepId, string srcEntryId, int srcSeq)
        {
            var srcStep = FindStep(srcStepId);
            string srcStepName = srcStep != null && !string.IsNullOrEmpty(srcStep.name)
                ? srcStep.name : srcStepId;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var lbl = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            EditorGUILayout.LabelField(
                $"↪ Start inherited from step {srcSeq} · {srcStepName}",
                lbl);
            if (GUILayout.Button(
                    new GUIContent("→ Edit source",
                        "Jump to the upstream tool task whose endTransform is this task's start pose. " +
                        "Edit there to update the chain — overriding start here would break the single-source-of-truth invariant."),
                    EditorStyles.miniButton, GUILayout.Width(96), GUILayout.Height(18)))
            {
                JumpToTaskInStep(srcStepId, srcEntryId);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// For the active tool task, walks earlier steps in descending seq
        /// order to find the latest tool task whose endTransform writes this
        /// part. Returns the source step + task entry id so the banner can
        /// label the source and the jump button can navigate there.
        /// </summary>
        internal bool IsActivePosePillStart() => _activePosePill == InteractionPosePill.Start;

        /// <summary>
        /// When a tool×part pose pill is active and the supplied target backs
        /// the active task (or any multi-selected sibling), returns the
        /// PreviewRoot-local position the part has been snapped to (Start =
        /// chain inherited; End = entry.endTransform). Used by
        /// <c>UpdateToolPreview</c> to keep the tool's tip glued to the
        /// snapped part instead of the static target marker.
        /// </summary>
        internal bool TryGetActivePosePillPositionForTarget(string targetId, out Vector3 pillPos)
        {
            pillPos = Vector3.zero;
            if (string.IsNullOrEmpty(targetId)) return false;
            if (string.IsNullOrEmpty(_activePosePillStepId)) return false;
            if (_activeTaskKind != "toolAction") return false;

            var step = FindStep(_activePosePillStepId);
            if (step?.requiredToolActions == null || step.taskOrder == null) return false;

            ToolActionDefinition action = null;
            TaskOrderEntry entry = null;
            foreach (var a in step.requiredToolActions)
            {
                if (a == null || a.targetId != targetId) continue;
                foreach (var e in step.taskOrder)
                {
                    if (e != null && e.kind == "toolAction" && e.id == a.id)
                    { action = a; entry = e; break; }
                }
                if (entry != null) break;
            }
            if (entry == null) return false;

            if (_activePosePill == InteractionPosePill.End)
            {
                if (entry.endTransform == null) return false;
                var ep = entry.endTransform.position;
                pillPos = new Vector3(ep.x, ep.y, ep.z);
                return true;
            }

            // Start: per-task override if authored, else chain-derived
            // inherited pose, else the part's staging placement (first
            // instance — no upstream tool task has touched this part yet).
            // Mirrors DrawInheritedStartReadOnly's fallback chain so dot,
            // gizmo, and tool preview all resolve to the same pose for
            // first-instance tool tasks instead of falling back to the
            // end-aligned target marker.
            var src = TryGetEffectiveStartTransform(step, action, entry, out _);
            if (src != null)
            {
                pillPos = new Vector3(src.position.x, src.position.y, src.position.z);
                return true;
            }
            string fallbackPartId = ResolvePartIdForTarget(action?.targetId);
            // Live-part fallback. When the part was placed by a prior Place
            // step (no upstream tool task to inherit from), the bolt is
            // already sitting at its post-Place pose in the scene — that IS
            // the inherited start. Reading the live GO matches what the user
            // actually sees and what the runtime tool-target spawner anchors
            // to. Falling straight through to startPosition (staging) was
            // pulling the dot back to the bench, producing the offset.
            if (!string.IsNullOrEmpty(fallbackPartId))
            {
                var partGo = FindLivePartGO(fallbackPartId);
                Transform pr = GetPreviewRoot();
                if (partGo != null && pr != null)
                {
                    pillPos = pr.InverseTransformPoint(partGo.transform.position);
                    return true;
                }
            }
            var pp = !string.IsNullOrEmpty(fallbackPartId) ? FindPartPlacement(fallbackPartId) : null;
            if (pp != null)
            {
                pillPos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Renders read-only Position / Rotation / Scale fields for a tool
        /// task's inherited start pose. Pulled from the chain (previous
        /// step's authored endTransform on this part). Editing is intentional-
        /// ly disabled — the "→ Edit source" button on the inheritance banner
        /// is the only authoring affordance, keeping endTransform on the
        /// upstream task as the single source of truth.
        /// </summary>
        internal void DrawInheritedStartReadOnly(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            // Override path: editable fields write to entry.startTransform.
            // Author has opted in by dragging the gizmo or clicking
            // "Author override" below.
            if (entry != null && entry.startTransform != null)
            {
                var st = entry.startTransform;
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = EditorGUILayout.Vector3Field("Position",
                    new Vector3(st.position.x, st.position.y, st.position.z));
                Quaternion currRot = st.rotation.IsIdentity
                    ? Quaternion.identity
                    : new Quaternion(st.rotation.x, st.rotation.y, st.rotation.z, st.rotation.w);
                Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation (Euler)", currRot.eulerAngles);
                Vector3 currScale = (st.scale.x == 0f && st.scale.y == 0f && st.scale.z == 0f)
                    ? Vector3.one
                    : new Vector3(st.scale.x, st.scale.y, st.scale.z);
                Vector3 newScale = EditorGUILayout.Vector3Field("Scale", currScale);
                if (EditorGUI.EndChangeCheck())
                {
                    st.position = new SceneFloat3 { x = newPos.x, y = newPos.y, z = newPos.z };
                    Quaternion newRot = Quaternion.Euler(newEuler);
                    st.rotation = new SceneQuaternion
                        { x = newRot.x, y = newRot.y, z = newRot.z, w = newRot.w };
                    st.scale = new SceneFloat3 { x = newScale.x, y = newScale.y, z = newScale.z };
                    _dirtyStepIds.Add(step.id);
                    if (entry != null) _dirtyTaskEntryIds.Add(entry.id);
                    SyncLivePartToStartOverride(action, st);
                    SceneView.RepaintAll();
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    "● Override — start authored on this task.",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        new GUIContent("⟲ Revert to inherited",
                            "Clear this task's start-pose override and fall back to the upstream task's endTransform (or the part's staging pose if no chain exists)."),
                        EditorStyles.miniButton, GUILayout.Width(140), GUILayout.Height(18)))
                {
                    if (RevertActiveStartOverride())
                    {
                        SceneView.RepaintAll();
                        Repaint();
                    }
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            // First-instance check — when no upstream tool task on the same
            // part exists, this task IS the canonical authoring point for
            // the part's start pose. Surface editable fields that auto-
            // promote `entry.startTransform` on first change, mirroring the
            // gizmo-drag auto-promote in IsActiveStartPillFollowedOverride.
            // Without this branch the inspector shows disabled fields and
            // a misleading "inherited from upstream" hint even though there
            // IS no upstream — leaving the author with no obvious way to
            // set the pose other than dragging the gizmo.
            bool hasUpstream = TryFindInheritedStartSource(step, action, out _, out _, out _);
            if (!hasUpstream)
            {
                DrawFirstInstanceStartFields(step, action, entry);
                return;
            }

            // Inherited (read-only) path: show upstream end / staging pose.
            TaskEndTransform src = TryGetInheritedStartTransform(step, action);
            using (new EditorGUI.DisabledScope(true))
            {
                if (src != null)
                {
                    EditorGUILayout.Vector3Field("Position",
                        new Vector3(src.position.x, src.position.y, src.position.z));
                    Quaternion rot = src.rotation.IsIdentity
                        ? Quaternion.identity
                        : new Quaternion(src.rotation.x, src.rotation.y, src.rotation.z, src.rotation.w);
                    EditorGUILayout.Vector3Field("Rotation (Euler)", rot.eulerAngles);
                    EditorGUILayout.Vector3Field("Scale",
                        new Vector3(src.scale.x, src.scale.y, src.scale.z));
                }
                else
                {
                    // No upstream tool task wrote this part. Prefer the live
                    // part GO's pose — it's already at the post-Place pose
                    // (the bolt is on the carriage, not at staging) for the
                    // currently-viewed step. Falling straight to startPosition
                    // would show the bench pose, which is wrong for any tool
                    // task whose part was placed by an earlier Place step.
                    string partId = ResolvePartIdForTarget(action?.targetId);
                    GameObject livePartGo = !string.IsNullOrEmpty(partId)
                        ? FindLivePartGO(partId) : null;
                    Transform pr = GetPreviewRoot();
                    if (livePartGo != null && pr != null)
                    {
                        Vector3 livePos = pr.InverseTransformPoint(livePartGo.transform.position);
                        Quaternion liveRot = Quaternion.Inverse(pr.rotation) * livePartGo.transform.rotation;
                        EditorGUILayout.Vector3Field("Position", livePos);
                        EditorGUILayout.Vector3Field("Rotation (Euler)", liveRot.eulerAngles);
                        EditorGUILayout.Vector3Field("Scale", livePartGo.transform.localScale);
                    }
                    else
                    {
                        var pp = !string.IsNullOrEmpty(partId) ? FindPartPlacement(partId) : null;
                        if (pp != null)
                        {
                            EditorGUILayout.Vector3Field("Position",
                                new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z));
                            Quaternion sr = pp.startRotation.IsIdentity
                                ? Quaternion.identity
                                : new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w);
                            EditorGUILayout.Vector3Field("Rotation (Euler)", sr.eulerAngles);
                            EditorGUILayout.Vector3Field("Scale",
                                new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z));
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "No upstream tool-task endTransform and no part placement — start pose unresolved.",
                                MessageType.Warning);
                        }
                    }
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Read-only — inherited from upstream. Click Author override to edit.",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            // Author override button: only meaningful for followPart tool×part
            // tasks where the override has runtime effect.
            bool canOverride = entry != null
                && action?.interaction != null
                && action.interaction.followPart
                && (action.interaction.archetype == "lerp"
                    || action.interaction.archetype == "thread_in");
            using (new EditorGUI.DisabledScope(!canOverride))
            {
                if (GUILayout.Button(
                        new GUIContent("Author override",
                            "Capture the current inherited start pose into this task's startTransform so it can be edited independently. Reverts cleanly via the ⟲ button."),
                        EditorStyles.miniButton, GUILayout.Width(110), GUILayout.Height(18)))
                {
                    EnsureStartOverride(step, action, entry);
                    _dirtyStepIds.Add(step.id);
                    if (entry != null) _dirtyTaskEntryIds.Add(entry.id);
                    SyncLivePartToStartOverride(action, entry.startTransform);
                    SceneView.RepaintAll();
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Editable Position/Rotation/Scale fields for a tool task
        /// with no upstream source (first instance). On first edit the
        /// fields auto-promote <c>entry.startTransform</c> from the live
        /// part pose, then apply the change. Mirror behavior of the gizmo
        /// drag in IsActiveStartPillFollowedOverride for first-instance.</summary>
        private void DrawFirstInstanceStartFields(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            // Seed the displayed values from either an existing override (if
            // somehow non-null already) or the live part's PR-local pose.
            Vector3    showPos   = Vector3.zero;
            Quaternion showRot   = Quaternion.identity;
            Vector3    showScale = Vector3.one;
            if (entry?.startTransform != null)
            {
                showPos = new Vector3(
                    entry.startTransform.position.x,
                    entry.startTransform.position.y,
                    entry.startTransform.position.z);
                showRot = entry.startTransform.rotation.IsIdentity
                    ? Quaternion.identity
                    : new Quaternion(
                        entry.startTransform.rotation.x,
                        entry.startTransform.rotation.y,
                        entry.startTransform.rotation.z,
                        entry.startTransform.rotation.w);
                if (!(entry.startTransform.scale.x == 0f && entry.startTransform.scale.y == 0f && entry.startTransform.scale.z == 0f))
                    showScale = new Vector3(
                        entry.startTransform.scale.x,
                        entry.startTransform.scale.y,
                        entry.startTransform.scale.z);
            }
            else
            {
                string partId = ResolvePartIdForTarget(action?.targetId);
                var partGO = !string.IsNullOrEmpty(partId) ? FindLivePartGO(partId) : null;
                Transform pr = GetPreviewRoot();
                if (partGO != null && pr != null)
                {
                    showPos = pr.InverseTransformPoint(partGO.transform.position);
                    showRot = Quaternion.Inverse(pr.rotation) * partGO.transform.rotation;
                    showScale = partGO.transform.localScale;
                }
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newPos   = EditorGUILayout.Vector3Field("Position", showPos);
            Vector3 newEuler = EditorGUILayout.Vector3Field("Rotation (Euler)", showRot.eulerAngles);
            Vector3 newScale = EditorGUILayout.Vector3Field("Scale", showScale);
            if (EditorGUI.EndChangeCheck())
            {
                EnsureStartOverride(step, action, entry);
                Quaternion newRot = Quaternion.Euler(newEuler);
                entry.startTransform.position = new SceneFloat3 { x = newPos.x, y = newPos.y, z = newPos.z };
                entry.startTransform.rotation = new SceneQuaternion
                    { x = newRot.x, y = newRot.y, z = newRot.z, w = newRot.w };
                entry.startTransform.scale = new SceneFloat3 { x = newScale.x, y = newScale.y, z = newScale.z };
                _dirtyStepIds.Add(step.id);
                if (entry != null) _dirtyTaskEntryIds.Add(entry.id);
                SyncLivePartToStartOverride(action, entry.startTransform);
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            var hint = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 1.00f, 0.20f, 0.95f) },
            };
            EditorGUILayout.LabelField(
                entry?.startTransform != null
                    ? "● First-instance task — start pose authored here."
                    : "First-instance task — drag the gizmo or edit fields to author start pose.",
                hint);
            GUILayout.FlexibleSpace();
            if (entry?.startTransform != null
                && GUILayout.Button(
                    new GUIContent("⟲ Reset",
                        "Clear the authored start pose. Defaults back to the live part pose at task entry."),
                    EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(18)))
            {
                if (RevertActiveStartOverride())
                {
                    SceneView.RepaintAll();
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Snaps the live part GO to the override pose so author
        /// sees the authored value reflected in scene immediately. No-op if
        /// the part isn't spawned.</summary>
        private void SyncLivePartToStartOverride(ToolActionDefinition action, TaskEndTransform st)
        {
            if (action == null || st == null) return;
            string partId = ResolvePartIdForTarget(action.targetId);
            if (string.IsNullOrEmpty(partId)) return;
            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;
            partGo.transform.localPosition = new Vector3(st.position.x, st.position.y, st.position.z);
            partGo.transform.localRotation = st.rotation.IsIdentity
                ? Quaternion.identity
                : new Quaternion(st.rotation.x, st.rotation.y, st.rotation.z, st.rotation.w);
            if (!(st.scale.x == 0f && st.scale.y == 0f && st.scale.z == 0f))
                partGo.transform.localScale = new Vector3(st.scale.x, st.scale.y, st.scale.z);
        }

        /// <summary>
        /// Returns the upstream tool task's endTransform that is inherited
        /// as this task's start pose, or <c>null</c> if no chain exists.
        /// </summary>
        private TaskEndTransform TryGetInheritedStartTransform(
            StepDefinition step, ToolActionDefinition action)
        {
            if (!TryFindInheritedStartSource(step, action, out string srcStepId, out string srcEntryId, out _))
                return null;
            var srcStep = FindStep(srcStepId);
            if (srcStep?.taskOrder == null) return null;
            foreach (var e in srcStep.taskOrder)
            {
                if (e != null && e.id == srcEntryId && e.endTransform != null) return e.endTransform;
            }
            return null;
        }

        /// <summary>
        /// Single source of truth for "what is the start pose of this task".
        /// Prefers the per-task <c>entry.startTransform</c> override when
        /// non-null; otherwise falls back to chain-inherited upstream end.
        /// Every reader of "start pose for task N" must go through here so
        /// override-vs-inherited stays one decision point.
        /// </summary>
        internal TaskEndTransform TryGetEffectiveStartTransform(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry,
            out bool isOverride)
        {
            isOverride = false;
            if (entry != null && entry.startTransform != null)
            {
                isOverride = true;
                return entry.startTransform;
            }
            return TryGetInheritedStartTransform(step, action);
        }

        private bool TryFindInheritedStartSource(
            StepDefinition step, ToolActionDefinition action,
            out string srcStepId, out string srcEntryId, out int srcSeq)
        {
            srcStepId = null; srcEntryId = null; srcSeq = -1;
            if (step == null || action == null || _pkg?.steps == null) return false;
            string partId = ResolvePartIdForTarget(action.targetId);
            if (string.IsNullOrEmpty(partId)) return false;

            int currentSeq = step.sequenceIndex;
            StepDefinition best = null;
            string bestEntryId = null;
            int bestSeq = int.MinValue;

            foreach (var s in _pkg.steps)
            {
                if (s == null || s.sequenceIndex >= currentSeq || s.sequenceIndex <= bestSeq) continue;
                if (s.taskOrder == null || s.requiredToolActions == null) continue;

                // action id → targetId for this candidate step
                foreach (var a in s.requiredToolActions)
                {
                    if (a == null || string.IsNullOrEmpty(a.targetId)) continue;
                    string aPartId = ResolvePartIdForTarget(a.targetId);
                    if (!string.Equals(aPartId, partId, System.StringComparison.Ordinal)) continue;

                    foreach (var e in s.taskOrder)
                    {
                        if (e == null || e.endTransform == null) continue;
                        if (!string.Equals(e.kind, "toolAction", System.StringComparison.Ordinal)) continue;
                        if (e.id != a.id) continue;
                        best = s; bestEntryId = e.id; bestSeq = s.sequenceIndex;
                        break;
                    }
                }
            }

            if (best == null) return false;
            srcStepId = best.id; srcEntryId = bestEntryId; srcSeq = bestSeq;
            return true;
        }

        private void JumpToTaskInStep(string stepId, string entryId)
        {
            if (string.IsNullOrEmpty(stepId) || _stepIds == null || _stepSequenceIdxs == null) return;
            int targetIdx = -1;
            for (int i = 1; i < _stepIds.Length; i++)
            {
                if (string.Equals(_stepIds[i], stepId, System.StringComparison.Ordinal)) { targetIdx = i; break; }
            }
            if (targetIdx < 0) return;

            ApplyStepFilter(targetIdx);

            var step = FindStep(stepId);
            var order = step != null ? GetOrDeriveTaskOrder(step) : null;
            if (order == null) return;
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                if (e != null && e.id == entryId)
                {
                    _selectedTaskSeqIdx = i;
                    ApplyTaskEntrySelection(step, e);
                    Repaint();
                    SceneView.RepaintAll();
                    return;
                }
            }
        }

        // ── Multi-select lock helpers ────────────────────────────────────────

        private int CountSelectedFollowedToolTasks(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return 0;
            int n = 0;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (TryResolveFollowedInteractionForTargetIdx(step, idx, out _, out _)) n++;
            }
            return n;
        }

        private bool AreAllSelectedToolTasksLocked(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return false;
            int seen = 0;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (!TryResolveFollowedInteractionForTargetIdx(step, idx, out _, out var entry)) continue;
                if (!IsTaskLocked(entry.id)) return false;
                seen++;
            }
            return seen > 0;
        }

        private void LockAllSelectedToolTasks(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (!TryResolveFollowedInteractionForTargetIdx(step, idx, out var action, out var entry)) continue;
                if (IsTaskLocked(entry.id)) continue;
                LockTask(step, action, entry);
            }
        }

        private void UnlockAllSelectedToolTasks(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (!TryResolveFollowedInteractionForTargetIdx(step, idx, out _, out var entry)) continue;
                UnlockTask(entry.id);
            }
        }

        /// <summary>
        /// Used by the targets-batch inspector to decide whether to render the
        /// pose pill row above the standard batch panel. Returns the first
        /// multi-selected target that backs a followPart tool action so the
        /// pill row has an action+entry to anchor on; the pill's internal
        /// multi-select branch then iterates the rest of <c>_multiSelected</c>.
        /// </summary>
        internal bool TryFindAnyFollowedToolTaskInMultiSelection(
            StepDefinition step, out ToolActionDefinition action, out TaskOrderEntry entry)
        {
            action = null; entry = null;
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return false;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (TryResolveFollowedInteractionForTargetIdx(step, idx, out action, out entry)) return true;
            }
            return false;
        }

        // ── Reset End → Start ────────────────────────────────────────────────

        /// <summary>
        /// Rewrites <c>entry.endTransform</c> to match the part's inherited
        /// start pose. The cache holds part-parent-local TRS; we promote that
        /// to worldspace via the part's parent, then convert worldspace into
        /// PreviewRoot-local (the frame endTransform lives in). After this
        /// the End pill snaps to the same place as Start = no movement, and
        /// the author can re-author from a clean baseline.
        /// </summary>
        private void ResetEndToStart(StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (step == null || entry == null) return;
            string partId = ResolvePartIdForTarget(action?.targetId);
            if (string.IsNullOrEmpty(partId)) return;
            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;

            // Cache miss can happen if the user clicks Reset before ever
            // visiting the task's pill row. Lazily seed from the live part —
            // assumes the part is currently AT its inherited start (true
            // unless the user already dragged the gizmo without locking).
            if (!_inheritedStartCache.TryGetValue(entry.id, out var s))
            {
                s = new InheritedPose
                {
                    Pos   = partGo.transform.localPosition,
                    Rot   = partGo.transform.localRotation,
                    Scale = partGo.transform.localScale,
                };
                _inheritedStartCache[entry.id] = s;
            }

            var parent = partGo.transform.parent;
            Vector3 worldPos = parent != null ? parent.TransformPoint(s.Pos) : s.Pos;
            Quaternion worldRot = parent != null ? parent.rotation * s.Rot : s.Rot;

            Transform pr = GetPreviewRoot();
            Vector3 prPos     = pr != null ? pr.InverseTransformPoint(worldPos) : worldPos;
            Quaternion prRot  = pr != null ? Quaternion.Inverse(pr.rotation) * worldRot : worldRot;

            entry.endTransform ??= new TaskEndTransform();
            entry.endTransform.position = new SceneFloat3 { x = prPos.x, y = prPos.y, z = prPos.z };
            entry.endTransform.rotation = new SceneQuaternion { x = prRot.x, y = prRot.y, z = prRot.z, w = prRot.w };
            entry.endTransform.scale    = new SceneFloat3 { x = s.Scale.x, y = s.Scale.y, z = s.Scale.z };
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);
        }

        private void ResetAllSelectedEndsToStart(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (TryResolveFollowedInteractionForTargetIdx(step, idx, out var a, out var e))
                    ResetEndToStart(step, a, e);
            }
        }

        private void RetargetSelectedLocksToActivePill(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return;
            bool any = false;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (!TryResolveFollowedInteractionForTargetIdx(step, idx, out _, out var entry)) continue;
                for (int li = 0; li < _locks.Count; li++)
                {
                    var l = _locks[li];
                    if (l != null && l.TaskEntryId == entry.id)
                    {
                        l.Pill = _activePosePill;
                        any = true;
                    }
                }
            }
            if (any) ApplyAllLockedPoses();
        }

        // ── followPart authoring sync ────────────────────────────────────────

        /// <summary>
        /// Called from <c>TTAW.SceneView</c> right after the target's
        /// PositionHandle drag has updated <c>_targets[_selectedIdx].position</c>.
        /// When the active task is a tool×part interaction (lerp / thread_in)
        /// AND <c>interaction.followPart</c> is true, applies the same
        /// <paramref name="deltaLocal"/> to <c>entry.endTransform.position</c>
        /// so the part's end pose stays glued to the target. Marks the step
        /// dirty for auto-save. Assumes the target's local frame and the
        /// part-end-transform local frame are the same (PreviewRoot in the
        /// typical case); a part nested under a group root with non-zero
        /// rotation would see a slightly mis-rotated delta — out of scope.
        /// </summary>
        internal bool TryApplyTargetPositionDeltaToFollowedPart(Vector3 deltaLocal)
        {
            // Multi-select takes precedence over the active-task branch. The
            // active task in multi-select is whichever entry the inspector
            // anchored the pill on (often NOT _selectedIdx), so an "active +
            // skip _selectedIdx" split would leave the primary's part frozen.
            // Iterate every selected index uniformly instead.
            if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction")
            {
                StepDefinition activeStep = FindStep(_activePosePillStepId);
                if (activeStep?.requiredToolActions == null || activeStep.taskOrder == null || _targets == null)
                    return false;
                bool any = false;
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    if (TryResolveFollowedInteractionForTargetIdx(activeStep, idx,
                            out ToolActionDefinition a, out TaskOrderEntry e))
                    {
                        ApplyPositionDeltaToTaskAndPart(activeStep, a, e, deltaLocal);
                        any = true;
                    }
                }
                return any;
            }

            if (TryResolveActiveFollowedInteraction(
                out StepDefinition step, out ToolActionDefinition action,
                out TaskOrderEntry entry))
            {
                ApplyPositionDeltaToTaskAndPart(step, action, entry, deltaLocal);
                return true;
            }
            return false;
        }

        private void ApplyPositionDeltaToTaskAndPart(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry, Vector3 deltaLocal)
        {
            var et = entry.endTransform;
            et.position = new SceneFloat3
            {
                x = et.position.x + deltaLocal.x,
                y = et.position.y + deltaLocal.y,
                z = et.position.z + deltaLocal.z,
            };
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);

            string partId = ResolvePartIdForTarget(action?.targetId);
            if (!string.IsNullOrEmpty(partId))
            {
                var partGo = FindLivePartGO(partId);
                if (partGo != null)
                    partGo.transform.localPosition += deltaLocal;
            }
        }

        /// <summary>
        /// Same idea for rotation: applies <paramref name="localDelta"/>
        /// (a left-multiply rotation in the target's local frame) onto
        /// <c>entry.endTransform.rotation</c> when the active task is a
        /// tool×part interaction with followPart=true.
        /// </summary>
        internal bool TryApplyTargetRotationDeltaToFollowedPart(Quaternion localDelta)
        {
            if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction")
            {
                StepDefinition activeStep = FindStep(_activePosePillStepId);
                if (activeStep?.requiredToolActions == null || activeStep.taskOrder == null || _targets == null)
                    return false;
                bool any = false;
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    if (TryResolveFollowedInteractionForTargetIdx(activeStep, idx,
                            out ToolActionDefinition a, out TaskOrderEntry e))
                    {
                        ApplyRotationDeltaToTaskAndPart(activeStep, a, e, localDelta);
                        any = true;
                    }
                }
                return any;
            }

            if (TryResolveActiveFollowedInteraction(
                out StepDefinition step, out ToolActionDefinition action,
                out TaskOrderEntry entry))
            {
                ApplyRotationDeltaToTaskAndPart(step, action, entry, localDelta);
                return true;
            }
            return false;
        }

        private void ApplyRotationDeltaToTaskAndPart(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry, Quaternion localDelta)
        {
            var et = entry.endTransform;
            Quaternion currRot = et.rotation.IsIdentity
                ? Quaternion.identity
                : new Quaternion(et.rotation.x, et.rotation.y, et.rotation.z, et.rotation.w);
            Quaternion newRot = localDelta * currRot;
            et.rotation = new SceneQuaternion
                { x = newRot.x, y = newRot.y, z = newRot.z, w = newRot.w };
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);

            string partId = ResolvePartIdForTarget(action?.targetId);
            if (!string.IsNullOrEmpty(partId))
            {
                var partGo = FindLivePartGO(partId);
                if (partGo != null)
                    partGo.transform.localRotation = localDelta * partGo.transform.localRotation;
            }
        }

        // ── Start-pill override authoring (opt-in per-task start pose) ───────
        //
        // When the user has the Start pose pill active on a tool×part
        // interaction (followPart=true, archetype=lerp/thread_in), dragging
        // the gizmo writes to entry.startTransform — the per-task override —
        // instead of corrupting endTransform. Auto-promotes startTransform
        // from the inherited pose on the first drag so the dot/gizmo math
        // self-corrects (displayLocal now tracks the same field we mutate).

        /// <summary>True when pill=Start AND the active task is a followed
        /// tool×part interaction AND the per-task override has already been
        /// authored (entry.startTransform != null). Drag handlers gate on
        /// this so Start-pill is read-only by default — the user opts in
        /// via the inspector's "Author override" button, then drag becomes
        /// active and writes to the existing override field.</summary>
        internal bool IsActiveStartPillFollowedOverride()
        {
            if (_activePosePill != InteractionPosePill.Start) return false;
            if (!IsActiveTaskFollowedInteraction()) return false;
            var step = FindStep(_activePosePillStepId);
            if (step?.taskOrder == null) return false;

            TaskOrderEntry entry = null;
            foreach (var e in step.taskOrder)
            {
                if (e != null && e.kind == "toolAction" && e.id == _activePosePillEntryRef)
                { entry = e; break; }
            }
            if (entry == null) return false;
            if (entry.startTransform != null) return true; // override authored → drag edits it

            // Auto-promote on drag for first-instance tool tasks (no upstream
            // chain to protect). The pose-chain invariant only applies when
            // there IS an inherited source — otherwise the author has no
            // affordance to set "where the action begins" for the first
            // touch of the part. EnsureStartOverride inside the delta apply
            // creates the override on first drag, mirroring what the
            // "Author override" button does explicitly.
            ToolActionDefinition action = null;
            foreach (var a in step.requiredToolActions)
                if (a != null && a.id == entry.id) { action = a; break; }
            if (action != null
                && !TryFindInheritedStartSource(step, action, out _, out _, out _))
                return true; // first-instance → drag auto-promotes
            return false;     // upstream exists → require explicit "Author override"
        }

        /// <summary>True when pill=Start on a followed tool×part interaction
        /// with NO override authored yet AND an upstream chain source exists.
        /// SceneView uses this to suppress the drag gizmo so Start-pill stays
        /// read-only — the only opt-in path is the inspector's "Author override"
        /// button. First-instance tool tasks (no upstream tool task on the
        /// same part) have nothing to inherit from, so their Start pose is
        /// always editable — otherwise the user would have no way to author
        /// where the action begins for the first time the part is touched.
        /// </summary>
        internal bool IsActiveStartPillReadOnly()
        {
            if (_activePosePill != InteractionPosePill.Start) return false;
            if (!IsActiveTaskFollowedInteraction()) return false;
            var step = FindStep(_activePosePillStepId);
            if (step?.taskOrder == null) return true;

            TaskOrderEntry entry = null;
            foreach (var e in step.taskOrder)
            {
                if (e != null && e.kind == "toolAction" && e.id == _activePosePillEntryRef)
                { entry = e; break; }
            }
            if (entry == null) return true;
            if (entry.startTransform != null) return false; // override already authored → editable

            // First-instance check: no upstream tool task means no inherited
            // chain to protect. Author should be free to set the start pose.
            ToolActionDefinition action = null;
            foreach (var a in step.requiredToolActions)
                if (a != null && a.id == entry.id) { action = a; break; }
            if (action == null) return true;
            if (!TryFindInheritedStartSource(step, action, out _, out _, out _))
                return false; // first-instance → editable
            return true;     // upstream exists → read-only until override authored
        }

        /// <summary>
        /// Position-handle drag entry point for the Start-pill override.
        /// Mirrors <see cref="TryApplyTargetPositionDeltaToFollowedPart"/> but
        /// writes <c>entry.startTransform</c> instead of <c>endTransform</c>,
        /// and does NOT mutate the target's <c>sel.position</c> — the target
        /// placement is unrelated to the part's start pose.
        /// </summary>
        internal bool TryApplyStartOverridePositionDelta(Vector3 deltaLocal)
        {
            if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction")
            {
                StepDefinition activeStep = FindStep(_activePosePillStepId);
                if (activeStep?.requiredToolActions == null || activeStep.taskOrder == null || _targets == null)
                    return false;
                bool any = false;
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    if (TryResolveFollowedInteractionForTargetIdx(activeStep, idx,
                            out ToolActionDefinition a, out TaskOrderEntry e))
                    {
                        ApplyPositionDeltaToStartOverrideAndPart(activeStep, a, e, deltaLocal);
                        any = true;
                    }
                }
                return any;
            }

            if (TryResolveActiveFollowedInteraction(
                out StepDefinition step, out ToolActionDefinition action,
                out TaskOrderEntry entry))
            {
                ApplyPositionDeltaToStartOverrideAndPart(step, action, entry, deltaLocal);
                return true;
            }
            return false;
        }

        /// <summary>Rotation-handle counterpart of
        /// <see cref="TryApplyStartOverridePositionDelta"/>.</summary>
        internal bool TryApplyStartOverrideRotationDelta(Quaternion localDelta)
        {
            if (_multiSelected.Count > 1 && _activeTaskKind == "toolAction")
            {
                StepDefinition activeStep = FindStep(_activePosePillStepId);
                if (activeStep?.requiredToolActions == null || activeStep.taskOrder == null || _targets == null)
                    return false;
                bool any = false;
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    if (TryResolveFollowedInteractionForTargetIdx(activeStep, idx,
                            out ToolActionDefinition a, out TaskOrderEntry e))
                    {
                        ApplyRotationDeltaToStartOverrideAndPart(activeStep, a, e, localDelta);
                        any = true;
                    }
                }
                return any;
            }

            if (TryResolveActiveFollowedInteraction(
                out StepDefinition step, out ToolActionDefinition action,
                out TaskOrderEntry entry))
            {
                ApplyRotationDeltaToStartOverrideAndPart(step, action, entry, localDelta);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Auto-promotes <c>entry.startTransform</c> from inherited if null,
        /// otherwise returns the existing override. Captures position +
        /// rotation + scale so the override is a complete pose from frame 1.
        /// </summary>
        private TaskEndTransform EnsureStartOverride(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (entry.startTransform != null) return entry.startTransform;

            // Capture the current effective start (= inherited) so the first
            // drag-frame's delta is applied on top of where the part already
            // visually was — no jump.
            var src = TryGetEffectiveStartTransform(step, action, entry, out _);
            var t = new TaskEndTransform();
            if (src != null)
            {
                t.position = new SceneFloat3 { x = src.position.x, y = src.position.y, z = src.position.z };
                t.rotation = src.rotation.IsIdentity
                    ? new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f }
                    : new SceneQuaternion { x = src.rotation.x, y = src.rotation.y, z = src.rotation.z, w = src.rotation.w };
                t.scale = (src.scale.x == 0f && src.scale.y == 0f && src.scale.z == 0f)
                    ? new SceneFloat3 { x = 1f, y = 1f, z = 1f }
                    : new SceneFloat3 { x = src.scale.x, y = src.scale.y, z = src.scale.z };
            }
            else
            {
                // No upstream — fall back to the part's current live transform
                // so the override starts from where the bolt is right now.
                string partId = ResolvePartIdForTarget(action?.targetId);
                var partGo = !string.IsNullOrEmpty(partId) ? FindLivePartGO(partId) : null;
                if (partGo != null)
                {
                    var lp = partGo.transform.localPosition;
                    var lr = partGo.transform.localRotation;
                    var ls = partGo.transform.localScale;
                    t.position = new SceneFloat3 { x = lp.x, y = lp.y, z = lp.z };
                    t.rotation = new SceneQuaternion { x = lr.x, y = lr.y, z = lr.z, w = lr.w };
                    t.scale    = new SceneFloat3 { x = ls.x, y = ls.y, z = ls.z };
                }
                else
                {
                    t.rotation = new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f };
                    t.scale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f };
                }
            }
            entry.startTransform = t;
            return t;
        }

        private void ApplyPositionDeltaToStartOverrideAndPart(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry, Vector3 deltaLocal)
        {
            var st = EnsureStartOverride(step, action, entry);
            st.position = new SceneFloat3
            {
                x = st.position.x + deltaLocal.x,
                y = st.position.y + deltaLocal.y,
                z = st.position.z + deltaLocal.z,
            };
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);

            string partId = ResolvePartIdForTarget(action?.targetId);
            if (!string.IsNullOrEmpty(partId))
            {
                var partGo = FindLivePartGO(partId);
                if (partGo != null)
                    partGo.transform.localPosition += deltaLocal;
            }
        }

        private void ApplyRotationDeltaToStartOverrideAndPart(
            StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry, Quaternion localDelta)
        {
            var st = EnsureStartOverride(step, action, entry);
            Quaternion currRot = st.rotation.IsIdentity
                ? Quaternion.identity
                : new Quaternion(st.rotation.x, st.rotation.y, st.rotation.z, st.rotation.w);
            Quaternion newRot = localDelta * currRot;
            st.rotation = new SceneQuaternion
                { x = newRot.x, y = newRot.y, z = newRot.z, w = newRot.w };
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);

            string partId = ResolvePartIdForTarget(action?.targetId);
            if (!string.IsNullOrEmpty(partId))
            {
                var partGo = FindLivePartGO(partId);
                if (partGo != null)
                    partGo.transform.localRotation = localDelta * partGo.transform.localRotation;
            }
        }

        /// <summary>
        /// Clears <c>entry.startTransform</c> on the active task (revert to
        /// inherited). Snaps the live part GO back to inherited so the
        /// author sees the revert. Marks the step dirty for save. Called
        /// from the inspector's "⟲ Revert to inherited" button.
        /// </summary>
        internal bool RevertActiveStartOverride()
        {
            if (string.IsNullOrEmpty(_activePosePillStepId)
                || string.IsNullOrEmpty(_activePosePillEntryRef)) return false;

            var step = FindStep(_activePosePillStepId);
            if (step?.taskOrder == null || step.requiredToolActions == null) return false;

            TaskOrderEntry entry = null;
            foreach (var e in step.taskOrder)
            {
                if (e != null && e.kind == "toolAction" && e.id == _activePosePillEntryRef)
                { entry = e; break; }
            }
            if (entry == null || entry.startTransform == null) return false;

            ToolActionDefinition action = null;
            foreach (var a in step.requiredToolActions)
            {
                if (a != null && a.id == entry.id) { action = a; break; }
            }

            entry.startTransform = null;
            _dirtyStepIds.Add(step.id);
            _dirtyTaskEntryIds.Add(entry.id);

            // Snap the live part back to inherited so the revert is visible.
            if (action != null)
            {
                var inherited = TryGetInheritedStartTransform(step, action);
                string partId = ResolvePartIdForTarget(action.targetId);
                var partGo = !string.IsNullOrEmpty(partId) ? FindLivePartGO(partId) : null;
                if (partGo != null && inherited != null)
                {
                    partGo.transform.localPosition = new Vector3(
                        inherited.position.x, inherited.position.y, inherited.position.z);
                    partGo.transform.localRotation = inherited.rotation.IsIdentity
                        ? Quaternion.identity
                        : new Quaternion(inherited.rotation.x, inherited.rotation.y,
                                         inherited.rotation.z, inherited.rotation.w);
                    if (!(inherited.scale.x == 0f && inherited.scale.y == 0f && inherited.scale.z == 0f))
                    {
                        partGo.transform.localScale = new Vector3(
                            inherited.scale.x, inherited.scale.y, inherited.scale.z);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Sweep: clears <c>startTransform</c> on every <c>TaskOrderEntry</c>
        /// in the loaded package and marks each affected step dirty. Used to
        /// undo accidental auto-promotions from earlier drag-without-gate
        /// behavior, and as a "back to all-inherited defaults" reset.
        /// Returns the number of entries cleared.
        /// </summary>
        internal int RevertAllStartOverridesInPackage()
        {
            if (_pkg?.steps == null) return 0;
            int cleared = 0;
            foreach (var s in _pkg.steps)
            {
                if (s?.taskOrder == null) continue;
                bool stepTouched = false;
                foreach (var e in s.taskOrder)
                {
                    if (e == null || e.startTransform == null) continue;
                    e.startTransform = null;
                    cleared++;
                    stepTouched = true;
                }
                if (stepTouched) _dirtyStepIds.Add(s.id);
            }
            if (cleared > 0)
            {
                // Re-apply inherited start to the active part so the scene
                // reflects the revert immediately on the currently-selected
                // task. Other steps will sync on next navigation.
                if (!string.IsNullOrEmpty(_activePosePillStepId)
                    && !string.IsNullOrEmpty(_activePosePillEntryRef))
                {
                    var step = FindStep(_activePosePillStepId);
                    if (step?.requiredToolActions != null && step.taskOrder != null)
                    {
                        TaskOrderEntry entry = null;
                        foreach (var e in step.taskOrder)
                        {
                            if (e != null && e.kind == "toolAction" && e.id == _activePosePillEntryRef)
                            { entry = e; break; }
                        }
                        ToolActionDefinition action = null;
                        if (entry != null)
                        {
                            foreach (var a in step.requiredToolActions)
                            {
                                if (a != null && a.id == entry.id) { action = a; break; }
                            }
                        }
                        if (action != null) SnapPartToPill(action, entry);
                    }
                }
                Repaint();
                SceneView.RepaintAll();
            }
            return cleared;
        }

        [UnityEditor.MenuItem("OSE/Tools/Revert All Start-Pose Overrides (Active TTAW)", priority = 700)]
        private static void Menu_RevertAllStartOverrides()
        {
            var win = UnityEditor.EditorWindow.GetWindow<ToolTargetAuthoringWindow>(
                utility: false, title: null, focus: false);
            if (win == null)
            {
                UnityEditor.EditorUtility.DisplayDialog(
                    "Revert Start Overrides",
                    "Open the Tool Target Authoring Window first — the sweep operates on the loaded package.",
                    "OK");
                return;
            }
            int n = win.RevertAllStartOverridesInPackage();
            UnityEditor.EditorUtility.DisplayDialog(
                "Revert Start Overrides",
                n == 0
                    ? "No overrides to revert — every tool task already inherits its start pose."
                    : $"Cleared {n} start-pose override(s). Steps marked dirty — save TTAW to persist.",
                "OK");
        }

        /// <summary>
        /// Resolves a multi-selected target index back to its tool action +
        /// task order entry, but only when the action has a followPart
        /// interaction (lerp/thread_in). Used by the multi-select branch of
        /// the followPart sync helpers above.
        /// </summary>
        private bool TryResolveFollowedInteractionForTargetIdx(
            StepDefinition step, int targetIdx,
            out ToolActionDefinition action, out TaskOrderEntry entry)
        {
            action = null; entry = null;
            string targetId = _targets[targetIdx].def?.id;
            if (string.IsNullOrEmpty(targetId)) return false;

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                var a = step.requiredToolActions[i];
                if (a == null || a.targetId != targetId) continue;
                if (a.interaction == null || !a.interaction.followPart) continue;
                if (a.interaction.archetype != "lerp" && a.interaction.archetype != "thread_in") continue;
                action = a;
                break;
            }
            if (action == null) return false;

            for (int i = 0; i < step.taskOrder.Length; i++)
            {
                var e = step.taskOrder[i];
                if (e == null || e.kind != "toolAction" || e.id != action.id) continue;
                if (e.endTransform == null) continue;
                entry = e;
                break;
            }
            return entry != null;
        }

        /// <summary>True iff the currently-selected task is a tool×part
        /// interaction (lerp/thread_in) with followPart=true. Used by the
        /// SceneView to decorate the target gizmo so the author can see
        /// at a glance that drags will move the part too.</summary>
        internal bool IsActiveTaskFollowedInteraction()
        {
            return TryResolveActiveFollowedInteraction(out _, out _, out _);
        }

        /// <summary>
        /// Common gate: resolves the active task's step + action + entry,
        /// and returns true only when archetype is lerp/thread_in AND
        /// interaction.followPart is true.
        /// </summary>
        private bool TryResolveActiveFollowedInteraction(
            out StepDefinition step, out ToolActionDefinition action, out TaskOrderEntry entry)
        {
            step = null; action = null; entry = null;
            if (string.IsNullOrEmpty(_activePosePillStepId)
                || string.IsNullOrEmpty(_activePosePillEntryRef)) return false;
            if (_activeTaskKind != "toolAction") return false;

            step = FindStep(_activePosePillStepId);
            if (step?.requiredToolActions == null || step.taskOrder == null) return false;

            for (int i = 0; i < step.taskOrder.Length; i++)
            {
                var e = step.taskOrder[i];
                if (e != null && string.Equals(e.kind, "toolAction") && e.id == _activePosePillEntryRef)
                { entry = e; break; }
            }
            if (entry?.endTransform == null) return false;

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                var a = step.requiredToolActions[i];
                if (a != null && a.id == entry.id) { action = a; break; }
            }
            if (action?.interaction == null) return false;
            if (!action.interaction.followPart) return false;
            if (action.interaction.archetype != "lerp"
                && action.interaction.archetype != "thread_in") return false;
            return true;
        }

        // ── Lock helpers ─────────────────────────────────────────────────────

        private bool IsTaskLocked(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return false;
            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i] != null && _locks[i].TaskEntryId == entryId) return true;
            return false;
        }

        private void LockTask(StepDefinition step, ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (step == null || entry == null) return;
            string partId = ResolvePartIdForTarget(action?.targetId);
            if (string.IsNullOrEmpty(partId)) return;

            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;

            // Source-of-truth for "Start" is the cached inherited pose (captured
            // on the task's first pill render, before any followPart drag).
            // Falls back to the live transform if the cache is empty (rare —
            // would happen if Lock fires before pill-row capture).
            InheritedPose start;
            if (!_inheritedStartCache.TryGetValue(entry.id, out start))
            {
                start = new InheritedPose
                {
                    Pos   = partGo.transform.localPosition,
                    Rot   = partGo.transform.localRotation,
                    Scale = partGo.transform.localScale,
                };
                _inheritedStartCache[entry.id] = start;
            }

            var lockEntry = new LockedPose
            {
                TaskEntryId        = entry.id,
                PartId             = partId,
                PartGO             = partGo,
                OriginalLocalPos   = start.Pos,
                OriginalLocalRot   = start.Rot,
                OriginalLocalScale = start.Scale,
                Pill               = _activePosePill,
            };
            _locks.Add(lockEntry);
            _locksStepId = step.id;

            ApplyLockToPart(lockEntry, entry);
        }

        // ── Pill-click snap (no lock required) ───────────────────────────────

        private void CaptureInheritedStartIfMissing(ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) return;
            if (_inheritedStartCache.ContainsKey(entry.id)) return;
            string partId = ResolvePartIdForTarget(action?.targetId);
            if (string.IsNullOrEmpty(partId)) return;
            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;
            _inheritedStartCache[entry.id] = new InheritedPose
            {
                Pos   = partGo.transform.localPosition,
                Rot   = partGo.transform.localRotation,
                Scale = partGo.transform.localScale,
            };
        }

        private void SnapPartToPill(ToolActionDefinition action, TaskOrderEntry entry)
        {
            if (entry == null) return;
            string partId = ResolvePartIdForTarget(action?.targetId);
            if (string.IsNullOrEmpty(partId)) return;
            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;

            // SyncAllPartMeshesToActivePose can SetActive(false) on parts whose
            // step-aware pose doesn't resolve. Force-on so the End-pose snap is
            // actually visible.
            if (!partGo.activeSelf) partGo.SetActive(true);

            if (_activePosePill == InteractionPosePill.Start)
            {
                // Override wins over inherited-start cache so toggling to
                // Start visualizes the authored override pose.
                if (entry.startTransform != null)
                {
                    ApplyEndTransformToPartViaWorld(partGo.transform, entry.startTransform);
                }
                else if (_inheritedStartCache.TryGetValue(entry.id, out var s))
                {
                    partGo.transform.localPosition = s.Pos;
                    partGo.transform.localRotation = s.Rot;
                    partGo.transform.localScale    = s.Scale;
                }
            }
            else if (entry.endTransform != null)
            {
                ApplyEndTransformToPartViaWorld(partGo.transform, entry.endTransform);
            }
        }

        // endTransform is authored in PreviewRoot-local space. For parts whose
        // immediate parent IS PreviewRoot, the local frames coincide and a
        // direct localPosition write is fine. For parts under a Group_xxx
        // root, the frames diverge — the absolute write would teleport the
        // part to wherever PreviewRoot-local coords land in the group's local
        // frame (often far off-screen, looks like "the part disappeared").
        // Convert via worldspace so the parent frame is handled implicitly.
        private static void ApplyEndTransformToPartViaWorld(
            Transform partXform, TaskEndTransform et)
        {
            Transform previewRoot = GetPreviewRoot();
            Vector3 etPos = new Vector3(et.position.x, et.position.y, et.position.z);
            Quaternion etRot = et.rotation.IsIdentity
                ? Quaternion.identity
                : new Quaternion(et.rotation.x, et.rotation.y, et.rotation.z, et.rotation.w);

            if (previewRoot != null)
            {
                partXform.position = previewRoot.TransformPoint(etPos);
                if (!et.rotation.IsIdentity)
                    partXform.rotation = previewRoot.rotation * etRot;
            }
            else
            {
                partXform.localPosition = etPos;
                if (!et.rotation.IsIdentity)
                    partXform.localRotation = etRot;
            }

            Vector3 scl = new Vector3(et.scale.x, et.scale.y, et.scale.z);
            if (scl.sqrMagnitude > 0.000001f) partXform.localScale = scl;
        }

        private void SnapAllSelectedPartsToPill(StepDefinition step)
        {
            if (step?.requiredToolActions == null || step.taskOrder == null || _targets == null) return;
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                if (TryResolveFollowedInteractionForTargetIdx(step, idx, out var a, out var e))
                    SnapPartToPill(a, e);
            }
        }

        private void UnlockTask(string entryId)
        {
            for (int i = _locks.Count - 1; i >= 0; i--)
            {
                var l = _locks[i];
                if (l == null || l.TaskEntryId != entryId) continue;
                if (l.PartGO != null)
                {
                    l.PartGO.transform.localPosition = l.OriginalLocalPos;
                    l.PartGO.transform.localRotation = l.OriginalLocalRot;
                    l.PartGO.transform.localScale    = l.OriginalLocalScale;
                }
                _locks.RemoveAt(i);
            }
        }

        /// <summary>
        /// Cross-step boundary detection — if <paramref name="currentStepId"/>
        /// differs from the step the existing locks belong to, restore every
        /// locked part to its captured originals and clear the list. Locks
        /// are intentionally per-step.
        /// </summary>
        private void EnsureLocksFreshForStep(string currentStepId)
        {
            if (_locks.Count == 0)
            {
                if (!string.Equals(_locksStepId, currentStepId, System.StringComparison.Ordinal))
                {
                    _inheritedStartCache.Clear();
                    _pillByEntry.Clear();
                }
                _locksStepId = currentStepId;
                return;
            }
            if (string.Equals(_locksStepId, currentStepId, System.StringComparison.Ordinal))
                return;

            for (int i = 0; i < _locks.Count; i++)
            {
                var l = _locks[i];
                if (l == null || l.PartGO == null) continue;
                l.PartGO.transform.localPosition = l.OriginalLocalPos;
                l.PartGO.transform.localRotation = l.OriginalLocalRot;
                l.PartGO.transform.localScale    = l.OriginalLocalScale;
            }
            _locks.Clear();
            _inheritedStartCache.Clear();
            _pillByEntry.Clear();
            _locksStepId = currentStepId;
        }

        /// <summary>
        /// Re-applies every active lock — idempotent. Covers code paths
        /// elsewhere in TTAW that re-position part GOs (e.g. pose-pill
        /// snaps in other panels, SyncAllPartMeshesToActivePose during
        /// mesh-sync). Without this the locked part would drift back.
        /// </summary>
        private void ApplyAllLockedPoses()
        {
            if (_locks.Count == 0) return;

            // We need each lock's task's endTransform to know where End is.
            // Resolve via the active step (locks are per-step so the same
            // step owns all of them).
            var step = FindStep(_locksStepId);
            if (step?.taskOrder == null) return;

            for (int i = 0; i < _locks.Count; i++)
            {
                var l = _locks[i];
                if (l == null || l.PartGO == null) continue;

                TaskOrderEntry entry = null;
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var e = step.taskOrder[ti];
                    if (e != null && e.id == l.TaskEntryId) { entry = e; break; }
                }
                ApplyLockToPart(l, entry);
            }
        }

        private static void ApplyLockToPart(LockedPose l, TaskOrderEntry entry)
        {
            if (l == null || l.PartGO == null) return;
            switch (l.Pill)
            {
                case InteractionPosePill.Start:
                    l.PartGO.transform.localPosition = l.OriginalLocalPos;
                    l.PartGO.transform.localRotation = l.OriginalLocalRot;
                    l.PartGO.transform.localScale    = l.OriginalLocalScale;
                    break;
                case InteractionPosePill.End:
                    if (entry?.endTransform == null) return;
                    ApplyEndTransformToPartViaWorld(l.PartGO.transform, entry.endTransform);
                    break;
            }
        }

        /// <summary>
        /// Called from <c>ApplyTaskEntrySelection</c> when the active task
        /// changes. Clears the recorded active-task identity so the ghost
        /// for the prior task stops rendering. The live mesh is never
        /// modified by this path, so there's nothing to "restore."
        /// </summary>
        internal void RestoreLiveMeshToInheritedPose()
        {
            _activePosePillStepId   = null;
            _activePosePillEntryRef = null;
            // _activePosePill is intentionally NOT reset here. The next
            // DrawInteractionPosePillRow restores it from _pillByEntry for
            // whichever task the user just selected — preserves per-task
            // pose memory across selection changes.
        }

        // ── Per-entry pose pill memory ──────────────────────────────────────
        //
        // The pill (Start | End) is window-global (_activePosePill) but the
        // author thinks of it per task. Without per-entry memory, clicking
        // task A in End mode → task B → back to A would silently reset to
        // Start while the live part still shows End — a save trap. These
        // helpers persist the last-viewed pill per entry within the active
        // step. Lifecycle parity with _locks / _inheritedStartCache: cleared
        // on step boundary by EnsureLocksFreshForStep and on package load by
        // Cleanup.

        /// <summary>Stamps the entry's last-viewed pill state.</summary>
        private void RecordPillForEntry(string entryId, InteractionPosePill p)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            _pillByEntry[entryId] = p;
        }

        /// <summary>Returns the remembered pill for the given entry id, or
        /// Start when none has been recorded. Read by row badge + scene ring
        /// so they stay coherent with the inspector pill. Private because
        /// InteractionPosePill is a private nested enum — exposing this any
        /// wider triggers CS0050 (inconsistent accessibility).</summary>
        private InteractionPosePill GetPillForEntry(string entryId)
        {
            if (string.IsNullOrEmpty(entryId)) return InteractionPosePill.Start;
            return _pillByEntry.TryGetValue(entryId, out var p) ? p : InteractionPosePill.Start;
        }

        /// <summary>Debug-only inspection: logs the current state of the
        /// per-entry pill map. Call from suspicious code paths to confirm
        /// the entry is or isn't in the map.</summary>
        private void DebugDumpPillMap(string callerTag, string entryId)
        {
            string hitInfo = _pillByEntry.TryGetValue(entryId, out var p)
                ? $"{p}"
                : "MISS";
            UnityEngine.Debug.Log(
                $"[ttaw-pill:{callerTag}] entry={entryId} → {hitInfo}, map size={_pillByEntry.Count}, " +
                $"keys=[{string.Join(",", _pillByEntry.Keys)}]");
        }

        /// <summary>Stamps every multi-selected toolAction entry with the
        /// chosen pill. Used when a Start/End click in multi-select mode
        /// promotes the whole selection to a single pose. Stamps EVERY
        /// toolAction row in the row-list selection — not just followed
        /// interactions — so per-task pose memory survives re-selection
        /// for tasks whose tool is conceptual (hand-tighten, observe) or
        /// whose archetype isn't lerp/thread_in.</summary>
        private void RecordPillForSelection(StepDefinition step, InteractionPosePill p)
        {
            if (step?.taskOrder == null || step.requiredToolActions == null) return;
            // Resolve target ids for every multi-selected target index, then
            // walk taskOrder and stamp the pill on every toolAction whose
            // backing action targets one of those ids. Cheaper than nested
            // resolution and tolerates conceptual-tool tasks.
            var stampTargetIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            if (_targets != null)
            {
                foreach (int idx in _multiSelected)
                {
                    if (idx < 0 || idx >= _targets.Length) continue;
                    string tid = _targets[idx].def?.id;
                    if (!string.IsNullOrEmpty(tid)) stampTargetIds.Add(tid);
                }
            }
            if (stampTargetIds.Count == 0) return;
            foreach (var e in step.taskOrder)
            {
                if (e == null || e.kind != "toolAction" || string.IsNullOrEmpty(e.id)) continue;
                foreach (var a in step.requiredToolActions)
                {
                    if (a == null || a.id != e.id || string.IsNullOrEmpty(a.targetId)) continue;
                    if (stampTargetIds.Contains(a.targetId))
                        _pillByEntry[e.id] = p;
                    break;
                }
            }
        }

        /// <summary>Returns true when any tool-action targeting
        /// <paramref name="targetId"/> on the locked step is currently in
        /// End pill mode. Used by the SceneView accent ring so its semantics
        /// stay aligned with the inspector row's E badge. Cheap when no
        /// per-entry pose state exists (early-out on empty map). Private —
        /// the partial class members reading this are all in-class.</summary>
        private bool TargetIsInEndPill(string targetId)
        {
            if (string.IsNullOrEmpty(targetId) || _pillByEntry.Count == 0) return false;
            var step = FindStep(_locksStepId);
            if (step?.requiredToolActions == null) return false;
            foreach (var a in step.requiredToolActions)
            {
                if (a == null || !string.Equals(a.targetId, targetId, System.StringComparison.Ordinal)) continue;
                if (GetPillForEntry(a.id) == InteractionPosePill.End) return true;
            }
            return false;
        }

        /// <summary>Surveys multi-selected tool tasks and reports whether
        /// they share a pose pill (mixed=false) or disagree (mixed=true).
        /// Counts per-pose feed the "1 Start, 3 End" hint. Walks every
        /// toolAction row backing a multi-selected target — broader than
        /// the followed-interaction filter, so conceptual-tool tasks
        /// (hand-tighten, observe) participate in shared/mixed accounting.</summary>
        private void ComputeMultiSelectPillState(
            StepDefinition step, out bool mixed, out int startCount, out int endCount)
        {
            mixed = false; startCount = 0; endCount = 0;
            if (step?.taskOrder == null || step.requiredToolActions == null || _targets == null) return;
            var stampTargetIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (int idx in _multiSelected)
            {
                if (idx < 0 || idx >= _targets.Length) continue;
                string tid = _targets[idx].def?.id;
                if (!string.IsNullOrEmpty(tid)) stampTargetIds.Add(tid);
            }
            if (stampTargetIds.Count == 0) return;
            foreach (var e in step.taskOrder)
            {
                if (e == null || e.kind != "toolAction" || string.IsNullOrEmpty(e.id)) continue;
                foreach (var a in step.requiredToolActions)
                {
                    if (a == null || a.id != e.id || string.IsNullOrEmpty(a.targetId)) continue;
                    if (!stampTargetIds.Contains(a.targetId)) break;
                    var p = GetPillForEntry(e.id);
                    if (p == InteractionPosePill.End) endCount++;
                    else                              startCount++;
                    break;
                }
            }
            mixed = startCount > 0 && endCount > 0;
        }

        // ── SceneView ghost + gizmo (called from OnSceneGUI inside !isPlaying) ─

        /// <summary>
        /// Draws the wireframe ghost of the part (and, for tool×part, the tool)
        /// at the active task's authored end pose, plus a PositionHandle +
        /// RotationHandle when the End pill is selected. The handles edit
        /// <c>entry.endTransform</c> directly. The live part / live tool meshes
        /// are not moved — both stay at their inherited start so the author
        /// sees start AND end simultaneously.
        /// </summary>
        internal void DrawEndPoseHandleForActiveInteractionTask()
        {
            // No-op kept for the OnSceneGUI call site. Tool×part interactions
            // no longer render an end-pose ghost — the live tool sits at its
            // working pose and the live part follows the target gizmo when
            // followPart is on (or stays at the placed pose otherwise).
            // Identity tracking via _activePosePillStepId / EntryRef still
            // happens in DrawInteractionPosePillRow so the followPart sync
            // and lock helpers can resolve the active task on drag.
            //
            // If the user has navigated to a non-tool-task, clear the cached
            // identity so locks and the followPart sync stop firing for the
            // stale task.
            if (!string.IsNullOrEmpty(_activePosePillEntryRef)
                && _activeTaskKind != "toolAction")
            {
                RestoreLiveMeshToInheritedPose();
            }
        }

    }
}
