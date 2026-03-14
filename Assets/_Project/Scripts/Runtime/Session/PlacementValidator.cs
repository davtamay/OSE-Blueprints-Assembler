using OSE.Core;
using UnityEngine;

namespace OSE.Runtime
{
    /// <summary>
    /// Validates part placement against target positions using tolerance values
    /// from the content package's ValidationRuleDefinition data.
    /// Plain C# — implements IPlacementValidator.
    /// </summary>
    public sealed class PlacementValidator : IPlacementValidator
    {
        public PlacementValidationResult Validate(PlacementValidationRequest request)
        {
            float positionError = Vector3.Distance(request.CandidatePosition, request.ExpectedPosition);
            float rotationError = Quaternion.Angle(request.CandidateRotation, request.ExpectedRotation);

            // Convert mm tolerance to Unity units (1 unit = 1 meter, tolerance is in mm)
            float positionToleranceUnits = request.PositionTolerance / 1000f;

            if (positionError > positionToleranceUnits)
            {
                return PlacementValidationResult.Invalid(
                    ValidationFailureReason.PositionOutOfTolerance,
                    positionError: positionError,
                    rotationError: rotationError,
                    severity: ValidationSeverity.Standard);
            }

            if (rotationError > request.RotationToleranceDegrees)
            {
                return PlacementValidationResult.Invalid(
                    ValidationFailureReason.RotationOutOfTolerance,
                    positionError: positionError,
                    rotationError: rotationError,
                    severity: ValidationSeverity.Standard);
            }

            bool autoSnap = positionError > 0f || rotationError > 0f;
            return PlacementValidationResult.Valid(autoSnapAllowed: autoSnap);
        }

        /// <summary>
        /// Convenience: validates a placement using exact target position (auto-valid).
        /// Used when the user places via context menu or confirmation mode.
        /// </summary>
        public static PlacementValidationResult ValidateExact()
        {
            return PlacementValidationResult.Valid(autoSnapAllowed: true);
        }
    }
}
