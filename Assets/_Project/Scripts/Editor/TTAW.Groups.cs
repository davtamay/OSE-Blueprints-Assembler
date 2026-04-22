// TTAW.Groups.cs — Group (subassembly) pose authoring: BuildGroupList,
//                   SyncGroupRootToActivePose, group pose helpers.
// ──────────────────────────────────────────────────────────────────────────────
// Phase G1-G3 of the group pose authoring plan. Groups mirror the same pose
// system as individual parts (start / assembled / custom step poses) but
// operate on the subassembly root GO — all member parts follow via Unity
// parenting.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Content.Loading;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── BuildGroupList ────────────────────────────────────────────────────

        private void BuildGroupList()
        {
            if (_pkg == null)
            {
                _groups = Array.Empty<GroupEditState>();
                return;
            }

            var allSubs = _pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0)
            {
                _groups = Array.Empty<GroupEditState>();
                return;
            }

            var list = new List<GroupEditState>();
            foreach (var sub in allSubs)
            {
                if (sub == null || sub.isAggregate) continue;

                var sp = FindSubassemblyPlacement(sub.id);
                bool hasP = sp != null;

                // Prefer the new start/assembled fields; fall back to legacy position
                Vector3 initPos = hasP && (sp.startPosition.x != 0f || sp.startPosition.y != 0f || sp.startPosition.z != 0f)
                    ? PackageJsonUtils.ToVector3(sp.startPosition)
                    : hasP ? PackageJsonUtils.ToVector3(sp.position) : Vector3.zero;
                Quaternion initRot = hasP && (sp.startRotation.x != 0f || sp.startRotation.y != 0f || sp.startRotation.z != 0f || sp.startRotation.w != 0f)
                    ? PackageJsonUtils.ToUnityQuaternion(sp.startRotation)
                    : hasP ? PackageJsonUtils.ToUnityQuaternion(sp.rotation) : Quaternion.identity;
                Vector3 initScl = hasP && (sp.startScale.x != 0f || sp.startScale.y != 0f || sp.startScale.z != 0f)
                    ? PackageJsonUtils.ToVector3(sp.startScale)
                    : hasP ? PackageJsonUtils.ToVector3(sp.scale) : Vector3.one;

                Vector3    asmPos = hasP && (sp.assembledPosition.x != 0f || sp.assembledPosition.y != 0f || sp.assembledPosition.z != 0f)
                    ? PackageJsonUtils.ToVector3(sp.assembledPosition) : initPos;
                Quaternion asmRot = hasP && (sp.assembledRotation.x != 0f || sp.assembledRotation.y != 0f || sp.assembledRotation.z != 0f || sp.assembledRotation.w != 0f)
                    ? PackageJsonUtils.ToUnityQuaternion(sp.assembledRotation) : initRot;
                Vector3    asmScl = hasP && (sp.assembledScale.x != 0f || sp.assembledScale.y != 0f || sp.assembledScale.z != 0f)
                    ? PackageJsonUtils.ToVector3(sp.assembledScale) : initScl;

                // If no authored placement, derive start pose from member parts'
                // assembled positions — the group's "start" is where its parts
                // sit after being individually assembled in earlier steps.
                if (!hasP && sub.partIds != null && sub.partIds.Length > 0 && _pkg.previewConfig?.partPlacements != null)
                {
                    Vector3 centroid = Vector3.zero;
                    int found = 0;
                    foreach (var pid in sub.partIds)
                    {
                        foreach (var pp2 in _pkg.previewConfig.partPlacements)
                        {
                            if (pp2 != null && string.Equals(pp2.partId, pid, StringComparison.Ordinal))
                            {
                                var aPos = pp2.assembledPosition;
                                if (aPos.x != 0f || aPos.y != 0f || aPos.z != 0f)
                                {
                                    centroid += new Vector3(aPos.x, aPos.y, aPos.z);
                                    found++;
                                }
                                break;
                            }
                        }
                    }
                    if (found > 0)
                    {
                        // Don't move the start pose to the centroid — leave at origin.
                        // The parts are already positioned at their assembled locations.
                        // The group root at (0,0,0) means the parts stay where they are.
                        // The "assembled pose" for the GROUP is where it goes AFTER
                        // being placed into the next-level assembly (e.g., the cube).
                        // That's what the author needs to set via the gizmo.
                    }
                }

                var state = new GroupEditState
                {
                    def              = sub,
                    placement        = sp,
                    hasPlacement     = hasP,
                    startPosition    = initPos,
                    startRotation    = initRot,
                    startScale       = initScl.sqrMagnitude < 0.00001f ? Vector3.one : initScl,
                    assembledPosition = asmPos,
                    assembledRotation = asmRot,
                    assembledScale    = asmScl.sqrMagnitude < 0.00001f ? Vector3.one : asmScl,
                    isDirty          = false,
                };
                list.Add(state);
            }

            _groups = list.ToArray();
            _selectedGroupIdx = -1;
        }

        private SubassemblyPreviewPlacement FindSubassemblyPlacement(string subId)
        {
            if (_pkg?.previewConfig?.subassemblyPlacements == null) return null;
            foreach (var sp in _pkg.previewConfig.subassemblyPlacements)
                if (sp != null && string.Equals(sp.subassemblyId, subId, StringComparison.Ordinal))
                    return sp;
            return null;
        }

        /// <summary>
        /// Group-scope variant of <c>IsCueFromPoseSameAsStart</c>. Delegates to
        /// the shape-agnostic comparator in TTAW.Layout.cs so part and group
        /// paths share the same tolerance semantics. Returns false when no
        /// placement exists (can't compare — render the pill).
        /// </summary>
        private static bool IsGroupCueFromPoseSameAsStart(OSE.Content.AnimationCueEntry cue,
            SubassemblyPreviewPlacement placement)
        {
            if (placement == null) return false;
            return IsCueFromPoseSameAsStartPose(cue,
                placement.startPosition, placement.startRotation, placement.startScale);
        }

        // ── SyncGroupRootToActivePose ─────────────────────────────────────────

        /// <summary>
        /// Sets each group root GO's transform from its GroupEditState based on
        /// the current pose mode. Called after SyncAllPartMeshesToActivePose so
        /// member parts (which are children of the root) follow via Unity parenting.
        /// </summary>
        private void SyncAllGroupRootsToActivePose()
        {
            var previewRoot = GetPreviewRoot();
            SyncAggregateRootsToActivePose(previewRoot);
            if (_groups == null) return;

            // One-shot diagnostic at method entry — changes key when state
            // changes. Shows whether the method is even invoked, which
            // group is "selected", and what mode it sees. If the user
            // clicks After cue and this line doesn't print with mode=-200,
            // either the event isn't arriving here or the key hasn't
            // changed since a prior invocation.
            string entryKey = $"mode={_editingGroupPoseMode}|sel={_selectedGroupIdx}|groups={_groups.Length}";
            if (entryKey != _lastGroupSyncEntryKey)
            {
                _lastGroupSyncEntryKey = entryKey;
                OSE.Core.OseLog.Info($"[TTAW.GroupSync.Entry] {entryKey}");
            }

            for (int i = 0; i < _groups.Length; i++)
            {
                ref GroupEditState g = ref _groups[i];
                if (g.def == null) continue;
                if (!_subassemblyRootGOs.TryGetValue(g.def.id, out var rootGO) || rootGO == null)
                {
                    // Log once if the selected group's root GO is missing —
                    // that would silently skip every pose-preview branch.
                    if (_selectedGroupIdx == i)
                    {
                        string mkey = $"rootmiss|{g.def.id}|mode={_editingGroupPoseMode}";
                        if (mkey != _lastGroupSyncEntryKey)
                        {
                            _lastGroupSyncEntryKey = mkey;
                            OSE.Core.OseLog.Warn($"[TTAW.GroupSync.Entry] root GO missing for selected group '{g.def.id}' — dict has {_subassemblyRootGOs.Count} entries. Every pose preview will silently skip.");
                        }
                    }
                    continue;
                }

                // Start Pose: root stays at (0,0,0) — member parts are already
                // at their individual assembled positions from earlier steps.
                // That IS the start pose for the group.
                //
                // Assembled Pose: root moves to the group's assembledPosition —
                // all member parts shift together (via Unity parenting) to where
                // the group goes in the next-level assembly (e.g., the cube).
                //
                // Only apply non-origin poses when dirty or hasPlacement AND
                // the pose mode is not Start.
                // Only the SELECTED group responds to the pose mode toggle.
                // All other groups always stay at origin so their member parts
                // display at their individual PreviewRoot-space positions.
                bool isThisGroupSelected = _selectedGroupIdx == i;
                // Only write pose overrides on the step that's actively
                // placing this group. Past-placed groups must not be driven
                // by the selected group's pose toggle — otherwise their
                // members revert to fabrication or jump to cube targets on
                // subsequent steps regardless of what already happened.
                StepDefinition curStep = _sceneBuildStepActive && _stepIds != null
                                          && _stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length
                    ? FindStep(_stepIds[_stepFilterIdx]) : null;
                bool currentStepPlacesThisGroup = curStep != null && g.def != null
                    && string.Equals(curStep.requiredSubassemblyId, g.def.id, StringComparison.Ordinal);
                bool applyPoseOverride = isThisGroupSelected && currentStepPlacesThisGroup;

                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                Vector3 scl = Vector3.one;

                // Synthesized/custom pose preview — _editingGroupPoseMode >= 0
                // indexes into g.placement.stepPoses. Applies the stepPose
                // position/rotation to the root in PreviewRoot-local space.
                // Member parts follow via Unity parenting (not individual
                // rigid-body offsets) — the stepPose IS the group's pose at
                // that step, so treating members as a rigid body anchored to
                // the root is the closest visual match.
                //
                // Deliberately requires only isThisGroupSelected (not the
                // full applyPoseOverride which also gates on
                // currentStepPlacesThisGroup): the author explicitly clicked
                // an After-cue or Before-cue pill on a NO-TASK row to
                // preview what the group looks like at that step, and the
                // group doesn't need to be "placed" this step for the
                // preview to be meaningful. Non-selected groups still stay
                // at origin (unchanged).
                if (isThisGroupSelected
                    && _editingGroupPoseMode >= 0
                    && g.placement?.stepPoses != null
                    && _editingGroupPoseMode < g.placement.stepPoses.Length)
                {
                    var sp = g.placement.stepPoses[_editingGroupPoseMode];
                    var spos = new Vector3(sp.position.x, sp.position.y, sp.position.z);
                    var srot = new Quaternion(sp.rotation.x, sp.rotation.y, sp.rotation.z, sp.rotation.w);
                    if (srot.x == 0 && srot.y == 0 && srot.z == 0 && srot.w == 0) srot = Quaternion.identity;

                    // Move root WITH children so the author sees the whole
                    // group visually snap to the cue's toPose. Plain
                    // transform.position / .localRotation writes propagate
                    // to children via Unity parenting — that's exactly the
                    // visual the synth stepPose represents (group at the
                    // end of a holdAtEnd cue). Preserve-children is wrong
                    // here: the stepPose anchors the GROUP pose, not the
                    // children's world locations.
                    if (previewRoot != null)
                    {
                        rootGO.transform.position = previewRoot.TransformPoint(spos);
                        rootGO.transform.rotation = previewRoot.rotation * srot;
                    }
                    else
                    {
                        rootGO.transform.localPosition = spos;
                        rootGO.transform.localRotation = srot;
                    }
                    rootGO.transform.localScale = Vector3.one;
                    continue;
                }

                // Before-cue preview — delegate to poseTable at the
                // CURRENT step's seq. "Before cue" means the state just
                // before the cue on this step fires, which is the state
                // carried in from the previous step (= current step's
                // pose-table entry before any current-step cues apply).
                // poseTable at current seq returns exactly that.
                if (isThisGroupSelected
                    && IsGroupBeforeCueMode()
                    && curStep != null
                    && _pkg?.poseTable != null)
                {
                    ApplyGroupMembersFromPoseTable(g, rootGO.transform, previewRoot, curStep.sequenceIndex);
                    continue;
                }

                // After-cue preview — delegate to the SAME pose-resolution
                // pipeline that renders step N+1 at runtime. PoseTable has
                // already baked the synth + composition. Iterate this
                // group's members, look up each member's pose at
                // nextStep.seq, apply it directly. Root stays wherever
                // Start Pose logic put it; members move via their own
                // local transforms. Visual: identical to step N+1 render.
                if (isThisGroupSelected
                    && IsGroupAfterCueMode()
                    && curStep != null
                    && TryGetNextStepSeqForAfterCue(curStep, out int nextSeq)
                    && _pkg?.poseTable != null)
                {
                    string diagKey = $"{g.def?.id}|afterCueStep={nextSeq}";
                    if (diagKey != _lastAfterCueGroupSyncKey)
                    {
                        _lastAfterCueGroupSyncKey = diagKey;
                        OSE.Core.OseLog.Info($"[TTAW.GroupAfterSync] sub='{g.def?.id}' mode={_editingGroupPoseMode} delegating to poseTable at nextSeq={nextSeq}");
                    }

                    ApplyGroupMembersFromPoseTable(g, rootGO.transform, previewRoot, nextSeq);
                    continue;
                }
                // Log when we expect to be in after-cue mode but fail to resolve.
                if (isThisGroupSelected && _editingGroupPoseMode <= PoseModeAfterCueBase && _editingGroupPoseMode > PoseModeAfterCueBase - 100)
                {
                    string diagKey = $"{g.def?.id}|miss|mode={_editingGroupPoseMode}|synth={_afterCueGroupSynthIdx}|afterGroupDef={(_afterCueGroupDef != null ? _afterCueGroupDef.id : "null")}";
                    if (diagKey != _lastAfterCueGroupSyncKey)
                    {
                        _lastAfterCueGroupSyncKey = diagKey;
                        OSE.Core.OseLog.Warn($"[TTAW.GroupAfterSync] MISS — in after-cue mode but TryResolveAfterCuePoseForGroup returned false. sub='{g.def?.id}' synthIdx={_afterCueGroupSynthIdx} mode={_editingGroupPoseMode} afterCueGroupDef={(_afterCueGroupDef != null ? _afterCueGroupDef.id : "null")} placementStepPoses={(g.placement?.stepPoses?.Length ?? -1)}");
                    }
                }

                if (applyPoseOverride && _editingGroupPoseMode == PoseModeAssembled)
                {
                    // Phase 1 rigid-body: pull from the baked (subId, targetId)
                    // rigid body so the group pose mirrors a single part's pose.
                    // Order matters: set the root FIRST (plain, not preserving
                    // children — we're about to overwrite their locals), then
                    // write each member's fixed offset in local space.
                    if (TryGetSelectedGroupRigidBody(g, out var rb))
                    {
                        if (previewRoot != null)
                        {
                            rootGO.transform.position = previewRoot.TransformPoint(rb.groupCenter);
                            rootGO.transform.rotation = previewRoot.rotation * rb.groupRotation;
                        }
                        else
                        {
                            rootGO.transform.localPosition = rb.groupCenter;
                            rootGO.transform.localRotation = rb.groupRotation;
                        }
                        rootGO.transform.localScale = Vector3.one;
                        ApplyRigidBodyOffsetsToMembers(rootGO.transform, rb);
                        continue;   // skip the preserve-children fallthrough
                    }
                    if (g.isDirty || g.hasPlacement)
                    {
                        pos = g.assembledPosition;
                        rot = g.assembledRotation;
                        scl = g.assembledScale;
                    }
                }
                // else: Start Pose mode OR non-selected group → origin

                if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;

                // Start Pose centering: ONLY for the currently selected group,
                // so the gizmo sits inside the frame. Non-selected groups must
                // stay at origin — the spawner writes member localPositions
                // assuming PreviewRoot is the parent, so a non-zero root offset
                // would shift their visuals on the next spawner pass.
                bool startPoseMode = !(applyPoseOverride && _editingGroupPoseMode == PoseModeAssembled
                                       && (g.isDirty || g.hasPlacement));

                if (startPoseMode && applyPoseOverride)
                {
                    // Start Pose: prefer the baked fabrication-layout rigid body
                    // so the gizmo sits at the constructed-panel centroid and
                    // members snap back to their fabrication offsets — identical
                    // behavior to Assembled Pose but anchored to panel layout.
                    var startRb = g.def?.startRigidBody;
                    if (startRb != null)
                    {
                        if (previewRoot != null)
                        {
                            rootGO.transform.position = previewRoot.TransformPoint(startRb.groupCenter);
                            rootGO.transform.rotation = previewRoot.rotation * startRb.groupRotation;
                        }
                        else
                        {
                            rootGO.transform.localPosition = startRb.groupCenter;
                            rootGO.transform.localRotation = startRb.groupRotation;
                        }
                        rootGO.transform.localScale = Vector3.one;
                        ApplyRigidBodyOffsetsToMembers(rootGO.transform, startRb);
                        continue;
                    }
                    SetRootToChildCentroidPreservingWorld(rootGO.transform, rot, scl);
                    continue;
                }

                // Non-selected group OR non-start authored pose. We must move
                // the root WITHOUT dragging children — the previous iteration
                // may have centered this root on its centroid (while it was
                // selected), and a naive position write would shift the
                // members out of place on deselect.
                Vector3 targetWorldPos = previewRoot != null
                    ? previewRoot.TransformPoint(pos)
                    : pos;
                Quaternion targetWorldRot = previewRoot != null
                    ? previewRoot.rotation * rot
                    : rot;
                SetRootWorldTransformPreservingChildren(rootGO.transform, targetWorldPos, targetWorldRot, scl);
            }
        }

        /// <summary>
        /// Snaps every live group root back to PreviewRoot-local origin
        /// (localPos=0, identity rot, unit scale) while preserving each active
        /// child's world pose. Call this BEFORE any code path that writes
        /// member localPositions (e.g. PackagePartSpawner.ApplyStepAwarePositions)
        /// — those writers assume PreviewRoot is the direct parent, and a
        /// non-origin root would shift visuals.
        /// </summary>
        private void ResetAllGroupRootsToOriginPreservingChildren()
        {
            if (_subassemblyRootGOs == null || _subassemblyRootGOs.Count == 0) return;
            var previewRoot = GetPreviewRoot();
            foreach (var kvp in _subassemblyRootGOs)
            {
                var rootGO = kvp.Value;
                if (rootGO == null) continue;
                Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(Vector3.zero) : Vector3.zero;
                Quaternion worldRot = previewRoot != null ? previewRoot.rotation : Quaternion.identity;
                SetRootWorldTransformPreservingChildren(rootGO.transform, worldPos, worldRot, Vector3.one);
            }
        }

        /// <summary>
        /// Sets a root's world transform without disturbing any active child's
        /// world pose. Used whenever the root's world position changes while
        /// members are already parented under it.
        /// </summary>
        private static void SetRootWorldTransformPreservingChildren(
            Transform root, Vector3 worldPos, Quaternion worldRot, Vector3 scl)
        {
            if (root == null) return;

            int n = root.childCount;
            if (n == 0)
            {
                root.SetPositionAndRotation(worldPos, worldRot);
                root.localScale = scl;
                return;
            }

            var children = new Transform[n];
            var savedPos = new Vector3[n];
            var savedRot = new Quaternion[n];
            for (int c = 0; c < n; c++)
            {
                var child = root.GetChild(c);
                if (child == null || !child.gameObject.activeSelf) continue;
                children[c] = child;
                savedPos[c] = child.position;
                savedRot[c] = child.rotation;
            }

            root.SetPositionAndRotation(worldPos, worldRot);
            root.localScale = scl;

            for (int c = 0; c < n; c++)
            {
                if (children[c] == null) continue;
                children[c].SetPositionAndRotation(savedPos[c], savedRot[c]);
            }
        }

        /// <summary>
        /// Moves <paramref name="root"/> to the centroid of its active children's
        /// world positions while leaving each child's world position unchanged.
        /// Applies the supplied rotation/scale to the root after the shift.
        /// </summary>
        private static void SetRootToChildCentroidPreservingWorld(Transform root, Quaternion rot, Vector3 scl)
        {
            if (root == null) return;

            int n = root.childCount;
            if (n == 0)
            {
                root.localPosition = Vector3.zero;
                root.localRotation = rot;
                root.localScale    = scl;
                return;
            }

            // Capture each active child's world TRS before we disturb the root.
            var children = new Transform[n];
            var savedPos = new Vector3[n];
            var savedRot = new Quaternion[n];
            int active = 0;
            Vector3 sum = Vector3.zero;
            for (int c = 0; c < n; c++)
            {
                var child = root.GetChild(c);
                if (child == null || !child.gameObject.activeSelf) continue;
                children[c] = child;
                savedPos[c] = child.position;
                savedRot[c] = child.rotation;
                sum += child.position;
                active++;
            }

            if (active == 0)
            {
                root.localPosition = Vector3.zero;
                root.localRotation = rot;
                root.localScale    = scl;
                return;
            }

            Vector3 centroid = sum / active;
            root.SetPositionAndRotation(centroid, root.parent != null ? root.parent.rotation * rot : rot);
            root.localScale = scl;

            // Restore children to their original world transforms.
            for (int c = 0; c < n; c++)
            {
                if (children[c] == null) continue;
                children[c].SetPositionAndRotation(savedPos[c], savedRot[c]);
            }
        }

        // ── Group pose read/write helpers ─────────────────────────────────────

        private Vector3 GetActiveGroupPosition(ref GroupEditState g)
            => _editingGroupPoseMode == PoseModeAssembled ? g.assembledPosition : g.startPosition;

        private Quaternion GetActiveGroupRotation(ref GroupEditState g)
            => _editingGroupPoseMode == PoseModeAssembled ? g.assembledRotation : g.startRotation;

        private void ApplyPositionToGroup(ref GroupEditState g, Vector3 pos)
        {
            if (_editingGroupPoseMode == PoseModeAssembled) g.assembledPosition = pos;
            else g.startPosition = pos;
        }

        private void ApplyRotationToGroup(ref GroupEditState g, Quaternion rot)
        {
            if (_editingGroupPoseMode == PoseModeAssembled) g.assembledRotation = rot;
            else g.startRotation = rot;
        }

        // ── Inspector pose fields for groups ───────────────────────────────────

        /// <summary>
        /// Draws the same pose editing UI as individual parts but operating on
        /// the group's root GO. Pose mode toggle + position/rotation/scale fields.
        /// </summary>
        private void DrawGroupPoseFields(ref GroupEditState g, StepDefinition step)
        {
            // NO-TASK detection for groups — id lives in step.visualPartIds.
            // When true, hide Assembled (same rationale as
            // DrawPartPoseToggle's isNoTaskRow: the group isn't being placed
            // this step, it's a visual marker; the authored Assembled Pose
            // has no role here).
            bool isGroupNoTaskRow = false;
            if (step != null && g.def != null && step.visualPartIds != null)
            {
                for (int i = 0; i < step.visualPartIds.Length; i++)
                    if (string.Equals(step.visualPartIds[i], g.def.id, StringComparison.Ordinal))
                    { isGroupNoTaskRow = true; break; }
            }

            // Pose mode toggle — uses the shared PoseToggleStyle so Part and
            // Group Inspectors read identically (green = Start, blue = Assembled,
            // muted gray = inactive).
            EditorGUILayout.BeginHorizontal();

            bool isStart     = _editingGroupPoseMode == PoseModeStart;
            bool isAssembled = _editingGroupPoseMode == PoseModeAssembled;

            if (GUILayout.Toggle(isStart, "Start Pose",
                    PoseToggleStyle(isStart, PoseAccentStart),
                    GUILayout.Height(18)))
            {
                if (!isStart) { _editingGroupPoseMode = PoseModeStart; SyncAllPartMeshesToActivePose(); SyncAllGroupRootsToActivePose(); ActivateAllVisibleGroupMembers(); SceneView.RepaintAll(); }
            }

            if (!isGroupNoTaskRow)
            {
                if (GUILayout.Toggle(isAssembled, "Assembled Pose",
                        PoseToggleStyle(isAssembled, PoseAccentAssembled),
                        GUILayout.Height(18)))
                {
                    if (!isAssembled) { _editingGroupPoseMode = PoseModeAssembled; SyncAllPartMeshesToActivePose(); SyncAllGroupRootsToActivePose(); ActivateAllVisibleGroupMembers(); SceneView.RepaintAll(); }
                }
            }

            EditorGUILayout.EndHorizontal();

            // Synthesized-pose pills (After cue N / Before cue N) — mirror
            // the part-inspector convention on a group. Groups still have
            // ONLY Start and Assembled as intrinsic AUTHORABLE poses; the
            // pills added here are preview-only affordances that let the
            // author visualize the pose a group will land at just after a
            // hold-at-end cue or just before a poseTransition fires.
            // JSON is untouched — the After-cue poses are synthesized by
            // MachinePackageNormalizer on every load; Before-cue pills
            // read directly from cue.fromPose.
            DrawGroupSynthAndBeforeCuePills(ref g, step);

            EditorGUILayout.Space(4);

            // Transform fields — read from the active pose mode
            Vector3    pos = GetActiveGroupPosition(ref g);
            Quaternion rot = GetActiveGroupRotation(ref g);

            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Vector3FieldClip(isAssembled ? "Assembled Position" : "Start Position", pos);
            Vector3 euler  = rot.eulerAngles;
            Vector3 newEuler = Vector3FieldClip(isAssembled ? "Assembled Rotation" : "Start Rotation", euler);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyPositionToGroup(ref g, newPos);
                ApplyRotationToGroup(ref g, Quaternion.Euler(newEuler));
                g.isDirty = true;
                _dirtySubassemblyIds.Add(g.def.id);
                SyncAllGroupRootsToActivePose();
                SceneView.RepaintAll();
            }

            if (g.isDirty)
            {
                var dirtyStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = ColDirty },
                    fontStyle = FontStyle.Bold,
                };
                EditorGUILayout.LabelField("● Unsaved group pose changes", dirtyStyle);
            }

            // Group-level "strip integrated" affordance — always visible so
            // the author can see the count even when it's 0 (which tells
            // them integrated placements aren't the source of pose drift
            // for this group).
            if (g.def != null)
            {
                int integratedCount = CountIntegratedPlacementsForSubassembly(g.def.id);
                EditorGUILayout.BeginHorizontal();
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = integratedCount > 0 ? new Color(0.95f, 0.65f, 0.30f) : new Color(0.55f, 0.58f, 0.62f) },
                    fontStyle = integratedCount > 0 ? FontStyle.Italic : FontStyle.Normal,
                };
                string txt = integratedCount > 0
                    ? $"  {integratedCount} integrated placement(s) on this group"
                    : "  0 integrated placements on this group";
                GUILayout.Label(txt, labelStyle);
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(integratedCount == 0);
                if (GUILayout.Button(new GUIContent("Strip integrated",
                        "Removes every integratedSubassemblyPlacements entry for this group. Members fall back to startPosition / assembledPosition so NO TASK poses dominate."),
                    EditorStyles.miniButton, GUILayout.Width(112)))
                {
                    StripIntegratedPlacementsForSubassembly(g.def.id);
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Renders "After cue N" pills for every synthesized holdAtEnd stepPose
        /// on the subassembly and "Before cue N" pills for every poseTransition
        /// cue hosted on this subassembly with authored fromPose scoped to the
        /// current step. Clicking a pill switches <see cref="_editingGroupPoseMode"/>
        /// so <see cref="SyncAllGroupRootsToActivePose"/> can pose the group
        /// root at the matching preview pose.
        /// </summary>
        private void DrawGroupSynthAndBeforeCuePills(ref GroupEditState g, StepDefinition step)
        {
            if (step == null) return;

            // Source of truth for After-cue pills on groups — the baker
            // writes synthesized stepPoses onto SubassemblyPreviewPlacement.
            // Declared here so both the count scan and the render loop
            // reference the same array.
            var poses = g.placement?.stepPoses;

            int beforeTotal = 0;
            int afterTotal  = 0;
            if (g.def?.animationCues != null)
            {
                for (int i = 0; i < g.def.animationCues.Length; i++)
                {
                    var cue = g.def.animationCues[i];
                    // Skip Before-cue count when the cue's fromPose equals the
                    // group's start pose — the pill would duplicate the Start
                    // pill. Matches the part-scope suppression.
                    if (IsAuthoredFromPoseMatch(cue, step.id)
                        && !IsGroupCueFromPoseSameAsStart(cue, g.placement))
                        beforeTotal++;
                    // Only count After for cues that also have a baked synth
                    // stepPose — we render the pill from that synth's pose,
                    // so no synth = no pill.
                    if (IsAuthoredToPoseMatch(cue, step.id)
                        && FindSynthStepPoseForCue(poses, i) >= 0)
                        afterTotal++;
                }
            }

            // One-shot diagnostic — tells us for each group/step combo what
            // the pill renderer actually sees: cue count, before/after
            // totals, and whether the synth-per-cue lookup is finding
            // entries. Critical for diagnosing "I click After cue and
            // nothing happens" — most likely the pill isn't rendering
            // because afterTotal==0.
            string renderKey = $"{g.def?.id}|{step.id}|beforeT={beforeTotal}|afterT={afterTotal}|poses={(poses?.Length ?? -1)}";
            if (renderKey != _lastGroupPillsDiagKey)
            {
                _lastGroupPillsDiagKey = renderKey;
                int cueCount = g.def?.animationCues?.Length ?? 0;
                int synthCount = 0;
                int matchedSynthCount = 0;
                if (poses != null)
                {
                    for (int pi = 0; pi < poses.Length; pi++)
                    {
                        if (!PoseLabels.IsHoldAtEndSynth(poses[pi])) continue;
                        synthCount++;
                        int cidx = PoseLabels.TryExtractCueIndexFromSynthLabel(poses[pi].label);
                        if (cidx >= 0 && cueCount > 0 && cidx < cueCount
                            && IsAuthoredToPoseMatch(g.def.animationCues[cidx], step.id))
                            matchedSynthCount++;
                    }
                }
                OSE.Core.OseLog.Info($"[TTAW.GroupPills] sub='{g.def?.id}' step='{step.id}' cues={cueCount} stepPoses={(poses?.Length ?? 0)} synthsOnPlacement={synthCount} synthsMatchingThisStep={matchedSynthCount} beforeTotal={beforeTotal} afterTotal={afterTotal}");
            }

            if (beforeTotal == 0 && afterTotal == 0) return;

            EditorGUILayout.BeginHorizontal();

            // Before cue pills — poseTransition cues on the subassembly with
            // authored fromPose scoped to current step. Uses
            // PoseModeBeforeCueBase - ord sentinel range (shared with parts).
            if (beforeTotal > 0)
            {
                int ord = 0;
                for (int i = 0; i < g.def.animationCues.Length; i++)
                {
                    var cue = g.def.animationCues[i];
                    if (!IsAuthoredFromPoseMatch(cue, step.id)) continue;
                    if (IsGroupCueFromPoseSameAsStart(cue, g.placement)) continue;
                    ord++;

                    int mode = PoseModeBeforeCueBase - (ord - 1);
                    string lbl = PoseLabels.BeforeCueName(ord, beforeTotal, out string tip,
                        cue.type ?? "cue", step.id, i);
                    bool isSel = _editingGroupPoseMode == mode;
                    if (GUILayout.Toggle(isSel, new GUIContent(lbl, tip), EditorStyles.miniButton, GUILayout.Height(18)) && !isSel)
                    {
                        _beforeCueGroupDef = g.def;
                        _beforeCueStepId   = step.id;
                        _editingGroupPoseMode = mode;
                        SyncAllPartMeshesToActivePose();
                        SyncAllGroupRootsToActivePose();
                        ActivateAllVisibleGroupMembers();
                        SceneView.RepaintAll();
                    }
                }
            }

            // After cue pills — one per poseTransition cue on the
            // subassembly with authored toPose scoped to the current step.
            //
            // Reads cue.toPose DIRECTLY (not the baker's synth stepPose).
            // Why: at runtime PoseTransitionPlayer.ResolveFrom uses the
            // cue's explicit fromPose when authored — the cue animates
            // fromPose → toPose and the group ends at cue.toPose verbatim,
            // regardless of prior held state. The baker's synth composes
            // current-state * deltaRot, which matches runtime ONLY when
            // fromPose is unauthored (runtime falls back to current
            // baselines). For authored-fromPose cues — the common case on
            // this package — the composed synth disagrees with runtime
            // end-state (step 55's +90° Z cancels step 57's -90° Z,
            // giving identity instead of -90° Z). Reading cue.toPose
            // directly preserves the "what the trainee sees at cue end"
            // invariant.
            if (afterTotal > 0)
            {
                int ord = 0;
                for (int i = 0; i < g.def.animationCues.Length; i++)
                {
                    var cue = g.def.animationCues[i];
                    if (!IsAuthoredToPoseMatch(cue, step.id)) continue;
                    ord++;

                    int mode = PoseModeAfterCueBase - (ord - 1);
                    string tip = $"After cue finishes — jumps the group to cue.toPose.\n{cue.type ?? "cue"} cue #{i} on step '{step.id}'.";
                    string lbl = afterTotal <= 1 ? "After cue" : $"After cue {ord}";
                    bool isSel = _editingGroupPoseMode == mode;
                    if (GUILayout.Toggle(isSel, new GUIContent(lbl, tip), EditorStyles.miniButton, GUILayout.Height(18)) && !isSel)
                    {
                        _afterCueGroupDef        = g.def;
                        _afterCueStepId          = step.id;
                        _afterCueGroupSynthIdx   = -1; // unused in direct-toPose path
                        _editingGroupPoseMode    = mode;
                        SyncAllPartMeshesToActivePose();
                        SyncAllGroupRootsToActivePose();
                        ActivateAllVisibleGroupMembers();
                        SceneView.RepaintAll();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Group-scope equivalents of IsBeforeCueMode / IsAfterCueMode in
        /// TTAW.Layout.cs — they check <c>_editingPoseMode</c> (parts);
        /// these check <c>_editingGroupPoseMode</c> (groups).
        /// </summary>
        private bool IsGroupBeforeCueMode() =>
            _editingGroupPoseMode <= PoseModeBeforeCueBase && _editingGroupPoseMode > PoseModeAfterCueBase;

        private bool IsGroupAfterCueMode() =>
            _editingGroupPoseMode <= PoseModeAfterCueBase && _editingGroupPoseMode > PoseModeAfterCueBase - 100;

        /// <summary>
        /// Returns the sequenceIndex of the step immediately AFTER the
        /// current step (<paramref name="cueStep"/>). That's where the
        /// cue's holdAtEnd synth lands — and therefore the step whose
        /// poseTable already contains the correct post-cue member poses.
        /// Returns false when there's no next step (cueStep is last).
        /// </summary>
        private bool TryGetNextStepSeqForAfterCue(StepDefinition cueStep, out int nextSeq)
        {
            nextSeq = 0;
            if (_pkg?.steps == null || cueStep == null) return false;
            int cur = cueStep.sequenceIndex;
            int best = int.MaxValue;
            foreach (var s in _pkg.steps)
            {
                if (s == null) continue;
                if (s.sequenceIndex > cur && s.sequenceIndex < best)
                    best = s.sequenceIndex;
            }
            if (best == int.MaxValue) return false;
            nextSeq = best;
            return true;
        }

        /// <summary>
        /// Apply each group member's poseTable-resolved pose at the given
        /// <paramref name="seq"/> directly to its live GameObject's local
        /// transform. Mirrors what the editor/runtime does on step
        /// transitions — no formula reimplementation. Resets the root to
        /// identity first (matching the step-navigation render path where
        /// non-placed groups sit at PreviewRoot origin), then writes each
        /// member's WORLD pose directly from poseTable.
        /// </summary>
        private void ApplyGroupMembersFromPoseTable(
            GroupEditState g, Transform rootTransform, Transform previewRoot, int seq)
        {
            string entryKey = $"{g.def?.id}|seq={seq}|partIds={(g.def?.partIds?.Length ?? -1)}|poseTable={(_pkg?.poseTable != null)}";
            if (entryKey != _lastMembersEntryKey)
            {
                _lastMembersEntryKey = entryKey;
                OSE.Core.OseLog.Info($"[TTAW.GroupMembers.Entry] sub='{g.def?.id}' seq={seq} partIds={(g.def?.partIds?.Length ?? -1)} poseTable={(_pkg?.poseTable != null)}");
            }

            if (g.def?.partIds == null || _pkg?.poseTable == null) return;

            // Reset the root to identity — mirror step-navigation render.
            // Step 58's group sits at PreviewRoot origin (non-selected /
            // non-placed groups). Setting root to origin first gives us a
            // clean parent for the child world-position writes below, so
            // members' localPositions correctly equal their poseTable
            // entries (PreviewRoot-local == root-local when root is at
            // origin with identity rotation).
            if (previewRoot != null)
            {
                rootTransform.position = previewRoot.position;
                rootTransform.rotation = previewRoot.rotation;
            }
            else
            {
                rootTransform.localPosition = Vector3.zero;
                rootTransform.localRotation = Quaternion.identity;
            }
            rootTransform.localScale = Vector3.one;

            int found = 0, missingGO = 0, missingPose = 0;
            var sb = new System.Text.StringBuilder();

            foreach (string memberId in g.def.partIds)
            {
                if (string.IsNullOrEmpty(memberId)) continue;
                if (!_pkg.poseTable.TryGet(memberId, seq, out var resolution))
                {
                    missingPose++;
                    continue;
                }

                // Find the live member GO. Members are parented under rootTransform
                // by the spawner. Look up by name (partId == GO name).
                Transform child = null;
                for (int c = 0; c < rootTransform.childCount; c++)
                {
                    var ch = rootTransform.GetChild(c);
                    if (ch != null && string.Equals(ch.name, memberId, StringComparison.Ordinal))
                    { child = ch; break; }
                }
                if (child == null)
                {
                    // Also look under PreviewRoot directly (member might not be
                    // parented under the group root — e.g. if EnsureSubassemblyRoots
                    // hasn't reparented yet on this step's tick).
                    if (previewRoot != null)
                    {
                        for (int c = 0; c < previewRoot.childCount; c++)
                        {
                            var ch = previewRoot.GetChild(c);
                            if (ch != null && string.Equals(ch.name, memberId, StringComparison.Ordinal))
                            { child = ch; break; }
                        }
                    }
                    if (child == null) { missingGO++; continue; }
                }

                found++;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{memberId}=({resolution.pos.x:F3},{resolution.pos.y:F3},{resolution.pos.z:F3}|rotE=({resolution.rot.eulerAngles.x:F0},{resolution.rot.eulerAngles.y:F0},{resolution.rot.eulerAngles.z:F0}))");

                // poseTable returns PreviewRoot-local pose. Convert to
                // world and write via transform.position — Unity computes
                // the correct localPosition given the current parent
                // (which we just reset to identity above).
                Vector3 worldPos = previewRoot != null
                    ? previewRoot.TransformPoint(resolution.pos)
                    : resolution.pos;
                Quaternion worldRot = previewRoot != null
                    ? previewRoot.rotation * resolution.rot
                    : resolution.rot;

                child.position = worldPos;
                child.rotation = worldRot;
                if (resolution.scl.sqrMagnitude > 0.00001f) child.localScale = resolution.scl;
            }

            string diagKey = $"{g.def.id}|seq={seq}|found={found}|miss={missingGO}|missPose={missingPose}";
            if (diagKey != _lastMembersApplyKey)
            {
                _lastMembersApplyKey = diagKey;
                OSE.Core.OseLog.Info($"[TTAW.GroupMembers] sub='{g.def.id}' seq={seq} found={found} missingGO={missingGO} missingPose={missingPose}\n  poses: [{sb}]");
            }
        }
        private string _lastMembersApplyKey;
        private string _lastMembersEntryKey;

        // After-cue preview state for groups (parallel to _beforeCueGroupDef).
        // _afterCueStepId is declared once in TTAW.Layout.cs (partial class)
        // and shared between the part-side and group-side resolvers — both
        // track the same "current step the author is previewing".
        //
        // _afterCueGroupSynthIdx caches the index into g.placement.stepPoses
        // of the specific synth entry that matches the currently-selected
        // After cue pill. Set on pill click; read by
        // TryResolveAfterCuePoseForGroup. Using the synth's baked
        // groupPos/groupRot (computed by the normalizer with centroid
        // rotation composition) gives the correct visual landing — reading
        // cue.toPose directly would show the raw delta input, wrong for
        // groups whose cues rotate members around a centroid.
        private OSE.Content.SubassemblyDefinition _afterCueGroupDef;
        private int _afterCueGroupSynthIdx = -1;
        // One-shot diagnostic keys — prevent sync-logs from spamming every OnGUI.
        private string _lastAfterCueGroupSyncKey;
        private string _lastGroupSyncEntryKey;
        private string _lastGroupPillsDiagKey;
        private string _lastAfterResolveKey;

        private void LogAfterResolve(string why)
        {
            string key = $"{_editingGroupPoseMode}|{why}";
            if (key == _lastAfterResolveKey) return;
            _lastAfterResolveKey = key;
            OSE.Core.OseLog.Warn($"[TTAW.GroupAfterResolve] reject: {why} (mode={_editingGroupPoseMode}, synthIdx={_afterCueGroupSynthIdx})");
        }

        /// <summary>
        /// Finds the index in <paramref name="poses"/> of the synth stepPose
        /// emitted for cue #<paramref name="cueIndex"/>. Returns -1 when no
        /// matching synth exists (cue without holdAtEnd, or normalizer
        /// skipped the bake).
        /// </summary>
        private static int FindSynthStepPoseForCue(StepPoseEntry[] poses, int cueIndex)
        {
            if (poses == null) return -1;
            for (int i = 0; i < poses.Length; i++)
            {
                if (!PoseLabels.IsHoldAtEndSynth(poses[i])) continue;
                if (PoseLabels.TryExtractCueIndexFromSynthLabel(poses[i].label) == cueIndex)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Resolves the After-cue preview pose for the current
        /// <see cref="_editingGroupPoseMode"/> when it sits in the
        /// PoseModeAfterCueBase range. Mirrors <see cref="TryResolveBeforeCuePoseForGroup"/>.
        /// </summary>
        private bool TryResolveAfterCuePoseForGroup(out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
            // After-cue range: mode in [PoseModeAfterCueBase, PoseModeAfterCueBase-99].
            if (_editingGroupPoseMode > PoseModeAfterCueBase) return false;
            if (_editingGroupPoseMode <= PoseModeAfterCueBase - 100) return false;
            if (_afterCueGroupDef?.animationCues == null) return false;
            if (string.IsNullOrEmpty(_afterCueStepId)) return false;

            // Read cue.toPose directly. See pill-render comment for rationale:
            // authored-fromPose cues animate fromPose→toPose at runtime
            // regardless of prior held state, so the visual end-state IS
            // cue.toPose. The baker's composed synth would disagree for
            // stacked-cue cases.
            int requestedOrdinal = PoseModeAfterCueBase - _editingGroupPoseMode; // 0-based
            int ord = 0;
            for (int i = 0; i < _afterCueGroupDef.animationCues.Length; i++)
            {
                var cue = _afterCueGroupDef.animationCues[i];
                if (!IsAuthoredToPoseMatch(cue, _afterCueStepId)) continue;
                if (ord == requestedOrdinal)
                {
                    var p = cue.toPose;
                    pos = new Vector3(p.position.x, p.position.y, p.position.z);
                    rot = new Quaternion(p.rotation.x, p.rotation.y, p.rotation.z, p.rotation.w);
                    scl = new Vector3(p.scale.x, p.scale.y, p.scale.z);
                    if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                    if (rot.x == 0 && rot.y == 0 && rot.z == 0 && rot.w == 0) rot = Quaternion.identity;
                    return true;
                }
                ord++;
            }
            return false;
        }

        // Before-cue preview state for groups (parallel to _beforeCuePartDef).
        // Set when a group Before-cue pill is clicked; read by
        // TryResolveBeforeCuePoseForGroup inside SyncAllGroupRootsToActivePose.
        private OSE.Content.SubassemblyDefinition _beforeCueGroupDef;

        /// <summary>
        /// Resolves the Before-cue preview pose for the current
        /// <see cref="_editingGroupPoseMode"/> when it sits in the
        /// PoseModeBeforeCueBase range. Mirrors <c>TryResolveBeforeCuePose</c>
        /// for parts.
        /// </summary>
        private bool TryResolveBeforeCuePoseForGroup(out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
            // Before-cue range: _editingGroupPoseMode in [PoseModeBeforeCueBase, PoseModeBeforeCueBase-99].
            // After-cue (PoseModeAfterCueBase = -200) is MORE negative, so exclude it
            // here — TryResolveAfterCuePoseForGroup owns that range.
            if (_editingGroupPoseMode > PoseModeBeforeCueBase) return false;
            if (_editingGroupPoseMode <= PoseModeAfterCueBase) return false;
            if (_beforeCueGroupDef?.animationCues == null) return false;
            if (string.IsNullOrEmpty(_beforeCueStepId)) return false;

            int requestedOrdinal = PoseModeBeforeCueBase - _editingGroupPoseMode;
            int ord = 0;
            for (int i = 0; i < _beforeCueGroupDef.animationCues.Length; i++)
            {
                var cue = _beforeCueGroupDef.animationCues[i];
                if (!IsAuthoredFromPoseMatch(cue, _beforeCueStepId)) continue;
                if (ord == requestedOrdinal)
                {
                    var p = cue.fromPose;
                    pos = new Vector3(p.position.x, p.position.y, p.position.z);
                    rot = new Quaternion(p.rotation.x, p.rotation.y, p.rotation.z, p.rotation.w);
                    scl = new Vector3(p.scale.x, p.scale.y, p.scale.z);
                    if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                    if (rot.x == 0 && rot.y == 0 && rot.z == 0 && rot.w == 0) rot = Quaternion.identity;
                    return true;
                }
                ord++;
            }
            return false;
        }

        // ── Aggregate (phase) pose sync ──────────────────────────────────────

        /// <summary>
        /// Positions aggregate root GOs (e.g. "Frame Cube Joining") at their
        /// baked centroid when selected, with child group roots preserved in
        /// world space. Aggregates that aren't selected snap back to origin
        /// (world-preserving) so their child groups' local hierarchies stay
        /// valid for the spawner's local-position writes.
        /// </summary>
        private void SyncAggregateRootsToActivePose(Transform previewRoot)
        {
            if (_subassemblyRootGOs == null || _pkg == null) return;
            foreach (var kvp in _subassemblyRootGOs)
            {
                var rootGO = kvp.Value;
                if (rootGO == null) continue;
                if (!_pkg.TryGetSubassembly(kvp.Key, out var sub) || sub == null || !sub.isAggregate) continue;

                bool isSelected = string.Equals(_canvasSelectedSubId, sub.id, StringComparison.Ordinal);
                Vector3 worldPos; Quaternion worldRot; Vector3 scl = Vector3.one;
                if (isSelected && sub.startRigidBody != null)
                {
                    worldPos = previewRoot != null
                        ? previewRoot.TransformPoint(sub.startRigidBody.groupCenter)
                        : sub.startRigidBody.groupCenter;
                    worldRot = previewRoot != null
                        ? previewRoot.rotation * sub.startRigidBody.groupRotation
                        : sub.startRigidBody.groupRotation;
                }
                else
                {
                    worldPos = previewRoot != null ? previewRoot.TransformPoint(Vector3.zero) : Vector3.zero;
                    worldRot = previewRoot != null ? previewRoot.rotation : Quaternion.identity;
                }
                SetRootWorldTransformPreservingChildren(rootGO.transform, worldPos, worldRot, scl);
            }
        }

        // ── Rigid-body helpers (Phase 1) ─────────────────────────────────────

        /// <summary>
        /// Looks up the baked rigid body for the currently selected group at
        /// the current step's primary targetId. Returns false when the step
        /// has no targetId or the group has no baked entry for that target.
        /// </summary>
        private bool TryGetSelectedGroupRigidBody(in GroupEditState g, out GroupRigidBody rb)
        {
            rb = null;
            if (g.def == null || g.def.rigidBodyByTargetId == null) return false;
            StepDefinition step = _sceneBuildStepActive && _stepIds != null
                                   && _stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            // The rigid body only applies when THIS step is actually committing
            // THIS group to an integrated target (e.g. stacking the frame side
            // onto the cube). Fabrication steps — which lay out the same group's
            // members at their individual assembled positions — must fall back
            // to the legacy per-part assembled fields.
            if (step == null) return false;
            if (!string.Equals(step.requiredSubassemblyId, g.def.id, StringComparison.Ordinal)) return false;
            string targetId = step.targetIds != null && step.targetIds.Length > 0 ? step.targetIds[0] : null;
            if (string.IsNullOrEmpty(targetId)) return false;
            return g.def.rigidBodyByTargetId.TryGetValue(targetId, out rb) && rb != null;
        }

        /// <summary>
        /// Writes each member's localPosition/localRotation/localScale from the
        /// rigid body's fixed offsets. Members thereafter follow the root as a
        /// rigid unit — moving the root moves them all, which is the whole
        /// point of the Phase 1 simplification.
        /// </summary>
        private void ApplyRigidBodyOffsetsToMembers(Transform root, GroupRigidBody rb)
        {
            if (root == null || rb == null || rb.memberPositionOffsets == null) return;
            for (int c = 0; c < root.childCount; c++)
            {
                var child = root.GetChild(c);
                if (child == null) continue;
                string pid = child.name;
                if (!rb.memberPositionOffsets.TryGetValue(pid, out var off)) continue;
                child.localPosition = off;
                if (rb.memberRotationOffsets != null && rb.memberRotationOffsets.TryGetValue(pid, out var r))
                    child.localRotation = r;
                if (rb.memberScales != null && rb.memberScales.TryGetValue(pid, out var s) && s.sqrMagnitude > 0.00001f)
                    child.localScale = s;
            }
        }

        // ── Find group index by subassembly ID ───────────────────────────────

        private int FindGroupIdx(string subId)
        {
            if (_groups == null || string.IsNullOrEmpty(subId)) return -1;
            for (int i = 0; i < _groups.Length; i++)
                if (_groups[i].def != null && string.Equals(_groups[i].def.id, subId, StringComparison.Ordinal))
                    return i;
            return -1;
        }
    }
}
