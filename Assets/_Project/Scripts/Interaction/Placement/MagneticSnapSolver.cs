using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Applies a magnetic pull toward the target position when the part
    /// is within the magnetic radius. Strength follows a quadratic ease-in
    /// curve — gentle at the edge, strong near the target.
    /// </summary>
    public sealed class MagneticSnapSolver
    {
        private float _radiusMultiplier = 2f;

        public MagneticSnapSolver(float radiusMultiplier = 2f)
        {
            _radiusMultiplier = radiusMultiplier;
        }

        // Reference frame rate for snap speed normalization.
        // 0.5 and 0.3 were tuned at 60 Hz; multiply by (deltaTime * 60) so
        // snap speed in units/second stays constant across frame rates.
        private const float ReferenceHz = 60f;

        /// <summary>
        /// Apply magnetic pull to the current result.
        /// </summary>
        /// <param name="current">Current placement state</param>
        /// <param name="targetPos">Where the part should end up</param>
        /// <param name="targetRot">Target rotation</param>
        /// <param name="snapTolerance">Base snap tolerance (magnetic radius = this × multiplier)</param>
        /// <param name="deltaTime">Frame delta time (Time.deltaTime) for frame-rate independent snap speed</param>
        public PlacementAssistResult Apply(
            PlacementAssistResult current,
            Vector3 targetPos,
            Quaternion targetRot,
            float snapTolerance,
            float deltaTime)
        {
            float magneticRadius = snapTolerance * _radiusMultiplier;
            float dist = Vector3.Distance(current.RawPosition, targetPos);

            if (dist > magneticRadius || dist < 0.001f)
                return current;

            // t: 0 at edge of magnetic field, 1 at target center
            float t = 1f - (dist / magneticRadius);
            t *= t; // Quadratic ease-in: gentle at edge, strong near center

            // Normalize per-frame lerp/slerp factors to the 60 Hz reference rate so
            // snap speed is consistent regardless of whether the device runs at 60 or 120 Hz.
            float frameScale = Mathf.Clamp01(deltaTime * ReferenceHz);

            // Pull position toward target (max 50% of remaining distance at 60 Hz)
            current.AssistedPosition = Vector3.Lerp(current.RawPosition, targetPos, t * 0.5f * frameScale);

            // Gradually align rotation (max 30% of remaining angle at 60 Hz)
            current.AssistedRotation = Quaternion.Slerp(current.RawRotation, targetRot, t * 0.3f * frameScale);

            current.MagneticStrength = t;
            current.IsInMagneticField = true;

            return current;
        }
    }
}
