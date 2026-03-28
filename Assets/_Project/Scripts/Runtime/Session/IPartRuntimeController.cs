using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Service interface for per-part placement state management.
    /// Allows call sites to depend on the abstraction rather than the
    /// concrete PartRuntimeController implementation.
    /// </summary>
    public interface IPartRuntimeController
    {
        string SelectedPartId { get; }
        string ActiveStepId { get; }

        void Initialize(MachinePackageDefinition package);
        void Dispose();

        PartPlacementState GetPartState(string partId);
        void GetPartsInState(PartPlacementState state, List<string> results);

        bool SelectPart(string partId);
        void DeselectPart();
        bool InspectPart(string partId);
        bool GrabPart(string partId);
        bool IsPartLockedForMovement(string partId);

        PlacementValidationResult AttemptPlacement(string partId, string targetId, PlacementValidationResult validationResult);
        void CompletePart(string partId);

        bool AreActiveStepRequiredPartsPlaced();
        string[] GetActiveStepTargetIds();
        string[] GetActiveStepRequiredPartIds();

        void RecomputePartsForNavigation(StepDefinition[] completedSteps, StepDefinition targetStep);
        void BulkCompletePartsForSteps(StepDefinition[] steps);
    }
}
