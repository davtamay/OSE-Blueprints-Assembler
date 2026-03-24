using System;
using System.Collections.Generic;
using System.Reflection;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Input;
using OSE.Interaction;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;
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
    public sealed class PartInteractionBridge : MonoBehaviour, IPartActionBridge, IToolGhostProvider, IPersistentToolManager
    {
        private static readonly Color SelectedPartColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly Color GrabbedPartColor = new Color(1.0f, 0.65f, 0.1f, 1.0f);
        private static readonly Color HoveredPartColor = new Color(0.60f, 0.82f, 1.0f, 1.0f);
        private static readonly Color DimmedPartColor = new Color(0.58f, 0.58f, 0.58f, 1.0f);
        private static readonly Color ActiveStepEmission = new Color(0.15f, 0.35f, 0.6f);
        private static readonly Color GhostReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);
        private static readonly Color HintHighlightColorA = new Color(0.95f, 0.85f, 0.2f, 0.4f);
        private static readonly Color HintHighlightColorB = new Color(1.0f, 0.95f, 0.35f, 0.7f);
        private static readonly Color HoveredSubassemblyEmission = new Color(0.05f, 0.16f, 0.28f);
        private static readonly Color SelectedSubassemblyEmission = new Color(0.35f, 0.22f, 0.02f);

        private const float DragThresholdPixels = 5f;
        private const float ScrollDepthSpeed = 0.5f; // units per scroll tick
        private const float PinchDepthSpeed = 0.02f; // units per pixel of pinch delta
        private const float DepthAdjustSpeed = 0.01f; // units per pixel in shift depth mode
        private const float MinDragRayDistance = 0.05f;
        private const float DragViewportMargin = 0.03f;
        private const float DragFloorEpsilon = 0.001f;
        private const float HintHighlightDuration = 6f;
        private const float HintHighlightPulseSpeed = 4f;
        // Toggled automatically by V2 InteractionOrchestrator at runtime via IPartActionBridge.
        // When true, this bridge skips pointer input polling (V2 handles input instead).
        [HideInInspector] public bool ExternalControlEnabled;

        /// <summary>
        /// World position of the last successfully executed tool action target.
        /// Updated by TryExternalToolAction; read by V2 orchestrator via IPartActionBridge to focus camera.
        /// </summary>
        public Vector3 LastToolActionWorldPos => _lastToolActionWorldPos;

        private PackagePartSpawner _spawner;
        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _spawnedGhosts = new List<GameObject>();
        private ToolCursorManager _cursorManager;
        private ToolCursorManager CursorManager => _cursorManager ??= new ToolCursorManager(transform);
        private UseStepHandler _useHandler;
        private ConnectStepHandler _connectHandler;
        private PlaceStepHandler _placeHandler;
        private SubassemblyPlacementController _subassemblyPlacementController;
        private StepExecutionRouter _router;
        private string _lastCameraFramedStepId;
        private float _lastCameraFramedTime;
        [SerializeField] private InputActionRouter _actionRouter;
        [SerializeField] private SelectionService _selectionService;
        private bool _suppressSelectionEvents;
        private GameObject _pendingSelectPart;
        private int _selectionFrame; // frame when last selection happened
        private Vector2 _pointerDownScreenPos;
        private Camera _pointerDownCamera;

        // ── Persistent tools (clamps, fixtures) that remain in scene across steps ──
        private readonly List<PersistentToolInstance> _persistentTools = new();

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
        private GameObject _hintGhost;
        private GameObject _hintSourceProxy;
        private GameObject _externalHoveredPartForUi;
        private float _hintHighlightUntil;
        private HintWorldCanvas _hintWorldCanvas;
        private DockArcVisual _dockArcVisual;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private GameObject _hoveredPart;
        private bool _startupSyncPending;
        private Vector3 _lastToolActionWorldPos;

        // Step-based part visibility â€” only reveal parts when their step activates
        private readonly HashSet<string> _revealedPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeStepPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _partsHiddenOnSpawn;
        private const float PartGridSpacing = 0.6f;         // spacing between grid cells (X and Z)
        private const float PartGridStartZ = -2.8f;        // Z of the first row (closest to camera)
        private const float PartLayoutY = 0.55f;            // height above floor

        // Sequential target ordering â€” tracks which targetId index is active
        // when the step uses targetOrder == "sequential".
        private int _sequentialTargetIndex;
        private bool _isSequentialStep;


        // â”€â”€ Lifecycle â”€â”€

        private void OnEnable()
        {
            _spawner = GetComponent<PackagePartSpawner>();
            _setup = GetComponent<PreviewSceneSetup>();
            _useHandler ??= new UseStepHandler(
                _spawner,
                () => _setup,
                () => CursorManager,
                _spawnedGhosts,
                GetCurrentSequentialTargetId,
                () => AdvanceSequentialTarget());
            _router ??= new StepExecutionRouter();
            _router.Register(StepFamily.Use, _useHandler);
            _router.Register(StepFamily.Confirm, new ConfirmStepHandler());
            _connectHandler ??= new ConnectStepHandler(_spawner, () => _setup, () => CursorManager, FindSpawnedPart);
            _router.Register(StepFamily.Connect, _connectHandler);
            _subassemblyPlacementController ??= new SubassemblyPlacementController(
                _spawner,
                () => _setup,
                FindSpawnedPart,
                GetPartState);
            ServiceRegistry.Register<ISubassemblyPlacementService>(_subassemblyPlacementController);
            _placeHandler ??= new PlaceStepHandler(
                _spawner,
                () => _setup,
                FindSpawnedPart,
                partId => _partStates.TryGetValue(partId, out var s) ? s : PartPlacementState.NotIntroduced,
                RestorePartVisual,
                ResetDragState,
                _spawnedGhosts,
                () => _isSequentialStep,
                () => AdvanceSequentialTarget(),
                partGo => _selectionService?.NotifySelected(partGo),
                HandlePlacementSucceeded);
            _router.Register(StepFamily.Place, _placeHandler);
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<HintRequested>(HandleHintRequested);
            RuntimeEventBus.Subscribe<ActiveToolChanged>(HandleActiveToolChanged);
            RuntimeEventBus.Subscribe<SessionRestored>(HandleSessionRestored);
            RuntimeEventBus.Subscribe<StepNavigated>(HandleStepNavigated);

            if (_spawner != null)
                _spawner.PartsReady += HandlePartsReady;

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
            RuntimeEventBus.Unsubscribe<SessionRestored>(HandleSessionRestored);
            RuntimeEventBus.Unsubscribe<StepNavigated>(HandleStepNavigated);

            if (_spawner != null)
                _spawner.PartsReady -= HandlePartsReady;

            if (_actionRouter != null)
                _actionRouter.OnAction -= HandleCanonicalAction;

            if (_selectionService != null)
            {
                _selectionService.OnSelected -= HandleSelectionServiceSelected;
                _selectionService.OnDeselected -= HandleSelectionServiceDeselected;
                _selectionService.OnInspected -= HandleSelectionServiceInspected;
            }

            ClearPartHoverVisual();
            ClearDockArcVisual();
            _partStates.Clear();
            _revealedPartIds.Clear();
            _activeStepPartIds.Clear();
            _partsHiddenOnSpawn = false;
            ClearToolGhostIndicator();
            ClearToolActionTargets();
            _router?.CleanupAll();
            _subassemblyPlacementController?.Dispose();
            _subassemblyPlacementController = null;
            ServiceRegistry.Unregister<ISubassemblyPlacementService>();
            _startupSyncPending = false;
        }

        private void Update()
        {
            // Startup sync must run even during intro so parts are revealed
            TrySyncStartupState();

            // Block all interaction while the intro overlay is displayed
            if (SessionDriver.IsIntroActive)
            {
                ClearDockArcVisual();
                return;
            }

            HandlePointerInput();
            // Snap/flash/ghost-pulse/required-part-pulse handled by PlaceStepHandler.Update via router
            UpdateXRGhostProximity();
            if (!ExternalControlEnabled)
            {
                UpdatePartHoverVisual();
                UpdatePointerDragSelectionVisual();
            }
            UpdateSelectedSubassemblyVisual();
            UpdateDockArcVisual();
            UpdateHintHighlight();
            // Stop ghost pulse when dragging starts — proximity highlight takes over
            if (_draggedPart != null)
                _placeHandler?.StopGhostSelectionPulse();
            UpdateToolGhostIndicatorPosition();
            if (TryBuildHandlerContext(out var updateCtx))
                _router.Update(in updateCtx, Time.deltaTime);
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
                    {
                        StepDefinition[] completedSteps = GetCompletedSteps(
                            session,
                            session.SessionState != null ? session.SessionState.CompletedStepCount : 0);
                        RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);
                    }
                }
            }
            _startupSyncPending = false;
        }

        // Ã¢â€â‚¬Ã¢â€â‚¬ Canonical actions Ã¢â€â‚¬Ã¢â€â‚¬

        private void HandleCanonicalAction(CanonicalAction action)
        {
            if (SessionDriver.IsIntroActive) return;

            switch (action)
            {
                case CanonicalAction.Select:
                    if (ExternalControlEnabled)
                        break;
                    TrySelectFromPointer(isInspect: false);
                    break;
                case CanonicalAction.Inspect:
                    if (ExternalControlEnabled)
                        break;
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
            selected = NormalizeSelectablePlacementTarget(selected);

            bool canGrab = true;
            if (IsSpawnedPart(selected) && ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
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
            selected = NormalizeSelectablePlacementTarget(selected);

            AttemptPlacementForSelection(selected);

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
            if (IsRepositioning()) return;
            OseLog.VerboseInfo("[PartInteraction] HandleConfirmOrToolPrimaryAction invoked.");

            // Let registered handlers consume the action first (e.g. ConfirmStepHandler).
            if (TryBuildHandlerContext(out var actionCtx))
            {
                if (_router.TryHandlePointerAction(in actionCtx))
                    return;
            }

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null)
                return;

            bool stepHasToolActions = step.requiredToolActions != null && step.requiredToolActions.Length > 0;
            bool allowToolActionStepCompletion = !step.IsPlacement;

            if (stepHasToolActions)
            {
                OseLog.Info($"[PartInteraction] V1 tool action path: step='{step.id}', allowCompletion={allowToolActionStepCompletion}, spawnedTargets={_useHandler?.SpawnedTargetCount ?? 0}.");
                if (TryExecuteToolPrimaryActionFromPointer(session, stepController, allowToolActionStepCompletion))
                    return;

                OseLog.Info($"[PartInteraction] V1 tool action path: TryExecuteToolPrimaryActionFromPointer returned false.");
                if (step.IsToolAction)
                {
                    if ((_useHandler?.SpawnedTargetCount ?? 0) > 0)
                        FlashToolTargetOnFailure();

                    return;
                }
            }

            // Non-tool-action steps: try tool action first (for measurement/equip steps).
            // Confirm steps are handled by the router dispatch above (ConfirmStepHandler).
            if (TryExecuteToolPrimaryActionFromPointer(session, stepController))
                return;
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
        /// Called by V2 orchestrator via IPartActionBridge. Attempts placement for the
        /// already-selected part at the given screen position without re-deriving click intent.
        /// </summary>
        public bool TryExternalClickToPlace(GameObject selectedPart, Vector2 screenPos)
        {
            if (!Application.isPlaying || !ExternalControlEnabled)
                return false;

            return TryHandleClickToPlace(selectedPart, screenPos);
        }

        /// <summary>
        /// Called by V2 orchestrator via IPartActionBridge when tool mode is locked.
        /// Resolves the executable tool target for the current click without executing it.
        /// </summary>
        public bool TryResolveExternalToolActionTarget(Vector2 screenPos, out string interactedTargetId, out Vector3 targetWorldPos, out Vector3 surfaceWorldPos, out Vector3 weldAxis, out float weldLength)
        {
            interactedTargetId = null;
            targetWorldPos = Vector3.zero;
            surfaceWorldPos = Vector3.zero;
            weldAxis = Vector3.zero;
            weldLength = 0f;

            if (!Application.isPlaying)
                return false;

            ToolActionTargetInfo resolvedTarget = null;
            if (!TryGetToolActionTargetForExecution(screenPos, out resolvedTarget) || resolvedTarget == null)
                return false;

            interactedTargetId = resolvedTarget.TargetId;
            targetWorldPos = resolvedTarget.transform.position;
            surfaceWorldPos = resolvedTarget.SurfaceWorldPos;
            weldAxis = resolvedTarget.WeldAxis;
            weldLength = resolvedTarget.WeldLength;
            return !string.IsNullOrWhiteSpace(interactedTargetId);
        }

        /// <summary>
        /// Called by V2 orchestrator via IPartActionBridge when tool mode is locked.
        /// Executes the tool primary action for an explicitly resolved target id.
        /// </summary>
        public bool TryExecuteExternalToolAction(string interactedTargetId)
        {
            if (!Application.isPlaying)
                return false;

            return TryHandleExternalToolAction(interactedTargetId);
        }

        /// <summary>
        /// Called by V2 orchestrator via IPartActionBridge when tool mode is locked.
        /// Directly executes the tool primary action using a direct hit on a spawned
        /// tool target sphere, bypassing the canonical action router to avoid wiring failures.
        /// </summary>
        public bool TryExternalToolAction(Vector2 screenPos)
        {
            if (!Application.isPlaying)
                return false;

            // Pipe connection steps: handle even when a tool is held.
            if (TryBuildHandlerContext(out var pipeCtx) && _router.TryHandlePointerDown(in pipeCtx, screenPos))
                return true;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            bool allowToolActionStepCompletion = step == null || !step.IsPlacement;

            int spawnedTargetCount = _useHandler?.SpawnedTargetCount ?? 0;
            OseLog.Info($"[PartInteraction] TryExternalToolAction at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={spawnedTargetCount}. Tool='{session.ToolController?.ActiveToolId ?? "none"}'.");
            if (!TryResolveExternalToolActionTarget(screenPos, out string interactedTargetId, out Vector3 targetWorldPos, out _, out _, out _))
            {
                OseLog.Info($"[PartInteraction] TryExternalToolAction: no ready tool target resolved at ({screenPos.x:F0},{screenPos.y:F0}). Spawned={spawnedTargetCount}.");
                return false;
            }

            // Capture world position before executing (the target may be destroyed/refreshed after).
            _lastToolActionWorldPos = targetWorldPos;

            return TryHandleExternalToolAction(interactedTargetId);
        }

        private bool TryHandleExternalToolAction(string interactedTargetId)
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            bool allowToolActionStepCompletion = step == null || !step.IsPlacement;

            int spawnedTargetCount = _useHandler?.SpawnedTargetCount ?? 0;
            if (string.IsNullOrWhiteSpace(interactedTargetId))
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: no target id provided. Spawned={spawnedTargetCount}.");
                return false;
            }

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: action rejected for '{interactedTargetId}'.");
                return false;
            }

            if (!handled)
            {
                OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: not handled for '{interactedTargetId}'.");
                return false;
            }

            OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: success on '{interactedTargetId}'. shouldComplete={shouldCompleteStep}, allowCompletion={allowToolActionStepCompletion}.");
            if (!allowToolActionStepCompletion)
                return true;

            OseLog.Info($"[PartInteraction] TryExecuteExternalToolAction: calling HandleToolPrimaryResult shouldComplete={shouldCompleteStep}.");
            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        /// <summary>
        /// Called by V2 orchestrator via IPartActionBridge for any tap (regardless of
        /// selection or tool state). Delegates to ConnectStepHandler via the router.
        /// Returns true if a port sphere was hit and the interaction was consumed.
        /// </summary>
        public bool TryExternalPipeConnection(Vector2 screenPos)
        {
            if (!Application.isPlaying)
                return false;

            if (!TryBuildHandlerContext(out var ctx))
                return false;

            return _connectHandler != null && _connectHandler.TryHandlePointerDown(in ctx, screenPos);
        }

        public GameObject NormalizeExternalSelectableTarget(GameObject target)
        {
            if (!Application.isPlaying)
                return target;

            return NormalizeSelectablePlacementTarget(target);
        }

        /// <summary>
        /// Called by V2 orchestrator via IPartActionBridge while external control is enabled.
        /// Shows hovered-part info while hovering; when hover clears, restores selected-part
        /// info if any, otherwise hides the panel.
        /// </summary>
        public void SetExternalHoveredPart(GameObject hoveredPart)
        {
            if (!Application.isPlaying || !ExternalControlEnabled)
                return;

            hoveredPart = NormalizeSelectablePlacementTarget(hoveredPart);

            if (IsToolModeLockedForParts())
            {
                ClearPartHoverVisual();
                _externalHoveredPartForUi = null;
                if (ServiceRegistry.TryGet<IPresentationAdapter>(out var toolModeUi) && toolModeUi is UIRootCoordinator toolModeHoverUi)
                    toolModeHoverUi.ClearHoverPartInfo();
                return;
            }

            bool hoverChanged = hoveredPart != _externalHoveredPartForUi;
            if (hoverChanged)
            {
                ClearPartHoverVisual();
                if (hoveredPart != null && CanApplyHoverVisual(hoveredPart, hoveredPart.name))
                {
                    _hoveredPart = hoveredPart;
                    ApplyHoveredPartVisual(_hoveredPart);
                }
            }

            _externalHoveredPartForUi = hoveredPart;

            if (_externalHoveredPartForUi != null)
            {
                if (IsSubassemblyProxy(_externalHoveredPartForUi))
                    PushSubassemblyInfoToUI(_externalHoveredPartForUi, isHoverInfo: true);
                else
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
                selected = NormalizeSelectablePlacementTarget(selected);
                if (IsSubassemblyProxy(selected))
                    PushSubassemblyInfoToUI(selected);
                else
                    PushPartInfoToUI(selected.name);
            }
            else if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                ui.HidePartInfoPanel();
            }
        }

        // ── IPartActionBridge explicit implementations ──
        // Maps interface names to the existing "External"-prefixed methods.

        bool IPartActionBridge.ExternalControlEnabled
        {
            get => ExternalControlEnabled;
            set => ExternalControlEnabled = value;
        }

        GameObject IPartActionBridge.NormalizeSelectableTarget(GameObject target)
            => NormalizeExternalSelectableTarget(target);

        bool IPartActionBridge.TryClickToPlace(GameObject selectedPart, Vector2 screenPos)
            => TryExternalClickToPlace(selectedPart, screenPos);

        bool IPartActionBridge.TryToolAction(Vector2 screenPos)
            => TryExternalToolAction(screenPos);

        bool IPartActionBridge.TryToolAction(string targetId)
            => TryExecuteExternalToolAction(targetId);

        bool IPartActionBridge.TryResolveToolActionTarget(
            Vector2 screenPos, out string targetId, out Vector3 worldPos,
            out Vector3 surfaceWorldPos, out Vector3 weldAxis, out float weldLength)
            => TryResolveExternalToolActionTarget(screenPos, out targetId, out worldPos,
                out surfaceWorldPos, out weldAxis, out weldLength);

        bool IPartActionBridge.TryPipeConnection(Vector2 screenPos)
            => TryExternalPipeConnection(screenPos);

        void IPartActionBridge.SetHoveredPart(GameObject part)
            => SetExternalHoveredPart(part);

        bool IPartActionBridge.TryGetStepFocusBounds(string stepId, out Bounds bounds)
        {
            bounds = default;
            if (!TryResolveStepFocusBounds(stepId, out bounds))
                return false;
            bounds.Expand(new Vector3(0.18f, 0.12f, 0.18f));
            return true;
        }

        // ── Tool Action Preview bridge methods ──

        /// <summary>Returns the tool cursor ghost GameObject, or null if none is active.</summary>
        public GameObject GetToolGhost() => CursorManager.ToolGhost;

        /// <summary>Number of tool targets completed in the current step (for I Do / We Do / You Do resolution).</summary>
        public int GetCompletedToolTargetCount() => _useHandler?.CompletedTargetCountForStep ?? 0;

        /// <summary>Increments the completed target count after a preview completes.</summary>
        public void IncrementCompletedToolTargetCount() => _useHandler?.IncrementCompletedTargetCount();

        /// <summary>Suspends/resumes cursor position updates during tool action preview.</summary>
        public void SetToolGhostPositionSuspended(bool suspended) => CursorManager.PositionUpdateSuspended = suspended;

        /// <summary>Returns the active profile for the current Use step, or null.</summary>
        public string GetActiveToolProfile()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return null;
            var stepCtrl = session?.AssemblyController?.StepController;
            return stepCtrl != null && stepCtrl.HasActiveStep ? stepCtrl.CurrentStepDefinition?.profile : null;
        }

        /// <summary>Returns the currently equipped tool ID, or null.</summary>
        public string GetActiveToolId()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return null;
            return session?.ToolController?.ActiveToolId;
        }

        private bool TryFocusCameraOnToolTarget(Vector2 screenPos)
            => _useHandler != null && _useHandler.TryFocusCameraOnToolTarget(screenPos);

        private void FlashToolTargetOnFailure()
            => _useHandler?.FlashToolTargetOnFailure();

        private bool TryExecuteToolPrimaryActionFromPointer(
            MachineSessionController session,
            StepController stepController,
            bool allowStepCompletion = true)
            => _useHandler != null && _useHandler.TryExecuteToolPrimaryActionFromPointer(session, stepController, allowStepCompletion);

        private bool TryExecuteToolPrimaryAction(
            string interactedTargetId,
            out bool shouldCompleteStep,
            out bool handled)
        {
            if (_useHandler != null)
                return _useHandler.TryExecuteToolPrimaryAction(interactedTargetId, out shouldCompleteStep, out handled);

            shouldCompleteStep = false;
            handled = false;
            return false;
        }

        private bool HandleToolPrimaryResult(
            MachineSessionController session,
            StepController stepController,
            bool shouldCompleteStep)
            => UseStepHandler.HandleToolPrimaryResult(session, stepController, shouldCompleteStep);

        private void RefreshToolActionTargets()
            => _useHandler?.RefreshToolActionTargets();

        private void TrySelectFromPointer(bool isInspect)
        {
            if (ExternalControlEnabled)
            {
                _pendingSelectPart = null;
                return;
            }

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

            _pendingSelectPart = null;
        }

        private void HandleSelectionServiceSelected(GameObject target) =>
            HandleSelectionServiceSelection(target, isInspect: false);

        private void HandleSelectionServiceInspected(GameObject target) =>
            HandleSelectionServiceSelection(target, isInspect: true);

        private void HandleSelectionServiceSelection(GameObject target, bool isInspect)
        {
            target = NormalizeSelectablePlacementTarget(target);
            if (_suppressSelectionEvents || target == null)
                return;

            if (IsToolModeLockedForParts())
            {
                DeselectFromSelectionService();
                return;
            }

            if (!IsSelectablePlacementObject(target))
                return;

            bool accepted;
            string selectionId = ResolveSelectionId(target);
            if (IsSubassemblyProxy(target))
            {
                accepted = !string.IsNullOrWhiteSpace(selectionId);
                if (accepted)
                {
                    PushSubassemblyInfoToUI(target, isHoverInfo: false);
                    ClearPartHoverVisual();
                    ApplySelectedPartVisual(target);
                }
            }
            else
            {
                if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                    return;

                accepted = isInspect
                    ? partController.InspectPart(target.name)
                    : partController.SelectPart(target.name);
            }

            if (!accepted)
            {
                DeselectFromSelectionService();
                return;
            }

            OseLog.Info($"[PartInteraction] Selected item '{selectionId ?? target.name}'");
            _selectionFrame = Time.frameCount;
            StartGhostSelectionPulse(selectionId ?? target.name);
            if (!IsSubassemblyProxy(target))
                TryAutoCompleteSelectionStep(target.name);

            if (_pointerDown && _pendingSelectPart == target)
            {
                if (IsPartMovementLocked(selectionId))
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag tracking blocked for locked item '{selectionId}'.");
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
            target = NormalizeSelectablePlacementTarget(target);
            if (_suppressSelectionEvents)
                return;

            if (IsSubassemblyProxy(target))
            {
                RestorePartVisual(target);
            }
            else if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
            {
                partController.DeselectPart();
            }

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                ui.HidePartInfoPanel();

            StopGhostSelectionPulse();
            ResetDragState();
        }

        private void DeselectFromSelectionService()
        {
            if (_selectionService == null)
                return;

            GameObject current = NormalizeSelectablePlacementTarget(_selectionService.CurrentSelection);
            if (IsSubassemblyProxy(current))
            {
                RestorePartVisual(current);
                if (_hoveredPart == current)
                    _hoveredPart = null;
            }

            _suppressSelectionEvents = true;
            _selectionService.Deselect();
            _suppressSelectionEvents = false;
        }

        private void HandlePlacementSucceeded(GameObject target)
        {
            target = NormalizeSelectablePlacementTarget(target);
            if (!IsSubassemblyProxy(target))
                return;

            ClearPartHoverVisual();
            _externalHoveredPartForUi = null;
            StopGhostSelectionPulse();
            ResetDragState();

            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                partController.DeselectPart();

            RestorePartVisual(target);
            DeselectFromSelectionService();
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

            return RaycastSelectableObject(cam.ScreenPointToRay(screenPos));
        }

        // â”€â”€ Pointer input (mouse + touch) â”€â”€

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
            if (IsRepositioning()) return;

            // Click on a pulsating tool target sphere â†’ focus camera on it
            TryFocusCameraOnToolTarget(screenPos);

            if (TryHandleToolActionPointerDown(screenPos))
                return;

            // Let registered handlers consume the pointer-down first (e.g. ConnectStepHandler, PlaceStepHandler).
            if (TryBuildHandlerContext(out var pointerCtx))
            {
                if (_router.TryHandlePointerDown(in pointerCtx, screenPos))
                    return;
            }

            if (TryHandleClickToPlace(screenPos))
                return;

            Camera cam = Camera.main;
            if (cam == null) return;

            GameObject matchedPart = RaycastSelectableObject(cam.ScreenPointToRay(screenPos));
            if (matchedPart == null)
                return;

            string matchedSelectionId = ResolveSelectionId(matchedPart);
            if (string.IsNullOrWhiteSpace(matchedSelectionId))
                return;

            if (IsPartMovementLocked(matchedSelectionId))
            {
                OseLog.VerboseInfo($"[PartInteraction] Drag blocked for locked item '{matchedSelectionId}'. Selection is still allowed.");

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
                else if (!IsSubassemblyProxy(matchedPart) && ServiceRegistry.TryGet<PartRuntimeController>(out var lockedPartController))
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
                if (IsSubassemblyProxy(matchedPart))
                {
                    if (_selectionService != null)
                    {
                        _selectionService.NotifySelected(matchedPart);
                    }
                    else
                    {
                        OseLog.Info($"[PartInteraction] Selected subassembly '{matchedSelectionId}'");
                        if (!IsPartMovementLocked(matchedSelectionId))
                            BeginDragTracking(matchedPart);
                    }
                }
                else if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                {
                    bool selected = partController.SelectPart(matchedPart.name);
                    if (selected)
                    {
                        OseLog.Info($"[PartInteraction] Selected part '{matchedPart.name}'");
                        if (!IsPartMovementLocked(matchedSelectionId))
                            BeginDragTracking(matchedPart);
                    }
                }
            }
        }

        private void HandlePointerDrag(Vector2 screenPos)
        {
            if (IsRepositioning()) return;
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
                if (!IsSubassemblyProxy(_draggedPart) && ServiceRegistry.TryGet<PartRuntimeController>(out var pc))
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

            // Touch pinch â†’ push/pull along camera forward (mobile)
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
            _subassemblyPlacementController?.ApplyProxyTransform(_draggedPart);

            // Check proximity to ghosts and highlight when in snap zone
            UpdateGhostProximity();
        }

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (IsRepositioning()) return;
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

            AttemptPlacementForSelection(_draggedPart);
        }

        private void AttemptPlacementForSelection(GameObject targetGo)
        {
            if (targetGo == null)
                return;

            string selectionId = ResolveSelectionId(targetGo);
            if (string.IsNullOrWhiteSpace(selectionId))
                return;

            _placeHandler?.AttemptPlacement(targetGo, selectionId);
        }

        // â”€â”€ Click-to-place â”€â”€

        /// <summary>
        /// Attempts click-to-place: if a part is selected and the pointer hits (or is near)
        /// a matching ghost target, snap the part there without requiring drag.
        /// </summary>
        private bool TryHandleClickToPlace(Vector2 screenPos)
        {
            GameObject selected = _selectionService != null ? _selectionService.CurrentSelection : null;
            return TryHandleClickToPlace(selected, screenPos);
        }

        private bool TryHandleClickToPlace(GameObject selected, Vector2 screenPos)
        {
            if (_spawnedGhosts.Count == 0 || _placeHandler == null)
                return false;

            // Need a selected part that isn't being dragged.
            if (selected == null || _draggedPart != null)
                return false;
            selected = NormalizeSelectablePlacementTarget(selected);

            // Skip click-to-place on the same frame the part was selected.
            if (Time.frameCount == _selectionFrame)
                return false;

            string selectionId = ResolveSelectionId(selected);
            if (!IsSelectablePlacementObject(selected) || string.IsNullOrWhiteSpace(selectionId))
                return false;

            if (IsPartMovementLocked(selectionId))
                return false;

            return _placeHandler.TryClickToPlace(selectionId, selected, screenPos);
        }

        // Ghost raycast, screen proximity, and click-to-place execution
        // are now owned by PlaceStepHandler.

        // â”€â”€ Ghost proximity detection â”€â”€

        private void UpdateGhostProximity()
        {
            if (_draggedPart == null || _spawnedGhosts.Count == 0)
                return;

            _placeHandler?.UpdateDragProximity(_draggedPart, _draggedPartId, _isDragging);
        }
        private void ClearGhostHighlight() => _placeHandler?.ClearGhostHighlight();

        private void StartGhostSelectionPulse(string partId) => _placeHandler?.StartGhostSelectionPulse(partId);

        private void StopGhostSelectionPulse() => _placeHandler?.StopGhostSelectionPulse();

        // â”€â”€ Required-part pulse (highlights parts the user needs to grab) â”€â”€

        private void ClearRequiredPartEmission() => _placeHandler?.ClearRequiredPartEmission();

        /// <summary>
        /// Shows and positions all parts belonging to the current step's subassembly.
        /// When the first step of a subassembly activates, all parts for that entire
        /// subassembly appear at once â€” matching how a real workbench is organized.
        /// Parts are arranged in an arc on the near side of the floor,
        /// keeping the center clear for the machine being assembled.
        /// </summary>
        private void HideNonIntroducedParts()
        {
            if (_partsHiddenOnSpawn) return;
            _partsHiddenOnSpawn = true;

            var parts = _spawner?.SpawnedParts;
            if (parts == null) return;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null) continue;

                string partId = parts[i].name;

                // Keep completed/placed parts visible
                if (_partStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                    continue;

                // Keep already-revealed parts visible
                if (_revealedPartIds.Contains(partId))
                    continue;

                parts[i].SetActive(false);
            }

            OseLog.Info($"[PartInteraction] Hid non-introduced parts for hybrid presentation.");
        }

        private void RevealStepParts(string stepId)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Determine which subassembly we're in
            string subassemblyId = step.subassemblyId;

            // Collect all part ids for this subassembly (from all its steps)
            var subassemblyPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(subassemblyId))
            {
                StepDefinition[] allSteps = package.GetOrderedSteps();
                for (int s = 0; s < allSteps.Length; s++)
                {
                    if (!string.Equals(allSteps[s].subassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string[] rp = allSteps[s].requiredPartIds;
                    if (rp == null) continue;
                    for (int p = 0; p < rp.Length; p++)
                    {
                        if (!string.IsNullOrWhiteSpace(rp[p]))
                            subassemblyPartIds.Add(rp[p]);
                    }
                }
            }
            else
            {
                // No subassembly â€” fall back to just this step's parts
                string[] rp = step.requiredPartIds;
                if (rp != null)
                {
                    for (int p = 0; p < rp.Length; p++)
                    {
                        if (!string.IsNullOrWhiteSpace(rp[p]))
                            subassemblyPartIds.Add(rp[p]);
                    }
                }
            }

            if (subassemblyPartIds.Count == 0)
                return;

            // Filter to parts not yet revealed
            var toReveal = new List<string>();
            foreach (string partId in subassemblyPartIds)
            {
                if (_partStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                {
                    _revealedPartIds.Add(partId);
                    continue;
                }

                if (!_revealedPartIds.Contains(partId))
                    toReveal.Add(partId);
            }

            if (toReveal.Count == 0)
                return;

            // Activate, position, and style each newly-revealed part.
            // Use the authored startPosition/Rotation/Scale from previewConfig when
            // available so parts stage near the assembly (matching edit-mode layout).
            // Fall back to a computed row for parts without authored placements.
            var unplacedParts = new List<(string partId, GameObject go, float width)>();

            for (int i = 0; i < toReveal.Count; i++)
            {
                string partId = toReveal[i];
                GameObject partGo = FindSpawnedPart(partId);
                if (partGo == null) continue;

                PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
                Vector3 scale = pp != null
                    ? new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z)
                    : Vector3.one;

                partGo.transform.localScale = scale;
                partGo.SetActive(true);

                bool hasAuthored = pp != null &&
                    (!Mathf.Approximately(pp.startPosition.x, 0f) ||
                     !Mathf.Approximately(pp.startPosition.y, 0f) ||
                     !Mathf.Approximately(pp.startPosition.z, 0f));

                if (hasAuthored)
                {
                    Vector3 pos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    Quaternion rot = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
                    partGo.transform.SetLocalPositionAndRotation(pos, rot);
                }
                else
                {
                    partGo.transform.localRotation = Quaternion.identity;

                    // Measure extents for fallback row layout
                    float width = PartGridSpacing;
                    var renderers = partGo.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        Bounds combined = renderers[0].bounds;
                        for (int r = 1; r < renderers.Length; r++)
                            combined.Encapsulate(renderers[r].bounds);
                        width = Mathf.Max(combined.size.x, combined.size.z, PartGridSpacing);
                    }
                    unplacedParts.Add((partId, partGo, width));
                }

                _partStates[partId] = PartPlacementState.Available;
                SyncPartGrabInteractivity(partGo, partId);
                ApplyPartVisualForState(partGo, partId, PartPlacementState.Available);
                _revealedPartIds.Add(partId);
            }

            // Fallback row layout for parts without authored start positions.
            if (unplacedParts.Count > 0)
            {
                float padding = 0.15f;
                float totalWidth = 0f;
                for (int i = 0; i < unplacedParts.Count; i++)
                    totalWidth += unplacedParts[i].width + (i > 0 ? padding : 0f);

                float cursor = -totalWidth * 0.5f;
                for (int i = 0; i < unplacedParts.Count; i++)
                {
                    var (_, partGo, width) = unplacedParts[i];
                    float x = cursor + width * 0.5f;
                    cursor += width + padding;
                    partGo.transform.localPosition = new Vector3(x, PartLayoutY, PartGridStartZ);
                }
            }

            OseLog.Info($"[PartInteraction] Revealed {toReveal.Count} part(s) for subassembly '{subassemblyId}'.");
        }

        /// <summary>
        /// Highlights the active step's required parts with emission glow and dims
        /// previously-revealed parts that belong to the subassembly but aren't needed
        /// for the current step.
        /// </summary>
        private void ApplyStepPartHighlighting(string stepId)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Build set of the current step's active parts.
            // For finished-subassembly placement steps, the whole required subassembly
            // is the active object, not an empty requiredPartIds list.
            _activeStepPartIds.Clear();
            if (step.RequiresSubassemblyPlacement &&
                package.TryGetSubassembly(step.requiredSubassemblyId, out var requiredSubassembly) &&
                requiredSubassembly?.partIds != null)
            {
                for (int i = 0; i < requiredSubassembly.partIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(requiredSubassembly.partIds[i]))
                        _activeStepPartIds.Add(requiredSubassembly.partIds[i]);
                }
            }
            else if (step.requiredPartIds != null)
            {
                for (int i = 0; i < step.requiredPartIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(step.requiredPartIds[i]))
                        _activeStepPartIds.Add(step.requiredPartIds[i]);
                }
            }

            // Walk all revealed parts: highlight active, dim the rest
            foreach (string partId in _revealedPartIds)
            {
                // Skip completed/placed parts — they already have their own visual
                if (_partStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                    continue;

                GameObject partGo = FindSpawnedPart(partId);
                if (partGo == null) continue;

                if (_activeStepPartIds.Contains(partId))
                {
                    // Active step part: restore normal visual + soft emission highlight
                    ApplyAvailablePartVisual(partGo, partId);
                    MaterialHelper.SetEmission(partGo, ActiveStepEmission);
                }
                else
                {
                    // Revealed but not needed right now: dim it
                    ClearRendererPropertyBlocks(partGo);
                    if (MaterialHelper.IsImportedModel(partGo))
                        MaterialHelper.ApplyTint(partGo, DimmedPartColor);
                    else
                        MaterialHelper.Apply(partGo, "Preview Part Material", DimmedPartColor);
                    MaterialHelper.SetEmission(partGo, Color.black);
                }
            }
        }

        // RefreshRequiredPartIds and UpdateRequiredPartPulse are now owned by PlaceStepHandler
        // (called via router lifecycle: OnStepActivated/Update).

        private void UpdateHintHighlight()
        {
            if ((_hintGhost == null && _hintSourceProxy == null) || _hintHighlightUntil <= 0f)
                return;

            if (Time.time >= _hintHighlightUntil)
            {
                ClearHintHighlight();
                return;
            }

            Color pulseColor = ColorPulseHelper.Lerp(HintHighlightColorA, HintHighlightColorB, HintHighlightPulseSpeed);

            if (_hintGhost != null)
            {
                if (!(_placeHandler != null && _placeHandler.IsGhostHighlighted && _placeHandler.HoveredGhost == _hintGhost))
                    MaterialHelper.SetMaterialColor(_hintGhost, pulseColor);
            }

            if (_hintSourceProxy != null)
                ForEachProxyMember(_hintSourceProxy, member => ApplyHintSourceVisual(member, pulseColor));
        }

        private void ClearHintHighlight()
        {
            if (_hintGhost != null)
            {
                if (_placeHandler != null && _placeHandler.IsGhostHighlighted && _placeHandler.HoveredGhost == _hintGhost)
                {
                    MaterialHelper.Apply(_hintGhost, "Ghost Ready Material", GhostReadyColor);
                }
                else
                {
                    MaterialHelper.ApplyGhost(_hintGhost);
                }
            }

            if (_hintSourceProxy != null)
                RestorePartVisual(_hintSourceProxy);

            _hintGhost = null;
            _hintSourceProxy = null;
            _hintHighlightUntil = 0f;
        }

        /// <summary>
        /// Returns the world position of the nearest ghost target matching the given part ID.
        /// Used by the V2 orchestrator to pivot the camera toward the placement target.
        /// </summary>
        public bool TryGetGhostWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            return _placeHandler != null && _placeHandler.TryGetGhostWorldPosForPart(partId, out worldPos);
        }

        private GhostPlacementInfo FindNearestGhostForPart(string partId, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (_placeHandler == null) return null;
            return _placeHandler.FindNearestGhostForSelection(partId, worldPos, out nearestDist);
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
        // Snap animation, flash invalid, and their update loops are now
        // owned by PlaceStepHandler (run via router.Update).

        // â”€â”€ Runtime event handlers â”€â”€

        private void HandleSessionRestored(SessionRestored evt)
        {
            if (evt.CompletedStepCount <= 0) return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepDefinition[] completedSteps = GetCompletedSteps(session, evt.CompletedStepCount);
            string activeStepId = GetActiveStepId(session);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

            OseLog.Info($"[PartInteraction] Restored visual state for {completedSteps.Length} completed steps.");
        }

        /// <summary>
        /// Called when PackagePartSpawner finishes spawning all parts (including
        /// async GLB models). If the session was restored, re-applies completed-part
        /// positioning because the async spawn + PositionParts may have overwritten
        /// restore positions with start/arc positions.
        /// </summary>
        private void HandlePartsReady()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            if (session.SessionState == null || !session.SessionState.IsRestored)
                return;

            int completedCount = session.SessionState.CompletedStepCount;
            if (completedCount <= 0) return;

            StepDefinition[] completedSteps = GetCompletedSteps(session, completedCount);
            string activeStepId = GetActiveStepId(session);
            RebuildVisualStateForActiveStep(completedSteps, activeStepId, resetToDefaultView: true);

            OseLog.Info($"[PartInteraction] Re-applied restore positioning after async part spawn ({completedSteps.Length} steps).");
        }

        /// <summary>
        /// Handles step navigation (back/forward). Repositions all parts based on
        /// their recomputed states: completed parts move to play positions,
        /// available parts return to their arc layout positions, and future parts
        /// are hidden. The subsequent StepStateChanged(Active) spawns new ghosts.
        /// </summary>
        private void HandleStepNavigated(StepNavigated evt)
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            var package = _spawner?.CurrentPackage;
            if (package == null) return;
            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            int targetGlobalIndex = Mathf.Clamp(evt.TargetStepIndex, 0, Mathf.Max(orderedSteps.Length - 1, 0));

            // Clear current visual state
            _router?.CleanupAll();
            ClearGhosts();
            ClearToolActionTargets();
            ClearRequiredPartEmission();
            _connectHandler?.ClearTransientVisuals();
            _revealedPartIds.Clear();
            _subassemblyPlacementController?.ResetReplayState();

            StepDefinition[] completedSteps = Array.Empty<StepDefinition>();
            if (targetGlobalIndex > 0 && orderedSteps.Length > 0)
            {
                completedSteps = new StepDefinition[targetGlobalIndex];
                Array.Copy(orderedSteps, completedSteps, targetGlobalIndex);
            }

            if (completedSteps.Length > 0)
            {
                RestoreCompletedStepParts(completedSteps);
                _subassemblyPlacementController?.RestoreCompletedPlacements(completedSteps);
            }

            if (targetGlobalIndex < orderedSteps.Length)
                RevertFutureStepParts(orderedSteps, targetGlobalIndex);

            OseLog.Info($"[PartInteraction] Navigated from global step {evt.PreviousStepIndex + 1} to {evt.TargetStepIndex + 1}: repositioned parts.");
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            // On any step transition, force-clear drag state so nothing gets stuck.
            ResetDragState();

            // FailedAttempt is a transient state within the same step (Active â†’ FailedAttempt â†’ Active).
            // Preserve ghosts and sequential progress for both the transition INTO FailedAttempt
            // and the auto-return back to Active.
            bool isFailRelated = evt.Current == StepState.FailedAttempt
                              || (evt.Current == StepState.Active && evt.Previous == StepState.FailedAttempt);

            if (!isFailRelated)
            {
                ClearHintHighlight();
                ClearToolActionTargets();

                // Reset sequential tracking only on genuine new-step transitions.
                _isSequentialStep = false;
                _sequentialTargetIndex = 0;

                // Connect-step markers are transient step visuals and must not leak
                // into unrelated steps or navigation states.
                _connectHandler?.ClearTransientVisuals();
            }

            if (evt.Current == StepState.Active)
            {
                if (isFailRelated)
                {
                    // Ghost and sequential state are still valid â€” skip re-spawn.
                    OseLog.VerboseInfo($"[PartInteraction] Step '{evt.StepId}' re-activated after failed attempt â€” keeping {_spawnedGhosts.Count} ghost(s).");
                }
                else
                {
                    // Clear any stale SelectionService selection so the dedup guard in
                    // NotifySelected doesn’t block re-selection of the same part on the
                    // new step (e.g. beam selected on step 2 â†’ must be selectable again on step 3).
                    DeselectFromSelectionService();

                    // Hybrid presentation: hide all parts on first step, then reveal per-subassembly
                    HideNonIntroducedParts();
                    RevealStepParts(evt.StepId);
                    ApplyStepPartHighlighting(evt.StepId);
                    _subassemblyPlacementController?.RefreshForStep(evt.StepId);

                    SpawnGhostsForStep(evt.StepId);
                    if (TryBuildHandlerContext(out var activatedCtx))
                        _router.OnStepActivated(in activatedCtx);
                    FocusCameraOnStepArea(evt.StepId);
                    OseLog.VerboseInfo($"[PartInteraction] Step ‘{evt.StepId}’ active: spawned {_spawnedGhosts.Count} ghost(s).");
                }
            }
            else if (evt.Current == StepState.Completed)
            {
                if (TryBuildHandlerContextForStep(evt.StepId, out var completedCtx))
                    _router.OnStepCompleted(in completedCtx);

                var package = _spawner?.CurrentPackage;
                if (package != null &&
                    package.TryGetStep(evt.StepId, out var completedStep) &&
                    completedStep != null &&
                    completedStep.RequiresSubassemblyPlacement &&
                    _subassemblyPlacementController != null &&
                    !string.IsNullOrWhiteSpace(completedStep.requiredSubassemblyId) &&
                    _subassemblyPlacementController.TryGetProxy(completedStep.requiredSubassemblyId, out GameObject completedProxy))
                {
                    RestorePartVisual(completedProxy);
                }

                DeselectFromSelectionService();
                ClearPartHoverVisual();
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                    partController.DeselectPart();

                MoveStepPartsToPlayPosition(evt.StepId);
                _subassemblyPlacementController?.HandleStepCompleted(evt.StepId);
                ClearGhosts();
                // Handler clears required-part emission via OnStepCompleted
            }

            // Handler refreshes required-part IDs via OnStepActivated

            RefreshToolGhostIndicator();
            RefreshToolActionTargets();
            if (IsToolModeLockedForParts())
                ClearPartHoverVisual();
        }

        private static string GetActiveStepId(MachineSessionController session)
        {
            StepController stepController = session?.AssemblyController?.StepController;
            if (stepController != null && stepController.HasActiveStep)
            {
                string stepId = stepController.CurrentStepState.StepId;
                if (!string.IsNullOrWhiteSpace(stepId))
                    return stepId;
            }

            return session?.SessionState?.CurrentStepId;
        }

        private StepDefinition[] GetCompletedSteps(MachineSessionController session, int completedCount)
        {
            if (session == null || completedCount <= 0)
                return Array.Empty<StepDefinition>();

            MachinePackageDefinition package = _spawner?.CurrentPackage ?? session.Package;
            StepDefinition[] orderedSteps = package?.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return Array.Empty<StepDefinition>();

            int clamped = Math.Min(completedCount, orderedSteps.Length);
            if (clamped <= 0)
                return Array.Empty<StepDefinition>();

            StepDefinition[] result = new StepDefinition[clamped];
            Array.Copy(orderedSteps, result, clamped);
            return result;
        }

        private void RebuildVisualStateForActiveStep(StepDefinition[] completedSteps, string activeStepId, bool resetToDefaultView)
        {
            if (string.IsNullOrWhiteSpace(activeStepId))
                return;

            _router?.CleanupAll();
            ClearGhosts();
            ClearToolActionTargets();
            ClearRequiredPartEmission();
            _connectHandler?.ClearTransientVisuals();
            _revealedPartIds.Clear();
            _activeStepPartIds.Clear();
            ClearPartHoverVisual();
            _subassemblyPlacementController?.ResetReplayState();

            if (completedSteps != null && completedSteps.Length > 0)
            {
                RestoreCompletedStepParts(completedSteps);
                _subassemblyPlacementController?.RestoreCompletedPlacements(completedSteps);
            }

            HideNonIntroducedParts();
            RevealStepParts(activeStepId);
            ApplyStepPartHighlighting(activeStepId);
            _subassemblyPlacementController?.RefreshForStep(activeStepId);

            SpawnGhostsForStep(activeStepId);
            if (TryBuildHandlerContext(out var rebuildCtx))
                _router.OnStepActivated(in rebuildCtx);

            FocusCameraOnStepArea(activeStepId, resetToDefaultView);
            RefreshToolGhostIndicator();
            RefreshToolActionTargets();
        }

        private void FocusCameraOnStepArea(string stepId, bool resetToDefaultView = false)
        {
            // When V2 owns interaction, camera framing is handled by StepGuidanceService.
            if (ExternalControlEnabled)
                return;

            // Debounce: skip if the same step was already framed within 0.5s.
            // Multiple startup paths (StepStateChanged, SessionRestored, TrySyncStartupState)
            // all trigger framing for the same step, causing visible re-animation.
            float now = Time.unscaledTime;
            if (string.Equals(_lastCameraFramedStepId, stepId, StringComparison.Ordinal)
                && now - _lastCameraFramedTime < 0.5f)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(stepId) || !TryResolveStepFocusBounds(stepId, out Bounds focusBounds))
            {
                OseLog.Info($"[FocusCamera] Step '{stepId}' — no bounds resolved, skipping.");
                return;
            }

            _lastCameraFramedStepId = stepId;
            _lastCameraFramedTime = now;

            focusBounds.Expand(new Vector3(0.18f, 0.12f, 0.18f));
            OseLog.Info($"[FocusCamera] Step '{stepId}' — bounds center={focusBounds.center}, size={focusBounds.size}");

            if (resetToDefaultView)
                TryInvokeCameraMethod("ResetToDefault", Array.Empty<object>());

            if (TryInvokeCameraMethod("FrameBounds", new object[] { focusBounds }, typeof(Bounds)))
            {
                OseLog.Info($"[FocusCamera] Step '{stepId}' — FrameBounds applied.");
                return;
            }

            OseLog.Info($"[FocusCamera] Step '{stepId}' — FrameBounds failed, falling back to FocusOn.");
            TryInvokeCameraMethod("FocusOn", new object[] { focusBounds.center, -1f }, typeof(Vector3), typeof(float));
            TryInvokeCameraMethod("FocusOn", new object[] { focusBounds.center }, typeof(Vector3));
        }

        private bool TryResolveStepFocusBounds(string stepId, out Bounds bounds)
        {
            bounds = default;

            MachinePackageDefinition package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out StepDefinition step) || step == null)
                return false;

            Bounds accumulatedBounds = default;
            bool hasBounds = false;
            int ghostCount = 0, partCount = 0, toolTargetCount = 0, fallbackTargetCount = 0;

            void Encapsulate(Bounds candidate)
            {
                if (!hasBounds)
                {
                    accumulatedBounds = candidate;
                    hasBounds = true;
                    return;
                }

                accumulatedBounds.Encapsulate(candidate);
            }

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null)
                    continue;

                ghostCount++;
                if (TryGetRenderableBounds(ghost, out Bounds ghostBounds))
                    Encapsulate(ghostBounds);
                else
                    Encapsulate(new Bounds(ghost.transform.position, Vector3.one * 0.08f));
            }

            HashSet<string> focusPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectStepFocusPartIds(package, step, focusPartIds);

            foreach (string partId in focusPartIds)
            {
                GameObject partGo = FindSpawnedPart(partId);
                if (partGo == null || !partGo.activeInHierarchy)
                    continue;

                partCount++;
                if (TryGetRenderableBounds(partGo, out Bounds partBounds))
                    Encapsulate(partBounds);
                else
                    Encapsulate(new Bounds(partGo.transform.position, Vector3.one * 0.08f));
            }

            if (step.RequiresSubassemblyPlacement &&
                _subassemblyPlacementController != null &&
                _subassemblyPlacementController.TryGetProxy(step.requiredSubassemblyId, out GameObject proxy) &&
                proxy != null)
            {
                if (TryGetRenderableBounds(proxy, out Bounds proxyBounds))
                    Encapsulate(proxyBounds);
                else
                    Encapsulate(new Bounds(proxy.transform.position, Vector3.one * 0.18f));
            }

            if (_useHandler != null && _useHandler.TryGetSpawnedTargetBounds(out Bounds toolTargetBounds))
            {
                toolTargetCount++;
                Encapsulate(toolTargetBounds);
            }

            // Always include target positions from previewConfig — not just as a fallback.
            // For Use steps, tool action targets may not be spawned yet and this may be
            // the only spatial data available.
            if (step.targetIds != null && step.targetIds.Length > 0)
            {
                Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    TargetPreviewPlacement targetPlacement = _spawner.FindTargetPlacement(step.targetIds[i]);
                    if (targetPlacement == null)
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            // Also include requiredToolActions target positions (these are often on
            // different targets than targetIds, e.g. individual weld/bolt points).
            if (step.requiredToolActions != null)
            {
                Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
                for (int i = 0; i < step.requiredToolActions.Length; i++)
                {
                    ToolActionDefinition action = step.requiredToolActions[i];
                    if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                        continue;

                    TargetPreviewPlacement targetPlacement = _spawner.FindTargetPlacement(action.targetId);
                    if (targetPlacement == null)
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            // Enforce a minimum bounds size so the camera gives a "third person"
            // overview with enough surrounding context visible.
            if (hasBounds)
            {
                const float minSize = 1.0f; // minimum full-size in any axis
                Vector3 size = accumulatedBounds.size;
                size.x = Mathf.Max(size.x, minSize);
                size.y = Mathf.Max(size.y, minSize);
                size.z = Mathf.Max(size.z, minSize);
                accumulatedBounds.size = size;
            }

            OseLog.Info($"[FocusBounds] Step '{stepId}' — ghosts={ghostCount}, parts={partCount}/{focusPartIds.Count}, toolTargets={toolTargetCount}, fallbackTargets={fallbackTargetCount}, hasBounds={hasBounds}");

            if (hasBounds)
                bounds = accumulatedBounds;

            return hasBounds;
        }

        private static void CollectStepFocusPartIds(MachinePackageDefinition package, StepDefinition step, HashSet<string> results)
        {
            if (package == null || step == null || results == null)
                return;

            if (!string.IsNullOrWhiteSpace(step.subassemblyId))
            {
                StepDefinition[] allSteps = package.GetOrderedSteps();
                for (int i = 0; i < allSteps.Length; i++)
                {
                    StepDefinition candidate = allSteps[i];
                    if (candidate == null ||
                        !string.Equals(candidate.subassemblyId, step.subassemblyId, StringComparison.OrdinalIgnoreCase) ||
                        candidate.requiredPartIds == null)
                    {
                        continue;
                    }

                    for (int p = 0; p < candidate.requiredPartIds.Length; p++)
                    {
                        string partId = candidate.requiredPartIds[p];
                        if (!string.IsNullOrWhiteSpace(partId))
                            results.Add(partId);
                    }
                }

                return;
            }

            if (step.requiredPartIds == null)
                return;

            for (int i = 0; i < step.requiredPartIds.Length; i++)
            {
                string partId = step.requiredPartIds[i];
                if (!string.IsNullOrWhiteSpace(partId))
                    results.Add(partId);
            }
        }

        private static bool TryInvokeCameraMethod(string methodName, object[] args, params Type[] signature)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return false;

            Transform current = cam.transform;
            while (current != null)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    MethodInfo method = component.GetType().GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: signature,
                        modifiers: null);

                    if (method == null)
                        continue;

                    try
                    {
                        method.Invoke(component, args);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        string message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        OseLog.Warn($"[PartInteraction] Camera method '{methodName}' failed on '{component.GetType().Name}': {message}");
                    }
                }

                current = current.parent;
            }

            return false;
        }

        private bool TryBuildHandlerContext(out StepHandlerContext context)
        {
            context = default;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
                return false;
            var step = stepCtrl.CurrentStepDefinition;
            context = new StepHandlerContext(step, stepCtrl, step.id, session.GetElapsedSeconds());
            return true;
        }

        /// <summary>
        /// Builds a handler context for a specific step ID (e.g. a just-completed step
        /// that may no longer be the active step on the controller).
        /// </summary>
        private bool TryBuildHandlerContextForStep(string stepId, out StepHandlerContext context)
        {
            context = default;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;
            var stepCtrl = session.AssemblyController?.StepController;
            if (stepCtrl == null)
                return false;
            var package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return false;
            context = new StepHandlerContext(step, stepCtrl, stepId, session.GetElapsedSeconds());
            return true;
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
            SyncPartGrabInteractivity(partGo, evt.PartId);
            ApplyPartVisualForState(partGo, evt.PartId, evt.Current);

            if (_hoveredPart == partGo && CanApplyHoverVisual(partGo, evt.PartId))
                ApplyHoveredPartVisual(partGo);

            if (evt.Current == PartPlacementState.Selected || evt.Current == PartPlacementState.Inspected)
            {
                PushPartInfoToUI(evt.PartId);
            }

            // Remove placed parts from the required-part pulse list
            if (evt.Current == PartPlacementState.PlacedVirtually || evt.Current == PartPlacementState.Completed)
            {
                RemoveFromRequiredPartIds(evt.PartId);
            }
        }

        private void RemoveFromRequiredPartIds(string partId) => _placeHandler?.RemoveFromRequiredPartIds(partId);

        private void HandleHintRequested(HintRequested evt)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null)
                return;

            if (!package.TryGetStep(evt.StepId, out var step))
                return;

            HintDefinition hint = ResolveHintForStep(package, step, evt.TotalHintsForStep);
            if (hint == null && !step.RequiresSubassemblyPlacement)
                return;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui) && !ui.IsHintDisplayAllowed)
                return;

            string hintTitle = hint?.title;
            string hintMessage = hint?.message;
            Transform targetTransform = ResolveHintTargetTransform(hint);
            GameObject sourceProxy = null;
            GameObject targetGhost = targetTransform != null ? targetTransform.gameObject : null;

            if (TryBuildSubassemblyHintPresentation(
                    step,
                    hint,
                    out string stackTitle,
                    out string stackMessage,
                    out Transform stackAnchor,
                    out sourceProxy,
                    out GameObject stackGhost))
            {
                hintTitle = stackTitle;
                hintMessage = stackMessage;
                targetTransform = stackAnchor;
                targetGhost = stackGhost;
            }

            if (ui != null)
                ui.ShowHintContent(hintTitle, hintMessage, hint?.type);

            if (targetTransform != null)
            {
                _hintGhost = targetGhost;
                _hintSourceProxy = sourceProxy;
                _hintHighlightUntil = Time.time + HintHighlightDuration;
                if (_hintGhost != null)
                    MaterialHelper.ApplyGhost(_hintGhost);

                if (_hintWorldCanvas == null)
                    _hintWorldCanvas = FindFirstObjectByType<HintWorldCanvas>();

                if (_hintWorldCanvas == null)
                {
                    var go = new GameObject("Hint World Canvas");
                    _hintWorldCanvas = go.AddComponent<HintWorldCanvas>();
                }

                _hintWorldCanvas.ShowHint(hint?.type, hintTitle, hintMessage, targetTransform);
            }
        }

        // â”€â”€ Ghost parts â”€â”€

        private void SpawnGhostsForStep(string stepId)
        {
            ClearGhosts();
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Pipe connection steps are handled entirely by ConnectStepHandler via the router.
            if (step.IsPipeConnection)
                return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0)
                return;

            _isSequentialStep = step.IsSequential;
            _sequentialTargetIndex = 0;

            if (_isSequentialStep)
            {
                // Sequential: spawn only the first target's ghost.
                SpawnGhostForTarget(package, targetIds[0]);
            }
            else
            {
                // Parallel (default): spawn all ghosts at once.
                foreach (string targetId in targetIds)
                    SpawnGhostForTarget(package, targetId);
            }
        }

        /// <summary>
        /// Called after a part is placed on a ghost in sequential mode.
        /// Advances to the next target and spawns its ghost/tool-target,
        /// or returns true if all targets are done.
        /// </summary>
        private bool AdvanceSequentialTarget()
        {
            if (!_isSequentialStep) return false;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return true;

            var package = session.Package;
            StepController stepController = session.AssemblyController?.StepController;
            if (package == null || stepController == null || !stepController.HasActiveStep)
                return true;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step?.targetIds == null) return true;

            _sequentialTargetIndex++;
            if (_sequentialTargetIndex >= step.targetIds.Length)
                return true; // all targets done

            SpawnGhostForTarget(package, step.targetIds[_sequentialTargetIndex]);
            RefreshToolActionTargets();
            return false;
        }

        /// <summary>
        /// Returns the target ID that is currently active in sequential mode,
        /// or null if not in sequential mode or index is out of range.
        /// </summary>
        private string GetCurrentSequentialTargetId()
        {
            if (!_isSequentialStep) return null;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return null;

            StepDefinition step = session.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step?.targetIds == null || _sequentialTargetIndex >= step.targetIds.Length)
                return null;

            return step.targetIds[_sequentialTargetIndex];
        }

        private void SpawnGhostForTarget(MachinePackageDefinition package, string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) { OseLog.Warn("[PartInteraction] SpawnGhostForTarget: targetId is null/empty."); return; }
            if (!package.TryGetTarget(targetId, out var target)) { OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: target '{targetId}' not found in package."); return; }

            if (!string.IsNullOrWhiteSpace(target.associatedSubassemblyId))
            {
                SpawnGhostForSubassemblyTarget(package, targetId, target);
                return;
            }

            string associatedPartId = target.associatedPartId;
            if (string.IsNullOrEmpty(associatedPartId)) { OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: target '{targetId}' has no associatedPartId."); return; }
            if (!package.TryGetPart(associatedPartId, out var part)) { OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: part '{associatedPartId}' not found in package."); return; }

            // Skip ghost for parts already placed or completed from a prior step.
            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
            {
                var partState = partController.GetPartState(associatedPartId);
                if (partState == PartPlacementState.Completed ||
                    partState == PartPlacementState.PlacedVirtually)
                {
                    OseLog.VerboseInfo($"[PartInteraction] SpawnGhostForTarget: skipping ghost for '{associatedPartId}' â€” already {partState}.");
                    return;
                }
                OseLog.VerboseInfo($"[PartInteraction] SpawnGhostForTarget: part '{associatedPartId}' state={partState}, proceeding with ghost.");
            }

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;

            TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
            PartPreviewPlacement pp = _spawner.FindPartPlacement(associatedPartId);

            // --- Spline parts: create a procedural ghost tube instead of loading a GLB ---
            if (SplinePartFactory.HasSplineData(pp))
            {
                GameObject splineGhost = SplinePartFactory.CreateGhost(associatedPartId, pp.splinePath, previewRoot);
                if (splineGhost == null)
                {
                    OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: failed to create spline ghost for '{associatedPartId}'.");
                    return;
                }

                GhostPlacementInfo splineInfo = splineGhost.AddComponent<GhostPlacementInfo>();
                splineInfo.TargetId = targetId;
                splineInfo.PartId = associatedPartId;

                // Spline ghost sits at play position (0,0,0) / scale (1,1,1) — knots define the routing
                splineGhost.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                splineGhost.transform.localScale = Vector3.one;

                // Convert existing colliders to triggers for click-to-place
                foreach (var col in splineGhost.GetComponentsInChildren<Collider>(true))
                    col.isTrigger = true;

                MaterialHelper.ApplyGhost(splineGhost);
                _spawnedGhosts.Add(splineGhost);
                OseLog.Info($"[PartInteraction] Spline ghost spawned for '{associatedPartId}' at target '{targetId}'. Total ghosts: {_spawnedGhosts.Count}");
                return;
            }

            // --- Standard GLB-based ghost ---
            string ghostRef = part.assetRef;
            if (string.IsNullOrEmpty(ghostRef)) { OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: part '{associatedPartId}' has no assetRef."); return; }

            Vector3 ghostPos;
            Quaternion ghostRot;
            Vector3 ghostScale;

            // playPosition is the single source of truth for where a part ends up
            // when placed. Ghost must appear at the same location so there is no
            // discrepancy between the ghost preview and the actual placement.
            // TargetPreviewPlacement is only used as fallback for targets without
            // an associated part placement (tool-action targets, checkpoints, etc.).
            if (pp != null)
            {
                ghostPos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                ghostRot = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;
            }
            else if (tp != null)
            {
                ghostPos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                ghostRot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;
            }
            else
            {
                ghostPos = Vector3.zero;
                ghostRot = Quaternion.identity;
            }

            // Ghost should mirror the live source part dimensions exactly.
            GameObject sourcePart = FindSpawnedPart(associatedPartId);
            if (sourcePart != null)
            {
                ghostScale = sourcePart.transform.localScale;
            }
            else if (pp != null)
            {
                Vector3 authoredStartScale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                Vector3 authoredPlayScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                ghostScale = authoredStartScale.sqrMagnitude > 0f
                    ? authoredStartScale
                    : (authoredPlayScale.sqrMagnitude > 0f ? authoredPlayScale : Vector3.one);
            }
            else if (tp != null)
            {
                ghostScale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
            }
            else
            {
                ghostScale = Vector3.one * 0.5f;
            }

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

            // Fit a BoxCollider to the combined renderer bounds so the trigger
            // is accurate to the mesh shape rather than an oversized sphere.
            var ghostRenderers = ghost.GetComponentsInChildren<Renderer>(true);
            var clickCollider = ghost.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            if (ghostRenderers.Length > 0)
            {
                Bounds combined = ghostRenderers[0].bounds;
                for (int ri = 1; ri < ghostRenderers.Length; ri++)
                    combined.Encapsulate(ghostRenderers[ri].bounds);
                Vector3 lossyScale = ghost.transform.lossyScale;
                clickCollider.center = ghost.transform.InverseTransformPoint(combined.center);
                clickCollider.size = new Vector3(
                    lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                    lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                    lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            }

            MaterialHelper.ApplyGhost(ghost);
            _spawnedGhosts.Add(ghost);
            OseLog.Info($"[PartInteraction] Ghost spawned for '{associatedPartId}' at target '{targetId}' pos={ghostPos} scale={ghostScale}. Total ghosts: {_spawnedGhosts.Count}");
        }

        private void SpawnGhostForSubassemblyTarget(MachinePackageDefinition package, string targetId, TargetDefinition target)
        {
            string subassemblyId = target.associatedSubassemblyId;
            if (string.IsNullOrWhiteSpace(subassemblyId))
                return;

            if (_subassemblyPlacementController == null || !_subassemblyPlacementController.IsSubassemblyReady(subassemblyId))
            {
                OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: subassembly '{subassemblyId}' is not ready for placement.");
                return;
            }

            if (!package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) || subassembly == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: subassembly '{subassemblyId}' not found in package.");
                return;
            }

            SubassemblyPreviewPlacement frame = _spawner.FindSubassemblyPlacement(subassemblyId);
            if (frame == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnGhostForTarget: subassembly '{subassemblyId}' has no authored preview frame.");
                return;
            }

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            GameObject ghostRoot = new GameObject($"Ghost_{subassemblyId}");
            if (previewRoot != null)
                ghostRoot.transform.SetParent(previewRoot, false);

            if (_subassemblyPlacementController.TryResolveTargetPose(targetId, out Vector3 ghostPos, out Quaternion ghostRot, out Vector3 ghostScale))
            {
                ghostRoot.transform.SetLocalPositionAndRotation(ghostPos, ghostRot);
                ghostRoot.transform.localScale = ghostScale;
            }
            else
            {
                ghostRoot.transform.SetLocalPositionAndRotation(
                    GhostToVector3(frame.position),
                    GhostToQuaternion(frame.rotation));
                ghostRoot.transform.localScale = GhostSanitizeScale(GhostToVector3(frame.scale), Vector3.one);
            }

            Vector3 framePos = GhostToVector3(frame.position);
            Quaternion frameRot = GhostToQuaternion(frame.rotation);
            Vector3 frameScale = GhostSanitizeScale(GhostToVector3(frame.scale), Vector3.one);
            IntegratedSubassemblyPreviewPlacement integratedPlacement = _spawner.FindIntegratedSubassemblyPlacement(subassemblyId, targetId);
            ConstrainedSubassemblyFitPreviewPlacement fitPlacement = _spawner.FindConstrainedSubassemblyFitPlacement(subassemblyId, targetId);
            Vector3 fitAxisLocal = fitPlacement != null ? GhostToVector3(fitPlacement.fitAxisLocal) : Vector3.zero;
            if (fitAxisLocal.sqrMagnitude > 0.000001f)
                fitAxisLocal.Normalize();
            float fitTravel = fitPlacement != null
                ? Mathf.Clamp(
                    fitPlacement.completionTravel,
                    Mathf.Min(fitPlacement.minTravel, fitPlacement.maxTravel),
                    Mathf.Max(fitPlacement.minTravel, fitPlacement.maxTravel))
                : 0f;
            bool isAxisFitGhost = fitPlacement?.drivenPartIds != null && fitPlacement.drivenPartIds.Length > 0;
            HashSet<string> fitDrivenPartIds = isAxisFitGhost
                ? new HashSet<string>(fitPlacement.drivenPartIds, StringComparer.OrdinalIgnoreCase)
                : null;
            var fitGhostChildren = isAxisFitGhost ? new List<Transform>() : null;

            string[] memberIds = subassembly.partIds ?? Array.Empty<string>();
            for (int i = 0; i < memberIds.Length; i++)
            {
                string memberId = memberIds[i];
                if (string.IsNullOrWhiteSpace(memberId) || !package.TryGetPart(memberId, out PartDefinition part))
                    continue;
                if (isAxisFitGhost && !fitDrivenPartIds.Contains(memberId))
                    continue;

                PartPreviewPlacement placement = _spawner.FindPartPlacement(memberId);
                if (placement == null)
                    continue;

                GameObject childGhost = _spawner.TryLoadPackageAsset(part.assetRef);
                if (childGhost == null)
                    childGhost = GameObject.CreatePrimitive(PrimitiveType.Cube);

                childGhost.transform.SetParent(ghostRoot.transform, false);

                Vector3 memberLocalPos;
                Quaternion memberLocalRot;
                Vector3 memberLocalScale;

                if (TryGetIntegratedGhostMemberPlacement(integratedPlacement, memberId, out Vector3 integratedPos, out Quaternion integratedRot, out Vector3 integratedScale))
                {
                    memberLocalPos = GhostInverseTransformPoint(
                        ghostRoot.transform.localPosition,
                        ghostRoot.transform.localRotation,
                        GhostSanitizeScale(ghostRoot.transform.localScale, Vector3.one),
                        integratedPos);
                    memberLocalRot = Quaternion.Inverse(ghostRoot.transform.localRotation) * integratedRot;
                    memberLocalScale = GhostDivideScale(integratedScale, GhostSanitizeScale(ghostRoot.transform.localScale, Vector3.one));
                }
                else
                {
                    Vector3 memberPlayPos = new Vector3(placement.playPosition.x, placement.playPosition.y, placement.playPosition.z);
                    Quaternion memberPlayRot = !placement.playRotation.IsIdentity
                        ? new Quaternion(placement.playRotation.x, placement.playRotation.y, placement.playRotation.z, placement.playRotation.w)
                        : Quaternion.identity;
                    Vector3 memberPlayScale = GhostSanitizeScale(new Vector3(placement.playScale.x, placement.playScale.y, placement.playScale.z), Vector3.one);

                    memberLocalPos = GhostInverseTransformPoint(framePos, frameRot, frameScale, memberPlayPos);
                    memberLocalRot = Quaternion.Inverse(frameRot) * memberPlayRot;
                    memberLocalScale = GhostDivideScale(memberPlayScale, frameScale);
                }

                if (fitPlacement?.drivenPartIds != null &&
                    Array.IndexOf(fitPlacement.drivenPartIds, memberId) >= 0)
                {
                    memberLocalPos += fitAxisLocal * (fitTravel - fitPlacement.minTravel);
                }

                childGhost.transform.SetLocalPositionAndRotation(memberLocalPos, memberLocalRot);
                childGhost.transform.localScale = memberLocalScale;
                if (fitGhostChildren != null)
                    fitGhostChildren.Add(childGhost.transform);

                foreach (Collider collider in childGhost.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
            }

            if (isAxisFitGhost && fitGhostChildren != null && fitGhostChildren.Count > 0)
            {
                Vector3 anchorLocal = Vector3.zero;
                for (int i = 0; i < fitGhostChildren.Count; i++)
                    anchorLocal += fitGhostChildren[i].localPosition;
                anchorLocal /= fitGhostChildren.Count;

                ghostRoot.transform.localPosition = GhostTransformPoint(
                    ghostRoot.transform.localPosition,
                    ghostRoot.transform.localRotation,
                    GhostSanitizeScale(ghostRoot.transform.localScale, Vector3.one),
                    anchorLocal);

                for (int i = 0; i < fitGhostChildren.Count; i++)
                    fitGhostChildren[i].localPosition -= anchorLocal;
            }

            GhostPlacementInfo info = ghostRoot.AddComponent<GhostPlacementInfo>();
            info.TargetId = targetId;
            info.SubassemblyId = subassemblyId;

            ApplyGhostClickCollider(ghostRoot, minAxisSize: isAxisFitGhost ? 0.09f : 0.18f, paddingWorld: isAxisFitGhost ? 0.03f : 0.08f);
            MaterialHelper.ApplyGhost(ghostRoot);
            _spawnedGhosts.Add(ghostRoot);
            OseLog.Info($"[PartInteraction] Composite ghost spawned for subassembly '{subassemblyId}' at target '{targetId}'. Total ghosts: {_spawnedGhosts.Count}");
        }

        private static bool TryGetIntegratedGhostMemberPlacement(
            IntegratedSubassemblyPreviewPlacement integratedPlacement,
            string partId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (integratedPlacement?.memberPlacements == null || string.IsNullOrWhiteSpace(partId))
                return false;

            for (int i = 0; i < integratedPlacement.memberPlacements.Length; i++)
            {
                IntegratedMemberPreviewPlacement member = integratedPlacement.memberPlacements[i];
                if (member == null || !string.Equals(member.partId, partId, StringComparison.OrdinalIgnoreCase))
                    continue;

                position = GhostToVector3(member.position);
                rotation = GhostToQuaternion(member.rotation);
                scale = GhostSanitizeScale(GhostToVector3(member.scale), Vector3.one);
                return true;
            }

            return false;
        }

        private static void ApplyGhostClickCollider(GameObject ghost, float minAxisSize = 0.06f, float paddingWorld = 0f)
        {
            if (ghost == null)
                return;

            Renderer[] ghostRenderers = ghost.GetComponentsInChildren<Renderer>(true);
            BoxCollider clickCollider = ghost.GetComponent<BoxCollider>();
            if (clickCollider == null)
                clickCollider = ghost.AddComponent<BoxCollider>();

            clickCollider.isTrigger = true;
            if (ghostRenderers.Length <= 0)
                return;

            Bounds combined = ghostRenderers[0].bounds;
            for (int ri = 1; ri < ghostRenderers.Length; ri++)
                combined.Encapsulate(ghostRenderers[ri].bounds);

            if (paddingWorld > 0f)
                combined.Expand(paddingWorld);

            Vector3 lossyScale = ghost.transform.lossyScale;
            clickCollider.center = ghost.transform.InverseTransformPoint(combined.center);
            Vector3 localSize = new Vector3(
                lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            clickCollider.size = new Vector3(
                Mathf.Max(localSize.x, minAxisSize),
                Mathf.Max(localSize.y, minAxisSize),
                Mathf.Max(localSize.z, minAxisSize));
        }

        private static Vector3 GhostToVector3(SceneFloat3 value) => new Vector3(value.x, value.y, value.z);

        private static Quaternion GhostToQuaternion(SceneQuaternion value)
        {
            return !value.IsIdentity
                ? new Quaternion(value.x, value.y, value.z, value.w)
                : Quaternion.identity;
        }

        private static Vector3 GhostSanitizeScale(Vector3 value, Vector3 fallback)
        {
            return new Vector3(
                Mathf.Approximately(value.x, 0f) ? fallback.x : value.x,
                Mathf.Approximately(value.y, 0f) ? fallback.y : value.y,
                Mathf.Approximately(value.z, 0f) ? fallback.z : value.z);
        }

        private static Vector3 GhostDivideScale(Vector3 value, Vector3 divisor)
        {
            Vector3 safe = GhostSanitizeScale(divisor, Vector3.one);
            return new Vector3(value.x / safe.x, value.y / safe.y, value.z / safe.z);
        }

        private static Vector3 GhostMultiplyScale(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        private static Vector3 GhostTransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 localPoint)
        {
            return origin + rotation * GhostMultiplyScale(scale, localPoint);
        }

        private static Vector3 GhostInverseTransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 point)
        {
            Vector3 translated = Quaternion.Inverse(rotation) * (point - origin);
            return GhostDivideScale(translated, scale);
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
            => _ = CursorManager.RefreshAsync(_spawner, _setup, _hintGhost == CursorManager.ToolGhost, ClearHintHighlight);

        private void UpdateToolGhostIndicatorPosition()
        {
            if (!TryGetPointerPosition(out Vector2 screenPos))
                screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            CursorManager.UpdatePosition(_isDragging, screenPos);
        }



        private void ClearToolGhostIndicator()
            => CursorManager.Clear(_hintGhost == CursorManager.ToolGhost, ClearHintHighlight);

        private bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            if (session?.ToolController == null)
                return false;

            if (!session.ToolController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot))
                return false;

            if (!snapshot.IsConfigured || snapshot.IsCompleted)
                return false;

            // Mixed placement+tool steps: don't lock parts until all placements are done.
            if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController) &&
                !partController.AreActiveStepRequiredPartsPlaced())
            {
                return false;
            }

            return true;
        }

        private bool TryHandleToolActionPointerDown(Vector2 screenPos)
        {
            if (!IsToolModeLockedForParts())
                return false;

            // Don't block pipe_connection steps — port sphere clicks need to pass through.
            if (_connectHandler != null && _connectHandler.HasActivePortSpheres)
                return false;

            // Block pointer-down from reaching part selection/drag when tool mode is active.
            // The actual tool action execution is handled exclusively by the canonical action
            // path (HandleConfirmOrToolPrimaryAction) to prevent double-execution per click.
            return true;
        }

        private void ClearToolActionTargets()
            => _useHandler?.ClearToolActionTargets();

        private bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            return _useHandler != null && _useHandler.TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        private bool TryGetToolActionTargetForExecution(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            return _useHandler != null && _useHandler.TryResolveToolActionTargetForExecution(screenPos, out targetInfo);
        }

        /// <summary>
        /// Returns the world position of the nearest tool action target within screen proximity.
        /// Called by V2 orchestrator (via IPartActionBridge) to focus the camera
        /// on a pulsating sphere even when no tool is equipped.
        /// </summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            return _useHandler != null && _useHandler.TryGetNearestToolTargetWorldPos(screenPos, out worldPos);
        }

        // TryGetGhostTargetPose is now owned by PlaceStepHandler.

        private void RemoveGhostForPart(string partId)
        {
            // Clear hint highlight if the hint ghost is being removed
            if (_hintGhost != null && !string.IsNullOrEmpty(partId))
            {
                GhostPlacementInfo hInfo = _hintGhost.GetComponent<GhostPlacementInfo>();
                if (hInfo != null && hInfo.MatchesPart(partId))
                    ClearHintHighlight();
            }
            _placeHandler?.RemoveGhostForPart(partId);
        }

        // â”€â”€ Step completion: move parts to assembled position â”€â”€

        private void MoveStepPartsToPlayPosition(string stepId)
        {
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null) return;

            foreach (string partId in partIds)
                MovePartToPlayPosition(partId);
        }

        /// <summary>
        /// Moves parts from all given steps to their play positions and applies
        /// completed visuals. Used by session restore to position parts in bulk
        /// without replaying step events.
        /// </summary>
        public void RestoreCompletedStepParts(StepDefinition[] steps)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || steps == null) return;

            for (int s = 0; s < steps.Length; s++)
            {
                string[] partIds = steps[s].requiredPartIds;
                if (partIds == null) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    MovePartToPlayPosition(partId);

                    GameObject partGo = FindSpawnedPart(partId);
                    if (partGo != null)
                    {
                        // Ensure the part is visible — HideNonIntroducedParts may
                        // have hidden it before the restore path ran.
                        partGo.SetActive(true);
                    }

                    _partStates[partId] = PartPlacementState.Completed;
                    SyncPartGrabInteractivity(partGo, partId);
                    ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                    _revealedPartIds.Add(partId);
                }
            }
        }

        private void MovePartToPlayPosition(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return;

            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
            if (pp == null) return;

            GameObject partGo = FindSpawnedPart(partId);
            if (partGo == null) return;

            Vector3    pPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
            Vector3    pScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
            Quaternion pRot   = !pp.playRotation.IsIdentity
                ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                : Quaternion.identity;

            partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
            partGo.transform.localScale = pScale;
        }

        /// <summary>
        /// Reverts parts from steps at or after <paramref name="fromStepIndex"/> back
        /// to their start positions with available visuals, undoing any play-position
        /// placement. Called during backward navigation so future parts visually rewind.
        /// </summary>
        private void RevertFutureStepParts(StepDefinition[] allSteps, int fromStepIndex)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || allSteps == null) return;

            for (int s = fromStepIndex; s < allSteps.Length; s++)
            {
                string[] partIds = allSteps[s].requiredPartIds;
                if (partIds == null) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    GameObject partGo = FindSpawnedPart(partId);
                    if (partGo == null) continue;

                    // Hide future parts instead of repositioning — they'll be revealed
                    // when their step activates via RevealStepParts.
                    partGo.SetActive(false);
                    _revealedPartIds.Remove(partId);
                    _partStates[partId] = PartPlacementState.NotIntroduced;
                }
            }
        }

        // â”€â”€ Context menu: place selected part at target (debug shortcut) â”€â”€

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

            if (_isSequentialStep)
            {
                if (AdvanceSequentialTarget())
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
            else if (partController.AreActiveStepRequiredPartsPlaced())
            {
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
        }

        // â”€â”€ UI push â”€â”€

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

        // â”€â”€ Helpers â”€â”€

        private void BeginDragTracking(GameObject partGo)
        {
            if (partGo == null)
                return;

            _isDragging = false;
            _draggedPart = partGo;
            _draggedPartId = ResolveSelectionId(partGo);
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
            _draggedPartId = ResolveSelectionId(partGo);
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

        private void UpdateSelectedSubassemblyVisual()
        {
            if (!Application.isPlaying || _selectionService == null)
                return;

            GameObject selected = NormalizeSelectablePlacementTarget(_selectionService.CurrentSelection);
            if (!IsSubassemblyProxy(selected))
                return;

            ApplySelectedPartVisual(selected);
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
                    return NormalizeSelectablePlacementTarget(partGo);
            }

            return null;
        }

        private bool TryBuildSubassemblyHintPresentation(
            StepDefinition step,
            HintDefinition authoredHint,
            out string title,
            out string message,
            out Transform worldAnchor,
            out GameObject sourceProxy,
            out GameObject targetGhost)
        {
            title = null;
            message = null;
            worldAnchor = null;
            sourceProxy = null;
            targetGhost = null;

            if (step == null || !step.RequiresSubassemblyPlacement || _subassemblyPlacementController == null)
                return false;

            string subassemblyId = step.requiredSubassemblyId;
            if (string.IsNullOrWhiteSpace(subassemblyId) ||
                !_subassemblyPlacementController.TryGetProxy(subassemblyId, out sourceProxy))
            {
                return false;
            }

            string targetId = !string.IsNullOrWhiteSpace(authoredHint?.targetId)
                ? authoredHint.targetId
                : (step.targetIds != null && step.targetIds.Length > 0 ? step.targetIds[0] : null);

            if (!string.IsNullOrWhiteSpace(targetId))
            {
                foreach (GameObject ghost in _spawnedGhosts)
                {
                    if (ghost == null)
                        continue;

                    GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                    if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        targetGhost = ghost;
                        break;
                    }
                }
            }

            if (!_subassemblyPlacementController.TryGetDisplayInfo(sourceProxy, out string displayName, out _))
                displayName = subassemblyId;

            title = $"Move {displayName}";
            message = $"Move the completed {displayName} as one finished panel. Drag the whole frame side toward the highlighted target and it will rotate into place as it docks.";

            worldAnchor = sourceProxy.transform;
            return true;
        }

        private GameObject GetHoveredPartFromMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return null;

            var cam = Camera.main;
            if (cam == null)
                return null;

            return RaycastSelectableObject(cam.ScreenPointToRay(mouse.position.ReadValue()));
        }

        private bool CanApplyHoverVisual(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrEmpty(partId))
                return false;

            if (_selectionService != null && _selectionService.CurrentSelection == partGo)
                return false;

            if (IsSubassemblyProxy(partGo))
                return true;

            PartPlacementState state = GetPartState(partId);
            return state == PartPlacementState.Available ||
                   state == PartPlacementState.Completed ||
                   state == PartPlacementState.PlacedVirtually;
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

            if (IsSubassemblyProxy(partGo))
            {
                ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

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

            if (IsSubassemblyProxy(partGo))
            {
                switch (state)
                {
                    case PartPlacementState.Selected:
                    case PartPlacementState.Inspected:
                    case PartPlacementState.Grabbed:
                        ForEachProxyMember(partGo, ApplySelectedPartVisual);
                        break;
                    default:
                        ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                        break;
                }

                return;
            }

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
                    MaterialHelper.SetEmission(partGo, Color.black);
                    ClearRendererPropertyBlocks(partGo);
                    DisablePartColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;

                case PartPlacementState.Available:
                default:
                    DisablePartColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;
            }
        }

        private void SyncPartGrabInteractivity(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrWhiteSpace(partId) || IsSubassemblyProxy(partGo))
                return;

            XRGrabInteractable grabInteractable = partGo.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                return;

            bool shouldEnableGrab = !IsPartMovementLocked(partId);
            if (grabInteractable.enabled == shouldEnableGrab)
                return;

            grabInteractable.enabled = shouldEnableGrab;

            Rigidbody rb = partGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (!shouldEnableGrab && _draggedPart == partGo)
                ResetDragState();
        }

        private void ApplyAvailablePartVisual(GameObject partGo, string partId)
        {
            if (IsSubassemblyProxy(partGo))
            {
                ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

            MaterialHelper.SetEmission(partGo, Color.black);
            ClearRendererPropertyBlocks(partGo);

            // Restore original textured materials if available
            if (MaterialHelper.RestoreOriginals(partGo))
                return;

            // Fallback for parts without original textures (primitives/placeholders)
            PartPreviewPlacement placement = _spawner != null ? _spawner.FindPartPlacement(partId) : null;
            Color baseColor = placement != null
                ? new Color(placement.color.r, placement.color.g, placement.color.b, placement.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);

            MaterialHelper.Apply(partGo, "Preview Part Material", baseColor);
        }

        private static void ClearRendererPropertyBlocks(GameObject target)
        {
            if (target == null)
                return;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }
        }

        private static void DisablePartColorAffordance(GameObject target)
        {
            if (target == null)
                return;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider != null)
            {
                if (Application.isPlaying)
                    Destroy(stateProvider);
                else
                    DestroyImmediate(stateProvider);
            }

            var receivers = target.GetComponentsInChildren<ColorMaterialPropertyAffordanceReceiver>(includeInactive: true);
            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(receivers[i]);
                else
                    DestroyImmediate(receivers[i]);
            }

            var blockHelpers = target.GetComponentsInChildren<MaterialPropertyBlockHelper>(includeInactive: true);
            for (int i = 0; i < blockHelpers.Length; i++)
            {
                if (blockHelpers[i] == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(blockHelpers[i]);
                else
                    DestroyImmediate(blockHelpers[i]);
            }
        }

        private void ApplyHoveredPartVisual(GameObject partGo)
        {
            if (IsSubassemblyProxy(partGo))
            {
                ForEachProxyMember(partGo, member =>
                {
                    ApplyHoveredPartVisual(member);
                    MaterialHelper.SetEmission(member, HoveredSubassemblyEmission);
                });
                return;
            }

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, HoveredPartColor);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", HoveredPartColor);
        }

        private void ApplySelectedPartVisual(GameObject partGo)
        {
            if (IsSubassemblyProxy(partGo))
            {
                ForEachProxyMember(partGo, member =>
                {
                    ApplySelectedPartVisual(member);
                    MaterialHelper.SetEmission(member, SelectedSubassemblyEmission);
                });
                return;
            }

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, SelectedPartColor);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", SelectedPartColor);
        }

        private void ApplyHintSourceVisual(GameObject partGo, Color color)
        {
            if (partGo == null)
                return;

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, color);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", color);
        }

        private void UpdateDockArcVisual()
        {
            if (!TryResolveActiveDockArc(
                out GameObject sourceProxy,
                out Vector3 guideStartWorldPos,
                out Vector3 guideEndWorldPos,
                out Vector3 sourceUp,
                out Vector3 targetUp,
                out bool useLinearGuide))
            {
                ClearDockArcVisual();
                return;
            }

            if (_dockArcVisual == null)
                _dockArcVisual = DockArcVisual.Spawn();

            GameObject selected = NormalizeSelectablePlacementTarget(_selectionService != null ? _selectionService.CurrentSelection : null);
            GameObject hovered = ExternalControlEnabled ? _externalHoveredPartForUi : _hoveredPart;
            hovered = NormalizeSelectablePlacementTarget(hovered);

            float emphasis = useLinearGuide ? 0.7f : 0.35f;
            if (_draggedPart == sourceProxy || selected == sourceProxy)
                emphasis = 1f;
            else if (hovered == sourceProxy)
                emphasis = 0.7f;

            if (useLinearGuide)
            {
                _dockArcVisual.SetLinearGuide(guideStartWorldPos, guideEndWorldPos, sourceUp, targetUp, emphasis);
                return;
            }

            _dockArcVisual.SetArc(guideStartWorldPos, guideEndWorldPos, sourceUp, targetUp, emphasis);
        }

        private void ClearDockArcVisual()
        {
            if (_dockArcVisual == null)
                return;

            _dockArcVisual.Cleanup();
            _dockArcVisual = null;
        }

        private bool TryResolveActiveDockArc(
            out GameObject sourceProxy,
            out Vector3 guideStartWorldPos,
            out Vector3 guideEndWorldPos,
            out Vector3 sourceUp,
            out Vector3 targetUp,
            out bool useLinearGuide)
        {
            sourceProxy = null;
            guideStartWorldPos = Vector3.zero;
            guideEndWorldPos = Vector3.zero;
            sourceUp = Vector3.up;
            targetUp = Vector3.up;
            useLinearGuide = false;

            if (!Application.isPlaying ||
                !ServiceRegistry.TryGet<MachineSessionController>(out var session))
            {
                return false;
            }

            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.HasActiveStep == true ? stepController.CurrentStepDefinition : null;
            if (step == null ||
                !step.RequiresSubassemblyPlacement ||
                string.IsNullOrWhiteSpace(step.requiredSubassemblyId) ||
                step.targetIds == null ||
                step.targetIds.Length != 1 ||
                _subassemblyPlacementController == null ||
                !_subassemblyPlacementController.TryGetProxy(step.requiredSubassemblyId, out sourceProxy))
            {
                return false;
            }

            if (step.IsAxisFitPlacement &&
                _subassemblyPlacementController.TryGetActiveFitGuide(step.requiredSubassemblyId, out Vector3 fitCurrentWorld, out Vector3 fitFinalWorld, out Vector3 fitUp))
            {
                guideStartWorldPos = fitCurrentWorld;
                guideEndWorldPos = fitFinalWorld;
                sourceUp = fitUp;
                targetUp = fitUp;
                useLinearGuide = true;
                return true;
            }

            guideStartWorldPos = ResolveVisualAnchor(sourceProxy);
            sourceUp = sourceProxy.transform.up;

            GameObject targetGhost = FindGhostForTarget(step.targetIds[0]);
            if (targetGhost != null)
            {
                guideEndWorldPos = ResolveVisualAnchor(targetGhost);
                targetUp = targetGhost.transform.up;
                return true;
            }

            if (!_subassemblyPlacementController.TryResolveTargetPose(step.targetIds[0], out Vector3 targetLocalPos, out Quaternion targetRot, out _))
                return false;

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            guideEndWorldPos = previewRoot != null ? previewRoot.TransformPoint(targetLocalPos) : targetLocalPos;
            targetUp = (previewRoot != null ? previewRoot.rotation : Quaternion.identity) * targetRot * Vector3.up;
            return true;
        }

        private GameObject FindGhostForTarget(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return null;

            for (int i = 0; i < _spawnedGhosts.Count; i++)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null)
                    continue;

                GhostPlacementInfo info = ghost.GetComponent<GhostPlacementInfo>();
                if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    return ghost;
            }

            return null;
        }

        private static Vector3 ResolveVisualAnchor(GameObject target)
        {
            if (target == null)
                return Vector3.zero;

            if (TryGetRenderableBounds(target, out Bounds bounds))
                return bounds.center;

            return target.transform.position;
        }

        private static bool TryGetRenderableBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                bounds = renderer.bounds;
                for (int j = i + 1; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        bounds.Encapsulate(renderers[j].bounds);
                }

                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(includeInactive: false);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                bounds = collider.bounds;
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    if (colliders[j] != null)
                        bounds.Encapsulate(colliders[j].bounds);
                }

                return true;
            }

            return false;
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

        // BeginSnapToTarget is now owned by PlaceStepHandler.

        private bool TryResolveSnapPose(string partId, string targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            scale = Vector3.one;
            return _placeHandler != null && _placeHandler.TryResolveSnapPose(partId, targetId, out pos, out rot, out scale);
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

        private bool IsSubassemblyProxy(GameObject target) =>
            _subassemblyPlacementController != null &&
            _subassemblyPlacementController.IsProxy(target);

        private bool IsSelectablePlacementObject(GameObject target) =>
            IsSpawnedPart(target) || IsSubassemblyProxy(target);

        private string ResolveSelectionId(GameObject target)
        {
            if (target == null)
                return null;

            if (_subassemblyPlacementController != null &&
                _subassemblyPlacementController.TryGetSubassemblyId(target, out string subassemblyId) &&
                IsSubassemblyProxy(target))
            {
                return subassemblyId;
            }

            return IsSpawnedPart(target) ? target.name : null;
        }

        private GameObject NormalizeSelectablePlacementTarget(GameObject target)
        {
            if (target == null || _subassemblyPlacementController == null)
                return target;

            GameObject proxyTarget = _subassemblyPlacementController.ResolveSelectableFromHit(target.transform);
            return proxyTarget != null ? proxyTarget : target;
        }

        private void ForEachProxyMember(GameObject proxy, Action<GameObject> visitor)
        {
            if (proxy == null || visitor == null || _subassemblyPlacementController == null)
                return;

            foreach (GameObject member in _subassemblyPlacementController.EnumerateMemberParts(proxy))
            {
                if (member != null)
                    visitor(member);
            }
        }

        private void PushSubassemblyInfoToUI(GameObject target, bool isHoverInfo = false)
        {
            if (_subassemblyPlacementController == null ||
                !_subassemblyPlacementController.TryGetDisplayInfo(target, out string displayName, out string description) ||
                !ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                return;
            }

            const string material = "Completed panel";
            const string searchTerms = "finished subassembly panel cube joining";

            if (isHoverInfo && ui is UIRootCoordinator hoverAwareUi)
            {
                hoverAwareUi.ShowHoverPartInfoShell(
                    displayName,
                    description ?? string.Empty,
                    material,
                    string.Empty,
                    searchTerms);
            }
            else
            {
                ui.ShowPartInfoShell(
                    displayName,
                    description ?? string.Empty,
                    material,
                    string.Empty,
                    searchTerms);
            }
        }

        private bool IsPartMovementLocked(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId))
                return false;

            if (_subassemblyPlacementController != null &&
                string.Equals(_subassemblyPlacementController.ActiveSubassemblyId, partId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

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
                    if (parts[i] != null &&
                        (parts[i].transform == hitTransform || hitTransform.IsChildOf(parts[i].transform)))
                        return parts[i];
                }
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        private GameObject RaycastSpawnedPart(Ray ray)
        {
            // Use RaycastAll because low-profile parts may sit nearly flush with the floor.
            // A single raycast would stop at the environment collider before reaching the part.
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return null;

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                GameObject matchedProxy = _subassemblyPlacementController?.ResolveSelectableFromHit(hits[i].transform);
                if (matchedProxy != null)
                    return matchedProxy;

                GameObject matchedPart = FindPartFromHit(hits[i].transform);
                if (matchedPart != null)
                    return matchedPart;
            }

            return null;
        }

        private GameObject RaycastSelectableObject(Ray ray) => RaycastSpawnedPart(ray);

        internal sealed class ToolActionTargetInfo : MonoBehaviour
        {
            public string TargetId;
            public string RequiredToolId;
            public Vector3 BaseScale;
            public Vector3 BaseLocalPosition;
            /// <summary>World position of the actual action point on the surface (before sphere lift).</summary>
            public Vector3 SurfaceWorldPos;
            /// <summary>Direction of the weld line in world space (normalized). Zero = point target.</summary>
            public Vector3 WeldAxis;
            /// <summary>Length of the weld line in scene units.</summary>
            public float WeldLength;
        }

        internal sealed class GhostPlacementInfo : MonoBehaviour
        {
            public string TargetId;
            public string PartId;
            public string SubassemblyId;

            public bool MatchesPart(string partId)
            {
                return !string.IsNullOrEmpty(partId) &&
                    string.Equals(PartId, partId, StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesSubassembly(string subassemblyId)
            {
                return !string.IsNullOrEmpty(subassemblyId) &&
                    string.Equals(SubassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase);
            }

            public bool MatchesSelectionId(string selectionId)
            {
                return MatchesPart(selectionId) || MatchesSubassembly(selectionId);
            }
        }

        private static bool IsRepositioning()
        {
            return ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller) &&
                   controller != null &&
                   controller.IsRepositioning;
        }

        // ════════════════════════════════════════════════════════════════════
        // Persistent Tools — clamps/fixtures that stay in the scene across steps
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Places a persistent copy of the current tool ghost at the given target position.
        /// The clone stays in the scene across step transitions until explicitly removed.
        /// Used for clamps, fixtures, and other tools that remain on the workpiece.
        /// </summary>
        public GameObject SpawnPersistentTool(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
        {
            GameObject ghost = CursorManager.ToolGhost;
            if (ghost == null)
            {
                OseLog.Warn($"[PersistentTool] Cannot spawn — no tool ghost for '{toolId}'.");
                return null;
            }

            GameObject clone = Instantiate(ghost);
            clone.name = $"PersistentTool_{toolId}_{targetId}";
            clone.transform.SetPositionAndRotation(worldPos, rotation);

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            if (previewRoot != null)
                clone.transform.SetParent(previewRoot, worldPositionStays: true);

            // Remove any colliders so it doesn't interfere with interaction
            foreach (var col in clone.GetComponentsInChildren<Collider>())
                Destroy(col);

            // The cursor ghost is transparent (55%) — restore full opacity for the placed tool
            MaterialHelper.RestoreOpaque(clone);

            // Apply confirmation glow so the user sees the tool "lock in"
            MaterialHelper.SetEmission(clone, new Color(0.1f, 0.7f, 0.25f, 1f) * 1.5f);

            var info = clone.AddComponent<PersistentToolInstance>();
            info.ToolId = toolId;
            info.TargetId = targetId;
            info.ConfirmationGlowRemaining = 1.5f;

            _persistentTools.Add(info);
            OseLog.Info($"[PersistentTool] Spawned '{clone.name}' at {worldPos}. Total persistent: {_persistentTools.Count}");
            return clone;
        }

        /// <summary>
        /// Converts the current cursor ghost into a persistent tool at the given pose.
        /// The ghost is detached from the cursor manager (not cloned), made opaque,
        /// and registered as persistent. A new cursor ghost is then spawned for subsequent targets.
        /// Returns the converted GameObject, or null if there was no ghost.
        /// </summary>
        public GameObject ConvertGhostToPersistent(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
        {
            GameObject ghost = CursorManager.DetachGhost();
            if (ghost == null)
            {
                OseLog.Warn($"[PersistentTool] ConvertGhost — no ghost to detach for '{toolId}'.");
                return null;
            }

            ghost.name = $"PersistentTool_{toolId}_{targetId}";
            ghost.transform.SetParent(null, worldPositionStays: true);
            ghost.transform.SetPositionAndRotation(worldPos, rotation);

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            if (previewRoot != null)
                ghost.transform.SetParent(previewRoot, worldPositionStays: true);

            foreach (var col in ghost.GetComponentsInChildren<Collider>())
                Destroy(col);

            MaterialHelper.RestoreOpaque(ghost);

            // Apply a brief confirmation glow so the user sees the tool "lock in"
            MaterialHelper.SetEmission(ghost, new Color(0.1f, 0.7f, 0.25f, 1f) * 1.5f);
            ghost.SetActive(true);

            var info = ghost.AddComponent<PersistentToolInstance>();
            info.ToolId = toolId;
            info.TargetId = targetId;
            info.ConfirmationGlowRemaining = 1.5f;

            _persistentTools.Add(info);
            OseLog.Info($"[PersistentTool] Converted ghost → persistent '{ghost.name}' at {worldPos}. Total: {_persistentTools.Count}");

            // Spawn a fresh cursor ghost for subsequent targets
            RefreshToolGhostIndicator();

            return ghost;
        }

        /// <summary>
        /// Removes a persistent tool placed at the given target.
        /// Returns true if a matching tool was found and destroyed.
        /// </summary>
        public bool RemovePersistentTool(string targetId)
        {
            for (int i = _persistentTools.Count - 1; i >= 0; i--)
            {
                var inst = _persistentTools[i];
                if (inst == null) { _persistentTools.RemoveAt(i); continue; }
                if (string.Equals(inst.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    OseLog.Info($"[PersistentTool] Removing '{inst.gameObject.name}' from target '{targetId}'.");
                    _persistentTools.RemoveAt(i);
                    Destroy(inst.gameObject);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes all persistent tools with the given tool id.
        /// </summary>
        public int RemoveAllPersistentTools(string toolId = null)
        {
            int removed = 0;
            for (int i = _persistentTools.Count - 1; i >= 0; i--)
            {
                var inst = _persistentTools[i];
                if (inst == null) { _persistentTools.RemoveAt(i); continue; }
                if (toolId == null || string.Equals(inst.ToolId, toolId, StringComparison.OrdinalIgnoreCase))
                {
                    Destroy(inst.gameObject);
                    _persistentTools.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0)
                OseLog.Info($"[PersistentTool] Removed {removed} persistent tool(s) (filter='{toolId ?? "all"}').");
            return removed;
        }

        /// <summary>Returns how many persistent tools of the given type are currently placed.</summary>
        public int GetPersistentToolCount(string toolId = null)
        {
            if (toolId == null) return _persistentTools.Count;
            int count = 0;
            for (int i = 0; i < _persistentTools.Count; i++)
                if (_persistentTools[i] != null && string.Equals(_persistentTools[i].ToolId, toolId, StringComparison.OrdinalIgnoreCase))
                    count++;
            return count;
        }

        /// <summary>Checks if a persistent tool is placed at the given target.</summary>
        public bool HasPersistentToolAt(string targetId)
        {
            for (int i = 0; i < _persistentTools.Count; i++)
                if (_persistentTools[i] != null && string.Equals(_persistentTools[i].TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Marker component for persistent tool instances in the scene.</summary>
        internal sealed class PersistentToolInstance : MonoBehaviour
        {
            public string ToolId;
            public string TargetId;

            /// <summary>
            /// When > 0, the confirmation glow fades out over this duration.
            /// Set by ConvertGhostToPersistent to give a clear "locked in" signal.
            /// </summary>
            public float ConfirmationGlowRemaining;

            private static readonly Color ConfirmGlow = new Color(0.1f, 0.7f, 0.25f, 1f) * 1.5f;

            private void Update()
            {
                if (ConfirmationGlowRemaining <= 0f) return;

                ConfirmationGlowRemaining -= Time.deltaTime;
                if (ConfirmationGlowRemaining <= 0f)
                {
                    ConfirmationGlowRemaining = 0f;
                    MaterialHelper.SetEmission(gameObject, Color.black);
                    enabled = false;
                }
                else
                {
                    float t = ConfirmationGlowRemaining / 1.5f;
                    MaterialHelper.SetEmission(gameObject, ConfirmGlow * t);
                }
            }
        }

    }
}
