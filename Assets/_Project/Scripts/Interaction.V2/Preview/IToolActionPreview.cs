using UnityEngine;

namespace OSE.Interaction.V2
{
    public enum PreviewMode
    {
        /// <summary>Auto-play animation — user observes ("I Do").</summary>
        Observe,

        /// <summary>User-driven with ghost overlay and guided drag ("We Do").</summary>
        Guided
    }

    public struct PreviewContext
    {
        public GameObject ToolGhost;
        public Vector3 TargetWorldPos;
        public Vector3 ToolStartWorldPos;
        public Quaternion ToolStartRotation;
        public string Profile;
        public PreviewMode Mode;
        public InteractionMode Platform;
        /// <summary>Direction of the weld/cut line in world space (normalized). Zero = point target.</summary>
        public Vector3 WeldAxis;
        /// <summary>Length of the weld/cut line in scene units. Zero = use default.</summary>
        public float WeldLength;
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

        /// <summary>Guided mode tick. Returns normalized progress 0..1 based on user drag input.</summary>
        float TickGuided(float deltaTime, Vector2 dragDelta);

        /// <summary>Expected drag direction in screen space for guided mode arrow overlay.</summary>
        Vector2 GetExpectedDragDirection(PreviewContext context);

        /// <summary>Called on completion or cancellation. Clean up any spawned objects.</summary>
        void End(bool completed);
    }
}
