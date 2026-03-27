using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Manages per-part placement state for the current session.
    /// Subscribes to step events to make parts Available when a step activates,
    /// and provides methods for the interaction flow (select, grab, place).
    /// Plain C# — registered in ServiceRegistry, not a MonoBehaviour.
    /// </summary>
    public sealed class PartRuntimeController
    {
        private MachinePackageDefinition _package;
        private readonly Dictionary<string, PartPlacementState> _partStates = new Dictionary<string, PartPlacementState>(StringComparer.OrdinalIgnoreCase);
        private string _selectedPartId;
        private PartPlacementState _selectedPartPreviousState;
        private string _activeStepId;

        public string SelectedPartId => _selectedPartId;
        public string ActiveStepId => _activeStepId;

        /// <summary>
        /// Returns the current placement state for a part, or NotIntroduced if unknown.
        /// </summary>
        public PartPlacementState GetPartState(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return PartPlacementState.NotIntroduced;
            return _partStates.TryGetValue(partId, out var state) ? state : PartPlacementState.NotIntroduced;
        }

        /// <summary>
        /// Returns all part IDs that are currently in a given state.
        /// </summary>
        public void GetPartsInState(PartPlacementState state, List<string> results)
        {
            results.Clear();
            foreach (var kvp in _partStates)
            {
                if (kvp.Value == state)
                    results.Add(kvp.Key);
            }
        }

        public void Initialize(MachinePackageDefinition package)
        {
            _package = package;
            _partStates.Clear();
            _selectedPartId = null;
            _activeStepId = null;

            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
        }

        public void Dispose()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            _partStates.Clear();
            _selectedPartId = null;
            _activeStepId = null;
            _package = null;
        }

        /// <summary>
        /// Marks a part as Selected. Publishes PartStateChanged.
        /// </summary>
        public bool SelectPart(string partId)
        {
            if (string.IsNullOrEmpty(partId) || _package == null)
                return false;

            var currentState = GetPartState(partId);
            if (currentState == PartPlacementState.NotIntroduced)
                return false;

            // Deselect previous
            if (_selectedPartId != null && _selectedPartId != partId)
                DeselectPart();

            // Re-selecting the same part: keep the original previous state so we
            // don't overwrite Completed with Selected and break the lock check.
            if (!string.Equals(_selectedPartId, partId, StringComparison.OrdinalIgnoreCase))
                _selectedPartPreviousState = currentState;

            _selectedPartId = partId;
            TransitionPart(partId, PartPlacementState.Selected);
            return true;
        }

        /// <summary>
        /// Clears the current selection.
        /// </summary>
        public void DeselectPart()
        {
            if (_selectedPartId == null) return;

            string partId = _selectedPartId;
            var previousState = _selectedPartPreviousState;
            _selectedPartId = null;
            _selectedPartPreviousState = PartPlacementState.Available;

            var state = GetPartState(partId);
            if (state == PartPlacementState.Selected ||
                state == PartPlacementState.Inspected ||
                state == PartPlacementState.Grabbed)
            {
                // Restore to original state if it was Completed or PlacedVirtually
                var restoreState = (previousState == PartPlacementState.Completed ||
                                    previousState == PartPlacementState.PlacedVirtually)
                    ? previousState
                    : PartPlacementState.Available;
                TransitionPart(partId, restoreState);
            }
        }

        /// <summary>
        /// Marks a part as Inspected (selected + info shown). Publishes PartStateChanged.
        /// </summary>
        public bool InspectPart(string partId)
        {
            if (string.IsNullOrEmpty(partId) || _package == null)
                return false;

            var currentState = GetPartState(partId);
            if (currentState != PartPlacementState.Selected && currentState != PartPlacementState.Available)
                return false;

            _selectedPartId = partId;
            TransitionPart(partId, PartPlacementState.Inspected);
            return true;
        }

        /// <summary>
        /// Marks a part as Grabbed (being dragged/moved). Publishes PartStateChanged.
        /// </summary>
        public bool GrabPart(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return false;

            var currentState = GetPartState(partId);
            if (currentState == PartPlacementState.Grabbed)
                return !IsPartLockedForMovement(partId);

            // Lock parts once they are placed/completed so users cannot drag
            // them away from validated preview/target positions.
            if (IsPartLockedForMovement(partId))
                return false;

            if (currentState != PartPlacementState.Selected &&
                currentState != PartPlacementState.Available &&
                currentState != PartPlacementState.Inspected)
                return false;

            TransitionPart(partId, PartPlacementState.Grabbed);
            return true;
        }

        /// <summary>
        /// Returns true when a part should be immovable (placed/completed), including
        /// when currently selected/inspected after coming from a placed/completed state.
        /// </summary>
        public bool IsPartLockedForMovement(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return false;

            PartPlacementState currentState = GetPartState(partId);
            if (currentState == PartPlacementState.PlacedVirtually ||
                currentState == PartPlacementState.Completed)
            {
                return true;
            }

            if (_selectedPartId != null &&
                string.Equals(_selectedPartId, partId, StringComparison.OrdinalIgnoreCase) &&
                (currentState == PartPlacementState.Selected ||
                 currentState == PartPlacementState.Inspected ||
                 currentState == PartPlacementState.Grabbed))
            {
                return _selectedPartPreviousState == PartPlacementState.PlacedVirtually ||
                       _selectedPartPreviousState == PartPlacementState.Completed;
            }

            return false;
        }

        /// <summary>
        /// Attempts placement. If valid, transitions to ValidPlacement then PlacedVirtually.
        /// If invalid, transitions to InvalidPlacement then back to Available.
        /// Returns the validation result.
        /// </summary>
        public PlacementValidationResult AttemptPlacement(string partId, string targetId, PlacementValidationResult validationResult)
        {
            if (string.IsNullOrEmpty(partId)) return PlacementValidationResult.Invalid(ValidationFailureReason.WrongPart);

            RuntimeEventBus.Publish(new PlacementAttempted(
                partId, targetId, validationResult.IsValid,
                validationResult.PositionError, validationResult.RotationError));

            if (validationResult.IsValid)
            {
                TransitionPart(partId, PartPlacementState.ValidPlacement);
                TransitionPart(partId, PartPlacementState.PlacedVirtually);
                OseLog.Info($"[PartRuntime] Part '{partId}' placed at target '{targetId}'.");
            }
            else
            {
                TransitionPart(partId, PartPlacementState.InvalidPlacement);
                TransitionPart(partId, PartPlacementState.Available);
                OseLog.VerboseInfo($"[PartRuntime] Part '{partId}' placement invalid at '{targetId}': {validationResult.FailureReason}");
            }

            return validationResult;
        }

        /// <summary>
        /// Marks a part as Completed (final state after step completion).
        /// </summary>
        public void CompletePart(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return;
            TransitionPart(partId, PartPlacementState.Completed);
        }

        /// <summary>
        /// Recomputes all part states for step navigation.
        /// Parts from steps before targetIndex become Completed.
        /// Parts from the target step become Available (unless already Completed from a prior step).
        /// Parts from steps after targetIndex stay NotIntroduced.
        /// Does NOT publish PartStateChanged events — the caller publishes StepNavigated
        /// for the visual layer to reposition parts in bulk.
        /// </summary>
        public void RecomputePartsForNavigation(StepDefinition[] completedSteps, StepDefinition targetStep)
        {
            if (_package == null) return;

            _partStates.Clear();
            _selectedPartId = null;
            _selectedPartPreviousState = PartPlacementState.Available;
            _activeStepId = null;

            if (completedSteps != null)
            {
                for (int i = 0; i < completedSteps.Length; i++)
                {
                    string[] partIds = completedSteps[i]?.requiredPartIds;
                    if (partIds == null) continue;
                    for (int p = 0; p < partIds.Length; p++)
                    {
                        if (!string.IsNullOrEmpty(partIds[p]))
                            _partStates[partIds[p]] = PartPlacementState.Completed;
                    }
                }
            }

            if (targetStep != null)
            {
                _activeStepId = targetStep.id;
                string[] partIds = targetStep.requiredPartIds;
                if (partIds != null)
                {
                    for (int p = 0; p < partIds.Length; p++)
                    {
                        if (!string.IsNullOrEmpty(partIds[p]) && GetPartState(partIds[p]) != PartPlacementState.Completed)
                            _partStates[partIds[p]] = PartPlacementState.Available;
                    }
                }
            }

            OseLog.Info($"[PartRuntime] Recomputed part states for navigation to '{_activeStepId ?? "<none>"}' with {(_partStates?.Count ?? 0)} tracked part(s).");
        }

        /// <summary>
        /// Marks all required parts from the given steps as Completed in bulk.
        /// Used by session restore to set part states directly without replaying
        /// the full step event cascade.
        /// </summary>
        public void BulkCompletePartsForSteps(StepDefinition[] steps)
        {
            if (_package == null || steps == null) return;

            for (int s = 0; s < steps.Length; s++)
            {
                string[] partIds = steps[s].requiredPartIds;
                if (partIds == null) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    if (string.IsNullOrEmpty(partIds[p])) continue;
                    _partStates[partIds[p]] = PartPlacementState.Completed;
                }
            }

            OseLog.Info($"[PartRuntime] Bulk-completed parts for {steps.Length} restored steps.");
        }

        /// <summary>
        /// Returns the target IDs for the currently active step, or empty if none.
        /// </summary>
        public string[] GetActiveStepTargetIds()
        {
            if (_package == null || string.IsNullOrEmpty(_activeStepId))
                return Array.Empty<string>();

            if (!_package.TryGetStep(_activeStepId, out var step))
                return Array.Empty<string>();

            return step.targetIds ?? Array.Empty<string>();
        }

        /// <summary>
        /// Returns the required part IDs for the currently active step.
        /// </summary>
        public string[] GetActiveStepRequiredPartIds()
        {
            if (_package == null || string.IsNullOrEmpty(_activeStepId))
                return Array.Empty<string>();

            if (!_package.TryGetStep(_activeStepId, out var step))
                return Array.Empty<string>();

            return step.requiredPartIds ?? Array.Empty<string>();
        }

        /// <summary>
        /// Returns true if all required parts for the active step are placed (PlacedVirtually or Completed).
        /// </summary>
        public bool AreActiveStepRequiredPartsPlaced()
        {
            if (_package == null || string.IsNullOrEmpty(_activeStepId))
                return false;

            if (!_package.TryGetStep(_activeStepId, out var step))
                return false;

            if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
            {
                return ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                       subassemblyController != null &&
                       subassemblyController.IsActiveStepPlacementSatisfied(_activeStepId);
            }

            string[] required = step.requiredPartIds;
            if (required == null || required.Length == 0)
                return true;

            for (int i = 0; i < required.Length; i++)
            {
                string partId = required[i];
                if (string.IsNullOrEmpty(partId))
                    continue;

                var state = GetPartState(partId);
                if (state != PartPlacementState.PlacedVirtually && state != PartPlacementState.Completed)
                    return false;
            }

            return true;
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            // Always clear selection on any step transition
            if (_selectedPartId != null)
            {
                string prevSelected = _selectedPartId;
                var previousState = _selectedPartPreviousState;
                _selectedPartId = null;
                _selectedPartPreviousState = PartPlacementState.Available;
                var state = GetPartState(prevSelected);
                if (state == PartPlacementState.Selected ||
                    state == PartPlacementState.Inspected ||
                    state == PartPlacementState.Grabbed)
                {
                    var restoreState = (previousState == PartPlacementState.Completed ||
                                        previousState == PartPlacementState.PlacedVirtually)
                        ? previousState
                        : PartPlacementState.Available;
                    TransitionPart(prevSelected, restoreState);
                }
            }

            if (evt.Current == StepState.Active)
            {
                _activeStepId = evt.StepId;
                ResetAllTransientStates();
                IntroduceStepParts(evt.StepId);
            }
            else if (evt.Current == StepState.Completed)
            {
                CompleteStepParts(evt.StepId);
            }
        }

        /// <summary>
        /// Resets any part stuck in a transient state (Selected, Inspected, Grabbed)
        /// back to Available. Guarantees no stuck state across step transitions.
        /// </summary>
        private void ResetAllTransientStates()
        {
            var keysToReset = new List<string>();
            foreach (var kvp in _partStates)
            {
                if (kvp.Value == PartPlacementState.Selected ||
                    kvp.Value == PartPlacementState.Inspected ||
                    kvp.Value == PartPlacementState.Grabbed)
                    keysToReset.Add(kvp.Key);
            }
            for (int i = 0; i < keysToReset.Count; i++)
                TransitionPart(keysToReset[i], PartPlacementState.Available);
        }

        private void IntroduceStepParts(string stepId)
        {
            if (_package == null || !_package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds != null)
            {
                for (int i = 0; i < partIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(partIds[i])) continue;
                    var current = GetPartState(partIds[i]);
                    // Only introduce parts not yet seen. Parts already Completed or
                    // PlacedVirtually from prior steps must keep their state.
                    if (current == PartPlacementState.NotIntroduced)
                    {
                        TransitionPart(partIds[i], PartPlacementState.Available);
                        OseLog.VerboseInfo($"[PartRuntime] Part '{partIds[i]}' introduced (NotIntroduced → Available) for step '{stepId}'.");
                    }
                    else
                    {
                        OseLog.VerboseInfo($"[PartRuntime] Part '{partIds[i]}' already in state '{current}', skipping introduction for step '{stepId}'.");
                    }
                }
            }

            string[] optionalIds = step.optionalPartIds;
            if (optionalIds != null)
            {
                for (int i = 0; i < optionalIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(optionalIds[i])) continue;
                    var current = GetPartState(optionalIds[i]);
                    if (current == PartPlacementState.NotIntroduced)
                        TransitionPart(optionalIds[i], PartPlacementState.Available);
                }
            }

            OseLog.VerboseInfo($"[PartRuntime] Part introduction complete for step '{stepId}'. Required: {partIds?.Length ?? 0}, Optional: {optionalIds?.Length ?? 0}.");
        }

        private void CompleteStepParts(string stepId)
        {
            if (_package == null || !_package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds != null)
            {
                for (int i = 0; i < partIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(partIds[i])) continue;
                    var current = GetPartState(partIds[i]);
                    if (current != PartPlacementState.Completed)
                        TransitionPart(partIds[i], PartPlacementState.Completed);
                }
            }

            // Reset optional parts that were selected/inspected back to Available
            string[] optionalIds = step.optionalPartIds;
            if (optionalIds != null)
            {
                for (int i = 0; i < optionalIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(optionalIds[i])) continue;
                    var current = GetPartState(optionalIds[i]);
                    if (current == PartPlacementState.Selected ||
                        current == PartPlacementState.Inspected ||
                        current == PartPlacementState.Grabbed)
                        TransitionPart(optionalIds[i], PartPlacementState.Available);
                }
            }

            _selectedPartId = null;
        }

        private void TransitionPart(string partId, PartPlacementState newState)
        {
            var previous = GetPartState(partId);
            _partStates[partId] = newState;

            RuntimeEventBus.Publish(new PartStateChanged(partId, _activeStepId ?? string.Empty, previous, newState));
        }

    }
}
