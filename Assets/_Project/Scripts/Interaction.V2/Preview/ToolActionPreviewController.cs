using System;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Orchestrates the Tool Action Preview lifecycle:
    /// 1. Approach — tool ghost detaches from camera, lerps to working position near target
    /// 2. Action — profile-specific preview plays (observe or guided)
    /// 3. Return — tool ghost flies back to cursor position
    /// 4. Complete — ghost re-parents to camera, callback fires
    /// </summary>
    public sealed class ToolActionPreviewController
    {
        public enum Phase { Inactive, Approach, Action, Return }

        private Phase _phase;
        private PreviewMode _mode;
        private IToolActionPreview _preview;
        private GameObject _toolGhost;
        private Transform _originalParent;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Vector3 _startPos, _workingPos, _surfacePos;
        private Quaternion _startRot, _actionRot;
        private const float ApproachDuration = 0.5f;
        private const float ReturnDuration = 0.35f;
        private float _elapsed;
        private Action<string> _onComplete;
        private Action<string, Vector3, Quaternion> _onActionDone;
        private Action _onCancel;
        private string _targetId;
        private string _profile;
        private Vector3 _weldAxis;
        private float _weldLength;
        private Renderer _targetSphereRenderer;
        private bool _completed;

        // Return animation state
        private Vector3 _returnStartPos;
        private Quaternion _returnStartRot;
        private Vector3 _returnEndPos;
        private Quaternion _returnEndRot;

        public bool IsActive => _phase != Phase.Inactive;
        public Phase CurrentPhase => _phase;

        /// <summary>The tool's orientation during the action phase (before return animation).</summary>
        public Quaternion LastActionRotation => _actionRot;

        public void Enter(
            string targetId,
            Vector3 surfaceWorldPos,
            GameObject toolGhost,
            string profile,
            PreviewMode mode,
            InteractionMode platform,
            Action<string> onComplete,
            Action onCancel,
            Action<string, Vector3, Quaternion> onActionDone = null,
            Vector3 weldAxis = default,
            float weldLength = 0f)
        {
            if (toolGhost == null)
            {
                OseLog.Warn("[ToolActionPreview] Cannot enter — tool ghost is null.");
                onCancel?.Invoke();
                return;
            }

            _targetId = targetId;
            _toolGhost = toolGhost;
            _mode = mode;
            _profile = profile;
            _weldAxis = weldAxis;
            _weldLength = weldLength;
            _onComplete = onComplete;
            _onActionDone = onActionDone;
            _onCancel = onCancel;

            // Snapshot current ghost state and detach from camera
            _originalParent = toolGhost.transform.parent;
            _originalLocalPos = toolGhost.transform.localPosition;
            _originalLocalRot = toolGhost.transform.localRotation;
            _startPos = toolGhost.transform.position;
            _startRot = toolGhost.transform.rotation;
            _surfacePos = surfaceWorldPos;

            // Compute working position and action orientation
            float toolReach = EstimateTipDistance(toolGhost);
            ComputeWorkingPose(surfaceWorldPos, toolReach, profile, out _workingPos, out _actionRot);

            toolGhost.transform.SetParent(null, worldPositionStays: true);

            _preview = ToolActionPreviewFactory.Create(profile);
            _phase = Phase.Approach;
            _elapsed = 0f;
            _completed = false;

            // Hide the target sphere renderer so the tool action is fully visible
            _targetSphereRenderer = FindTargetSphereRenderer(surfaceWorldPos);
            if (_targetSphereRenderer != null)
                _targetSphereRenderer.enabled = false;

            OseLog.Info($"[ToolActionPreview] Entered — profile='{profile}', mode={mode}, target='{targetId}', tipDist={toolReach:F3}");
        }

        public void Tick(float deltaTime, Vector2 dragDelta = default)
        {
            if (_phase == Phase.Inactive) return;

            // Ghost was destroyed externally (step advanced, tool changed, etc.)
            if (_toolGhost == null)
            {
                OseLog.Info("[ToolActionPreview] Ghost destroyed externally — cancelling.");
                ExitDestroyed();
                return;
            }

            _elapsed += deltaTime;

            switch (_phase)
            {
                case Phase.Approach:
                    TickApproach();
                    break;
                case Phase.Action:
                    TickAction(deltaTime, dragDelta);
                    break;
                case Phase.Return:
                    TickReturn();
                    break;
            }
        }

        public void Cancel()
        {
            if (IsActive)
            {
                _completed = false;
                BeginReturn();
            }
        }

        /// <summary>
        /// Skips the return animation entirely — the ghost stays where it is.
        /// Called when the ghost has been converted into a persistent tool in-place
        /// and should not fly back to the cursor.
        /// </summary>
        public void SkipReturn()
        {
            if (!IsActive) return;

            if (_preview != null)
            {
                _preview.End(true);
                _preview = null;
            }
            RestoreTargetSphere();

            // Null the ghost reference so CleanupAndNotify won't try to re-parent it
            _toolGhost = null;
            CleanupAndNotify(completed: true);
        }

        // ── Phase ticks ──

        private void TickApproach()
        {
            float t = Mathf.Clamp01(_elapsed / ApproachDuration);
            float eased = EaseOutQuad(t);

            _toolGhost.transform.position = Vector3.Lerp(_startPos, _workingPos, eased);
            _toolGhost.transform.rotation = Quaternion.Slerp(_startRot, _actionRot, eased);

            if (t >= 1f)
            {
                _phase = Phase.Action;
                _elapsed = 0f;

                _toolGhost.transform.position = _workingPos;
                _toolGhost.transform.rotation = _actionRot;

                var ctx = new PreviewContext
                {
                    ToolGhost = _toolGhost,
                    TargetWorldPos = _surfacePos,
                    ToolStartWorldPos = _startPos,
                    ToolStartRotation = _startRot,
                    Profile = _profile,
                    Mode = _mode,
                    Platform = InteractionMode.Auto,
                    WeldAxis = _weldAxis,
                    WeldLength = _weldLength
                };
                _preview.Begin(ctx);
            }
        }

        private void TickAction(float deltaTime, Vector2 dragDelta)
        {
            float progress = (_mode == PreviewMode.Observe)
                ? _preview.TickObserve(deltaTime)
                : _preview.TickGuided(deltaTime, dragDelta);

            if (progress >= 1f)
            {
                _completed = true;
                _preview?.End(true);
                _preview = null;

                // Fire before return animation so callers can convert the ghost
                // in-place (persistent tools) or spawn clones at the action position.
                // Use the ghost's current rotation (not _actionRot) because the preview
                // may have modified it (e.g. TorquePreview adds a 90° rotation).
                if (_toolGhost != null)
                    _onActionDone?.Invoke(_targetId, _toolGhost.transform.position, _toolGhost.transform.rotation);

                // onActionDone may have called SkipReturn() — if so, we're already Inactive.
                if (_phase == Phase.Inactive) return;

                BeginReturn();
            }
        }

        private void TickReturn()
        {
            float t = Mathf.Clamp01(_elapsed / ReturnDuration);
            float eased = EaseInQuad(t);

            _toolGhost.transform.position = Vector3.Lerp(_returnStartPos, _returnEndPos, eased);
            _toolGhost.transform.rotation = Quaternion.Slerp(_returnStartRot, _returnEndRot, eased);

            if (t >= 1f)
                FinishExit();
        }

        // ── Return animation ──

        private void BeginReturn()
        {
            if (_phase == Phase.Action && _preview != null)
            {
                _preview.End(false);
                _preview = null;
            }

            RestoreTargetSphere();

            if (_toolGhost == null)
            {
                CleanupAndNotify(_completed);
                return;
            }

            Transform parent = _originalParent != null
                ? _originalParent
                : (Camera.main != null ? Camera.main.transform : null);

            if (parent == null)
            {
                FinishExit();
                return;
            }

            _returnStartPos = _toolGhost.transform.position;
            _returnStartRot = _toolGhost.transform.rotation;
            _returnEndPos = parent.TransformPoint(_originalLocalPos);
            _returnEndRot = parent.rotation * _originalLocalRot;

            _phase = Phase.Return;
            _elapsed = 0f;
        }

        private void FinishExit()
        {
            if (_toolGhost != null)
            {
                Transform parent = _originalParent != null
                    ? _originalParent
                    : (Camera.main != null ? Camera.main.transform : null);

                if (parent != null)
                {
                    _toolGhost.transform.SetParent(parent, worldPositionStays: false);
                    _toolGhost.transform.localPosition = _originalLocalPos;
                    _toolGhost.transform.localRotation = _originalLocalRot;
                }
            }

            CleanupAndNotify(_completed);
        }

        private void ExitDestroyed()
        {
            if (_preview != null)
            {
                _preview.End(false);
                _preview = null;
            }
            RestoreTargetSphere();
            CleanupAndNotify(completed: false);
        }

        private void CleanupAndNotify(bool completed)
        {
            _phase = Phase.Inactive;
            _preview = null;

            string targetId = _targetId;
            var onComplete = _onComplete;
            var onCancel = _onCancel;

            _onComplete = null;
            _onActionDone = null;
            _onCancel = null;
            _targetId = null;
            _toolGhost = null;
            _profile = null;

            if (completed)
            {
                OseLog.Info($"[ToolActionPreview] Completed for '{targetId}'.");
                onComplete?.Invoke(targetId);
            }
            else
            {
                OseLog.Info($"[ToolActionPreview] Cancelled for '{targetId}'.");
                onCancel?.Invoke();
            }
        }

        // ── Helpers ──

        private void RestoreTargetSphere()
        {
            if (_targetSphereRenderer != null)
            {
                _targetSphereRenderer.enabled = true;
                _targetSphereRenderer = null;
            }
        }

        /// <summary>
        /// Computes where the tool should be positioned and how it should be oriented
        /// during the action phase. The tool approaches from the camera side so it
        /// doesn't clip through geometry on the far side of the target.
        /// </summary>
        private void ComputeWorkingPose(Vector3 surfacePos, float toolHalfLength, string profile,
            out Vector3 workingPos, out Quaternion actionRot)
        {
            // Direction from surface to camera — the tool will approach from this side
            Camera cam = Camera.main;
            Vector3 camToSurface = (surfacePos - (cam != null ? cam.transform.position : _startPos)).normalized;

            // The tool tip should point along camToSurface (toward the surface, from the camera side).
            // We offset the tool center AWAY from the surface along this axis.
            Vector3 approachDir = -camToSurface; // from surface toward camera
            float workingDist = GetProfileWorkingDistance(profile);
            workingPos = surfacePos + approachDir * (toolHalfLength + workingDist);

            // The tool tip (local -Y after upright correction) should point toward the surface.
            // _startRot already includes the model's upright correction via ToolCursorManager.
            // When held at cursor, tip is at the bottom (local down), handle at top (local up).
            Vector3 currentTip = _startRot * Vector3.down;
            Vector3 desiredShaft = -approachDir; // toward surface
            Quaternion shaftCorrection = Quaternion.FromToRotation(currentTip, desiredShaft);
            actionRot = shaftCorrection * _startRot;

            // Apply small profile-specific tilt for realism
            float tiltDegrees = GetProfileTilt(profile);
            if (Mathf.Abs(tiltDegrees) > 0.01f)
            {
                // Tilt around a perpendicular axis (world right relative to approach direction)
                Vector3 tiltAxis = Vector3.Cross(desiredShaft, Vector3.up).normalized;
                if (tiltAxis.sqrMagnitude < 0.01f)
                    tiltAxis = Vector3.Cross(desiredShaft, Vector3.right).normalized;
                actionRot = Quaternion.AngleAxis(tiltDegrees, tiltAxis) * actionRot;
            }
        }

        /// <summary>Profile-specific tilt in degrees from the approach axis.</summary>
        private static float GetProfileTilt(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return 0f;

            if (profile.Equals(ToolActionProfiles.Weld, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("solder", StringComparison.OrdinalIgnoreCase))
                return 12f; // slight tilt for torch visibility

            if (profile.Equals(ToolActionProfiles.Cut, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("grind", StringComparison.OrdinalIgnoreCase))
                return 25f; // grinder angled into cut

            return 0f;
        }

        /// <summary>Gap between tool tip and surface. Smaller = tip closer to work.</summary>
        private static float GetProfileWorkingDistance(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return 0.03f;

            if (profile.Equals(ToolActionProfiles.Weld, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("solder", StringComparison.OrdinalIgnoreCase))
                return 0.005f; // MIG tip nearly touches workpiece (~5mm standoff)

            if (profile.Equals(ToolActionProfiles.Cut, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("grind", StringComparison.OrdinalIgnoreCase))
                return 0.008f;

            return 0.03f;
        }

        private static Renderer FindTargetSphereRenderer(Vector3 worldPos)
        {
            var colliders = Physics.OverlapSphere(worldPos, 0.5f);
            Renderer best = null;
            float bestDist = float.MaxValue;
            foreach (var col in colliders)
            {
                if (col is not SphereCollider) continue;
                float dist = Vector3.Distance(col.transform.position, worldPos);
                if (dist < bestDist)
                {
                    var r = col.GetComponent<Renderer>();
                    if (r != null)
                    {
                        bestDist = dist;
                        best = r;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Estimates the distance from the tool pivot to the tip by projecting
        /// the combined renderer bounds onto the tip direction vector.
        /// This accounts for asymmetric meshes and off-center pivots.
        /// </summary>
        private float EstimateTipDistance(GameObject toolGhost)
        {
            var renderers = toolGhost.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0.06f;

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            // Tip direction in world space (local -Y for tools corrected by ToolCursorManager)
            Vector3 tipDir = _startRot * Vector3.down;
            Vector3 pivot = toolGhost.transform.position;

            // Project pivot-to-bounds-center onto tip direction
            float centerDot = Vector3.Dot(combined.center - pivot, tipDir);

            // AABB support: max extent projection along tip direction
            Vector3 ext = combined.extents;
            float extentProjection = Mathf.Abs(ext.x * tipDir.x)
                                   + Mathf.Abs(ext.y * tipDir.y)
                                   + Mathf.Abs(ext.z * tipDir.z);

            float tipDist = centerDot + extentProjection;
            return Mathf.Max(tipDist, 0.01f);
        }

        private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        private static float EaseInQuad(float t) => t * t;
    }
}
