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
            if (_groups == null) return;
            var previewRoot = GetPreviewRoot();

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
                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                Vector3 scl = Vector3.one;

                if (_editingGroupPoseMode == PoseModeAssembled && (g.isDirty || g.hasPlacement))
                {
                    pos = g.assembledPosition;
                    rot = g.assembledRotation;
                    scl = g.assembledScale;
                }
                else if (_editingGroupPoseMode >= 0 && g.stepPoses != null
                    && _editingGroupPoseMode < g.stepPoses.Count && (g.isDirty || g.hasPlacement))
                {
                    var sp = g.stepPoses[_editingGroupPoseMode];
                    pos = PackageJsonUtils.ToVector3(sp.position);
                    rot = PackageJsonUtils.ToUnityQuaternion(sp.rotation);
                    scl = PackageJsonUtils.ToVector3(sp.scale);
                }
                // else: Start Pose mode → pos stays (0,0,0), parts at their individual positions

                if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;

                // Set the root GO's transform in PreviewRoot space
                if (previewRoot != null)
                {
                    rootGO.transform.position = previewRoot.TransformPoint(pos);
                    rootGO.transform.rotation = previewRoot.rotation * rot;
                }
                else
                {
                    rootGO.transform.localPosition = pos;
                    rootGO.transform.localRotation = rot;
                }
                rootGO.transform.localScale = scl;
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
                if (!isStart) { _editingGroupPoseMode = PoseModeStart; SyncAllGroupRootsToActivePose(); SceneView.RepaintAll(); }
            }

            var asmStyle = new GUIStyle(toggleStyle)
            {
                normal = { textColor = isAssembled ? new Color(0.20f, 0.62f, 0.95f) : new Color(0.55f, 0.55f, 0.58f) },
            };
            if (GUILayout.Toggle(isAssembled, "Assembled Pose", asmStyle, GUILayout.Height(18)))
            {
                if (!isAssembled) { _editingGroupPoseMode = PoseModeAssembled; SyncAllGroupRootsToActivePose(); SceneView.RepaintAll(); }
            }
            EditorGUILayout.EndHorizontal();

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
