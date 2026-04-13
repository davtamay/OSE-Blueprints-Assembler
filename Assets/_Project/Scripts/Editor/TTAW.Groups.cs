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
                    stepPoses        = hasP && sp.stepPoses != null ? new List<StepPoseEntry>(sp.stepPoses) : null,
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

            for (int i = 0; i < _groups.Length; i++)
            {
                ref GroupEditState g = ref _groups[i];
                if (g.def == null) continue;
                if (!_subassemblyRootGOs.TryGetValue(g.def.id, out var rootGO) || rootGO == null)
                    continue;

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
                else if (applyPoseOverride && _editingGroupPoseMode >= 0 && g.stepPoses != null
                    && _editingGroupPoseMode < g.stepPoses.Count && (g.isDirty || g.hasPlacement))
                {
                    var sp = g.stepPoses[_editingGroupPoseMode];
                    pos = PackageJsonUtils.ToVector3(sp.position);
                    rot = PackageJsonUtils.ToUnityQuaternion(sp.rotation);
                    scl = PackageJsonUtils.ToVector3(sp.scale);
                }
                // else: Start Pose mode OR non-selected group → origin

                if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;

                // Start Pose centering: ONLY for the currently selected group,
                // so the gizmo sits inside the frame. Non-selected groups must
                // stay at origin — the spawner writes member localPositions
                // assuming PreviewRoot is the parent, so a non-zero root offset
                // would shift their visuals on the next spawner pass.
                bool startPoseMode = !(applyPoseOverride && _editingGroupPoseMode == PoseModeAssembled
                                       && (g.isDirty || g.hasPlacement))
                                  && !(applyPoseOverride && _editingGroupPoseMode >= 0 && g.stepPoses != null
                                       && _editingGroupPoseMode < g.stepPoses.Count && (g.isDirty || g.hasPlacement));

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
        {
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
                return PackageJsonUtils.ToVector3(g.stepPoses[_editingGroupPoseMode].position);
            return _editingGroupPoseMode == PoseModeAssembled ? g.assembledPosition : g.startPosition;
        }

        private Quaternion GetActiveGroupRotation(ref GroupEditState g)
        {
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
                return PackageJsonUtils.ToUnityQuaternion(g.stepPoses[_editingGroupPoseMode].rotation);
            return _editingGroupPoseMode == PoseModeAssembled ? g.assembledRotation : g.startRotation;
        }

        private void ApplyPositionToGroup(ref GroupEditState g, Vector3 pos)
        {
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
                g.stepPoses[_editingGroupPoseMode].position = PackageJsonUtils.ToFloat3(pos);
            else if (_editingGroupPoseMode == PoseModeAssembled) g.assembledPosition = pos;
            else g.startPosition = pos;
        }

        private void ApplyRotationToGroup(ref GroupEditState g, Quaternion rot)
        {
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
                g.stepPoses[_editingGroupPoseMode].rotation = PackageJsonUtils.ToQuaternion(rot);
            else if (_editingGroupPoseMode == PoseModeAssembled) g.assembledRotation = rot;
            else g.startRotation = rot;
        }

        // ── Inspector pose fields for groups ───────────────────────────────────

        /// <summary>
        /// Draws the same pose editing UI as individual parts but operating on
        /// the group's root GO. Pose mode toggle + position/rotation/scale fields.
        /// </summary>
        private void DrawGroupPoseFields(ref GroupEditState g, StepDefinition step)
        {
            // Pose mode toggle
            EditorGUILayout.BeginHorizontal();
            var toggleStyle = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };

            bool isStart     = _editingGroupPoseMode == PoseModeStart;
            bool isAssembled = _editingGroupPoseMode == PoseModeAssembled;

            var startStyle = new GUIStyle(toggleStyle)
            {
                normal = { textColor = isStart ? new Color(0.30f, 0.78f, 0.36f) : new Color(0.55f, 0.55f, 0.58f) },
            };
            if (GUILayout.Toggle(isStart, "Start Pose", startStyle, GUILayout.Height(18)))
            {
                if (!isStart) { _editingGroupPoseMode = PoseModeStart; SyncAllPartMeshesToActivePose(); SyncAllGroupRootsToActivePose(); ActivateAllVisibleGroupMembers(); SceneView.RepaintAll(); }
            }

            var asmStyle = new GUIStyle(toggleStyle)
            {
                normal = { textColor = isAssembled ? new Color(0.20f, 0.62f, 0.95f) : new Color(0.55f, 0.55f, 0.58f) },
            };
            if (GUILayout.Toggle(isAssembled, "Assembled Pose", asmStyle, GUILayout.Height(18)))
            {
                if (!isAssembled) { _editingGroupPoseMode = PoseModeAssembled; SyncAllPartMeshesToActivePose(); SyncAllGroupRootsToActivePose(); ActivateAllVisibleGroupMembers(); SceneView.RepaintAll(); }
            }

            // Per-stepPose toggles — [Custom 1] [Custom 2] … lets the author
            // target a specific authored group stepPose.
            int gPoseCount = g.stepPoses?.Count ?? 0;
            for (int gi = 0; gi < gPoseCount; gi++)
            {
                string btnLabel = !string.IsNullOrEmpty(g.stepPoses[gi].label)
                    ? g.stepPoses[gi].label
                    : $"Custom {gi + 1}";
                bool sel = _editingGroupPoseMode == gi;
                if (GUILayout.Toggle(sel, btnLabel, toggleStyle, GUILayout.Height(18)) && !sel)
                {
                    _editingGroupPoseMode = gi;
                    SyncAllPartMeshesToActivePose();
                    SyncAllGroupRootsToActivePose();
                    ActivateAllVisibleGroupMembers();
                    SceneView.RepaintAll();
                }
            }

            // [+] add a new group stepPose anchored to the current step.
            if (GUILayout.Button("+", toggleStyle, GUILayout.Width(22), GUILayout.Height(18)))
                AddGroupStepPoseForCurrentStep(ref g);

            EditorGUILayout.EndHorizontal();

            // Inline From/Through propagation row — mirrors the part UI.
            // Same-group filter defaults ON so the pickers only list steps
            // referencing this group or any of its members.
            string curStepId = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? _stepIds[_stepFilterIdx] : null;
            if (!string.IsNullOrEmpty(curStepId) && g.def != null)
                DrawGroupPropagationRow(ref g, curStepId);

            // Span-control row mirrors the part UI — author picks "Just this
            // step / Start → this step / This step → end / All / Fixed range"
            // in one click. Only visible when a custom group pose is active.
            if (_editingGroupPoseMode >= 0 && gPoseCount > 0 && _editingGroupPoseMode < gPoseCount)
                DrawGroupStepPoseDetailRow(ref g, _editingGroupPoseMode);

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

            EditorGUILayout.Space(4);
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

        // ── Group stepPose authoring (parallel to parts) ─────────────────────

        /// <summary>
        /// Creates a new <see cref="StepPoseEntry"/> on the group anchored to
        /// the currently selected step. Captures the live group root pose so
        /// the entry starts at whatever the author sees on screen. Default
        /// span is anchor→end (empty fields) — identical to how a new part
        /// stepPose defaults to "This step → end".
        /// </summary>
        private void AddGroupStepPoseForCurrentStep(ref GroupEditState g)
        {
            if (g.def == null) return;
            string stepId = (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                ? _stepIds[_stepFilterIdx] : "";

            Vector3 pos = _editingGroupPoseMode == PoseModeAssembled ? g.assembledPosition : g.startPosition;
            Quaternion rot = _editingGroupPoseMode == PoseModeAssembled ? g.assembledRotation : g.startRotation;
            Vector3 scl = _editingGroupPoseMode == PoseModeAssembled ? g.assembledScale : g.startScale;

            if (g.stepPoses == null) g.stepPoses = new List<StepPoseEntry>();
            g.stepPoses.Add(new StepPoseEntry
            {
                stepId   = stepId,
                position = PackageJsonUtils.ToFloat3(pos),
                rotation = PackageJsonUtils.ToQuaternion(rot),
                scale    = PackageJsonUtils.ToFloat3(scl),
            });
            g.isDirty = true;
            _dirtySubassemblyIds.Add(g.def.id);
            _editingGroupPoseMode = g.stepPoses.Count - 1;
            SyncAllGroupRootsToActivePose();
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawGroupStepPoseDetailRow(ref GroupEditState g, int poseIdx)
        {
            if (g.stepPoses == null || poseIdx < 0 || poseIdx >= g.stepPoses.Count) return;
            var pose = g.stepPoses[poseIdx];

            EditorGUILayout.BeginHorizontal();
            var smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("Anchor", smallLabel, GUILayout.Width(46));
            string anchorLabel = string.IsNullOrEmpty(pose.stepId) ? "(none)" : StepShortLabel(pose.stepId);
            if (GUILayout.Button(anchorLabel, EditorStyles.miniButton, GUILayout.MinWidth(80)))
                ShowGroupStepIdPickerMenu(g.def.id, poseIdx);

            GUILayout.Label("Apply to", smallLabel, GUILayout.Width(52));
            string applyLabel = DescribeSpan(pose);
            if (GUILayout.Button(applyLabel, EditorStyles.popup, GUILayout.MinWidth(130)))
                ShowStepPoseSpanMenuForGroup(g.def.id, poseIdx);

            GUILayout.FlexibleSpace();

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
                RemoveGroupStepPose(g.def.id, poseIdx);
            EditorGUILayout.EndHorizontal();
        }

        private void ShowGroupStepIdPickerMenu(string subId, int poseIdx)
        {
            var menu = new GenericMenu();
            if (_pkg?.steps == null) { menu.AddDisabledItem(new GUIContent("No steps available")); menu.ShowAsContext(); return; }
            foreach (var step in _pkg.steps)
            {
                if (step == null) continue;
                string sid = step.id;
                string label = step.GetDisplayName() ?? sid;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    int gi = FindGroupIdx(subId);
                    if (gi < 0) return;
                    ref GroupEditState gg = ref _groups[gi];
                    if (gg.stepPoses == null || poseIdx < 0 || poseIdx >= gg.stepPoses.Count) return;
                    gg.stepPoses[poseIdx].stepId = sid;
                    gg.isDirty = true;
                    _dirtySubassemblyIds.Add(subId);
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        // Same-group filter for group propagation pickers. Defaults ON.
        private bool _groupPropagationFilterSameGroup = true;

        private void DrawGroupPropagationRow(ref GroupEditState g, string anchorStepId)
        {
            int gPoseCount = g.stepPoses?.Count ?? 0;
            int activeIdx  = _editingGroupPoseMode >= 0 && gPoseCount > 0 && _editingGroupPoseMode < gPoseCount
                ? _editingGroupPoseMode : -1;
            string fromId    = activeIdx >= 0 ? g.stepPoses[activeIdx].propagateFromStep    : "";
            string throughId = activeIdx >= 0 ? g.stepPoses[activeIdx].propagateThroughStep : "";

            var header = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
            string poseName = _editingGroupPoseMode == PoseModeStart     ? "Start pose"
                            : _editingGroupPoseMode == PoseModeAssembled ? "Assembled pose"
                            : (activeIdx >= 0
                                ? (string.IsNullOrEmpty(g.stepPoses[activeIdx].label) ? $"Custom {activeIdx + 1}" : g.stepPoses[activeIdx].label)
                                : "Pose");
            EditorGUILayout.LabelField($"Propagate {poseName}", header);

            _groupPropagationFilterSameGroup = EditorGUILayout.ToggleLeft(
                new GUIContent("Only steps using this group",
                    "List only steps whose subassemblyId / requiredSubassemblyId matches this group, or whose part fields touch any of its members. Flip off to pick from every step."),
                _groupPropagationFilterSameGroup, EditorStyles.miniLabel);

            string subId = g.def?.id;
            EditorGUILayout.BeginHorizontal();
            var smallLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label("From", smallLabel, GUILayout.Width(40));
            if (GUILayout.Button(FormatStepButtonLabel(fromId, "(start of package)"),
                    EditorStyles.popup, GUILayout.MinWidth(150)))
            {
                Func<StepDefinition, bool> filter = _groupPropagationFilterSameGroup ? (Func<StepDefinition, bool>)(s => StepTouchesGroup(s, subId)) : null;
                string captured = subId;
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, filter, anchorStepId,
                        sid => SetGroupPropagationEndpoint(captured, anchorStepId, fromEndpoint: true, stepId: sid));
            }

            GUILayout.Label("Through", smallLabel, GUILayout.Width(58));
            if (GUILayout.Button(FormatStepButtonLabel(throughId, "(end of package)"),
                    EditorStyles.popup, GUILayout.MinWidth(150)))
            {
                Func<StepDefinition, bool> filter = _groupPropagationFilterSameGroup ? (Func<StepDefinition, bool>)(s => StepTouchesGroup(s, subId)) : null;
                string captured = subId;
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, filter, anchorStepId,
                        sid => SetGroupPropagationEndpoint(captured, anchorStepId, fromEndpoint: false, stepId: sid));
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool StepTouchesGroup(StepDefinition step, string subId)
        {
            if (step == null || string.IsNullOrEmpty(subId)) return false;
            if (string.Equals(step.subassemblyId,         subId, StringComparison.Ordinal)) return true;
            if (string.Equals(step.requiredSubassemblyId, subId, StringComparison.Ordinal)) return true;
            // Also include steps whose part fields reach any member of this group.
            if (_pkg != null && _pkg.TryGetSubassembly(subId, out var sub) && sub?.partIds != null)
            {
                foreach (var pid in sub.partIds)
                    if (StepTouchesPart(step, pid)) return true;
            }
            return false;
        }

        private void SetGroupPropagationEndpoint(string subId, string anchorStepId, bool fromEndpoint, string stepId)
        {
            int gi = FindGroupIdx(subId);
            if (gi < 0) return;
            ref GroupEditState g = ref _groups[gi];

            StepPoseEntry target;
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
            {
                target = g.stepPoses[_editingGroupPoseMode];
            }
            else
            {
                Vector3 capPos; Quaternion capRot; Vector3 capScl;
                if (_editingGroupPoseMode == PoseModeAssembled)
                {
                    capPos = g.assembledPosition; capRot = g.assembledRotation; capScl = g.assembledScale;
                }
                else
                {
                    capPos = g.startPosition; capRot = g.startRotation; capScl = g.startScale;
                }
                if (g.stepPoses == null) g.stepPoses = new List<StepPoseEntry>();
                target = new StepPoseEntry
                {
                    stepId   = anchorStepId,
                    position = PackageJsonUtils.ToFloat3(capPos),
                    rotation = PackageJsonUtils.ToQuaternion(capRot),
                    scale    = PackageJsonUtils.ToFloat3(capScl),
                };
                g.stepPoses.Add(target);
                _editingGroupPoseMode = g.stepPoses.Count - 1;
            }

            if (fromEndpoint) target.propagateFromStep    = stepId;
            else              target.propagateThroughStep = stepId;

            g.isDirty = true;
            _dirtySubassemblyIds.Add(subId);
            SyncAllGroupRootsToActivePose();
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Like <see cref="ShowPartPosePropagationMenu"/> but for groups. When
        /// Start/Assembled is active, captures those field values into a new
        /// stepPose entry at the current step; otherwise rewrites the active
        /// Custom entry's span.
        /// </summary>
        private void ShowGroupPosePropagationMenu(string subId, string anchorStepId)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("From start → this step"),   false, () => PropagateGroupPose(subId, anchorStepId, SpanPreset.StartToAnchor, null));
            menu.AddItem(new GUIContent("This step → end"),          false, () => PropagateGroupPose(subId, anchorStepId, SpanPreset.AnchorToEnd, null));
            menu.AddItem(new GUIContent("All steps"),                false, () => PropagateGroupPose(subId, anchorStepId, SpanPreset.AllSteps, null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Through step… (search)"),  false, () =>
            {
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, sid => PropagateGroupPose(subId, anchorStepId, SpanPreset.FixedThrough, sid));
            });
            menu.ShowAsContext();
        }

        private void PropagateGroupPose(string subId, string anchorStepId, SpanPreset preset, string throughStepIdOpt)
        {
            int gi = FindGroupIdx(subId);
            if (gi < 0) return;
            ref GroupEditState g = ref _groups[gi];

            StepPoseEntry target;
            if (_editingGroupPoseMode >= 0 && g.stepPoses != null && _editingGroupPoseMode < g.stepPoses.Count)
            {
                target = g.stepPoses[_editingGroupPoseMode];
            }
            else
            {
                Vector3 capPos; Quaternion capRot; Vector3 capScl;
                if (_editingGroupPoseMode == PoseModeAssembled)
                {
                    capPos = g.assembledPosition;
                    capRot = g.assembledRotation;
                    capScl = g.assembledScale;
                }
                else
                {
                    capPos = g.startPosition;
                    capRot = g.startRotation;
                    capScl = g.startScale;
                }
                if (g.stepPoses == null) g.stepPoses = new List<StepPoseEntry>();
                target = new StepPoseEntry
                {
                    stepId   = anchorStepId,
                    position = PackageJsonUtils.ToFloat3(capPos),
                    rotation = PackageJsonUtils.ToQuaternion(capRot),
                    scale    = PackageJsonUtils.ToFloat3(capScl),
                };
                g.stepPoses.Add(target);
                _editingGroupPoseMode = g.stepPoses.Count - 1;
            }

            ApplyPreset(target, preset, throughStepIdOpt);
            g.isDirty = true;
            _dirtySubassemblyIds.Add(subId);
            SyncAllGroupRootsToActivePose();
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
            Repaint();
        }

        private void ShowStepPoseSpanMenuForGroup(string subId, int poseIdx)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("From start → this step"), false, () => SetGroupSpan(subId, poseIdx, SpanPreset.StartToAnchor, null));
            menu.AddItem(new GUIContent("This step → end"),        false, () => SetGroupSpan(subId, poseIdx, SpanPreset.AnchorToEnd, null));
            menu.AddItem(new GUIContent("All steps"),              false, () => SetGroupSpan(subId, poseIdx, SpanPreset.AllSteps, null));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Through step… (search)"), false, () =>
            {
                EditorApplication.delayCall += () =>
                    StepPickerDropdown.Open(_pkg?.steps, sid => SetGroupSpan(subId, poseIdx, SpanPreset.FixedThrough, sid));
            });
            menu.ShowAsContext();
        }

        private void SetGroupSpan(string subId, int poseIdx, SpanPreset preset, string throughStepIdOpt)
        {
            int gi = FindGroupIdx(subId);
            if (gi < 0) return;
            ref GroupEditState g = ref _groups[gi];
            if (g.stepPoses == null || poseIdx < 0 || poseIdx >= g.stepPoses.Count) return;
            ApplyPreset(g.stepPoses[poseIdx], preset, throughStepIdOpt);
            g.isDirty = true;
            _dirtySubassemblyIds.Add(subId);
            SyncAllGroupRootsToActivePose();
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
            Repaint();
        }

        private void RemoveGroupStepPose(string subId, int poseIdx)
        {
            int gi = FindGroupIdx(subId);
            if (gi < 0) return;
            ref GroupEditState g = ref _groups[gi];
            if (g.stepPoses == null || poseIdx < 0 || poseIdx >= g.stepPoses.Count) return;
            g.stepPoses.RemoveAt(poseIdx);
            g.isDirty = true;
            _dirtySubassemblyIds.Add(subId);
            _editingGroupPoseMode = PoseModeAssembled;
            SyncAllGroupRootsToActivePose();
            SyncAllPartMeshesToActivePose();
            SceneView.RepaintAll();
            Repaint();
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
