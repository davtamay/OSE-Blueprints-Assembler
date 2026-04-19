using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Place"/> steps.
    /// Owns preview interaction (proximity highlight, click-to-place matching,
    /// and drag-to-place execution).  All per-frame animation — snap lerp,
    /// invalid flash, preview pulse, and required-part emission — is delegated
    /// to <see cref="PlaceStepAnimator"/>.  The preview lifecycle (spawn/destroy)
    /// remains on <see cref="PartInteractionBridge"/>; this handler operates
    /// on the shared preview list by reference.
    /// </summary>
    internal sealed class PlaceStepHandler : IStepFamilyHandler
    {
        // ── Constructor dependency ──
        private readonly IBridgeContext _ctx;

        // ── Owned state ──
        private GameObject _hoveredPreview;
        private bool _previewHighlighted;

        // Phase I.i — for interchangeable unordered-set drags the proximity
        // pass highlights ALL set-member ghosts green (not just the nearest
        // match). This list tracks every preview currently carrying the
        // ready material so drag-end / step-change can restore them all.
        // For non-set drags it holds at most one entry (the single nearest
        // hovered preview), keeping the legacy one-green-ghost behavior.
        private readonly List<GameObject> _highlightedPreviews = new List<GameObject>();

        // ── Constants ──
        private const float SnapZoneRadius = 0.8f;
        private const float SubassemblySnapZoneRadius = 1.35f;
        private const float SubassemblyDockPreviewRadius = 1.9f;
        private static readonly Color PreviewReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);

        // ── Animation sub-system ──
        private readonly PlaceStepAnimator _animator;

        // ── Constructor ──

        public PlaceStepHandler(IBridgeContext context)
        {
            _ctx      = context;
            _animator = new PlaceStepAnimator(context);
        }

        // ====================================================================
        //  IStepFamilyHandler
        // ====================================================================

        public void OnStepActivated(in StepHandlerContext context)
        {
            _animator.RefreshRequiredPartIds(context.StepId);
        }

        public bool TryHandlePointerAction(in StepHandlerContext context) => false;

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos) => false;

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            _animator.Update(deltaTime);
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            _animator.OnStepCompleted();
        }

        public void Cleanup()
        {
            ClearPreviewHighlight();
            _animator.Cleanup();
        }

        // ====================================================================
        //  Public methods (called directly by bridge)
        // ====================================================================

        /// <summary>
        /// Attempts click-to-place: matches the pointer position against preview
        /// targets for the given part and snaps/places if a match is found.
        /// Returns true if placement was consumed.
        /// </summary>
        public bool TryClickToPlace(string selectionId, GameObject partGo, Vector2 screenPos)
        {
            if (_ctx.SpawnedPreviews.Count == 0) return false;

            PlacementPreviewInfo previewInfo = RaycastPreviewAtScreen(screenPos, selectionId);
            if (previewInfo == null)
                previewInfo = FindNearestPreviewByScreenProximity(screenPos, selectionId);
            if (previewInfo == null)
                return false;

            ExecuteClickToPlace(selectionId, partGo, previewInfo);
            return true;
        }

        /// <summary>
        /// Core placement attempt after a drag release or auto-snap.
        /// Validates proximity, snaps or flashes, and checks step completion.
        /// </summary>
        public void AttemptPlacement(GameObject partGo, string selectionId)
        {
            if (partGo == null || string.IsNullOrEmpty(selectionId))
                return;

            ClearPreviewHighlight();

            if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out string subassemblyId);

            // Phase I.i — cross-placement target search. When the dragged
            // part belongs to an interchangeable unorderedSet, any ghost in
            // the set accepts the drop. Non-set drags use the strict per-
            // part match as before.
            bool cross = !isSubassemblySelection &&
                         TryGetInterchangeableSet(selectionId, out HashSet<string> setPartIds);
            float nearestDist;
            PlacementPreviewInfo nearestInfo = cross
                ? FindNearestPreviewInSet(setPartIds, partGo.transform.position, out nearestDist)
                : FindNearestPreviewForSelection(selectionId, partGo.transform.position, out nearestDist);
            float snapZoneRadius = isSubassemblySelection ? SubassemblySnapZoneRadius : SnapZoneRadius;
            bool inSnapZone = nearestInfo != null && nearestDist <= snapZoneRadius;

            if (!inSnapZone)
            {
                string targetId = nearestInfo != null ? nearestInfo.TargetId : "unknown";
                if (!isSubassemblySelection)
                {
                    var invalid = PlacementValidationResult.Invalid(
                        ValidationFailureReason.PositionOutOfTolerance,
                        positionError: nearestDist,
                        rotationError: 0f);
                    partController.AttemptPlacement(selectionId, targetId, invalid);
                    partController.SelectPart(selectionId);
                }

                OseLog.Info($"[PlaceHandler] Dropped '{selectionId}' outside snap zone.");

                _animator.FlashInvalidSelection(partGo, selectionId);
                _ctx.SelectionService?.NotifySelected(partGo);
                _animator.StartPreviewSelectionPulse(selectionId);
                session.AssemblyController?.StepController?.FailAttempt();
                if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxReject))
                    fxReject.PlayHaptic(EffectRole.ErrorHighlight);
                return;
            }

            string matchedTargetId = nearestInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            if (!isSubassemblySelection)
                partController.AttemptPlacement(selectionId, matchedTargetId, result);

            if (!result.IsValid)
            {
                _animator.FlashInvalidSelection(partGo, selectionId);
                if (!isSubassemblySelection)
                    partController.SelectPart(selectionId);
                _ctx.SelectionService?.NotifySelected(partGo);
                StartPreviewSelectionPulse(selectionId);
                session.AssemblyController?.StepController?.FailAttempt();
                if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxInvalid))
                    fxInvalid.PlayHaptic(EffectRole.ErrorHighlight);
                return;
            }

            OseLog.Info($"[PlaceHandler] Dropped '{selectionId}' in snap zone -> snapping to target.");

            if (isSubassemblySelection)
            {
                if (!subassemblyController.IsPlacementCommitReady(partGo, matchedTargetId))
                {
                    _animator.FlashInvalidSelection(partGo, selectionId);
                    _ctx.SelectionService?.NotifySelected(partGo);
                    StartPreviewSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxNotReady))
                        fxNotReady.PlayHaptic(EffectRole.ErrorHighlight);
                    return;
                }

                if (!subassemblyController.TryApplyPlacement(subassemblyId, matchedTargetId))
                {
                    _animator.FlashInvalidSelection(partGo, selectionId);
                    _ctx.SelectionService?.NotifySelected(partGo);
                    StartPreviewSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxNoPlace))
                        fxNoPlace.PlayHaptic(EffectRole.ErrorHighlight);
                    return;
                }
            }
            else
            {
                _animator.BeginSnapToTarget(partGo, selectionId, matchedTargetId, nearestInfo.transform);
            }

            // Phase I.i — under cross-placement the dropped ghost and the
            // dragged part's authored ghost are different. Remove the
            // ghost the user DROPPED ON (the set-member target that was
            // just filled), not the dragged part's authored target.
            if (cross)
                RemovePreviewByTargetId(matchedTargetId);
            else
                RemovePreviewForSelection(selectionId);

            _ctx.HandlePlacementSucceeded(partGo);
            CheckStepCompletion(partController, session);
        }

        /// <summary>
        /// Phase I.i — mirror of <see cref="RemovePreviewForSelection"/>
        /// that matches on <c>PlacementPreviewInfo.TargetId</c> instead of
        /// PartId. Used by cross-placement so the ghost at the drop
        /// location disappears even when the dropped-on target's
        /// associatedPartId differs from the dragged part's id.
        /// </summary>
        private void RemovePreviewByTargetId(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            for (int i = _ctx.SpawnedPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _ctx.SpawnedPreviews[i];
                if (preview == null)
                {
                    _ctx.SpawnedPreviews.RemoveAt(i);
                    continue;
                }
                var info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.Destroy(preview);
                    _ctx.SpawnedPreviews.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Called every drag frame to highlight the nearest matching preview and
        /// trigger auto-snap when the part enters the snap zone.
        /// </summary>
        public void UpdateDragProximity(GameObject partGo, string selectionId, bool isDragging)
        {
            if (partGo == null || _ctx.SpawnedPreviews.Count == 0)
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out _);

            // Phase I.i — interchangeable cross-placement detection. When
            // the dragged part sits inside an authored unorderedSet span
            // whose members all share the same assetRef + assembledScale,
            // widen the proximity match across the whole set so any target
            // in the set can accept any member.
            bool cross = !isSubassemblySelection &&
                         TryGetInterchangeableSet(selectionId, out HashSet<string> setPartIds);

            float nearestDist;
            PlacementPreviewInfo nearestInfo = cross
                ? FindNearestPreviewInSet(setPartIds, partGo.transform.position, out nearestDist)
                : FindNearestPreviewForSelection(selectionId, partGo.transform.position, out nearestDist);

            float snapZoneRadius = isSubassemblySelection ? SubassemblySnapZoneRadius : SnapZoneRadius;
            float previewRadius = isSubassemblySelection ? SubassemblyDockPreviewRadius : snapZoneRadius;
            GameObject nearest = (nearestInfo != null && nearestDist <= previewRadius) ? nearestInfo.gameObject : null;

            if (isSubassemblySelection &&
                nearestInfo != null &&
                nearestDist <= previewRadius &&
                subassemblyController != null)
            {
                if (!subassemblyController.TryApplyPlacementPreview(partGo, nearestInfo.TargetId, nearestDist, previewRadius))
                {
                    ApplySubassemblyDockPreview(partGo, nearestInfo.TargetId, nearestDist, previewRadius, subassemblyController);
                }
            }

            if (cross)
            {
                // All set-member ghosts glow green. Frame-idempotent: build
                // the new highlight set, then restore any prior highlights
                // that dropped out (a set member was just placed and its
                // ghost was removed — ApplyPreviewMaterial on a dead
                // GameObject is a no-op because the list entry has gone
                // null, which the loop filters).
                var newHighlighted = new List<GameObject>();
                foreach (var preview in _ctx.SpawnedPreviews)
                {
                    if (preview == null) continue;
                    var info = preview.GetComponent<PlacementPreviewInfo>();
                    if (info == null || string.IsNullOrEmpty(info.PartId)) continue;
                    if (!setPartIds.Contains(TaskInstanceId.ToPartId(info.PartId))) continue;
                    MaterialHelper.Apply(preview, "Preview Ready Material", PreviewReadyColor);
                    newHighlighted.Add(preview);
                }
                for (int i = 0; i < _highlightedPreviews.Count; i++)
                {
                    var old = _highlightedPreviews[i];
                    if (old != null && !newHighlighted.Contains(old))
                        MaterialHelper.ApplyPreviewMaterial(old);
                }
                _highlightedPreviews.Clear();
                _highlightedPreviews.AddRange(newHighlighted);
                _hoveredPreview = nearest;     // still tracked for auto-snap
                _previewHighlighted = _highlightedPreviews.Count > 0;
            }
            else
            {
                // Legacy single-green-highlight for non-set drags.
                if (nearest != null && nearest != _hoveredPreview)
                {
                    ClearAllHighlights();
                    _hoveredPreview = nearest;
                    _previewHighlighted = true;
                    MaterialHelper.Apply(nearest, "Preview Ready Material", PreviewReadyColor);
                    _highlightedPreviews.Add(nearest);
                }
                else if (nearest == null && _previewHighlighted)
                {
                    ClearAllHighlights();
                }
            }

            if (nearestInfo != null && nearest != null && isDragging)
                TryAutoSnapCurrentTarget(partGo, selectionId, nearestInfo, isSubassemblySelection, subassemblyController);
        }

        public void ClearPreviewHighlight()
        {
            ClearAllHighlights();
        }

        private void ClearAllHighlights()
        {
            for (int i = 0; i < _highlightedPreviews.Count; i++)
            {
                var p = _highlightedPreviews[i];
                if (p != null)
                    MaterialHelper.ApplyPreviewMaterial(p);
            }
            _highlightedPreviews.Clear();
            _hoveredPreview = null;
            _previewHighlighted = false;
        }

        /// <summary>
        /// Phase I.i — returns true iff <paramref name="selectionId"/>'s
        /// partId is in a currently-open unorderedSet span (size ≥ 2) AND
        /// every member of that span shares the same <c>assetRef</c> and
        /// <c>assembledScale</c>. Under those conditions the runtime treats
        /// the set's parts as physically interchangeable: any part can snap
        /// to any target in the set. All three gates must hold; a single
        /// failure falls back to strict per-part matching.
        /// </summary>
        private static bool TryGetInterchangeableSet(string selectionId, out HashSet<string> openPartIds)
        {
            openPartIds = null;
            if (string.IsNullOrEmpty(selectionId)) return false;
            string draggedPartId = TaskInstanceId.ToPartId(selectionId);
            if (string.IsNullOrEmpty(draggedPartId)) return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session)) return false;
            TaskCursor cursor = session?.AssemblyController?.StepController?.CurrentTaskCursor;
            if (cursor == null || cursor.IsComplete || string.IsNullOrEmpty(cursor.CurrentSetLabel))
                return false;

            var partIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in cursor.OpenTasks)
            {
                if (entry?.kind == "part" && !string.IsNullOrEmpty(entry.id))
                    partIds.Add(TaskInstanceId.ToPartId(entry.id));
            }
            if (partIds.Count < 2 || !partIds.Contains(draggedPartId)) return false;

            var package = session.Package;
            if (package == null) return false;
            var placements = package.previewConfig?.partPlacements;
            if (placements == null) return false;

            string commonAssetRef = null;
            bool scaleSet = false;
            SceneFloat3 commonScale = default;
            foreach (var pid in partIds)
            {
                if (!package.TryGetPart(pid, out var part) || part == null) return false;
                if (string.IsNullOrEmpty(part.assetRef)) return false;
                if (commonAssetRef == null) commonAssetRef = part.assetRef;
                else if (!string.Equals(commonAssetRef, part.assetRef, StringComparison.Ordinal)) return false;

                // Find assembledScale for this part
                SceneFloat3 scale = default;
                bool found = false;
                foreach (var entry in placements)
                {
                    if (entry != null && string.Equals(entry.partId, pid, StringComparison.Ordinal))
                    {
                        scale = entry.assembledScale;
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
                if (!scaleSet) { commonScale = scale; scaleSet = true; }
                else if (!ScaleApprox(commonScale, scale)) return false;
            }

            openPartIds = partIds;
            return true;
        }

        private static bool ScaleApprox(SceneFloat3 a, SceneFloat3 b)
        {
            const float eps = 0.0001f;
            return Mathf.Abs(a.x - b.x) < eps
                && Mathf.Abs(a.y - b.y) < eps
                && Mathf.Abs(a.z - b.z) < eps;
        }

        /// <summary>
        /// Phase I.i — proximity-matches any preview whose partId is in the
        /// interchangeable set. Mirror of <see cref="FindNearestPreviewForSelection"/>
        /// with a widened membership check so cross-placement can pick the
        /// nearest set-target, not just the dragged part's authored target.
        /// </summary>
        private PlacementPreviewInfo FindNearestPreviewInSet(HashSet<string> setPartIds, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (setPartIds == null || setPartIds.Count == 0) return null;

            PlacementPreviewInfo nearest = null;
            foreach (var preview in _ctx.SpawnedPreviews)
            {
                if (preview == null) continue;
                var info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || string.IsNullOrEmpty(info.PartId)) continue;
                if (!setPartIds.Contains(TaskInstanceId.ToPartId(info.PartId))) continue;

                float dist = Vector3.Distance(worldPos, preview.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = info;
                }
            }
            return nearest;
        }

        private void TryAutoSnapCurrentTarget(
            GameObject partGo,
            string selectionId,
            PlacementPreviewInfo nearestInfo,
            bool isSubassemblySelection,
            ISubassemblyPlacementService subassemblyController)
        {
            if (partGo == null || nearestInfo == null)
                return;

            bool commitReady = !isSubassemblySelection ||
                               subassemblyController == null ||
                               subassemblyController.IsPlacementCommitReady(partGo, nearestInfo.TargetId);
            if (!commitReady)
                return;

            OseLog.Info($"[PlaceHandler] Auto-snap: '{selectionId}' committed in snap zone of '{nearestInfo.TargetId}'.");

            if (isSubassemblySelection &&
                subassemblyController != null &&
                subassemblyController.TryResolveTargetPose(nearestInfo.TargetId, out Vector3 proxyPos, out Quaternion proxyRot, out Vector3 proxyScale))
            {
                partGo.transform.SetLocalPositionAndRotation(proxyPos, proxyRot);
                partGo.transform.localScale = proxyScale;
                subassemblyController.ApplyProxyTransform(partGo);
            }
            else
            {
                partGo.transform.localPosition = nearestInfo.transform.localPosition;
                partGo.transform.localRotation = nearestInfo.transform.localRotation;

                if (TryResolveSnapPose(selectionId, nearestInfo.TargetId, out _, out _, out Vector3 snapScale))
                    partGo.transform.localScale = snapScale;
                else
                    partGo.transform.localScale = nearestInfo.transform.localScale;
            }

            _ctx.ResetDragState();
            AttemptPlacement(partGo, selectionId);
        }

        public void StartPreviewSelectionPulse(string partId)  => _animator.StartPreviewSelectionPulse(partId);
        public void StopPreviewSelectionPulse()               => _animator.StopPreviewSelectionPulse();
        public void RemoveFromRequiredPartIds(string partId)  => _animator.RemoveFromRequiredPartIds(partId);
        public void ClearRequiredPartEmission()               => _animator.ClearRequiredPartEmission();

        /// <summary>
        /// Returns the world position of the nearest preview target matching the given part ID.
        /// Used by the orchestrator to pivot the camera toward the placement target.
        /// </summary>
        public bool TryGetPreviewWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            PlacementPreviewInfo preview = FindNearestPreviewForSelection(partId, Vector3.zero, out _);
            if (preview == null) return false;
            worldPos = preview.transform.position;
            return true;
        }

        public PlacementPreviewInfo FindNearestPreviewForSelection(string selectionId, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (string.IsNullOrEmpty(selectionId)) return null;

            PlacementPreviewInfo nearest = null;
            foreach (var preview in _ctx.SpawnedPreviews)
            {
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !info.MatchesSelectionId(selectionId)) continue;

                float dist = Vector3.Distance(worldPos, preview.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = info;
                }
            }
            return nearest;
        }

        public void RemovePreviewForPart(string partId)
        {
            RemovePreviewForSelection(partId);
        }

        public void RemovePreviewForSelection(string selectionId)
        {
            if (string.IsNullOrEmpty(selectionId)) return;

            for (int i = _ctx.SpawnedPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _ctx.SpawnedPreviews[i];
                if (preview == null)
                {
                    _ctx.SpawnedPreviews.RemoveAt(i);
                    continue;
                }

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && info.MatchesSelectionId(selectionId))
                {
                    UnityEngine.Object.Destroy(preview);
                    _ctx.SpawnedPreviews.RemoveAt(i);
                }
            }
        }

        public void BeginSnapToTarget(GameObject partGo, string selectionId, string targetId, Transform fallback)
            => _animator.BeginSnapToTarget(partGo, selectionId, targetId, fallback);

        public bool TryResolveSnapPose(string partId, string targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale)
            => _animator.TryResolveSnapPose(partId, targetId, out pos, out rot, out scale);

        /// <summary>Whether the hint highlight is currently targeting one of this handler's previews.</summary>
        public bool IsPreviewHighlighted => _previewHighlighted;
        public GameObject HoveredPreview => _hoveredPreview;

        // ====================================================================
        //  Private helpers
        // ====================================================================

        private void ExecuteClickToPlace(string selectionId, GameObject partGo, PlacementPreviewInfo previewInfo)
        {
            if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out string subassemblyId);

            string targetId = previewInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            if (!isSubassemblySelection)
                partController.AttemptPlacement(selectionId, targetId, result);

            if (!result.IsValid)
            {
                _animator.FlashInvalidSelection(partGo, selectionId);
                if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxClickInvalid))
                    fxClickInvalid.PlayHaptic(EffectRole.ErrorHighlight);
                return;
            }

            OseLog.Info($"[PlaceHandler] Click-to-place '{selectionId}' at preview target '{targetId}'.");

            if (isSubassemblySelection)
            {
                if (!subassemblyController.IsPlacementCommitReady(partGo, targetId))
                {
                    _animator.FlashInvalidSelection(partGo, selectionId);
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxClickNotReady))
                        fxClickNotReady.PlayHaptic(EffectRole.ErrorHighlight);
                    return;
                }

                if (!subassemblyController.TryApplyPlacement(subassemblyId, targetId))
                {
                    _animator.FlashInvalidSelection(partGo, selectionId);
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxClickNoPlace))
                        fxClickNoPlace.PlayHaptic(EffectRole.ErrorHighlight);
                    return;
                }
            }
            else
            {
                _animator.BeginSnapToTarget(partGo, selectionId, targetId, previewInfo.transform);
            }

            RemovePreviewForSelection(selectionId);
            _ctx.HandlePlacementSucceeded(partGo);
            CheckStepCompletion(partController, session);
        }

        private void CheckStepCompletion(IPartRuntimeController partController, IMachineSessionController session)
        {
            // Don't complete the step if there are pending tool actions (e.g. clamp).
            // Mixed placement+tool steps must wait for both parts AND tool actions.
            if (HasPendingToolActions(session))
                return;

            string currentStepId = session.AssemblyController?.StepController?.CurrentStepDefinition?.id;
            if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                !string.IsNullOrWhiteSpace(currentStepId) &&
                subassemblyController.IsActiveStepPlacementSatisfied(currentStepId))
            {
                if ((_ctx.PreviewManager?.IsSequentialStep ?? false))
                {
                    if ((_ctx.PreviewManager?.AdvanceSequentialTarget() ?? true))
                    {
                        if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxSeqSub))
                            fxSeqSub.PlayHaptic(EffectRole.HapticFeedback);
                        session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
                    }
                }
                else
                {
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxSub))
                        fxSub.PlayHaptic(EffectRole.HapticFeedback);
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
                }

                return;
            }

            if ((_ctx.PreviewManager?.IsSequentialStep ?? false))
            {
                if ((_ctx.PreviewManager?.AdvanceSequentialTarget() ?? true))
                {
                    if (ServiceRegistry.TryGet<IEffectPlayer>(out var fxSeq))
                        fxSeq.PlayHaptic(EffectRole.HapticFeedback);
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
                }
            }
            else if (partController.AreActiveStepRequiredPartsPlaced())
            {
                if (ServiceRegistry.TryGet<IEffectPlayer>(out var fx))
                    fx.PlayHaptic(EffectRole.HapticFeedback);
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
        }

        private static bool HasPendingToolActions(IMachineSessionController session)
        {
            if (session?.ToolController == null)
                return false;
            if (!session.ToolController.TryGetPrimaryActionSnapshot(out var snapshot))
                return false;
            return snapshot.IsConfigured && !snapshot.IsCompleted;
        }

        private PlacementPreviewInfo RaycastPreviewAtScreen(Vector2 screenPos, string selectionId)
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return null;

            PlacementPreviewInfo best = null;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                PlacementPreviewInfo info = FindPreviewInfoFromHit(hits[i].transform);
                if (info == null)
                    continue;

                if (!string.IsNullOrEmpty(selectionId) && !info.MatchesSelectionId(selectionId))
                    continue;

                if (hits[i].distance < bestDistance)
                {
                    best = info;
                    bestDistance = hits[i].distance;
                }
            }

            return best;
        }

        private static PlacementPreviewInfo FindPreviewInfoFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                PlacementPreviewInfo info = hitTransform.GetComponent<PlacementPreviewInfo>();
                if (info != null) return info;
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        private PlacementPreviewInfo FindNearestPreviewByScreenProximity(Vector2 screenPos, string selectionId)
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null) return null;

            bool isSubassemblySelection = IsSubassemblySelectionId(selectionId);
            float threshold = StepHandlerConstants.Proximity.GetThreshold(isSubassemblySelection);
            float closestDist = threshold;
            PlacementPreviewInfo best = null;

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !info.MatchesSelectionId(selectionId)) continue;

                Vector3 sp = cam.WorldToScreenPoint(preview.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    best = info;
                }
            }
            return best;
        }

        private bool TryGetSubassemblySelection(
            GameObject targetGo,
            out ISubassemblyPlacementService subassemblyController,
            out string subassemblyId)
        {
            subassemblyController = null;
            subassemblyId = null;

            return targetGo != null &&
                   ServiceRegistry.TryGet<ISubassemblyPlacementService>(out subassemblyController) &&
                   subassemblyController != null &&
                   subassemblyController.IsProxy(targetGo) &&
                   subassemblyController.TryGetSubassemblyId(targetGo, out subassemblyId);
        }

        private bool IsSubassemblySelectionId(string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return false;

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null)
                    continue;

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && info.MatchesSubassembly(selectionId))
                    return true;
            }

            return false;
        }

        private static void ApplySubassemblyDockPreview(
            GameObject partGo,
            string targetId,
            float nearestDist,
            float previewRadius,
            ISubassemblyPlacementService subassemblyController)
        {
            if (partGo == null ||
                subassemblyController == null ||
                string.IsNullOrWhiteSpace(targetId) ||
                !subassemblyController.TryResolveTargetPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
            {
                return;
            }

            float closeness = 1f - Mathf.Clamp01(nearestDist / Mathf.Max(previewRadius, 0.001f));
            if (closeness <= 0f)
                return;

            float rotationBlend = Mathf.Lerp(0.06f, 0.32f, closeness);
            float positionBlend = Mathf.Lerp(0.03f, 0.14f, closeness);
            float scaleBlend = Mathf.Lerp(0.02f, 0.1f, closeness);

            partGo.transform.localRotation = Quaternion.Slerp(partGo.transform.localRotation, targetRot, rotationBlend);
            partGo.transform.localPosition = Vector3.Lerp(partGo.transform.localPosition, targetPos, positionBlend);
            partGo.transform.localScale = Vector3.Lerp(partGo.transform.localScale, targetScale, scaleBlend);
            subassemblyController.ApplyProxyTransform(partGo);
        }

    }
}
