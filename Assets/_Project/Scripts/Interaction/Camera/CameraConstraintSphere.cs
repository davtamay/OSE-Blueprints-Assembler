using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Clamps a CameraState to valid orbital bounds (distance, pitch).
    /// The camera is conceptually on a constrained sphere around the pivot.
    /// </summary>
    public sealed class CameraConstraintSphere
    {
        public float MinDistance { get; set; } = 0.3f;
        public float MaxDistance { get; set; } = 10f;
        public float MinPitch { get; set; } = -10f;
        public float MaxPitch { get; set; } = 85f;

        public CameraConstraintSphere() { }

        public CameraConstraintSphere(InteractionSettings settings)
        {
            MinDistance = settings.MinCameraDistance;
            MaxDistance = settings.MaxCameraDistance;
            MinPitch = settings.MinPitch;
            MaxPitch = settings.MaxPitch;
        }

        public CameraState Clamp(CameraState state)
        {
            state.Distance = Mathf.Clamp(state.Distance, MinDistance, MaxDistance);
            state.Pitch = Mathf.Clamp(state.Pitch, MinPitch, MaxPitch);
            // Yaw wraps naturally, no clamping needed
            return state;
        }
    }
}
