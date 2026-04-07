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
    public sealed class MachineSessionController : IMachineSessionController, INavigationHost
    {
        private readonly IMachinePackageLoader _loader;

        public MachineSessionController() : this(new MachinePackageLoader()) { }

        /// <summary>
        /// Accepts an explicit <see cref="IMachinePackageLoader"/> — useful for testing
        /// with a stub loader that returns a pre-built package without hitting the file system.
        /// </summary>
        public MachineSessionController(IMachinePackageLoader loader)
        {
            _loader = loader;
        }
        private MachineSessionState _sessionState;
        private MachinePackageDefinition _package;
        private AssemblyRuntimeController _assemblyController;
        private IPartRuntimeController _partController;
        private IToolRuntimeController _toolController;
        private string[] _assemblyOrder;
        private int _currentAssemblyIndex;

        private SessionNavigationController _navigation;
        private bool _isLoading;
        // INavigationHost explicit implementations (private — callers use IMachineSessionController)
        MachinePackageDefinition INavigationHost.Package => _package;
        AssemblyRuntimeController INavigationHost.AssemblyController => _assemblyController;
        IPartRuntimeController INavigationHost.PartController => _partController;
        MachineSessionState INavigationHost.SessionState => _sessionState;
        string[] INavigationHost.AssemblyOrder => _assemblyOrder;
        int INavigationHost.CurrentAssemblyIndex
        {
            get => _currentAssemblyIndex;
            set => _currentAssemblyIndex = value;
        }

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
        public IPartRuntimeController PartController => _partController;
        public IToolRuntimeController ToolController => _toolController;

        // ── Step Navigation (delegated to SessionNavigationController) ──

        /// <summary>True while an explicit back/forward navigation is in progress.</summary>
        public bool IsNavigating => _navigation?.IsNavigating ?? false;

        /// <summary>Realtime seconds when the last navigation completed. -1 if never.</summary>
        public float LastNavigationTime => _navigation?.LastNavigationTime ?? -1f;

        public bool CanStepBack => _navigation?.CanStepBack ?? false;
        public bool CanStepForward => _navigation?.CanStepForward ?? false;

        /// <summary>
        /// Loads a machine package and starts a new session.
        /// If <paramref name="restoreStepCount"/> is greater than zero the session
        /// fast-forwards to that step boundary instead of starting at step 1.
        /// Returns true if the session started successfully.
        /// </summary>
        public async Task<bool> StartSessionAsync(
            string packageId,
            SessionMode mode,
            int restoreStepCount = 0,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                OseLog.Error(OseErrorCode.SessionStartFailed, "[MachineSessionController] Package id is required.");
                return false;
            }

            if (_isLoading)
            {
                OseLog.Warn(OseErrorCode.SessionStartFailed, "[MachineSessionController] Session load already in progress — ignoring concurrent request.");
                return false;
            }

            _isLoading = true;
            try
            {

            // Clean up any previous session
            EndSession();

            // Initialize session state
            _sessionState = new MachineSessionState
            {
                MachineId = packageId,
                Mode = mode,
                Lifecycle = SessionLifecycle.Uninitialized
            };

            // Stamp all log output with a short session ID so multi-session logs are filterable.
            string sessionTag = $"{packageId}/{System.Guid.NewGuid().ToString("N")[..8]}";
            OseLog.SetSessionTag(sessionTag);

            SetLifecycle(SessionLifecycle.Initializing);

            // Load the package
            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId, cancellationToken);
            if (!result.IsSuccess)
            {
                OseLog.Error(OseErrorCode.PackageLoadFailed,
                    $"[MachineSessionController] Failed to load package '{packageId}': {result.ErrorMessage}");
                SetLifecycle(SessionLifecycle.Error);
                return false;
            }

            _package = result.Package;
            _sessionState.MachineVersion = _package.packageVersion ?? string.Empty;
            _sessionState.StepStructureHash = _package.StepStructureHash;
            _sessionState.ChallengeActive = ResolveChallengeActive(mode, _package);

            // Determine assembly order
            _assemblyOrder = ResolveAssemblyOrder();
            if (_assemblyOrder.Length == 0)
            {
                OseLog.Error(OseErrorCode.PackageValidationFailed,
                    $"[MachineSessionController] Package '{packageId}' has no assemblies to run.");
                SetLifecycle(SessionLifecycle.Error);
                return false;
            }

            // Create child controllers
            _assemblyController = new AssemblyRuntimeController();
            _assemblyController.Initialize(_package, () => _navigation?.IsNavigating ?? false);
            _assemblyController.OnAssemblyCompleted += HandleAssemblyCompleted;

            // Initialize part runtime controller if registered
            if (ServiceRegistry.TryGet<IPartRuntimeController>(out _partController))
            {
                _partController.Initialize(_package);
            }

            if (ServiceRegistry.TryGet<IToolRuntimeController>(out _toolController))
            {
                _toolController.Initialize(_package);
            }

            // Subscribe to step events to keep session state current
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<HintRequested>(HandleHintRequested);
            RuntimeEventBus.Subscribe<ToolActionFailed>(HandleToolActionFailed);

            _currentAssemblyIndex = 0;
            _navigation = new SessionNavigationController(this);
            SetLifecycle(SessionLifecycle.SessionActive);

            // Notify listeners before any step events fire
            PackageReady?.Invoke(_package);

            // Begin the first assembly — restore path skips directly to the
            // saved step boundary so step 1 is never spuriously activated.
            if (restoreStepCount > 0)
                BeginCurrentAssemblyRestored(restoreStepCount);
            else
                BeginCurrentAssembly();

            return true;

            }
            finally
            {
                _isLoading = false;
            }
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

            // Flush metrics (hints, mistakes, timing) that accumulated since
            // the last step completion so they survive a crash or force-quit.
            AutoSave();

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

            OseLog.SetSessionTag(null);
            FlushPersistenceSnapshot();

            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<HintRequested>(HandleHintRequested);
            RuntimeEventBus.Unsubscribe<ToolActionFailed>(HandleToolActionFailed);

            if (_partController != null)
            {
                _partController.Dispose();
                _partController = null;
            }

            if (_toolController != null)
            {
                _toolController.Dispose();
                _toolController = null;
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

        public void FlushPersistenceSnapshot()
        {
            if (_sessionState == null)
                return;

            if (_sessionState.Lifecycle == SessionLifecycle.Completed ||
                _sessionState.Lifecycle == SessionLifecycle.Completing)
            {
                return;
            }

            AutoSave();
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
            if (_assemblyOrder == null || _currentAssemblyIndex >= _assemblyOrder.Length)
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

        /// <summary>
        /// Starts the first assembly at a restored step boundary.
        /// Uses RestoreAssemblyState so the progression cursor skips forward
        /// before any step is activated, then bulk-completes parts and publishes
        /// SessionRestored for the visual layer.
        /// </summary>
        private void BeginCurrentAssemblyRestored(int completedStepCount)
        {
            if (_assemblyOrder == null || _currentAssemblyIndex >= _assemblyOrder.Length)
            {
                CompleteSession();
                return;
            }

            if (!TryResolveRestoreCursor(completedStepCount, out string assemblyId, out int localCompletedStepCount, out StepDefinition[] completedGlobalSteps))
            {
                assemblyId = _assemblyOrder[_currentAssemblyIndex];
                localCompletedStepCount = completedStepCount;
                completedGlobalSteps = Array.Empty<StepDefinition>();
            }
            _sessionState.CurrentAssemblyId = assemblyId;
            _sessionState.IsRestored = true;
            _sessionState.CompletedStepCount = completedStepCount;

            SetLifecycle(SessionLifecycle.StepActive);

            OseLog.Info($"[MachineSessionController] Restoring session - completedGlobal={completedStepCount}, assembly='{assemblyId}', completedLocal={localCompletedStepCount}.");

            // RestoreAssemblyState skips the cursor then activates the target step
            _assemblyController.RestoreAssemblyState(assemblyId, localCompletedStepCount, () => _sessionState.ElapsedSeconds);

            // Bulk-complete parts for all globally completed steps
            if (_partController != null && completedGlobalSteps.Length > 0)
                _partController.BulkCompletePartsForSteps(completedGlobalSteps);

            // Notify visual layer so it can position completed parts
            RuntimeEventBus.Publish(new SessionRestored(completedStepCount));

            // Update session state with the active step id
            if (_assemblyController.StepController.HasActiveStep)
            {
                _sessionState.CurrentStepId = _assemblyController.StepController.CurrentStepState.StepId;
            }
        }

        private bool TryResolveRestoreCursor(
            int completedStepCount,
            out string assemblyId,
            out int localCompletedStepCount,
            out StepDefinition[] completedGlobalSteps)
        {
            assemblyId = null;
            localCompletedStepCount = 0;
            completedGlobalSteps = Array.Empty<StepDefinition>();

            StepDefinition[] orderedSteps = _package?.GetOrderedSteps() ?? Array.Empty<StepDefinition>();
            if (orderedSteps.Length == 0 || _assemblyOrder == null || _assemblyOrder.Length == 0)
                return false;

            int clampedCompleted = Math.Max(0, Math.Min(completedStepCount, orderedSteps.Length));
            if (clampedCompleted > 0)
            {
                completedGlobalSteps = new StepDefinition[clampedCompleted];
                Array.Copy(orderedSteps, completedGlobalSteps, clampedCompleted);
            }

            StepDefinition activeGlobalStep = clampedCompleted < orderedSteps.Length
                ? orderedSteps[clampedCompleted]
                : orderedSteps[orderedSteps.Length - 1];

            string resolvedAssemblyId = !string.IsNullOrWhiteSpace(activeGlobalStep?.assemblyId)
                ? activeGlobalStep.assemblyId
                : _assemblyOrder[Math.Min(_currentAssemblyIndex, _assemblyOrder.Length - 1)];

            int resolvedAssemblyIndex = Array.FindIndex(
                _assemblyOrder,
                id => string.Equals(id, resolvedAssemblyId, StringComparison.OrdinalIgnoreCase));

            _currentAssemblyIndex = resolvedAssemblyIndex >= 0 ? resolvedAssemblyIndex : 0;
            assemblyId = resolvedAssemblyId;
            localCompletedStepCount = SessionNavigationController.CountCompletedStepsForAssembly(orderedSteps, assemblyId, clampedCompleted);
            return true;
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
                // No AutoSave here — step activation is frequent and transient.
                // The state will be saved when the step completes or the session pauses.
            }
            else if (evt.Current == StepState.FailedAttempt)
            {
                _sessionState.MistakeCount++;
                // Metrics update only — persisted on next step completion or flush.
            }
            else if (evt.Current == StepState.Completed)
            {
                float duration = evt.AtSeconds - _sessionState.CurrentStepStartSeconds;
                if (duration < 0f) duration = 0f;

                _sessionState.LastStepDurationSeconds = duration;
                _sessionState.TotalStepDurationSeconds += duration;
                _sessionState.CurrentStepElapsedSeconds = duration;

                // Only count and save for first-time completions
                var progression = _assemblyController?.ProgressionController;
                if (progression != null && progression.LastAdvanceWasFirstTime)
                {
                    _sessionState.CompletedStepCount++;
                    AutoSave();
                }
            }
        }

        private void HandleHintRequested(HintRequested evt)
        {
            if (_sessionState == null)
                return;

            _sessionState.HintsUsed++;
            // Metrics update only — persisted on next step completion or flush.
        }

        private void HandleToolActionFailed(ToolActionFailed evt)
        {
            if (_sessionState == null)
                return;

            _sessionState.MistakeCount++;
            // Metrics update only — persisted on next step completion or flush.
        }

        private void HandleAssemblyCompleted(string assemblyId)
        {
            // During explicit navigation (skip-to-end, step forward/back),
            // suppress assembly advancement and session completion.
            if (_navigation != null && _navigation.IsNavigating)
            {
                OseLog.Info($"[MachineSessionController] Assembly '{assemblyId}' completed during navigation — suppressed.");
                return;
            }

            OseLog.Info($"[MachineSessionController] Assembly '{assemblyId}' completed. Checking for next assembly.");

            _currentAssemblyIndex++;
            if (_currentAssemblyIndex < _assemblyOrder.Length)
            {
                // Publish transition event so the UI can show an interstitial overlay.
                string completedName = null;
                if (_package != null && _package.TryGetAssembly(assemblyId, out var completedAssembly))
                    completedName = completedAssembly.name;

                string nextId = _assemblyOrder[_currentAssemblyIndex];
                string nextName = null;
                string nextDescription = null;
                string nextLearningFocus = null;
                if (_package != null && _package.TryGetAssembly(nextId, out var nextAssembly))
                {
                    nextName = nextAssembly.name;
                    nextDescription = nextAssembly.description;
                    nextLearningFocus = nextAssembly.learningFocus;
                }

                int completedStepsGlobal = 0;
                int totalStepsGlobal = 0;
                if (_package != null)
                {
                    var orderedSteps = _package.GetOrderedSteps();
                    totalStepsGlobal = orderedSteps?.Length ?? 0;
                    // Count steps belonging to assemblies up to and including the completed one
                    for (int i = 0; i < _currentAssemblyIndex && i < _assemblyOrder.Length; i++)
                    {
                        completedStepsGlobal += _package.GetStepsForAssembly(_assemblyOrder[i]).Length;
                    }
                }

                SetLifecycle(SessionLifecycle.AwaitingResume);
                RuntimeEventBus.Publish(new AssemblyTransitionRequested(
                    completedName ?? assemblyId,
                    nextId,
                    nextName ?? nextId,
                    nextDescription,
                    nextLearningFocus,
                    _currentAssemblyIndex, // 0-based index of next module (also count of completed modules)
                    _assemblyOrder.Length,
                    completedStepsGlobal,
                    totalStepsGlobal));
            }
            else
            {
                CompleteSession();
            }
        }

        public void ResumeAfterTransition()
        {
            if (_sessionState?.Lifecycle != SessionLifecycle.AwaitingResume)
                return;

            OseLog.Info($"[MachineSessionController] Transition dismissed — beginning assembly index {_currentAssemblyIndex}.");
            BeginCurrentAssembly();
        }

        private void AutoSave()
        {
            if (_sessionState == null) return;

            if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence))
                persistence.SaveSession(_sessionState);
        }

        /// <summary>
        /// Restores a previously saved session by advancing the progression cursor
        /// directly to the saved step, marking all skipped parts as Completed,
        /// and activating the next step normally.
        ///
        /// This avoids replaying the full event cascade (preview spawn/clear, tool
        /// action setup/teardown, visual updates) for every skipped step.
        /// </summary>
        public bool RestoreToStep(int completedStepCount)
        {
            if (_sessionState == null || _assemblyController == null)
                return false;

            var progression = _assemblyController.ProgressionController;
            if (progression == null || completedStepCount <= 0)
                return false;

            OseLog.Info($"[MachineSessionController] Restoring session — skipping {completedStepCount} completed steps.");

            _sessionState.IsRestored = true;
            _sessionState.CompletedStepCount = completedStepCount;

            // 1. Advance the progression cursor and collect skipped step definitions
            StepDefinition[] skippedSteps = progression.SkipToIndex(completedStepCount);
            if (skippedSteps.Length == 0)
                return false;

            // 2. Mark all parts from skipped steps as Completed (state only, no events)
            if (_partController != null)
                _partController.BulkCompletePartsForSteps(skippedSteps);

            // 3. Notify visual layer so it can position completed parts
            RuntimeEventBus.Publish(new SessionRestored(completedStepCount));

            // 4. Activate the current step normally — this fires a single
            //    StepStateChanged(Active) so all listeners set up correctly
            StepDefinition currentStep = progression.GetCurrentStep();
            if (currentStep != null)
            {
                _assemblyController.StepController.ActivateStep(currentStep, _sessionState.ElapsedSeconds);
            }
            else
            {
                // All steps were completed — session is done
                CompleteSession();
            }

            return true;
        }

        // ── Step Navigation (delegated to SessionNavigationController) ──

        /// <summary>
        /// Navigates one step backward. Parts from the current step revert to
        /// Available; parts from subsequent steps become NotIntroduced.
        /// </summary>
        public bool StepBack() => _navigation?.StepBack() ?? false;

        /// <summary>
        /// Navigates one step forward within the package-wide ordered step list.
        /// This is review/navigation behavior, not durable progression advancement.
        /// </summary>
        public bool StepForward() => _navigation?.StepForward() ?? false;

        /// <summary>Jumps directly to the last step, showing all parts at final positions.</summary>
        public bool NavigateToLastStep() => _navigation?.NavigateToLastStep() ?? false;

        /// <summary>Jumps to a specific global step index (0-based).</summary>
        public bool NavigateToGlobalStep(int globalIndex) => _navigation?.NavigateToGlobalStep(globalIndex) ?? false;

        private void CompleteSession()
        {
            float totalSeconds = _sessionState.ElapsedSeconds;
            string machineId = _sessionState.MachineId;

            SetLifecycle(SessionLifecycle.Completing);

            OseLog.Info($"[MachineSessionController] Session '{machineId}' completed in {totalSeconds:F1}s.");
            OseLog.Info(
                $"[SessionSummary] machine={machineId} " +
                $"mode={_sessionState.Mode} " +
                $"steps={_sessionState.CompletedStepCount} " +
                $"elapsed={totalSeconds:F1}s " +
                $"stepTime={_sessionState.TotalStepDurationSeconds:F1}s " +
                $"mistakes={_sessionState.MistakeCount} " +
                $"hints={_sessionState.HintsUsed} " +
                $"challenge={_sessionState.ChallengeActive}");

            // Clear saved progress — the session is done
            if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence))
            {
                persistence.ClearSession(machineId);
            }

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
