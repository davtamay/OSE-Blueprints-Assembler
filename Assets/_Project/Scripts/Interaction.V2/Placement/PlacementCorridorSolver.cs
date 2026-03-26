using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Validates that the part is approaching the target from a sensible direction.
    /// The corridor is a cone centered on the target's expected approach direction
    /// (default: local up). Parts approaching from outside the cone are considered
    /// invalid even if position is correct.
    /// </summary>
    public sealed class PlacementCorridorSolver
    {
        public float CorridorHalfAngle { get; set; } = 45f;

        public PlacementCorridorSolver(float corridorHalfAngle = 45f)
        {
            CorridorHalfAngle = corridorHalfAngle;
        }

        /// <summary>
        /// Returns true if the part is approaching the target from within the valid corridor.
        /// </summary>
        /// <param name="partPos">Current part world position</param>
        /// <param name="targetPos">Target world position</param>
        /// <param name="targetRot">Target rotation (corridor direction = local up)</param>
        public bool Evaluate(Vector3 partPos, Vector3 targetPos, Quaternion targetRot)
        {
            Vector3 approachDir = (partPos - targetPos).normalized;
            Vector3 expectedDir = targetRot * Vector3.up;

            float angle = Vector3.Angle(approachDir, expectedDir);
            return angle <= CorridorHalfAngle;
        }

        /// <summary>
        /// Evaluate with a custom approach direction instead of local up.
        /// </summary>
        public bool Evaluate(Vector3 partPos, Vector3 targetPos, Vector3 expectedApproachDir)
        {
            Vector3 approachDir = (partPos - targetPos).normalized;
            float angle = Vector3.Angle(approachDir, expectedApproachDir);
            return angle <= CorridorHalfAngle;
        }
    }
}
