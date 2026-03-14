using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class ChallengeConfigDefinition
    {
        public bool enabled;
        public bool trackTime;
        public bool trackRetries;
        public bool trackHintUsage;
        public bool leaderboardReady;
        public bool strictValidationModeAvailable;
    }

    [Serializable]
    public sealed class StepChallengeFlagsDefinition
    {
        public bool penalizeHintUsage;
        public bool penalizeInvalidPlacement;
        public bool stricterToleranceAvailable;
    }
}
