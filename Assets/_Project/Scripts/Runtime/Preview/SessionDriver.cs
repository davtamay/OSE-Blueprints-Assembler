using System;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Play-mode session bridge. Starts a <see cref="MachineSessionController"/> session,
    /// subscribes to runtime events, and drives the UI from live step transitions.
    ///
    /// Edit-mode step preview is handled by <see cref="EditModePreviewDriver"/>, which
    /// uses the same <see cref="PushStepAndPartToUI"/> path — so the editor preview is
    /// visually identical to what the trainee sees at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionDriver : MonoBehaviour
    {
        /// <summary>
        /// Published in both edit and play mode whenever a machine package finishes loading.
        /// The scene harness subscribes to apply per-package visual configuration without
        /// any ScriptableObject dependency — enabling the runtime package-runner model.
        /// </summary>
        /// <remarks>Prefer subscribing to <see cref="PackageLoaded"/> via <see cref="RuntimeEventBus"/>
        /// over this static event.</remarks>
        [Obsolete("Subscribe to RuntimeEventBus.Subscribe<PackageLoaded> instead.")]
        public static event Action<MachinePackageDefinition> PackageChanged;
        public static MachinePackageDefinition CurrentPackage { get; private set; }

        /// <summary>
        /// Fired in edit mode whenever the preview step sequence index changes.
        /// Subscribers (e.g. ToolTargetAuthoringWindow) use this to stay in sync.
        /// </summary>
        public static event Action<int> EditModeStepChanged;

        /// <summary>
        /// Fires <see cref="EditModeStepChanged"/>. Called by <see cref="EditModePreviewDriver"/>
        /// to notify authoring windows of a step change (C# events can only be invoked from
        /// within their declaring type).
        /// </summary>
        internal static void RaiseEditModeStepChanged(int sequenceIndex) =>
            EditModeStepChanged?.Invoke(sequenceIndex);

        /// <summary>
        /// True while the machine intro overlay is displayed.
        /// Checked by PartInteractionBridge to block 3D interaction during intro.
        /// </summary>
        public static bool IsIntroActive { get; private set; }

        [Header("Session Configuration")]
        [SerializeField] private string _packageId = "onboarding_tutorial";
        [SerializeField] private SessionMode _sessionMode = SessionMode.Tutorial;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private string _lifecycle = "—";
        [SerializeField] private string _currentAssemblyId = "—";
        [SerializeField] private string _currentStepId = "—";
        [Tooltip("1-based step number (1 = first step)")]
        [SerializeField] private int _stepNumber;
        [SerializeField] private int _totalSteps;
        [SerializeField] private float _elapsedSeconds;
        [SerializeField] private float _currentStepElapsedSeconds;
        [SerializeField] private float _lastStepDurationSeconds;
        [SerializeField] private int _completedStepCount;
        [SerializeField] private int _mistakeCount;
        [SerializeField] private int _hintsUsed;

        private IMachineSessionController _session;
        private bool _sessionStarted;
        private bool _introActive;
        private bool _introDismissed;
        private bool _pendingStepUiPush;
        private string _lastPlayModePackageId;
        private SessionMode _lastPlayModeSessionMode;

        // Persistence / restore
        private int _savedCompletedSteps;
        private int _savedTotalSteps;
        private string _savedMachineVersion;
        private string _savedStepStructureHash;

        // --------------------------------------------------------------------
        // Lifecycle
        // --------------------------------------------------------------------

        private void OnEnable() { }

        private void OnDisable()
        {
            // Reset session state on disable.
            // With Domain Reload disabled, OnDisable fires AFTER Application.isPlaying
            // becomes true during play mode entry. Unconditional reset ensures
            // _sessionStarted is cleared so StartSessionAsync can retry on re-enable.
            if (_session != null)
            {
                _session.PackageReady -= HandlePackageReady;
                RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
                RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
                RuntimeEventBus.Unsubscribe<AssemblyCompleted>(HandleAssemblyCompleted);
                RuntimeEventBus.Unsubscribe<SessionCompleted>(HandleSessionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionProgressed>(HandleToolActionProgressed);
                RuntimeEventBus.Unsubscribe<ToolActionCompleted>(HandleToolActionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionFailed>(HandleToolActionFailed);
                RuntimeEventBus.Unsubscribe<MachineIntroDismissed>(HandleIntroDismissed);
                RuntimeEventBus.Unsubscribe<MachineIntroReset>(HandleIntroReset);
                RuntimeEventBus.Unsubscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
                RuntimeEventBus.Unsubscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);
            }
            _sessionStarted = false;
            _introActive = false;
            _introDismissed = false;
            _pendingStepUiPush = false;
            IsIntroActive = false;
            _session = null;
        }

        private System.Collections.IEnumerator DeferredStartSession()
        {
            yield break; // unused — session start is now triggered from UpdatePlayMode
        }

        private void Update()
        {
            if (Application.isPlaying)
                UpdatePlayMode();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;

            // Detect package ID or mode change in play mode and restart session.
            if (_sessionStarted && _lastPlayModePackageId != _packageId)
            {
                _ = RestartSession();
                return;
            }

            if (_sessionStarted && _lastPlayModeSessionMode != _sessionMode)
            {
                OseLog.Info($"[SessionDriver] Session mode changed to {_sessionMode}. Restarting session.");
                _ = RestartSession();
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.PackageReady -= HandlePackageReady;
                RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
                RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
                RuntimeEventBus.Unsubscribe<AssemblyCompleted>(HandleAssemblyCompleted);
                RuntimeEventBus.Unsubscribe<SessionCompleted>(HandleSessionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionProgressed>(HandleToolActionProgressed);
                RuntimeEventBus.Unsubscribe<ToolActionCompleted>(HandleToolActionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionFailed>(HandleToolActionFailed);
                RuntimeEventBus.Unsubscribe<MachineIntroDismissed>(HandleIntroDismissed);
                RuntimeEventBus.Unsubscribe<MachineIntroReset>(HandleIntroReset);
                RuntimeEventBus.Unsubscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
                RuntimeEventBus.Unsubscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);
            }
        }

        // --------------------------------------------------------------------
        // Play Mode
        // --------------------------------------------------------------------

        private void UpdatePlayMode()
        {
            // Invariant: auto-start stays in Update, not OnEnable/Start. The UI/presentation
            // adapter is not reliably registered earlier when ExecuteAlways and no-domain-reload
            // are both active.
            if (!_sessionStarted || _session == null || _session.SessionState == null)
            {
                _ = StartSessionAsync();
                return;
            }

            if (_session == null)
                return;

            // Invariant: keep intro self-heal polling before step pushes. This recovers the
            // intro overlay when first-frame UI build order shifts.
            EnsureMachineIntroVisible();

            if (_pendingStepUiPush && !_introActive && PushStepToUI())
            {
                _pendingStepUiPush = false;
            }

            _session.TickElapsed(Time.deltaTime);
            RefreshInspectorState();
            PushChallengeMetricsToUI();
        }

        [ContextMenu("Start Session")]
        public async Task StartSessionFromMenu()
        {
            await StartSessionAsync();
        }

        [ContextMenu("Complete Current Step")]
        public void CompleteCurrentStep()
        {
            if (_session?.AssemblyController?.StepController == null)
            {
                OseLog.Warn("[SessionDriver] No active step to complete.");
                return;
            }

            var stepController = _session.AssemblyController.StepController;
            if (!stepController.HasActiveStep)
            {
                OseLog.Warn("[SessionDriver] Step is not active.");
                return;
            }

            OseLog.Info($"[SessionDriver] Manually completing step '{stepController.CurrentStepState.StepId}'.");
            stepController.CompleteStep(_session.GetElapsedSeconds());
        }

        [ContextMenu("Pause Session")]
        public void PauseSession()
        {
            _session?.PauseSession();
        }

        [ContextMenu("Resume Session")]
        public void ResumeSession()
        {
            _session?.ResumeSession();
        }

        [ContextMenu("End Session")]
        public void EndSession()
        {
            _session?.EndSession();
            RefreshInspectorState();
        }

        /// <summary>
        /// Restarts the session with the current _packageId.
        /// Use this after changing the Package Id field in play mode.
        /// </summary>
        [ContextMenu("Restart Session (Switch Package)")]
        public async Task RestartSession(bool clearSavedProgress = false)
        {
            if (!Application.isPlaying) return;

            // End previous session
            if (_session != null)
            {
                _session.PackageReady -= HandlePackageReady;
                RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
                RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
                RuntimeEventBus.Unsubscribe<AssemblyCompleted>(HandleAssemblyCompleted);
                RuntimeEventBus.Unsubscribe<SessionCompleted>(HandleSessionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionProgressed>(HandleToolActionProgressed);
                RuntimeEventBus.Unsubscribe<ToolActionCompleted>(HandleToolActionCompleted);
                RuntimeEventBus.Unsubscribe<ToolActionFailed>(HandleToolActionFailed);
                RuntimeEventBus.Unsubscribe<MachineIntroDismissed>(HandleIntroDismissed);
                RuntimeEventBus.Unsubscribe<MachineIntroReset>(HandleIntroReset);
                RuntimeEventBus.Unsubscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
                RuntimeEventBus.Unsubscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);
                _session.EndSession();
                _session = null;
            }

            // Clear saved progress AFTER EndSession (which flushes persistence)
            // so the flush doesn't re-save what we just cleared.
            if (clearSavedProgress && ServiceRegistry.TryGet<IPersistenceService>(out var persistence))
                persistence.ClearSession(_packageId);

            _sessionStarted = false;
            _introActive = false;
            _introDismissed = false;
            _pendingStepUiPush = false;
            IsIntroActive = false;
            _savedCompletedSteps = 0;
            _savedTotalSteps = 0;
            _savedMachineVersion = null;
            _savedStepStructureHash = null;

            OseLog.Info($"[SessionDriver] Restarting session with package '{_packageId}'.");
            await StartSessionAsync();
        }

        private async Task StartSessionAsync()
        {
            if (_sessionStarted)
                return;

            _sessionStarted = true;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out _session))
            {
                // Transient — Bootstrap may not have registered the controller yet.
                // Reset so UpdatePlayMode can retry next frame.
                OseLog.Warn("[SessionDriver] MachineSessionController not in ServiceRegistry yet — will retry.");
                _lifecycle = "Waiting for session controller...";
                _sessionStarted = false;
                _session = null;
                return;
            }

            // Invariant: PackageReady stays subscribed before StartSessionAsync, and the rest of
            // the runtime events stay subscribed after the await. Changing that ordering caused
            // step UI pushes to race ahead of intro activation.
            _session.PackageReady += HandlePackageReady;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                ui.SetSessionMode(_sessionMode);
                ui.ResetMachineIntroState();
            }

            // Detect saved progress BEFORE starting the session so the restore
            // step count can be passed in. This ensures the session controller
            // skips directly to the saved step boundary instead of activating
            // step 1 first and patching up afterward.
            _savedCompletedSteps = 0;
            _savedTotalSteps = 0;
            if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence)
                && persistence.HasSavedSession(_packageId))
            {
                var saved = persistence.LoadSession(_packageId);
                if (saved != null && saved.CompletedStepCount > 0)
                {
                    // Version guard: discard saved progress if the package changed
                    string savedVersion = saved.MachineVersion ?? string.Empty;
                    string savedHash = saved.StepStructureHash ?? string.Empty;
                    // We cannot check package version yet (not loaded), so store
                    // the saved version/hash and validate after load below.
                    _savedCompletedSteps = saved.CompletedStepCount;
                    _savedMachineVersion = savedVersion;
                    _savedStepStructureHash = savedHash;
                }
            }

            OseLog.Info($"[SessionDriver] Starting session for '{_packageId}' in {_sessionMode} mode" +
                (_savedCompletedSteps > 0 ? $" (restoring {_savedCompletedSteps} steps)" : "") + ".");

            bool success = await _session.StartSessionAsync(_packageId, _sessionMode, _savedCompletedSteps);

            // Subscribe to runtime events now that startup step events have already fired.
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<AssemblyCompleted>(HandleAssemblyCompleted);
            RuntimeEventBus.Subscribe<SessionCompleted>(HandleSessionCompleted);
            RuntimeEventBus.Subscribe<ToolActionProgressed>(HandleToolActionProgressed);
            RuntimeEventBus.Subscribe<ToolActionCompleted>(HandleToolActionCompleted);
            RuntimeEventBus.Subscribe<ToolActionFailed>(HandleToolActionFailed);
            RuntimeEventBus.Subscribe<MachineIntroDismissed>(HandleIntroDismissed);
            RuntimeEventBus.Subscribe<MachineIntroReset>(HandleIntroReset);
            RuntimeEventBus.Subscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
            RuntimeEventBus.Subscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);

            if (success)
            {
                _lastPlayModePackageId = _packageId;
                _lastPlayModeSessionMode = _sessionMode;
                _introDismissed = false;
                _pendingStepUiPush = false;

                // Resolve total steps now that the package is loaded
                _savedTotalSteps = _session.Package?.GetOrderedSteps().Length ?? 0;

                // Structure guard: if the step structure changed (steps added, removed,
                // or reordered), the saved CompletedStepCount is invalid. Auto-discard
                // and restart fresh to prevent silent navigation bugs.
                if (_savedCompletedSteps > 0 && _session.Package != null)
                {
                    string currentHash = _session.Package.StepStructureHash;
                    bool hashMismatch = !string.IsNullOrEmpty(_savedStepStructureHash)
                        && _savedStepStructureHash != currentHash;

                    string currentVersion = _session.Package.packageVersion ?? string.Empty;
                    bool versionMismatch = !string.IsNullOrEmpty(_savedMachineVersion)
                        && _savedMachineVersion != currentVersion;

                    if (hashMismatch)
                    {
                        OseLog.Warn($"[SessionDriver] Step structure changed (saved hash='{_savedStepStructureHash}', " +
                            $"current='{currentHash}'). Discarding stale saved progress and restarting fresh.");

                        if (ServiceRegistry.TryGet<IPersistenceService>(out var p))
                            p.ClearSession(_packageId);

                        _savedCompletedSteps = 0;
                        _savedStepStructureHash = null;
                        _ = RestartSession(clearSavedProgress: true);
                        return;
                    }

                    if (versionMismatch)
                    {
                        OseLog.Warn($"[SessionDriver] Package version changed (saved='{_savedMachineVersion}', " +
                            $"current='{currentVersion}'). Saved progress may be invalid.");
                    }
                }

                TryShowMachineIntro();

                // If the package has no machine definition (no intro possible), show step directly.
                if (_session?.Package?.machine == null)
                    _pendingStepUiPush = !PushStepToUI();
            }
            else
            {
                _lifecycle = "ERROR: Session failed to start";
                OseLog.Error($"[SessionDriver] Session failed to start for '{_packageId}'.");
                // Reset so Restart Session or package-ID change can retry cleanly.
                _sessionStarted = false;
            }

            RefreshInspectorState();
        }

        // --------------------------------------------------------------------
        // Runtime Event Handlers
        // --------------------------------------------------------------------

        private void HandlePackageReady(MachinePackageDefinition package)
        {
            PublishPackageChanged(package);
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            RefreshInspectorState();

            if (evt.Current == StepState.Active)
            {
                if (!_introActive)
                {
                    _pendingStepUiPush = !PushStepToUI();
                }
            }
            else if (evt.Current == StepState.Completed)
            {
                if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                {
                    ui.ShowStepCompletionToast("Step Complete!");
                }
            }
        }

        private void HandlePartStateChanged(PartStateChanged evt)
        {
            if (evt.Current != PartPlacementState.Selected && evt.Current != PartPlacementState.Inspected)
                return;

            // Push part info to UI when a part is selected through the runtime controller
            if (_session?.Package == null)
                return;

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            if (!_session.Package.TryGetPart(evt.PartId, out PartDefinition part))
                return;

            ui.ShowPartInfoShell(
                part.GetDisplayName(),
                part.function ?? string.Empty,
                part.material ?? string.Empty,
                StepUiContentUtility.ResolveToolNames(_session.Package, part.toolIds),
                StepUiContentUtility.JoinStrings(part.searchTerms));
        }

        private void HandleAssemblyCompleted(AssemblyCompleted evt)
        {
            OseLog.Info($"[SessionDriver] Assembly '{evt.AssemblyId}' completed.");
            RefreshInspectorState();
        }

        private void HandleSessionCompleted(SessionCompleted evt)
        {
            OseLog.Info($"[SessionDriver] Session '{evt.MachineId}' completed in {evt.TotalSeconds:F1}s.");
            _pendingStepUiPush = false;
            RefreshInspectorState();

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                int minutes = (int)(evt.TotalSeconds / 60f);
                int secs = (int)(evt.TotalSeconds % 60f);
                string timeStr = minutes > 0 ? $"{minutes}m {secs}s" : $"{secs}s";
                string machineName = _session?.Package?.machine?.GetDisplayName() ?? "Assembly";
                ui.ShowMilestoneFeedback($"{machineName} Complete! ({timeStr})");
            }
        }

        private void HandleToolActionProgressed(ToolActionProgressed evt)
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            if (!string.IsNullOrWhiteSpace(evt.Message))
            {
                ui.ShowStepCompletionToast(evt.Message);
            }
        }

        private void HandleToolActionCompleted(ToolActionCompleted evt)
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            string message = string.IsNullOrWhiteSpace(evt.Message)
                ? "Tool action complete."
                : evt.Message;
            ui.ShowStepCompletionToast(message);
        }

        private void HandleToolActionFailed(ToolActionFailed evt)
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            string message = string.IsNullOrWhiteSpace(evt.Message)
                ? "Tool action failed."
                : evt.Message;

            // Show both a toast (prominent, immediate) and a hint panel (persistent context).
            ui.ShowStepCompletionToast(message);
            ui.ShowHintContent("Tool Check", message, "tool");
        }

        private void TryShowMachineIntro()
        {
            if (_session?.Package?.machine == null)
            {
                OseLog.Warn("[SessionDriver] TryShowMachineIntro: no machine data.");
                _introActive = false;
                IsIntroActive = false;
                return;
            }

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                OseLog.Error("[SessionDriver] TryShowMachineIntro: IPresentationAdapter not registered.");
                _introActive = false;
                IsIntroActive = false;
                return;
            }

            MachineDefinition machine = _session.Package.machine;

            int totalSteps = _savedTotalSteps > 0
                ? _savedTotalSteps
                : (_session.Package?.GetOrderedSteps().Length ?? 0);

            ui.ShowMachineIntro(
                machine.GetDisplayName(),
                machine.description ?? string.Empty,
                machine.difficulty ?? string.Empty,
                machine.estimatedBuildTimeMinutes,
                machine.learningObjectives,
                machine.introImageRef,
                _savedCompletedSteps,
                totalSteps);

            _introActive = ui.IsMachineIntroVisible;
            IsIntroActive = _introActive;
        }

        private void HandleIntroReset(MachineIntroReset evt)
        {
            _introActive = false;
            _introDismissed = false;
            IsIntroActive = false;
            _savedCompletedSteps = 0;
            _savedTotalSteps = 0;
            _savedMachineVersion = null;
            _savedStepStructureHash = null;

            OseLog.Info("[SessionDriver] Progress reset. Restarting session.");
            _ = RestartSession(clearSavedProgress: true);
        }

        private void HandleAssemblyPickerRequested(AssemblyPickerRequested evt)
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            ui.ShowAssemblyPicker();
        }

        private void HandleAssemblyPickerDismissed(AssemblyPickerDismissed evt)
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                ui.DismissAssemblyPicker();

            if (!string.IsNullOrEmpty(evt.SelectedAssemblyId) && evt.GlobalStepIndex >= 0 && _session != null)
            {
                OseLog.Info($"[SessionDriver] Assembly picker: jumping to '{evt.SelectedAssemblyId}' (global step {evt.GlobalStepIndex}).");
                _session.NavigateToGlobalStep(evt.GlobalStepIndex);
            }

            // Ensure step UI is pushed after picker dismissal (handles the intro→picker→dismiss flow).
            _introDismissed = true;
            _introActive = false;
            IsIntroActive = false;
            _pendingStepUiPush = !PushStepToUI();
        }

        private void HandleIntroDismissed(MachineIntroDismissed evt)
        {
            _introActive = false;
            _introDismissed = true;
            _pendingStepUiPush = true;
            IsIntroActive = false;

            // Restore already happened during StartSessionAsync — the session
            // controller skipped directly to the saved step boundary before any
            // step was activated. No deferred RestoreToStep needed here.

            OseLog.Info("[SessionDriver] Machine intro dismissed. Pushing step UI.");
            if (PushStepToUI())
            {
                _pendingStepUiPush = false;
            }
        }

        private void EnsureMachineIntroVisible()
        {
            if (_introDismissed || _session?.Package?.machine == null)
                return;

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            if (ui.IsMachineIntroVisible)
            {
                _introActive = true;
                IsIntroActive = true;
                return;
            }

            TryShowMachineIntro();
        }

        private bool PushStepToUI()
        {
            if (_session?.Package == null)
                return false;

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return false;

            var stepController = _session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null)
                return false;

            StepDefinition[] orderedSteps = _session.Package.GetOrderedSteps();
            int totalSteps = orderedSteps.Length;
            int stepNumber = StepUiContentUtility.ResolveDisplayStepNumber(orderedSteps, step);

            if (stepNumber <= 0)
            {
                var progression = _session.AssemblyController.ProgressionController;
                if (progression == null)
                    return false;

                stepNumber = progression.CurrentStepIndex + 1;
                totalSteps = progression.TotalSteps;
            }

            PushStepAndPartToUI(ui, _session.Package, step,
                stepNumber, totalSteps);

            return true;
        }

        internal static void PublishPackageChanged(MachinePackageDefinition package)
        {
            CurrentPackage = package;
#pragma warning disable CS0618 // kept for any legacy subscribers still on the old event
            PackageChanged?.Invoke(package);
#pragma warning restore CS0618
            RuntimeEventBus.Publish(new PackageLoaded(package?.packageId));
        }

        // --------------------------------------------------------------------
        // Shared UI Push (used by both SessionDriver and EditModePreviewDriver)
        // --------------------------------------------------------------------

        internal static void PushStepAndPartToUI(
            IPresentationAdapter ui,
            MachinePackageDefinition package,
            StepDefinition step,
            int stepNumber,
            int totalSteps)
        {
            StepUiContentUtility.StepShellContent stepShell = StepUiContentUtility.BuildStepShellContent(step);

            ui.ShowStepShell(
                stepNumber,
                totalSteps,
                stepShell.Title,
                stepShell.Instruction,
                stepShell.ShowConfirmButton,
                stepShell.ShowHintButton,
                stepShell.ConfirmGate);

            // Only auto-push part info for required parts.
            // Optional parts should only show info when the user selects them.
            // For preview/runtime bootstrap, keep the existing fallback payload when no
            // required part is authored so the shell still shows useful context.
            StepUiContentUtility.PartInfoShellContent partInfo =
                StepUiContentUtility.BuildStepPartInfoShellContent(package, step, includeFallbackWhenNoRequiredPart: true);
            if (partInfo.HasContent)
            {
                ui.ShowPartInfoShell(
                    partInfo.PartName,
                    partInfo.Function,
                    partInfo.Material,
                    partInfo.Tool,
                    partInfo.SearchTerms);
            }

            ui.ShowProgressUpdate(stepNumber > 0 ? stepNumber - 1 : 0, totalSteps);
        }

        // --------------------------------------------------------------------
        // Inspector State
        // --------------------------------------------------------------------

        private void RefreshInspectorState()
        {
            MachineSessionState state = _session?.SessionState;
            if (state == null)
            {
                _lifecycle = "No session";
                _currentAssemblyId = "—";
                _currentStepId = "—";
                _stepNumber = 0;
                _totalSteps = 0;
                _elapsedSeconds = 0f;
                _currentStepElapsedSeconds = 0f;
                _lastStepDurationSeconds = 0f;
                _completedStepCount = 0;
                _mistakeCount = 0;
                _hintsUsed = 0;
                return;
            }

            _lifecycle = state.Lifecycle.ToString();
            _currentAssemblyId = state.CurrentAssemblyId ?? "—";
            _currentStepId = state.CurrentStepId ?? "—";
            _elapsedSeconds = state.ElapsedSeconds;
            _currentStepElapsedSeconds = state.CurrentStepElapsedSeconds;
            _lastStepDurationSeconds = state.LastStepDurationSeconds;
            _completedStepCount = state.CompletedStepCount;
            _mistakeCount = state.MistakeCount;
            _hintsUsed = state.HintsUsed;

            var progression = _session.AssemblyController?.ProgressionController;
            if (progression != null)
            {
                _stepNumber = progression.CurrentStepIndex + 1;
                _totalSteps = progression.TotalSteps;
            }
        }

        private void PushChallengeMetricsToUI()
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            MachineSessionState state = _session?.SessionState;
            if (state == null)
                return;

            ui.ShowChallengeMetrics(
                state.HintsUsed,
                state.MistakeCount,
                state.CurrentStepElapsedSeconds,
                state.ElapsedSeconds,
                state.ChallengeActive);
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static bool ResolveChallengeActive(SessionMode mode, MachinePackageDefinition package)
        {
            if (mode != SessionMode.Challenge)
                return false;

            if (package?.challengeConfig != null)
                return package.challengeConfig.enabled;

            return true;
        }
    }
}
