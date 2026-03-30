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
    }
}
