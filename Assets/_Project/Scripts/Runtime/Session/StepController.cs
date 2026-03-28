using System.Diagnostics;
using OSE.App;
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
        // 50 ms covers ~3 frames at 60 fps — enough for UIToolkit to settle —
        // while being imperceptibly short to the user.
        // Prevents a back-click input processed on the same Update tick as navigation
        // from re-completing the step (UIToolkit frame-ordering race).
        private const float NavigationCooldownSeconds = 0.05f;

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
                OseLog.Warn(OseErrorCode.StepActivationOverride,
                    $"[StepController] Activating step '{step.id}' while '{_currentStep.id}' is still {_currentState.State}. Forcing transition.");
            }

            _currentStep = step;

            // Transition through Available briefly for event consistency
            TransitionTo(StepState.Available, atSeconds);
            TransitionTo(StepState.Active, atSeconds);
        }

        public void CompleteStep(float atSeconds)
        {
            // Failsafe: never complete a step during explicit navigation.
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var navSession))
            {
                if (navSession.IsNavigating)
                {
                    OseLog.Warn(OseErrorCode.StepCompletionBlocked,
                        $"[StepController] CompleteStep blocked during active navigation for step '{_currentStep?.id}'. " +
                        $"Caller stack: {System.Environment.StackTrace}");
                    return;
                }

                // Block completion within a short window after navigation.
                // Unity runs Update() (InteractionOrchestrator) BEFORE UpdatePanels()
                // (UIToolkit), so a back-click input processed on the same Update tick
                // as navigation would otherwise re-complete the step.
                float timeSinceNav = UnityEngine.Time.realtimeSinceStartup - navSession.LastNavigationTime;
                if (navSession.LastNavigationTime >= 0f && timeSinceNav < NavigationCooldownSeconds)
                {
                    OseLog.Warn(OseErrorCode.StepCompletionBlocked,
                        $"[StepController] CompleteStep blocked within navigation cooldown " +
                        $"({timeSinceNav * 1000f:F1}ms since nav, cooldown={NavigationCooldownSeconds * 1000f:F0}ms) " +
                        $"for step '{_currentStep?.id}'.");
                    return;
                }
            }

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
            RuntimeEventBus.Publish(new HintRequested(_currentStep.id, _currentState.HintsUsed));
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

            AssertValidTransition(stepId, previous, newState);
            OseLog.StepEvent(stepId, newState);

            RuntimeEventBus.Publish(new StepStateChanged(stepId, previous, newState, atSeconds));
        }

        // ── FSM post-validator ──────────────────────────────────────────────
        // Strips to nothing in non-development builds (UNITY_ASSERTIONS is
        // defined in Editor and Development Build configurations only).

        [Conditional("UNITY_ASSERTIONS")]
        private static void AssertValidTransition(string stepId, StepState from, StepState to)
        {
            if (!IsValidTransition(from, to))
                OseLog.Error(OseErrorCode.StepFsmInvalidTransition,
                    $"[StepController] Invalid FSM transition '{from}' → '{to}' " +
                    $"for step '{stepId}'. Check the call stack.");
        }

        // Encodes the complete valid transition graph for StepState.
        // Any (from, to) pair not listed here is a bug.
        private static bool IsValidTransition(StepState from, StepState to) => (from, to) switch
        {
            // ActivateStep: any prior state resets through Available
            (_, StepState.Available)                                        => true,
            // ActivateStep: Available fires immediately into Active
            (StepState.Available, StepState.Active)                         => true,
            // CompleteStep: Active and its reserved interrupt states
            (StepState.Active, StepState.Completed)                         => true,
            (StepState.Interacting, StepState.Completed)                    => true,
            (StepState.WaitingForPhysicalConfirmation, StepState.Completed) => true,
            // FailAttempt: Active → FailedAttempt → Active (auto-return pair)
            (StepState.Active, StepState.FailedAttempt)                     => true,
            (StepState.FailedAttempt, StepState.Active)                     => true,
            // SuspendStep / ResumeStep
            (StepState.Active, StepState.Suspended)                         => true,
            (StepState.Suspended, StepState.Active)                         => true,
            _                                                               => false
        };
    }
}
