using System;
using UnityEngine;

namespace OSE.Content
{
    /// <summary>
    /// Spatial metadata for a tool model: where to grip it and where the business end is.
    /// Authored in machine.json per tool (auto-detected by analyze_tool_pose.py,
    /// refined via the Grab Pose Editor).
    ///
    /// All positions are in the GLB model's local space.
    /// The tip direction (for approach alignment) is derived at runtime as
    /// <c>(tipPoint - gripPoint).normalized</c> — no separate axis field needed.
    /// </summary>
    [Serializable]
    public sealed class ToolPoseConfig
    {
        /// <summary>Local offset from mesh origin to center of hand grip.</summary>
        public SceneFloat3 gripPoint;

        /// <summary>Euler correction from mesh-local to "held naturally" orientation.
        /// Replaces the legacy <c>orientationEuler</c> field on ToolDefinition.</summary>
        public SceneFloat3 gripRotation;

        /// <summary>Local offset from mesh origin to the business end
        /// (nozzle tip, socket center, blade edge, etc.).</summary>
        public SceneFloat3 tipPoint;

        /// <summary>Additional offset for the desktop/mobile cursor position
        /// relative to the grip point. Defaults to (0,0,0) = cursor at grip.
        /// Positive Y moves cursor above grip, etc.</summary>
        public SceneFloat3 cursorOffset;

        /// <summary>Euler rotation for the desktop/mobile cursor preview.
        /// When authored (non-zero), this overrides both <c>orientationEuler</c>
        /// and the auto-detect fallback. (0,0,0) = model's native orientation.</summary>
        public SceneFloat3 cursorRotation;

        /// <summary>"right", "left", or "either" — preferred hand for XR grab.</summary>
        public string handedness;

        /// <summary>"power_grip", "pinch", "precision", or "two_hand" — drives XR hand animation.</summary>
        public string poseHint;

        // ── Legacy fields (kept for backward-compatible deserialization) ──

        /// <summary>Explicit tip direction in model-local space. When authored, this is
        /// preferred over the derived <c>tipPoint - gripPoint</c> direction. This keeps
        /// the approach direction stable when the grip changes (e.g., tool rotation).</summary>
        public SceneFloat3 tipAxis;

        /// <summary>
        /// Local axis used for torque/insertion motion when the business-end axis is not
        /// the same as grip-to-tip (for example an Allen key's short inserted leg).
        /// Retained for backward-compatible deserialization and now consumed when authored.
        /// </summary>
        public SceneFloat3 actionAxis;

        // ── Presence checks (zero = not authored) ──

        public bool HasGripPoint =>
            gripPoint.x != 0f || gripPoint.y != 0f || gripPoint.z != 0f;

        public bool HasGripRotation =>
            gripRotation.x != 0f || gripRotation.y != 0f || gripRotation.z != 0f;

        public bool HasTipPoint =>
            tipPoint.x != 0f || tipPoint.y != 0f || tipPoint.z != 0f;

        public bool HasCursorOffset =>
            cursorOffset.x != 0f || cursorOffset.y != 0f || cursorOffset.z != 0f;

        public bool HasCursorRotation =>
            cursorRotation.x != 0f || cursorRotation.y != 0f || cursorRotation.z != 0f;

        public bool HasTipAxis =>
            tipAxis.x != 0f || tipAxis.y != 0f || tipAxis.z != 0f;

        public bool HasActionAxis =>
            actionAxis.x != 0f || actionAxis.y != 0f || actionAxis.z != 0f;

        // ── Conversion helpers ──

        public Vector3 GetGripPoint() => new(gripPoint.x, gripPoint.y, gripPoint.z);
        public Quaternion GetGripRotation() => Quaternion.Euler(gripRotation.x, gripRotation.y, gripRotation.z);
        public Vector3 GetTipPoint() => new(tipPoint.x, tipPoint.y, tipPoint.z);
        public Vector3 GetCursorOffset() => new(cursorOffset.x, cursorOffset.y, cursorOffset.z);
        public Quaternion GetCursorRotation() => Quaternion.Euler(cursorRotation.x, cursorRotation.y, cursorRotation.z);
        public Vector3 GetActionAxis()
        {
            Vector3 axis = new(actionAxis.x, actionAxis.y, actionAxis.z);
            return axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward;
        }

        public Vector3 GetTipAxis()
        {
            Vector3 axis = new(tipAxis.x, tipAxis.y, tipAxis.z);
            return axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.down;
        }

        /// <summary>
        /// Direction for approach alignment. Prefers explicit <c>tipAxis</c> when authored
        /// (stable across grip changes), else falls back to <c>tipPoint - gripPoint</c>.
        /// </summary>
        public Vector3 GetTipDirection()
        {
            if (HasTipAxis) return GetTipAxis();
            if (!HasGripPoint && !HasTipPoint) return Vector3.down;
            Vector3 dir = GetTipPoint() - GetGripPoint();
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.down;
        }
    }
}
