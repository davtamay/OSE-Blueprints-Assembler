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
        // Toggled by InteractionOrchestrator at runtime via IPartActionBridge.
        [HideInInspector] public bool ExternalControlEnabled;

        /// <summary>
        /// World position of the last successfully executed tool action target.
        /// Updated by TryExternalToolAction; read by orchestrator via IPartActionBridge to focus camera.
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

        // ── Persistent tools (clamps, fixtures) that remain in scene across steps ──
        private PersistentToolManagerBridge _persistentToolMgr;

        // ── Visual feedback (hover, selection, hint highlight, revelation) ──
        private PartVisualFeedbackManager _visualFeedback;
        private DragController _drag;
        private HintManager _hintManager;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private int _hoverPollFrame;
        private const int HoverPollInterval = 3; // ~20 Hz at 60 fps
        private ToolActionExecutor _toolAction;
        private PartLookupService _lookup;

        // ── Extracted coordinators ────────────────────────────────────────
        private SelectionCoordinator _selection;
        private StepStateResponder _stepResponder;
        private DockArcCoordinator _dockArc;

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
            _lookup ??= new PartLookupService(
                () => _spawner,
                () => _setup,
                () => _subassemblyPlacementController,
                _partStates);
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
            _selection ??= new SelectionCoordinator(ctx);
            _stepResponder ??= new StepStateResponder(ctx, _selection);
            _dockArc ??= new DockArcCoordinator(ctx);
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

            _stepResponder.SetStartupSyncPending(true);
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
            _dockArc?.Clear();
            _partStates.Clear();
            _toolAction?.ClearToolPreviewIndicator();
            ClearToolActionTargets();
            _router?.CleanupAll();
            _subassemblyPlacementController?.Dispose();
            _subassemblyPlacementController = null;
            ServiceRegistry.Unregister<ISubassemblyPlacementService>();
            _stepResponder?.SetStartupSyncPending(false);
        }

        private void Update()
        {
            // Startup sync must run even during intro so parts are revealed
            _stepResponder?.TrySyncStartupState();

            // Block all interaction while the intro overlay is displayed
            if (SessionDriver.IsIntroActive)
            {
                _dockArc?.Clear();
                return;
            }

            // Snap/flash/preview-pulse/required-part-pulse handled by PlaceStepHandler.Update via router
            UpdateXRPreviewProximity();

            // Hover detection + dock arc are expensive (raycasts, renderer iteration)
            // but don't need full frame rate — throttle to ~20 Hz.
            bool hoverFrame = (++_hoverPollFrame % HoverPollInterval) == 0;
            if (hoverFrame)
            {
                _visualFeedback?.UpdatePartHoverVisual();
                _visualFeedback?.UpdateSelectedSubassemblyVisual();
                _dockArc?.Update();
            }

            _visualFeedback?.UpdatePointerDragSelectionVisual();
            _visualFeedback?.UpdateHintHighlight();
            // Stop preview pulse when dragging starts — proximity highlight takes over
            if (_drag.DraggedPart != null)
                _placeHandler?.StopPreviewSelectionPulse();
            UpdateToolPreviewIndicatorPosition();
            if (TryBuildHandlerContext(out var updateCtx))
                _router.Update(in updateCtx, Time.deltaTime);
        }

        // ── Canonical actions ──

        // ── RuntimeEventBus wrappers (delegate to existing handlers) ──

        private void HandleCanonicalActionDispatched(CanonicalActionDispatched evt) => HandleCanonicalAction(evt.Action);
        private void HandlePartSelected(PartSelected evt) => _selection.HandleSelectionServiceSelected(evt.Target);
        private void HandlePartDeselected(PartDeselected evt) => _selection.HandleSelectionServiceDeselected(evt.Target);
        private void HandlePartInspected(PartInspected evt) => _selection.HandleSelectionServiceInspected(evt.Target);
        private void HandleSpawnerPartsReady(SpawnerPartsReady _) => _stepResponder.HandlePartsReady();

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
            if (IsSpawnedPart(selected) && ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
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

            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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
        /// Called by orchestrator via IPartActionBridge. Attempts placement for the
        /// already-selected part at the given screen position without re-deriving click intent.
        /// </summary>
        public bool TryExternalClickToPlace(GameObject selectedPart, Vector2 screenPos)
        {
            if (!Application.isPlaying || !ExternalControlEnabled)
                return false;

            return TryHandleClickToPlace(selectedPart, screenPos);
        }

        /// <summary>
        /// Called by orchestrator via IPartActionBridge when tool mode is locked.
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
        /// Called by orchestrator via IPartActionBridge for any tap (regardless of
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
        /// Called by orchestrator via IPartActionBridge while external control is enabled.
        /// Delegates to <see cref="SelectionCoordinator.SetExternalHoveredPart"/>.
        /// </summary>
        public void SetExternalHoveredPart(GameObject hoveredPart)
            => _selection.SetExternalHoveredPart(hoveredPart);

        // ── IPartActionBridge explicit implementations ──
        // Maps interface names to the existing "External"-prefixed methods.

        bool IPartActionBridge.ExternalControlEnabled
        {
            get => ExternalControlEnabled;
            set => ExternalControlEnabled = value;
        }

        GameObject IPartActionBridge.NormalizeSelectableTarget(GameObject target)
            => NormalizeExternalSelectableTarget(target);

        bool IPartActionBridge.IsSelectableTarget(GameObject target)
            => IsSelectablePlacementObject(NormalizeSelectablePlacementTarget(target));

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
                ServiceRegistry.TryGet<IPartRuntimeController>(out var memberController);
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

        // ── Sub-interface explicit implementations ──

        // ISpawnerContext
        PackagePartSpawner ISpawnerContext.Spawner => _spawner;
        PreviewSceneSetup ISpawnerContext.Setup => _setup;
        GameObject ISpawnerContext.FindSpawnedPart(string partId) => FindSpawnedPart(partId);
        PartPlacementState ISpawnerContext.GetPartState(string partId) => GetPartState(partId);
        Dictionary<string, PartPlacementState> ISpawnerContext.PartStates => _partStates;
        void ISpawnerContext.DestroyObject(UnityEngine.Object obj) => Destroy(obj);

        // IPartQueryContext
        bool IPartQueryContext.IsSubassemblyProxy(GameObject target) => IsSubassemblyProxy(target);
        bool IPartQueryContext.ForEachProxyMember(GameObject proxy, Action<GameObject> action) { ForEachProxyMember(proxy, action); return true; }
        GameObject IPartQueryContext.NormalizeSelectablePlacementTarget(GameObject target) => NormalizeSelectablePlacementTarget(target);
        bool IPartQueryContext.IsSelectablePlacementObject(GameObject target) => IsSelectablePlacementObject(target);
        string IPartQueryContext.ResolveSelectionId(GameObject target) => ResolveSelectionId(target);
        bool IPartQueryContext.IsPartMovementLocked(string partId) => IsPartMovementLocked(partId);
        bool IPartQueryContext.IsToolModeLockedForParts() => IsToolModeLockedForParts();
        SubassemblyPlacementController IPartQueryContext.SubassemblyController => _subassemblyPlacementController;

        // IInteractionStateContext
        SelectionService IInteractionStateContext.SelectionService => _selectionService;
        DragController IInteractionStateContext.Drag => _drag;
        bool IInteractionStateContext.IsDragging => _drag != null && _drag.IsDragging;
        bool IInteractionStateContext.IsExternalControlEnabled => ExternalControlEnabled;
        GameObject IInteractionStateContext.GetHoveredPartFromXri() => GetHoveredPartFromXri();
        GameObject IInteractionStateContext.GetHoveredPartFromMouse() => GetHoveredPartFromMouse();
        void IInteractionStateContext.ResetDragState() => ResetDragState();

        // IPreviewContext
        List<GameObject> IPreviewContext.SpawnedPreviews => _spawnedPreviews;
        PreviewSpawnManager IPreviewContext.PreviewManager => _previewManager;
        void IPreviewContext.RefreshToolActionTargets() => RefreshToolActionTargets();
        void IPreviewContext.HandlePlacementSucceeded(GameObject target) => _selection.HandlePlacementSucceeded(target);

        // ISiblingAccessContext
        PlaceStepHandler ISiblingAccessContext.PlaceHandler => _placeHandler;
        UseStepHandler ISiblingAccessContext.UseHandler => _useHandler;
        ConnectStepHandler ISiblingAccessContext.ConnectHandler => _connectHandler;
        PartVisualFeedbackManager ISiblingAccessContext.VisualFeedback => _visualFeedback;
        StepExecutionRouter ISiblingAccessContext.Router => _router;
        ToolCursorManager ISiblingAccessContext.CursorManager => CursorManager;
        ToolActionExecutor ISiblingAccessContext.ToolAction => _toolAction;
        StepFocusComputer ISiblingAccessContext.FocusComputer => _focusComputer;
        void ISiblingAccessContext.ClearHintHighlight() => ClearHintHighlight();
        void ISiblingAccessContext.RestorePartVisual(GameObject part) => RestorePartVisual(part);

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
            IMachineSessionController session,
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

                candidate = _lookup.RaycastPartAtScreen(screenPos);
            }

            if (candidate == null)
                return;

            if (isInspect)
                _selectionService.NotifyInspected(candidate);
            else
                _selectionService.NotifySelected(candidate);

            _drag.PendingSelectPart = null;
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
        private bool TryHandleClickToPlace(GameObject selected, Vector2 screenPos)
        {
            int previewCount = _previewManager?.SpawnedPreviews.Count ?? 0;
            if (previewCount == 0 || _placeHandler == null)
                return false;

            if (selected == null || _drag.DraggedPart != null)
                return false;

            selected = NormalizeSelectablePlacementTarget(selected);

            // Skip click-to-place within 50ms of selection to prevent the
            // pointer-down that triggered selection from also triggering placement.
            const float SelectionCooldownSeconds = 0.05f;
            float selectionTime = _selection.SelectionTime;
            if (selectionTime >= 0f && Time.realtimeSinceStartup - selectionTime < SelectionCooldownSeconds)
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

        private void ClearHintHighlight() => _visualFeedback?.ClearHintHighlight();


        /// <summary>
        /// Returns the world position of the nearest preview target matching the given part ID.
        /// Used by the orchestrator to pivot the camera toward the placement target.
        /// </summary>
        public bool TryGetPreviewWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            return _placeHandler != null && _placeHandler.TryGetPreviewWorldPosForPart(partId, out worldPos);
        }

        // Snap animation, flash invalid, and their update loops are now
        // owned by PlaceStepHandler (run via router.Update).

        // ── Runtime event handlers ──

        private void HandleSessionRestored(SessionRestored evt)
            => _stepResponder.HandleSessionRestored(evt);

        private void HandleStepNavigated(StepNavigated evt)
            => _stepResponder.HandleStepNavigated(evt);

        private void HandleStepStateChanged(StepStateChanged evt)
            => _stepResponder.HandleStepStateChanged(evt);

        private bool TryBuildHandlerContext(out StepHandlerContext context)
            => _stepResponder.TryBuildHandlerContext(out context);

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
            _visualFeedback?.SyncPartGrabInteractivity(partGo, evt.PartId);
            _visualFeedback?.ApplyPartVisualForState(partGo, evt.PartId, evt.Current);

            if (_visualFeedback?.HoveredPart == partGo && CanApplyHoverVisual(partGo, evt.PartId))
                ApplyHoveredPartVisual(partGo);

            if (evt.Current == PartPlacementState.Selected || evt.Current == PartPlacementState.Inspected)
            {
                _selection.PushPartInfoToUI(evt.PartId);
            }

            // Remove placed parts from the required-part pulse list
            if (evt.Current == PartPlacementState.PlacedVirtually || evt.Current == PartPlacementState.Completed)
            {
                _placeHandler?.RemoveFromRequiredPartIds(evt.PartId);
            }
        }

        private void HandleHintRequested(HintRequested evt)
            => _hintManager?.HandleHintRequested(evt);


        // ── Preview parts ──

        private bool AdvanceSequentialTarget()
            => _previewManager?.AdvanceSequentialTarget() ?? true;

        private void RefreshToolPreviewIndicator()
            => _toolAction?.RefreshToolPreviewIndicator();

        private void UpdateToolPreviewIndicatorPosition()
        {
            if (!TryGetPointerPosition(out Vector2 screenPos))
                screenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _toolAction?.UpdateToolPreviewIndicatorPosition(screenPos);
        }

        private bool IsToolModeLockedForParts()
            => _toolAction?.IsToolModeLockedForParts() ?? false;

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

        /// <summary>
        /// Moves parts from all given steps to their play positions and applies
        /// completed visuals. Used by session restore to position parts in bulk
        /// without replaying step events.
        /// </summary>
        public void RestoreCompletedStepParts(StepDefinition[] steps) => _visualFeedback?.RestoreCompletedStepParts(steps);


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

            if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

        // ── Helpers ──

        private void BeginDragTracking(GameObject partGo)
            => _drag?.BeginDragTracking(partGo, ResolveSelectionId(partGo));


        private void ResetDragState()
        {
            _drag?.Reset();
            _placeHandler?.ClearPreviewHighlight();
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

            var cam = CameraUtil.GetMain();
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






        // ── Lookup delegations (extracted to PartLookupService) ──

        private bool IsSpawnedPart(GameObject target) => _lookup.IsSpawnedPart(target);
        private bool IsSubassemblyProxy(GameObject target) => _lookup.IsSubassemblyProxy(target);
        private bool IsSelectablePlacementObject(GameObject target) => _lookup.IsSelectablePlacementObject(target);
        private string ResolveSelectionId(GameObject target) => _lookup.ResolveSelectionId(target);
        private GameObject NormalizeSelectablePlacementTarget(GameObject target) => _lookup.NormalizeSelectablePlacementTarget(target);
        private void ForEachProxyMember(GameObject proxy, Action<GameObject> visitor) => _lookup.ForEachProxyMember(proxy, visitor);

        private bool IsPartMovementLocked(string partId) => _lookup.IsPartMovementLocked(partId);
        private bool IsPartStateLockedLocally(string partId) => _lookup.IsPartStateLockedLocally(partId);
        private GameObject FindSpawnedPart(string partId) => _lookup.FindSpawnedPart(partId);
        private GameObject RaycastSelectableObject(Ray ray) => _lookup.RaycastSelectableObject(ray);

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
