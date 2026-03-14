using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Owns the step state machine for the current active step.
    /// Manages state transitions, publishes events, and logs diagnostics.
    /// </summary>
    public sealed class StepController
    {
        private StepDefinition _currentStep;
        private RuntimeStepState _currentState;

        public RuntimeStepState CurrentStepState => _currentState;
        public StepDefinition CurrentStepDefinition => _currentStep;
        public bool HasActiveStep => _currentStep != null && !_currentState.IsTerminal;

        public void ActivateStep(StepDefinition step, float atSeconds)
        {
            if (step == null)
            {
                OseLog.Warn("[StepController] Cannot activate a null step.");
                return;
            }

            // If there's already an active step that isn't terminal, warn
            if (_currentStep != null && !_currentState.IsTerminal)
            {
                OseLog.Warn($"[StepController] Activating step '{step.id}' while '{_currentStep.id}' is still {_currentState.State}. Forcing transition.");
            }

            _currentStep = step;

            // Transition through Available briefly for event consistency
            TransitionTo(StepState.Available, atSeconds);
            TransitionTo(StepState.Active, atSeconds);
        }

        public void CompleteStep(float atSeconds)
        {
            if (!HasActiveStep)
            {
                OseLog.Warn("[StepController] CompleteStep called but no active step exists.");
                return;
            }

            if (_currentState.State != StepState.Active &&
                _currentState.State != StepState.Interacting &&
                _currentState.State != StepState.WaitingForPhysicalConfirmation)
            {
                OseLog.Warn($"[StepController] Cannot complete step '{_currentStep.id}' from state {_currentState.State}.");
                return;
            }

            _currentState = new RuntimeStepState(
                _currentState.StepId,
                _currentState.State,
                _currentState.AttemptCount,
                _currentState.HintsUsed,
                _currentState.ActivatedAtSeconds,
                completedAtSeconds: atSeconds);

            TransitionTo(StepState.Completed, atSeconds);
        }

        public void FailAttempt()
        {
            if (!HasActiveStep || _currentState.State != StepState.Active)
            {
                OseLog.Warn("[StepController] FailAttempt called but step is not active.");
                return;
            }

            _currentState = new RuntimeStepState(
                _currentState.StepId,
                _currentState.State,
                attemptCount: _currentState.AttemptCount + 1,
                _currentState.HintsUsed,
                _currentState.ActivatedAtSeconds,
                _currentState.CompletedAtSeconds);

            TransitionTo(StepState.FailedAttempt, 0f);
            // Auto-return to Active after recording the failure
            TransitionTo(StepState.Active, 0f);
        }

        public void RequestHint()
        {
            if (!HasActiveStep)
            {
                OseLog.Warn("[StepController] RequestHint called but no active step exists.");
                return;
            }

            _currentState = new RuntimeStepState(
                _currentState.StepId,
                _currentState.State,
                _currentState.AttemptCount,
                hintsUsed: _currentState.HintsUsed + 1,
                _currentState.ActivatedAtSeconds,
                _currentState.CompletedAtSeconds);

            OseLog.VerboseInfo($"[StepController] Hint requested for step '{_currentStep.id}'. Total hints: {_currentState.HintsUsed}");
        }

        public void SuspendStep()
        {
            if (!HasActiveStep || _currentState.State != StepState.Active)
            {
                OseLog.Warn("[StepController] SuspendStep called but step is not active.");
                return;
            }

            TransitionTo(StepState.Suspended, 0f);
        }

        public void ResumeStep(float atSeconds)
        {
            if (_currentStep == null || _currentState.State != StepState.Suspended)
            {
                OseLog.Warn("[StepController] ResumeStep called but step is not suspended.");
                return;
            }

            TransitionTo(StepState.Active, atSeconds);
        }

        public void Reset()
        {
            _currentStep = null;
            _currentState = default;
        }

        private void TransitionTo(StepState newState, float atSeconds)
        {
            var previous = _currentState.State;
            var stepId = _currentStep.id;

            float activatedAt = _currentState.ActivatedAtSeconds;
            if (newState == StepState.Active && previous != StepState.Active)
                activatedAt = atSeconds;

            _currentState = new RuntimeStepState(
                stepId,
                newState,
                _currentState.AttemptCount,
                _currentState.HintsUsed,
                activatedAt,
                _currentState.CompletedAtSeconds);

            OseLog.StepEvent(stepId, newState);

            RuntimeEventBus.Publish(new StepStateChanged(stepId, previous, newState, atSeconds));
        }
    }
}
