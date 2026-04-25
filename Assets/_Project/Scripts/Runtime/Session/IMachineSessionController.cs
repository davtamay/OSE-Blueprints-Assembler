using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Service interface for the top-level session orchestrator.
    /// Allows call sites to depend on the abstraction rather than the
    /// concrete MachineSessionController implementation.
    /// </summary>
    public interface IMachineSessionController
    {
        /// <summary>
        /// Fires after the package is loaded and controllers are initialized,
        /// but before the first assembly begins.
        /// </summary>
        event Action<MachinePackageDefinition> PackageReady;

        MachineSessionState SessionState { get; }
        MachinePackageDefinition Package { get; }
        AssemblyRuntimeController AssemblyController { get; }
        IPartRuntimeController PartController { get; }
        IToolRuntimeController ToolController { get; }

        /// <summary>True while an explicit back/forward navigation is in progress.</summary>
        bool IsNavigating { get; }

        /// <summary>Realtime seconds when the last navigation completed. -1 if never.</summary>
        float LastNavigationTime { get; }

        bool CanStepBack { get; }
        bool CanStepForward { get; }

        Task<bool> StartSessionAsync(
            string packageId,
            SessionMode mode,
            int restoreStepCount = 0,
            string lastCompletedStepId = null,
            CancellationToken cancellationToken = default);

        void PauseSession();
        void ResumeSession();
        void EndSession();
        void FlushPersistenceSnapshot();

        float GetElapsedSeconds();
        void TickElapsed(float deltaTime);

        bool StepBack();
        bool StepForward();
        bool NavigateToLastStep();
        bool NavigateToGlobalStep(int globalIndex);

        bool RestoreToStep(int completedStepCount);

        void ResumeAfterTransition();

        /// <summary>
        /// Live-edit hook for authoring tools (TTAW). Updates the in-memory
        /// preview placement for one <paramref name="targetId"/> and notifies
        /// the spawned target marker GameObject to re-apply its transform.
        /// Strictly cosmetic / spatial — does NOT touch step state, task
        /// cursor, completed steps, or any other entity. The on-disk JSON
        /// is NOT read or written by this method; persistence is the caller's
        /// responsibility (TTAW's auto-save handles it via WriteJson). Returns
        /// false when the target id isn't found in the live preview config.
        /// Safe to call outside Play (no-op).
        /// </summary>
        bool HotReloadTargetPlacement(string targetId, SceneFloat3 position, SceneQuaternion rotation, SceneFloat3 scale);
    }
}
