using System;
using System.Collections.Generic;
using System.Reflection;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Input;
using OSE.Interaction;
using OSE.Interaction.V2.Integration;
using OSE.Runtime;
using OSE.UI.Root;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Top-level interaction coordinator. Owns the interaction state machine,
    /// polls the active IIntentProvider, and routes intents to the appropriate
    /// subsystem (camera, placement, feedback).
    ///
    /// Self-bootstrapping: discovers scene systems in Start() and wires everything.
    /// No separate bootstrap component needed — just add this to the scene.
    ///
    /// When UseV2Interaction is false, this component is completely passive —
    /// existing PartInteractionBridge continues to handle everything.
    ///
    /// One instance per scene, placed on a root-level GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionOrchestrator : MonoBehaviour
    {
        [SerializeField] private InteractionSettings _settings;
        [SerializeField] private Camera _camera;
        [SerializeField] private AssemblyCameraRig _cameraRigOverride;
        [SerializeField] private Vector3 _defaultPivot = Vector3.zero;

        [Header("Platform")]
        [Tooltip("Auto = detect at runtime. Override to force a specific input mode in the editor.\n\n" +
                 "Desktop: mouse + keyboard, V2 camera rig\n" +
                 "Mobile: touch gestures, V2 camera rig\n" +
                 "XR: headset/controllers, TrackedPoseDriver stays active, V2 dormant")]
        [SerializeField] private InteractionMode _modeOverride = InteractionMode.Auto;

        // ── Public State ──

        public InteractionMode ResolvedMode { get; private set; }
        public InteractionState CurrentState { get; private set; } = InteractionState.Idle;
        public GameObject HoveredPart { get; private set; }
        public GameObject SelectedPart { get; private set; }
        public GameObject DraggedPart { get; private set; }
        public InteractionSettings Settings => _settings;

        // ── Subsystem references ──

        private IIntentProvider _intentProvider;
        private AssemblyCameraRig _cameraRig;
        private CanonicalActionBridge _actionBridge;
        private IPartActionBridge _partBridge;
        private IToolGhostProvider _toolGhost;
        private PersistentToolController _persistentToolController;
        private PlacementAssistService _placementAssist;
        private InteractionFeedbackPresenter _feedbackPresenter;
        private StepGuidanceService _guidanceService;
        private ToolFocusController _toolFocusController;
        private ToolActionPreviewController _previewController;

        // ── Internal state ──

        private bool _cameraIntentActiveLastFrame;
        private int _intentLogCountdown = 20; // Log first N non-None intents for diagnostics
        private bool _bootstrapped;

        // Part validation (resolved via reflection to avoid circular asmdef dependency)
        private object _spawnerRef;
        private PropertyInfo _spawnedPartsProperty;

        // Guidance service package context (resolved lazily via reflection on spawner)
        private bool _guidanceContextProvided;
        private PropertyInfo _currentPackageProperty;
        private MethodInfo _findTargetPlacementMethod;
        private PropertyInfo _previewRootProperty; // on PreviewSceneSetup

        // ── Lifecycle ──

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
        }

        private void Start()
        {
            if (_settings == null)
            {
                OseLog.Info("[InteractionOrchestrator] No InteractionSettings assigned. V2 disabled.");
                return;
            }

            if (!_settings.UseV2Interaction)
            {
                OseLog.Info("[InteractionOrchestrator] UseV2Interaction is false. V2 disabled.");
                return;
            }

            if (_camera == null)
            {
                OseLog.Warn("[InteractionOrchestrator] No main camera found. V2 disabled.");
                return;
            }

            // ── Resolve platform mode (single source of truth) ──
            var mode = InteractionModeResolver.Resolve(_modeOverride);

            ResolvedMode = mode;

            if (mode == InteractionMode.XR)
            {
                // XR mode: TrackedPoseDriver + XROrigin handle everything.
                // V2 stays dormant — PartInteractionBridge + XR input path are in charge.
                OseLog.Info("[InteractionOrchestrator] XR mode — V2 dormant (TrackedPoseDriver + XR input active).");
                return;
            }

            Bootstrap(mode);
        }

        private void Bootstrap(InteractionMode mode)
        {
            OseLog.Info($"[InteractionOrchestrator] Bootstrapping V2 interaction (mode={mode})...");

            // ── 1. Disable XR camera drivers that would fight V2 ──
            DisableXRCameraDrivers(_camera);

            // ── 2. Intent Provider (based on resolved mode, not compile-time platform) ──
            _intentProvider = mode == InteractionMode.Mobile
                ? (IIntentProvider)new MobileIntentProvider(_camera, _settings)
                : new DesktopIntentProvider(_camera, _settings);
            OseLog.Info($"[InteractionOrchestrator] Intent provider: {_intentProvider.GetType().Name}");

            // ── 3. Camera Rig ──
            _cameraRig = _cameraRigOverride;
            if (_cameraRig == null)
                _cameraRig = _camera.GetComponent<AssemblyCameraRig>();
            if (_cameraRig == null)
                _cameraRig = _camera.gameObject.AddComponent<AssemblyCameraRig>();

            _cameraRig.InitializeFromCurrentTransform(_defaultPivot, _settings);

            // ── 4. Find existing scene systems ──
            var router = FindFirstObjectByType<InputActionRouter>();
            var selectionService = FindFirstObjectByType<SelectionService>();

            if (router == null) OseLog.Warn("[InteractionOrchestrator] InputActionRouter not found.");
            if (selectionService == null) OseLog.Warn("[InteractionOrchestrator] SelectionService not found.");

            // ── 5. Canonical Action Bridge ──
            _actionBridge = new CanonicalActionBridge(router, selectionService);

            // ── 6. Part Interaction Bridge (typed interfaces) ──
            var bridge = FindFirstObjectByType<PartInteractionBridge>();
            if (bridge != null)
            {
                _partBridge = bridge;
                _toolGhost = bridge;
                _persistentToolController = new PersistentToolController(bridge, bridge);
                _partBridge.ExternalControlEnabled = true;
                OseLog.Info("[InteractionOrchestrator] Connected to PartInteractionBridge via typed interfaces.");
            }
            else
            {
                OseLog.Warn("[InteractionOrchestrator] PartInteractionBridge not found.");
            }

            var allMonos = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            // ── 7. Placement Assist ──
            _placementAssist = new PlacementAssistService(_settings);

            // ── 8. Feedback Presenter ──
            _feedbackPresenter = FindFirstObjectByType<InteractionFeedbackPresenter>();
            if (_feedbackPresenter == null)
            {
                _feedbackPresenter = gameObject.AddComponent<InteractionFeedbackPresenter>();
                _feedbackPresenter.Initialize(_settings);
            }

            // ── 9. Find PackagePartSpawner for hit validation ──
            foreach (var mono in allMonos)
            {
                if (mono.GetType().Name == "PackagePartSpawner")
                {
                    _spawnerRef = mono;
                    _spawnedPartsProperty = mono.GetType().GetProperty("SpawnedParts");
                    break;
                }
            }

            if (_spawnerRef == null)
                OseLog.Warn("[InteractionOrchestrator] PackagePartSpawner not found. Part hit validation disabled.");

            // ── 10. Step Guidance Service ──
            _guidanceService = new StepGuidanceService(_settings, _cameraRig);
            if (_partBridge != null)
                _guidanceService.SetPartBridge(_partBridge);

            // ── 11. Tool Focus (gesture engagement — legacy) ──
            _toolFocusController = new ToolFocusController();

            // ── 12. Tool Action Preview ("I Do / We Do / You Do") ──
            _previewController = new ToolActionPreviewController();

            // Cache reflection handles for lazy package context injection
            if (_spawnerRef != null)
            {
                var spawnerType = _spawnerRef.GetType();
                _currentPackageProperty = spawnerType.GetProperty("CurrentPackage");
                _findTargetPlacementMethod = spawnerType.GetMethod("FindTargetPlacement");

                // PreviewSceneSetup is on the same GameObject (or discoverable via field)
                var setupField = spawnerType.GetField("_setup", BindingFlags.NonPublic | BindingFlags.Instance);
                if (setupField != null)
                {
                    object setup = setupField.GetValue(_spawnerRef);
                    if (setup != null)
                        _previewRootProperty = setup.GetType().GetProperty("PreviewRoot");
                }
            }

            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<StepActivated>(HandleStepActivated);
            RuntimeEventBus.Subscribe<SessionRestored>(HandleSessionRestored);
            RuntimeEventBus.Subscribe<RepositionModeChanged>(HandleRepositionModeChanged);

            _bootstrapped = true;
            OseLog.Info($"[InteractionOrchestrator] V2 READY. Mode={mode} Camera={_camera.name} Bridge={_partBridge != null}");

            // If a step is already active (e.g. hot reload), frame it now
            TryFrameCurrentStep();
        }

        /// <summary>
        /// Disable TrackedPoseDriver and XROrigin so they stop overriding the camera
        /// transform every frame. Camera stays in its hierarchy — we just stop the
        /// components that fight for control.
        /// </summary>
        private static void DisableXRCameraDrivers(Camera cam)
        {
            // Disable TrackedPoseDriver on the camera itself
            foreach (var comp in cam.GetComponents<MonoBehaviour>())
            {
                string typeName = comp.GetType().Name;
                if (typeName is "TrackedPoseDriver" or "TrackedPoseDriverDataProvider")
                {
                    comp.enabled = false;
                    OseLog.Info($"[InteractionOrchestrator] Disabled {typeName} on {cam.name}.");
                }
            }

            // Disable XROrigin/XRRig component in parent hierarchy (it repositions child transforms)
            Transform check = cam.transform.parent;
            while (check != null)
            {
                foreach (var comp in check.GetComponents<MonoBehaviour>())
                {
                    string typeName = comp.GetType().Name;
                    if (typeName is "XROrigin" or "XRRig")
                    {
                        comp.enabled = false;
                        OseLog.Info($"[InteractionOrchestrator] Disabled {typeName} on {check.name}.");
                    }
                }
                check = check.parent;
            }
        }

        private void OnDestroy()
        {
            RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<StepActivated>(HandleStepActivated);
            RuntimeEventBus.Unsubscribe<SessionRestored>(HandleSessionRestored);
            RuntimeEventBus.Unsubscribe<RepositionModeChanged>(HandleRepositionModeChanged);
            if (_partBridge != null)
                _partBridge.ExternalControlEnabled = false;
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (!_bootstrapped) return;
            if (evt.Current != StepState.Active) return;

            // Clear stale selection/drag state so the new step starts from Idle.
            // PartInteractionBridge and PartRuntimeController handle their own cleanup;
            // this just keeps the Orchestrator's own fields in sync.
            SelectedPart = null;
            DraggedPart = null;
            TransitionTo(InteractionState.Idle);
        }

        private void HandleSessionRestored(SessionRestored evt)
        {
            if (!_bootstrapped) return;
            // After restore, the next StepActivated will trigger framing.
            // But if it already fired, frame the current step now.
            TryFrameCurrentStep();
        }

        private void TryFrameCurrentStep()
        {
            if (_guidanceService == null || _partBridge == null) return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            var stepCtrl = session?.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep) return;

            string stepId = stepCtrl.CurrentStepState.StepId;
            if (string.IsNullOrWhiteSpace(stepId)) return;

            OseLog.Info($"[V2] TryFrameCurrentStep '{stepId}'");
            _guidanceService.FrameStep(stepId);
        }

        private void HandleStepActivated(StepActivated evt)
        {
            OseLog.Info($"[V2] HandleStepActivated '{evt.StepId}' bootstrapped={_bootstrapped}");
            if (!_bootstrapped) return;

            // Remove persistent tools (clamps) if the new step no longer needs them
            CleanUpPersistentToolsForStep(evt.StepId);

            if (_guidanceService != null)
            {
                // Lazily provide package context on first activation
                if (!_guidanceContextProvided)
                    TryProvideGuidanceContext();

                _guidanceService.OnStepActivated(evt);
            }
        }

        private void TryProvideGuidanceContext()
        {
            if (_spawnerRef == null || _currentPackageProperty == null || _findTargetPlacementMethod == null)
                return;

            var package = _currentPackageProperty.GetValue(_spawnerRef) as MachinePackageDefinition;
            if (package == null) return;

            // Build Func<string, TargetPreviewPlacement> via reflection
            object spawner = _spawnerRef;
            MethodInfo findMethod = _findTargetPlacementMethod;
            Func<string, TargetPreviewPlacement> findTarget = targetId =>
                findMethod.Invoke(spawner, new object[] { targetId }) as TargetPreviewPlacement;

            // Resolve PreviewRoot
            Transform previewRoot = null;
            if (_previewRootProperty != null)
            {
                var setupField = _spawnerRef.GetType().GetField("_setup", BindingFlags.NonPublic | BindingFlags.Instance);
                if (setupField != null)
                {
                    object setup = setupField.GetValue(_spawnerRef);
                    if (setup != null)
                        previewRoot = _previewRootProperty.GetValue(setup) as Transform;
                }
            }

            _guidanceService.SetPackageContext(package, findTarget, previewRoot);
            _guidanceContextProvided = true;
            OseLog.Info("[InteractionOrchestrator] Guidance service package context provided.");
        }

        private void HandleRepositionModeChanged(RepositionModeChanged evt)
        {
            if (!_bootstrapped) return;

            if (evt.IsActive)
            {
                // Release any in-progress drag and deselect
                if (CurrentState == InteractionState.DraggingPart && DraggedPart != null)
                {
                    _actionBridge?.OnPartReleased();
                    DraggedPart = null;
                }

                if (SelectedPart != null)
                {
                    SelectedPart = null;
                    _actionBridge?.OnDeselected();
                }

                HoveredPart = null;
                TransitionTo(InteractionState.RepositioningAssembly);
            }
            else
            {
                TransitionTo(InteractionState.Idle);
            }
        }

        private void HandlePartStateChanged(PartStateChanged evt)
        {
            // When a dragged part gets placed (e.g. auto-snap from proximity),
            // immediately end the V2 drag so we stop repositioning it.
            if (CurrentState != InteractionState.DraggingPart || DraggedPart == null)
                return;

            if (!string.Equals(DraggedPart.name, evt.PartId, System.StringComparison.OrdinalIgnoreCase))
                return;

            if (evt.Current is PartPlacementState.ValidPlacement
                or PartPlacementState.PlacedVirtually
                or PartPlacementState.Completed)
            {
                OseLog.VerboseInfo($"[Orchestrator] Dragged part '{evt.PartId}' placed — ending drag.");
                DraggedPart = null;
                TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
            }
        }

        private void Update()
        {
            if (!_bootstrapped)
                return;

            // Block all interaction while the intro overlay is displayed
            if (OSE.Runtime.Preview.SessionDriver.IsIntroActive)
                return;

            if (IsToolModeLockedForParts() && CurrentState == InteractionState.DraggingPart)
            {
                _actionBridge?.OnPartReleased();

                DraggedPart = null;
                TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
            }

            // ── ToolFocus: tick preview or gesture, suppress normal input ──
            if (CurrentState == InteractionState.ToolFocus)
            {
                // Tool Action Preview (new system)
                if (_previewController != null && _previewController.IsActive)
                {
                    var previewIntent = _intentProvider.Poll();
                    if (!previewIntent.IsNone && previewIntent.IntentKind == InteractionIntent.Kind.Cancel)
                        _previewController.Cancel();
                    else
                        _previewController.Tick(Time.deltaTime, previewIntent.ScreenDelta);
                    return;
                }

                // Legacy gesture engagement (fallback)
                if (_toolFocusController != null && _toolFocusController.IsActive)
                {
                    var focusIntent = _intentProvider.Poll();
                    if (!focusIntent.IsNone && focusIntent.IntentKind == InteractionIntent.Kind.Cancel)
                        _toolFocusController.Cancel();
                    else
                        _toolFocusController.Tick(Time.deltaTime);
                    return;
                }
            }

            var intent = _intentProvider.Poll();

            // Camera return-to-idle: if we were in a camera state last frame
            // and no camera intent this frame, go back to Idle/PartSelected.
            bool cameraIntentThisFrame = !intent.IsNone && intent.IsCameraIntent;
            if (_cameraIntentActiveLastFrame && !cameraIntentThisFrame && IsCameraState(CurrentState))
            {
                TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
            }
            _cameraIntentActiveLastFrame = cameraIntentThisFrame;

            if (!intent.IsNone)
            {
                if (_intentLogCountdown > 0)
                {
                    _intentLogCountdown--;
                    OseLog.Info($"[V2 Intent] {intent.IntentKind} delta={intent.ScreenDelta} scroll={intent.ScrollDelta} hit={intent.HitTarget?.name ?? "null"}");
                }
                ProcessIntent(intent);
            }

            // Update hover tracking via pointer raycast
            UpdateHover();

            // Forward hover target to the legacy bridge so part-info UI can
            // show hovered part details even when V2 owns input.
            _partBridge?.SetHoveredPart(HoveredPart);
        }

        private void LateUpdate()
        {
            if (!_bootstrapped || _feedbackPresenter == null)
                return;

            // When the part bridge is connected, it owns runtime part visuals.
            // Running V2 feedback on top of that causes color contention.
            if (_partBridge != null)
                return;

            var feedbackData = new InteractionFeedbackData
            {
                HoveredPart = HoveredPart,
                SelectedPart = SelectedPart,
                DraggedPart = DraggedPart
            };

            _feedbackPresenter.UpdateFeedback(CurrentState, feedbackData);
        }

        // ── Hover Tracking ──

        private void UpdateHover()
        {
            if (_camera == null)
            {
                HoveredPart = null;
                return;
            }

            if (IsToolModeLockedForParts())
            {
                HoveredPart = null;
                return;
            }

            // Keep the dragged part in hovered state so selected-hover visuals
            // remain active while the user drags.
            if (DraggedPart != null)
            {
                HoveredPart = DraggedPart;
                return;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                HoveredPart = null;
                return;
            }

            Vector2 screenPos = mouse.position.ReadValue();
            HoveredPart = RaycastValidPart(screenPos);
        }

        // ── Public API ──

        public void ForceState(InteractionState state) => TransitionTo(state);

        // ── State Machine ──

        /// <summary>
        /// Check if a hit GameObject is actually a spawned part (not the floor/environment).
        /// Walks up the transform hierarchy matching against PackagePartSpawner.SpawnedParts,
        /// exactly like PartInteractionBridge.FindPartFromHit.
        /// Returns the matched part GO, or null if the hit is not a part.
        /// </summary>
        private GameObject ValidatePartHit(GameObject hit)
        {
            if (hit == null) return null;
            if (_spawnedPartsProperty == null) return hit; // No spawner — can't validate, pass through

            var parts = _spawnedPartsProperty.GetValue(_spawnerRef) as IReadOnlyList<GameObject>;
            if (parts == null) return null;

            Transform t = hit.transform;
            while (t != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] != null && parts[i].transform == t)
                        return parts[i];
                }
                t = t.parent;
            }
            return null;
        }

        private GameObject RaycastValidPart(Vector2 screenPos)
        {
            if (_camera == null)
                return null;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, _settings.PartLayerMask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return null;

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                GameObject validPart = ValidatePartHit(hits[i].collider != null ? hits[i].collider.gameObject : null);
                if (validPart != null)
                    return NormalizeSelectableTarget(validPart);
            }

            return null;
        }

        /// <summary>
        /// Raycast for ghost trigger colliders at a screen position and focus camera if hit.
        /// Uses RaycastAll because ghost colliders are triggers that may sit behind solid
        /// environment colliders — a single Raycast would stop at the floor.
        /// </summary>
        private bool TryFocusGhost(Vector2 screenPos)
        {
            if (_cameraRig == null || _camera == null) return false;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < hits.Length; i++)
            {
                if (!hits[i].collider.isTrigger) continue;

                // Walk up hierarchy to find any MonoBehaviour whose type name contains "GhostPlacement"
                Transform t = hits[i].collider.transform;
                while (t != null)
                {
                    foreach (var comp in t.GetComponents<MonoBehaviour>())
                    {
                        if (comp != null && comp.GetType().Name == "GhostPlacementInfo")
                        {
                            _cameraRig.FocusOn(t.position);
                            OseLog.Info($"[Orchestrator] Ghost focused at {t.position}");
                            return true;
                        }
                    }
                    t = t.parent;
                }
            }
            return false;
        }

        private void ProcessIntent(InteractionIntent intent)
        {
            // Suppress all intents during reposition mode (Cancel exits via event bus)
            if (CurrentState == InteractionState.RepositioningAssembly)
            {
                if (intent.IntentKind == InteractionIntent.Kind.Cancel)
                    RuntimeEventBus.Publish(new RepositionModeChanged(false));
                return;
            }

            // UI takes priority — intent provider should already suppress,
            // but double-check here.
            if (intent.IsOverUI)
                return;

            // Validate part hits — filter out floor/environment objects
            if (intent.IsPartIntent && intent.HitTarget != null)
            {
                GameObject validPart = ValidatePartHit(intent.HitTarget);
                if (validPart == null)
                {
                    // Hit was not a part — treat as empty-space interaction
                    intent = new InteractionIntent(intent.IntentKind, intent.ScreenPosition, hitTarget: null);
                }
                else
                {
                    GameObject normalizedPart = NormalizeSelectableTarget(validPart);
                    intent = new InteractionIntent(intent.IntentKind, intent.ScreenPosition,
                        screenDelta: intent.ScreenDelta, hitTarget: normalizedPart);
                }
            }

            switch (intent.IntentKind)
            {
                // ── Camera intents ──
                case InteractionIntent.Kind.Orbit:
                case InteractionIntent.Kind.Pan:
                case InteractionIntent.Kind.Zoom:
                    RouteCameraIntent(intent);
                    break;

                case InteractionIntent.Kind.Focus:
                    HandleFocus();
                    break;

                case InteractionIntent.Kind.ResetView:
                    _cameraRig?.ResetToDefault();
                    break;

                // ── Part intents ──
                case InteractionIntent.Kind.Select:
                    HandleSelect(intent);
                    break;

                case InteractionIntent.Kind.BeginDrag:
                    HandleBeginDrag(intent);
                    break;

                case InteractionIntent.Kind.ContinueDrag:
                    if (IsToolModeLockedForParts())
                        break;
                    HandleContinueDrag(intent);
                    break;

                case InteractionIntent.Kind.EndDrag:
                    if (IsToolModeLockedForParts())
                        break;
                    HandleEndDrag(intent);
                    break;

                case InteractionIntent.Kind.Inspect:
                    HandleInspect(intent);
                    break;

                case InteractionIntent.Kind.Cancel:
                    HandleCancel();
                    break;
            }
        }

        // ── Camera routing ──

        private void RouteCameraIntent(InteractionIntent intent)
        {
            if (intent.IntentKind == InteractionIntent.Kind.Zoom)
            {
                float scrollAmount = intent.ScrollDelta + intent.PinchDelta;

                // ── Contextual scroll: depth adjust vs camera zoom ──
                // While dragging → always depth-adjust the dragged part
                if (CurrentState == InteractionState.DraggingPart && DraggedPart != null && _camera != null)
                {
                    DraggedPart.transform.position += _camera.transform.forward * scrollAmount * 5f;
                    return;
                }

                // Scroll over the selected part → depth-adjust it (forward/backward)
                if (SelectedPart != null && intent.HitTarget != null)
                {
                    GameObject validHit = NormalizeSelectableTarget(ValidatePartHit(intent.HitTarget));
                    if (validHit != null &&
                        validHit == SelectedPart &&
                        _camera != null &&
                        !IsToolModeLockedForParts() &&
                        !IsPartLockedForMovement(SelectedPart))
                    {
                        SelectedPart.transform.position += _camera.transform.forward * scrollAmount * 5f;
                        return;
                    }
                }

                // Otherwise → camera zoom
                if (_cameraRig != null)
                {
                    if (!IsCameraState(CurrentState) && CurrentState != InteractionState.DraggingPart)
                        TransitionTo(InteractionState.CameraZoom);
                    _cameraRig.ApplyZoom(scrollAmount);
                }
                return;
            }

            // Never orbit/pan while dragging a part
            if (CurrentState == InteractionState.DraggingPart)
                return;

            if (_cameraRig == null) return;

            switch (intent.IntentKind)
            {
                case InteractionIntent.Kind.Orbit:
                    TransitionTo(InteractionState.CameraOrbit);
                    _cameraRig.ApplyOrbit(intent.ScreenDelta);
                    break;

                case InteractionIntent.Kind.Pan:
                    TransitionTo(InteractionState.CameraPan);
                    _cameraRig.ApplyPan(intent.ScreenDelta);
                    break;
            }
        }

        // ── Part interaction handlers ──

        private void HandleSelect(InteractionIntent intent)
        {
            // Pipe connection port spheres: screen-proximity check runs BEFORE any
            // tool-mode routing so pipe steps are not hidden behind tool-locked flow.
            if (TryHandlePipeConnection(intent.ScreenPosition))
                return;

            bool toolLocked = IsToolModeLockedForParts();
            OseLog.VerboseInfo($"[V2] HandleSelect: toolLocked={toolLocked}, hitTarget={intent.HitTarget?.name ?? "null"}");

            if (toolLocked)
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            // When a part is already selected, try click-to-place BEFORE re-selecting.
            // The raycast often hits a part behind the transparent ghost, so without this
            // the user would keep re-selecting parts instead of placing onto the ghost.
            if (SelectedPart != null && _partBridge != null &&
                _partBridge.TryClickToPlace(SelectedPart, intent.ScreenPosition))
            {
                SelectedPart = null;
                HoveredPart = null;
                DraggedPart = null;
                TransitionTo(InteractionState.Idle);
                _actionBridge?.OnExternallyResolvedDeselected();
                return;
            }

            GameObject selectedTarget = NormalizeSelectableTarget(intent.HitTarget);
            if (selectedTarget != null)
            {
                // Clicked on a part → select it
                SelectedPart = selectedTarget;
                TransitionTo(InteractionState.PartSelected);

                _actionBridge?.OnExternallyResolvedPartSelected(selectedTarget);

                // Smart pivot: shift camera to orbit around the midpoint between
                // the selected part and its target ghost. Using the midpoint keeps
                // both source part and destination visible during orbit.
                if (_settings.EnableSmartPivot && _cameraRig != null)
                {
                    Vector3 partPos = selectedTarget.transform.position;
                    if (_settings.EnablePivotToTarget && _partBridge != null &&
                        _partBridge.TryGetGhostWorldPosForPart(selectedTarget.name, out Vector3 ghostPos))
                    {
                        _cameraRig.SetPivot(Vector3.Lerp(partPos, ghostPos, 0.5f));
                    }
                    else
                    {
                        _cameraRig.SetPivot(partPos);
                    }
                }
            }
            else
            {
                HandleEmptyClickFallback(intent.ScreenPosition);
            }
        }

        private void HandleBeginDrag(InteractionIntent intent)
        {
            // Pipe connection port spheres: screen-proximity check runs before drag logic.
            if (TryHandlePipeConnection(intent.ScreenPosition))
                return;

            if (IsToolModeLockedForParts())
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            GameObject dragTarget = NormalizeSelectableTarget(intent.HitTarget);
            if (dragTarget == null) return;
            if (IsPartLockedForMovement(dragTarget))
            {
                // Keep selection, but block movement for already-placed parts.
                SelectedPart = dragTarget;
                TransitionTo(InteractionState.PartSelected);

                _actionBridge?.OnExternallyResolvedPartSelected(dragTarget);

                OseLog.VerboseInfo($"[Orchestrator] Drag blocked for locked part '{dragTarget.name}'.");
                return;
            }

            DraggedPart = dragTarget;
            SelectedPart = dragTarget;
            TransitionTo(InteractionState.DraggingPart);

            _actionBridge?.OnExternallyResolvedPartSelected(dragTarget);
            _actionBridge?.OnPartGrabbed(dragTarget);

            OseLog.VerboseInfo($"[Orchestrator] Begin drag: {dragTarget.name}");
        }

        private void HandleContinueDrag(InteractionIntent intent)
        {
            if (CurrentState != InteractionState.DraggingPart || DraggedPart == null)
                return;

            // Move the dragged part to follow the pointer
            if (_camera != null)
            {
                Ray ray = _camera.ScreenPointToRay(intent.ScreenPosition);
                // Project onto the camera-facing plane at the part's current depth
                float depth = Vector3.Dot(DraggedPart.transform.position - _camera.transform.position,
                                          _camera.transform.forward);
                if (depth < 0.1f) depth = 0.1f;
                Vector3 worldPos = _camera.transform.position + ray.direction * (depth / Vector3.Dot(ray.direction, _camera.transform.forward));

                // Apply placement assist if enabled
                if (_placementAssist != null)
                {
                    // For now, apply raw position. PlacementAssist can refine it
                    // when we have target info from the step system.
                }

                DraggedPart.transform.position = worldPos;
            }
        }

        private void HandleEndDrag(InteractionIntent intent)
        {
            if (CurrentState != InteractionState.DraggingPart)
                return;

            OseLog.VerboseInfo($"[Orchestrator] End drag: {DraggedPart?.name}");

            // Notify the canonical action pipeline that the part was released.
            // PartInteractionBridge (still running snap/validation) will handle
            // the placement validation through its HandleCanonicalAction(Place).
            _actionBridge?.OnPartReleased();

            DraggedPart = null;
            TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
        }

        private void HandleInspect(InteractionIntent intent)
        {
            if (TryHandlePipeConnection(intent.ScreenPosition))
                return;

            if (IsToolModeLockedForParts())
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            GameObject inspectedTarget = NormalizeSelectableTarget(intent.HitTarget);
            if (inspectedTarget == null) return;

            SelectedPart = inspectedTarget;
            TransitionTo(InteractionState.InspectMode);

            _actionBridge?.OnExternallyResolvedPartInspected(inspectedTarget);
        }

        private void HandleFocus()
        {
            if (_cameraRig == null) return;

            if (SelectedPart != null)
                _cameraRig.FocusOn(SelectedPart.transform.position);
            else
                _cameraRig.ResetToDefault();
        }

        private void HandleCancel()
        {
            if (CurrentState == InteractionState.ToolFocus)
            {
                if (_previewController != null && _previewController.IsActive)
                {
                    _previewController.Cancel();
                    return;
                }
                if (_toolFocusController != null && _toolFocusController.IsActive)
                {
                    _toolFocusController.Cancel();
                    return;
                }
            }

            if (CurrentState == InteractionState.DraggingPart)
            {
                // Cancel mid-drag: drop part where it is
                DraggedPart = null;
                _actionBridge?.OnPartReleased();
            }

            if (CurrentState == InteractionState.InspectMode)
            {
                TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
                return;
            }

            // Deselect
            SelectedPart = null;
            HoveredPart = null;
            TransitionTo(InteractionState.Idle);

            _actionBridge?.OnDeselected();
        }

        private void HandleEmptyClickFallback(Vector2 screenPos)
        {
            // If a part is selected, try click-to-place first (explicit placement intent
            // takes priority over camera navigation).
            if (SelectedPart != null && _partBridge != null &&
                _partBridge.TryClickToPlace(SelectedPart, screenPos))
            {
                SelectedPart = null;
                HoveredPart = null;
                DraggedPart = null;
                TransitionTo(InteractionState.Idle);
                _actionBridge?.OnExternallyResolvedDeselected();
                return;
            }

            // Check if click was on a ghost part → focus camera on it.
            // Skip when a part is selected — the user is trying to place, not navigate,
            // and re-centering on the ghost would lose sight of the source parts.
            if (SelectedPart == null && TryFocusGhost(screenPos))
                return;

            // Check if click was near a pulsating tool target → focus camera on it.
            // This works regardless of tool equip state, so users can navigate to targets.
            if (_cameraRig != null && _partBridge != null &&
                _partBridge.TryGetNearestToolTargetWorldPos(screenPos, out Vector3 targetWorldPos))
            {
                _cameraRig.FocusOn(targetWorldPos);
                return; // don't deselect — user clicked to navigate
            }

            if (SelectedPart == null)
                return;

            SelectedPart = null;
            TransitionTo(InteractionState.Idle);

            _actionBridge?.OnExternallyResolvedDeselected();
        }

        private bool TryHandlePipeConnection(Vector2 screenPos)
        {
            // Pipe connection port spheres use screen-proximity targeting rather than
            // part-hit resolution, so V2 keeps this check ahead of part/tool routing.
            if (_partBridge == null)
                return false;

            return _partBridge.TryPipeConnection(screenPos);
        }

        // ── Helpers ──

        private void TransitionTo(InteractionState newState)
        {
            if (CurrentState == newState) return;
            OseLog.VerboseInfo($"[InteractionOrchestrator] {CurrentState} → {newState}");
            CurrentState = newState;
        }

        private static bool IsCameraState(InteractionState state) =>
            state is InteractionState.CameraOrbit
                or InteractionState.CameraPan
                or InteractionState.CameraZoom;

        private static bool IsPartLockedForMovement(GameObject part)
        {
            if (part == null)
                return false;

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return false;

            return partController.IsPartLockedForMovement(part.name);
        }

        private GameObject NormalizeSelectableTarget(GameObject target)
        {
            if (target == null)
                return null;

            if (_partBridge == null)
                return target;

            return _partBridge.NormalizeSelectableTarget(target);
        }

        /// <summary>
        /// Routes a tool action directly to the legacy bridge, bypassing the
        /// canonical action router. Falls back to the router if the direct call
        /// is unavailable.
        /// </summary>
        private void RouteToolAction(Vector2 screenPos)
        {
            if (_partBridge == null)
            {
                OseLog.Warn("[V2] RouteToolAction: no part bridge — canonical fallback only.");
                _actionBridge?.OnToolPrimaryAction();
                return;
            }

            OseLog.Info($"[V2] RouteToolAction resolve at ({screenPos.x:F0},{screenPos.y:F0})");
            if (_partBridge.TryResolveToolActionTarget(screenPos, out string targetId, out Vector3 targetWorldPos, out Vector3 surfaceWorldPos, out Vector3 weldAxis, out float weldLength))
            {
                OseLog.Info($"[V2] RouteToolAction: resolved target='{targetId}' at {targetWorldPos}, surface={surfaceWorldPos}. Executing...");

                // ── Tool Action Preview: "I Do / We Do / You Do" ──
                if (_settings.EnableToolActionPreview && _previewController != null)
                {
                    PreviewMode? previewMode = ResolvePreviewMode();
                    OseLog.Info($"[V2] Preview check: mode={previewMode}, target='{targetId}', pos={targetWorldPos}");
                    // Use surfaceWorldPos for the preview so the tool targets the actual joint, not the lifted sphere
                    if (previewMode.HasValue && TryEnterToolActionPreview(targetId, surfaceWorldPos, previewMode.Value, weldAxis, weldLength))
                        return;
                }
                else
                {
                    OseLog.Info($"[V2] Preview skipped: EnableToolActionPreview={_settings.EnableToolActionPreview}, controller={_previewController != null}");
                }

                if (_partBridge.TryToolAction(targetId))
                {
                    OseLog.Info($"[V2] RouteToolAction: TryToolAction SUCCESS for '{targetId}'.");
                    TrySpawnPersistentToolOnComplete(targetId, surfaceWorldPos);
                    if (_cameraRig != null)
                        _cameraRig.FocusOn(targetWorldPos);
                    return;
                }

                OseLog.Warn($"[V2] RouteToolAction: TryToolAction FAILED for '{targetId}' — execution rejected.");
                // Execution was rejected (wrong tool, missing tool, etc.). Keep the
                // fallback ordering in V2 by focusing the already-resolved target.
                if (_cameraRig != null)
                    _cameraRig.FocusOn(targetWorldPos);
                return;
            }
            OseLog.Info("[V2] RouteToolAction: TryResolveToolActionTarget returned false.");

            // Tool action failed (wrong tool, no tool equipped, etc.) — still focus
            // camera on the nearest target sphere so the user can navigate to it.
            if (_cameraRig != null && _partBridge != null &&
                _partBridge.TryGetNearestToolTargetWorldPos(screenPos, out Vector3 nearestTargetPos))
            {
                _cameraRig.FocusOn(nearestTargetPos);
                return;
            }

            OseLog.Info("[V2] RouteToolAction: bridge returned false — canonical fallback.");
            _actionBridge?.OnToolPrimaryAction();
        }

        /// <summary>
        /// Attempts to enter ToolFocus for the resolved target. Returns true if
        /// gesture engagement was activated (caller should skip immediate execution).
        /// Returns false if the step doesn't use gesture engagement (easy/absent mode).
        /// </summary>
        private bool TryEnterToolFocus(string targetId, Vector3 targetWorldPos, Vector2 screenPos)
        {
            // Get the current step definition
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return false;

            StepController stepCtrl = session?.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
                return false;

            StepDefinition step = stepCtrl.CurrentStepDefinition;
            if (step == null || !GestureConfigResolver.IsGestureEngaged(step))
                return false;

            // Enter ToolFocus
            TransitionTo(InteractionState.ToolFocus);

            _toolFocusController.Enter(
                step,
                targetId,
                targetWorldPos,
                screenPos,
                ResolvedMode,
                onComplete: completedTargetId =>
                {
                    // Gesture completed — execute the actual tool action
                    OseLog.Info($"[V2] ToolFocus completed for '{completedTargetId}' — executing tool action.");
                    if (_partBridge != null && _partBridge.TryToolAction(completedTargetId))
                    {
                        OseLog.Info($"[V2] Post-gesture TryToolAction SUCCESS for '{completedTargetId}'.");
                        if (_cameraRig != null)
                            _cameraRig.FocusOn(targetWorldPos);
                    }
                    TransitionTo(InteractionState.Idle);
                },
                onCancel: () =>
                {
                    OseLog.Info("[V2] ToolFocus cancelled — returning to idle.");
                    TransitionTo(InteractionState.Idle);
                });

            // If the gesture type was Tap, the controller fires onComplete synchronously
            // and we're already back in Idle — that's fine.
            return true;
        }

        /// <summary>
        /// Resolves the preview mode based on completed target count for the current step.
        /// Returns null if the step should skip preview (solo / "You Do" phase).
        /// </summary>
        private PreviewMode? ResolvePreviewMode()
        {
            if (_toolGhost == null)
                return null;

            // Must be a Use-family step
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
            {
                OseLog.Info("[V2] ResolvePreviewMode: no MachineSessionController");
                return null;
            }
            StepController stepCtrl = session?.AssemblyController?.StepController;
            if (stepCtrl == null || !stepCtrl.HasActiveStep)
            {
                OseLog.Info($"[V2] ResolvePreviewMode: stepCtrl={stepCtrl != null}, hasActive={stepCtrl?.HasActiveStep}");
                return null;
            }
            StepDefinition step = stepCtrl.CurrentStepDefinition;
            if (step == null || step.ResolvedFamily != Content.StepFamily.Use)
            {
                OseLog.Info($"[V2] ResolvePreviewMode: step={step?.id}, family={step?.ResolvedFamily} (need Use)");
                return null;
            }

            int completedCount = _toolGhost.GetCompletedToolTargetCount();
            OseLog.Info($"[V2] ResolvePreviewMode: step='{step.id}', family={step.ResolvedFamily}, completedCount={completedCount}");

            // "I Do, We Do, You Do"
            if (completedCount == 0)
                return PreviewMode.Observe;
            if (completedCount == 1)
                return PreviewMode.Guided;

            // 2+ → Solo (click-to-complete)
            return null;
        }

        /// <summary>
        /// Enters the Tool Action Preview for the resolved target.
        /// Returns true if preview was activated (caller should skip immediate execution).
        /// </summary>
        private bool TryEnterToolActionPreview(string targetId, Vector3 targetWorldPos, PreviewMode mode, Vector3 weldAxis = default, float weldLength = 0f)
        {
            GameObject toolGhost = _toolGhost?.GetToolGhost();
            string profile = _toolGhost?.GetActiveToolProfile();
            OseLog.Info($"[V2] TryEnterToolActionPreview: ghost={toolGhost?.name ?? "NULL"}, profile='{profile}', mode={mode}");
            if (toolGhost == null)
            {
                OseLog.Info("[V2] TryEnterToolActionPreview: no tool ghost — falling back to click-to-complete.");
                return false;
            }

            TransitionTo(InteractionState.ToolFocus);

            // Suspend cursor position updates so the ghost isn't yanked back each frame
            _toolGhost?.SetToolGhostPositionSuspended(true);

            // Profile-aware camera: tighten to close-up of the action point
            _guidanceService?.FrameToolAction(targetWorldPos, profile ?? "");

            _previewController.Enter(
                targetId,
                targetWorldPos,
                toolGhost,
                profile ?? "",
                mode,
                ResolvedMode,
                onComplete: completedTargetId =>
                {
                    _toolGhost?.SetToolGhostPositionSuspended(false);
                    OseLog.Info($"[V2] Tool action preview completed for '{completedTargetId}' — executing tool action.");
                    _toolGhost?.IncrementCompletedToolTargetCount();

                    if (_partBridge != null && _partBridge.TryToolAction(completedTargetId))
                    {
                        OseLog.Info($"[V2] Post-preview TryToolAction SUCCESS for '{completedTargetId}'.");
                    }
                    // Ease back to step home framing after the action
                    _guidanceService?.ReturnFromToolAction();
                    TransitionTo(InteractionState.Idle);
                },
                onCancel: () =>
                {
                    _toolGhost?.SetToolGhostPositionSuspended(false);
                    OseLog.Info("[V2] Tool action preview cancelled — returning to idle.");
                    // Return to step home on cancel too
                    _guidanceService?.ReturnFromToolAction();
                    TransitionTo(InteractionState.Idle);
                },
                onActionDone: (doneTargetId, actionPos, actionRot) =>
                {
                    // For persistent tools (clamps), convert the cursor ghost in-place
                    // so it stays at the target — no clone, no return animation.
                    if (TryConvertGhostToPersistentAtAction(doneTargetId, actionPos, actionRot))
                    {
                        _previewController.SkipReturn();
                    }
                },
                weldAxis: weldAxis,
                weldLength: weldLength);

            return true;
        }

        /// <summary>
        /// Called at the end of the action phase (before return animation).
        /// For persistent tools (clamps), converts the cursor ghost in-place so it
        /// stays at the target — no clone, no return animation. The bridge spawns
        /// a fresh cursor ghost for subsequent targets.
        /// Returns true if the ghost was converted (caller should skip return).
        /// </summary>
        // ── Persistent tool delegation ──

        private bool TryConvertGhostToPersistentAtAction(string targetId, Vector3 actionPos, Quaternion actionRot)
            => _persistentToolController?.TryConvertGhostAtAction(targetId, actionPos, actionRot) ?? false;

        private void TrySpawnPersistentToolOnComplete(string targetId, Vector3 worldPos)
            => _persistentToolController?.TryConvertGhostOnComplete(targetId, worldPos);

        private void CleanUpPersistentToolsForStep(string stepId)
            => _persistentToolController?.CleanUpForStep(stepId);

        private static bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) ||
                session == null ||
                session.ToolController == null)
            {
                return false;
            }

            // If a tool is actively equipped, always lock parts.
            if (!string.IsNullOrWhiteSpace(session.ToolController.ActiveToolId))
                return true;

            // If the step has a configured (incomplete) tool action, only lock parts
            // after all required part placements are done. Mixed placement+tool steps
            // (e.g. "place 4 posts then clamp") must allow part interaction first.
            if (session.ToolController.TryGetPrimaryActionSnapshot(
                    out ToolRuntimeController.ToolActionSnapshot snapshot))
            {
                if (!snapshot.IsConfigured || snapshot.IsCompleted)
                    return false;

                // Check if the step still has outstanding part placements
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var partController) &&
                    !partController.AreActiveStepRequiredPartsPlaced())
                {
                    return false; // parts still need placing — don't lock for tools yet
                }

                return true;
            }

            return false;
        }

        // ── Debug Context Menus ──

        [ContextMenu("V2 Debug/Log Current State")]
        private void DebugLogState()
        {
            OseLog.Info($"[V2 Debug] Mode={ResolvedMode} State={CurrentState} " +
                       $"Selected={SelectedPart?.name ?? "none"} Dragged={DraggedPart?.name ?? "none"} " +
                       $"Provider={_intentProvider?.GetType().Name ?? "none"}");
        }

        [ContextMenu("V2 Debug/Reset to Idle")]
        private void DebugResetIdle()
        {
            SelectedPart = null;
            DraggedPart = null;
            HoveredPart = null;
            TransitionTo(InteractionState.Idle);
            OseLog.Info("[V2 Debug] Forced reset to Idle.");
        }

        [ContextMenu("V2 Debug/Toggle UseV2Interaction")]
        private void DebugToggleV2()
        {
            if (_settings == null) return;
            _settings.UseV2Interaction = !_settings.UseV2Interaction;
            OseLog.Info($"[V2 Debug] UseV2Interaction = {_settings.UseV2Interaction}");

            // When toggling off, ensure bridge releases control
            if (!_settings.UseV2Interaction && _partBridge != null)
                _partBridge.ExternalControlEnabled = false;
        }
    }
}
