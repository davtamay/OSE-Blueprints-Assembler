using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;
using GhostPlacementInfo = OSE.UI.Root.PartInteractionBridge.GhostPlacementInfo;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Place"/> steps.
    /// Owns ghost interaction (proximity highlight, click-to-place matching,
    /// drag-to-place execution), snap/flash animation, ghost selection pulse,
    /// and required-part emission pulse.  The ghost lifecycle (spawn/destroy)
    /// remains on <see cref="PartInteractionBridge"/>; this handler operates
    /// on the shared ghost list by reference.
    /// </summary>
    internal sealed class PlaceStepHandler : IStepFamilyHandler
    {
        // â"€â"€ Constructor dependencies â"€â"€
        private readonly PackagePartSpawner _spawner;
        private readonly Func<PreviewSceneSetup> _getSetup;
        private readonly Func<string, GameObject> _findSpawnedPart;
        private readonly Func<string, PartPlacementState> _getPartState;
        private readonly Action<GameObject> _restorePartVisual;
        private readonly Action _resetDragState;
        private readonly List<GameObject> _spawnedGhosts; // shared reference from bridge
        private readonly Func<bool> _isSequentialStep;
        private readonly Func<bool> _advanceSequentialTarget;
        private readonly Action<GameObject> _onPlacementFailed;
        private readonly Action<GameObject> _onPlacementSucceeded;

        // â"€â"€ Owned state â"€â"€
        private GameObject _hoveredGhost;
        private bool _ghostHighlighted;
        private string _ghostPulsePartId;
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

        // â"€â"€ Constants â"€â"€
        private const float SnapZoneRadius = 0.8f;
        private const float SubassemblySnapZoneRadius = 1.35f;
        private const float SubassemblyDockPreviewRadius = 1.9f;
        private const float SnapLerpSpeed = 12f;
        private const float InvalidFlashDuration = 0.3f;
        private const float ScreenProximityDesktop = 120f;
        private const float ScreenProximityMobile = 180f;
        private const float SubassemblyScreenProximityDesktop = 220f;
        private const float SubassemblyScreenProximityMobile = 300f;
        private const float GhostSelectedPulseSpeed = 3.0f;
        private const float RequiredPartPulseSpeed = 0.8f;

        // â"€â"€ Colors â"€â"€
        private static readonly Color InvalidFlashColor = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        private static readonly Color GhostReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color GhostSelectedPulseA = new Color(0.35f, 0.85f, 1.0f, 0.35f);
        private static readonly Color GhostSelectedPulseB = new Color(0.55f, 1.0f, 0.7f, 0.7f);
        private static readonly Color RequiredPartEmissionA = new Color(0.15f, 0.08f, 0.0f, 1.0f);
        private static readonly Color RequiredPartEmissionB = new Color(0.45f, 0.25f, 0.02f, 1.0f);

        // â"€â"€ Constructor â"€â"€

        public PlaceStepHandler(
            PackagePartSpawner spawner,
            Func<PreviewSceneSetup> getSetup,
            Func<string, GameObject> findSpawnedPart,
            Func<string, PartPlacementState> getPartState,
            Action<GameObject> restorePartVisual,
            Action resetDragState,
            List<GameObject> spawnedGhosts,
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
            _spawnedGhosts = spawnedGhosts;
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
            UpdateGhostSelectionPulse();
            UpdateRequiredPartPulse();
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
        }

        public void Cleanup()
        {
            ClearGhostHighlight();
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
        }

        // ====================================================================
        //  Public methods (called directly by bridge)
        // ====================================================================

        /// <summary>
        /// Attempts click-to-place: matches the pointer position against ghost
        /// targets for the given part and snaps/places if a match is found.
        /// Returns true if placement was consumed.
        /// </summary>
        public bool TryClickToPlace(string selectionId, GameObject partGo, Vector2 screenPos)
        {
            if (_spawnedGhosts.Count == 0) return false;

            GhostPlacementInfo ghostInfo = RaycastGhostAtScreen(screenPos, selectionId);
            if (ghostInfo == null)
                ghostInfo = FindNearestGhostByScreenProximity(screenPos, selectionId);
            if (ghostInfo == null)
                return false;

            ExecuteClickToPlace(selectionId, partGo, ghostInfo);
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

            ClearGhostHighlight();

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out string subassemblyId);

            GhostPlacementInfo nearestInfo = FindNearestGhostForSelection(selectionId, partGo.transform.position, out float nearestDist);
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
                StartGhostSelectionPulse(selectionId);
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
                StartGhostSelectionPulse(selectionId);
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
                    StartGhostSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    return;
                }

                if (!subassemblyController.TryApplyPlacement(subassemblyId, matchedTargetId))
                {
                    FlashInvalidSelection(partGo, selectionId);
                    _onPlacementFailed?.Invoke(partGo);
                    StartGhostSelectionPulse(selectionId);
                    session.AssemblyController?.StepController?.FailAttempt();
                    return;
                }
            }
            else
            {
                BeginSnapToTarget(partGo, selectionId, matchedTargetId, nearestInfo.transform);
            }

            RemoveGhostForSelection(selectionId);
            _onPlacementSucceeded?.Invoke(partGo);
            CheckStepCompletion(partController, session);
        }

        /// <summary>
        /// Called every drag frame to highlight the nearest matching ghost and
        /// trigger auto-snap when the part enters the snap zone.
        /// </summary>
        public void UpdateDragProximity(GameObject partGo, string selectionId, bool isDragging)
        {
            if (partGo == null || _spawnedGhosts.Count == 0)
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out _);
            GhostPlacementInfo nearestInfo = FindNearestGhostForSelection(selectionId, partGo.transform.position, out float nearestDist);
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

            if (nearest != null && nearest != _hoveredGhost)
            {
                ClearGhostHighlight();
                _hoveredGhost = nearest;
                _ghostHighlighted = true;
                MaterialHelper.Apply(nearest, "Ghost Ready Material", GhostReadyColor);
            }
            else if (nearest == null && _ghostHighlighted)
            {
                ClearGhostHighlight();
            }

            if (nearestInfo != null && nearest != null && isDragging)
                TryAutoSnapCurrentTarget(partGo, selectionId, nearestInfo, isSubassemblySelection, subassemblyController);
        }

        public void ClearGhostHighlight()
        {
            if (_ghostHighlighted && _hoveredGhost != null)
                MaterialHelper.ApplyGhost(_hoveredGhost);
            _hoveredGhost = null;
            _ghostHighlighted = false;
        }

        private void TryAutoSnapCurrentTarget(
            GameObject partGo,
            string selectionId,
            GhostPlacementInfo nearestInfo,
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

        public void StartGhostSelectionPulse(string partId)
        {
            StopGhostSelectionPulse();
            _ghostPulsePartId = partId;
        }

        public void StopGhostSelectionPulse()
        {
            if (_ghostPulsePartId == null) return;

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info != null && info.MatchesSelectionId(_ghostPulsePartId))
                    MaterialHelper.ApplyGhost(ghost);
            }
            _ghostPulsePartId = null;
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
        /// Returns the world position of the nearest ghost target matching the given part ID.
        /// Used by the V2 orchestrator to pivot the camera toward the placement target.
        /// </summary>
        public bool TryGetGhostWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            GhostPlacementInfo ghost = FindNearestGhostForSelection(partId, Vector3.zero, out _);
            if (ghost == null) return false;
            worldPos = ghost.transform.position;
            return true;
        }

        public GhostPlacementInfo FindNearestGhostForSelection(string selectionId, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (string.IsNullOrEmpty(selectionId)) return null;

            GhostPlacementInfo nearest = null;
            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesSelectionId(selectionId)) continue;

                float dist = Vector3.Distance(worldPos, ghost.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = info;
                }
            }
            return nearest;
        }

        public void RemoveGhostForPart(string partId)
        {
            RemoveGhostForSelection(partId);
        }

        public void RemoveGhostForSelection(string selectionId)
        {
            if (string.IsNullOrEmpty(selectionId)) return;

            for (int i = _spawnedGhosts.Count - 1; i >= 0; i--)
            {
                var ghost = _spawnedGhosts[i];
                if (ghost == null)
                {
                    _spawnedGhosts.RemoveAt(i);
                    continue;
                }

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info != null && info.MatchesSelectionId(selectionId))
                {
                    UnityEngine.Object.Destroy(ghost);
                    _spawnedGhosts.RemoveAt(i);
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
            if (TryGetGhostTargetPose(targetId, out pos, out rot, out scale))
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

        /// <summary>Whether the hint highlight is currently targeting one of this handler's ghosts.</summary>
        public bool IsGhostHighlighted => _ghostHighlighted;
        public GameObject HoveredGhost => _hoveredGhost;

        // ====================================================================
        //  Private helpers
        // ====================================================================

        private void ExecuteClickToPlace(string selectionId, GameObject partGo, GhostPlacementInfo ghostInfo)
        {
            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            bool isSubassemblySelection = TryGetSubassemblySelection(partGo, out var subassemblyController, out string subassemblyId);

            string targetId = ghostInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            if (!isSubassemblySelection)
                partController.AttemptPlacement(selectionId, targetId, result);

            if (!result.IsValid)
            {
                FlashInvalidSelection(partGo, selectionId);
                return;
            }

            OseLog.Info($"[PlaceHandler] Click-to-place '{selectionId}' at ghost target '{targetId}'.");

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
                BeginSnapToTarget(partGo, selectionId, targetId, ghostInfo.transform);
            }

            RemoveGhostForSelection(selectionId);
            _onPlacementSucceeded?.Invoke(partGo);
            CheckStepCompletion(partController, session);
        }

        private void CheckStepCompletion(PartRuntimeController partController, MachineSessionController session)
        {
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

        private GhostPlacementInfo RaycastGhostAtScreen(Vector2 screenPos, string selectionId)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return null;

            GhostPlacementInfo best = null;
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                GhostPlacementInfo info = FindGhostInfoFromHit(hits[i].transform);
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

        private static GhostPlacementInfo FindGhostInfoFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                GhostPlacementInfo info = hitTransform.GetComponent<GhostPlacementInfo>();
                if (info != null) return info;
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        private GhostPlacementInfo FindNearestGhostByScreenProximity(Vector2 screenPos, string selectionId)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            bool isSubassemblySelection = IsSubassemblySelectionId(selectionId);
            float threshold = Application.isMobilePlatform
                ? (isSubassemblySelection ? SubassemblyScreenProximityMobile : ScreenProximityMobile)
                : (isSubassemblySelection ? SubassemblyScreenProximityDesktop : ScreenProximityDesktop);
            float closestDist = threshold;
            GhostPlacementInfo best = null;

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesSelectionId(selectionId)) continue;

                Vector3 sp = cam.WorldToScreenPoint(ghost.transform.position);
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

        private bool TryGetGhostTargetPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _spawnedGhosts.Count == 0)
                return false;

            PreviewSceneSetup setup = _getSetup();
            Transform previewRoot = setup != null ? setup.PreviewRoot : null;

            for (int i = _spawnedGhosts.Count - 1; i >= 0; i--)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = ghost.transform;
                if (previewRoot != null && tx.parent == previewRoot)
                {
                    // Ghosts are authored and spawned directly in PreviewRoot local space.
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

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null)
                    continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
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
                MaterialHelper.ApplyTint(partGo, InvalidFlashColor);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", InvalidFlashColor);

            _activeFlashes.Add(new FlashEntry
            {
                Part = partGo,
                OriginalColor = originalColor,
                Timer = InvalidFlashDuration
            });
        }

        private void UpdateSnapAnimation(float deltaTime)
        {
            if (_activeSnaps.Count == 0) return;

            float t = SnapLerpSpeed * deltaTime;

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

        private void UpdateGhostSelectionPulse()
        {
            if (_ghostPulsePartId == null || _spawnedGhosts.Count == 0)
                return;

            Color pulseColor = ColorPulseHelper.Lerp(GhostSelectedPulseA, GhostSelectedPulseB, GhostSelectedPulseSpeed);

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesSelectionId(_ghostPulsePartId)) continue;
                MaterialHelper.SetMaterialColor(ghost, pulseColor);
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
