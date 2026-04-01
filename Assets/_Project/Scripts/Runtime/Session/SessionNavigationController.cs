using System;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Provides read/write access to the session-level state that
    /// <see cref="SessionNavigationController"/> needs without taking a full
    /// <see cref="MachineSessionController"/> reference.
    /// </summary>
    internal interface INavigationHost
    {
        MachinePackageDefinition Package { get; }
        AssemblyRuntimeController AssemblyController { get; }
        IPartRuntimeController PartController { get; }
        MachineSessionState SessionState { get; }
        string[] AssemblyOrder { get; }
        int CurrentAssemblyIndex { get; set; }
    }

    /// <summary>
    /// Owns step back/forward navigation logic.
    /// Extracted from <see cref="MachineSessionController"/> so it can be
    /// read, tested, and modified in isolation.
    /// </summary>
    internal sealed class SessionNavigationController
    {
        private readonly INavigationHost _host;
        private int _lastNavigatedGlobalIndex = -1;

        /// <summary>True while an explicit back/forward navigation is in progress.</summary>
        public bool IsNavigating { get; private set; }

        /// <summary>Realtime seconds when the last navigation completed. -1 if never.</summary>
        public float LastNavigationTime { get; private set; } = -1f;

        public SessionNavigationController(INavigationHost host)
        {
            _host = host;
        }

        public bool CanStepBack
        {
            get
            {
                // During navigation the StepController is in a transitional state;
                // trust the index we just committed to.
                int idx;
                bool resolved;
                if (IsNavigating)
                {
                    idx = _lastNavigatedGlobalIndex;
                    resolved = true;
                }
                else
                {
                    resolved = TryGetCurrentGlobalStepIndex(out idx);
                    if (!resolved)
                        idx = _lastNavigatedGlobalIndex;
                }

                bool result = idx > 0;
                if (!result)
                {
                    OseLog.Warn($"[Nav] CanStepBack=false: resolved={resolved}, idx={idx}, " +
                        $"_lastNavigatedGlobalIndex={_lastNavigatedGlobalIndex}, IsNavigating={IsNavigating}");
                }
                return result;
            }
        }

        public bool CanStepForward
        {
            get
            {
                StepDefinition[] orderedSteps = _host.Package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
                if (orderedSteps.Length == 0)
                    return false;

                int idx;
                if (IsNavigating)
                    idx = _lastNavigatedGlobalIndex;
                else if (!TryGetCurrentGlobalStepIndex(out idx))
                    idx = _lastNavigatedGlobalIndex;

                return idx >= 0 && idx < orderedSteps.Length - 1;
            }
        }

        /// <summary>
        /// Navigates one step backward. Parts from the current step revert to
        /// Available; parts from subsequent steps become NotIntroduced.
        /// </summary>
        public bool StepBack()
        {
            if (_host.AssemblyController == null || _host.PartController == null)
            {
                OseLog.Warn("[Nav] StepBack: assemblyController or partController is null.");
                return false;
            }

            if (!TryGetCurrentGlobalStepIndex(out int currentGlobalIndex))
            {
                OseLog.Warn("[Nav] StepBack BLOCKED: unable to resolve current global step index.");
                return false;
            }

            OseLog.Info($"[Nav] StepBack: currentGlobalIndex={currentGlobalIndex}, CanStepBack={currentGlobalIndex > 0}, IsNavigating={IsNavigating}");

            if (currentGlobalIndex <= 0)
            {
                OseLog.Warn($"[Nav] StepBack BLOCKED: already at first global step (currentGlobalIndex={currentGlobalIndex}).");
                return false;
            }

            bool result = NavigateToGlobalStepInternal(currentGlobalIndex - 1);
            OseLog.Info($"[Nav] StepBack result={result}, new currentGlobalIndex={(TryGetCurrentGlobalStepIndex(out int afterIndex) ? afterIndex : -1)}");
            return result;
        }

        /// <summary>
        /// Navigates one step forward within the package-wide ordered step list.
        /// This is review/navigation behavior, not durable progression advancement.
        /// </summary>
        public bool StepForward()
        {
            if (_host.AssemblyController == null || _host.PartController == null)
                return false;

            if (!TryGetCurrentGlobalStepIndex(out int currentGlobalIndex))
                return false;

            StepDefinition[] orderedSteps = _host.Package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
            if (orderedSteps.Length == 0)
                return false;

            int maxNavigableIndex = orderedSteps.Length - 1;
            OseLog.Info($"[Nav] StepForward: currentGlobalIndex={currentGlobalIndex}, maxNavigableIndex={maxNavigableIndex}");

            if (currentGlobalIndex >= maxNavigableIndex)
                return false;

            return NavigateToGlobalStepInternal(currentGlobalIndex + 1);
        }

        /// <summary>
        /// Jumps directly to the last step in the package-wide ordered step list.
        /// All prior parts are treated as completed and placed at their playPositions.
        /// </summary>
        public bool NavigateToLastStep()
        {
            StepDefinition[] orderedSteps = _host.Package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
            if (orderedSteps.Length == 0)
                return false;

            return NavigateToGlobalStepInternal(orderedSteps.Length - 1);
        }

        /// <summary>
        /// Jumps directly to a specific global step index (0-based).
        /// </summary>
        public bool NavigateToGlobalStep(int targetGlobalIndex)
        {
            return NavigateToGlobalStepInternal(targetGlobalIndex);
        }

        private bool NavigateToGlobalStepInternal(int targetGlobalIndex)
        {
            StepDefinition[] orderedSteps = _host.Package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
            if (orderedSteps.Length == 0)
                return false;

            if (!TryGetCurrentGlobalStepIndex(out int previousGlobalIndex))
                previousGlobalIndex = 0;

            int clampedTargetGlobalIndex = Math.Max(0, Math.Min(targetGlobalIndex, orderedSteps.Length - 1));
            StepDefinition targetStep = orderedSteps[clampedTargetGlobalIndex];
            if (targetStep == null)
                return false;

            MachineSessionState sessionState = _host.SessionState;
            string targetAssemblyId = !string.IsNullOrWhiteSpace(targetStep.assemblyId)
                ? targetStep.assemblyId
                : sessionState?.CurrentAssemblyId;
            if (string.IsNullOrWhiteSpace(targetAssemblyId))
                return false;

            int targetAssemblyIndex = Array.FindIndex(
                _host.AssemblyOrder ?? Array.Empty<string>(),
                id => string.Equals(id, targetAssemblyId, StringComparison.OrdinalIgnoreCase));
            if (targetAssemblyIndex < 0)
                targetAssemblyIndex = _host.CurrentAssemblyIndex;

            int localTargetIndex = CountCompletedStepsForAssembly(orderedSteps, targetAssemblyId, clampedTargetGlobalIndex);

            StepDefinition[] completedGlobalSteps = Array.Empty<StepDefinition>();
            if (clampedTargetGlobalIndex > 0)
            {
                completedGlobalSteps = new StepDefinition[clampedTargetGlobalIndex];
                Array.Copy(orderedSteps, completedGlobalSteps, clampedTargetGlobalIndex);
            }

            OseLog.Info($"[Nav] NavigateToGlobalStepInternal: from global {previousGlobalIndex} to {clampedTargetGlobalIndex}, assembly='{targetAssemblyId}', localTargetIndex={localTargetIndex}, totalGlobalSteps={orderedSteps.Length}");

            IsNavigating = true;
            _lastNavigatedGlobalIndex = clampedTargetGlobalIndex;
            try
            {
                _host.PartController.RecomputePartsForNavigation(completedGlobalSteps, targetStep);

                RuntimeEventBus.Publish(new StepNavigated(previousGlobalIndex, clampedTargetGlobalIndex, orderedSteps.Length));

                _host.CurrentAssemblyIndex = targetAssemblyIndex;
                sessionState.CurrentAssemblyId = targetAssemblyId;

                AssemblyRuntimeController assembly = _host.AssemblyController;
                if (string.Equals(assembly.CurrentAssemblyId, targetAssemblyId, StringComparison.OrdinalIgnoreCase))
                {
                    assembly.NavigateToStep(localTargetIndex, () => sessionState.ElapsedSeconds);
                }
                else
                {
                    assembly.NavigateToStepInAssembly(targetAssemblyId, localTargetIndex, () => sessionState.ElapsedSeconds);
                }

                if (assembly.StepController.HasActiveStep)
                    sessionState.CurrentStepId = assembly.StepController.CurrentStepState.StepId;

                OseLog.Info($"[Nav] Navigated from global step {previousGlobalIndex + 1} to global step {clampedTargetGlobalIndex + 1}. " +
                    $"CanStepBack={CanStepBack}, CanStepForward={CanStepForward}");
                return true;
            }
            catch (Exception ex)
            {
                OseLog.Warn($"[Nav] EXCEPTION during navigation to global step {clampedTargetGlobalIndex + 1}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
            finally
            {
                IsNavigating = false;
                LastNavigationTime = UnityEngine.Time.realtimeSinceStartup;
            }
        }

        private bool TryGetCurrentGlobalStepIndex(out int currentGlobalIndex)
        {
            currentGlobalIndex = -1;

            StepDefinition[] orderedSteps = _host.Package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
            AssemblyRuntimeController assembly = _host.AssemblyController;
            string activeStepId =
                assembly?.StepController?.HasActiveStep == true
                    ? assembly.StepController.CurrentStepState.StepId
                    : _host.SessionState?.CurrentStepId;

            if (orderedSteps.Length == 0 || string.IsNullOrWhiteSpace(activeStepId))
            {
                OseLog.Warn($"[Nav] TryGetCurrentGlobalStepIndex FAILED: orderedSteps={orderedSteps.Length}, " +
                    $"activeStepId='{activeStepId}', hasActiveStep={assembly?.StepController?.HasActiveStep}, " +
                    $"sessionStepId='{_host.SessionState?.CurrentStepId}'");
                return false;
            }

            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].id, activeStepId, StringComparison.OrdinalIgnoreCase))
                {
                    currentGlobalIndex = i;
                    return true;
                }
            }

            OseLog.Warn($"[Nav] TryGetCurrentGlobalStepIndex FAILED: step '{activeStepId}' not found in {orderedSteps.Length} ordered steps.");
            return false;
        }

        /// <summary>
        /// Counts steps within <paramref name="assemblyId"/> among the first
        /// <paramref name="completedGlobalCount"/> entries of <paramref name="orderedSteps"/>.
        /// Used to translate a global step index into an assembly-local index.
        /// </summary>
        internal static int CountCompletedStepsForAssembly(
            StepDefinition[] orderedSteps,
            string assemblyId,
            int completedGlobalCount)
        {
            if (orderedSteps == null || completedGlobalCount <= 0 || string.IsNullOrWhiteSpace(assemblyId))
                return 0;

            int count = 0;
            int limit = Math.Min(completedGlobalCount, orderedSteps.Length);
            for (int i = 0; i < limit; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].assemblyId, assemblyId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
