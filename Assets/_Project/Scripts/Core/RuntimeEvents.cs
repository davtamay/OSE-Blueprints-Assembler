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
}
