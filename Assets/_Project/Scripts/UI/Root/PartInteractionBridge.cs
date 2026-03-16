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
        private static readonly Color HoveredPartColor = new Color(0.60f, 0.82f, 1.0f, 1.0f);
        private static readonly Color CompletedPartColor = new Color(0.3f, 0.9f, 0.4f, 1.0f);
        private static readonly Color InvalidFlashColor = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        private static readonly Color GhostReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color ToolCursorColor = new Color(1.0f, 0.75f, 0.22f, 0.40f);
        private static readonly Color ToolTargetIdleColor = new Color(0.25f, 0.9f, 1.0f, 0.62f);
        private static readonly Color ToolTargetHoverColor = new Color(0.55f, 1.0f, 1.0f, 0.9f);
        private static readonly Color HintHighlightColorA = new Color(0.95f, 0.85f, 0.2f, 0.4f);
        private static readonly Color HintHighlightColorB = new Color(1.0f, 0.95f, 0.35f, 0.7f);
        private static readonly Color GhostSelectedPulseA = new Color(0.35f, 0.85f, 1.0f, 0.35f);
        private static readonly Color GhostSelectedPulseB = new Color(0.55f, 1.0f, 0.7f, 0.7f);

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
        private const float ToolCursorUniformScale = 0.16f;
        private const float ToolCursorRayDistance = 0.75f;
        private const float ToolCursorVerticalOffset = -0.08f;
        private const float ToolTargetPulseSpeed = 3.6f;
        private const float ToolTargetScalePulse = 0.12f;
        private const float ToolTargetHeightPulse = 0.05f;
        private const float GhostClickColliderRadius = 1.2f;
        private const float ToolTargetColliderRadius = 1.5f;
        private const float ScreenProximityDesktop = 70f;
        private const float ScreenProximityMobile = 120f;
        private const float GhostSelectedPulseSpeed = 3.0f;     // Hz for ghost "click here" pulse
        private const float ToolCursorScreenProximityReady = 150f;  // screen pixels — cursor changes color
        private static readonly Color ToolCursorReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.65f); // bright green

        // Toggled automatically by V2 InteractionOrchestrator at runtime via reflection.
        // When true, this bridge skips pointer input polling (V2 handles input instead).
        [HideInInspector] public bool ExternalControlEnabled;

        /// <summary>
        /// World position of the last successfully executed tool action target.
        /// Updated by TryExternalToolAction; read by V2 orchestrator via reflection to focus camera.
        /// </summary>
        public Vector3 LastToolActionWorldPos => _lastToolActionWorldPos;

        private PackagePartSpawner _spawner;
        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _spawnedGhosts = new List<GameObject>();
        private readonly List<GameObject> _spawnedToolActionTargets = new List<GameObject>();
        private GameObject _toolGhostIndicator;
        private GameObject _hoveredToolActionTarget;
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
        private GameObject _externalHoveredPartForUi;
        private float _hintHighlightUntil;
        private HintWorldCanvas _hintWorldCanvas;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private GameObject _hoveredPart;
        private bool _startupSyncPending;
        private bool _toolCursorInReadyState;
        private string _ghostPulsePartId; // part id whose matching ghosts are pulsing
        private Vector3 _lastToolActionWorldPos;

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
            RuntimeEventBus.Subscribe<ActiveToolChanged>(HandleActiveToolChanged);

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

            _startupSyncPending = true;
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Unsubscribe<HintRequested>(HandleHintRequested);
            RuntimeEventBus.Unsubscribe<ActiveToolChanged>(HandleActiveToolChanged);

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
            ClearToolGhostIndicator();
            ClearToolActionTargets();
            _startupSyncPending = false;
        }

        private void Update()
        {
            TrySyncStartupState();
            HandlePointerInput();
            UpdateSnapAnimation();
            UpdateInvalidFlash();
            UpdateXRGhostProximity();
            if (!ExternalControlEnabled)
            {
                UpdatePartHoverVisual();
                UpdatePointerDragSelectionVisual();
            }
            UpdateHintHighlight();
            UpdateGhostSelectionPulse();
            UpdateToolGhostIndicatorPosition();
            UpdateToolCursorProximity();
            UpdateToolActionTargetVisuals();
        }

        private void TrySyncStartupState()
        {
            if (!_startupSyncPending || !Application.isPlaying)
                return;

            if (_spawner == null)
            {
                _startupSyncPending = false;
                return;
            }

            if (_spawner.CurrentPackage == null && SessionDriver.CurrentPackage != null)
            {
                _spawner.ApplyPackageSnapshot(SessionDriver.CurrentPackage);
            }

            if (_spawner.CurrentPackage == null)
                return;

            if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
            {
                var stepController = session.AssemblyController?.StepController;
                if (stepController != null && stepController.HasActiveStep)
                {
                    string activeStepId = stepController.CurrentStepState.StepId;
                    if (!string.IsNullOrWhiteSpace(activeStepId))
                        SpawnGhostsForStep(activeStepId);
                }
            }

            RefreshToolGhostIndicator();
            RefreshToolActionTargets();
            _startupSyncPending = false;
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
                case CanonicalAction.ToggleToolMenu:
                    HandleToggleToolMenuAction();
                    break;
                case CanonicalAction.Confirm:
                case CanonicalAction.ToolPrimaryAction:
                    HandleConfirmOrToolPrimaryAction();
                    break;
            }
        }

        private void HandleGrabAction()
        {
            if (IsToolModeLockedForParts())
                return;

            if (_selectionService == null)
                return;

            GameObject selected = _selectionService.CurrentSelection;
            if (selected == null)
                return;

            bool canGrab = true;
            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                canGrab = partController.GrabPart(selected.name);

            if (!canGrab)
            {
                OseLog.VerboseInfo($"[PartInteraction] Grab blocked for locked part '{selected.name}'.");
                return;
            }

            if (!_pointerDown)
                BeginXRGrabTracking(selected);
        }

        private void HandlePlaceAction()
        {
            if (IsToolModeLockedForParts())
                return;

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

        private void HandleConfirmOrToolPrimaryAction()
        {
            OseLog.VerboseInfo("[PartInteraction] HandleConfirmOrToolPrimaryAction invoked.");

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null)
                return;

            if (TryExecuteToolPrimaryActionFromPointer(session, stepController))
                return;

            bool allowConfirmationComplete =
                step.requiresConfirmation ||
                string.Equals(step.completionMode, "confirmation_only", StringComparison.OrdinalIgnoreCase);

            if (!allowConfirmationComplete)
                return;

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        private void HandleToggleToolMenuAction()
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var uiAdapter))
                return;

            if (uiAdapter is UIRootCoordinator uiRoot)
            {
                uiRoot.ToggleToolDock();
            }
        }

        /// <summary>
        /// Called by V2 orchestrator via LegacyBridgeAdapter. If the screen position
        /// hits (or is near) a ghost matching the currently selected part, snaps the
        /// part to the ghost target and returns true. Otherwise returns false.
        /// </summary>
        public bool TryExternalClickToPlace(Vector2 screenPos)
        {
            if (!Application.isPlaying || !ExternalControlEnabled)
                return false;
            return TryHandleClickToPlace(screenPos);
        }

        /// <summary>
        /// Called by V2 orchestrator via LegacyBridgeAdapter when tool mode is locked.
        /// Directly executes the tool primary action using screen position + auto-resolve,
        /// bypassing the canonical action router to avoid wiring failures.
        /// </summary>
        public bool TryExternalToolAction(Vector2 screenPos)
        {
            if (!Application.isPlaying)
                return false;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            OseLog.Info($"[PartInteraction] TryExternalToolAction at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={_spawnedToolActionTargets.Count}. Tool='{session.ToolController?.ActiveToolId ?? "none"}'.");
            string interactedTargetId = null;

            ToolActionTargetInfo resolvedTarget = null;
            if (TryGetToolActionTargetAtScreen(screenPos, out resolvedTarget))
                interactedTargetId = resolvedTarget.TargetId;

            if (interactedTargetId == null)
                interactedTargetId = TryAutoResolveSingleToolTarget();

            if (interactedTargetId == null)
            {
                OseLog.Info($"[PartInteraction] TryExternalToolAction: no target found at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={_spawnedToolActionTargets.Count}.");
                return false;
            }

            // Capture world position before executing (the target may be destroyed/refreshed after).
            if (resolvedTarget != null)
                _lastToolActionWorldPos = resolvedTarget.transform.position;
            else
            {
                // Single-target auto-resolve: get position from the spawned target.
                var singleTarget = _spawnedToolActionTargets.Count == 1 ? _spawnedToolActionTargets[0] : null;
                if (singleTarget != null)
                    _lastToolActionWorldPos = singleTarget.transform.position;
            }

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.Info($"[PartInteraction] TryExternalToolAction: action rejected for '{interactedTargetId}'.");
                return false;
            }

            if (!handled)
                return false;

            OseLog.Info($"[PartInteraction] TryExternalToolAction: success on '{interactedTargetId}'.");
            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        /// <summary>
        /// Called by V2 orchestrator via LegacyBridgeAdapter while external control is enabled.
        /// Shows hovered-part info while hovering; when hover clears, restores selected-part
        /// info if any, otherwise hides the panel.
        /// </summary>
        public void SetExternalHoveredPart(GameObject hoveredPart)
        {
            if (!Application.isPlaying || !ExternalControlEnabled)
                return;

            if (IsToolModeLockedForParts())
            {
                _externalHoveredPartForUi = null;
                if (ServiceRegistry.TryGet<IPresentationAdapter>(out var toolModeUi) && toolModeUi is UIRootCoordinator toolModeHoverUi)
                    toolModeHoverUi.ClearHoverPartInfo();
                return;
            }

            bool hoverChanged = hoveredPart != _externalHoveredPartForUi;
            _externalHoveredPartForUi = hoveredPart;

            if (_externalHoveredPartForUi != null)
            {
                // Re-push every frame while hovered so hover info remains visible
                // even if selected/info updates occur during drag/place events.
                PushPartInfoToUI(_externalHoveredPartForUi.name, isHoverInfo: true);
                return;
            }

            if (!hoverChanged)
                return;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var uiAdapter) && uiAdapter is UIRootCoordinator hoverAwareUi)
                hoverAwareUi.ClearHoverPartInfo();

            GameObject selected = _selectionService != null ? _selectionService.CurrentSelection : null;
            if (selected != null)
            {
                PushPartInfoToUI(selected.name);
            }
            else if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                ui.HidePartInfoPanel();
            }
        }

        private void TrySelectFromPointer(bool isInspect)
        {
            if (IsToolModeLockedForParts())
                return;

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

            if (IsToolModeLockedForParts())
            {
                DeselectFromSelectionService();
                return;
            }

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
            StartGhostSelectionPulse(target.name);
            TryAutoCompleteSelectionStep(target.name);

            if (_pointerDown && _pendingSelectPart == target)
            {
                if (IsPartMovementLocked(target.name))
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag tracking blocked for locked part '{target.name}'.");
                    _pendingSelectPart = null;
                    return;
                }

                BeginDragTracking(target);
            }
        }

        private void TryAutoCompleteSelectionStep(string selectedPartId)
        {
            if (string.IsNullOrWhiteSpace(selectedPartId))
                return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null || !HasEventTag(step.eventTags, "select"))
                return;

            if (!IsPartValidForSelectionStep(step, selectedPartId))
                return;

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        private static bool IsPartValidForSelectionStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrWhiteSpace(partId))
                return false;

            if (ContainsId(step.requiredPartIds, partId))
                return true;

            if (HasAnyIds(step.requiredPartIds))
                return false;

            if (ContainsId(step.optionalPartIds, partId))
                return true;

            if (HasAnyIds(step.optionalPartIds))
                return false;

            return true;
        }

        private static bool HasEventTag(string[] tags, string expectedTag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrWhiteSpace(expectedTag))
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], expectedTag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsId(string[] ids, string expectedId)
        {
            if (ids == null || ids.Length == 0 || string.IsNullOrWhiteSpace(expectedId))
                return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i]))
                    continue;

                if (string.Equals(ids[i].Trim(), expectedId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasAnyIds(string[] ids)
        {
            if (ids == null || ids.Length == 0)
                return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(ids[i]))
                    return true;
            }

            return false;
        }

        private void HandleSelectionServiceDeselected(GameObject target)
        {
            if (_suppressSelectionEvents)
                return;

            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                partController.DeselectPart();

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                ui.HidePartInfoPanel();

            StopGhostSelectionPulse();
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

            // Ignore triggers so ghost/click-to-place colliders don't block part selection.
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                return null;

            return FindPartFromHit(hit.transform);
        }

        // ── Pointer input (mouse + touch) ──

        private void HandlePointerInput()
        {
            if (ExternalControlEnabled) return;

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
            if (TryHandleToolActionPointerDown(screenPos))
                return;

            if (TryHandleClickToPlace(screenPos))
                return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                return;

            GameObject matchedPart = FindPartFromHit(hit.transform);
            if (matchedPart == null)
                return;

            if (IsPartMovementLocked(matchedPart.name))
            {
                OseLog.VerboseInfo($"[PartInteraction] Drag blocked for locked part '{matchedPart.name}'. Selection is still allowed.");

                _pendingSelectPart = matchedPart;

                if (_actionRouter != null && _selectionService != null)
                {
                    _actionRouter.InjectAction(CanonicalAction.Select);
                }
                else if (_selectionService != null)
                {
                    _selectionService.NotifySelected(matchedPart);
                    _pendingSelectPart = null;
                }
                else if (ServiceRegistry.TryGet<PartRuntimeController>(out var lockedPartController))
                {
                    lockedPartController.SelectPart(matchedPart.name);
                    _pendingSelectPart = null;
                }

                return;
            }

            _pointerDown = true;
            _pointerDownScreenPos = screenPos;
            _pointerDownCamera = cam;
            _pendingSelectPart = matchedPart;

            // If the same part is already selected, SelectionService.NotifySelected short-circuits
            // and no OnSelected callback is fired. Start drag tracking immediately in that case.
            if (_selectionService != null && _selectionService.CurrentSelection == matchedPart)
            {
                HandleSelectionServiceSelection(matchedPart, isInspect: false);
                return;
            }

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
                        if (!IsPartMovementLocked(matchedPart.name))
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
                bool canGrab = true;
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var pc))
                    canGrab = pc.GrabPart(_draggedPartId);

                if (!canGrab)
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag blocked for locked part '{_draggedPartId}'.");
                    ResetDragState();
                    return;
                }

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
                // Keep the part selected after a failed drop; deselection should only
                // happen via explicit user action (click-away/cancel).
                partController.SelectPart(partId);
                _selectionService?.NotifySelected(partGo);
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            string matchedTargetId = nearestInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            partController.AttemptPlacement(partId, matchedTargetId, result);

            if (!result.IsValid)
            {
                FlashInvalid(partGo, partId);
                // Keep the part selected after a failed drop; deselection should only
                // happen via explicit user action (click-away/cancel).
                partController.SelectPart(partId);
                _selectionService?.NotifySelected(partGo);
                session.AssemblyController?.StepController?.FailAttempt();
                return;
            }

            OseLog.Info($"[PartInteraction] Dropped '{partId}' in snap zone ? snapping to target.");

            BeginSnapToTarget(partGo, partId, matchedTargetId, nearestInfo.transform);
            RemoveGhostForPart(partId);

            if (partController.AreActiveStepRequiredPartsPlaced())
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
        }

        // ── Click-to-place ──

        /// <summary>
        /// Attempts click-to-place: if a part is selected and the pointer hits (or is near)
        /// a matching ghost target, snap the part there without requiring drag.
        /// </summary>
        private bool TryHandleClickToPlace(Vector2 screenPos)
        {
            if (_spawnedGhosts.Count == 0)
                return false;

            // Need a selected part that isn't being dragged.
            GameObject selected = _selectionService != null ? _selectionService.CurrentSelection : null;
            if (selected == null || _draggedPart != null)
                return false;

            string partId = selected.name;
            if (!IsSpawnedPart(selected))
                return false;

            if (IsPartMovementLocked(partId))
                return false;

            // Layer 1: Direct raycast on ghost trigger collider.
            GhostPlacementInfo ghostInfo = RaycastGhostAtScreen(screenPos);

            // Layer 2: Screen-space proximity fallback.
            if (ghostInfo == null)
                ghostInfo = FindNearestGhostByScreenProximity(screenPos, partId);

            if (ghostInfo == null || !ghostInfo.MatchesPart(partId))
                return false;

            ExecuteClickToPlace(partId, selected, ghostInfo);
            return true;
        }

        private GhostPlacementInfo RaycastGhostAtScreen(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            // Ghost colliders are triggers, so we must explicitly include them.
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide))
                return null;

            return FindGhostInfoFromHit(hit.transform);
        }

        private static GhostPlacementInfo FindGhostInfoFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                GhostPlacementInfo info = hitTransform.GetComponent<GhostPlacementInfo>();
                if (info != null)
                    return info;
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        private GhostPlacementInfo FindNearestGhostByScreenProximity(Vector2 screenPos, string partId)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            float threshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop;
            float closestDist = threshold;
            GhostPlacementInfo best = null;

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesPart(partId)) continue;

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

        private void ExecuteClickToPlace(string partId, GameObject partGo, GhostPlacementInfo ghostInfo)
        {
            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            string targetId = ghostInfo.TargetId;
            PlacementValidationResult result = PlacementValidator.ValidateExact();
            partController.AttemptPlacement(partId, targetId, result);

            if (!result.IsValid)
            {
                FlashInvalid(partGo, partId);
                return;
            }

            OseLog.Info($"[PartInteraction] Click-to-place '{partId}' at ghost target '{targetId}'.");

            BeginSnapToTarget(partGo, partId, targetId, ghostInfo.transform);
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

                // Auto-snap: trigger placement immediately when entering the snap zone
                // instead of waiting for the user to release the pointer.
                // Instantly move the part to the ghost position so there's no visible
                // "fly from drag position" animation — the user sees it lock in place.
                if (nearestInfo != null && _isDragging)
                {
                    OseLog.Info($"[PartInteraction] Auto-snap: '{_draggedPartId}' entered snap zone of '{nearestInfo.TargetId}'.");
                    GameObject partGo = _draggedPart;
                    string partId = _draggedPartId;

                    // Teleport part to ghost pose before starting snap/placement.
                    partGo.transform.localPosition = nearestInfo.transform.localPosition;
                    partGo.transform.localRotation = nearestInfo.transform.localRotation;
                    partGo.transform.localScale = nearestInfo.transform.localScale;

                    ResetDragState();
                    AttemptPlacementForPart(partGo, partId);
                }
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

        // ── Ghost "click here" pulse when a part is selected ──

        private void StartGhostSelectionPulse(string partId)
        {
            _ghostPulsePartId = partId;
        }

        private void StopGhostSelectionPulse()
        {
            if (_ghostPulsePartId == null)
                return;

            // Restore default ghost material on all matching ghosts.
            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info != null && info.MatchesPart(_ghostPulsePartId))
                    MaterialHelper.ApplyGhost(ghost);
            }
            _ghostPulsePartId = null;
        }

        private void UpdateGhostSelectionPulse()
        {
            if (_ghostPulsePartId == null || _spawnedGhosts.Count == 0)
                return;

            // Stop pulsing once dragging starts — the proximity highlight takes over.
            if (_draggedPart != null)
            {
                StopGhostSelectionPulse();
                return;
            }

            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * GhostSelectedPulseSpeed);
            Color pulseColor = Color.Lerp(GhostSelectedPulseA, GhostSelectedPulseB, t);

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !info.MatchesPart(_ghostPulsePartId))
                    continue;

                MaterialHelper.SetMaterialColor(ghost, pulseColor);
            }
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
            // On any step transition, force-clear drag state so nothing gets stuck.
            ResetDragState();

            if (evt.Current == StepState.Active)
            {
                // Clear any stale SelectionService selection so the dedup guard in
                // NotifySelected doesn't block re-selection of the same part on the
                // new step (e.g. beam selected on step 2 → must be selectable again on step 3).
                DeselectFromSelectionService();
                SpawnGhostsForStep(evt.StepId);
            }
            else if (evt.Current == StepState.Completed)
            {
                MoveStepPartsToPlayPosition(evt.StepId);
                ClearGhosts();
            }

            RefreshToolGhostIndicator();
            RefreshToolActionTargets();
            if (IsToolModeLockedForParts())
                ClearPartHoverVisual();
        }

        private void HandleActiveToolChanged(ActiveToolChanged evt)
        {
            if (!string.IsNullOrWhiteSpace(evt.CurrentToolId))
                ResetDragState();

            RefreshToolGhostIndicator();
            RefreshToolActionTargets();
            if (IsToolModeLockedForParts())
                ClearPartHoverVisual();
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

                // Add a generous trigger collider for click-to-place interaction.
                var clickCollider = ghost.AddComponent<SphereCollider>();
                clickCollider.isTrigger = true;
                clickCollider.radius = GhostClickColliderRadius;

                MaterialHelper.ApplyGhost(ghost);
                _spawnedGhosts.Add(ghost);
            }
        }

        private void ClearGhosts()
        {
            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                Destroy(ghost);
            }
            _spawnedGhosts.Clear();
        }

        private void RefreshToolGhostIndicator()
        {
            ClearToolGhostIndicator();

            if (!Application.isPlaying || _spawner == null || _setup == null)
                return;

            if (!TryGetActiveToolDefinition(out string activeToolId, out ToolDefinition tool))
                return;

            GameObject ghostTool = !string.IsNullOrWhiteSpace(tool.assetRef)
                ? _spawner.TryLoadPackageAsset(tool.assetRef)
                : null;

            if (ghostTool == null)
            {
                ghostTool = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            }

            _toolGhostIndicator = ghostTool;
            _toolGhostIndicator.name = $"CursorTool_{activeToolId}";
            _toolGhostIndicator.transform.SetParent(transform, false);
            _toolGhostIndicator.transform.localScale = Vector3.one * ToolCursorUniformScale;

            foreach (Collider col in _toolGhostIndicator.GetComponentsInChildren<Collider>(true))
                Destroy(col);

            MaterialHelper.ApplyToolCursor(_toolGhostIndicator, ToolCursorColor);
            UpdateToolGhostIndicatorPosition();
        }

        private void UpdateToolGhostIndicatorPosition()
        {
            if (_toolGhostIndicator == null)
                return;

            if (_isDragging)
            {
                _toolGhostIndicator.SetActive(false);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
                return;

            if (!TryGetPointerPosition(out Vector2 screenPos))
            {
                _toolGhostIndicator.SetActive(false);
                return;
            }

            if (!_toolGhostIndicator.activeSelf)
                _toolGhostIndicator.SetActive(true);

            Ray ray = cam.ScreenPointToRay(screenPos);
            Vector3 worldPos = ray.GetPoint(ToolCursorRayDistance) + (cam.transform.up * ToolCursorVerticalOffset);
            _toolGhostIndicator.transform.position = worldPos;
            _toolGhostIndicator.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        }

        private static bool TryGetActiveToolDefinition(out string toolId, out ToolDefinition tool)
        {
            toolId = null;
            tool = null;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            if (session == null || session.Package == null || session.ToolController == null)
                return false;

            toolId = session.ToolController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(toolId))
                return false;

            return session.Package.TryGetTool(toolId, out tool);
        }

        private void ClearToolGhostIndicator()
        {
            if (_toolGhostIndicator == null)
                return;

            if (_hintGhost == _toolGhostIndicator)
                ClearHintHighlight();

            Destroy(_toolGhostIndicator);
            _toolGhostIndicator = null;
            _toolCursorInReadyState = false;
        }

        private bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            if (session?.ToolController == null)
                return false;

            if (!session.ToolController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot))
                return false;

            return snapshot.IsConfigured && !snapshot.IsCompleted;
        }

        private bool TryHandleToolActionPointerDown(Vector2 screenPos)
        {
            if (!IsToolModeLockedForParts())
                return false;

            if (!TryGetPrimaryToolActionSnapshot(out ToolRuntimeController.ToolActionSnapshot actionSnapshot, out _))
                return false;

            // Equip-only actions intentionally have no world target and must be advanced via explicit confirm.
            if (string.IsNullOrWhiteSpace(actionSnapshot.TargetId))
                return true;

            string interactedTargetId = null;
            if (TryGetToolActionTargetAtScreen(screenPos, out ToolActionTargetInfo toolTarget))
                interactedTargetId = toolTarget.TargetId;

            // Single-target auto-resolve: if only one target exists, any tap triggers it.
            if (interactedTargetId == null)
                interactedTargetId = TryAutoResolveSingleToolTarget();

            TryExecuteToolPrimaryAction(interactedTargetId, out _, out _);
            return true;
        }

        private bool TryExecuteToolPrimaryActionFromPointer(
            MachineSessionController session,
            StepController stepController)
        {
            string interactedTargetId = null;

            // Use screen-based detection (includes enlarged collider + proximity fallback)
            // instead of hover state which may be suppressed when tool mode is locked.
            if (TryGetPointerPosition(out Vector2 pointerPos) &&
                TryGetToolActionTargetAtScreen(pointerPos, out ToolActionTargetInfo resolvedTarget))
                interactedTargetId = resolvedTarget.TargetId;

            // Single-target auto-resolve.
            if (interactedTargetId == null)
            {
                interactedTargetId = TryAutoResolveSingleToolTarget();
                if (interactedTargetId != null)
                    OseLog.VerboseInfo($"[PartInteraction] Tool action auto-resolved to single target '{interactedTargetId}'.");
            }

            if (interactedTargetId == null)
            {
                OseLog.VerboseInfo($"[PartInteraction] Tool action: no target resolved. Spawned targets={_spawnedToolActionTargets.Count}.");
                return false;
            }

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.VerboseInfo($"[PartInteraction] Tool action failed for target '{interactedTargetId}'. Check ToolRuntimeController logs.");
                return false;
            }

            if (!handled)
                return false;

            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        private bool TryExecuteToolPrimaryAction(
            string interactedTargetId,
            out bool shouldCompleteStep,
            out bool handled)
        {
            shouldCompleteStep = false;
            handled = false;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session.ToolController == null)
            {
                OseLog.VerboseInfo("[PartInteraction] TryExecuteToolPrimaryAction: no session or tool controller.");
                return false;
            }

            ToolRuntimeController.ToolActionExecutionResult toolResult =
                session.ToolController.TryExecutePrimaryAction(interactedTargetId);

            handled = toolResult.Handled;
            shouldCompleteStep = toolResult.ShouldCompleteStep;

            // Only treat as success when the action actually ran (FailureReason == None).
            // "Handled" being true on a Failed result means the controller understood the request
            // but rejected it (wrong tool, wrong target, etc.) — NOT a successful execution.
            bool actionRan = handled && toolResult.FailureReason == ToolActionFailureReason.None;
            if (!actionRan)
            {
                if (toolResult.FailureReason != ToolActionFailureReason.None)
                    OseLog.Info($"[PartInteraction] Tool action rejected ({toolResult.FailureReason}): {toolResult.Message}.");
                return false;
            }

            RefreshToolActionTargets();
            return true;
        }

        private bool HandleToolPrimaryResult(
            MachineSessionController session,
            StepController stepController,
            bool shouldCompleteStep)
        {
            if (!shouldCompleteStep)
                return true;

            stepController.CompleteStep(session.GetElapsedSeconds());
            return true;
        }

        /// <summary>
        /// When exactly one tool action target is spawned, return its id so any tap triggers the action.
        /// </summary>
        private string TryAutoResolveSingleToolTarget()
        {
            if (_spawnedToolActionTargets.Count != 1)
                return null;

            GameObject single = _spawnedToolActionTargets[0];
            if (single == null)
                return null;

            var info = single.GetComponent<ToolActionTargetInfo>();
            return info != null ? info.TargetId : null;
        }

        private void RefreshToolActionTargets()
        {
            ClearToolActionTargets();

            if (!Application.isPlaying || _spawner == null || _setup == null)
                return;

            MachineSessionController session = null;
            string requiredToolId = null;
            string targetId = null;

            if (TryGetPrimaryToolActionSnapshot(out ToolRuntimeController.ToolActionSnapshot actionSnapshot, out session))
            {
                if (actionSnapshot.IsCompleted)
                    return;

                requiredToolId = actionSnapshot.ToolId;
                targetId = actionSnapshot.TargetId;
            }
            else
            {
                if (!TryResolveFallbackStepToolActionTarget(out session, out requiredToolId, out targetId))
                {
                    TryWarnMissingPrimaryToolActionSnapshot();
                    return;
                }

                StepController stepController = session?.AssemblyController?.StepController;
                OseLog.Warn($"[PartInteraction] Falling back to step-defined tool target for step '{stepController?.CurrentStepDefinition?.id}'.");
            }

            if (string.IsNullOrWhiteSpace(targetId))
                return;

            if (!TryResolveToolActionTargetPose(session.Package, targetId, out Vector3 markerPos, out Quaternion markerRot, out Vector3 markerScale))
            {
                OseLog.Warn($"[PartInteraction] Could not resolve tool target pose for '{targetId}'.");
                return;
            }

            // Prefer anchoring the marker to the currently spawned ghost when available.
            // This keeps Step 5 guidance aligned with what the user is already looking at.
            if (TryGetGhostTargetPose(targetId, out Vector3 ghostPos, out Quaternion ghostRot, out Vector3 ghostScale))
            {
                markerPos = ghostPos;
                markerRot = ghostRot;
                markerScale = ghostScale;
            }

            Transform previewRoot = _setup.PreviewRoot;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (previewRoot != null)
                marker.transform.SetParent(previewRoot, false);

            marker.name = $"ToolTarget_{requiredToolId}_{targetId}";
            marker.transform.SetLocalPositionAndRotation(markerPos, markerRot);
            marker.transform.localScale = ResolveToolTargetMarkerScale(markerScale);
            float markerLift = Mathf.Max(markerScale.y * 0.75f, marker.transform.localScale.y * 0.6f);
            marker.transform.position += Vector3.up * markerLift;

            PackagePartSpawner.EnsureColliders(marker);
            // Enlarge collider for more forgiving tap detection.
            var toolCol = marker.GetComponent<SphereCollider>();
            if (toolCol != null)
                toolCol.radius = ToolTargetColliderRadius;
            MaterialHelper.ApplyToolTargetMarker(marker, ToolTargetIdleColor);

            ToolActionTargetInfo info = marker.GetComponent<ToolActionTargetInfo>();
            if (info == null)
                info = marker.AddComponent<ToolActionTargetInfo>();
            info.TargetId = targetId;
            info.RequiredToolId = requiredToolId;
            info.BaseScale = marker.transform.localScale;
            info.BaseLocalPosition = marker.transform.localPosition;

            _spawnedToolActionTargets.Add(marker);

            OseLog.VerboseInfo(
                $"[PartInteraction] Spawned tool target marker '{marker.name}' for target '{info.TargetId}' at local {info.BaseLocalPosition} / world {marker.transform.position}.");
        }

        private void UpdateToolActionTargetVisuals()
        {
            if (_spawnedToolActionTargets.Count == 0)
            {
                _hoveredToolActionTarget = null;
                return;
            }

            _hoveredToolActionTarget = TryGetHoveredToolActionTarget(out ToolActionTargetInfo hoveredTarget)
                ? hoveredTarget.gameObject
                : null;

            Color idlePulseColor = ToolTargetIdleColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * ToolTargetPulseSpeed);
            float intensity = Mathf.Lerp(0.75f, 1.25f, pulse);
            idlePulseColor = new Color(
                Mathf.Clamp01(idlePulseColor.r * intensity),
                Mathf.Clamp01(idlePulseColor.g * intensity),
                Mathf.Clamp01(idlePulseColor.b * intensity),
                Mathf.Clamp01(0.55f + 0.35f * pulse));

            // When the tool cursor is in "ready" state (near a target), make targets glow brighter too.
            bool cursorNearTarget = _toolCursorInReadyState;

            for (int i = _spawnedToolActionTargets.Count - 1; i >= 0; i--)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null)
                {
                    _spawnedToolActionTargets.RemoveAt(i);
                    continue;
                }

                Color targetColor = (target == _hoveredToolActionTarget || cursorNearTarget)
                    ? ToolTargetHoverColor
                    : idlePulseColor;
                MaterialHelper.SetMaterialColor(target, targetColor);

                ToolActionTargetInfo info = target.GetComponent<ToolActionTargetInfo>();
                Vector3 baseScale = info != null && info.BaseScale.sqrMagnitude > 0f
                    ? info.BaseScale
                    : target.transform.localScale;
                float scaleFactor = 1f + (ToolTargetScalePulse * pulse);
                target.transform.localScale = baseScale * scaleFactor;

                Vector3 baseLocalPosition = info != null
                    ? info.BaseLocalPosition
                    : target.transform.localPosition;
                target.transform.localPosition = baseLocalPosition + (Vector3.up * (ToolTargetHeightPulse * (pulse - 0.5f)));
            }
        }

        /// <summary>
        /// Checks screen-space proximity between the pointer (or tool cursor) and tool action targets.
        /// When near: tool cursor turns green ("ready") and target glows brighter.
        /// When overlapping: auto-triggers the tool action (same as clicking).
        /// Works with or without a tool ghost indicator — uses pointer position directly.
        /// </summary>
        private void UpdateToolCursorProximity()
        {
            if (_spawnedToolActionTargets.Count == 0)
            {
                if (_toolCursorInReadyState)
                    RestoreToolCursorColor();
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                if (_toolCursorInReadyState)
                    RestoreToolCursorColor();
                return;
            }

            // Compute the tool cursor's screen position from its world position (most accurate)
            // Also get pointer screen position as a fallback
            Vector2 cursorScreen = Vector2.zero;
            bool hasCursorScreen = false;
            if (_toolGhostIndicator != null && _toolGhostIndicator.activeSelf)
            {
                Vector3 cursorWorldScreen = cam.WorldToScreenPoint(_toolGhostIndicator.transform.position);
                if (cursorWorldScreen.z > 0f)
                {
                    cursorScreen = new Vector2(cursorWorldScreen.x, cursorWorldScreen.y);
                    hasCursorScreen = true;
                }
            }

            Vector2 pointerScreen = Vector2.zero;
            bool hasPointer = TryGetPointerPosition(out pointerScreen);

            if (!hasCursorScreen && !hasPointer)
            {
                if (_toolCursorInReadyState)
                    RestoreToolCursorColor();
                return;
            }

            float closestScreenDist = float.MaxValue;
            ToolActionTargetInfo closestTarget = null;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null) continue;

                Vector3 targetScreen = cam.WorldToScreenPoint(target.transform.position);
                if (targetScreen.z <= 0f) continue;

                Vector2 targetScreen2D = new Vector2(targetScreen.x, targetScreen.y);

                // Use the CLOSER of: tool cursor screen pos vs pointer screen pos
                float dist = float.MaxValue;
                if (hasCursorScreen)
                    dist = Vector2.Distance(cursorScreen, targetScreen2D);
                if (hasPointer)
                    dist = Mathf.Min(dist, Vector2.Distance(pointerScreen, targetScreen2D));

                if (dist < closestScreenDist)
                {
                    closestScreenDist = dist;
                    closestTarget = target.GetComponent<ToolActionTargetInfo>();
                }
            }

            // Visual feedback: tool cursor turns green when near a target.
            if (closestScreenDist <= ToolCursorScreenProximityReady)
            {
                if (!_toolCursorInReadyState)
                {
                    if (_toolGhostIndicator != null && _toolGhostIndicator.activeSelf)
                        MaterialHelper.ApplyToolCursor(_toolGhostIndicator, ToolCursorReadyColor);
                    _toolCursorInReadyState = true;
                }
            }
            else if (_toolCursorInReadyState)
            {
                RestoreToolCursorColor();
            }

        }

        private void RestoreToolCursorColor()
        {
            _toolCursorInReadyState = false;
            if (_toolGhostIndicator != null)
                MaterialHelper.ApplyToolCursor(_toolGhostIndicator, ToolCursorColor);
        }

        private void ClearToolActionTargets()
        {
            if (_hoveredToolActionTarget != null)
                _hoveredToolActionTarget = null;

            for (int i = _spawnedToolActionTargets.Count - 1; i >= 0; i--)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker == null)
                    continue;

                Destroy(marker);
            }

            _spawnedToolActionTargets.Clear();
        }

        private bool TryGetPrimaryToolActionSnapshot(
            out ToolRuntimeController.ToolActionSnapshot snapshot,
            out MachineSessionController session)
        {
            snapshot = default;
            session = null;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out session) ||
                session == null ||
                session.Package == null ||
                session.ToolController == null)
            {
                return false;
            }

            return session.ToolController.TryGetPrimaryActionSnapshot(out snapshot);
        }

        private void TryWarnMissingPrimaryToolActionSnapshot()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session == null)
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return;

            OseLog.Warn($"[PartInteraction] Active step '{step.id}' has required tool actions, but no primary tool action snapshot was available.");
        }

        private bool TryResolveFallbackStepToolActionTarget(
            out MachineSessionController session,
            out string requiredToolId,
            out string targetId)
        {
            session = null;
            requiredToolId = null;
            targetId = null;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out session) ||
                session == null ||
                session.Package == null)
            {
                return false;
            }

            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.CurrentStepDefinition;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return false;

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null)
                    continue;

                if (string.IsNullOrWhiteSpace(requiredToolId) && !string.IsNullOrWhiteSpace(action.toolId))
                    requiredToolId = action.toolId.Trim();
                if (string.IsNullOrWhiteSpace(targetId) && !string.IsNullOrWhiteSpace(action.targetId))
                    targetId = action.targetId.Trim();
            }

            if (string.IsNullOrWhiteSpace(requiredToolId) && step.relevantToolIds != null)
            {
                for (int i = 0; i < step.relevantToolIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(step.relevantToolIds[i]))
                    {
                        requiredToolId = step.relevantToolIds[i].Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(targetId) && step.targetIds != null)
            {
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(step.targetIds[i]))
                    {
                        targetId = step.targetIds[i].Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(requiredToolId) && session.ToolController != null)
                requiredToolId = session.ToolController.ActiveToolId;

            return !string.IsNullOrWhiteSpace(targetId);
        }

        private bool TryGetHoveredToolActionTarget(out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            if (!TryGetPointerPosition(out Vector2 screenPos))
                return false;

            return TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        private bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            // Layer 1: Direct raycast (now hits enlarged collider).
            // Explicitly ignore triggers so ghost trigger colliders don't block the ray.
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                targetInfo = FindToolActionTargetFromHit(hit.transform);
                if (targetInfo != null)
                    return true;
            }

            // Layer 2: Screen-space proximity fallback.
            return TryGetNearestToolTargetByScreenProximity(screenPos, out targetInfo);
        }

        /// <summary>
        /// Returns the world position of the nearest tool action target within screen proximity.
        /// Called by V2 orchestrator (via LegacyBridgeAdapter reflection) to focus the camera
        /// on a pulsating sphere even when no tool is equipped.
        /// </summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!TryGetNearestToolTargetByScreenProximity(screenPos, out ToolActionTargetInfo info))
                return false;
            worldPos = info.transform.position;
            return true;
        }

        private bool TryGetNearestToolTargetByScreenProximity(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            Camera cam = Camera.main;
            if (cam == null) return false;

            float threshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop;
            float closestDist = threshold;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null) continue;

                Vector3 sp = cam.WorldToScreenPoint(target.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    var info = target.GetComponent<ToolActionTargetInfo>();
                    if (info != null)
                        targetInfo = info;
                }
            }
            return targetInfo != null;
        }

        private static ToolActionTargetInfo FindToolActionTargetFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                ToolActionTargetInfo info = hitTransform.GetComponent<ToolActionTargetInfo>();
                if (info != null)
                    return info;

                hitTransform = hitTransform.parent;
            }

            return null;
        }

        private bool TryResolveToolActionTargetPose(
            MachinePackageDefinition package,
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one * 0.25f;

            TargetPreviewPlacement targetPlacement = _spawner.FindTargetPlacement(targetId);
            if (targetPlacement != null)
            {
                position = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                rotation = !targetPlacement.rotation.IsIdentity
                    ? new Quaternion(targetPlacement.rotation.x, targetPlacement.rotation.y, targetPlacement.rotation.z, targetPlacement.rotation.w)
                    : Quaternion.identity;
                scale = new Vector3(targetPlacement.scale.x, targetPlacement.scale.y, targetPlacement.scale.z);
                return true;
            }

            if (package != null &&
                package.TryGetTarget(targetId, out TargetDefinition targetDef) &&
                !string.IsNullOrWhiteSpace(targetDef.associatedPartId))
            {
                PartPreviewPlacement partPlacement = _spawner.FindPartPlacement(targetDef.associatedPartId);
                if (partPlacement != null)
                {
                    position = new Vector3(partPlacement.playPosition.x, partPlacement.playPosition.y, partPlacement.playPosition.z);
                    rotation = !partPlacement.playRotation.IsIdentity
                        ? new Quaternion(partPlacement.playRotation.x, partPlacement.playRotation.y, partPlacement.playRotation.z, partPlacement.playRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(partPlacement.playScale.x, partPlacement.playScale.y, partPlacement.playScale.z);
                    return true;
                }
            }

            return false;
        }

        private bool TryGetGhostTargetPose(
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _spawnedGhosts.Count == 0)
                return false;

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;

            for (int i = _spawnedGhosts.Count - 1; i >= 0; i--)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null)
                    continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = ghost.transform;
                if (previewRoot != null)
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

        private static Vector3 ResolveToolTargetMarkerScale(Vector3 sourceScale)
        {
            float dominant = Mathf.Max(sourceScale.x, Mathf.Max(sourceScale.y, sourceScale.z));
            float uniform = Mathf.Clamp(dominant * 0.85f, 0.30f, 0.75f);
            return Vector3.one * uniform;
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

        private void PushPartInfoToUI(string partId, bool isHoverInfo = false)
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

            string displayName = part.GetDisplayName();
            string functionText = part.function ?? string.Empty;
            string materialText = part.material ?? string.Empty;
            string searchTerms = part.searchTerms != null ? string.Join(" ", part.searchTerms) : string.Empty;

            if (isHoverInfo && ui is UIRootCoordinator hoverAwareUi)
            {
                hoverAwareUi.ShowHoverPartInfoShell(
                    displayName,
                    functionText,
                    materialText,
                    toolNames,
                    searchTerms);
            }
            else
            {
                ui.ShowPartInfoShell(
                    displayName,
                    functionText,
                    materialText,
                    toolNames,
                    searchTerms);
            }
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
            if (!Application.isPlaying || _spawner == null || _isDragging || IsToolModeLockedForParts())
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
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
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

        private bool IsPartMovementLocked(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId))
                return false;

            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return partController.IsPartLockedForMovement(partId);

            PartPlacementState localState = GetPartState(partId);
            return localState == PartPlacementState.PlacedVirtually ||
                localState == PartPlacementState.Completed;
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

        private sealed class ToolActionTargetInfo : MonoBehaviour
        {
            public string TargetId;
            public string RequiredToolId;
            public Vector3 BaseScale;
            public Vector3 BaseLocalPosition;
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
