using System;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Unified scene bridge for both edit-mode content preview and play-mode
    /// runtime sessions. In edit mode it loads a machine package and pushes a
    /// selected step into the UI panels for visual authoring feedback. In play
    /// mode it starts a MachineSessionController session, subscribes to runtime
    /// events, and drives the UI from live step transitions.
    /// This is a preview/test harness component, not the future runtime authority.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SessionDriver : MonoBehaviour
    {
        /// <summary>
        /// Published in both edit and play mode whenever a machine package finishes loading.
        /// The scene harness subscribes to apply per-package visual configuration without
        /// any ScriptableObject dependency — enabling the runtime package-runner model.
        /// </summary>
        public static event Action<MachinePackageDefinition> PackageChanged;
        public static MachinePackageDefinition CurrentPackage { get; private set; }

        /// <summary>
        /// True while the machine intro overlay is displayed.
        /// Checked by PartInteractionBridge to block 3D interaction during intro.
        /// </summary>
        public static bool IsIntroActive { get; private set; }

        [Header("Session Configuration")]
        [SerializeField] private string _packageId = "onboarding_tutorial";
        [SerializeField] private SessionMode _sessionMode = SessionMode.Tutorial;

        [Header("Edit Mode Preview")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField, Min(1)] private int _previewStepSequenceIndex = 1;

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

        private MachineSessionController _session;
        private bool _sessionStarted;
        private bool _introActive;
        private bool _introDismissed;
        private bool _pendingStepUiPush;
        private string _lastPlayModePackageId;
        private SessionMode _lastPlayModeSessionMode;

        // Persistence / restore
        private int _savedCompletedSteps;
        private int _savedTotalSteps;

        // Edit-mode preview state
        private readonly MachinePackageLoader _loader = new MachinePackageLoader();
        private MachinePackageDefinition _editModePackage;
        private bool _editModePreviewApplied;
        private int _editModeLoadVersion;

        // --------------------------------------------------------------------
        // Lifecycle
        // --------------------------------------------------------------------

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                _editModePreviewApplied = false;
                RequestEditModeRefresh();

                HidePreviewIfPossible();
            }

        }

        private void OnDisable()
        {
            // Always reset session state when disabled, regardless of play/edit mode.
            // With Domain Reload disabled and [ExecuteAlways], OnDisable fires AFTER
            // Application.isPlaying becomes true during play mode entry, so guarding
            // with !Application.isPlaying would silently skip the reset and leave
            // _sessionStarted=true from the previous session, blocking StartSessionAsync.
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
            }
            _sessionStarted = false;
            _introActive = false;
            _introDismissed = false;
            _pendingStepUiPush = false;
            IsIntroActive = false;
            _session = null;
            _editModePreviewApplied = false;
        }

        private void Start()
        {
            HidePreviewIfPossible();
        }

        private System.Collections.IEnumerator DeferredStartSession()
        {
            yield break; // unused — session start is now triggered from UpdatePlayMode
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                UpdatePlayMode();
            }
            else
            {
                UpdateEditMode();
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
                return;

            if (Application.isPlaying)
            {
                // Detect package ID change in play mode and restart session
                if (_sessionStarted && _lastPlayModePackageId != _packageId)
                {
                    RestartSession();
                    return;
                }

                if (_sessionStarted && _lastPlayModeSessionMode != _sessionMode)
                {
                    OseLog.Info($"[SessionDriver] Session mode changed to {_sessionMode}. Restarting session.");
                    RestartSession();
                }
            }
            else
            {
                _editModePreviewApplied = false;
                RequestEditModeRefresh();
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
        public async void StartSessionFromMenu()
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
        public async void RestartSession()
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
                _session.EndSession();
                _session = null;
            }
            _sessionStarted = false;
            _introActive = false;
            _introDismissed = false;
            _pendingStepUiPush = false;
            IsIntroActive = false;
            _savedCompletedSteps = 0;
            _savedTotalSteps = 0;

            OseLog.Info($"[SessionDriver] Restarting session with package '{_packageId}'.");
            await StartSessionAsync();
        }

        private async Task StartSessionAsync()
        {
            if (_sessionStarted)
                return;

            _sessionStarted = true;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out _session))
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

            OseLog.Info($"[SessionDriver] Starting session for '{_packageId}' in {_sessionMode} mode.");

            bool success = await _session.StartSessionAsync(_packageId, _sessionMode);

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

            if (success)
            {
                _lastPlayModePackageId = _packageId;
                _lastPlayModeSessionMode = _sessionMode;
                _introDismissed = false;
                _pendingStepUiPush = false;

                // Check for saved progress
                _savedCompletedSteps = 0;
                _savedTotalSteps = 0;
                if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence)
                    && persistence.HasSavedSession(_packageId))
                {
                    var saved = persistence.LoadSession(_packageId);
                    if (saved != null && saved.CompletedStepCount > 0)
                    {
                        _savedCompletedSteps = saved.CompletedStepCount;
                        _savedTotalSteps = _session.Package?.GetOrderedSteps().Length ?? 0;
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

            // Clear saved progress using the actual package ID
            if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence))
            {
                persistence.ClearSession(_packageId);
            }

            OseLog.Info("[SessionDriver] Progress reset. Restarting session.");
            RestartSession();
        }

        private void HandleIntroDismissed(MachineIntroDismissed evt)
        {
            _introActive = false;
            _introDismissed = true;
            _pendingStepUiPush = true;
            IsIntroActive = false;

            // Restore saved progress if available
            if (_savedCompletedSteps > 0 && _session != null)
            {
                OseLog.Info($"[SessionDriver] Restoring {_savedCompletedSteps} completed steps.");
                _session.RestoreToStep(_savedCompletedSteps);
                _savedCompletedSteps = 0;
            }

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

        // --------------------------------------------------------------------
        // Edit Mode Preview
        // --------------------------------------------------------------------

        private void UpdateEditMode()
        {
            if (!_previewInEditMode)
            {
                HidePreviewIfPossible();
                return;
            }

            if (!_editModePreviewApplied)
            {
                TryApplyEditModePreview();
            }
        }

        private void RequestEditModeRefresh()
        {
            _editModePreviewApplied = false;

            if (!_previewInEditMode)
            {
                HidePreviewIfPossible();
                return;
            }

            _ = ReloadEditModePackageAsync(++_editModeLoadVersion);
        }

        private async Task ReloadEditModePackageAsync(int loadVersion)
        {
            string packageId = _packageId;
            if (string.IsNullOrWhiteSpace(packageId))
                return;

            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId);

            if (loadVersion != _editModeLoadVersion || !this)
                return;

            _editModePackage = result.Package;
            _editModePreviewApplied = false;
            PublishPackageChanged(_editModePackage);

            if (!result.IsSuccess)
            {
                OseLog.Warn($"[SessionDriver] Edit-mode preview failed to load '{packageId}': {result.ErrorMessage}");
            }

            TryApplyEditModePreview();
        }

        private void TryApplyEditModePreview()
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            ui.SetSessionMode(_sessionMode);
            ui.ShowChallengeMetrics(0, 0, 0f, 0f, ResolveChallengeActive(_sessionMode, _editModePackage));

            if (_editModePackage == null)
            {
                ui.ShowStepShell(0, 0, "Preview Unavailable", "Package not loaded.");
                _editModePreviewApplied = true;
                return;
            }

            StepDefinition[] orderedSteps = _editModePackage.GetOrderedSteps();
            if (orderedSteps.Length == 0)
            {
                ui.ShowStepShell(0, 0, "No Steps", "Package has no steps authored.");
                _editModePreviewApplied = true;
                return;
            }

            StepDefinition step = ResolveStepBySequenceIndex(orderedSteps, _previewStepSequenceIndex);

            PushStepAndPartToUI(ui, _editModePackage, step,
                step.sequenceIndex, orderedSteps.Length);

            _editModePreviewApplied = true;
        }

        private void HidePreviewIfPossible()
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                ui.HideAll();
            }
        }

        private static void PublishPackageChanged(MachinePackageDefinition package)
        {
            CurrentPackage = package;
            PackageChanged?.Invoke(package);
        }

        // --------------------------------------------------------------------
        // Shared UI Push (used by both edit and play mode)
        // --------------------------------------------------------------------

        private static void PushStepAndPartToUI(
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
        // External Notifications
        // --------------------------------------------------------------------

        /// <summary>
        /// Called by editor tooling (e.g. AssetPostprocessor) when a package's
        /// content files change on disk. If the active SessionDriver is
        /// showing this package in edit-mode preview, it triggers a reload.
        /// </summary>
        public static void NotifyPackageContentChanged(string packageId)
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(packageId))
                return;

            foreach (var driver in FindObjectsByType<SessionDriver>(FindObjectsSortMode.None))
            {
                if (!driver.isActiveAndEnabled)
                    continue;

                if (!string.Equals(driver._packageId, packageId, StringComparison.Ordinal))
                    continue;

                if (Application.isPlaying)
                    continue;

                OseLog.Info($"[SessionDriver] Package content changed for '{packageId}' — reloading preview.");
                driver.RequestEditModeRefresh();
            }
#endif
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static StepDefinition ResolveStepBySequenceIndex(StepDefinition[] orderedSteps, int sequenceIndex)
        {
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null && orderedSteps[i].sequenceIndex == sequenceIndex)
                    return orderedSteps[i];
            }

            return orderedSteps[0];
        }

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
