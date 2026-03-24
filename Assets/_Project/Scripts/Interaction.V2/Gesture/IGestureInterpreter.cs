using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Platform-specific interpreter that converts raw input (mouse, touch, XR controller)
    /// into normalized gesture progress for a given <see cref="GestureType"/>.
    /// </summary>
    public interface IGestureInterpreter
    {
        /// <summary>Begin tracking a gesture with the given context.</summary>
        void Begin(GestureContext context);

        /// <summary>Process one frame of input. Returns the current gesture state.</summary>
        GestureFrame Tick(float deltaTime);

        /// <summary>Abort or clean up the current gesture.</summary>
        void End();

        /// <summary>Whether this interpreter can handle the given gesture type.</summary>
        bool Supports(GestureType gestureType);
    }

    /// <summary>
    /// Immutable context passed to an <see cref="IGestureInterpreter"/> when a gesture begins.
    /// </summary>
    public readonly struct GestureContext
    {
        /// <summary>The gesture type to interpret.</summary>
        public readonly GestureType GestureType;

        /// <summary>World-space position of the target being acted upon.</summary>
        public readonly Vector3 TargetWorldPosition;

        /// <summary>Screen-space position of the target at gesture start.</summary>
        public readonly Vector2 TargetScreenPosition;

        /// <summary>Resolved configuration for thresholds and targets.</summary>
        public readonly GestureConfig Config;

        public GestureContext(GestureType gestureType, Vector3 targetWorldPosition,
            Vector2 targetScreenPosition, GestureConfig config)
        {
            GestureType = gestureType;
            TargetWorldPosition = targetWorldPosition;
            TargetScreenPosition = targetScreenPosition;
            Config = config;
        }
    }

    /// <summary>
    /// Per-frame output from an <see cref="IGestureInterpreter"/>.
    /// </summary>
    public struct GestureFrame
    {
        /// <summary>Normalized progress 0..1.</summary>
        public float Progress;
        /// <summary>True when the gesture has been completed successfully.</summary>
        public bool IsComplete;
        /// <summary>True when the gesture was cancelled (e.g. pointer left target area).</summary>
        public bool WasCancelled;
        /// <summary>Raw gesture value — degrees for torque, mm for pull, seconds for hold.</summary>
        public float GestureValue;
    }

    /// <summary>
    /// Resolved gesture configuration combining step authoring, profile defaults, and difficulty mode.
    /// </summary>
    public struct GestureConfig
    {
        /// <summary>Target angle in degrees (RotaryTorque). Default 90.</summary>
        public float TargetAngleDegrees;
        /// <summary>Target pull distance in world units (LinearPull). Default 0.3.</summary>
        public float TargetPullDistance;
        /// <summary>Hold duration in seconds (SteadyHold). Default 2.0.</summary>
        public float HoldDurationSeconds;
        /// <summary>Path tolerance radius in screen pixels (PathTrace). Default 30.</summary>
        public float PathTolerancePixels;
        /// <summary>Required flick speed in screen pixels/sec (ImpactStrike). Default 800.</summary>
        public float StrikeSpeedThreshold;
        /// <summary>Whether to show the gesture guide overlay.</summary>
        public bool ShowGuide;
        /// <summary>Seconds before auto-assist kicks in (standard mode). 0 = disabled.</summary>
        public float AutoAssistDelay;
        /// <summary>The gesture difficulty mode: "easy", "standard", "realistic".</summary>
        public string GestureMode;

        public static GestureConfig Default => new GestureConfig
        {
            TargetAngleDegrees = 90f,
            TargetPullDistance = 0.3f,
            HoldDurationSeconds = 2f,
            PathTolerancePixels = 30f,
            StrikeSpeedThreshold = 800f,
            ShowGuide = true,
            AutoAssistDelay = 5f,
            GestureMode = "standard"
        };
    }
}
