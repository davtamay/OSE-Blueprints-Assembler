using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Session
{
    /// <summary>
    /// Tracks the active assembly station and fires <see cref="StationContextChanged"/>
    /// whenever the learner moves to a new workstation.
    ///
    /// Called by <see cref="MachineSessionController"/> at the start of each assembly.
    /// The active station is derived from <see cref="AssemblyDefinition.stationId"/>
    /// looked up against <see cref="PackagePreviewConfig.stations"/>.
    ///
    /// When the composition step for a bench station completes (a Place step with
    /// a bench-unit subassembly), this controller publishes
    /// <see cref="StationCompositionCompleted"/> so visual layers can respond.
    /// </summary>
    public sealed class AssemblyStationController
    {
        private string                    _activeStationId;
        private MachinePackageDefinition  _package;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// The currently active station definition, or null when the active assembly
        /// has no <c>stationId</c> declared.
        /// </summary>
        public AssemblyStationDefinition ActiveStation { get; private set; }

        /// <summary>
        /// Called by <see cref="MachineSessionController.BeginCurrentAssembly"/> after
        /// the assembly is resolved. Fires <see cref="StationContextChanged"/> if the
        /// station differs from the previously active one.
        /// </summary>
        public void OnAssemblyStarted(string assemblyId, MachinePackageDefinition package)
        {
            _package = package;

            string newStationId = ResolveStationId(assemblyId, package);
            if (string.Equals(newStationId, _activeStationId, System.StringComparison.OrdinalIgnoreCase))
                return; // Same station — no transition needed

            _activeStationId = newStationId;
            ActiveStation    = FindStation(newStationId, package);

            if (ActiveStation == null)
            {
                OseLog.Info($"[StationController] Assembly '{assemblyId}' has no station — no spatial transition.");
                return;
            }

            var pos    = new Vector3(ActiveStation.position.x, ActiveStation.position.y, ActiveStation.position.z);
            var camPos = new Vector3(ActiveStation.cameraHome.x, ActiveStation.cameraHome.y, ActiveStation.cameraHome.z);

            RuntimeEventBus.Publish(new StationContextChanged(
                ActiveStation.id,
                ActiveStation.displayName,
                assemblyId,
                pos,
                ActiveStation.surfaceY,
                camPos,
                ActiveStation.cameraDistance));

            OseLog.Info($"[StationController] Station → '{ActiveStation.displayName}' (assembly '{assemblyId}').");
        }

        /// <summary>
        /// Called when a composition step (Place + bench-unit subassembly) completes.
        /// Publishes <see cref="StationCompositionCompleted"/> so the bench table prop
        /// can respond with a cleared/dimmed visual state.
        /// </summary>
        public void OnCompositionStepCompleted(string subassemblyId)
        {
            if (ActiveStation == null) return;
            RuntimeEventBus.Publish(new StationCompositionCompleted(ActiveStation.id, subassemblyId));
            OseLog.Info($"[StationController] Composition complete — bench '{ActiveStation.id}' cleared (subassembly '{subassemblyId}').");
        }

        /// <summary>Resets controller state at session end or restart.</summary>
        public void Reset()
        {
            _activeStationId = null;
            ActiveStation    = null;
            _package         = null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ResolveStationId(string assemblyId, MachinePackageDefinition package)
        {
            if (package?.assemblies == null) return null;
            foreach (var asm in package.assemblies)
                if (string.Equals(asm.id, assemblyId, System.StringComparison.OrdinalIgnoreCase))
                    return asm.stationId;
            return null;
        }

        private static AssemblyStationDefinition FindStation(string stationId, MachinePackageDefinition package)
        {
            if (string.IsNullOrEmpty(stationId)) return null;
            var stations = package?.previewConfig?.stations;
            if (stations == null) return null;
            foreach (var s in stations)
                if (string.Equals(s.id, stationId, System.StringComparison.OrdinalIgnoreCase))
                    return s;
            return null;
        }
    }
}
