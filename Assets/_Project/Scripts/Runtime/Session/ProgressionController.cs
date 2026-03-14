using System.Collections.Generic;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Owns step ordering, advancement cursor, and completion history
    /// for the current assembly. Pure logic — no Unity dependencies.
    /// </summary>
    public sealed class ProgressionController
    {
        private StepDefinition[] _orderedSteps;
        private int _currentIndex;
        private readonly List<RuntimeStepState> _completedHistory = new List<RuntimeStepState>();

        public int CurrentStepIndex => _currentIndex;
        public int TotalSteps => _orderedSteps?.Length ?? 0;
        public bool HasNextStep => _orderedSteps != null && _currentIndex < _orderedSteps.Length - 1;
        public bool IsComplete => _orderedSteps != null && _currentIndex >= _orderedSteps.Length;
        public IReadOnlyList<RuntimeStepState> CompletedStepHistory => _completedHistory;

        public void Initialize(StepDefinition[] orderedSteps)
        {
            _orderedSteps = orderedSteps ?? System.Array.Empty<StepDefinition>();
            _currentIndex = 0;
            _completedHistory.Clear();

            OseLog.VerboseInfo($"[ProgressionController] Initialized with {_orderedSteps.Length} steps.");
        }

        public StepDefinition GetCurrentStep()
        {
            if (_orderedSteps == null || _currentIndex >= _orderedSteps.Length)
                return null;

            return _orderedSteps[_currentIndex];
        }

        /// <summary>
        /// Advances the cursor and returns the next step, or null if the assembly is complete.
        /// </summary>
        public StepDefinition AdvanceToNextStep()
        {
            if (_orderedSteps == null)
            {
                OseLog.Warn("[ProgressionController] AdvanceToNextStep called before initialization.");
                return null;
            }

            _currentIndex++;

            if (_currentIndex >= _orderedSteps.Length)
            {
                OseLog.VerboseInfo("[ProgressionController] All steps completed. Assembly is done.");
                return null;
            }

            OseLog.VerboseInfo($"[ProgressionController] Advanced to step {_currentIndex + 1}/{_orderedSteps.Length}: '{_orderedSteps[_currentIndex].id}'");
            return _orderedSteps[_currentIndex];
        }

        public void RecordStepCompletion(RuntimeStepState state)
        {
            _completedHistory.Add(state);
            OseLog.VerboseInfo($"[ProgressionController] Recorded completion for '{state.StepId}'. History: {_completedHistory.Count}/{TotalSteps}");
        }

        public void Reset()
        {
            _orderedSteps = null;
            _currentIndex = 0;
            _completedHistory.Clear();
        }
    }
}
