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
    /// preview part spawning, visual feedback, and step-completion repositioning.
    ///
    /// Requires <see cref="PackagePartSpawner"/> on the same GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PackagePartSpawner))]
    public sealed class PartInteractionBridge : MonoBehaviour, IPartActionBridge, IToolPreviewProvider, IPersistentToolManager, IBridgeContext
    {
        // Visual constants — see InteractionVisualConstants for values
        private static Color SelectedPartColor => InteractionVisualConstants.SelectedPartColor;
        private static Color GrabbedPartColor => InteractionVisualConstants.GrabbedPartColor;
        private static Color HoveredPartColor => InteractionVisualConstants.HoveredPartColor;
        private static Color DimmedPartColor => InteractionVisualConstants.DimmedPartColor;
        private static Color ActiveStepEmission => InteractionVisualConstants.ActiveStepEmission;
        private static Color PreviewReadyColor => InteractionVisualConstants.PreviewReadyColor;
        private static Color HintHighlightColorA => InteractionVisualConstants.HintHighlightColorA;
        private static Color HintHighlightColorB => InteractionVisualConstants.HintHighlightColorB;
        private static Color HoveredSubassemblyEmission => InteractionVisualConstants.HoveredSubassemblyEmission;
        private static Color SelectedSubassemblyEmission => InteractionVisualConstants.SelectedSubassemblyEmission;
        private const float DragThresholdPixels = InteractionVisualConstants.DragThresholdPixels;
        private const float ScrollDepthSpeed = InteractionVisualConstants.ScrollDepthSpeed;
        private const float PinchDepthSpeed = InteractionVisualConstants.PinchDepthSpeed;
        private const float DepthAdjustSpeed = InteractionVisualConstants.DepthAdjustSpeed;
        private const float MinDragRayDistance = InteractionVisualConstants.MinDragRayDistance;
        private const float DragViewportMargin = InteractionVisualConstants.DragViewportMargin;
        private const float DragFloorEpsilon = InteractionVisualConstants.DragFloorEpsilon;
        private const float HintHighlightDuration = InteractionVisualConstants.HintHighlightDuration;
        private const float HintHighlightPulseSpeed = InteractionVisualConstants.HintHighlightPulseSpeed;
        // Toggled automatically by V2 InteractionOrchestrator at runtime via IPartActionBridge.
        // When true, this bridge skips pointer input polling (V2 handles input instead).
        [HideInInspector] public bool ExternalControlEnabled;

        /// <summary>
        /// World position of the last successfully executed tool action target.
        /// Updated by TryExternalToolAction; read by V2 orchestrator via IPartActionBridge to focus camera.
        /// </summary>
        public Vector3 LastToolActionWorldPos => _toolAction?.LastToolActionWorldPos ?? Vector3.zero;

        private PackagePartSpawner _spawner;
        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _spawnedPreviews = new List<GameObject>();
        private PreviewSpawnManager _previewManager;
        private StepFocusComputer _focusComputer;
        private ToolCursorManager _cursorManager;
        private ToolCursorManager CursorManager => _cursorManager ??= new ToolCursorManager(transform);
        private UseStepHandler _useHandler;
        private ConnectStepHandler _connectHandler;
        private PlaceStepHandler _placeHandler;
        private SubassemblyPlacementController _subassemblyPlacementController;
        private StepExecutionRouter _router;
        [SerializeField] private InputActionRouter _actionRouter;
        [SerializeField] private SelectionService _selectionService;
        private bool _suppressSelectionEvents;

        // ── Persistent tools (clamps, fixtures) that remain in scene across steps ──
        private PersistentToolManagerBridge _persistentToolMgr;

        // ── Visual feedback (hover, selection, hint highlight, revelation) ──
        private PartVisualFeedbackManager _visualFeedback;
        private DragController _drag;
        private HintManager _hintManager;
        private int _selectionFrame; // frame when last selection happened
        private GameObject _lastSelectedVisualTarget; // tracks selection visual for cleanup

        private GameObject _externalHoveredPartForUi;
        private DockArcVisual _dockArcVisual;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private bool _startupSyncPending;
        private ToolActionExecutor _toolAction;

        // Step-based part visibility state is owned by _visualFeedback.
        private const float PartGridSpacing = InteractionVisualConstants.PartGridSpacing;
        private const float PartGridStartZ = InteractionVisualConstants.PartGridStartZ;
        private const float PartLayoutY = InteractionVisualConstants.PartLayoutY;

        // Sequential target ordering — tracks which targetId index is active
        // when the step uses targetOrder == "sequential".


        // ── Lifecycle ──

        private void OnEnable()
        {
            _spawner = GetComponent<PackagePartSpawner>();
            _setup = GetComponent<PreviewSceneSetup>();
            _persistentToolMgr ??= new PersistentToolManagerBridge(
                () => CursorManager.ToolPreview,
                () => CursorManager.DetachPreview(),
                () => _setup != null ? _setup.PreviewRoot : null,
                RefreshToolPreviewIndicator);
            // All extracted classes share a single context reference back to
            // this bridge, replacing the previous 40+ Func<>/Action lambdas.
            IBridgeContext ctx = this;
            _useHandler ??= new UseStepHandler(ctx);
            _router ??= new StepExecutionRouter();
            _router.Register(StepFamily.Use, _useHandler);
            _router.Register(StepFamily.Confirm, new ConfirmStepHandler());
            _connectHandler ??= new ConnectStepHandler(ctx);
            _router.Register(StepFamily.Connect, _connectHandler);
            _subassemblyPlacementController ??= new SubassemblyPlacementController(ctx);
            ServiceRegistry.Register<ISubassemblyPlacementService>(_subassemblyPlacementController);
            _visualFeedback ??= new PartVisualFeedbackManager(ctx);
            _drag ??= new DragController(() => _setup);
            _hintManager ??= new HintManager(ctx);
            _previewManager ??= new PreviewSpawnManager(ctx);
            _focusComputer ??= new StepFocusComputer(ctx);
            _placeHandler ??= new PlaceStepHandler(ctx);
            _router.Register(StepFamily.Place, _placeHandler);
            _toolAction ??= new ToolActionExecutor(ctx);
            // All event subscriptions go through RuntimeEventBus — one pattern,
            // no null-checked references, symmetric subscribe/unsubscribe.
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<HintRequested>(HandleHintRequested);
            RuntimeEventBus.Subscribe<ActiveToolChanged>(HandleActiveToolChanged);
            RuntimeEventBus.Subscribe<SessionRestored>(HandleSessionRestored);
            RuntimeEventBus.Subscribe<StepNavigated>(HandleStepNavigated);
            RuntimeEventBus.Subscribe<CanonicalActionDispatched>(HandleCanonicalActionDispatched);
            RuntimeEventBus.Subscribe<PartSelected>(HandlePartSelected);
            RuntimeEventBus.Subscribe<PartDeselected>(HandlePartDeselected);
            RuntimeEventBus.Subscribe<PartInspected>(HandlePartInspected);
            RuntimeEventBus.Subscribe<SpawnerPartsReady>(HandleSpawnerPartsReady);

            EnsureInputWiring();

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
            RuntimeEventBus.Unsubscribe<CanonicalActionDispatched>(HandleCanonicalActionDispatched);
            RuntimeEventBus.Unsubscribe<PartSelected>(HandlePartSelected);
            RuntimeEventBus.Unsubscribe<PartDeselected>(HandlePartDeselected);
            RuntimeEventBus.Unsubscribe<PartInspected>(HandlePartInspected);
            RuntimeEventBus.Unsubscribe<SpawnerPartsReady>(HandleSpawnerPartsReady);

            _visualFeedback?.Clear();
            ClearDockArcVisual();
            _partStates.Clear();
            ClearToolPreviewIndicator();
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
            // Snap/flash/preview-pulse/required-part-pulse handled by PlaceStepHandler.Update via router
            UpdateXRPreviewProximity();
            if (!ExternalControlEnabled)
            {
                _visualFeedback?.UpdatePartHoverVisual();
                _visualFeedback?.UpdatePointerDragSelectionVisual();
            }
            _visualFeedback?.UpdateSelectedSubassemblyVisual();
            UpdateDockArcVisual();
            _visualFeedback?.UpdateHintHighlight();
            // Stop preview pulse when dragging starts — proximity highlight takes over
            if (_drag.DraggedPart != null)
                _placeHandler?.StopPreviewSelectionPulse();
            UpdateToolPreviewIndicatorPosition();
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

        // Ã¢”€Ã¢”€ Canonical actions Ã¢”€Ã¢”€

        // ── RuntimeEventBus wrappers (delegate to existing handlers) ──

        private void HandleCanonicalActionDispatched(CanonicalActionDispatched evt) => HandleCanonicalAction(evt.Action);
        private void HandlePartSelected(PartSelected evt) => HandleSelectionServiceSelected(evt.Target);
        private void HandlePartDeselected(PartDeselected evt) => HandleSelectionServiceDeselected(evt.Target);
        private void HandlePartInspected(PartInspected evt) => HandleSelectionServiceInspected(evt.Target);
        private void HandleSpawnerPartsReady(SpawnerPartsReady _) => HandlePartsReady();

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

            if (!_drag.PointerDown)
                BeginXRGrabTracking(selected);
        }

        private void HandlePlaceAction()
        {
            if (IsToolModeLockedForParts())
                return;

            if (_drag.IsDragging && _drag.PointerDown)
                return; // pointer-up path handles placement

            GameObject selected = _drag.DraggedPart;
            if (selected == null && _selectionService != null)
                selected = _selectionService.CurrentSelection;

            if (selected == null)
                return;
            selected = NormalizeSelectablePlacementTarget(selected);

            AttemptPlacementForSelection(selected);

            if (!_drag.PointerDown)
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
                OseLog.Info($"[PartInteraction] V1 tool action path: step='{step.id}', allowCompletion={allowToolActionStepCompletion}, spawnedTargets={_toolAction?.SpawnedTargetCount ?? 0}.");
                if (TryExecuteToolPrimaryActionFromPointer(session, stepController, allowToolActionStepCompletion))
                    return;

                OseLog.Info($"[PartInteraction] V1 tool action path: TryExecuteToolPrimaryActionFromPointer returned false.");
                if (step.IsToolAction)
                {
                    if ((_toolAction?.SpawnedTargetCount ?? 0) > 0)
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
        public bool TryResolveExternalToolActionTarget(Vector2 screenPos, out ToolActionContext context)
        {
            if (_toolAction != null) return _toolAction.TryResolveToolActionTarget(screenPos, out context);
            context = default;
            return false;
        }

        public bool TryExecuteExternalToolAction(string interactedTargetId)
            => _toolAction?.TryExecuteToolAction(interactedTargetId) ?? false;

        public bool TryExternalToolAction(Vector2 screenPos)
            => _toolAction?.TryExecuteToolActionAtScreen(screenPos) ?? false;

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
                    if (_visualFeedback != null)
                        _visualFeedback.HoveredPart = hoveredPart;
                    ApplyHoveredPartVisual(hoveredPart);
                }
            }

            _externalHoveredPartForUi = hoveredPart;

            if (_externalHoveredPartForUi != null)
            {
                if (IsSubassemblyProxy(_externalHoveredPartForUi))
                {
                    PushSubassemblyInfoToUI(_externalHoveredPartForUi, isHoverInfo: true);
                }
                else if (_subassemblyPlacementController != null &&
                         _subassemblyPlacementController.TryGetSubassemblyId(_externalHoveredPartForUi, out _) &&
                         _subassemblyPlacementController.TryGetDisplayInfo(_externalHoveredPartForUi, out _, out _))
                {
                    // Part belongs to a completed subassembly — show subassembly info
                    PushSubassemblyInfoToUI(_externalHoveredPartForUi, isHoverInfo: true);
                }
                else
                {
                    PushPartInfoToUI(_externalHoveredPartForUi.name, isHoverInfo: true);
                }
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

        bool IPartActionBridge.IsPartMovementLocked(GameObject target)
        {
            if (target == null) return false;
            target = NormalizeSelectablePlacementTarget(target);
            string selectionId = ResolveSelectionId(target);

            if (string.IsNullOrWhiteSpace(selectionId))
            {
                // Target wasn't recognized as a spawned part or proxy — check by
                // name directly against the local state dictionary as a fallback.
                selectionId = target.name;
            }

            bool locked = IsPartMovementLocked(selectionId);

            // Double-check the bridge's own state dictionary for completed parts
            // that might not be tracked in PartRuntimeController (e.g., after session restore).
            if (!locked)
            {
                PartPlacementState localState = GetPartState(selectionId);
                if (localState == PartPlacementState.PlacedVirtually || localState == PartPlacementState.Completed)
                {
                    OseLog.VerboseInfo($"[PartInteraction] Bridge local state override: '{selectionId}' is {localState} — locking.");
                    locked = true;
                }
            }

            // Also check subassembly membership — if this part belongs to any
            // subassembly whose other members are completed, lock it.
            // Uses PartRuntimeController.IsPartLockedForMovement which handles the
            // Selected-from-Completed case (previousState tracking).
            if (!locked && _subassemblyPlacementController != null)
            {
                ServiceRegistry.TryGet<PartRuntimeController>(out var memberController);
                if (_subassemblyPlacementController.TryGetSubassemblyId(target, out string subId) &&
                    !string.IsNullOrWhiteSpace(subId))
                {
                    foreach (GameObject member in _subassemblyPlacementController.EnumerateMemberParts(target))
                    {
                        if (member == null) continue;
                        bool memberLocked = memberController != null
                            ? memberController.IsPartLockedForMovement(member.name)
                            : IsPartStateLockedLocally(member.name);
                        if (memberLocked)
                        {
                            OseLog.VerboseInfo($"[PartInteraction] Subassembly member '{member.name}' locked — locking '{selectionId}'.");
                            locked = true;
                            break;
                        }
                    }
                }
            }

            OseLog.Info($"[PartInteraction] IsPartMovementLocked(GO '{target.name}', id='{selectionId}'): {locked}, bridgeState={GetPartState(selectionId)}");
            return locked;
        }

        bool IPartActionBridge.TryClickToPlace(GameObject selectedPart, Vector2 screenPos)
            => TryExternalClickToPlace(selectedPart, screenPos);

        bool IPartActionBridge.TryToolAction(Vector2 screenPos)
            => TryExternalToolAction(screenPos);

        bool IPartActionBridge.TryToolAction(string targetId)
            => TryExecuteExternalToolAction(targetId);

        bool IPartActionBridge.TryResolveToolActionTarget(Vector2 screenPos, out ToolActionContext context)
            => TryResolveExternalToolActionTarget(screenPos, out context);

        bool IPartActionBridge.TryPipeConnection(Vector2 screenPos)
            => TryExternalPipeConnection(screenPos);

        void IPartActionBridge.SetHoveredPart(GameObject part)
            => SetExternalHoveredPart(part);

        bool IPartActionBridge.TryGetStepFocusBounds(string stepId, out Bounds bounds)
        {
            bounds = default;
            if (_focusComputer == null || !_focusComputer.TryResolveStepFocusBounds(stepId, out bounds))
                return false;
            bounds.Expand(new Vector3(0.18f, 0.12f, 0.18f));
            return true;
        }

        // ── IBridgeContext explicit implementations ──

        PackagePartSpawner IBridgeContext.Spawner => _spawner;
        PreviewSceneSetup IBridgeContext.Setup => _setup;
        SelectionService IBridgeContext.SelectionService => _selectionService;
        DragController IBridgeContext.Drag => _drag;
        PlaceStepHandler IBridgeContext.PlaceHandler => _placeHandler;
        UseStepHandler IBridgeContext.UseHandler => _useHandler;
        ConnectStepHandler IBridgeContext.ConnectHandler => _connectHandler;
        PartVisualFeedbackManager IBridgeContext.VisualFeedback => _visualFeedback;
        PreviewSpawnManager IBridgeContext.PreviewManager => _previewManager;
        StepExecutionRouter IBridgeContext.Router => _router;
        ToolCursorManager IBridgeContext.CursorManager => CursorManager;
        SubassemblyPlacementController IBridgeContext.SubassemblyController => _subassemblyPlacementController;
        List<GameObject> IBridgeContext.SpawnedPreviews => _spawnedPreviews;
        Dictionary<string, PartPlacementState> IBridgeContext.PartStates => _partStates;
        GameObject IBridgeContext.FindSpawnedPart(string partId) => FindSpawnedPart(partId);
        bool IBridgeContext.IsSubassemblyProxy(GameObject target) => IsSubassemblyProxy(target);
        bool IBridgeContext.ForEachProxyMember(GameObject proxy, Action<GameObject> action) { ForEachProxyMember(proxy, action); return true; }
        GameObject IBridgeContext.NormalizeSelectablePlacementTarget(GameObject target) => NormalizeSelectablePlacementTarget(target);
        bool IBridgeContext.IsPartMovementLocked(string partId) => IsPartMovementLocked(partId);
        bool IBridgeContext.IsToolModeLockedForParts() => IsToolModeLockedForParts();
        PartPlacementState IBridgeContext.GetPartState(string partId) => GetPartState(partId);
        bool IBridgeContext.IsDragging => _drag != null && _drag.IsDragging;
        bool IBridgeContext.IsExternalControlEnabled => ExternalControlEnabled;
        GameObject IBridgeContext.GetHoveredPartFromXri() => GetHoveredPartFromXri();
        GameObject IBridgeContext.GetHoveredPartFromMouse() => GetHoveredPartFromMouse();
        void IBridgeContext.ResetDragState() => ResetDragState();
        void IBridgeContext.ClearHintHighlight() => ClearHintHighlight();
        void IBridgeContext.RestorePartVisual(GameObject part) => RestorePartVisual(part);
        void IBridgeContext.RefreshToolActionTargets() => RefreshToolActionTargets();
        void IBridgeContext.DestroyObject(UnityEngine.Object obj) => Destroy(obj);
        void IBridgeContext.HandlePlacementSucceeded(GameObject target) => HandlePlacementSucceeded(target);

        // ── Tool Action Preview bridge methods ──

        public GameObject GetToolPreview() => _toolAction?.GetToolPreview();
        public int GetCompletedToolTargetCount() => _toolAction?.GetCompletedToolTargetCount() ?? 0;
        public void IncrementCompletedToolTargetCount() => _toolAction?.IncrementCompletedToolTargetCount();
        public void SetToolPreviewPositionSuspended(bool suspended) => _toolAction?.SetToolPreviewPositionSuspended(suspended);
        public string GetActiveToolProfile() => _toolAction?.GetActiveToolProfile();

        /// <summary>Returns the currently equipped tool ID, or null.</summary>
        public string GetActiveToolId()
            => _toolAction?.GetActiveToolId();

        private bool TryFocusCameraOnToolTarget(Vector2 screenPos)
            => _toolAction?.TryFocusCameraOnToolTarget(screenPos) ?? false;

        private void FlashToolTargetOnFailure()
            => _toolAction?.FlashToolTargetOnFailure();

        private bool TryExecuteToolPrimaryActionFromPointer(
            MachineSessionController session,
            StepController stepController,
            bool allowStepCompletion = true)
            => _toolAction?.TryExecuteToolPrimaryActionFromPointer(session, stepController, allowStepCompletion) ?? false;

        private void RefreshToolActionTargets()
            => _toolAction?.RefreshToolActionTargets();

        private void TrySelectFromPointer(bool isInspect)
        {
            if (ExternalControlEnabled)
            {
                _drag.PendingSelectPart = null;
                return;
            }

            if (IsToolModeLockedForParts())
                return;

            if (_selectionService == null)
                return;

            GameObject candidate = _drag.PendingSelectPart;
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

            _drag.PendingSelectPart = null;
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

            // Restore previous selection visual before applying the new one.
            if (_lastSelectedVisualTarget != null && _lastSelectedVisualTarget != target)
            {
                RestorePartVisual(_lastSelectedVisualTarget);
                _lastSelectedVisualTarget = null;
            }

            bool accepted;
            string selectionId = ResolveSelectionId(target);
            bool isProxy = IsSubassemblyProxy(target);
            bool isMemberOfSubassembly = !isProxy && _subassemblyPlacementController != null &&
                _subassemblyPlacementController.TryGetSubassemblyId(target, out _);

            if (isProxy)
            {
                accepted = !string.IsNullOrWhiteSpace(selectionId);
                if (accepted)
                {
                    PushSubassemblyInfoToUI(target, isHoverInfo: false);
                    ClearPartHoverVisual();
                    ApplySelectedPartVisual(target);
                    _lastSelectedVisualTarget = target;
                }
            }
            else
            {
                if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                    return;

                accepted = isInspect
                    ? partController.InspectPart(target.name)
                    : partController.SelectPart(target.name);

                // If this part belongs to a completed subassembly, show subassembly info
                // in the panel but only highlight the clicked part (not all siblings).
                if (accepted && isMemberOfSubassembly &&
                    _subassemblyPlacementController.TryGetDisplayInfo(target, out _, out _))
                {
                    PushSubassemblyInfoToUI(target, isHoverInfo: false);
                    ClearPartHoverVisual();
                }
            }

            if (!accepted)
            {
                DeselectFromSelectionService();
                return;
            }

            OseLog.Info($"[PartInteraction] Selected item '{selectionId ?? target.name}'");
            _selectionFrame = Time.frameCount;
            _lastSelectedVisualTarget = target;
            StartPreviewSelectionPulse(selectionId ?? target.name);
            if (!IsSubassemblyProxy(target))
                TryAutoCompleteSelectionStep(target.name);

            if (_drag.PointerDown && _drag.PendingSelectPart == target)
            {
                if (IsPartMovementLocked(selectionId))
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag tracking blocked for locked item '{selectionId}'.");
                    _drag.PendingSelectPart = null;
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

            StopPreviewSelectionPulse();
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
                if (_visualFeedback?.HoveredPart == current)
                    _visualFeedback?.ClearPartHoverVisual();
            }

            if (_lastSelectedVisualTarget != null)
            {
                RestorePartVisual(_lastSelectedVisualTarget);
                _lastSelectedVisualTarget = null;
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
            StopPreviewSelectionPulse();
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
                    ServiceRegistry.TryGet<InputActionRouter>(out _actionRouter);
                if (_selectionService == null)
                    ServiceRegistry.TryGet<SelectionService>(out _selectionService);
                return;
            }

            if (_actionRouter == null)
                ServiceRegistry.TryGet<InputActionRouter>(out _actionRouter);

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
                ServiceRegistry.TryGet<SelectionService>(out _selectionService);

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
            => DragController.TryGetPointerPosition(out screenPos);


        private GameObject RaycastPartAtScreen(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return null;

            return RaycastSelectableObject(cam.ScreenPointToRay(screenPos));
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
                else if (_drag.PointerDown)
                    HandlePointerDrag(screenPos);

                return;
            }

            // Failsafe: recover from missed release events (window focus loss, input edge-cases).
            // Without this, _drag.PointerDown can stay latched and block future drag/select flow.
            if (_drag.PointerDown)
            {
                if (!TryGetPointerPosition(out Vector2 fallbackPos))
                    fallbackPos = _drag.PointerDownScreenPos;

                HandlePointerUp(fallbackPos);
            }
        }

        private static bool TryGetPointerState(out Vector2 screenPos, out bool pressed, out bool released)
            => DragController.TryGetPointerState(out screenPos, out pressed, out released);


        private void HandlePointerDown(Vector2 screenPos)
        {
            if (IsRepositioning()) return;

            // Click on a pulsating tool target sphere → focus camera on it
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

                _drag.PendingSelectPart = matchedPart;

                if (_actionRouter != null && _selectionService != null)
                {
                    _actionRouter.InjectAction(CanonicalAction.Select);
                }
                else if (_selectionService != null)
                {
                    _selectionService.NotifySelected(matchedPart);
                    _drag.PendingSelectPart = null;
                }
                else if (!IsSubassemblyProxy(matchedPart) && ServiceRegistry.TryGet<PartRuntimeController>(out var lockedPartController))
                {
                    lockedPartController.SelectPart(matchedPart.name);
                    _drag.PendingSelectPart = null;
                }

                return;
            }

            _drag.SetPointerDown(screenPos, cam, matchedPart);

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
            if (_drag.DraggedPart == null || _drag.DragCamera == null) return;

            if (!_drag.IsDragging)
            {
                if (!_drag.ExceedsDragThreshold(screenPos))
                    return;

                // Start dragging
                _drag.IsDragging = true;
                bool canGrab = true;
                if (!IsSubassemblyProxy(_drag.DraggedPart) && ServiceRegistry.TryGet<PartRuntimeController>(out var pc))
                    canGrab = pc.GrabPart(_drag.DraggedPartId);

                if (!canGrab)
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag blocked for locked part '{_drag.DraggedPartId}'.");
                    ResetDragState();
                    return;
                }

                OseLog.Info($"[PartInteraction] Dragging part '{_drag.DraggedPartId}'");
            }

            _drag.DraggedPart.transform.position = _drag.ComputeDragWorldPosition(screenPos);
            _subassemblyPlacementController?.ApplyProxyTransform(_drag.DraggedPart);

            // Check proximity to previews and highlight when in snap zone
            UpdatePreviewProximity();
        }

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (IsRepositioning()) return;
            if (!_drag.PointerDown)
                return;

            _drag.ClearPointerDown();

            if (!_drag.IsDragging || _drag.DraggedPart == null)
            {
                // Was just a click, not a drag ? selection handled by canonical action
                _drag.PendingSelectPart = null;
                ResetDragState();
                return;
            }

            _drag.IsDragging = false;

            // Attempt placement
            AttemptDragPlacement();

            ResetDragState();
        }

        private void AttemptDragPlacement()
        {
            if (_drag.DraggedPart == null || string.IsNullOrEmpty(_drag.DraggedPartId))
                return;

            AttemptPlacementForSelection(_drag.DraggedPart);
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

        // ── Click-to-place ──

        /// <summary>
        /// Attempts click-to-place: if a part is selected and the pointer hits (or is near)
        /// a matching preview target, snap the part there without requiring drag.
        /// </summary>
        private bool TryHandleClickToPlace(Vector2 screenPos)
        {
            GameObject selected = _selectionService != null ? _selectionService.CurrentSelection : null;
            return TryHandleClickToPlace(selected, screenPos);
        }

        private bool TryHandleClickToPlace(GameObject selected, Vector2 screenPos)
        {
            int previewCount = _previewManager?.SpawnedPreviews.Count ?? 0;
            if (previewCount == 0 || _placeHandler == null)
                return false;

            if (selected == null || _drag.DraggedPart != null)
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

        // Preview raycast, screen proximity, and click-to-place execution
        // are now owned by PlaceStepHandler.

        // ── Preview proximity detection ──

        private void UpdatePreviewProximity()
        {
            if (_drag.DraggedPart == null || (_previewManager?.SpawnedPreviews.Count ?? 0) == 0)
                return;

            _placeHandler?.UpdateDragProximity(_drag.DraggedPart, _drag.DraggedPartId, _drag.IsDragging);
        }
        private void ClearPreviewHighlight() => _placeHandler?.ClearPreviewHighlight();

        private void StartPreviewSelectionPulse(string partId) => _placeHandler?.StartPreviewSelectionPulse(partId);

        private void StopPreviewSelectionPulse() => _placeHandler?.StopPreviewSelectionPulse();

        // ── Required-part pulse (highlights parts the user needs to grab) ──

        private void ClearRequiredPartEmission() => _placeHandler?.ClearRequiredPartEmission();

        /// <summary>
        /// Shows and positions all parts belonging to the current step's subassembly.
        /// When the first step of a subassembly activates, all parts for that entire
        /// subassembly appear at once — matching how a real workbench is organized.
        /// Parts are arranged in an arc on the near side of the floor,
        /// keeping the center clear for the machine being assembled.
        /// </summary>
        private void HideNonIntroducedParts() => _visualFeedback?.HideNonIntroducedParts();

        private void RevealStepParts(string stepId) => _visualFeedback?.RevealStepParts(stepId);


        /// <summary>
        /// Highlights the active step's required parts with emission glow and dims
        /// previously-revealed parts that belong to the subassembly but aren't needed
        /// for the current step.
        /// </summary>
        private void ApplyStepPartHighlighting(string stepId) => _visualFeedback?.ApplyStepPartHighlighting(stepId);


        // RefreshRequiredPartIds and UpdateRequiredPartPulse are now owned by PlaceStepHandler
        // (called via router lifecycle: OnStepActivated/Update).

        private void UpdateHintHighlight() => _visualFeedback?.UpdateHintHighlight();


        private void ClearHintHighlight() => _visualFeedback?.ClearHintHighlight();


        /// <summary>
        /// Returns the world position of the nearest preview target matching the given part ID.
        /// Used by the V2 orchestrator to pivot the camera toward the placement target.
        /// </summary>
        public bool TryGetPreviewWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            return _placeHandler != null && _placeHandler.TryGetPreviewWorldPosForPart(partId, out worldPos);
        }

        private PlacementPreviewInfo FindNearestPreviewForPart(string partId, Vector3 worldPos, out float nearestDist)
        {
            nearestDist = float.PositiveInfinity;
            if (_placeHandler == null) return null;
            return _placeHandler.FindNearestPreviewForSelection(partId, worldPos, out nearestDist);
        }


        // Snap animation, flash invalid, and their update loops are now
        // owned by PlaceStepHandler (run via router.Update).

        // ── Runtime event handlers ──

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
        /// are hidden. The subsequent StepStateChanged(Active) spawns new previews.
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
            _previewManager?.ClearPreviews();
            ClearToolActionTargets();
            ClearRequiredPartEmission();
            _connectHandler?.ClearTransientVisuals();
            _visualFeedback?.RevealedPartIds.Clear();
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

            // FailedAttempt is a transient state within the same step (Active → FailedAttempt → Active).
            // Preserve previews and sequential progress for both the transition INTO FailedAttempt
            // and the auto-return back to Active.
            bool isFailRelated = evt.Current == StepState.FailedAttempt
                              || (evt.Current == StepState.Active && evt.Previous == StepState.FailedAttempt);

            if (!isFailRelated)
            {
                ClearHintHighlight();
                ClearToolActionTargets();

                // Reset sequential tracking only on genuine new-step transitions.
                _previewManager?.ResetSequentialState();

                // Connect-step markers are transient step visuals and must not leak
                // into unrelated steps or navigation states.
                _connectHandler?.ClearTransientVisuals();
            }

            if (evt.Current == StepState.Active)
            {
                if (isFailRelated)
                {
                    // Preview and sequential state are still valid — skip re-spawn.
                    OseLog.VerboseInfo($"[PartInteraction] Step '{evt.StepId}' re-activated after failed attempt — keeping {_previewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
                }
                else
                {
                    // Clear any stale SelectionService selection so the dedup guard in
                    // NotifySelected doesn’t block re-selection of the same part on the
                    // new step (e.g. beam selected on step 2 → must be selectable again on step 3).
                    DeselectFromSelectionService();

                    // Hybrid presentation: hide all parts on first step, then reveal per-subassembly
                    HideNonIntroducedParts();
                    RevealStepParts(evt.StepId);
                    ApplyStepPartHighlighting(evt.StepId);
                    _subassemblyPlacementController?.RefreshForStep(evt.StepId);

                    _previewManager?.SpawnPreviewsForStep(evt.StepId);
                    if (TryBuildHandlerContext(out var activatedCtx))
                        _router.OnStepActivated(in activatedCtx);
                    _focusComputer?.FocusCameraOnStepArea(evt.StepId);
                    OseLog.VerboseInfo($"[PartInteraction] Step ‘{evt.StepId}’ active: spawned {_previewManager?.SpawnedPreviews.Count ?? 0} preview(s).");
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
                _previewManager?.ClearPreviews();
                // Handler clears required-part emission via OnStepCompleted
            }

            // Handler refreshes required-part IDs via OnStepActivated

            RefreshToolPreviewIndicator();
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
            _previewManager?.ClearPreviews();
            ClearToolActionTargets();
            ClearRequiredPartEmission();
            _connectHandler?.ClearTransientVisuals();
            _visualFeedback?.RevealedPartIds.Clear();
            _visualFeedback?.ActiveStepPartIds.Clear();
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

            _previewManager?.SpawnPreviewsForStep(activeStepId);
            if (TryBuildHandlerContext(out var rebuildCtx))
                _router.OnStepActivated(in rebuildCtx);

            _focusComputer?.FocusCameraOnStepArea(activeStepId, resetToDefaultView);
            RefreshToolPreviewIndicator();
            RefreshToolActionTargets();
        }

        private void FocusCameraOnStepArea(string stepId, bool resetToDefaultView = false)
            => _focusComputer?.FocusCameraOnStepArea(stepId, resetToDefaultView);





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

            RefreshToolPreviewIndicator();
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

            if (_visualFeedback?.HoveredPart == partGo && CanApplyHoverVisual(partGo, evt.PartId))
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
            => _hintManager?.HandleHintRequested(evt);


        // ── Preview parts ──

        private void SpawnPreviewsForStep(string stepId)
            => _previewManager?.SpawnPreviewsForStep(stepId);


        private bool AdvanceSequentialTarget()
            => _previewManager?.AdvanceSequentialTarget() ?? true;


        private string GetCurrentSequentialTargetId()
            => _previewManager?.GetCurrentSequentialTargetId();













        private void ClearPreviews()
            => _previewManager?.ClearPreviews();


        private void RefreshToolPreviewIndicator()
            => _toolAction?.RefreshToolPreviewIndicator();

        private void UpdateToolPreviewIndicatorPosition()
        {
            if (!TryGetPointerPosition(out Vector2 screenPos))
                screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _toolAction?.UpdateToolPreviewIndicatorPosition(screenPos);
        }

        private void ClearToolPreviewIndicator()
            => _toolAction?.ClearToolPreviewIndicator();

        private bool IsToolModeLockedForParts()
            => _toolAction?.IsToolModeLockedForParts() ?? false;

        private bool TryHandleToolActionPointerDown(Vector2 screenPos)
            => _toolAction?.TryHandleToolActionPointerDown(screenPos) ?? false;

        private void ClearToolActionTargets()
            => _toolAction?.ClearToolActionTargets();

        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            if (_toolAction != null) return _toolAction.TryGetNearestToolTargetWorldPos(screenPos, out worldPos);
            worldPos = Vector3.zero;
            return false;
        }

        public Vector3[] GetActiveToolTargetPositions()
            => _toolAction?.GetActiveToolTargetPositions() ?? Array.Empty<Vector3>();

        // TryGetPreviewTargetPose is now owned by PlaceStepHandler.

        private void RemovePreviewForPart(string partId)
        {
            // Clear hint highlight if the hint preview is being removed
            if (_visualFeedback?.HintPreview != null && !string.IsNullOrEmpty(partId))
            {
                PlacementPreviewInfo hInfo = _visualFeedback.HintPreview.GetComponent<PlacementPreviewInfo>();
                if (hInfo != null && hInfo.MatchesPart(partId))
                    ClearHintHighlight();
            }
            _placeHandler?.RemovePreviewForPart(partId);
        }

        // ── Step completion: move parts to assembled position ──

        private void MoveStepPartsToPlayPosition(string stepId) => _visualFeedback?.MoveStepPartsToPlayPosition(stepId);


        /// <summary>
        /// Moves parts from all given steps to their play positions and applies
        /// completed visuals. Used by session restore to position parts in bulk
        /// without replaying step events.
        /// </summary>
        public void RestoreCompletedStepParts(StepDefinition[] steps) => _visualFeedback?.RestoreCompletedStepParts(steps);


        private void MovePartToPlayPosition(string partId) => _visualFeedback?.MovePartToPlayPosition(partId);


        /// <summary>
        /// Reverts parts from steps at or after <paramref name="fromStepIndex"/> back
        /// to their start positions with available visuals, undoing any play-position
        /// placement. Called during backward navigation so future parts visually rewind.
        /// </summary>
        private void RevertFutureStepParts(StepDefinition[] allSteps, int fromStepIndex) => _visualFeedback?.RevertFutureStepParts(allSteps, fromStepIndex);


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

            RemovePreviewForPart(selectedId);

            if (_previewManager != null && _previewManager.IsSequentialStep)
            {
                if (AdvanceSequentialTarget())
                    session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
            else if (partController.AreActiveStepRequiredPartsPlaced())
            {
                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
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
            => _drag?.BeginDragTracking(partGo, ResolveSelectionId(partGo));


        private void ResetDragState()
        {
            _drag?.Reset();
            ClearPreviewHighlight();
        }


        private void BeginXRGrabTracking(GameObject partGo)
            => _drag?.BeginXRGrabTracking(partGo, ResolveSelectionId(partGo));



        private void UpdateXRPreviewProximity()
        {
            if (_drag.PointerDown)
                return;

            if (_drag.IsDragging && _drag.DraggedPart != null)
                UpdatePreviewProximity();
        }


        private void UpdatePartHoverVisual() => _visualFeedback?.UpdatePartHoverVisual();


        private void UpdateSelectedSubassemblyVisual() => _visualFeedback?.UpdateSelectedSubassemblyVisual();


        private void UpdatePointerDragSelectionVisual() => _visualFeedback?.UpdatePointerDragSelectionVisual();


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

        private bool CanApplyHoverVisual(GameObject partGo, string partId) => _visualFeedback != null && _visualFeedback.CanApplyHoverVisual(partGo, partId);


        private void ClearPartHoverVisual() => _visualFeedback?.ClearPartHoverVisual();


        private void RestorePartVisual(GameObject partGo) => _visualFeedback?.RestorePartVisual(partGo);


        private PartPlacementState GetPartState(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return PartPlacementState.Available;

            return _partStates.TryGetValue(partId, out PartPlacementState state)
                ? state
                : PartPlacementState.Available;
        }

        private void ApplyPartVisualForState(GameObject partGo, string partId, PartPlacementState state) => _visualFeedback?.ApplyPartVisualForState(partGo, partId, state);


        private void SyncPartGrabInteractivity(GameObject partGo, string partId) => _visualFeedback?.SyncPartGrabInteractivity(partGo, partId);


        private void ApplyAvailablePartVisual(GameObject partGo, string partId) => _visualFeedback?.ApplyAvailablePartVisual(partGo, partId);


        private static void ClearRendererPropertyBlocks(GameObject target) => PartVisualFeedbackManager.ClearRendererPropertyBlocks(target);


        private static void DisablePartColorAffordance(GameObject target) => PartVisualFeedbackManager.DisablePartColorAffordance(target);


        private void ApplyHoveredPartVisual(GameObject partGo) => _visualFeedback?.ApplyHoveredPartVisual(partGo);


        private void ApplySelectedPartVisual(GameObject partGo) => _visualFeedback?.ApplySelectedPartVisual(partGo);


        /// <summary>
        /// Highlights all members of a completed subassembly when one member is selected.
        /// </summary>
        private void ApplySelectedSubassemblyMemberVisual(GameObject clickedMember, string subassemblyId)
        {
            ApplySelectedPartVisual(clickedMember);
            MaterialHelper.SetEmission(clickedMember, SelectedSubassemblyEmission);

            // Also highlight sibling members so the whole subassembly lights up.
            // EnumerateMemberParts works with or without a proxy record.
            if (_subassemblyPlacementController != null)
            {
                foreach (GameObject member in _subassemblyPlacementController.EnumerateMemberParts(clickedMember))
                {
                    if (member == null || member == clickedMember) continue;
                    ApplySelectedPartVisual(member);
                    MaterialHelper.SetEmission(member, SelectedSubassemblyEmission);
                }
            }
        }

        private void ApplyHintSourceVisual(GameObject partGo, Color color) => _visualFeedback?.ApplyHintSourceVisual(partGo, color);


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
            GameObject hovered = ExternalControlEnabled ? _externalHoveredPartForUi : _visualFeedback?.HoveredPart;
            hovered = NormalizeSelectablePlacementTarget(hovered);

            float emphasis = useLinearGuide ? 0.7f : 0.35f;
            if (_drag.DraggedPart == sourceProxy || selected == sourceProxy)
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

            GameObject targetPreview = FindPreviewForTarget(step.targetIds[0]);
            if (targetPreview != null)
            {
                guideEndWorldPos = ResolveVisualAnchor(targetPreview);
                targetUp = targetPreview.transform.up;
                return true;
            }

            if (!_subassemblyPlacementController.TryResolveTargetPose(step.targetIds[0], out Vector3 targetLocalPos, out Quaternion targetRot, out _))
                return false;

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            guideEndWorldPos = previewRoot != null ? previewRoot.TransformPoint(targetLocalPos) : targetLocalPos;
            targetUp = (previewRoot != null ? previewRoot.rotation : Quaternion.identity) * targetRot * Vector3.up;
            return true;
        }

        private GameObject FindPreviewForTarget(string targetId)
            => _previewManager?.FindPreviewForTarget(targetId);

        private static Vector3 ResolveVisualAnchor(GameObject target)
        {
            if (target == null)
                return Vector3.zero;

            if (TryGetRenderableBounds(target, out Bounds bounds))
                return bounds.center;

            return target.transform.position;
        }

        private static bool TryGetRenderableBounds(GameObject target, out Bounds bounds)
            => PreviewSpawnManager.TryGetRenderableBounds(target, out bounds);

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

            ServiceRegistry.TryGet<PartRuntimeController>(out var partController);

            // For subassembly IDs (including the active subassembly): check if ANY
            // member part is locked. Previously the active subassembly was unconditionally
            // exempted, which allowed dragging completed proxies.
            if (_subassemblyPlacementController != null)
            {
                var package = _spawner?.CurrentPackage;
                GameObject partGo = FindSpawnedPart(partId);
                bool isSubassemblyId = partGo == null && _subassemblyPlacementController.TryGetProxy(partId, out _);
                if (!isSubassemblyId)
                    isSubassemblyId = package != null && package.TryGetSubassembly(partId, out _);

                if (isSubassemblyId)
                {
                    // If this subassembly is required for placement by the active step, allow movement.
                    if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
                    {
                        var stepCtrl = session.AssemblyController?.StepController;
                        if (stepCtrl != null && stepCtrl.HasActiveStep)
                        {
                            var currentStep = stepCtrl.CurrentStepDefinition;
                            if (currentStep != null && currentStep.RequiresSubassemblyPlacement &&
                                string.Equals(currentStep.requiredSubassemblyId, partId, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                        }
                    }

                    bool anyMemberFound = false;
                    bool anyMemberLocked = false;
                    if (package != null && package.TryGetSubassembly(partId, out var subDef) && subDef?.partIds != null)
                    {
                        foreach (string memberId in subDef.partIds)
                        {
                            if (string.IsNullOrWhiteSpace(memberId)) continue;
                            anyMemberFound = true;
                            bool memberLocked = partController != null
                                ? partController.IsPartLockedForMovement(memberId)
                                : IsPartStateLockedLocally(memberId);
                            if (memberLocked) { anyMemberLocked = true; break; }
                        }
                    }
                    if (anyMemberLocked) return true;
                    // Active subassembly with no locked members → allow drag for placement.
                    if (anyMemberFound) return false;
                }
            }

            if (partController != null)
                return partController.IsPartLockedForMovement(partId);

            return IsPartStateLockedLocally(partId);
        }

        private bool IsPartStateLockedLocally(string partId)
        {
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

        // ToolActionTargetInfo and PlacementPreviewInfo extracted to standalone files.

        private static bool IsRepositioning()
        {
            return ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller) &&
                   controller != null &&
                   controller.IsRepositioning;
        }

        // ════════════════════════════════════════════════════════════════════
        // Persistent Tools — delegated to PersistentToolManagerBridge
        // ════════════════════════════════════════════════════════════════════

        public GameObject SpawnPersistentTool(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
            => _persistentToolMgr.SpawnPersistentTool(toolId, targetId, worldPos, rotation);

        public GameObject ConvertPreviewToPersistent(string toolId, string targetId, Vector3 worldPos, Quaternion rotation)
            => _persistentToolMgr.ConvertPreviewToPersistent(toolId, targetId, worldPos, rotation);

        public bool RemovePersistentTool(string targetId)
            => _persistentToolMgr.RemovePersistentTool(targetId);

        public int RemoveAllPersistentTools(string toolId = null)
            => _persistentToolMgr.RemoveAllPersistentTools(toolId);

        public bool HasPersistentToolAt(string targetId)
            => _persistentToolMgr.HasPersistentToolAt(targetId);

        public int GetPersistentToolCount(string toolId = null)
            => _persistentToolMgr.GetPersistentToolCount(toolId);

        public string[] GetPlacedPersistentToolIds()
            => _persistentToolMgr.GetPlacedPersistentToolIds();

    }
}
