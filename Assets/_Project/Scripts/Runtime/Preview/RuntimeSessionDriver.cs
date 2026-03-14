using System.Text;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Scene bridge that starts a MachineSessionController session on Play
    /// and ticks the elapsed timer. Provides inspector visibility into runtime
    /// state and a context menu action to manually complete steps for testing.
    /// This is a preview/test harness component, not the future runtime authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeSessionDriver : MonoBehaviour
    {
        [Header("Session Configuration")]
        [SerializeField] private string _packageId = "tutorial_build";
        [SerializeField] private SessionMode _sessionMode = SessionMode.Guided;
        [SerializeField] private bool _autoStartOnPlay = true;

        [Header("Runtime State (Read Only)")]
        [SerializeField] private string _lifecycle = "—";
        [SerializeField] private string _currentAssemblyId = "—";
        [SerializeField] private string _currentStepId = "—";
        [SerializeField] private int _stepIndex;
        [SerializeField] private int _totalSteps;
        [SerializeField] private float _elapsedSeconds;
        [SerializeField] private int _mistakeCount;
        [SerializeField] private int _hintsUsed;

        private MachineSessionController _session;
        private bool _sessionStarted;

        private void Start()
        {
            if (_autoStartOnPlay)
            {
                _ = StartSessionAsync();
            }
        }

        private void Update()
        {
            if (_session == null)
                return;

            _session.TickElapsed(Time.deltaTime);
            RefreshInspectorState();
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
                RuntimeEventBus.Unsubscribe<AssemblyCompleted>(HandleAssemblyCompleted);
                RuntimeEventBus.Unsubscribe<SessionCompleted>(HandleSessionCompleted);
            }
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
                OseLog.Warn("[RuntimeSessionDriver] No active step to complete.");
                return;
            }

            var stepController = _session.AssemblyController.StepController;
            if (!stepController.HasActiveStep)
            {
                OseLog.Warn("[RuntimeSessionDriver] Step is not active.");
                return;
            }

            OseLog.Info($"[RuntimeSessionDriver] Manually completing step '{stepController.CurrentStepState.StepId}'.");
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

        private async Task StartSessionAsync()
        {
            if (_sessionStarted)
                return;

            _sessionStarted = true;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out _session))
            {
                OseLog.Error("[RuntimeSessionDriver] MachineSessionController not found in ServiceRegistry. Is AppBootstrap present?");
                _lifecycle = "ERROR: No session controller";
                return;
            }

            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<AssemblyCompleted>(HandleAssemblyCompleted);
            RuntimeEventBus.Subscribe<SessionCompleted>(HandleSessionCompleted);

            OseLog.Info($"[RuntimeSessionDriver] Starting session for '{_packageId}' in {_sessionMode} mode.");

            bool success = await _session.StartSessionAsync(_packageId, _sessionMode);

            if (!success)
            {
                _lifecycle = "ERROR: Session failed to start";
                OseLog.Error($"[RuntimeSessionDriver] Session failed to start for '{_packageId}'.");
            }

            RefreshInspectorState();
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            RefreshInspectorState();

            if (evt.Current == StepState.Active)
            {
                PushStepToUI();
            }
        }

        private void HandleAssemblyCompleted(AssemblyCompleted evt)
        {
            OseLog.Info($"[RuntimeSessionDriver] Assembly '{evt.AssemblyId}' completed.");
            RefreshInspectorState();
        }

        private void HandleSessionCompleted(SessionCompleted evt)
        {
            OseLog.Info($"[RuntimeSessionDriver] Session '{evt.MachineId}' completed in {evt.TotalSeconds:F1}s.");
            RefreshInspectorState();
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

            // Push step panel
            ui.ShowStepShell(
                progression.CurrentStepIndex + 1,
                progression.TotalSteps,
                step.GetDisplayName(),
                step.BuildInstructionBody());

            // Push part info from first required part
            MachinePackageDefinition package = _session.Package;
            string partId = GetFirstNonEmpty(step.requiredPartIds) ?? GetFirstNonEmpty(step.optionalPartIds);

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

            // Push progress
            ui.ShowProgressUpdate(progression.CurrentStepIndex, progression.TotalSteps);
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

        private void RefreshInspectorState()
        {
            MachineSessionState state = _session?.SessionState;
            if (state == null)
            {
                _lifecycle = "No session";
                _currentAssemblyId = "—";
                _currentStepId = "—";
                _stepIndex = 0;
                _totalSteps = 0;
                _elapsedSeconds = 0f;
                _mistakeCount = 0;
                _hintsUsed = 0;
                return;
            }

            _lifecycle = state.Lifecycle.ToString();
            _currentAssemblyId = state.CurrentAssemblyId ?? "—";
            _currentStepId = state.CurrentStepId ?? "—";
            _elapsedSeconds = state.ElapsedSeconds;
            _mistakeCount = state.MistakeCount;
            _hintsUsed = state.HintsUsed;

            var progression = _session.AssemblyController?.ProgressionController;
            if (progression != null)
            {
                _stepIndex = progression.CurrentStepIndex;
                _totalSteps = progression.TotalSteps;
            }
        }
    }
}
