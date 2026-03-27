using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;
// PlacementPreviewInfo is now a top-level type in this namespace.

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Place"/> steps.
    /// Owns preview interaction (proximity highlight, click-to-place matching,
    /// drag-to-place execution), snap/flash animation, preview selection pulse,
    /// and required-part emission pulse.  The preview lifecycle (spawn/destroy)
    /// remains on <see cref="PartInteractionBridge"/>; this handler operates
    /// on the shared preview list by reference.
    /// </summary>
    internal sealed class PlaceStepHandler : IStepFamilyHandler
    {
        // ── Constructor dependencies ──
        private readonly PackagePartSpawner _spawner;
        private readonly Func<PreviewSceneSetup> _getSetup;
        private readonly Func<string, GameObject> _findSpawnedPart;
        private readonly Func<string, PartPlacementState> _getPartState;
        private readonly Action<GameObject> _restorePartVisual;
        private readonly Action _resetDragState;
        private readonly List<GameObject> _spawnedPreviews; // shared reference from bridge
        private readonly Func<bool> _isSequentialStep;
        private readonly Func<bool> _advanceSequentialTarget;
        private readonly Action<GameObject> _onPlacementFailed;
        private readonly Action<GameObject> _onPlacementSucceeded;

        // ── Owned state ──
        private GameObject _hoveredPreview;
        private bool _previewHighlighted;
        private string _previewPulsePartId;
        private string[] _requiredPartIdsForStep;

        // Snap animation (list-based for multi-target steps)
        private struct SnapEntry
        {
            public GameObject Part;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public Vector3 TargetScale;
        }
        private readonly List<SnapEntry> _activeSnaps = new();

        // Invalid flash (list-based for multi-target steps)
        private struct FlashEntry
        {
            public GameObject Part;
            public Color OriginalColor;
            public float Timer;
        }
        private readonly List<FlashEntry> _activeFlashes = new();

        // ── Constants ──
        private const float SnapZoneRadius = 0.8f;
        private const float SubassemblySnapZoneRadius = 1.35f;
        private const float SubassemblyDockPreviewRadius = 1.9f;
        private const float PreviewSelectedPulseSpeed = 3.0f;
        private const float RequiredPartPulseSpeed = 0.8f;
        private static readonly Color PreviewReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color PreviewSelectedPulseA = new Color(0.35f, 0.85f, 1.0f, 0.35f);
        private static readonly Color PreviewSelectedPulseB = new Color(0.55f, 1.0f, 0.7f, 0.7f);
        private static readonly Color RequiredPartEmissionA = new Color(0.15f, 0.08f, 0.0f, 1.0f);
        private static readonly Color RequiredPartEmissionB = new Color(0.45f, 0.25f, 0.02f, 1.0f);

        // ── Constructor ──

        public PlaceStepHandler(
            PackagePartSpawner spawner,
            Func<PreviewSceneSetup> getSetup,
            Func<string, GameObject> findSpawnedPart,
            Func<string, PartPlacementState> getPartState,
            Action<GameObject> restorePartVisual,
            Action resetDragState,
            List<GameObject> spawnedPreviews,
            Func<bool> isSequentialStep,
            Func<bool> advanceSequentialTarget,
            Action<GameObject> onPlacementFailed = null,
            Action<GameObject> onPlacementSucceeded = null)
        {
            _spawner = spawner;
            _getSetup = getSetup;
            _findSpawnedPart = findSpawnedPart;
            _getPartState = getPartState;
            _restorePartVisual = restorePartVisual;
            _resetDragState = resetDragState;
            _spawnedPreviews = spawnedPreviews;
            _isSequentialStep = isSequentialStep;
            _advanceSequentialTarget = advanceSequentialTarget;
            _onPlacementFailed = onPlacementFailed;
            _onPlacementSucceeded = onPlacementSucceeded;
        }

        // ====================================================================
        //  IStepFamilyHandler
        // ====================================================================

        public void OnStepActivated(in StepHandlerContext context)
        {
            RefreshRequiredPartIds(context.StepId);
        }

        public bool TryHandlePointerAction(in StepHandlerContext context) => false;

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos) => false;

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            UpdateSnapAnimation(deltaTime);
            UpdateInvalidFlash(deltaTime);
            UpdatePreviewSelectionPulse();
            UpdateRequiredPartPulse();
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
        }

        public void Cleanup()
        {
            ClearPreviewHighlight();
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
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
            if (_spawnedPreviews.Count == 0) return false;

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

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
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

                FlashInvalidSelection(partGo, selectionId);
                _onPlacementFailed?.Invoke(partGo);
                StartPreviewSelectionPulse(selectionId);
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            string matchedTargetId = nearestInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            if (!isSubassemblySelection)
                partController.AttemptPlacement(selectionId, matchedTargetId, result);

            if (!result.IsValid)
            {
                FlashInvalidSelection(partGo, selectionId);
                if (!isSubassemblySelection)
                    partController.SelectPart(selectionId);
                _onPlacementFailed?.Invoke(partGo);
                StartPreviewSelectionPulse(selectionId);
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            OseLog.Info($"[PlaceHandler] Dropped '{selectionId}' in snap zone -> snapping to target.");

            if (isSubassemblySelection)
            {
                if (!subassemblyController.IsPlacementCommitReady(partGo, matchedTargetId))
                {
                    FlashInvalidSelection(partGo, selectionId);
                    _onPlacementFailed?.Invoke(partGo);
                    StartPreviewSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    return;
                }

                if (!subassemblyController.TryApplyPlacement(subassemblyId, matchedTargetId))
                {
                    FlashInvalidSelection(partGo, selectionId);
                    _onPlacementFailed?.Invoke(partGo);
                    StartPreviewSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    return;
                }
            }
            else
            {
                BeginSnapToTarget(partGo, selectionId, matchedTargetId, nearestInfo.transform);
            }

            RemovePreviewForSelection(selectionId);
            _onPlacementSucceeded?.Invoke(partGo);
            CheckStepCompletion(partController, session);
        }

        /// <summary>
        /// Called every drag frame to highlight the nearest matching preview and
        /// trigger auto-snap when the part enters the snap zone.
        /// </summary>
        public void UpdateDragProximity(GameObject partGo, string selectionId, bool isDragging)
        {
            if (partGo == null || _spawnedPreviews.Count == 0)
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
                ClearPreviewHighlight();
                _hoveredPreview = nearest;
                _previewHighlighted = true;
                MaterialHelper.Apply(nearest, "Preview Ready Material", PreviewReadyColor);
            }
            else if (nearest == null && _previewHighlighted)
            {
                ClearPreviewHighlight();
            }

            if (nearestInfo != null && nearest != null && isDragging)
                TryAutoSnapCurrentTarget(partGo, selectionId, nearestInfo, isSubassemblySelection, subassemblyController);
        }

        public void ClearPreviewHighlight()
        {
            if (_previewHighlighted && _hoveredPreview != null)
                MaterialHelper.ApplyPreviewMaterial(_hoveredPreview);
            _hoveredPreview = null;
            _previewHighlighted = false;
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

            _resetDragState();
            AttemptPlacement(partGo, selectionId);
        }

        public void StartPreviewSelectionPulse(string partId)
        {
            StopPreviewSelectionPulse();
            _previewPulsePartId = partId;
        }

        public void StopPreviewSelectionPulse()
        {
            if (_previewPulsePartId == null) return;

            for (int i = 0; i < _spawnedPreviews.Count; i++)
            {
                GameObject preview = _spawnedPreviews[i];
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && info.MatchesSelectionId(_previewPulsePartId))
                    MaterialHelper.ApplyPreviewMaterial(preview);
            }
            _previewPulsePartId = null;
        }

        public void RemoveFromRequiredPartIds(string partId)
        {
            if (_requiredPartIdsForStep == null) return;

            int idx = -1;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                if (string.Equals(_requiredPartIdsForStep[i], partId, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return;

            GameObject partGo = _findSpawnedPart(partId);
            if (partGo != null)
                MaterialHelper.SetEmission(partGo, Color.black);

            if (_requiredPartIdsForStep.Length == 1)
            {
                _requiredPartIdsForStep = null;
                return;
            }

            string[] updated = new string[_requiredPartIdsForStep.Length - 1];
            int j = 0;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                if (i != idx) updated[j++] = _requiredPartIdsForStep[i];
            }
            _requiredPartIdsForStep = updated;
        }

        public void ClearRequiredPartEmission()
        {
            if (_requiredPartIdsForStep == null) return;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                GameObject partGo = _findSpawnedPart(_requiredPartIdsForStep[i]);
                if (partGo != null)
                    MaterialHelper.SetEmission(partGo, Color.black);
            }
        }

        /// <summary>
        /// Returns the world position of the nearest preview target matching the given part ID.
        /// Used by the V2 orchestrator to pivot the camera toward the placement target.
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
            foreach (var preview in _spawnedPreviews)
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

            for (int i = _spawnedPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _spawnedPreviews[i];
                if (preview == null)
                {
                    _spawnedPreviews.RemoveAt(i);
                    continue;
                }

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && info.MatchesSelectionId(selectionId))
                {
                    UnityEngine.Object.Destroy(preview);
                    _spawnedPreviews.RemoveAt(i);
                }
            }
        }

        public void BeginSnapToTarget(GameObject partGo, string selectionId, string targetId, Transform fallback)
        {
            if (partGo == null) return;

            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                if (_activeSnaps[i].Part == partGo)
                    _activeSnaps.RemoveAt(i);
            }

            if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                subassemblyController.IsProxy(partGo) &&
                subassemblyController.TryResolveTargetPose(targetId, out Vector3 proxyPos, out Quaternion proxyRot, out Vector3 proxyScale))
            {
                _activeSnaps.Add(new SnapEntry { Part = partGo, TargetPos = proxyPos, TargetRot = proxyRot, TargetScale = proxyScale });
                return;
            }

            if (TryResolveSnapPose(selectionId, targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale))
            {
                _activeSnaps.Add(new SnapEntry { Part = partGo, TargetPos = pos, TargetRot = rot, TargetScale = scale });
                return;
            }

            if (fallback != null)
            {
                _activeSnaps.Add(new SnapEntry
                {
                    Part = partGo,
                    TargetPos = fallback.localPosition,
                    TargetRot = fallback.localRotation,
                    TargetScale = fallback.localScale
                });
            }
        }

        public bool TryResolveSnapPose(string partId, string targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            if (TryGetPreviewTargetPose(targetId, out pos, out rot, out scale))
                return true;

            TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);

            if (tp != null)
            {
                pos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                rot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;
            }
            else if (pp != null)
            {
                pos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                rot = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;
            }
            else
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
            }

            if (pp != null)
                scale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
            else if (tp != null)
                scale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
            else
                scale = Vector3.one;

            return tp != null || pp != null;
        }

        /// <summary>Whether the hint highlight is currently targeting one of this handler's previews.</summary>
        public bool IsPreviewHighlighted => _previewHighlighted;
        public GameObject HoveredPreview => _hoveredPreview;

        // ====================================================================
        //  Private helpers
        // ====================================================================

        private void ExecuteClickToPlace(string selectionId, GameObject partGo, PlacementPreviewInfo previewInfo)
        {
            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out string subassemblyId);

            string targetId = previewInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            if (!isSubassemblySelection)
                partController.AttemptPlacement(selectionId, targetId, result);

            if (!result.IsValid)
            {
                FlashInvalidSelection(partGo, selectionId);
                return;
            }

            OseLog.Info($"[PlaceHandler] Click-to-place '{selectionId}' at preview target '{targetId}'.");

            if (isSubassemblySelection)
            {
                if (!subassemblyController.IsPlacementCommitReady(partGo, targetId))
                {
                    FlashInvalidSelection(partGo, selectionId);
                    return;
                }

                if (!subassemblyController.TryApplyPlacement(subassemblyId, targetId))
                {
                    FlashInvalidSelection(partGo, selectionId);
                    return;
                }
            }
            else
            {
                BeginSnapToTarget(partGo, selectionId, targetId, previewInfo.transform);
            }

            RemovePreviewForSelection(selectionId);
            _onPlacementSucceeded?.Invoke(partGo);
            CheckStepCompletion(partController, session);
        }

        private void CheckStepCompletion(PartRuntimeController partController, MachineSessionController session)
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
                if (_isSequentialStep())
                {
                    if (_advanceSequentialTarget())
                        session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
                }
                else
                {
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
                }

                return;
            }

            if (_isSequentialStep())
            {
                if (_advanceSequentialTarget())
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
            else if (partController.AreActiveStepRequiredPartsPlaced())
            {
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
        }

        private static bool HasPendingToolActions(MachineSessionController session)
        {
            if (session?.ToolController == null)
                return false;
            if (!session.ToolController.TryGetPrimaryActionSnapshot(out var snapshot))
                return false;
            return snapshot.IsConfigured && !snapshot.IsCompleted;
        }

        private PlacementPreviewInfo RaycastPreviewAtScreen(Vector2 screenPos, string selectionId)
        {
            Camera cam = Camera.main;
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
            Camera cam = Camera.main;
            if (cam == null) return null;

            bool isSubassemblySelection = IsSubassemblySelectionId(selectionId);
            float threshold = StepHandlerConstants.Proximity.GetThreshold(isSubassemblySelection);
            float closestDist = threshold;
            PlacementPreviewInfo best = null;

            for (int i = 0; i < _spawnedPreviews.Count; i++)
            {
                GameObject preview = _spawnedPreviews[i];
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

        private bool TryGetPreviewTargetPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _spawnedPreviews.Count == 0)
                return false;

            PreviewSceneSetup setup = _getSetup();
            Transform previewRoot = setup != null ? setup.PreviewRoot : null;

            for (int i = _spawnedPreviews.Count - 1; i >= 0; i--)
            {
                GameObject preview = _spawnedPreviews[i];
                if (preview == null) continue;

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = preview.transform;
                if (previewRoot != null && tx.parent == previewRoot)
                {
                    // Previews are authored and spawned directly in PreviewRoot local space.
                    // Re-deriving that pose from scaled world transforms creates drift as
                    // the assembly scale changes, so prefer the stored local transform.
                    position = tx.localPosition;
                    rotation = tx.localRotation;
                }
                else if (previewRoot != null)
                {
                    position = previewRoot.InverseTransformPoint(tx.position);
                    rotation = Quaternion.Inverse(previewRoot.rotation) * tx.rotation;
                }
                else
                {
                    position = tx.position;
                    rotation = tx.rotation;
                }

                scale = tx.localScale;
                return true;
            }
            return false;
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

            for (int i = 0; i < _spawnedPreviews.Count; i++)
            {
                GameObject preview = _spawnedPreviews[i];
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

        private void FlashInvalid(GameObject partGo, string partId)
        {
            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                if (_activeFlashes[i].Part == partGo)
                    _activeFlashes.RemoveAt(i);
            }

            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
            Color originalColor = pp != null
                ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, StepHandlerConstants.Colors.InvalidFlash);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", StepHandlerConstants.Colors.InvalidFlash);

            _activeFlashes.Add(new FlashEntry
            {
                Part = partGo,
                OriginalColor = originalColor,
                Timer = StepHandlerConstants.Animation.InvalidFlashDuration
            });
        }

        private void UpdateSnapAnimation(float deltaTime)
        {
            if (_activeSnaps.Count == 0) return;

            float t = StepHandlerConstants.Animation.SnapLerpSpeed * deltaTime;

            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                var snap = _activeSnaps[i];
                if (snap.Part == null) { _activeSnaps.RemoveAt(i); continue; }

                snap.Part.transform.localPosition = Vector3.Lerp(snap.Part.transform.localPosition, snap.TargetPos, t);
                snap.Part.transform.localRotation = Quaternion.Slerp(snap.Part.transform.localRotation, snap.TargetRot, t);
                snap.Part.transform.localScale = Vector3.Lerp(snap.Part.transform.localScale, snap.TargetScale, t);
                if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                    subassemblyController != null &&
                    subassemblyController.IsProxy(snap.Part))
                {
                    subassemblyController.ApplyProxyTransform(snap.Part);
                }

                if (Vector3.Distance(snap.Part.transform.localPosition, snap.TargetPos) < 0.001f)
                {
                    snap.Part.transform.SetLocalPositionAndRotation(snap.TargetPos, snap.TargetRot);
                    snap.Part.transform.localScale = snap.TargetScale;
                    if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var finalController) &&
                        finalController != null &&
                        finalController.IsProxy(snap.Part))
                    {
                        finalController.ApplyProxyTransform(snap.Part);
                    }
                    _activeSnaps.RemoveAt(i);
                }
            }
        }

        private void UpdateInvalidFlash(float deltaTime)
        {
            if (_activeFlashes.Count == 0) return;

            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                var flash = _activeFlashes[i];
                if (flash.Part == null) { _activeFlashes.RemoveAt(i); continue; }

                flash.Timer -= deltaTime;
                if (flash.Timer <= 0f)
                {
                    _restorePartVisual(flash.Part);
                    _activeFlashes.RemoveAt(i);
                }
                else
                {
                    _activeFlashes[i] = flash;
                }
            }
        }

        private void UpdatePreviewSelectionPulse()
        {
            if (_previewPulsePartId == null || _spawnedPreviews.Count == 0)
                return;

            Color pulseColor = ColorPulseHelper.Lerp(PreviewSelectedPulseA, PreviewSelectedPulseB, PreviewSelectedPulseSpeed);

            for (int i = 0; i < _spawnedPreviews.Count; i++)
            {
                GameObject preview = _spawnedPreviews[i];
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !info.MatchesSelectionId(_previewPulsePartId)) continue;
                MaterialHelper.SetMaterialColor(preview, pulseColor);
            }
        }

        private void FlashInvalidSelection(GameObject targetGo, string selectionId)
        {
            if (targetGo == null)
                return;

            if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                subassemblyController.IsProxy(targetGo))
            {
                foreach (GameObject member in subassemblyController.EnumerateMemberParts(targetGo))
                    FlashInvalid(member, member != null ? member.name : selectionId);
                return;
            }

            FlashInvalid(targetGo, selectionId);
        }

        private void UpdateRequiredPartPulse()
        {
            if (_requiredPartIdsForStep == null || _requiredPartIdsForStep.Length == 0)
                return;

            Color emissionColor = ColorPulseHelper.Lerp(RequiredPartEmissionA, RequiredPartEmissionB, RequiredPartPulseSpeed * Mathf.PI * 2f);

            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                string partId = _requiredPartIdsForStep[i];
                GameObject partGo = _findSpawnedPart(partId);
                if (partGo == null) continue;

                PartPlacementState state = _getPartState(partId);
                if (state == PartPlacementState.Selected || state == PartPlacementState.Grabbed
                    || state == PartPlacementState.Inspected)
                {
                    MaterialHelper.SetEmission(partGo, Color.black);
                    continue;
                }

                if (state == PartPlacementState.PlacedVirtually || state == PartPlacementState.Completed)
                    continue;

                MaterialHelper.SetEmission(partGo, emissionColor);
            }
        }

        private void RefreshRequiredPartIds(string stepId)
        {
            _requiredPartIdsForStep = null;

            var package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            if (!step.IsPlacement)
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null || partIds.Length == 0)
                return;

            var pending = new List<string>();
            for (int i = 0; i < partIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(partIds[i])) continue;
                PartPlacementState state = _getPartState(partIds[i]);
                if (state != PartPlacementState.PlacedVirtually && state != PartPlacementState.Completed)
                    pending.Add(partIds[i]);
            }

            _requiredPartIdsForStep = pending.Count > 0 ? pending.ToArray() : null;

            if (_requiredPartIdsForStep != null)
                OseLog.Info($"[PlaceHandler] Required parts for step '{stepId}': {string.Join(", ", _requiredPartIdsForStep)}");
        }
    }
}
