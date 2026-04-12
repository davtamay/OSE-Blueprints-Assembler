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
// TTAW.Parts.cs  —  BuildPartList, step-aware pose helpers, undo/redo,
//                   live-GO scene handles, connections overlay, and the
//                   FindLivePartGO / FindChildNamed spawner-query helpers.
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Parts tab — BuildPartList + sync ──────────────────────────────────

        private void BuildPartList()
        {
            if (_pkg?.parts == null) { _parts = Array.Empty<PartEditState>(); return; }

            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                var step = FindStep(_stepIds[_stepFilterIdx]);
                // Always set filterIds when a step is selected — null means "show all", but a step
                // with no requiredPartIds (e.g. OBSERVE/CONFIRM steps) should show zero parts.
                filterIds = step?.requiredPartIds != null
                    ? new HashSet<string>(step.requiredPartIds, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var list = new List<PartEditState>();
            foreach (var def in _pkg.parts)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

                PartPreviewPlacement pp = FindPartPlacement(def.id);
                bool hasP = pp != null;

                // Prefer stagingPose from parts[] (agent-authored source of truth).
                // Fall back to previewConfig.partPlacements.startPosition for un-migrated packages.
                StagingPose sp = def.stagingPose;
                Vector3    initPos = sp != null ? PackageJsonUtils.ToVector3(sp.position)
                                   : hasP ? PackageJsonUtils.ToVector3(pp.startPosition) : Vector3.zero;
                Quaternion initRot = sp != null ? PackageJsonUtils.ToUnityQuaternion(sp.rotation)
                                   : hasP ? PackageJsonUtils.ToUnityQuaternion(pp.startRotation) : Quaternion.identity;
                Vector3    initScl = sp != null && (sp.scale.x != 0f || sp.scale.y != 0f || sp.scale.z != 0f)
                                   ? PackageJsonUtils.ToVector3(sp.scale)
                                   : hasP ? PackageJsonUtils.ToVector3(pp.startScale) : Vector3.one;
                Color      initCol = sp != null && sp.color.a > 0f
                                   ? new Color(sp.color.r, sp.color.g, sp.color.b, sp.color.a)
                                   : hasP ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a) : ColAuthored;

                var state = new PartEditState
                {
                    def           = def,
                    placement     = pp,
                    hasPlacement  = hasP,
                    startPosition = initPos,
                    startRotation = initRot,
                    startScale    = initScl,
                    // For assembledPosition: use placement if available, otherwise
                    // default to the same position as start (part stays where it is
                    // until explicitly moved by a future step).
                    assembledPosition  = pp != null ? PackageJsonUtils.ToVector3(pp.assembledPosition)  : initPos,
                    assembledRotation  = pp != null ? PackageJsonUtils.ToUnityQuaternion(pp.assembledRotation) : initRot,
                    assembledScale     = pp != null ? PackageJsonUtils.ToVector3(pp.assembledScale)     : initScl,
                    color         = initCol,
                    isDirty       = false,
                    stepPoses     = pp != null && pp.stepPoses != null ? DeepCopyStepPoses(pp.stepPoses) : null,
                };
                if (state.startScale.sqrMagnitude < 0.00001f) state.startScale = Vector3.one;
                if (state.assembledScale.sqrMagnitude  < 0.00001f) state.assembledScale  = Vector3.one;
                list.Add(state);
            }

            // Restore selection across rebuilds by matching part ID
            string prevSelectedId = (_selectedPartIdx >= 0 && _parts != null && _selectedPartIdx < _parts.Length)
                ? _parts[_selectedPartIdx].def.id : _selectedPartId;

            _parts          = list.ToArray();
            _selectedPartIdx = -1;
            if (prevSelectedId != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                    if (string.Equals(_parts[i].def.id, prevSelectedId, StringComparison.OrdinalIgnoreCase))
                    { _selectedPartIdx = i; break; }
            }
            _multiSelectedParts.Clear();
        }

        // ── Live spawner GO helpers (mirrors PPAW) ────────────────────────────

        private GameObject FindLivePartGO(string partId)
        {
            // Primary: fast path via spawner registry
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var s) && s?.SpawnedParts != null)
            {
                foreach (var go in s.SpawnedParts)
                    if (go != null && go.name == partId) return go;
                // Service is available but part not found — don't fall through to scene search
                // (avoids picking up unrelated GOs from other windows/tools).
                return null;
            }

            // Fallback: service not registered (e.g., first edit-mode frame before driver starts).
            // Walk the loaded scene(s) looking for a root-or-child GO whose name matches the part ID.
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    // Check the root itself, then all descendants
                    var found = root.name == partId ? root : FindChildNamed(root.transform, partId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static GameObject FindChildNamed(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child.gameObject;
                var found = FindChildNamed(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Returns the display position/rotation/scale for a part, respecting the
        /// step-filter context cached by the last <see cref="RespawnScene"/> call.
        ///
        /// When a step is selected:
        ///   - Past parts  → always assembledPosition  (already assembled)
        ///   - Current part → startPosition unless <c>_editAssembledPose</c> or part is a
        ///                    subassembly member (which arrives pre-assembled)
        ///   - Future parts → caller should skip; returns false
        ///
        /// When no step selected (All Steps mode): always obeys <c>_editAssembledPose</c>.
        /// </summary>
        private bool TryGetStepAwarePose(ref PartEditState p,
            out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            string pid = p.def.id;

            // Intermediate step pose editing — if editing this part's stepPose, use it directly
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count
                && _selectedPartIdx >= 0 && _selectedPartIdx < (_parts?.Length ?? 0)
                && _parts[_selectedPartIdx].def.id == pid)
            {
                var sp = p.stepPoses[_editingPoseMode];
                pos = PackageJsonUtils.ToVector3(sp.position);
                rot = PackageJsonUtils.ToUnityQuaternion(sp.rotation);
                scl = PackageJsonUtils.ToVector3(sp.scale);
            }
            else if (_sceneBuildStepActive)
            {
                bool inMap = _sceneBuildPartStepSeq.TryGetValue(pid, out int placedAt);

                if (inMap && placedAt > _sceneBuildCurrentSeq)
                {
                    // Future part — must not be shown in the scene
                    pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
                    return false;
                }

                // Non-selected parts stay at start when viewing Start or any Custom pose;
                // only jump to assembled when explicitly viewing Assembled Pose.
                bool useStart = inMap
                    && placedAt == _sceneBuildCurrentSeq
                    && !_sceneBuildCurrentSubassembly.Contains(pid)
                    && _editingPoseMode != PoseModeAssembled;

                pos = useStart ? p.startPosition : p.assembledPosition;
                rot = useStart ? p.startRotation  : p.assembledRotation;
                scl = useStart ? p.startScale     : p.assembledScale;
            }
            else
            {
                pos = _editAssembledPose ? p.assembledPosition : p.startPosition;
                rot = _editAssembledPose ? p.assembledRotation  : p.startRotation;
                scl = _editAssembledPose ? p.assembledScale     : p.startScale;
            }

            // Phase A2: working orientation is now applied via the subassembly
            // root GO's localRotation (set in EnsureSubassemblyRoot). Parts
            // parented under the root inherit the rotation automatically via
            // Unity's transform hierarchy — no manual recomputation needed.
            // The SetPoseInPreviewSpace helper converts PreviewRoot-space pos/rot
            // to world space, and the parent GO applies the delta.

            if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
            return true;
        }

        // ── Pose read/write helpers for current _editingPoseMode ──────────────

        private Vector3 GetActivePosePosition(ref PartEditState p)
        {
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
                return PackageJsonUtils.ToVector3(p.stepPoses[_editingPoseMode].position);
            return _editAssembledPose ? p.assembledPosition : p.startPosition;
        }

        private Quaternion GetActivePoseRotation(ref PartEditState p)
        {
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
                return PackageJsonUtils.ToUnityQuaternion(p.stepPoses[_editingPoseMode].rotation);
            return _editAssembledPose ? p.assembledRotation : p.startRotation;
        }

        private void ApplyPositionToPart(ref PartEditState p, Vector3 pos)
        {
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
                p.stepPoses[_editingPoseMode].position = PackageJsonUtils.ToFloat3(pos);
            else if (_editAssembledPose) p.assembledPosition = pos;
            else p.startPosition = pos;
        }

        private void ApplyRotationToPart(ref PartEditState p, Quaternion rot)
        {
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
                p.stepPoses[_editingPoseMode].rotation = PackageJsonUtils.ToQuaternion(rot);
            else if (_editAssembledPose) p.assembledRotation = rot;
            else p.startRotation = rot;
        }

        private void ApplyPoseToPart(ref PartEditState p, Vector3 pos, Quaternion rot)
        {
            ApplyPositionToPart(ref p, pos);
            ApplyRotationToPart(ref p, rot);
        }

        // ── PreviewRoot-space transform helpers (Phase A1) ────────────────────
        //
        // All authored positions are in PreviewRoot local space. These helpers
        // convert between PreviewRoot space and world space so part transforms
        // work correctly regardless of whether the part is a direct child of
        // PreviewRoot or nested under a subassembly root GO.
        //
        // Phase A1: with parts still flat under PreviewRoot, these are identity
        // transforms (PreviewRoot.TransformPoint of a local pos = the child's
        // localPosition). Phase A2 reparents parts under subassembly roots and
        // these helpers become load-bearing.

        /// <summary>
        /// Sets a part's transform from PreviewRoot-local authored data.
        /// If the part is directly under PreviewRoot, localPosition/Rotation
        /// are set directly. If the part is under a group root (subassembly
        /// hierarchy), the position is set relative to PreviewRoot so the
        /// group root's transform (working orientation) can apply on top.
        /// </summary>
        private static void SetPoseInPreviewSpace(Transform part, Transform previewRoot,
            Vector3 pos, Quaternion rot, Vector3 scale)
        {
            if (previewRoot != null)
            {
                // Check if the part's parent IS PreviewRoot (direct child)
                // or a group root (nested child). In either case, we need the
                // part at the authored PreviewRoot-local position. Setting
                // world position via TransformPoint ensures this regardless
                // of the parent hierarchy — Unity auto-computes localPosition.
                //
                // BUT: if the parent has a working orientation rotation, we
                // want the part's local position to be the offset from the
                // group center, not the world position. The group root's
                // rotation then naturally rotates the part.
                //
                // Strategy: always set via world position. This is correct
                // because the authored pos IS in PreviewRoot space, and
                // TransformPoint converts it to world space. The parent
                // hierarchy (if any) is transparent.
                part.position = previewRoot.TransformPoint(pos);
                part.rotation = previewRoot.rotation * rot;
            }
            else
            {
                part.localPosition = pos;
                part.localRotation = rot;
            }
            if (scale.sqrMagnitude > 0.00001f) part.localScale = scale;
        }

        /// <summary>
        /// Reads a part's current transform back into PreviewRoot-local space.
        /// </summary>
        private static (Vector3 pos, Quaternion rot) GetPoseInPreviewSpace(
            Transform part, Transform previewRoot)
        {
            if (previewRoot != null)
            {
                return (
                    previewRoot.InverseTransformPoint(part.position),
                    Quaternion.Inverse(previewRoot.rotation) * part.rotation
                );
            }
            return (part.localPosition, part.localRotation);
        }

        private void SyncPartMeshToActivePose(ref PartEditState p)
        {
            if (!TryGetStepAwarePose(ref p, out Vector3 pos, out Quaternion rot, out Vector3 scl))
                return; // future part — leave hidden

            var liveGO = FindLivePartGO(p.def.id);
            if (liveGO != null)
                SetPoseInPreviewSpace(liveGO.transform, GetPreviewRoot(), pos, rot, scl);
        }

        /// <summary>
        /// Returns true if this part is a member of a group whose pose is being
        /// actively managed (Assembled Pose or custom step pose). In that case,
        /// the group root handles positioning and individual sync should be skipped.
        /// </summary>
        private bool IsPartManagedByGroupPose(string partId)
        {
            if (_editingGroupPoseMode == PoseModeStart) return false; // Start = parts at individual positions
            if (_groups == null || _selectedGroupIdx < 0 || _selectedGroupIdx >= _groups.Length) return false;

            ref GroupEditState g = ref _groups[_selectedGroupIdx];
            if (g.def?.partIds == null) return false;
            if (!g.isDirty && !g.hasPlacement) return false; // no authored pose, root at origin anyway

            foreach (var pid in g.def.partIds)
                if (string.Equals(pid, partId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private void SyncAllPartMeshesToActivePose()
        {
            if (_parts == null) return;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (!_parts[i].hasPlacement) continue;

                // Compute step-aware pose; hide future parts that don't belong to this step.
                if (!TryGetStepAwarePose(ref _parts[i], out Vector3 pos, out Quaternion rot, out Vector3 scl))
                {
                    var futureGO = FindLivePartGO(_parts[i].def.id);
                    if (futureGO != null && futureGO.activeSelf)
                        futureGO.SetActive(false);
                    continue;
                }

                var liveGO = FindLivePartGO(_parts[i].def.id);
                if (liveGO != null && !liveGO.activeSelf)
                    liveGO.SetActive(true);

                // Skip world-position sync for parts whose group root is handling
                // their position (Assembled Pose mode). The parts' localPositions
                // are already set relative to the root from when they were parented
                // — moving the root moves all parts together.
                if (_parts[i].def != null && IsPartManagedByGroupPose(_parts[i].def.id))
                    continue;

                SyncPartMeshToActivePose(ref _parts[i]);
            }
        }

        /// <summary>
        /// For parts with hasPlacement == false that are visible in the scene,
        /// captures their live GO position into the editor state so they persist
        /// when navigating to the next step. Called AFTER SyncAllPartMeshesToActivePose
        /// so the spawner has already positioned them via its fallback layout.
        /// </summary>
        private void CaptureUnplacedPartPositions()
        {
            if (_parts == null) return;
            var root = GetPreviewRoot();

            for (int i = 0; i < _parts.Length; i++)
            {
                if (_parts[i].hasPlacement) continue;
                if (_parts[i].def == null) continue;

                var liveGO = FindLivePartGO(_parts[i].def.id);
                if (liveGO == null || !liveGO.activeSelf) continue;

                Vector3 capturedPos;
                Quaternion capturedRot;
                Vector3 capturedScl = liveGO.transform.localScale;

                if (root != null)
                {
                    capturedPos = root.InverseTransformPoint(liveGO.transform.position);
                    capturedRot = Quaternion.Inverse(root.rotation) * liveGO.transform.rotation;
                }
                else
                {
                    capturedPos = liveGO.transform.localPosition;
                    capturedRot = liveGO.transform.localRotation;
                }

                _parts[i].hasPlacement     = true;
                _parts[i].startPosition    = capturedPos;
                _parts[i].startRotation    = capturedRot;
                _parts[i].startScale       = capturedScl;
                // assembledPosition defaults to the same as startPosition
                // so the part stays in place on the next step.
                _parts[i].assembledPosition = capturedPos;
                _parts[i].assembledRotation = capturedRot;
                _parts[i].assembledScale    = capturedScl;
            }
        }

        // ── Parts tab — Undo / Redo ───────────────────────────────────────────

        private PartSnapshot CapturePartSnapshot(ref PartEditState p) => new()
        {
            startPosition = p.startPosition,
            startRotation = p.startRotation,
            startScale    = p.startScale,
            assembledPosition  = p.assembledPosition,
            assembledRotation  = p.assembledRotation,
            assembledScale     = p.assembledScale,
            stepPoses     = p.stepPoses != null ? DeepCopyStepPoseList(p.stepPoses) : null,
        };

        private void BeginPartEdit(int forIdx)
        {
            if (_snapshotPendingPart || forIdx < 0 || _parts == null || forIdx >= _parts.Length) return;
            _undoStackParts.Add((forIdx, CapturePartSnapshot(ref _parts[forIdx])));
            if (_undoStackParts.Count > MaxUndoHistory) _undoStackParts.RemoveAt(0);
            _redoStackParts.Clear();
            _snapshotPendingPart = true;
        }

        private void EndPartEdit() => _snapshotPendingPart = false;

        private void UndoPartPose()
        {
            if (_undoStackParts.Count == 0 || _parts == null) return;
            var (idx, prev) = _undoStackParts[_undoStackParts.Count - 1];
            _undoStackParts.RemoveAt(_undoStackParts.Count - 1);
            if (idx < _parts.Length)
            {
                _redoStackParts.Add((idx, CapturePartSnapshot(ref _parts[idx])));
                ApplyPartSnapshot(idx, prev);
            }
        }

        private void RedoPartPose()
        {
            if (_redoStackParts.Count == 0 || _parts == null) return;
            var (idx, next) = _redoStackParts[_redoStackParts.Count - 1];
            _redoStackParts.RemoveAt(_redoStackParts.Count - 1);
            if (idx < _parts.Length)
            {
                _undoStackParts.Add((idx, CapturePartSnapshot(ref _parts[idx])));
                ApplyPartSnapshot(idx, next);
            }
        }

        private void ApplyPartSnapshot(int idx, PartSnapshot s)
        {
            ref PartEditState p = ref _parts[idx];
            p.startPosition = s.startPosition;
            p.startRotation = s.startRotation;
            p.startScale    = s.startScale;
            p.assembledPosition  = s.assembledPosition;
            p.assembledRotation  = s.assembledRotation;
            p.assembledScale     = s.assembledScale;
            p.stepPoses          = s.stepPoses != null ? DeepCopyStepPoseList(s.stepPoses) : null;
            p.isDirty            = true;
            _snapshotPendingPart = false;
            SyncPartMeshToActivePose(ref p);
            Repaint();
            SceneView.RepaintAll();
        }

        private static StepPoseEntry CloneStepPoseEntry(StepPoseEntry e) => new StepPoseEntry
        {
            stepId   = e.stepId,
            label    = e.label,
            position = new SceneFloat3 { x = e.position.x, y = e.position.y, z = e.position.z },
            rotation = new SceneQuaternion { x = e.rotation.x, y = e.rotation.y, z = e.rotation.z, w = e.rotation.w },
            scale    = new SceneFloat3 { x = e.scale.x, y = e.scale.y, z = e.scale.z },
        };

        private static List<StepPoseEntry> DeepCopyStepPoseList(List<StepPoseEntry> src)
        {
            if (src == null) return null;
            var dst = new List<StepPoseEntry>(src.Count);
            foreach (var e in src) dst.Add(CloneStepPoseEntry(e));
            return dst;
        }

        private static List<StepPoseEntry> DeepCopyStepPoses(StepPoseEntry[] src)
        {
            if (src == null) return null;
            var dst = new List<StepPoseEntry>(src.Length);
            foreach (var e in src) dst.Add(CloneStepPoseEntry(e));
            return dst;
        }

        // ── Parts tab — SceneView handles ─────────────────────────────────────

        private void DrawPartSceneHandles(SceneView sv)
        {
            if (_parts == null || _parts.Length == 0) return;

            // Use spawner.PreviewRoot as the coordinate root — exactly as PPAW does.
            ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner);
            Transform root = spawner?.PreviewRoot;

            // Guard against stale Selection.activeGameObject pointing to a destroyed
            // spawned GO — clears before Unity's Inspector tries to access it.
            if (Selection.activeGameObject == null && !ReferenceEquals(Selection.activeGameObject, null))
                Selection.activeGameObject = null;

            // Time-based cooldown: suppress native polling and handle change-detection
            // for a fixed duration after selection/pose changes, regardless of frame rate.
            bool poseCooldownActive = EditorApplication.timeSinceStartup < _poseSwitchCooldownUntil;

            if (Event.current.type == EventType.MouseUp) EndPartEdit();

            // F key → frame on selected part
            if (_selectedPartIdx >= 0 && _selectedPartIdx < _parts.Length
                && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F)
            {
                var liveGO = FindLivePartGO(_parts[_selectedPartIdx].def.id);
                if (liveGO != null) { Selection.activeGameObject = liveGO; sv.FrameSelected(); }
                Event.current.Use();
            }

            // Scene selection → window selection sync (click GO in Hierarchy to select it).
            // Skip when multi-selection is active — the user's batch selection takes priority.
            if (spawner?.SpawnedParts != null && _multiSelectedParts.Count <= 1 && _multiSelectedTaskSeqIdxs.Count <= 1)
            {
                var activeGO = Selection.activeGameObject;
                if (activeGO != null)
                {
                    foreach (var liveGO in spawner.SpawnedParts)
                    {
                        if (liveGO == null) continue;
                        if (activeGO != liveGO && !activeGO.transform.IsChildOf(liveGO.transform)) continue;
                        for (int si = 0; si < _parts.Length; si++)
                        {
                            if (_parts[si].def.id != liveGO.name) continue;
                            if (_selectedPartIdx != si)
                            {
                                _selectedPartIdx = si;
                                _selectedPartId  = liveGO.name;
                                _multiSelectedParts.Clear();
                                // Clear target selection
                                _selectedIdx = -1;
                                _multiSelected.Clear();
                                // Suppress false dirty from polling the newly selected part
                                _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5;
                                SyncAllPartMeshesToActivePose();
                                Repaint();
                            }
                            break;
                        }
                        break;
                    }
                }
            }

            // Indicator dots for NON-selected parts — drawn before gizmo handles.
            // Selected part indicator is drawn AFTER handles (end of method) so it
            // renders on top of the position/rotation gizmo.
            bool hasStep = _stepFilterIdx > 0;
            if (root != null && hasStep)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    ref PartEditState p = ref _parts[i];
                    if (!p.hasPlacement) continue;
                    bool isSelected = i == _selectedPartIdx
                                   || (_multiSelectedParts.Count > 1 && _multiSelectedParts.Contains(i));
                    if (isSelected) continue; // drawn after handles

                    var liveGO = FindLivePartGO(p.def.id);
                    Vector3 worldPos = liveGO != null
                        ? liveGO.transform.position
                        : root.TransformPoint(_editAssembledPose ? p.assembledPosition : p.startPosition);
                    float size = HandleUtility.GetHandleSize(worldPos) * 0.08f;

                    Color col = p.isDirty      ? ColDirty
                              : p.hasPlacement ? ColAuthored
                              :                  ColNoPlacement;
                    Handles.color = col;
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                }
            }

            // Hide Unity's native transform gizmo when multi-selecting parts so only
            // our custom Handles.PositionHandle / RotationHandle is visible.
            bool isMultiPart = _multiSelectedParts.Count > 1;
            if (Tools.hidden != isMultiPart) Tools.hidden = isMultiPart;

            // Native Move-tool polling on selected part only (matches PPAW).
            // Skipped during multi-select — our custom handle drives all parts.
            // Uses TryGetStepAwarePose for comparison so the expected pose matches
            // what SyncPartMeshToActivePose set — prevents false re-dirty on past/subassembly parts.
            //
            // Two-stage detection: first check if the GO drifted from the expected pose
            // (e.g. external driver repositioned it). If so, re-sync it back rather than
            // marking dirty — only mark dirty when the user has genuinely dragged the handle
            // past the threshold, which also means the GO differs from the PREVIOUS expected
            // pose (not just a resync artifact).
            if (!poseCooldownActive && !isMultiPart &&
                _selectedPartIdx >= 0 && _selectedPartIdx < _parts.Length &&
                (Tools.current == Tool.Move || Tools.current == Tool.Transform) &&
                (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout))
            {
                ref PartEditState pp = ref _parts[_selectedPartIdx];
                if (TryGetStepAwarePose(ref pp, out Vector3 expectedPos, out Quaternion expectedRot, out _))
                {
                    var pollGO = FindLivePartGO(pp.def.id);
                    if (pollGO != null)
                    {
                        var (goPos, goRot) = GetPoseInPreviewSpace(pollGO.transform, root);
                        bool posChg = (goPos - expectedPos).sqrMagnitude > 1e-5f;
                        bool rotChg = Quaternion.Angle(goRot, expectedRot) > 0.05f;
                        if (posChg || rotChg)
                        {
                            // Check if user is actively interacting (mouse button held).
                            // If not, this is likely an external reposition — re-sync instead of dirtying.
                            if (GUIUtility.hotControl == 0)
                            {
                                // No active handle drag — snap GO back to expected pose
                                SetPoseInPreviewSpace(pollGO.transform, root, expectedPos, expectedRot, pollGO.transform.localScale);
                            }
                            else
                            {
                                BeginPartEdit(_selectedPartIdx);
                                ApplyPoseToPart(ref pp, goPos, goRot);
                                pp.isDirty = true;
                                EndPartEdit();
                                Repaint();
                            }
                        }
                    }
                }
            }

            // Position + Rotation handles for selected part (PPAW pattern)
            if (_selectedPartIdx < 0 || _selectedPartIdx >= _parts.Length || root == null) return;

            ref PartEditState sel     = ref _parts[_selectedPartIdx];
            var selectedGO = FindLivePartGO(sel.def.id);
            if (selectedGO == null) return;

            Vector3    selWorldPos = selectedGO.transform.position;
            Quaternion selWorldRot = selectedGO.transform.rotation;

            Handles.color = ColSelected;
            Handles.DrawWireDisc(selWorldPos, sv.camera.transform.forward,
                HandleUtility.GetHandleSize(selWorldPos) * 0.14f);

            // Position handle
            EditorGUI.BeginChangeCheck();
            Quaternion posHandleRot = Tools.pivotRotation == PivotRotation.Local ? selWorldRot : Quaternion.identity;
            Vector3    newWorldPos  = Handles.PositionHandle(selWorldPos, posHandleRot);
            if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldPos - selWorldPos).sqrMagnitude > 1e-10f)
            {
                BeginPartEdit(_selectedPartIdx);
                Vector3 oldPreviewPos = GetActivePosePosition(ref sel);
                selectedGO.transform.position = newWorldPos;
                var (newPreviewPos, _) = GetPoseInPreviewSpace(selectedGO.transform, root);
                ApplyPositionToPart(ref sel, newPreviewPos);
                sel.isDirty = true;

                // Move group as a unit — apply the same delta so offsets are preserved
                if (_multiSelectedParts.Count > 1)
                {
                    Vector3 delta = newPreviewPos - oldPreviewPos;
                    foreach (int midx in _multiSelectedParts)
                    {
                        if (midx == _selectedPartIdx || midx < 0 || midx >= _parts.Length) continue;
                        ref PartEditState mp = ref _parts[midx];
                        Vector3 cur = GetActivePosePosition(ref mp);
                        cur += delta;
                        ApplyPositionToPart(ref mp, cur);
                        mp.isDirty = true;
                        var otherGO = FindLivePartGO(mp.def.id);
                        if (otherGO != null) SetPoseInPreviewSpace(otherGO.transform, root, cur, otherGO.transform.localRotation, otherGO.transform.localScale);
                    }
                }
                Repaint();
            }

            // Rotation handle
            EditorGUI.BeginChangeCheck();
            Quaternion rotOrientation = Tools.pivotRotation == PivotRotation.Local ? selWorldRot : Quaternion.identity;
            Quaternion newWorldRot    = Handles.RotationHandle(rotOrientation, selWorldPos);
            if (EditorGUI.EndChangeCheck() && !poseCooldownActive && Quaternion.Angle(newWorldRot, rotOrientation) > 0.01f)
            {
                BeginPartEdit(_selectedPartIdx);
                if (!_rotDragActivePart)
                {
                    _rotDragActivePart      = true;
                    _rotDragStartHandlePart = rotOrientation;
                    _rotDragStartLocalPart  = GetActivePoseRotation(ref sel);
                }
                Quaternion rootRot     = root.rotation;
                Quaternion worldDelta  = newWorldRot * Quaternion.Inverse(_rotDragStartHandlePart);
                Quaternion newLocalRot = Quaternion.Inverse(rootRot) * (worldDelta * (rootRot * _rotDragStartLocalPart));

                // newLocalRot is in PreviewRoot space (rootRot = previewRoot.rotation)
                SetPoseInPreviewSpace(selectedGO.transform, root,
                    GetPoseInPreviewSpace(selectedGO.transform, root).pos,
                    newLocalRot, selectedGO.transform.localScale);
                ApplyRotationToPart(ref sel, newLocalRot);
                sel.isDirty = true;

                // Apply same absolute rotation to all multi-selected parts
                if (_multiSelectedParts.Count > 1)
                    foreach (int midx in _multiSelectedParts)
                    {
                        if (midx == _selectedPartIdx || midx < 0 || midx >= _parts.Length) continue;
                        ref PartEditState mp = ref _parts[midx];
                        ApplyRotationToPart(ref mp, newLocalRot);
                        mp.isDirty = true;
                        var otherGO = FindLivePartGO(mp.def.id);
                        if (otherGO != null)
                        {
                            var (otherPos, _) = GetPoseInPreviewSpace(otherGO.transform, root);
                            SetPoseInPreviewSpace(otherGO.transform, root, otherPos, newLocalRot, otherGO.transform.localScale);
                        }
                    }
                Repaint();
            }
            else if (_rotDragActivePart) _rotDragActivePart = false;

            // Selected part indicator — drawn AFTER gizmo handles so it's visible on top.
            if (root != null && hasStep)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    bool isSel = i == _selectedPartIdx
                              || (_multiSelectedParts.Count > 1 && _multiSelectedParts.Contains(i));
                    if (!isSel) continue;
                    ref PartEditState p = ref _parts[i];
                    if (!p.hasPlacement) continue;

                    var liveGO = FindLivePartGO(p.def.id);
                    Vector3 worldPos = liveGO != null
                        ? liveGO.transform.position
                        : root.TransformPoint(_editAssembledPose ? p.assembledPosition : p.startPosition);
                    float handleSz = HandleUtility.GetHandleSize(worldPos);

                    // Solid dot + outer ring so the selection indicator is clearly visible
                    Handles.color = ColSelected;
                    Handles.DrawSolidDisc(worldPos, sv.camera.transform.forward, handleSz * 0.04f);
                    Handles.DrawWireDisc(worldPos, sv.camera.transform.forward, handleSz * 0.22f);
                }
            }
        }

        private void DrawConnectionsSceneOverlay()
        {
            if (_targets == null || _targets.Length == 0) return;
            Transform root = GetPreviewRoot();
            if (root == null) return;

            // Build targetId → WireConnectEntry lookup so endpoint sphere colors match the wire color.
            var wireEntryMap = new Dictionary<string, WireConnectEntry>(StringComparer.Ordinal);
            StepDefinition overlayStep = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            if (overlayStep?.wireConnect?.wires != null)
                foreach (var we in overlayStep.wireConnect.wires)
                    if (we?.targetId != null) wireEntryMap[we.targetId] = we;

            foreach (var t in _targets)
            {
                if (t.portA.sqrMagnitude < 0.000001f && t.portB.sqrMagnitude < 0.000001f) continue;

                wireEntryMap.TryGetValue(t.def?.id ?? "", out WireConnectEntry we2);
                Color wireColor = (we2 != null && we2.color.a > 0f)
                    ? new Color(we2.color.r, we2.color.g, we2.color.b, 1f) : ColPortPoint;

                // Wire tube is rendered by _wirePreviewRoot mesh (see RefreshWirePreview).
                // Here we only draw the A/B endpoint spheres and labels as Handles overlays.
                Handles.color = wireColor;
                Vector3 wA = root.TransformPoint(t.portA);
                Vector3 wB = root.TransformPoint(t.portB);
                float sA = HandleUtility.GetHandleSize(wA) * 0.08f;
                float sB = HandleUtility.GetHandleSize(wB) * 0.08f;
                Handles.SphereHandleCap(0, wA, Quaternion.identity, sA, EventType.Repaint);
                Handles.SphereHandleCap(0, wB, Quaternion.identity, sB, EventType.Repaint);
                Handles.Label(wA, " A", EditorStyles.boldLabel);
                Handles.Label(wB, " B", EditorStyles.boldLabel);
            }
        }
    }
}
