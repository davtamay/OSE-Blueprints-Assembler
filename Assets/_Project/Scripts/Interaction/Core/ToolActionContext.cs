using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Bundles all target-level action data resolved at the bridge layer.
    /// Flows as a single value through IPartActionBridge → InteractionOrchestrator
    /// → ToolActionPreviewController, replacing individual parameter threading.
    /// </summary>
    public struct ToolActionContext
    {
        public string TargetId;
        public Vector3 TargetWorldPos;
        public Vector3 SurfaceWorldPos;
        public Quaternion TargetWorldRotation;

        // ── Weld / linear action ──
        public Vector3 WeldAxis;
        public float WeldLength;

        // ── Authored tool orientation ──
        public bool HasToolActionRotation;
        public Quaternion ToolActionRotation;

        // ── Placement behaviour ──
        /// <summary>
        /// When true the approach animation ends with instant placement — no action
        /// phase plays. Used for persistent tools (clamps, fixtures) that should snap
        /// into the authored position/rotation as soon as the tool arrives.
        /// </summary>
        public bool InstantPlacement;

        // ── Assembly scale ──
        /// <summary>
        /// Current assembly UI scale multiplier. The preview controller lerps tool
        /// scale from its cursor size to cursor × assemblyScale during approach so
        /// the tool matches the parts at the target without being oversized on the cursor.
        /// </summary>
        public float AssemblyScale;

        // ── Tool spatial metadata (grip, tip, action axis) ──
        public ToolPoseConfig ToolPose;
    }
}
