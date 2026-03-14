namespace OSE.Core
{
    public readonly struct RuntimeStepState
    {
        public readonly string StepId;
        public readonly StepState State;
        public readonly int AttemptCount;
        public readonly int HintsUsed;
        public readonly float ActivatedAtSeconds;
        public readonly float CompletedAtSeconds;

        public RuntimeStepState(
            string stepId,
            StepState state,
            int attemptCount = 0,
            int hintsUsed = 0,
            float activatedAtSeconds = 0f,
            float completedAtSeconds = 0f)
        {
            StepId = stepId;
            State = state;
            AttemptCount = attemptCount;
            HintsUsed = hintsUsed;
            ActivatedAtSeconds = activatedAtSeconds;
            CompletedAtSeconds = completedAtSeconds;
        }

        public bool IsTerminal =>
            State == StepState.Completed ||
            State == StepState.Skipped;
    }
}
