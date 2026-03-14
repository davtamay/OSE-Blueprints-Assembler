using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class ValidationRuleDefinition
    {
        public string id;
        public string type;
        public string targetId;
        public string expectedPartId;
        public float positionToleranceMm;
        public float rotationToleranceDeg;
        public string[] requiredStepIds;
        public string[] requiredPartIds;
        public ValidationModeOverrideDefinition modeOverrides;
        public string failureMessage;
        public string correctionHintId;
    }

    [Serializable]
    public sealed class ValidationModeOverrideDefinition
    {
        public float tutorialPositionToleranceMm;
        public float tutorialRotationToleranceDeg;
        public float guidedPositionToleranceMm;
        public float guidedRotationToleranceDeg;
        public float challengePositionToleranceMm;
        public float challengeRotationToleranceDeg;
    }
}
