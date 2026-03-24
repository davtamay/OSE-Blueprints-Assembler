using UnityEngine;

namespace OSE.Interaction.V2
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

        private bool _initialized;

        // ── Public accessors ──

        public CameraState CurrentState => _currentState;
        public CameraState TargetState => _targetState;
        public CameraPivotResolver PivotResolver => _pivotResolver;

        // ── Lifecycle ──

        private void Awake()
        {
            _pivotResolver = new CameraPivotResolver();
        }

        private void Start()
        {
            if (!_initialized)
                InitializeFromCurrentTransform(Vector3.zero);
        }

        private void LateUpdate()
        {
            if (!_initialized || _settings == null || !_settings.UseV2Interaction)
                return;

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

        /// <summary>Frame an axis-aligned bounding box so all contents are visible.</summary>
        public void FrameBounds(Bounds bounds)
        {
            if (!_initialized) return;
            _targetState.PivotPosition = bounds.center;

            // Compute distance from camera FOV so the bounding sphere fits on screen
            // with a comfortable margin. Uses the vertical half-angle of the camera.
            float radius = bounds.extents.magnitude;
            Camera cam = GetComponent<Camera>();
            float fov = cam != null ? cam.fieldOfView : 60f;
            float halfAngleRad = fov * 0.5f * Mathf.Deg2Rad;
            // Distance = radius / sin(halfAngle) ensures the sphere fits vertically.
            // Multiply by padding factor so targets aren't right at the screen edge.
            const float padding = 1.35f;
            float fovDistance = (radius / Mathf.Sin(halfAngleRad)) * padding;
            _targetState.Distance = Mathf.Max(fovDistance, 1.5f);

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
    }
}
