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
        private StepDefinition _currentStep;
        private RuntimeStepState _currentState;
        private TaskCursor _currentTaskCursor;

        // Set by the navigation layer (SessionNavigationController) to structurally
        // block step completion while navigation is in progress. This replaces the
        // previous wall-clock cooldown approach which was fragile under frame timing.
        private bool _completionBlocked;

        public RuntimeStepState CurrentStepState => _currentState;
        public StepDefinition CurrentStepDefinition => _currentStep;
        public bool HasActiveStep => _currentStep != null && !_currentState.IsTerminal;

        /// <summary>
        /// Phase I.c — the task sequencer for the currently-active step. Built
        /// fresh on every <see cref="ActivateStep"/>. Callers that need per-span
        /// task availability consult this cursor; subscribers that want to react
        /// to span transitions attach to <see cref="TaskCursor.TaskSpanOpened"/>
        /// from their step-activation handler. <see cref="TaskCursor.Start"/> is
        /// called at the end of <see cref="ActivateStep"/> (after the Active
        /// <see cref="StepStateChanged"/> is published) so event-bus subscribers
        /// have a chance to attach before the initial span fires.
        ///
        /// <para>Null when no step is active (e.g. after <see cref="Reset"/>).</para>
        /// </summary>
        public TaskCursor CurrentTaskCursor => _currentTaskCursor;

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
            // Defensive detach: if a previous cursor still holds our handler
            // (e.g. ActivateStep on top of a non-terminal step), drop the
            // subscription before replacing the field so the old cursor can
            // be GC'd without risk of firing StepTasksComplete into this
            // controller after the step has rolled over.
            if (_currentTaskCursor != null)
                _currentTaskCursor.StepTasksComplete -= HandleCursorStepComplete;
            _currentTaskCursor = new TaskCursor(step);
            // Phase I.e — cursor drives step completion when the step has a
            // non-empty taskOrder. Subscribing here (before TransitionTo)
            // means the handler captures the correct cursor instance even if
            // CompleteStep re-enters transition logic. The handler itself is
            // a closure around session.GetElapsedSeconds so it stays in sync
            // with whenever the cursor's final span actually closes.
            _currentTaskCursor.StepTasksComplete += HandleCursorStepComplete;

            // Transition through Available briefly for event consistency
            TransitionTo(StepState.Available, atSeconds);
            TransitionTo(StepState.Active, atSeconds);

            // Fire the cursor's initial TaskSpanOpened AFTER the Active state
            // change has been published, so handlers that subscribed to
            // StepStateChanged have a chance to attach to the cursor's event
            // before the initial span fires.
            _currentTaskCursor.Start();
        }

        private void HandleCursorStepComplete()
        {
            if (!HasActiveStep) return;

            float atSeconds = 0f;
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                atSeconds = session.GetElapsedSeconds();

            OseLog.Info($"[StepController] Cursor reports all tasks complete for step '{_currentStep?.id}' — completing step.");
            CompleteStep(atSeconds);
        }

        /// <summary>
        /// Called by the navigation layer to structurally block or unblock step completion.
        /// Block before navigation starts; unblock in the finally clause when it ends.
        /// This prevents a same-frame input (e.g. back-click) from re-completing the
        /// step that was just navigated away from (UIToolkit frame-ordering race).
        /// </summary>
        public void SetCompletionBlocked(bool blocked)
        {
            _completionBlocked = blocked;
            if (blocked)
                OseLog.VerboseInfo($"[StepController] Completion blocked for step '{_currentStep?.id}'.");
            else
                OseLog.VerboseInfo($"[StepController] Completion unblocked for step '{_currentStep?.id}'.");
        }

        public void CompleteStep(float atSeconds)
        {
            // Structurally blocked by the navigation layer — a navigation is either
            // in progress or just completed this frame.
            if (_completionBlocked)
            {
                OseLog.Warn(OseErrorCode.StepCompletionBlocked,
                    $"[StepController] CompleteStep blocked (navigation active) for step '{_currentStep?.id}'.");
                return;
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
            if (_currentTaskCursor != null)
                _currentTaskCursor.StepTasksComplete -= HandleCursorStepComplete;
            _currentTaskCursor = null;
            _completionBlocked = false;
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
