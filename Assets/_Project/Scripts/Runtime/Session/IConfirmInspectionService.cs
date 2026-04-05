using OSE.Content;

namespace OSE.Runtime
{
    /// <summary>
    /// Manages the observe-phase of Confirm-family steps that have <c>targetIds</c>.
    /// Implemented by <c>ConfirmInspectionService</c> in OSE.UI.Root, registered via
    /// <see cref="OSE.App.ServiceRegistry"/> so OSE.Runtime handlers can call it without
    /// a direct assembly reference.
    /// </summary>
    public interface IConfirmInspectionService
    {
        /// <summary>Spawns inspection markers for every targetId in <paramref name="step"/>.</summary>
        void ShowMarkersForStep(StepDefinition step);

        /// <summary>
        /// Tests each unvisited target against the camera frustum.
        /// Publishes <see cref="ObserveTargetsCompleted"/> when all are framed.
        /// </summary>
        void UpdateObservations();

        /// <summary>Despawns all markers and resets state.</summary>
        void ClearMarkers();
    }
}
