using System.Collections.Generic;
using System.Reflection;
using OSE.App;
using OSE.Core;
using OSE.Input;
using OSE.Interaction;
using OSE.Interaction.V2.Integration;
using OSE.Runtime;
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
        private LegacyBridgeAdapter _legacyBridge;
        private CanonicalActionBridge _actionBridge;
        private PlacementAssistService _placementAssist;
        private InteractionFeedbackPresenter _feedbackPresenter;

        // ── Internal state ──

        private bool _cameraIntentActiveLastFrame;
        private int _intentLogCountdown = 20; // Log first N non-None intents for diagnostics
        private bool _bootstrapped;

        // Part validation (resolved via reflection to avoid circular asmdef dependency)
        private object _spawnerRef;
        private PropertyInfo _spawnedPartsProperty;

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

            // ── 6. Legacy Bridge ──
            _legacyBridge = new LegacyBridgeAdapter(_actionBridge);

            var allMonos = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mono in allMonos)
            {
                if (mono.GetType().Name == "PartInteractionBridge")
                {
                    _legacyBridge.Connect(mono);
                    break;
                }
            }

            if (!_legacyBridge.IsConnected)
                OseLog.Warn("[InteractionOrchestrator] PartInteractionBridge not found. Legacy bridge not connected.");

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

            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<RepositionModeChanged>(HandleRepositionModeChanged);

            _bootstrapped = true;
            OseLog.Info($"[InteractionOrchestrator] V2 READY. Mode={mode} Camera={_camera.name} LegacyBridge={_legacyBridge.IsConnected}");
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
            RuntimeEventBus.Unsubscribe<RepositionModeChanged>(HandleRepositionModeChanged);
            _legacyBridge?.Disconnect();
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

        private void HandleRepositionModeChanged(RepositionModeChanged evt)
        {
            if (!_bootstrapped) return;

            if (evt.IsActive)
            {
                // Release any in-progress drag and deselect
                if (CurrentState == InteractionState.DraggingPart && DraggedPart != null)
                {
                    if (_legacyBridge != null)
                        _legacyBridge.ReleasePart();
                    else
                        _actionBridge?.OnPartReleased();
                    DraggedPart = null;
                }

                if (SelectedPart != null)
                {
                    SelectedPart = null;
                    if (_legacyBridge != null)
                        _legacyBridge.DeselectAll();
                    else
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
                if (_legacyBridge != null)
                    _legacyBridge.ReleasePart();
                else
                    _actionBridge?.OnPartReleased();

                DraggedPart = null;
                TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
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
            _legacyBridge?.SetHoveredPart(HoveredPart);
        }

        private void LateUpdate()
        {
            if (!_bootstrapped || _feedbackPresenter == null)
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
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _settings.PartLayerMask))
            {
                HoveredPart = ValidatePartHit(hit.collider.gameObject);
            }
            else
            {
                HoveredPart = null;
            }
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
                else if (validPart != intent.HitTarget)
                {
                    // Hit a child of a part — use the parent part GO
                    intent = new InteractionIntent(intent.IntentKind, intent.ScreenPosition,
                        screenDelta: intent.ScreenDelta, hitTarget: validPart);
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
                    GameObject validHit = ValidatePartHit(intent.HitTarget);
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
            bool toolLocked = IsToolModeLockedForParts();

            if (toolLocked)
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            // Pipe connection port spheres: screen-proximity check runs BEFORE part-hit resolution
            // so that a part occluding a sphere in the raycast never swallows the tap.
            if (_legacyBridge != null && _legacyBridge.TryPipeConnection(intent.ScreenPosition))
                return;

            if (intent.HitTarget != null)
            {
                // Clicked on a part → select it
                SelectedPart = intent.HitTarget;
                TransitionTo(InteractionState.PartSelected);

                if (_legacyBridge != null)
                    _legacyBridge.SelectPart(intent.HitTarget);
                else
                    _actionBridge?.OnPartSelected(intent.HitTarget);

                // Smart pivot: shift camera to orbit around placement target (or part if no ghost)
                if (_settings.EnableSmartPivot && _cameraRig != null)
                {
                    Vector3 pivotPos = intent.HitTarget.transform.position;
                    if (_settings.EnablePivotToTarget && _legacyBridge != null &&
                        _legacyBridge.TryGetGhostWorldPosForPart(intent.HitTarget.name, out Vector3 ghostPos))
                    {
                        pivotPos = ghostPos;
                    }
                    _cameraRig.SetPivot(pivotPos);
                }
            }
            else
            {
                // Clicked on empty space.
                // If a part is selected, try click-to-place first (explicit placement intent
                // takes priority over camera navigation).
                if (SelectedPart != null && _legacyBridge != null &&
                    _legacyBridge.TryClickToPlace(intent.ScreenPosition))
                    return;

                // Check if click was on a ghost part → focus camera on it.
                if (TryFocusGhost(intent.ScreenPosition))
                    return;

                // Check if click was near a pulsating tool target → focus camera on it.
                // This works regardless of tool equip state, so users can navigate to targets.
                if (_cameraRig != null && _legacyBridge != null &&
                    _legacyBridge.TryGetNearestToolTargetWorldPos(intent.ScreenPosition, out Vector3 targetWorldPos))
                {
                    _cameraRig.FocusOn(targetWorldPos);
                    return; // don't deselect — user clicked to navigate
                }

                // Deselect.
                if (SelectedPart != null)
                {
                    SelectedPart = null;
                    TransitionTo(InteractionState.Idle);

                    if (_legacyBridge != null)
                        _legacyBridge.DeselectAll();
                    else
                        _actionBridge?.OnDeselected();
                }
            }
        }

        private void HandleBeginDrag(InteractionIntent intent)
        {
            // Pipe connection port spheres: screen-proximity check runs before drag logic
            if (_legacyBridge != null && _legacyBridge.TryPipeConnection(intent.ScreenPosition))
                return;

            if (IsToolModeLockedForParts())
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            if (intent.HitTarget == null) return;
            if (IsPartLockedForMovement(intent.HitTarget))
            {
                // Keep selection, but block movement for already-placed parts.
                SelectedPart = intent.HitTarget;
                TransitionTo(InteractionState.PartSelected);

                if (_legacyBridge != null)
                    _legacyBridge.SelectPart(intent.HitTarget);
                else
                    _actionBridge?.OnPartSelected(intent.HitTarget);

                OseLog.VerboseInfo($"[Orchestrator] Drag blocked for locked part '{intent.HitTarget.name}'.");
                return;
            }

            DraggedPart = intent.HitTarget;
            SelectedPart = intent.HitTarget;
            TransitionTo(InteractionState.DraggingPart);

            if (_legacyBridge != null)
            {
                // Ensure selection is set first, then grab
                _legacyBridge.SelectPart(intent.HitTarget);
                _legacyBridge.GrabPart(intent.HitTarget);
            }
            else
            {
                _actionBridge?.OnPartSelected(intent.HitTarget);
                _actionBridge?.OnPartGrabbed(intent.HitTarget);
            }

            OseLog.VerboseInfo($"[Orchestrator] Begin drag: {intent.HitTarget.name}");
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
            if (_legacyBridge != null)
                _legacyBridge.ReleasePart();
            else
                _actionBridge?.OnPartReleased();

            DraggedPart = null;
            TransitionTo(SelectedPart != null ? InteractionState.PartSelected : InteractionState.Idle);
        }

        private void HandleInspect(InteractionIntent intent)
        {
            if (IsToolModeLockedForParts())
            {
                RouteToolAction(intent.ScreenPosition);
                return;
            }

            if (intent.HitTarget == null) return;

            SelectedPart = intent.HitTarget;
            TransitionTo(InteractionState.InspectMode);

            if (_legacyBridge != null)
                _legacyBridge.InspectPart(intent.HitTarget);
            else
                _actionBridge?.OnPartInspected(intent.HitTarget);
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
            if (CurrentState == InteractionState.DraggingPart)
            {
                // Cancel mid-drag: drop part where it is
                DraggedPart = null;
                if (_legacyBridge != null)
                    _legacyBridge.ReleasePart();
                else
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

            if (_legacyBridge != null)
                _legacyBridge.DeselectAll();
            else
                _actionBridge?.OnDeselected();
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

        /// <summary>
        /// Routes a tool action directly to the legacy bridge, bypassing the
        /// canonical action router. Falls back to the router if the direct call
        /// is unavailable.
        /// </summary>
        private void RouteToolAction(Vector2 screenPos)
        {
            if (_legacyBridge == null)
            {
                OseLog.Warn("[V2] RouteToolAction: no legacy bridge — canonical fallback only.");
                _actionBridge?.OnToolPrimaryAction();
                return;
            }

            OseLog.Info($"[V2] RouteToolAction → bridge at ({screenPos.x:F0},{screenPos.y:F0})");
            if (_legacyBridge.TryToolAction(screenPos))
            {
                // Focus camera on the executed target so the user can see the result.
                if (_cameraRig != null && _legacyBridge.TryGetLastToolActionWorldPos(out Vector3 actionWorldPos))
                    _cameraRig.FocusOn(actionWorldPos);
                return;
            }

            // Tool action failed (wrong tool, no tool equipped, etc.) — still focus
            // camera on the nearest target sphere so the user can navigate to it.
            if (_cameraRig != null &&
                _legacyBridge.TryGetNearestToolTargetWorldPos(screenPos, out Vector3 nearestTargetPos))
            {
                _cameraRig.FocusOn(nearestTargetPos);
                return;
            }

            OseLog.Info("[V2] RouteToolAction: bridge returned false — canonical fallback.");
            _actionBridge?.OnToolPrimaryAction();
        }

        private static bool IsToolModeLockedForParts()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) ||
                session == null ||
                session.ToolController == null)
            {
                return false;
            }

            // Check both: is a tool equipped, OR does the current step have a
            // configured (incomplete) tool action? Either condition locks parts.
            if (!string.IsNullOrWhiteSpace(session.ToolController.ActiveToolId))
                return true;

            if (session.ToolController.TryGetPrimaryActionSnapshot(
                    out ToolRuntimeController.ToolActionSnapshot snapshot))
            {
                return snapshot.IsConfigured && !snapshot.IsCompleted;
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

            // When toggling off, ensure legacy bridge releases control
            if (!_settings.UseV2Interaction)
                _legacyBridge?.Disconnect();
        }
    }
}
