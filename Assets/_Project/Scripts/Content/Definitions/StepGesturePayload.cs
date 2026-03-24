using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step gesture authoring payload. Controls how tool-use interactions
    /// require physical gesture input (rotation, pull, hold, trace, strike).
    /// When absent, the gesture type is resolved from the step's profile via GestureProfileRegistry.
    /// </summary>
    [Serializable]
    public sealed class StepGesturePayload
    {
        /// <summary>
        /// Explicit gesture type override.
        /// Accepted values: "Tap", "RotaryTorque", "LinearPull", "SteadyHold", "PathTrace", "ImpactStrike".
        /// When null/empty, resolved from profile defaults.
        /// </summary>
        public string gestureType;

        /// <summary>Target rotation angle in degrees for RotaryTorque gestures. Default: profile default (90).</summary>
        public float targetAngleDegrees;

        /// <summary>Target pull distance in world units for LinearPull gestures. Default: profile default (0.3).</summary>
        public float targetPullDistance;

        /// <summary>Hold duration in seconds for SteadyHold gestures. Default: profile default (2.0).</summary>
        public float holdDurationSeconds;

        /// <summary>Path control point target IDs for PathTrace gestures (ordered waypoints).</summary>
        public string[] pathControlPointIds;

        /// <summary>Whether to show the gesture guide overlay. Default: true for standard, false for realistic.</summary>
        public bool showGestureGuide = true;
    }
}
