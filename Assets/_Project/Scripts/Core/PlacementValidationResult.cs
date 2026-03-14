namespace OSE.Core
{
    public readonly struct PlacementValidationResult
    {
        public readonly bool IsValid;
        public readonly ValidationFailureReason FailureReason;
        public readonly float PositionError;
        public readonly float RotationError;
        public readonly string MissingPrerequisiteId;
        public readonly string SuggestedHintKey;
        public readonly ValidationSeverity Severity;
        public readonly bool AutoSnapAllowed;
        public readonly bool ReattemptPenaltyApplies;

        public static PlacementValidationResult Valid(bool autoSnapAllowed = false) =>
            new PlacementValidationResult(
                isValid: true,
                failureReason: ValidationFailureReason.None,
                autoSnapAllowed: autoSnapAllowed);

        public static PlacementValidationResult Invalid(
            ValidationFailureReason reason,
            float positionError = 0f,
            float rotationError = 0f,
            string missingPrerequisiteId = null,
            string suggestedHintKey = null,
            ValidationSeverity severity = ValidationSeverity.Standard,
            bool reattemptPenaltyApplies = false) =>
            new PlacementValidationResult(
                isValid: false,
                failureReason: reason,
                positionError: positionError,
                rotationError: rotationError,
                missingPrerequisiteId: missingPrerequisiteId,
                suggestedHintKey: suggestedHintKey,
                severity: severity,
                autoSnapAllowed: false,
                reattemptPenaltyApplies: reattemptPenaltyApplies);

        private PlacementValidationResult(
            bool isValid,
            ValidationFailureReason failureReason,
            float positionError = 0f,
            float rotationError = 0f,
            string missingPrerequisiteId = null,
            string suggestedHintKey = null,
            ValidationSeverity severity = ValidationSeverity.Standard,
            bool autoSnapAllowed = false,
            bool reattemptPenaltyApplies = false)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            PositionError = positionError;
            RotationError = rotationError;
            MissingPrerequisiteId = missingPrerequisiteId;
            SuggestedHintKey = suggestedHintKey;
            Severity = severity;
            AutoSnapAllowed = autoSnapAllowed;
            ReattemptPenaltyApplies = reattemptPenaltyApplies;
        }
    }

    public enum ValidationFailureReason
    {
        None,
        WrongPart,
        WrongTarget,
        PositionOutOfTolerance,
        RotationOutOfTolerance,
        MissingPrerequisite,
        PhysicalSubstitutionNotAllowed,
        ChallengeConstraintViolated,
        SequenceViolation,
        ProcessNotComplete
    }

    public enum ValidationSeverity
    {
        Info,
        Standard,
        Critical
    }
}
