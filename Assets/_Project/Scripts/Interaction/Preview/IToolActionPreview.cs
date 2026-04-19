using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    public enum PreviewMode
    {
        /// <summary>Auto-play animation — user observes ("I Do").</summary>
        Observe,

        /// <summary>User-driven with preview overlay and guided drag ("We Do").</summary>
        Guided
    }

    public struct PreviewContext
    {
        public GameObject ToolPreview;
        public Vector3 TargetWorldPos;
        public Vector3 ToolStartWorldPos;
        public Quaternion ToolStartRotation;
        public string Profile;
        public PreviewMode Mode;
        public InteractionMode Platform;
        public ToolPoseConfig ToolPose;
        /// <summary>Direction of the weld/cut line in world space (normalized). Zero = point target.</summary>
        public Vector3 WeldAxis;
        /// <summary>Length of the weld/cut line in scene units. Zero = use default.</summary>
        public float WeldLength;

        /// <summary>
        /// Projects a world-space direction into screen space, returning a normalized 2D vector.
        /// Falls back to <paramref name="fallback"/> if projection fails or direction is zero.
        /// </summary>
        public Vector2 ProjectDirectionToScreen(Vector3 worldDir, Vector2 fallback)
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null || worldDir.sqrMagnitude < 0.001f)
                return fallback;

            Vector3 screenA = cam.WorldToScreenPoint(TargetWorldPos);
            Vector3 screenB = cam.WorldToScreenPoint(TargetWorldPos + worldDir);
            Vector2 screenDir = new Vector2(screenB.x - screenA.x, screenB.y - screenA.y);
            return screenDir.sqrMagnitude > 0.001f ? screenDir.normalized : fallback;
        }
    }

    /// <summary>
    /// Per-profile tool action preview animation.
    /// Each implementation handles both Observe (auto-play) and Guided (user-driven) modes.
    /// </summary>
    public interface IToolActionPreview
    {
        /// <summary>Total duration of the action phase in seconds (excludes approach).</summary>
        float Duration { get; }

        /// <summary>Called once when the action phase begins (after approach completes).</summary>
        void Begin(PreviewContext context);

        /// <summary>Observe mode tick. Returns normalized progress 0..1 based on elapsed time.</summary>
        float TickObserve(float deltaTime);

        /// <summary>Guided mode tick. Returns normalized progress 0..1 based on user input.
        /// <paramref name="screenPos"/> is the current pointer position in screen pixels.</summary>
        float TickGuided(float deltaTime, Vector2 dragDelta, Vector2 screenPos);

        /// <summary>Expected drag direction in screen space for guided mode arrow overlay.</summary>
        Vector2 GetExpectedDragDirection(PreviewContext context);

        /// <summary>
        /// Per-frame positional overlay the preview contributes on top of the
        /// controller's authoritative follow-part math. Returned value is a
        /// world-space displacement (vibration, micro-wobble, shake, etc.)
        /// added to <c>workingPos + partFollowOffset</c> each tick.
        ///
        /// <para><b>Contract:</b> when a <see cref="IPartEffect"/> is active,
        /// the controller owns <c>tool.transform.position</c> and computes
        /// it as <c>workingPos + partFollowOffset + ComputeOverlayOffset</c>.
        /// Previews MUST NOT write <c>transform.position</c> directly in that
        /// regime — anything they write is overwritten by the controller's
        /// composition. Path-driven previews (weld bead travel, square
        /// settle) run with no PartEffect and the controller leaves the
        /// position alone; they can still write position directly in that
        /// case.</para>
        ///
        /// <para>Default zero means "no overlay" and is suitable for most
        /// previews. Override to contribute vibration or similar visual
        /// effects without breaking the follow-part contract.</para>
        /// </summary>
        Vector3 ComputeOverlayOffset(float progress);

        /// <summary>Called on completion or cancellation. Clean up any spawned objects.</summary>
        void End(bool completed);
    }
}
