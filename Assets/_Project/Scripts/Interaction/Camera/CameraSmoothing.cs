using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Interpolates between current and target CameraState for smooth movement.
    /// Each axis can have its own smoothing speed.
    /// </summary>
    public sealed class CameraSmoothing
    {
        public float OrbitSpeed { get; set; } = 8f;
        public float PanSpeed { get; set; } = 8f;
        public float ZoomSpeed { get; set; } = 6f;
        public float PivotSpeed { get; set; } = 4f;

        public CameraSmoothing() { }

        public CameraSmoothing(InteractionSettings settings)
        {
            OrbitSpeed = settings.OrbitSmoothing;
            PanSpeed = settings.PanSmoothing;
            ZoomSpeed = settings.ZoomSmoothing;
            PivotSpeed = settings.PivotSmoothing;
        }

        /// <summary>
        /// Interpolate current toward target. Call from LateUpdate with Time.deltaTime.
        /// </summary>
        public CameraState Step(CameraState current, CameraState target, float dt)
        {
            return new CameraState
            {
                Yaw = Mathf.LerpAngle(current.Yaw, target.Yaw, OrbitSpeed * dt),
                Pitch = Mathf.Lerp(current.Pitch, target.Pitch, OrbitSpeed * dt),
                Distance = Mathf.Lerp(current.Distance, target.Distance, ZoomSpeed * dt),
                PivotPosition = Vector3.Lerp(current.PivotPosition, target.PivotPosition, PivotSpeed * dt)
            };
        }

        /// <summary>
        /// Snap current to target immediately (used for initialization or teleport).
        /// </summary>
        public static CameraState Snap(CameraState target) => target;
    }
}
