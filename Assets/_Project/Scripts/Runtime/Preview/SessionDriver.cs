using System;
using System.Text;
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

        [Header("Session Configuration")]
        [SerializeField] private string _packageId = "onboarding_tutorial";
        [SerializeField] private SessionMode _sessionMode = SessionMode.Tutorial;
        [SerializeField] private bool _autoStartOnPlay = true;

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
        private string _lastPlayModePackageId;
        private SessionMode _lastPlayModeSessionMode;

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
            }
        }

        private void Start()
        {
            if (Application.isPlaying && _autoStartOnPlay)
            {
                _ = StartSessionAsync();
            }
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
            }
        }

        // --------------------------------------------------------------------
        // Play Mode
        // --------------------------------------------------------------------

        private void UpdatePlayMode()
        {
            if (_session == null)
                return;

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
                _session.EndSession();
                _session = null;
            }
            _sessionStarted = false;

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
                OseLog.Error("[SessionDriver] MachineSessionController not found in ServiceRegistry. Is AppBootstrap present?");
                _lifecycle = "ERROR: No session controller";
                return;
            }

            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
            RuntimeEventBus.Subscribe<AssemblyCompleted>(HandleAssemblyCompleted);
            RuntimeEventBus.Subscribe<SessionCompleted>(HandleSessionCompleted);

            // Fire PackageChanged before step events so scene objects exist in time
            _session.PackageReady += HandlePackageReady;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                ui.SetSessionMode(_sessionMode);
            }

            OseLog.Info($"[SessionDriver] Starting session for '{_packageId}' in {_sessionMode} mode.");

            bool success = await _session.StartSessionAsync(_packageId, _sessionMode);

            if (success)
            {
                _lastPlayModePackageId = _packageId;
                _lastPlayModeSessionMode = _sessionMode;
            }
            else
            {
                _lifecycle = "ERROR: Session failed to start";
                OseLog.Error($"[SessionDriver] Session failed to start for '{_packageId}'.");
            }

            RefreshInspectorState();
        }

        // --------------------------------------------------------------------
        // Runtime Event Handlers
        // --------------------------------------------------------------------

        private void HandlePackageReady(MachinePackageDefinition package)
        {
            PackageChanged?.Invoke(package);
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            RefreshInspectorState();

            if (evt.Current == StepState.Active)
            {
                PushStepToUI();
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
                ResolveToolNames(_session.Package, part.toolIds),
                JoinStrings(part.searchTerms));
        }

        private void HandleAssemblyCompleted(AssemblyCompleted evt)
        {
            OseLog.Info($"[SessionDriver] Assembly '{evt.AssemblyId}' completed.");
            RefreshInspectorState();
        }

        private void HandleSessionCompleted(SessionCompleted evt)
        {
            OseLog.Info($"[SessionDriver] Session '{evt.MachineId}' completed in {evt.TotalSeconds:F1}s.");
            RefreshInspectorState();

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                int minutes = (int)(evt.TotalSeconds / 60f);
                int secs = (int)(evt.TotalSeconds % 60f);
                string timeStr = minutes > 0 ? $"{minutes}m {secs}s" : $"{secs}s";
                ui.ShowMilestoneFeedback($"Tutorial Complete! ({timeStr})");
            }
        }

        private void PushStepToUI()
        {
            if (_session?.Package == null)
                return;

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                return;

            var stepController = _session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            var progression = _session.AssemblyController.ProgressionController;

            PushStepAndPartToUI(ui, _session.Package, step,
                progression.CurrentStepIndex + 1, progression.TotalSteps);
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
            PackageChanged?.Invoke(_editModePackage);

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
            bool showConfirm = string.Equals(step.completionMode, "confirmation_only",
                StringComparison.OrdinalIgnoreCase);

            ConfirmGate gate = ConfirmGate.None;
            if (showConfirm)
                gate = ResolveConfirmGate(step);

            // Only show the hint button when hint interaction is the teaching goal
            bool showHintButton = gate == ConfirmGate.RequestHint;

            ui.ShowStepShell(
                stepNumber,
                totalSteps,
                step.GetDisplayName(),
                step.BuildInstructionBody(),
                showConfirm,
                showHintButton,
                gate);

            // Only auto-push part info for required parts.
            // Optional parts should only show info when the user selects them.
            string partId = GetFirstNonEmpty(step.requiredPartIds);

            if (!string.IsNullOrEmpty(partId) && package.TryGetPart(partId, out PartDefinition part))
            {
                ui.ShowPartInfoShell(
                    part.GetDisplayName(),
                    part.function ?? string.Empty,
                    part.material ?? string.Empty,
                    ResolveToolNames(package, step.relevantToolIds ?? part.toolIds),
                    JoinStrings(part.searchTerms));
            }
            else
            {
                ui.ShowPartInfoShell(
                    "No part referenced",
                    step.instructionText ?? string.Empty,
                    string.Empty,
                    ResolveToolNames(package, step.relevantToolIds),
                    string.Empty);
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

        private static ConfirmGate ResolveConfirmGate(StepDefinition step)
        {
            string[] tags = step.eventTags;
            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (string.Equals(tags[i], "select", StringComparison.OrdinalIgnoreCase))
                        return ConfirmGate.SelectPart;
                    if (string.Equals(tags[i], "hint", StringComparison.OrdinalIgnoreCase))
                        return ConfirmGate.RequestHint;
                }
            }
            return ConfirmGate.None;
        }

        private static StepDefinition ResolveStepBySequenceIndex(StepDefinition[] orderedSteps, int sequenceIndex)
        {
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null && orderedSteps[i].sequenceIndex == sequenceIndex)
                    return orderedSteps[i];
            }

            return orderedSteps[0];
        }

        private static string GetFirstNonEmpty(string[] values)
        {
            if (values == null) return null;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }
            return null;
        }

        private static string ResolveToolNames(MachinePackageDefinition package, string[] toolIds)
        {
            if (toolIds == null || toolIds.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < toolIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(toolIds[i]))
                    continue;
                if (package.TryGetTool(toolIds[i], out ToolDefinition tool))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(tool.GetDisplayName());
                }
            }
            return sb.ToString();
        }

        private static string JoinStrings(string[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                    continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(values[i].Trim());
            }
            return sb.ToString();
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
