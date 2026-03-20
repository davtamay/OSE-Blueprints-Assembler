using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step difficulty and challenge tuning payload.
    /// Groups skip rules, challenge flags, time limits, and hint availability.
    /// </summary>
    [Serializable]
    public sealed class StepDifficultyPayload
    {
        public bool allowSkip;
        public StepChallengeFlagsDefinition challengeFlags;
        public float timeLimitSeconds;
        /// <summary>
        /// Hint availability mode: "always" (default/null), "limited", "none".
        /// </summary>
        public string hintAvailability;
    }
}
