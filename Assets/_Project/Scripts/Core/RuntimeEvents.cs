using System;
using System.Collections.Generic;

namespace OSE.Core
{
    /// <summary>
    /// Lightweight generic event bus for decoupled runtime communication.
    /// All runtime systems publish and subscribe through this static hub.
    /// </summary>
    public static class RuntimeEventBus
    {
        private static readonly Dictionary<Type, Delegate> _listeners = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> listener) where T : struct
        {
            var type = typeof(T);
            if (_listeners.TryGetValue(type, out var existing))
                _listeners[type] = Delegate.Combine(existing, listener);
            else
                _listeners[type] = listener;
        }

        public static void Unsubscribe<T>(Action<T> listener) where T : struct
        {
            var type = typeof(T);
            if (_listeners.TryGetValue(type, out var existing))
            {
                var result = Delegate.Remove(existing, listener);
                if (result == null)
                    _listeners.Remove(type);
                else
                    _listeners[type] = result;
            }
        }

        public static void Publish<T>(T evt) where T : struct
        {
            if (_listeners.TryGetValue(typeof(T), out var existing))
                ((Action<T>)existing)?.Invoke(evt);
        }

        public static void Clear() => _listeners.Clear();
    }

    // ── Session Events ──

    public readonly struct SessionLifecycleChanged
    {
        public readonly string MachineId;
        public readonly SessionLifecycle Previous;
        public readonly SessionLifecycle Current;

        public SessionLifecycleChanged(string machineId, SessionLifecycle previous, SessionLifecycle current)
        {
            MachineId = machineId;
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct SessionCompleted
    {
        public readonly string MachineId;
        public readonly float TotalSeconds;

        public SessionCompleted(string machineId, float totalSeconds)
        {
            MachineId = machineId;
            TotalSeconds = totalSeconds;
        }
    }

    // ── Step Events ──

    public readonly struct StepActivated
    {
        public readonly string StepId;
        public readonly string AssemblyId;
        public readonly int StepIndex;
        public readonly int TotalSteps;

        public StepActivated(string stepId, string assemblyId, int stepIndex, int totalSteps)
        {
            StepId = stepId;
            AssemblyId = assemblyId;
            StepIndex = stepIndex;
            TotalSteps = totalSteps;
        }
    }

    public readonly struct StepStateChanged
    {
        public readonly string StepId;
        public readonly StepState Previous;
        public readonly StepState Current;
        public readonly float AtSeconds;

        public StepStateChanged(string stepId, StepState previous, StepState current, float atSeconds)
        {
            StepId = stepId;
            Previous = previous;
            Current = current;
            AtSeconds = atSeconds;
        }
    }

    public readonly struct HintRequested
    {
        public readonly string StepId;
        public readonly int TotalHintsForStep;

        public HintRequested(string stepId, int totalHintsForStep)
        {
            StepId = stepId;
            TotalHintsForStep = totalHintsForStep;
        }
    }

    // ── Tool Events ──

    public readonly struct ActiveToolChanged
    {
        public readonly string PreviousToolId;
        public readonly string CurrentToolId;
        public readonly bool IsRequiredForActiveStep;

        public ActiveToolChanged(string previousToolId, string currentToolId, bool isRequiredForActiveStep)
        {
            PreviousToolId = previousToolId;
            CurrentToolId = currentToolId;
            IsRequiredForActiveStep = isRequiredForActiveStep;
        }
    }

    public readonly struct ToolActionProgressed
    {
        public readonly string StepId;
        public readonly string ToolId;
        public readonly ToolActionType ActionType;
        public readonly int CurrentCount;
        public readonly int RequiredCount;
        public readonly string Message;

        public ToolActionProgressed(
            string stepId,
            string toolId,
            ToolActionType actionType,
            int currentCount,
            int requiredCount,
            string message)
        {
            StepId = stepId;
            ToolId = toolId;
            ActionType = actionType;
            CurrentCount = currentCount;
            RequiredCount = requiredCount;
            Message = message;
        }
    }

    public readonly struct ToolActionCompleted
    {
        public readonly string StepId;
        public readonly string ToolId;
        public readonly ToolActionType ActionType;
        public readonly int RequiredCount;
        public readonly string Message;

        public ToolActionCompleted(
            string stepId,
            string toolId,
            ToolActionType actionType,
            int requiredCount,
            string message)
        {
            StepId = stepId;
            ToolId = toolId;
            ActionType = actionType;
            RequiredCount = requiredCount;
            Message = message;
        }
    }

    public readonly struct ToolActionFailed
    {
        public readonly string StepId;
        public readonly string RequiredToolId;
        public readonly string ActiveToolId;
        public readonly ToolActionType ActionType;
        public readonly ToolActionFailureReason FailureReason;
        public readonly string Message;

        public ToolActionFailed(
            string stepId,
            string requiredToolId,
            string activeToolId,
            ToolActionType actionType,
            ToolActionFailureReason failureReason,
            string message)
        {
            StepId = stepId;
            RequiredToolId = requiredToolId;
            ActiveToolId = activeToolId;
            ActionType = actionType;
            FailureReason = failureReason;
            Message = message;
        }
    }

    // ── Part Events ──

    public readonly struct PartStateChanged
    {
        public readonly string PartId;
        public readonly string StepId;
        public readonly PartPlacementState Previous;
        public readonly PartPlacementState Current;

        public PartStateChanged(string partId, string stepId, PartPlacementState previous, PartPlacementState current)
        {
            PartId = partId;
            StepId = stepId;
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct PlacementAttempted
    {
        public readonly string PartId;
        public readonly string TargetId;
        public readonly bool IsValid;
        public readonly float PositionError;
        public readonly float RotationError;

        public PlacementAttempted(string partId, string targetId, bool isValid, float positionError, float rotationError)
        {
            PartId = partId;
            TargetId = targetId;
            IsValid = isValid;
            PositionError = positionError;
            RotationError = rotationError;
        }
    }

    // ── Step Readiness Events ──

    public readonly struct StepReadinessChecked
    {
        public readonly string StepId;
        public readonly bool IsReady;
        public readonly string[] Issues;

        public StepReadinessChecked(string stepId, bool isReady, string[] issues)
        {
            StepId = stepId;
            IsReady = isReady;
            Issues = issues;
        }
    }

    // ── Assembly Events ──

    public readonly struct AssemblyStarted
    {
        public readonly string AssemblyId;

        public AssemblyStarted(string assemblyId)
        {
            AssemblyId = assemblyId;
        }
    }

    public readonly struct AssemblyCompleted
    {
        public readonly string AssemblyId;

        public AssemblyCompleted(string assemblyId)
        {
            AssemblyId = assemblyId;
        }
    }

    public readonly struct MachineIntroDismissed
    {
        public readonly string MachineId;

        public MachineIntroDismissed(string machineId)
        {
            MachineId = machineId;
        }
    }

    public readonly struct MachineIntroReset
    {
        public readonly string MachineId;

        public MachineIntroReset(string machineId)
        {
            MachineId = machineId;
        }
    }

    /// <summary>
    /// Published after a saved session is restored. Carries the number of
    /// steps that were skipped so visual-layer listeners can position
    /// completed parts without replaying step events.
    /// </summary>
    public readonly struct SessionRestored
    {
        public readonly int CompletedStepCount;

        public SessionRestored(int completedStepCount)
        {
            CompletedStepCount = completedStepCount;
        }
    }

    /// <summary>
    /// Published when the user navigates to a different step via back/forward controls.
    /// The visual layer uses this to reposition parts and clear/re-spawn ghosts.
    /// Fires BEFORE StepStateChanged(Active) for the target step.
    /// </summary>
    public readonly struct StepNavigated
    {
        public readonly int PreviousStepIndex;
        public readonly int TargetStepIndex;
        public readonly int TotalSteps;

        public StepNavigated(int previousStepIndex, int targetStepIndex, int totalSteps)
        {
            PreviousStepIndex = previousStepIndex;
            TargetStepIndex = targetStepIndex;
            TotalSteps = totalSteps;
        }
    }

    public readonly struct RepositionModeChanged
    {
        public readonly bool IsActive;

        public RepositionModeChanged(bool isActive)
        {
            IsActive = isActive;
        }
    }
}
