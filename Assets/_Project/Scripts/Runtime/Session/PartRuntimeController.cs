using System;
using System.Collections.Generic;
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
            if (currentState == PartPlacementState.NotIntroduced ||
                currentState == PartPlacementState.Completed ||
                currentState == PartPlacementState.PlacedVirtually)
                return false;

            // Deselect previous
            if (_selectedPartId != null && _selectedPartId != partId)
                DeselectPart();

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
            _selectedPartId = null;

            var state = GetPartState(partId);
            if (state == PartPlacementState.Selected || state == PartPlacementState.Inspected)
                TransitionPart(partId, PartPlacementState.Available);
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
            if (currentState != PartPlacementState.Selected &&
                currentState != PartPlacementState.Available &&
                currentState != PartPlacementState.Inspected)
                return false;

            TransitionPart(partId, PartPlacementState.Grabbed);
            return true;
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

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (evt.Current == StepState.Active)
            {
                _activeStepId = evt.StepId;
                IntroduceStepParts(evt.StepId);
            }
            else if (evt.Current == StepState.Completed)
            {
                CompleteStepParts(evt.StepId);
            }
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
                    if (current == PartPlacementState.NotIntroduced)
                        TransitionPart(partIds[i], PartPlacementState.Available);
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

            OseLog.VerboseInfo($"[PartRuntime] Parts introduced for step '{stepId}'.");
        }

        private void CompleteStepParts(string stepId)
        {
            if (_package == null || !_package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null) return;

            for (int i = 0; i < partIds.Length; i++)
            {
                if (string.IsNullOrEmpty(partIds[i])) continue;
                var current = GetPartState(partIds[i]);
                if (current != PartPlacementState.Completed)
                    TransitionPart(partIds[i], PartPlacementState.Completed);
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
