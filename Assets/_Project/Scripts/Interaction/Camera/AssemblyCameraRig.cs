using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Orbital camera rig for assembly scenes. Place on the main camera or a parent transform.
    ///
    /// The camera is positioned on a constraint sphere around a dynamic pivot.
    /// This component only applies commands — it never reads input directly.
    /// The InteractionOrchestrator calls ApplyOrbit/Pan/Zoom based on intents.
    ///
    /// In LateUpdate, the current state smoothly interpolates toward the target
    /// state, then constraints are enforced, then the transform is updated.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyCameraRig : MonoBehaviour
    {
        [SerializeField] private InteractionSettings _settings;

        // ── Internal state ──

        private CameraState _currentState;
        private CameraState _targetState;
        private CameraState _defaultState;

        private CameraConstraintSphere _constraint;
        private CameraSmoothing _smoothing;
        private CameraPivotResolver _pivotResolver;
        private CameraVantageSolver _vantageSolver;

        private bool _initialized;

        // ── Cue camera-follow state ──
        // While a PoseTransition cue translates a single part meaningfully,
        // PoseTransitionPlayer publishes CueCameraFollowStarted. We track the
        // motion and drive _targetState.PivotPosition along the fromWorld→toWorld
        // lerp for the cue's duration so the camera tracks the object in real
        // time. Stop event clears state; matching tokens prevent a stale Stop
        // from cancelling a newer Start (back-to-back cues).
        private bool _followActive;
        private object _followToken;
        private Vector3 _followFromWorld;
        private Vector3 _followToWorld;
        private float _followDuration;
        private string _followEasing;
        private float _followElapsed;

        // ── Public accessors ──

        public CameraState CurrentState => _currentState;
        public CameraState TargetState => _targetState;
        public CameraPivotResolver PivotResolver => _pivotResolver;

        // ── Lifecycle ──

        private void Awake()
        {
            _pivotResolver = new CameraPivotResolver();
            _vantageSolver = new CameraVantageSolver();
        }

        private void OnEnable()
        {
            RuntimeEventBus.Subscribe<CueCameraFollowStarted>(OnCueCameraFollowStarted);
            RuntimeEventBus.Subscribe<CueCameraFollowStopped>(OnCueCameraFollowStopped);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<CueCameraFollowStarted>(OnCueCameraFollowStarted);
            RuntimeEventBus.Unsubscribe<CueCameraFollowStopped>(OnCueCameraFollowStopped);
            _followActive = false;
            _followToken = null;
        }

        private void Start()
        {
            if (!_initialized)
                InitializeFromCurrentTransform(Vector3.zero);
        }

        private void LateUpdate()
        {
            if (!_initialized || _settings == null || !_settings.Enabled)
                return;

            // Cue-driven pivot follow — overrides _targetState.PivotPosition
            // for the cue's duration. Smoothing still applies, so the camera
            // doesn't snap between the pre-follow pivot and the fromWorld
            // starting point; it eases in naturally.
            if (_followActive)
                TickCameraFollow(Time.deltaTime);

            // Smooth interpolation
            _currentState = _smoothing.Step(_currentState, _targetState, Time.deltaTime);

            // Enforce constraints
            if (_settings.EnableCameraConstraintSphere)
                _currentState = _constraint.Clamp(_currentState);

            // Apply to transform
            ApplyStateToTransform(_currentState);
        }

        // ── Initialization ──

        /// <summary>
        /// Initialize the rig from the camera's current position and rotation.
        /// Computes a pivot point along the camera's forward direction so the
        /// orbital model preserves the exact editor view with no visual jump.
        /// </summary>
        public void InitializeFromCurrentTransform(Vector3 pivotHint, InteractionSettings settingsOverride = null)
        {
            if (settingsOverride != null)
                _settings = settingsOverride;

            if (_settings == null) return;

            _constraint = new CameraConstraintSphere(_settings);
            _smoothing = new CameraSmoothing(_settings);

            // Compute a sensible pivot: cast along the camera's forward direction.
            // This makes the orbital model match what the camera is actually looking at.
            Vector3 pivot = pivotHint;
            if (pivotHint == Vector3.zero)
            {
                // Raycast to find what the camera is looking at, or default to
                // a point 3 units in front of the camera.
                Ray ray = new Ray(transform.position, transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, 50f))
                    pivot = hit.point;
                else
                    pivot = transform.position + transform.forward * 3f;
            }

            _currentState = CameraState.FromTransform(transform, pivot);
            _targetState = _currentState;
            _defaultState = _currentState;

            _pivotResolver.SetSource(CameraPivotResolver.PivotSource.AssemblyCenter);

            _initialized = true;
        }

        // ── Commands (called by InteractionOrchestrator) ──

        /// <summary>Apply orbital rotation from screen-space delta.</summary>
        public void ApplyOrbit(Vector2 screenDelta)
        {
            if (!_initialized) return;
            _targetState.Yaw += screenDelta.x * _settings.OrbitSensitivity;
            _targetState.Pitch -= screenDelta.y * _settings.OrbitSensitivity;
        }

        /// <summary>Apply pan from screen-space delta. Moves the pivot on the camera's local XY plane.</summary>
        public void ApplyPan(Vector2 screenDelta)
        {
            if (!_initialized) return;

            Transform t = transform;
            Vector3 right = t.right;
            Vector3 up = t.up;

            float scale = _targetState.Distance * _settings.PanSensitivity;
            Vector3 panOffset = (-right * screenDelta.x + -up * screenDelta.y) * scale;

            _targetState.PivotPosition += panOffset;
        }

        /// <summary>Apply zoom (positive = zoom in, negative = zoom out).</summary>
        public void ApplyZoom(float delta)
        {
            if (!_initialized) return;
            // Logarithmic zoom so it feels consistent at all distances
            _targetState.Distance *= 1f - delta * _settings.ZoomSensitivity;
        }

        /// <summary>Smoothly move the pivot and frame to focus on a world position.</summary>
        public void FocusOn(Vector3 worldPosition, float distance = -1f)
        {
            if (!_initialized) return;
            _targetState.PivotPosition = worldPosition;
            if (distance > 0f)
                _targetState.Distance = distance;
        }

        /// <summary>
        /// Like <see cref="FocusOn"/> but also picks an orbital yaw/pitch/distance with
        /// line-of-sight to <paramref name="worldPosition"/>, walking through three tiers
        /// (current angle → yaw sweep → distance bump). When all tiers fail, falls back
        /// to <see cref="FocusOn"/> behavior (pivot + distance, current yaw/pitch
        /// unchanged) and returns false so callers can log / diagnose. The chosen
        /// state is clamped by <see cref="CameraConstraintSphere"/>.
        /// </summary>
        public bool TryFocusOnUnobstructed(
            Vector3 worldPosition,
            float distance,
            LayerMask occluderMask,
            float probeRadius,
            float nearTargetIgnore,
            Transform[] ignoreRoots,
            out CameraVantageResult result)
        {
            result = default;
            if (!_initialized) return false;

            float reqDistance = distance > 0f ? distance : _targetState.Distance;

            // Solver works against current yaw/pitch so the choice minimizes
            // disorientation from where the user is already looking.
            float minDist = _constraint != null ? _constraint.MinDistance : 0.3f;
            float maxDist = _constraint != null ? _constraint.MaxDistance : 10f;

            bool solved = _vantageSolver.TrySolve(
                worldPosition,
                reqDistance,
                _targetState.Yaw,
                _targetState.Pitch,
                occluderMask,
                probeRadius,
                nearTargetIgnore,
                ignoreRoots,
                minDist,
                maxDist,
                out result);

            _targetState.PivotPosition = worldPosition;

            if (solved)
            {
                _targetState.Yaw = result.Yaw;
                _targetState.Pitch = result.Pitch;
                _targetState.Distance = result.Distance;
            }
            else
            {
                _targetState.Distance = reqDistance;
            }

            // Clamp regardless of tier so a tier-3 distance bump still respects the
            // constraint sphere on the off-chance MaxDistance was tightened at runtime.
            if (_constraint != null)
                _targetState = _constraint.Clamp(_targetState);

            return solved;
        }

        /// <summary>Frame an axis-aligned bounding box so all contents are visible.</summary>
        public void FrameBounds(Bounds bounds)
        {
            if (!_initialized) return;
            _targetState.PivotPosition = bounds.center;

            // Compute distance from camera FOV so the bounding sphere fills the screen
            // at a consistent fraction, regardless of content size. Small parts get a
            // close-up; large assemblies get a wide view.
            //
            // formula: d = r / sin(halfFov) * padding
            //   → content sphere occupies 1/padding ≈ 74% of the half-FOV angle.
            //
            // Minimum is a comfort floor (~25 cm) to prevent clipping into geometry,
            // not an editorial decision about how far away the camera should be.
            // The old 1.5 m floor was suppressing close-up framing for small parts.
            float radius = bounds.extents.magnitude;
            Camera cam = GetComponent<Camera>();
            float fov = cam != null ? cam.fieldOfView : 60f;
            float halfAngleRad = fov * 0.5f * Mathf.Deg2Rad;
            const float padding = 1.35f;
            const float minDistance = 0.25f;
            float fovDistance = (radius / Mathf.Sin(halfAngleRad)) * padding;
            _targetState.Distance = Mathf.Max(fovDistance, minDistance);

            // Ensure an elevated "third person" viewing angle so the user sees the
            // assembly from above rather than a flat first-person perspective.
            // Only nudge when the camera is nearly horizontal (±10°), so we don't
            // fight the user after they've manually orbited to a preferred angle.
            if (Mathf.Abs(_targetState.Pitch) < 15f)
                _targetState.Pitch = 35f;
        }

        /// <summary>Reset to the state captured at initialization.</summary>
        public void ResetToDefault()
        {
            if (!_initialized) return;
            _targetState = _defaultState;
        }

        /// <summary>
        /// Apply a named viewpoint (from StepGuidanceService).
        /// The viewpoint offsets are relative to the current pivot.
        /// </summary>
        public void ApplyViewpoint(StepViewpoint viewpoint, bool animated = true)
        {
            if (!_initialized) return;

            _targetState.Yaw = viewpoint.Yaw;
            _targetState.Pitch = viewpoint.Pitch;
            _targetState.Distance = viewpoint.Distance;
            _targetState.PivotPosition += viewpoint.PivotOffset;

            if (!animated)
                _currentState = _targetState;
        }

        /// <summary>
        /// Directly set the pivot position (used by PivotResolver updates).
        /// </summary>
        public void SetPivot(Vector3 position)
        {
            if (!_initialized) return;
            _targetState.PivotPosition = position;
        }

        // ── Internal ──

        private void ApplyStateToTransform(CameraState state)
        {
            transform.position = state.ComputePosition();
            transform.rotation = state.ComputeRotation();
        }

        // ── Cue camera-follow handlers ──

        private void OnCueCameraFollowStarted(CueCameraFollowStarted evt)
        {
            _followActive = true;
            _followToken = evt.Token;
            _followFromWorld = evt.FromWorld;
            _followToWorld = evt.ToWorld;
            _followDuration = Mathf.Max(0.0001f, evt.DurationSeconds);
            _followEasing = evt.Easing;
            _followElapsed = 0f;
        }

        private void OnCueCameraFollowStopped(CueCameraFollowStopped evt)
        {
            // Token match prevents a stale Stop from the previous cue
            // cancelling a just-issued Start on a back-to-back transition.
            if (!_followActive) return;
            if (!ReferenceEquals(_followToken, evt.Token)) return;
            _followActive = false;
            _followToken = null;
        }

        private void TickCameraFollow(float deltaTime)
        {
            _followElapsed += deltaTime;
            float raw = Mathf.Clamp01(_followElapsed / _followDuration);
            float eased = ApplyEasing(_followEasing, raw);
            _targetState.PivotPosition = Vector3.Lerp(_followFromWorld, _followToWorld, eased);

            if (raw >= 1f)
            {
                _followActive = false;
                _followToken = null;
            }
        }

        // Mirrors OSE.UI.Root.EasingHelper. Kept local because
        // OSE.Interaction cannot depend on OSE.UI (layer direction).
        private static float ApplyEasing(string easing, float t) => easing switch
        {
            "linear"         => t,
            "easeIn"         => t * t,
            "easeOut"        => 1f - (1f - t) * (1f - t),
            "easeInOut"      => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f,
            "easeInCubic"    => t * t * t,
            "easeOutCubic"   => 1f - Mathf.Pow(1f - t, 3f),
            "easeInOutCubic" => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f,
            _                => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f, // default: easeInOut
        };
    }
}
