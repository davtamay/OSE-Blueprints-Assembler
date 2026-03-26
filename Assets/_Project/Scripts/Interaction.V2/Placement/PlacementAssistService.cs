using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Coordinates placement assist features (magnetic snap, corridors).
    /// Called by the orchestrator each frame while a part is being dragged.
    /// Toggle-gated: disabled features have zero cost.
    /// </summary>
    public sealed class PlacementAssistService
    {
        private readonly InteractionSettings _settings;
        private readonly MagneticSnapSolver _magneticSnap;
        private readonly PlacementCorridorSolver _corridorSolver;

        public PlacementAssistService(InteractionSettings settings)
        {
            _settings = settings;
            _magneticSnap = new MagneticSnapSolver(settings.MagneticRadiusMultiplier);
            _corridorSolver = new PlacementCorridorSolver(settings.CorridorHalfAngle);
        }

        /// <summary>
        /// Evaluate placement assist for the current frame.
        /// Call this every frame while a part is being dragged.
        /// </summary>
        /// <param name="partPosition">Current raw world position of the dragged part</param>
        /// <param name="partRotation">Current rotation of the dragged part</param>
        /// <param name="targetPosition">Target snap position</param>
        /// <param name="targetRotation">Target snap rotation</param>
        /// <param name="snapTolerance">Base snap tolerance from validation rules</param>
        public PlacementAssistResult Evaluate(
            Vector3 partPosition,
            Quaternion partRotation,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float snapTolerance)
        {
            var result = new PlacementAssistResult
            {
                RawPosition = partPosition,
                RawRotation = partRotation,
                AssistedPosition = partPosition,
                AssistedRotation = partRotation,
                MagneticStrength = 0f,
                IsInMagneticField = false,
                IsInCorridor = true // Default to true when corridors disabled
            };

            if (_settings.EnableMagneticPlacement)
            {
                result = _magneticSnap.Apply(result, targetPosition, targetRotation, snapTolerance);
            }

            if (_settings.EnablePlacementCorridors)
            {
                result.IsInCorridor = _corridorSolver.Evaluate(
                    partPosition, targetPosition, targetRotation);
            }

            return result;
        }
    }
}
