using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Describes the camera's orbital position around a pivot point.
    /// Position is computed as: pivot + SphericalToCartesian(Yaw, Pitch, Distance).
    /// All angles in degrees.
    /// </summary>
    public struct CameraState
    {
        public Vector3 PivotPosition;
        public float Yaw;       // Horizontal angle (degrees, 0 = forward along +Z)
        public float Pitch;     // Vertical angle (degrees, 0 = horizon, 90 = top-down)
        public float Distance;  // Distance from pivot

        /// <summary>
        /// Compute world position from orbital parameters.
        /// </summary>
        public Vector3 ComputePosition()
        {
            float yawRad = Yaw * Mathf.Deg2Rad;
            float pitchRad = Pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);

            var offset = new Vector3(
                cosPitch * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                cosPitch * Mathf.Cos(yawRad)
            );

            return PivotPosition + offset * Distance;
        }

        /// <summary>
        /// Compute the rotation that looks from the computed position toward the pivot.
        /// </summary>
        public Quaternion ComputeRotation()
        {
            Vector3 pos = ComputePosition();
            Vector3 dir = PivotPosition - pos;
            if (dir.sqrMagnitude < 0.0001f)
                return Quaternion.identity;
            return Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>
        /// Derive orbital parameters from an existing camera transform and pivot position.
        /// Used to initialize the rig from the current camera without a visual jump.
        /// </summary>
        public static CameraState FromTransform(Transform cameraTransform, Vector3 pivot)
        {
            Vector3 offset = cameraTransform.position - pivot;
            float distance = offset.magnitude;

            if (distance < 0.001f)
                return new CameraState { PivotPosition = pivot, Yaw = 0, Pitch = 30, Distance = 3f };

            Vector3 dir = offset / distance;
            float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            return new CameraState
            {
                PivotPosition = pivot,
                Yaw = yaw,
                Pitch = pitch,
                Distance = distance
            };
        }
    }
}
