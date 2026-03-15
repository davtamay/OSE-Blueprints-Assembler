using System;
using System.Threading;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Top-level session orchestrator. Owns MachineSessionState, loads packages,
    /// creates child controllers, and manages the session lifecycle.
    /// Registered in ServiceRegistry for external access.
    /// </summary>
    public sealed class MachineSessionController
    {
        private readonly MachinePackageLoader _loader = new MachinePackageLoader();
        private MachineSessionState _sessionState;
        private MachinePackageDefinition _package;
        private AssemblyRuntimeController _assemblyController;
        private PartRuntimeController _partController;
        private string[] _assemblyOrder;
        private int _currentAssemblyIndex;

        /// <summary>
        /// Fires after the package is loaded and controllers are initialized,
        /// but before the first assembly begins (i.e. before any StepStateChanged events).
        /// Subscribers can use this to set up scene objects that need to exist before
        /// step events fire.
        /// </summary>
        public event Action<MachinePackageDefinition> PackageReady;

        public MachineSessionState SessionState => _sessionState;
        public MachinePackageDefinition Package => _package;
        public AssemblyRuntimeController AssemblyController => _assemblyController;
        public PartRuntimeController PartController => _partController;

        /// <summary>
        /// Loads a machine package and starts a new session.
        /// Returns true if the session started successfully.
        /// </summary>
        public async Task<bool> StartSessionAsync(
            string packageId,
            SessionMode mode,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                OseLog.Error("[MachineSessionController] Package id is required.");
                return false;
            }

            // Clean up any previous session
            EndSession();

            // Initialize session state
            _sessionState = new MachineSessionState
            {
                MachineId = packageId,
                Mode = mode,
                Lifecycle = SessionLifecycle.Uninitialized
            };

            SetLifecycle(SessionLifecycle.Initializing);

            // Load the package
            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId, cancellationToken);
            if (!result.IsSuccess)
            {
                OseLog.Error($"[MachineSessionController] Failed to load package '{packageId}': {result.ErrorMessage}");
                SetLifecycle(SessionLifecycle.Error);
                return false;
            }

            _package = result.Package;
            _sessionState.MachineVersion = _package.packageVersion ?? string.Empty;
            _sessionState.ChallengeActive = ResolveChallengeActive(mode, _package);

            // Determine assembly order
            _assemblyOrder = ResolveAssemblyOrder();
            if (_assemblyOrder.Length == 0)
            {
                OseLog.Error($"[MachineSessionController] Package '{packageId}' has no assemblies to run.");
                SetLifecycle(SessionLifecycle.Error);
                return false;
            }

            // Create child controllers
            _assemblyController = new AssemblyRuntimeController();
            _assemblyController.Initialize(_package);
            _assemblyController.OnAssemblyCompleted += HandleAssemblyCompleted;

            // Initialize part runtime controller if registered
            if (ServiceRegistry.TryGet<PartRuntimeController>(out _partController))
            {
                _partController.Initialize(_package);
            }

            // Subscribe to step events to keep session state current
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<HintRequested>(HandleHintRequested);

            _currentAssemblyIndex = 0;
            SetLifecycle(SessionLifecycle.SessionActive);

            // Notify listeners before any step events fire
            PackageReady?.Invoke(_package);

            // Begin the first assembly
            BeginCurrentAssembly();

            return true;
        }

        private static bool ResolveChallengeActive(SessionMode mode, MachinePackageDefinition package)
        {
            if (mode != SessionMode.Challenge)
                return false;

            if (package?.challengeConfig != null)
                return package.challengeConfig.enabled;

            return true;
        }

        public void PauseSession()
        {
            if (_sessionState == null || _sessionState.Lifecycle == SessionLifecycle.Paused)
                return;

            if (_assemblyController?.StepController?.HasActiveStep == true)
                _assemblyController.StepController.SuspendStep();

            SetLifecycle(SessionLifecycle.Paused);
        }

        public void ResumeSession()
        {
            if (_sessionState == null || _sessionState.Lifecycle != SessionLifecycle.Paused)
                return;

            SetLifecycle(SessionLifecycle.StepActive);

            if (_assemblyController?.StepController != null)
                _assemblyController.StepController.ResumeStep(_sessionState.ElapsedSeconds);
        }

        public void EndSession()
        {
            if (_sessionState == null)
                return;

            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<HintRequested>(HandleHintRequested);

            if (_partController != null)
            {
                _partController.Dispose();
                _partController = null;
            }

            if (_assemblyController != null)
            {
                _assemblyController.OnAssemblyCompleted -= HandleAssemblyCompleted;
                _assemblyController.Dispose();
                _assemblyController = null;
            }

            string machineId = _sessionState.MachineId;
            SetLifecycle(SessionLifecycle.Completed);

            _package = null;
            _assemblyOrder = null;
            _sessionState = null;

            OseLog.Info($"[MachineSessionController] Session for '{machineId}' ended.");
        }

        /// <summary>
        /// Provides the current elapsed seconds for the session.
        /// Controllers use this to timestamp step transitions.
        /// </summary>
        public float GetElapsedSeconds() => _sessionState?.ElapsedSeconds ?? 0f;

        /// <summary>
        /// Call this externally (e.g. from a MonoBehaviour Update) to advance the elapsed timer.
        /// </summary>
        public void TickElapsed(float deltaTime)
        {
            if (_sessionState != null &&
                _sessionState.Lifecycle == SessionLifecycle.StepActive)
            {
                _sessionState.ElapsedSeconds += deltaTime;
                if (_sessionState.CurrentStepStartSeconds >= 0f)
                {
                    _sessionState.CurrentStepElapsedSeconds =
                        _sessionState.ElapsedSeconds - _sessionState.CurrentStepStartSeconds;
                }
            }
        }

        private void BeginCurrentAssembly()
        {
            if (_currentAssemblyIndex >= _assemblyOrder.Length)
            {
                CompleteSession();
                return;
            }

            string assemblyId = _assemblyOrder[_currentAssemblyIndex];
            _sessionState.CurrentAssemblyId = assemblyId;

            SetLifecycle(SessionLifecycle.StepActive);

            _assemblyController.BeginAssembly(assemblyId, () => _sessionState.ElapsedSeconds);

            // Update session state with the first step id
            if (_assemblyController.StepController.HasActiveStep)
            {
                _sessionState.CurrentStepId = _assemblyController.StepController.CurrentStepState.StepId;
            }
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (_sessionState == null)
                return;

            // Keep session state in sync with the active step
            if (evt.Current == StepState.Active)
            {
                _sessionState.CurrentStepId = evt.StepId;
                _sessionState.CurrentStepStartSeconds = evt.AtSeconds;
                _sessionState.CurrentStepElapsedSeconds = 0f;
            }
            else if (evt.Current == StepState.FailedAttempt)
            {
                _sessionState.MistakeCount++;
            }
            else if (evt.Current == StepState.Completed)
            {
                float duration = evt.AtSeconds - _sessionState.CurrentStepStartSeconds;
                if (duration < 0f) duration = 0f;

                _sessionState.LastStepDurationSeconds = duration;
                _sessionState.TotalStepDurationSeconds += duration;
                _sessionState.CompletedStepCount++;
                _sessionState.CurrentStepElapsedSeconds = duration;
            }
        }

        private void HandleHintRequested(HintRequested evt)
        {
            if (_sessionState == null)
                return;

            _sessionState.HintsUsed++;
        }

        private void HandleAssemblyCompleted(string assemblyId)
        {
            OseLog.Info($"[MachineSessionController] Assembly '{assemblyId}' completed. Checking for next assembly.");

            _currentAssemblyIndex++;
            if (_currentAssemblyIndex < _assemblyOrder.Length)
            {
                BeginCurrentAssembly();
            }
            else
            {
                CompleteSession();
            }
        }

        private void CompleteSession()
        {
            float totalSeconds = _sessionState.ElapsedSeconds;
            string machineId = _sessionState.MachineId;

            SetLifecycle(SessionLifecycle.Completing);

            OseLog.Info($"[MachineSessionController] Session '{machineId}' completed in {totalSeconds:F1}s.");
            RuntimeEventBus.Publish(new SessionCompleted(machineId, totalSeconds));

            SetLifecycle(SessionLifecycle.Completed);
        }

        private void SetLifecycle(SessionLifecycle next)
        {
            if (_sessionState == null)
                return;

            SessionLifecycle previous = _sessionState.Lifecycle;
            _sessionState.Lifecycle = next;

            OseLog.SessionEvent(_sessionState.MachineId, next);
            RuntimeEventBus.Publish(new SessionLifecycleChanged(_sessionState.MachineId, previous, next));
        }

        private string[] ResolveAssemblyOrder()
        {
            if (_package.machine?.entryAssemblyIds != null && _package.machine.entryAssemblyIds.Length > 0)
                return _package.machine.entryAssemblyIds;

            // Fallback: use all assemblies in definition order
            AssemblyDefinition[] assemblies = _package.GetAssemblies();
            string[] ids = new string[assemblies.Length];
            for (int i = 0; i < assemblies.Length; i++)
                ids[i] = assemblies[i].id;
            return ids;
        }
    }
}
