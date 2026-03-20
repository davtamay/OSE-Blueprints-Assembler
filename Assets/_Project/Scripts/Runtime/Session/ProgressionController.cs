using System.Collections.Generic;
using OSE.App;
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
        private int _furthestReachedIndex;
        private readonly List<RuntimeStepState> _completedHistory = new List<RuntimeStepState>();

        public int CurrentStepIndex => _currentIndex;
        public int TotalSteps => _orderedSteps?.Length ?? 0;
        public bool HasNextStep => _orderedSteps != null && _currentIndex < _orderedSteps.Length - 1;
        public bool IsComplete => _orderedSteps != null && _currentIndex >= _orderedSteps.Length;
        public IReadOnlyList<RuntimeStepState> CompletedStepHistory => _completedHistory;
        public int FurthestReachedIndex => _furthestReachedIndex;
        public bool HasPreviousStep => _orderedSteps != null && _currentIndex > 0;
        public bool CanNavigateForward => _orderedSteps != null && _currentIndex < _orderedSteps.Length - 1;

        /// <summary>
        /// True when the last call to AdvanceToNextStep moved beyond
        /// the previously furthest-reached index (i.e. first-time completion).
        /// </summary>
        public bool LastAdvanceWasFirstTime { get; private set; }

        public void Initialize(StepDefinition[] orderedSteps)
        {
            _orderedSteps = orderedSteps ?? System.Array.Empty<StepDefinition>();
            _currentIndex = 0;
            _furthestReachedIndex = 0;
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
        /// Returns step definitions for indices [0, count).
        /// Used by restore to get the actual steps that were skipped.
        /// </summary>
        public StepDefinition[] GetStepsUpTo(int count)
        {
            if (_orderedSteps == null || count <= 0)
                return System.Array.Empty<StepDefinition>();

            int clamped = System.Math.Min(count, _orderedSteps.Length);
            var result = new StepDefinition[clamped];
            System.Array.Copy(_orderedSteps, result, clamped);
            return result;
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

            // Watchdog: detect if advance is called during navigation
            if (ServiceRegistry.TryGet<MachineSessionController>(out var navSession) && navSession.IsNavigating)
            {
                OseLog.Warn($"[ProgressionController] CRITICAL: AdvanceToNextStep called during navigation! " +
                    $"currentIndex={_currentIndex}. Stack: {System.Environment.StackTrace}");
            }

            LastAdvanceWasFirstTime = _currentIndex >= _furthestReachedIndex;
            _currentIndex++;

            if (_currentIndex > _furthestReachedIndex)
                _furthestReachedIndex = _currentIndex;

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

        /// <summary>
        /// Advances the cursor to a specific index without firing any events.
        /// Used by session restore to skip already-completed steps.
        /// Returns the step definitions that were skipped (for bulk state setup).
        /// </summary>
        public StepDefinition[] SkipToIndex(int targetIndex)
        {
            if (_orderedSteps == null || targetIndex <= _currentIndex)
                return System.Array.Empty<StepDefinition>();

            int clampedTarget = System.Math.Min(targetIndex, _orderedSteps.Length);
            var skipped = new StepDefinition[clampedTarget - _currentIndex];

            for (int i = _currentIndex; i < clampedTarget; i++)
            {
                skipped[i - _currentIndex] = _orderedSteps[i];
                _completedHistory.Add(new RuntimeStepState(_orderedSteps[i].id, StepState.Completed, 0, 0, 0f, 0f));
            }

            _currentIndex = clampedTarget;
            if (_currentIndex > _furthestReachedIndex)
                _furthestReachedIndex = _currentIndex;
            OseLog.Info($"[ProgressionController] Skipped to step {_currentIndex}/{_orderedSteps.Length} (restored {skipped.Length} completed steps).");
            return skipped;
        }

        public void Reset()
        {
            _orderedSteps = null;
            _currentIndex = 0;
            _furthestReachedIndex = 0;
            _completedHistory.Clear();
        }

        /// <summary>
        /// Returns the step definition at the given index, or null if out of range.
        /// </summary>
        public StepDefinition GetStepAtIndex(int index)
        {
            if (_orderedSteps == null || index < 0 || index >= _orderedSteps.Length)
                return null;
            return _orderedSteps[index];
        }

        /// <summary>
        /// Moves the cursor to the specified index without firing events.
        /// Used by step navigation to reposition the cursor.
        /// </summary>
        public void SetCurrentIndex(int index)
        {
            if (_orderedSteps == null) return;
            _currentIndex = System.Math.Max(0, System.Math.Min(index, _orderedSteps.Length));
        }
    }
}
