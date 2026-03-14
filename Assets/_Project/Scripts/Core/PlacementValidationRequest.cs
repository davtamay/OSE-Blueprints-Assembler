using UnityEngine;

namespace OSE.Core
{
    public readonly struct PlacementValidationRequest
    {
        public readonly string ExpectedPartId;
        public readonly string ExpectedTargetAnchorId;
        public readonly Vector3 CandidatePosition;
        public readonly Quaternion CandidateRotation;
        public readonly Vector3 ExpectedPosition;
        public readonly Quaternion ExpectedRotation;
        public readonly float PositionTolerance;
        public readonly float RotationToleranceDegrees;
        public readonly string[] RequiredPrerequisiteStepIds;
        public readonly bool PhysicalSubstitutionAllowed;
        public readonly ValidationMode ValidationMode;

        public PlacementValidationRequest(
            string expectedPartId,
            string expectedTargetAnchorId,
            Vector3 candidatePosition,
            Quaternion candidateRotation,
            Vector3 expectedPosition,
            Quaternion expectedRotation,
            float positionTolerance,
            float rotationToleranceDegrees,
            string[] requiredPrerequisiteStepIds,
            bool physicalSubstitutionAllowed,
            ValidationMode validationMode)
        {
            ExpectedPartId = expectedPartId;
            ExpectedTargetAnchorId = expectedTargetAnchorId;
            CandidatePosition = candidatePosition;
            CandidateRotation = candidateRotation;
            ExpectedPosition = expectedPosition;
            ExpectedRotation = expectedRotation;
            PositionTolerance = positionTolerance;
            RotationToleranceDegrees = rotationToleranceDegrees;
            RequiredPrerequisiteStepIds = requiredPrerequisiteStepIds;
            PhysicalSubstitutionAllowed = physicalSubstitutionAllowed;
            ValidationMode = validationMode;
        }
    }
}
