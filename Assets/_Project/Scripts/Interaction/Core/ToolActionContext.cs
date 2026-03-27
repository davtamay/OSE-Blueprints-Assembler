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

        // ── Tool spatial metadata (grip, tip, action axis) ──
        public ToolPoseConfig ToolPose;
    }
}
