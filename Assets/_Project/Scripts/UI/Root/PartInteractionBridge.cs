using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Input;
using OSE.Interaction;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace OSE.UI.Root
{
    /// <summary>
    /// Play-mode bridge that connects runtime events to scene objects.
    /// Handles pointer-based interaction (mouse and touch): click-to-select,
    /// drag-to-place with tolerance-based validation and snap-to-target,
    /// ghost part spawning, visual feedback, and step-completion repositioning.
    ///
    /// Requires <see cref="PackagePartSpawner"/> on the same GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PackagePartSpawner))]
    public sealed class PartInteractionBridge : MonoBehaviour
    {
        private static readonly Color SelectedPartColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly Color GrabbedPartColor = new Color(1.0f, 0.65f, 0.1f, 1.0f);
        private static readonly Color HoveredPartColor = new Color(0.40f, 0.85f, 1.0f, 1.0f);
        private static readonly Color CompletedPartColor = new Color(0.3f, 0.9f, 0.4f, 1.0f);
        private static readonly Color InvalidFlashColor = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        private static readonly Color GhostReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color HintHighlightColorA = new Color(0.95f, 0.85f, 0.2f, 0.4f);
        private static readonly Color HintHighlightColorB = new Color(1.0f, 0.95f, 0.35f, 0.7f);

        private const float DragThresholdPixels = 5f;
        private const float SnapLerpSpeed = 12f;
        private const float InvalidFlashDuration = 0.3f;
        private const float SnapZoneRadius = 0.8f; // generous radius in Unity units
        private const float ScrollDepthSpeed = 0.5f; // units per scroll tick
        private const float PinchDepthSpeed = 0.02f; // units per pixel of pinch delta
        private const float DepthAdjustSpeed = 0.01f; // units per pixel in shift depth mode
        private const float MinDragRayDistance = 0.05f;
        private const float DragViewportMargin = 0.03f;
        private const float DragFloorEpsilon = 0.001f;
        private const float HintHighlightDuration = 6f;
        private const float HintHighlightPulseSpeed = 4f;

        private PackagePartSpawner _spawner;
        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _spawnedGhosts = new List<GameObject>();
        [SerializeField] private InputActionRouter _actionRouter;
        [SerializeField] private SelectionService _selectionService;
        private bool _suppressSelectionEvents;
        private GameObject _pendingSelectPart;
        private Vector2 _pointerDownScreenPos;
        private Camera _pointerDownCamera;

        // Drag state
        private GameObject _draggedPart;
        private string _draggedPartId;
        private Vector2 _dragScreenStart;
        private Camera _dragCamera;
        private bool _isDragging;
        private bool _pointerDown;
        private float _dragRayDistance;
        private float _lastPinchDistance;
        private float _lastPointerY;
        private bool _isDepthAdjustMode;
        private Vector2 _depthAdjustScreenAnchor;
        private GameObject _hoveredGhost;
        private bool _ghostHighlighted;
        private GameObject _hintGhost;
        private float _hintHighlightUntil;
        private HintWorldCanvas _hintWorldCanvas;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private GameObject _hoveredPart;

        // Snap animation (list-based for multi-target steps)
        private struct SnapEntry
        {
            public GameObject Part;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public Vector3 TargetScale;
        }
        private readonly List<SnapEntry> _activeSnaps = new List<SnapEntry>();

        // Invalid flash (list-based for multi-target steps)
        private struct FlashEntry
        {
            public GameObject Part;
            public Color OriginalColor;
            public float Timer;
        }
        private readonly List<FlashEntry> _activeFlashes = new List<FlashEntry>();

        // ── Lifecycle ──

        private void OnEnable()
        {
            _spawner = GetComponent<PackagePartSpawner>();
            _setup = GetComponent<PreviewSceneSetup>();
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<HintRequested>(HandleHintRequested);

            EnsureInputWiring();

            if (_actionRouter != null)
            {
                _actionRouter.OnAction += HandleCanonicalAction;
            }

            if (_selectionService != null)
            {
                _selectionService.OnSelected += HandleSelectionServiceSelected;
                _selectionService.OnDeselected += HandleSelectionServiceDeselected;
                _selectionService.OnInspected += HandleSelectionServiceInspected;
            }
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Unsubscribe<HintRequested>(HandleHintRequested);

            if (_actionRouter != null)
                _actionRouter.OnAction -= HandleCanonicalAction;

            if (_selectionService != null)
            {
                _selectionService.OnSelected -= HandleSelectionServiceSelected;
                _selectionService.OnDeselected -= HandleSelectionServiceDeselected;
                _selectionService.OnInspected -= HandleSelectionServiceInspected;
            }

            ClearPartHoverVisual();
            _partStates.Clear();
        }

        private void Update()
        {
            HandlePointerInput();
            UpdateSnapAnimation();
            UpdateInvalidFlash();
            UpdateXRGhostProximity();
            UpdatePartHoverVisual();
            UpdatePointerDragSelectionVisual();
            UpdateHintHighlight();
        }

        // â”€â”€ Canonical actions â”€â”€

        private void HandleCanonicalAction(CanonicalAction action)
        {
            switch (action)
            {
                case CanonicalAction.Select:
                    TrySelectFromPointer(isInspect: false);
                    break;
                case CanonicalAction.Inspect:
                    TrySelectFromPointer(isInspect: true);
                    break;
                case CanonicalAction.Grab:
                    HandleGrabAction();
                    break;
                case CanonicalAction.Place:
                    HandlePlaceAction();
                    break;
                case CanonicalAction.RequestHint:
                    HandleHintAction();
                    break;
            }
        }

        private void HandleGrabAction()
        {
            if (_selectionService == null)
                return;

            GameObject selected = _selectionService.CurrentSelection;
            if (selected == null)
                return;

            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                partController.GrabPart(selected.name);

            if (!_pointerDown)
                BeginXRGrabTracking(selected);
        }

        private void HandlePlaceAction()
        {
            if (_isDragging && _pointerDown)
                return; // pointer-up path handles placement

            GameObject selected = _draggedPart;
            if (selected == null && _selectionService != null)
                selected = _selectionService.CurrentSelection;

            if (selected == null)
                return;

            AttemptPlacementForPart(selected, selected.name);

            if (!_pointerDown)
                ResetDragState();
        }

        private void HandleHintAction()
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui) && !ui.IsHintDisplayAllowed)
                return;

            if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
            {
                session.AssemblyController?.StepController?.RequestHint();
            }
        }

        private void TrySelectFromPointer(bool isInspect)
        {
            if (_selectionService == null)
                return;

            GameObject candidate = _pendingSelectPart;
            if (candidate == null)
            {
                if (!TryGetPointerPosition(out Vector2 screenPos))
                    return;

                candidate = RaycastPartAtScreen(screenPos);
            }

            if (candidate == null)
                return;

            if (isInspect)
                _selectionService.NotifyInspected(candidate);
            else
                _selectionService.NotifySelected(candidate);

            // Keep pending candidate alive through selection callbacks so pointer-down
            // drag tracking can start from HandleSelectionServiceSelection.
            _pendingSelectPart = null;
        }

        private void HandleSelectionServiceSelected(GameObject target) =>
            HandleSelectionServiceSelection(target, isInspect: false);

        private void HandleSelectionServiceInspected(GameObject target) =>
            HandleSelectionServiceSelection(target, isInspect: true);

        private void HandleSelectionServiceSelection(GameObject target, bool isInspect)
        {
            if (_suppressSelectionEvents || target == null)
                return;

            if (!IsSpawnedPart(target))
                return;

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;

            bool accepted = isInspect
                ? partController.InspectPart(target.name)
                : partController.SelectPart(target.name);

            if (!accepted)
            {
                DeselectFromSelectionService();
                return;
            }

            OseLog.Info($"[PartInteraction] Selected part '{target.name}'");

            if (_pointerDown && _pendingSelectPart == target)
                BeginDragTracking(target);
        }

        private void HandleSelectionServiceDeselected(GameObject target)
        {
            if (_suppressSelectionEvents)
                return;

            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                partController.DeselectPart();

            ResetDragState();
        }

        private void DeselectFromSelectionService()
        {
            if (_selectionService == null)
                return;

            _suppressSelectionEvents = true;
            _selectionService.Deselect();
            _suppressSelectionEvents = false;
        }

        private void EnsureInputWiring()
        {
            if (!Application.isPlaying)
            {
                if (_actionRouter == null)
                    _actionRouter = FindFirstObjectByType<InputActionRouter>();
                if (_selectionService == null)
                    _selectionService = FindFirstObjectByType<SelectionService>();
                return;
            }

            if (_actionRouter == null)
                _actionRouter = FindFirstObjectByType<InputActionRouter>();

            if (_actionRouter == null)
            {
                _actionRouter = GetComponent<InputActionRouter>();
                if (_actionRouter == null)
                {
                    _actionRouter = gameObject.AddComponent<InputActionRouter>();
                    OseLog.Warn("[PartInteraction] Auto-created InputActionRouter on scene harness object.");
                }
            }

            if (_selectionService == null)
                _selectionService = FindFirstObjectByType<SelectionService>();

            if (_selectionService == null)
            {
                _selectionService = GetComponent<SelectionService>();
                if (_selectionService == null)
                {
                    _selectionService = gameObject.AddComponent<SelectionService>();
                    OseLog.Warn("[PartInteraction] Auto-created SelectionService on scene harness object.");
                }
            }

            if (Application.isPlaying && _actionRouter != null &&
                _actionRouter.CurrentContext != InputContext.StepInteraction)
            {
                OseLog.Info($"[PartInteraction] Input context '{_actionRouter.CurrentContext}' -> StepInteraction.");
                _actionRouter.SetContext(InputContext.StepInteraction);
            }
        }

        private static bool TryGetPointerPosition(out Vector2 screenPos)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = Vector2.zero;
            return false;
        }

        private GameObject RaycastPartAtScreen(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return null;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return null;

            return FindPartFromHit(hit.transform);
        }

        // ── Pointer input (mouse + touch) ──

        private void HandlePointerInput()
        {
            if (TryGetPointerState(out Vector2 screenPos, out bool pressed, out bool released))
            {
                if (pressed)
                    HandlePointerDown(screenPos);
                else if (released)
                    HandlePointerUp(screenPos);
                else if (_pointerDown)
                    HandlePointerDrag(screenPos);

                return;
            }

            // Failsafe: recover from missed release events (window focus loss, input edge-cases).
            // Without this, _pointerDown can stay latched and block future drag/select flow.
            if (_pointerDown)
            {
                if (!TryGetPointerPosition(out Vector2 fallbackPos))
                    fallbackPos = _pointerDownScreenPos;

                HandlePointerUp(fallbackPos);
            }
        }

        private static bool TryGetPointerState(out Vector2 screenPos, out bool pressed, out bool released)
        {
            screenPos = Vector2.zero;
            pressed = false;
            released = false;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                pressed = mouse.leftButton.wasPressedThisFrame;
                released = mouse.leftButton.wasReleasedThisFrame;
                if (pressed || released || mouse.leftButton.isPressed)
                    return true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                pressed = touch.primaryTouch.press.wasPressedThisFrame;
                released = touch.primaryTouch.press.wasReleasedThisFrame;
                if (pressed || released || touch.primaryTouch.press.isPressed)
                    return true;
            }

            return false;
        }

        private void HandlePointerDown(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return;

            GameObject matchedPart = FindPartFromHit(hit.transform);
            if (matchedPart == null)
                return;

            _pointerDown = true;
            _pointerDownScreenPos = screenPos;
            _pointerDownCamera = cam;
            _pendingSelectPart = matchedPart;

            // Route pointer selection through the canonical action pipeline first.
            if (_actionRouter != null && _selectionService != null)
            {
                _actionRouter.InjectAction(CanonicalAction.Select);
            }
            else if (_selectionService != null)
            {
                _selectionService.NotifySelected(matchedPart);
            }
            else
            {
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                {
                    bool selected = partController.SelectPart(matchedPart.name);
                    if (selected)
                    {
                        OseLog.Info($"[PartInteraction] Selected part '{matchedPart.name}'");
                        BeginDragTracking(matchedPart);
                    }
                }
            }
        }

        private void HandlePointerDrag(Vector2 screenPos)
        {
            if (_draggedPart == null || _dragCamera == null) return;

            Camera cam = _dragCamera;

            if (!_isDragging)
            {
                float dist = Vector2.Distance(screenPos, _dragScreenStart);
                if (dist < DragThresholdPixels)
                    return;

                // Start dragging
                _isDragging = true;
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var pc))
                    pc.GrabPart(_draggedPartId);

                OseLog.Info($"[PartInteraction] Dragging part '{_draggedPartId}'");
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool depthMode = (kb != null && kb.shiftKey.isPressed)
                || (mouse != null && (mouse.rightButton.isPressed || mouse.middleButton.isPressed));

            // Depth-adjust mode:
            // lock cursor ray anchor, and use vertical movement only for push/pull.
            if (depthMode)
            {
                if (!_isDepthAdjustMode)
                {
                    _isDepthAdjustMode = true;
                    _depthAdjustScreenAnchor = screenPos;
                    _lastPointerY = screenPos.y;
                }

                float deltaY = screenPos.y - _lastPointerY;
                _dragRayDistance += deltaY * DepthAdjustSpeed;
            }
            else
            {
                _isDepthAdjustMode = false;
                _depthAdjustScreenAnchor = screenPos;
            }
            _lastPointerY = screenPos.y;

            // Mouse scroll wheel
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _dragRayDistance += Mathf.Sign(scroll) * ScrollDepthSpeed;
            }

            // Q/E keys
            if (kb != null)
            {
                if (kb.eKey.isPressed)
                    _dragRayDistance += ScrollDepthSpeed * Time.deltaTime * 3f;
                if (kb.qKey.isPressed)
                    _dragRayDistance -= ScrollDepthSpeed * Time.deltaTime * 3f;
            }

            // Touch pinch → push/pull along camera forward (mobile)
            var touch = Touchscreen.current;
            if (touch != null && touch.touches.Count >= 2)
            {
                var t0 = touch.touches[0];
                var t1 = touch.touches[1];
                if (t0.press.isPressed && t1.press.isPressed)
                {
                    float pinchDist = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
                    if (_lastPinchDistance > 0f)
                    {
                        float delta = pinchDist - _lastPinchDistance;
                        _dragRayDistance += delta * PinchDepthSpeed;
                    }
                    _lastPinchDistance = pinchDist;
                }
            }
            else
            {
                _lastPinchDistance = -1f;
            }

            _dragRayDistance = Mathf.Max(MinDragRayDistance, _dragRayDistance);
            Vector2 rayScreenPos = _isDepthAdjustMode ? _depthAdjustScreenAnchor : screenPos;
            Ray ray = cam.ScreenPointToRay(rayScreenPos);
            Vector3 dragTargetWorld = ray.GetPoint(_dragRayDistance);
            _draggedPart.transform.position = ClampDragPosition(cam, dragTargetWorld, _draggedPart);

            // Check proximity to ghosts and highlight when in snap zone
            UpdateGhostProximity();
        }

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (!_pointerDown)
                return;

            _pointerDown = false;

            if (!_isDragging || _draggedPart == null)
            {
                // Was just a click, not a drag ? selection handled by canonical action
                _pendingSelectPart = null;
                ResetDragState();
                return;
            }

            _isDragging = false;

            // Attempt placement
            AttemptDragPlacement();

            ResetDragState();
        }

        private void AttemptDragPlacement()
        {
            if (_draggedPart == null || string.IsNullOrEmpty(_draggedPartId))
                return;

            AttemptPlacementForPart(_draggedPart, _draggedPartId);
        }

        private void AttemptPlacementForPart(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrEmpty(partId))
                return;

            ClearGhostHighlight();

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            GhostPlacementInfo nearestInfo = FindNearestGhostForPart(partId, partGo.transform.position, out float nearestDist);
            bool inSnapZone = nearestInfo != null && nearestDist <= SnapZoneRadius;

            if (!inSnapZone)
            {
                string targetId = nearestInfo != null ? nearestInfo.TargetId : "unknown";
                var invalid = PlacementValidationResult.Invalid(
                    ValidationFailureReason.PositionOutOfTolerance,
                    positionError: nearestDist,
                    rotationError: 0f);

                partController.AttemptPlacement(partId, targetId, invalid);

                OseLog.Info($"[PartInteraction] Dropped '{partId}' outside snap zone ? stays at drop position.");

                FlashInvalid(partGo, partId);
                partController.DeselectPart();
                DeselectFromSelectionService();
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            string matchedTargetId = nearestInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            partController.AttemptPlacement(partId, matchedTargetId, result);

            if (!result.IsValid)
            {
                FlashInvalid(partGo, partId);
                partController.DeselectPart();
                DeselectFromSelectionService();
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            OseLog.Info($"[PartInteraction] Dropped '{partId}' in snap zone ? snapping to target.");

            BeginSnapToTarget(partGo, partId, matchedTargetId, nearestInfo.transform);
            RemoveGhostForPart(partId);

            if (partController.AreActiveStepRequiredPartsPlaced())
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
        }

        // ── Ghost proximity detection ──

        private void UpdateGhostProximity()
        {
            if (_draggedPart == null || _spawnedGhosts.Count == 0)
                return;

            GhostPlacementInfo nearestInfo = FindNearestGhostForPart(_draggedPartId, _draggedPart.transform.position, out float nearestDist);
            GameObject nearest = (nearestInfo != null && nearestDist <= SnapZoneRadius) ? nearestInfo.gameObject : null;

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
        }
        private void ClearGhostHighlight()
        {
            if (_ghostHighlighted && _hoveredGhost != null)
            {
                MaterialHelper.ApplyGhost(_hoveredGhost);
            }
            _hoveredGhost = null;
            _ghostHighlighted = false;
        }

        private void UpdateHintHighlight()
        {
            if (_hintGhost == null || _hintHighlightUntil <= 0f)
                return;

            if (Time.time >= _hintHighlightUntil)
            {
                ClearHintHighlight();
                return;
            }

            if (_ghostHighlighted && _hoveredGhost == _hintGhost)
                return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * HintHighlightPulseSpeed);
            Color color = Color.Lerp(HintHighlightColorA, HintHighlightColorB, pulse);
            MaterialHelper.SetMaterialColor(_hintGhost, color);
        }

        private void ClearHintHighlight()
        {
            if (_hintGhost != null)
            {
                if (_ghostHighlighted && _hoveredGhost == _hintGhost)
                {
                    MaterialHelper.Apply(_hintGhost, "Ghost Ready Material", GhostReadyColor);
                }
                else
                {
                    MaterialHelper.ApplyGhost(_hintGhost);
                }
            }

            _hintGhost = null;
            _hintHighlightUntil = 0f;
        }

        private GhostPlacementInfo FindNearestGhostForPart(string partId, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (string.IsNullOrEmpty(partId))
                return null;

            GhostPlacementInfo nearest = null;

            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesPart(partId))
                    continue;

                float dist = Vector3.Distance(worldPos, ghost.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = info;
                }
            }

            return nearest;
        }

        private static HintDefinition ResolveHintForStep(MachinePackageDefinition package, StepDefinition step, int totalHintsForStep)
        {
            if (package == null || step == null)
                return null;

            string[] hintIds = step.hintIds;
            if (hintIds == null || hintIds.Length == 0)
                return null;

            int index = Mathf.Clamp(totalHintsForStep - 1, 0, hintIds.Length - 1);
            string hintId = hintIds[index];
            if (string.IsNullOrWhiteSpace(hintId))
                return null;

            if (package.TryGetHint(hintId, out HintDefinition hint))
                return hint;

            return null;
        }

        private Transform ResolveHintTargetTransform(HintDefinition hint)
        {
            if (hint == null)
                return null;

            if (!string.IsNullOrWhiteSpace(hint.targetId))
            {
                foreach (var ghost in _spawnedGhosts)
                {
                    if (ghost == null) continue;
                    GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                    if (info != null && string.Equals(info.TargetId, hint.targetId, StringComparison.OrdinalIgnoreCase))
                        return ghost.transform;
                }
            }

            if (!string.IsNullOrWhiteSpace(hint.partId))
            {
                foreach (var ghost in _spawnedGhosts)
                {
                    if (ghost == null) continue;
                    GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                    if (info != null && info.MatchesPart(hint.partId))
                        return ghost.transform;
                }
            }

            return null;
        }
        // ── Snap animation (list-based for multi-target) ──

        private void UpdateSnapAnimation()
        {
            if (_activeSnaps.Count == 0)
                return;

            float t = SnapLerpSpeed * Time.deltaTime;

            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                var snap = _activeSnaps[i];
                if (snap.Part == null)
                {
                    _activeSnaps.RemoveAt(i);
                    continue;
                }

                snap.Part.transform.localPosition = Vector3.Lerp(snap.Part.transform.localPosition, snap.TargetPos, t);
                snap.Part.transform.localRotation = Quaternion.Slerp(snap.Part.transform.localRotation, snap.TargetRot, t);
                snap.Part.transform.localScale = Vector3.Lerp(snap.Part.transform.localScale, snap.TargetScale, t);

                if (Vector3.Distance(snap.Part.transform.localPosition, snap.TargetPos) < 0.001f)
                {
                    snap.Part.transform.SetLocalPositionAndRotation(snap.TargetPos, snap.TargetRot);
                    snap.Part.transform.localScale = snap.TargetScale;
                    _activeSnaps.RemoveAt(i);
                }
            }
        }

        // ── Invalid placement flash (list-based for multi-target) ──

        private void FlashInvalid(GameObject partGo, string partId)
        {
            // Remove any existing flash for this part
            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                if (_activeFlashes[i].Part == partGo)
                    _activeFlashes.RemoveAt(i);
            }

            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
            Color originalColor = pp != null
                ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);
            MaterialHelper.Apply(partGo, "Preview Part Material", InvalidFlashColor);

            _activeFlashes.Add(new FlashEntry
            {
                Part = partGo,
                OriginalColor = originalColor,
                Timer = InvalidFlashDuration
            });
        }

        private void UpdateInvalidFlash()
        {
            if (_activeFlashes.Count == 0)
                return;

            float dt = Time.deltaTime;

            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                var flash = _activeFlashes[i];
                if (flash.Part == null)
                {
                    _activeFlashes.RemoveAt(i);
                    continue;
                }

                flash.Timer -= dt;
                if (flash.Timer <= 0f)
                {
                    RestorePartVisual(flash.Part);
                    _activeFlashes.RemoveAt(i);
                }
                else
                {
                    _activeFlashes[i] = flash; // write back modified timer
                }
            }
        }

        // ── Runtime event handlers ──

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            // On ANY step transition, force-clear all interaction state so nothing gets stuck
            ResetDragState();
            if (_selectionService != null)
                _selectionService.Deselect();

            if (evt.Current == StepState.Active)
            {
                SpawnGhostsForStep(evt.StepId);
            }
            else if (evt.Current == StepState.Completed)
            {
                MoveStepPartsToPlayPosition(evt.StepId);
                ClearGhosts();
            }
        }

        private void HandlePartStateChanged(PartStateChanged evt)
        {
            GameObject partGo = FindSpawnedPart(evt.PartId);
            if (partGo == null) return;

            _partStates[evt.PartId] = evt.Current;
            ApplyPartVisualForState(partGo, evt.PartId, evt.Current);

            if (_hoveredPart == partGo && CanApplyHoverVisual(partGo, evt.PartId))
                ApplyHoveredPartVisual(partGo);

            if (evt.Current == PartPlacementState.Selected || evt.Current == PartPlacementState.Inspected)
            {
                PushPartInfoToUI(evt.PartId);
            }
        }

        private void HandleHintRequested(HintRequested evt)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null)
                return;

            if (!package.TryGetStep(evt.StepId, out var step))
                return;

            HintDefinition hint = ResolveHintForStep(package, step, evt.TotalHintsForStep);
            if (hint == null)
                return;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui) && !ui.IsHintDisplayAllowed)
                return;

            if (ui != null)
                ui.ShowHintContent(hint.title, hint.message, hint.type);

            Transform targetTransform = ResolveHintTargetTransform(hint);
            if (targetTransform != null)
            {
                _hintGhost = targetTransform.gameObject;
                _hintHighlightUntil = Time.time + HintHighlightDuration;
                MaterialHelper.ApplyGhost(_hintGhost);

                if (_hintWorldCanvas == null)
                    _hintWorldCanvas = FindFirstObjectByType<HintWorldCanvas>();

                if (_hintWorldCanvas == null)
                {
                    var go = new GameObject("Hint World Canvas");
                    _hintWorldCanvas = go.AddComponent<HintWorldCanvas>();
                }

                _hintWorldCanvas.ShowHint(hint.type, hint.title, hint.message, targetTransform);
            }
        }

        // ── Ghost parts ──

        private void SpawnGhostsForStep(string stepId)
        {
            ClearGhosts();
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0)
                return;

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;

            foreach (string targetId in targetIds)
            {
                if (string.IsNullOrEmpty(targetId)) continue;
                if (!package.TryGetTarget(targetId, out var target)) continue;

                string associatedPartId = target.associatedPartId;
                if (string.IsNullOrEmpty(associatedPartId)) continue;
                if (!package.TryGetPart(associatedPartId, out var part)) continue;

                string ghostRef = part.assetRef;
                if (string.IsNullOrEmpty(ghostRef)) continue;

                TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
                PartPreviewPlacement pp = _spawner.FindPartPlacement(associatedPartId);
                Vector3 ghostPos;
                Quaternion ghostRot;
                Vector3 ghostScale;

                if (tp != null)
                {
                    ghostPos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                    ghostRot = !tp.rotation.IsIdentity
                        ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                        : Quaternion.identity;
                }
                else if (pp != null)
                {
                    ghostPos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                    ghostRot = !pp.playRotation.IsIdentity
                        ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                        : Quaternion.identity;
                }
                else
                {
                    ghostPos = Vector3.zero;
                    ghostRot = Quaternion.identity;
                }

                if (pp != null)
                    ghostScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                else if (tp != null)
                    ghostScale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
                else
                    ghostScale = Vector3.one * 0.5f;

                GameObject ghost = _spawner.TryLoadPackageAsset(ghostRef);
                if (ghost == null)
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (previewRoot != null)
                        ghost.transform.SetParent(previewRoot, false);
                }

                ghost.name = $"Ghost_{associatedPartId}";
                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null)
                    info = ghost.AddComponent<GhostPlacementInfo>();
                info.TargetId = targetId;
                info.PartId = associatedPartId;
                ghost.transform.SetLocalPositionAndRotation(ghostPos, ghostRot);
                ghost.transform.localScale = ghostScale;

                foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                    Destroy(col);

                MaterialHelper.ApplyGhost(ghost);
                _spawnedGhosts.Add(ghost);
            }
        }

        private void ClearGhosts()
        {
            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                ghost.transform.SetParent(null);
                Destroy(ghost);
            }
            _spawnedGhosts.Clear();
        }

        /// <summary>
        /// Removes the ghost associated with a specific part (used when a part is placed
        /// in a multi-target step so remaining ghosts stay visible).
        /// </summary>
        private void RemoveGhostForPart(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return;

            for (int i = _spawnedGhosts.Count - 1; i >= 0; i--)
            {
                var ghost = _spawnedGhosts[i];
                if (ghost == null)
                {
                    _spawnedGhosts.RemoveAt(i);
                    continue;
                }

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info != null && info.MatchesPart(partId))
                {
                    if (_hoveredGhost == ghost)
                        ClearGhostHighlight();
                    if (_hintGhost == ghost)
                        ClearHintHighlight();

                    ghost.transform.SetParent(null);
                    Destroy(ghost);
                    _spawnedGhosts.RemoveAt(i);
                }
            }
        }

        // ── Step completion: move parts to assembled position ──

        private void MoveStepPartsToPlayPosition(string stepId)
        {
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null) return;

            foreach (string partId in partIds)
            {
                if (string.IsNullOrEmpty(partId)) continue;

                PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
                if (pp == null) continue;

                GameObject partGo = FindSpawnedPart(partId);
                if (partGo == null) continue;

                Vector3    pPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                Vector3    pScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                Quaternion pRot   = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;

                partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
                partGo.transform.localScale = pScale;
            }
        }

        // ── Context menu: place selected part at target (debug shortcut) ──

        [ContextMenu("Place Selected Part at Target")]
        private void PlaceSelectedPartAtTarget()
        {
            if (!Application.isPlaying)
            {
                OseLog.Warn("[PartInteraction] PlaceSelectedPartAtTarget is only available in play mode.");
                return;
            }

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            string selectedId = partController.SelectedPartId;
            if (string.IsNullOrEmpty(selectedId))
            {
                OseLog.Warn("[PartInteraction] No part is selected. Click a part first.");
                return;
            }

            string[] targetIds = partController.GetActiveStepTargetIds();
            if (targetIds.Length == 0)
            {
                OseLog.Warn("[PartInteraction] Active step has no targets.");
                return;
            }

            string matchedTargetId = null;
            var package = _spawner.CurrentPackage;
            if (package != null)
            {
                foreach (string tid in targetIds)
                {
                    if (package.TryGetTarget(tid, out var t) && t.associatedPartId == selectedId)
                    {
                        matchedTargetId = tid;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(matchedTargetId))
            {
                OseLog.Warn($"[PartInteraction] No matching target found for part '{selectedId}'.");
                return;
            }

            PlacementValidationResult result = PlacementValidator.ValidateExact();
            partController.AttemptPlacement(selectedId, matchedTargetId, result);

            if (TryResolveSnapPose(selectedId, matchedTargetId, out Vector3 pPos, out Quaternion pRot, out Vector3 pScale))
            {
                GameObject partGo = FindSpawnedPart(selectedId);
                if (partGo != null)
                {
                    partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
                    partGo.transform.localScale = pScale;
                }
            }

            RemoveGhostForPart(selectedId);

            if (partController.AreActiveStepRequiredPartsPlaced())
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
        }

        // ── UI push ──

        private void PushPartInfoToUI(string partId)
        {
            var package = _spawner.CurrentPackage;
            if (package == null) return;
            if (!package.TryGetPart(partId, out var part)) return;
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui)) return;

            string toolNames = string.Empty;
            if (part.toolIds != null && part.toolIds.Length > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (string toolId in part.toolIds)
                {
                    if (string.IsNullOrEmpty(toolId)) continue;
                    if (package.TryGetTool(toolId, out var tool))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(tool.GetDisplayName());
                    }
                }
                toolNames = sb.ToString();
            }

            ui.ShowPartInfoShell(
                part.GetDisplayName(),
                part.function ?? string.Empty,
                part.material ?? string.Empty,
                toolNames,
                part.searchTerms != null ? string.Join(" ", part.searchTerms) : string.Empty);
        }

        // ── Helpers ──

        private void BeginDragTracking(GameObject partGo)
        {
            if (partGo == null)
                return;

            _isDragging = false;
            _draggedPart = partGo;
            _draggedPartId = partGo.name;
            _dragScreenStart = _pointerDownScreenPos;
            _dragCamera = _pointerDownCamera != null ? _pointerDownCamera : Camera.main;
            _dragRayDistance = ResolveInitialDragRayDistance(_dragCamera, _dragScreenStart, partGo.transform.position);
            _lastPinchDistance = -1f;
            _lastPointerY = _pointerDownScreenPos.y;
            _isDepthAdjustMode = false;
            _depthAdjustScreenAnchor = _pointerDownScreenPos;
        }

        private void ResetDragState()
        {
            _pointerDown = false;
            _isDragging = false;
            _draggedPart = null;
            _draggedPartId = null;
            _dragCamera = null;
            _dragRayDistance = 0f;
            _lastPinchDistance = -1f;
            _isDepthAdjustMode = false;
            _depthAdjustScreenAnchor = Vector2.zero;
            _pendingSelectPart = null;
            _pointerDownCamera = null;
            ClearGhostHighlight();
        }

        private void BeginXRGrabTracking(GameObject partGo)
        {
            if (partGo == null)
                return;

            _isDragging = true;
            _draggedPart = partGo;
            _draggedPartId = partGo.name;
            _dragCamera = Camera.main;
            if (_dragCamera != null && TryGetPointerPosition(out Vector2 pointerPos))
                _dragRayDistance = ResolveInitialDragRayDistance(_dragCamera, pointerPos, partGo.transform.position);
            else if (_dragCamera != null)
                _dragRayDistance = Mathf.Max(MinDragRayDistance, Vector3.Distance(_dragCamera.transform.position, partGo.transform.position));
            else
                _dragRayDistance = MinDragRayDistance;
            _lastPinchDistance = -1f;
            _lastPointerY = 0f;
            _isDepthAdjustMode = false;
            _depthAdjustScreenAnchor = Vector2.zero;
        }

        private static float ResolveInitialDragRayDistance(Camera cam, Vector2 screenPos, Vector3 worldPos)
        {
            if (cam == null)
                return MinDragRayDistance;

            Ray ray = cam.ScreenPointToRay(screenPos);
            float projectedDistance = Vector3.Dot(worldPos - ray.origin, ray.direction.normalized);
            if (projectedDistance > MinDragRayDistance)
                return projectedDistance;

            return Mathf.Max(MinDragRayDistance, Vector3.Distance(ray.origin, worldPos));
        }

        private void UpdateXRGhostProximity()
        {
            if (_pointerDown)
                return;

            if (_isDragging && _draggedPart != null)
                UpdateGhostProximity();
        }

        private void UpdatePartHoverVisual()
        {
            if (!Application.isPlaying || _spawner == null || _isDragging)
            {
                ClearPartHoverVisual();
                return;
            }

            GameObject hoveredPart = GetHoveredPartFromXri();
            if (hoveredPart == null)
                hoveredPart = GetHoveredPartFromMouse();

            if (hoveredPart == _hoveredPart)
            {
                if (_hoveredPart != null && CanApplyHoverVisual(_hoveredPart, _hoveredPart.name))
                    ApplyHoveredPartVisual(_hoveredPart);
                return;
            }

            ClearPartHoverVisual();

            if (hoveredPart == null || !CanApplyHoverVisual(hoveredPart, hoveredPart.name))
                return;

            _hoveredPart = hoveredPart;
            ApplyHoveredPartVisual(_hoveredPart);
        }

        private void UpdatePointerDragSelectionVisual()
        {
            // Keep dragged mouse/touch part highlighted until release.
            if (!_pointerDown || !_isDragging || _draggedPart == null)
                return;

            ApplySelectedPartVisual(_draggedPart);
        }

        private GameObject GetHoveredPartFromXri()
        {
            var spawnedParts = _spawner.SpawnedParts;
            for (int i = 0; i < spawnedParts.Count; i++)
            {
                GameObject partGo = spawnedParts[i];
                if (partGo == null)
                    continue;

                XRGrabInteractable grabInteractable = partGo.GetComponent<XRGrabInteractable>();
                if (grabInteractable != null && grabInteractable.isHovered)
                    return partGo;
            }

            return null;
        }

        private GameObject GetHoveredPartFromMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return null;

            var cam = Camera.main;
            if (cam == null)
                return null;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return null;

            return FindPartFromHit(hit.transform);
        }

        private bool CanApplyHoverVisual(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrEmpty(partId))
                return false;

            if (_selectionService != null && _selectionService.CurrentSelection == partGo)
                return false;

            PartPlacementState state = GetPartState(partId);
            return state == PartPlacementState.Available;
        }

        private void ClearPartHoverVisual()
        {
            if (_hoveredPart == null)
            {
                _hoveredPart = null;
                return;
            }

            RestorePartVisual(_hoveredPart);
            _hoveredPart = null;
        }

        private void RestorePartVisual(GameObject partGo)
        {
            if (partGo == null)
                return;

            string partId = partGo.name;
            ApplyPartVisualForState(partGo, partId, GetPartState(partId));
        }

        private PartPlacementState GetPartState(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return PartPlacementState.Available;

            return _partStates.TryGetValue(partId, out PartPlacementState state)
                ? state
                : PartPlacementState.Available;
        }

        private void ApplyPartVisualForState(GameObject partGo, string partId, PartPlacementState state)
        {
            if (partGo == null)
                return;

            switch (state)
            {
                case PartPlacementState.Selected:
                case PartPlacementState.Inspected:
                    ApplySelectedPartVisual(partGo);
                    break;

                case PartPlacementState.Grabbed:
                    ApplySelectedPartVisual(partGo);
                    break;

                case PartPlacementState.PlacedVirtually:
                case PartPlacementState.Completed:
                    MaterialHelper.Apply(partGo, "Preview Part Material", CompletedPartColor);
                    break;

                case PartPlacementState.Available:
                default:
                    ApplyAvailablePartVisual(partGo, partId);
                    break;
            }
        }

        private void ApplyAvailablePartVisual(GameObject partGo, string partId)
        {
            if (TryApplyAffordanceState(partGo, AffordanceStateShortcuts.idle))
                return;

            PartPreviewPlacement placement = _spawner != null ? _spawner.FindPartPlacement(partId) : null;
            Color baseColor = placement != null
                ? new Color(placement.color.r, placement.color.g, placement.color.b, placement.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);

            MaterialHelper.Apply(partGo, "Preview Part Material", baseColor);
        }

        private void ApplyHoveredPartVisual(GameObject partGo)
        {
            if (!TryApplyAffordanceState(partGo, AffordanceStateShortcuts.hovered))
                MaterialHelper.Apply(partGo, "Preview Part Material", HoveredPartColor);
        }

        private void ApplySelectedPartVisual(GameObject partGo)
        {
            if (!TryApplyAffordanceState(partGo, AffordanceStateShortcuts.selected))
                MaterialHelper.Apply(partGo, "Preview Part Material", SelectedPartColor);
        }

        private static bool TryApplyAffordanceState(GameObject partGo, byte stateIndex, float transitionAmount = 1f)
        {
            if (partGo == null)
                return false;

            var provider = partGo.GetComponent<XRInteractableAffordanceStateProvider>();
            if (provider == null)
                return false;

            provider.UpdateAffordanceState(new AffordanceStateData(stateIndex, transitionAmount));
            return true;
        }

        private void BeginSnapToTarget(GameObject partGo, string partId, string targetId, Transform fallback)
        {
            if (partGo == null)
                return;

            // Remove any existing snap for this part (e.g. re-grab before previous snap finished)
            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                if (_activeSnaps[i].Part == partGo)
                    _activeSnaps.RemoveAt(i);
            }

            if (TryResolveSnapPose(partId, targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale))
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

        private bool TryResolveSnapPose(string partId, string targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
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

        private Vector3 ClampDragPosition(Camera cam, Vector3 worldPosition, GameObject partGo)
        {
            Vector3 clamped = ClampToViewport(cam, worldPosition);
            clamped = ClampToFloorBounds(clamped, partGo);
            return clamped;
        }

        private static Vector3 ClampToViewport(Camera cam, Vector3 worldPosition)
        {
            if (cam == null)
                return worldPosition;

            Vector3 viewportPos = cam.WorldToViewportPoint(worldPosition);
            if (viewportPos.z <= 0f)
                return worldPosition;

            viewportPos.x = Mathf.Clamp(viewportPos.x, DragViewportMargin, 1f - DragViewportMargin);
            viewportPos.y = Mathf.Clamp(viewportPos.y, DragViewportMargin, 1f - DragViewportMargin);
            return cam.ViewportToWorldPoint(viewportPos);
        }

        private Vector3 ClampToFloorBounds(Vector3 worldPosition, GameObject partGo)
        {
            if (_setup == null || _setup.Floor == null)
                return worldPosition;

            float partHalfHeight = TryGetVerticalHalfExtent(partGo, out float halfHeight)
                ? halfHeight
                : 0f;

            if (TryGetWorldBounds(_setup.Floor, out Bounds floorBounds))
            {
                worldPosition.x = Mathf.Clamp(worldPosition.x, floorBounds.min.x, floorBounds.max.x);
                worldPosition.z = Mathf.Clamp(worldPosition.z, floorBounds.min.z, floorBounds.max.z);
                float minY = floorBounds.max.y + partHalfHeight + DragFloorEpsilon;
                if (worldPosition.y < minY)
                    worldPosition.y = minY;
                return worldPosition;
            }

            float fallbackFloorY = _setup.Floor.transform.position.y;
            float fallbackMinY = fallbackFloorY + partHalfHeight + DragFloorEpsilon;
            if (worldPosition.y < fallbackMinY)
                worldPosition.y = fallbackMinY;

            return worldPosition;
        }

        private static bool TryGetVerticalHalfExtent(GameObject target, out float halfHeight)
        {
            halfHeight = 0f;
            if (!TryGetWorldBounds(target, out Bounds bounds))
                return false;

            halfHeight = bounds.extents.y;
            return true;
        }

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers != null && renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return true;
            }

            var colliders = target.GetComponentsInChildren<Collider>();
            if (colliders != null && colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    bounds.Encapsulate(colliders[i].bounds);
                return true;
            }

            return false;
        }

        private bool IsSpawnedPart(GameObject target)
        {
            if (target == null)
                return false;

            var parts = _spawner.SpawnedParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == target)
                    return true;
            }
            return false;
        }

        private GameObject FindSpawnedPart(string partId)
        {
            var parts = _spawner.SpawnedParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].name == partId)
                    return parts[i];
            }
            return null;
        }

        private GameObject FindPartFromHit(Transform hitTransform)
        {
            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            while (hitTransform != null && hitTransform != previewRoot)
            {
                var parts = _spawner.SpawnedParts;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] != null && parts[i].transform == hitTransform)
                        return parts[i];
                }
                hitTransform = hitTransform.parent;
            }
            return null;
        }
        private sealed class GhostPlacementInfo : MonoBehaviour
        {
            public string TargetId;
            public string PartId;

            public bool MatchesPart(string partId)
            {
                return !string.IsNullOrEmpty(partId) &&
                    string.Equals(PartId, partId, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
