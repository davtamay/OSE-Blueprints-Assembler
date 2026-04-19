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

        // Phase I.h — set-aware drag hinting. When the trainee drags a part
        // that belongs to a currently-open unorderedSet span (size ≥ 2), every
        // other member's ghost pulses soft cyan as a hint "any of these is a
        // valid next placement." The specific proximity-matched ghost keeps
        // the stronger green ready color on top. Tracked as a list so the
        // cleanup path can restore all hint previews to their default
        // material without leaking state across drags or steps.
        private readonly List<GameObject> _hintedPreviews = new List<GameObject>();

        // ── Constants ──
        private const float SnapZoneRadius = 0.8f;
        private const float SubassemblySnapZoneRadius = 1.35f;
        private const float SubassemblyDockPreviewRadius = 1.9f;
        private static readonly Color PreviewReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color PreviewHintColor  = new Color(0.4f, 0.85f, 1.0f, 0.32f);

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

            PlacementPreviewInfo nearestInfo = FindNearestPreviewForSelection(selectionId, partGo.transform.position, out float nearestDist);
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

            RemovePreviewForSelection(selectionId);
            _ctx.HandlePlacementSucceeded(partGo);
            CheckStepCompletion(partController, session);
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
            PlacementPreviewInfo nearestInfo = FindNearestPreviewForSelection(selectionId, partGo.transform.position, out float nearestDist);
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

            if (nearest != null && nearest != _hoveredPreview)
            {
                ClearReadyHighlightOnly();
                _hoveredPreview = nearest;
                _previewHighlighted = true;
                MaterialHelper.Apply(nearest, "Preview Ready Material", PreviewReadyColor);
            }
            else if (nearest == null && _previewHighlighted)
            {
                ClearReadyHighlightOnly();
            }

            // Phase I.h — refresh the set-aware hint pass every frame. Frame-
            // idempotent: computes the current hint set and diffs against the
            // previous, restoring default material on previews that dropped
            // out. Running AFTER the ready-highlight pass so the one "ready"
            // preview doesn't get overwritten with the dim hint color.
            ApplyUnorderedSpanHints(selectionId, nearest);

            if (nearestInfo != null && nearest != null && isDragging)
                TryAutoSnapCurrentTarget(partGo, selectionId, nearestInfo, isSubassemblySelection, subassemblyController);
        }

        /// <summary>
        /// Full cleanup — ready highlight AND set-aware hints. Called from
        /// external drag-reset and step-transition paths.
        /// </summary>
        public void ClearPreviewHighlight()
        {
            ClearReadyHighlightOnly();
            ClearUnorderedSpanHints();
        }

        /// <summary>
        /// Narrow cleanup — just the "ready" (green) highlight. Used inside
        /// <see cref="UpdateDragProximity"/> when the proximity target changes
        /// or vanishes mid-drag: hints should persist across those micro-
        /// transitions so they don't flicker away while the trainee is still
        /// holding the part over other set members.
        /// </summary>
        private void ClearReadyHighlightOnly()
        {
            if (_previewHighlighted && _hoveredPreview != null)
                MaterialHelper.ApplyPreviewMaterial(_hoveredPreview);
            _hoveredPreview = null;
            _previewHighlighted = false;
        }

        /// <summary>
        /// Phase I.h — cyan hint pass. For a Part drag whose part belongs to
        /// a currently-open unorderedSet span of size ≥ 2, every OTHER member's
        /// ghost pulses soft cyan so the trainee sees all valid any-order
        /// destinations at once. The proximity-matched ghost (
        /// <paramref name="readyPreview"/>) is skipped so its stronger green
        /// ready color stays on top.
        ///
        /// Cases that fall through to <see cref="ClearUnorderedSpanHints"/>:
        /// - No active cursor / no open span / singleton span (size 1).
        /// - Dragged part is not in the open span (could happen during
        ///   navigation or if the trainee picks up a part outside the
        ///   current step's taskOrder scope).
        /// - No selectionId.
        /// </summary>
        private void ApplyUnorderedSpanHints(string selectionId, GameObject readyPreview)
        {
            string draggedPartId = string.IsNullOrEmpty(selectionId) ? null : TaskInstanceId.ToPartId(selectionId);
            if (string.IsNullOrEmpty(draggedPartId)) { ClearUnorderedSpanHints(); return; }

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session)) { ClearUnorderedSpanHints(); return; }
            TaskCursor cursor = session?.AssemblyController?.StepController?.CurrentTaskCursor;
            if (cursor == null || cursor.IsComplete || string.IsNullOrEmpty(cursor.CurrentSetLabel))
            {
                // No cursor, or cursor is past the last span, or currently-
                // open span is a singleton (no label) — nothing to hint.
                ClearUnorderedSpanHints();
                return;
            }

            var openPartIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in cursor.OpenTasks)
            {
                if (entry?.kind == "part" && !string.IsNullOrEmpty(entry.id))
                    openPartIds.Add(TaskInstanceId.ToPartId(entry.id));
            }
            if (openPartIds.Count < 2 || !openPartIds.Contains(draggedPartId))
            {
                ClearUnorderedSpanHints();
                return;
            }

            // Build the new hint set (every spawned preview whose partId is in
            // the open span, excluding the ready-highlighted one).
            var newHinted = new List<GameObject>();
            foreach (var preview in _ctx.SpawnedPreviews)
            {
                if (preview == null || preview == readyPreview) continue;
                var info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || string.IsNullOrEmpty(info.PartId)) continue;
                string previewPartId = TaskInstanceId.ToPartId(info.PartId);
                if (!openPartIds.Contains(previewPartId)) continue;

                MaterialHelper.Apply(preview, "Preview Hint Material", PreviewHintColor);
                newHinted.Add(preview);
            }

            // Restore any prior hints that dropped out of the new set (e.g.
            // the ready-preview target changed and the old ready is now just
            // a hint again — or a set member was placed and its ghost was
            // removed from SpawnedPreviews entirely).
            for (int i = 0; i < _hintedPreviews.Count; i++)
            {
                var old = _hintedPreviews[i];
                if (old != null && !newHinted.Contains(old))
                    MaterialHelper.ApplyPreviewMaterial(old);
            }

            _hintedPreviews.Clear();
            _hintedPreviews.AddRange(newHinted);
        }

        private void ClearUnorderedSpanHints()
        {
            for (int i = 0; i < _hintedPreviews.Count; i++)
            {
                var preview = _hintedPreviews[i];
                if (preview != null)
                    MaterialHelper.ApplyPreviewMaterial(preview);
            }
            _hintedPreviews.Clear();
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
