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
                // Include Required, Optional, AND NoTask (visualPartIds) parts
                // — all three are legitimate task-sequence rows. Filtering to
                // only requiredPartIds hid Optional / No Task entries and made
                // their inspector resolution impossible (the inspector looks
                // up _parts by id — missing entries → no pose UI).
                filterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (step?.requiredPartIds != null)
                    foreach (var pid in step.requiredPartIds)
                        if (!string.IsNullOrEmpty(pid)) filterIds.Add(pid);
                if (step?.optionalPartIds != null)
                    foreach (var pid in step.optionalPartIds)
                        if (!string.IsNullOrEmpty(pid)) filterIds.Add(pid);
                if (step?.visualPartIds != null)
                    foreach (var pid in step.visualPartIds)
                        if (!string.IsNullOrEmpty(pid)) filterIds.Add(pid);
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

            // Rebuild the ownership cache consumed by the proactive-guidance
            // surfaces (drop-zone pre-check, part inspector, step header, task
            // rows). Pinned to BuildPartList so every mutation path that
            // already refreshes parts also refreshes ownership.
            _ownership = PartOwnershipIndex.Build(_pkg);
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
            bool isSelectedPart = _selectedPartIdx >= 0
                && _selectedPartIdx < (_parts?.Length ?? 0)
                && _parts[_selectedPartIdx].def?.id == pid;

            // [A] Author is editing a Custom (author-written) intermediate
            // pose on the selected part — read directly from the in-memory
            // edit state because un-saved changes aren't in the baked
            // PoseTable yet.
            if (isSelectedPart
                && _editingPoseMode >= 0
                && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                var sp = p.stepPoses[_editingPoseMode];
                pos = PackageJsonUtils.ToVector3(sp.position);
                rot = PackageJsonUtils.ToUnityQuaternion(sp.rotation);
                scl = PackageJsonUtils.ToVector3(sp.scale);
                if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                return true;
            }

            // [B] Step-aware rendering. The Start/Assembled toggle applies
            // GLOBALLY to every part the current step acts on (Required or
            // Optional), not just the selected one — clicking "Assembled"
            // moves all of step N's task parts to their assembled poses, and
            // Start moves them all back. Other parts (past-placed visuals,
            // NO-TASK intros from earlier steps) read from the baked
            // PoseTable so they stay where they should be regardless of the
            // toggle.
            if (_sceneBuildStepActive)
            {
                var poseTable = _pkg?.poseTable;
                if (poseTable != null)
                {
                    bool currentStepActsOnPart = false;
                    if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                    {
                        var curStep = FindStep(_stepIds[_stepFilterIdx]);
                        if (curStep != null)
                        {
                            if (curStep.requiredPartIds != null)
                                foreach (var x in curStep.requiredPartIds)
                                    if (string.Equals(x, pid, StringComparison.Ordinal)) { currentStepActsOnPart = true; break; }
                            if (!currentStepActsOnPart && curStep.optionalPartIds != null)
                                foreach (var x in curStep.optionalPartIds)
                                    if (string.Equals(x, pid, StringComparison.Ordinal)) { currentStepActsOnPart = true; break; }
                        }
                    }

                    if (currentStepActsOnPart
                        && (_editingPoseMode == PoseModeStart || _editingPoseMode == PoseModeAssembled))
                    {
                        // Read directly from the in-memory edit state — the
                        // PoseTable is baked at load time and will return the
                        // stale value, snapping the part back as soon as the
                        // user tries to move it in Start/Assembled mode.
                        bool useAssembled = _editingPoseMode == PoseModeAssembled;
                        pos = useAssembled ? p.assembledPosition : p.startPosition;
                        rot = useAssembled ? p.assembledRotation : p.startRotation;
                        scl = useAssembled ? p.assembledScale    : p.startScale;
                        if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                        return true;
                    }

                    if (!poseTable.TryGet(pid, _sceneBuildCurrentSeq, out var cr))
                    {
                        pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
                        return false;
                    }
                    pos = cr.pos; rot = cr.rot; scl = cr.scl;
                    if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                    return true;
                }

                // No PoseTable (package not normalized yet) — transient load
                // state. Fall through to the "All Steps" code path so the
                // window doesn't flash empty.
            }

            // [C] "All Steps" mode — no step filter active. Respect the
            // global Start/Assembled toggle on the raw part placement.
            pos = _editAssembledPose ? p.assembledPosition : p.startPosition;
            rot = _editAssembledPose ? p.assembledRotation  : p.startRotation;
            scl = _editAssembledPose ? p.assembledScale     : p.startScale;
            if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
            return true;
        }

        private int SeqIndexForStepId(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || _pkg?.steps == null) return -1;
            foreach (var s in _pkg.steps)
                if (s != null && string.Equals(s.id, stepId, StringComparison.Ordinal))
                    return s.sequenceIndex;
            return -1;
        }

        /// <summary>
        /// When the author drags a part in the SceneView and the part is not
        /// referenced by the current step's <c>requiredPartIds</c>,
        /// <c>optionalPartIds</c>, <c>visualPartIds</c>, or
        /// <c>requiredSubassemblyId</c> members, automatically:
        ///  1. add it to <c>step.visualPartIds</c> (NO TASK),
        ///  2. create (or reuse) an author-authored <see cref="StepPoseEntry"/>
        ///     anchored at this step with span "from this step → end" so the
        ///     subsequent <see cref="ApplyPositionToPart"/> / <see cref="ApplyRotationToPart"/>
        ///     write lands in the right slot,
        ///  3. point <c>_editingPoseMode</c> at that entry so the next
        ///     <c>ApplyPositionToPart</c> mutates it.
        /// Result: an alien drag becomes a clean NO TASK + waypoint authoring
        /// gesture without any extra clicks. If the part is already part of
        /// the step's tasks, this is a no-op.
        /// </summary>
        private void AutoPromoteAlienPartToNoTaskWaypoint(int partIdx)
        {
            if (partIdx < 0 || _parts == null || partIdx >= _parts.Length) return;
            var step = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            if (step == null) return;

            ref PartEditState p = ref _parts[partIdx];
            string partId = p.def?.id;
            if (string.IsNullOrEmpty(partId)) return;

            // Auto-promote is OFF. Two reasons:
            //   (1) alien-drag silently mutated step.visualPartIds AND added
            //       a phantom "Custom N" stepPose without the author asking —
            //       exactly the "why did Custom 1 appear?" surprise.
            //   (2) editing the canonical startPosition / assembledPosition
            //       via the gizmo is almost always what the author actually
            //       wants when nudging a part that isn't in this step's task
            //       list. The subsequent ApplyPositionToPart handles that.
            //
            // If the author wants a step-specific pose for an alien part they
            // can (a) add it as NO TASK via the R/O/N toggle, or
            // (b) drop the part onto the drop zone, then edit.
            // StepTouchesPart is unused here now; kept for any future caller.
            return;
        }

        // ── Pose source trace (diagnostic) ────────────────────────────────────

        internal enum PoseSourceKind
        {
            Hidden,
            StartPosition,
            AssembledPosition,
            StepPose,
            NoTaskDefault,
            Integrated,
        }

        /// <summary>
        /// Records which branch of the pose-resolution decision tree a part
        /// took at a specific step. Used by the "WHAT'S CHANGING" panel to
        /// explain why a part moved (or didn't) between two steps.
        /// </summary>
        internal readonly struct PoseSourceTag
        {
            public readonly PoseSourceKind kind;
            public readonly string anchorStepId;   // StepPose
            public readonly string subassemblyId;  // Integrated
            public readonly string targetId;       // Integrated

            public PoseSourceTag(PoseSourceKind k, string anchor = null, string sub = null, string tgt = null)
            { kind = k; anchorStepId = anchor; subassemblyId = sub; targetId = tgt; }

            public string PrettyLabel()
            {
                switch (kind)
                {
                    case PoseSourceKind.Hidden:             return "hidden";
                    case PoseSourceKind.StartPosition:      return "startPosition";
                    case PoseSourceKind.AssembledPosition:  return "assembledPosition";
                    case PoseSourceKind.NoTaskDefault:      return "noTaskDefault(startPosition)";
                    case PoseSourceKind.StepPose:           return $"stepPose[{anchorStepId ?? "?"}]";
                    case PoseSourceKind.Integrated:         return $"integrated[{subassemblyId ?? "?"},{targetId ?? "?"}]";
                }
                return "?";
            }

            public bool ValueEquals(PoseSourceTag other)
                => kind == other.kind
                && string.Equals(anchorStepId,  other.anchorStepId,  StringComparison.Ordinal)
                && string.Equals(subassemblyId, other.subassemblyId, StringComparison.Ordinal)
                && string.Equals(targetId,      other.targetId,      StringComparison.Ordinal);
        }

        /// <summary>
        /// Stand-alone pose resolver that mirrors <see cref="TryGetStepAwarePose"/>'s
        /// decision tree but accepts any viewing sequenceIndex (not just the
        /// currently-selected one) and returns a <see cref="PoseSourceTag"/>
        /// describing which branch won. Used by the WHAT'S CHANGING panel to
        /// compare two steps without mutating the scene-build caches.
        /// </summary>
        private bool TracePartPoseAtStep(string partId, int viewingSeq,
            out Vector3 pos, out Quaternion rot, out Vector3 scl,
            out PoseSourceTag tag)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
            tag = new PoseSourceTag(PoseSourceKind.Hidden);

            if (_pkg?.steps == null || string.IsNullOrEmpty(partId)) return false;

            // 1) First-appearance sequenceIndex across requiredPartIds /
            //    optionalPartIds / visualPartIds / requiredSubassemblyId.
            int placedAt = ResolvePartFirstAppearance(partId);
            if (placedAt < 0) { tag = new PoseSourceTag(PoseSourceKind.Hidden); return false; }
            if (placedAt > viewingSeq) { tag = new PoseSourceTag(PoseSourceKind.Hidden); return false; }

            // 2) Find the part-edit state so we can read startPosition etc.
            PartEditState p = default;
            bool havePart = false;
            if (_parts != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                {
                    if (_parts[i].def != null && string.Equals(_parts[i].def.id, partId, StringComparison.Ordinal))
                    { p = _parts[i]; havePart = true; break; }
                }
            }
            if (!havePart) return false;

            // Decision tree mirrors the RUNTIME SPAWNER
            // (PackagePartSpawner.ApplyStepAwarePositions) so the panel
            // reports what the author actually sees in the SceneView. This is
            // deliberately NOT identical to TTAW's editor-side
            // TryGetStepAwarePose — the spawner does not apply TTAW's NO TASK
            // override, so the visual reality follows the spawner's branch
            // order: integrated > stepPose > assembled (past) / start (current).

            bool usePlay = placedAt < viewingSeq;

            // "Current step" (placedAt == viewingSeq): spawner writes the
            // part's startPosition (fabrication layout).
            if (!usePlay)
            {
                pos = p.startPosition; rot = p.startRotation; scl = p.startScale;
                tag = new PoseSourceTag(PoseSourceKind.StartPosition);
                return true;
            }

            // Past step — spawner checks integrated first, then stepPose,
            // then assembledPosition.
            if (TryFindIntegratedForPart(partId, viewingSeq, out Vector3 iPos, out Quaternion iRot, out Vector3 iScl,
                out string iSubId, out string iTargetId))
            {
                pos = iPos; rot = iRot; scl = iScl;
                tag = new PoseSourceTag(PoseSourceKind.Integrated, sub: iSubId, tgt: iTargetId);
                return true;
            }

            if (p.stepPoses != null && p.stepPoses.Count > 0
                && TryPickStepPoseAt(p, viewingSeq, out StepPoseEntry picked))
            {
                pos = PackageJsonUtils.ToVector3(picked.position);
                rot = PackageJsonUtils.ToUnityQuaternion(picked.rotation);
                scl = PackageJsonUtils.ToVector3(picked.scale);
                if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                tag = new PoseSourceTag(PoseSourceKind.StepPose, anchor: picked.stepId);
                return true;
            }

            pos = p.assembledPosition; rot = p.assembledRotation; scl = p.assembledScale;
            tag = new PoseSourceTag(PoseSourceKind.AssembledPosition);
            return true;
        }

        private int ResolvePartFirstAppearance(string partId)
        {
            int best = int.MaxValue;
            foreach (var s in _pkg.steps)
            {
                if (s == null) continue;
                bool hit = false;
                if (s.requiredPartIds != null) foreach (var x in s.requiredPartIds) if (string.Equals(x, partId, StringComparison.Ordinal)) { hit = true; break; }
                if (!hit && s.optionalPartIds != null) foreach (var x in s.optionalPartIds) if (string.Equals(x, partId, StringComparison.Ordinal)) { hit = true; break; }
                if (!hit && s.visualPartIds != null)   foreach (var x in s.visualPartIds)   if (string.Equals(x, partId, StringComparison.Ordinal)) { hit = true; break; }
                if (!hit && !string.IsNullOrEmpty(s.requiredSubassemblyId)
                    && _pkg.TryGetSubassembly(s.requiredSubassemblyId, out var sub)
                    && sub?.partIds != null)
                {
                    foreach (var x in sub.partIds) if (string.Equals(x, partId, StringComparison.Ordinal)) { hit = true; break; }
                }
                if (hit && s.sequenceIndex < best) best = s.sequenceIndex;
            }
            return best == int.MaxValue ? -1 : best;
        }

        private bool TryPickStepPoseAt(PartEditState p, int viewingSeq, out StepPoseEntry picked)
        {
            picked = null;
            if (p.stepPoses == null) return false;
            int bestDist = int.MaxValue;
            foreach (var pose in p.stepPoses)
            {
                if (pose == null) continue;
                // Skip legacy synthetic NO-TASK waypoints left behind in old
                // preview_config.json from before Step 8 of the pose rewrite
                // deleted BakeNoTaskWaypoints. They shouldn't influence the
                // WHAT'S CHANGING diagnostic either — the resolver already
                // ignores them, so surfacing them here would just confuse
                // the author with "ghost" pose sources that don't exist.
                if (!string.IsNullOrEmpty(pose.label)
                    && pose.label.StartsWith(OSE.Content.Loading.MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                    continue;
                int anchorSeq = SeqIndexForStepId(pose.stepId);
                int fromSeq;
                if (string.IsNullOrEmpty(pose.propagateFromStep))
                    fromSeq = anchorSeq >= 0 ? anchorSeq : int.MinValue;
                else
                {
                    int f = SeqIndexForStepId(pose.propagateFromStep);
                    fromSeq = f >= 0 ? f : int.MinValue;
                }
                int throughSeq;
                if (string.IsNullOrEmpty(pose.propagateThroughStep))
                    throughSeq = int.MaxValue;
                else
                {
                    int t = SeqIndexForStepId(pose.propagateThroughStep);
                    throughSeq = t >= 0 ? t : int.MaxValue;
                }
                if (viewingSeq < fromSeq || viewingSeq > throughSeq) continue;
                int dist = anchorSeq >= 0 ? Math.Abs(viewingSeq - anchorSeq) : int.MaxValue / 2;
                if (dist < bestDist) { bestDist = dist; picked = pose; }
            }
            return picked != null;
        }

        private bool TryFindIntegratedForPart(string partId, int viewingSeq,
            out Vector3 pos, out Quaternion rot, out Vector3 scl,
            out string subId, out string targetId)
        {
            pos = Vector3.zero; rot = Quaternion.identity; scl = Vector3.one;
            subId = null; targetId = null;
            var placements = _pkg?.previewConfig?.integratedSubassemblyPlacements;
            if (placements == null || placements.Length == 0) return false;

            // Walk steps whose sequenceIndex ≤ viewingSeq and have a
            // requiredSubassemblyId, find the LATEST one whose integrated
            // placement includes this part. Latest-wins mirrors the runtime
            // closest-anchor semantics for stacking.
            int bestSeq = int.MinValue;
            foreach (var step in _pkg.steps)
            {
                if (step == null || step.sequenceIndex > viewingSeq) continue;
                if (string.IsNullOrEmpty(step.requiredSubassemblyId)) continue;
                string tgt = step.targetIds != null && step.targetIds.Length > 0 ? step.targetIds[0] : null;
                if (string.IsNullOrEmpty(tgt)) continue;
                foreach (var pl in placements)
                {
                    if (pl == null || pl.memberPlacements == null) continue;
                    if (!string.Equals(pl.subassemblyId, step.requiredSubassemblyId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(pl.targetId, tgt, StringComparison.Ordinal)) continue;
                    foreach (var m in pl.memberPlacements)
                    {
                        if (m == null || !string.Equals(m.partId, partId, StringComparison.Ordinal)) continue;
                        if (step.sequenceIndex < bestSeq) continue;
                        bestSeq = step.sequenceIndex;
                        pos = PackageJsonUtils.ToVector3(m.position);
                        rot = m.rotation.IsIdentity
                            ? Quaternion.identity
                            : PackageJsonUtils.ToUnityQuaternion(m.rotation);
                        scl = PackageJsonUtils.ToVector3(m.scale);
                        if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;
                        subId = pl.subassemblyId;
                        targetId = pl.targetId;
                    }
                }
            }
            return bestSeq != int.MinValue;
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
            {
                p.stepPoses[_editingPoseMode].position = PackageJsonUtils.ToFloat3(pos);
            }
            else if (_editAssembledPose)
            {
                p.assembledPosition = pos;
                // Mirror to the PartPreviewPlacement the PoseResolver reads,
                // otherwise the resolver returns the stale previewConfig value
                // on the next SyncAllPartMeshesToActivePose tick and snaps the
                // part back to where it was — "can't edit" symptom.
                if (p.placement != null) p.placement.assembledPosition = PackageJsonUtils.ToFloat3(pos);
            }
            else
            {
                p.startPosition = pos;
                if (p.placement != null) p.placement.startPosition = PackageJsonUtils.ToFloat3(pos);
            }
        }

        private void ApplyRotationToPart(ref PartEditState p, Quaternion rot)
        {
            if (_editingPoseMode >= 0 && p.stepPoses != null && _editingPoseMode < p.stepPoses.Count)
            {
                p.stepPoses[_editingPoseMode].rotation = PackageJsonUtils.ToQuaternion(rot);
            }
            else if (_editAssembledPose)
            {
                p.assembledRotation = rot;
                if (p.placement != null) p.placement.assembledRotation = PackageJsonUtils.ToQuaternion(rot);
            }
            else
            {
                p.startRotation = rot;
                if (p.placement != null) p.placement.startRotation = PackageJsonUtils.ToQuaternion(rot);
            }
        }

        private void ApplyPoseToPart(ref PartEditState p, Vector3 pos, Quaternion rot)
        {
            ApplyPositionToPart(ref p, pos);
            ApplyRotationToPart(ref p, rot);
        }

        /// <summary>
        /// Copies the PartEditState's start/assembled pose fields into the
        /// referenced PartPreviewPlacement so the PoseResolver (which reads
        /// from the placement) sees fresh values without waiting for a save
        /// round-trip. Call after any mutation that writes to
        /// <c>p.startPosition</c> / <c>p.assembledPosition</c> / etc. directly
        /// instead of going through <see cref="ApplyPositionToPart"/> — most
        /// notably the <c>DrawPartBatchPanel</c> multi-select edits.
        /// </summary>
        private static void MirrorPartStateToPlacement(ref PartEditState p)
        {
            if (p.placement == null) return;
            p.placement.startPosition     = PackageJsonUtils.ToFloat3(p.startPosition);
            p.placement.startRotation     = PackageJsonUtils.ToQuaternion(p.startRotation);
            p.placement.startScale        = PackageJsonUtils.ToFloat3(p.startScale);
            p.placement.assembledPosition = PackageJsonUtils.ToFloat3(p.assembledPosition);
            p.placement.assembledRotation = PackageJsonUtils.ToQuaternion(p.assembledRotation);
            p.placement.assembledScale    = PackageJsonUtils.ToFloat3(p.assembledScale);
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
            // Only the SELECTED group's parts are managed — other groups'
            // parts continue to sync normally regardless of pose mode.
            if (_editingGroupPoseMode == PoseModeStart) return false;
            if (_groups == null || _selectedGroupIdx < 0 || _selectedGroupIdx >= _groups.Length) return false;

            ref GroupEditState g = ref _groups[_selectedGroupIdx];
            if (g.def?.partIds == null) return false;
            if (!g.isDirty && !g.hasPlacement) return false;

            // Only cede positioning to the group root when the current step
            // is actually stacking this group (requiredSubassemblyId matches).
            // During mid-group fabrication steps (e.g. bearing place, close
            // halves), each member part's own Start/Assembled toggle must
            // drive its pose — ceding to the root would blank out the part
            // toggle because Sync skips the part entirely.
            StepDefinition curStep = _stepIds != null && _stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            bool currentStepStacksThisGroup = curStep != null
                && g.def != null
                && string.Equals(curStep.requiredSubassemblyId, g.def.id, StringComparison.Ordinal);
            if (!currentStepStacksThisGroup) return false;

            // Only match the SELECTED group's member parts, not all groups
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
                // Parts with no placement and no author stepPoses have
                // nothing to resolve — skip. The PoseTable already excludes
                // parts that lack placements, so TryGetStepAwarePose would
                // return false for them anyway; this is just a fast path.
                if (!_parts[i].hasPlacement
                    && (_parts[i].stepPoses == null || _parts[i].stepPoses.Count == 0))
                    continue;

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

        // Hand-rolled CloneStepPoseEntry was deleted — it repeatedly dropped
        // fields when the type gained propagateFromStep/propagateThroughStep.
        // Callers now use StepPoseEntry.Clone() (MemberwiseClone) so every
        // serialized field is preserved automatically.

        private static List<StepPoseEntry> DeepCopyStepPoseList(List<StepPoseEntry> src)
        {
            if (src == null) return null;
            var dst = new List<StepPoseEntry>(src.Count);
            foreach (var e in src) if (e != null) dst.Add(e.Clone());
            return dst;
        }

        private static List<StepPoseEntry> DeepCopyStepPoses(StepPoseEntry[] src)
        {
            if (src == null) return null;
            var dst = new List<StepPoseEntry>(src.Length);
            foreach (var e in src) if (e != null) dst.Add(e.Clone());
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

            // For single-part selection, defer to Unity's native transform
            // gizmo (Move/Rotate/Transform tool) — it's familiar and more
            // capable than our custom one. The polling block below detects
            // the resulting transform drift and writes it into the active
            // pose mode (after auto-promoting alien parts to NO TASK).
            //
            // For multi-part batch edits we still need our own gizmo so the
            // delta applies uniformly to every selected part, so hide
            // Unity's gizmo only in that case.
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
                (Tools.current == Tool.Move || Tools.current == Tool.Rotate || Tools.current == Tool.Transform) &&
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
                                // Unity-native gizmo drag in progress. If the
                                // part isn't part of this step's tasks, auto-
                                // promote it to NO TASK + waypoint so the
                                // captured drag has somewhere to land.
                                AutoPromoteAlienPartToNoTaskWaypoint(_selectedPartIdx);
                                BeginPartEdit(_selectedPartIdx);
                                ApplyPoseToPart(ref pp, goPos, goRot);
                                pp.isDirty = true;
                                MirrorStepPosesToPreviewConfig(pp);
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

            // Custom Position / Rotation handles only render in BATCH (multi-
            // part) edit mode. Single-part selection uses Unity's native
            // Move/Rotate gizmo (visible because Tools.hidden = false above);
            // the polling block earlier in this method captures its drags.
            // Position handle
            if (isMultiPart)
            {
            EditorGUI.BeginChangeCheck();
            Quaternion posHandleRot = Tools.pivotRotation == PivotRotation.Local ? selWorldRot : Quaternion.identity;
            Vector3    newWorldPos  = Handles.PositionHandle(selWorldPos, posHandleRot);
            if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldPos - selWorldPos).sqrMagnitude > 1e-10f)
            {
                // Author moved a part that isn't part of the current step's
                // tasks — automatically register it as a NO TASK row + waypoint
                // so the move is captured against this step and not silently
                // pushed into the part's startPosition or another mode.
                AutoPromoteAlienPartToNoTaskWaypoint(_selectedPartIdx);

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
                AutoPromoteAlienPartToNoTaskWaypoint(_selectedPartIdx);

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
            } // end if (isMultiPart) — close batch-only gizmo block

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
