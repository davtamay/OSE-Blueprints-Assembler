using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using OSE.UI.Bindings;
using OSE.UI.Controllers;
using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocumentBootstrap))]
    public sealed class UIRootCoordinator : MonoBehaviour, IPresentationAdapter
    {
        [Serializable]
        private struct SessionUiModeProfile
        {
            public SessionMode Mode;
            public bool ShowStepPanel;
            public bool ShowPartInfoPanel;
            public bool ShowSessionHud;
            public bool AllowHints;
        }

        [SerializeField] private UIDocumentBootstrap _documentBootstrap;
        [SerializeField] private bool _showShellPlaceholders = true;
        [Header("Session UI Modes")]
        [SerializeField] private SessionUiModeProfile[] _modeProfiles = CreateDefaultModeProfiles();

        private StepPanelPresenter _stepPresenter;
        private PartInfoPanelPresenter _partInfoPresenter;
        private SessionHudPanelPresenter _sessionHudPresenter;
        private ToolDockPanelPresenter _toolDockPresenter;
        private ToolInfoPanelPresenter _toolInfoPresenter;
        private StepPanelController _stepPanelController;
        private PartInfoPanelController _partInfoPanelController;
        private SessionHudPanelController _sessionHudPanelController;
        private ToolDockPanelController _toolDockPanelController;
        private ToolInfoPanelController _toolInfoPanelController;
        private SelectionService _selectionService;
        private ToolDockStateMachine _toolDock;
        private bool _showingHoverPartInfo;
        private RepositionUiController _repositionUi;
        private VisualElement _rootElement;
        private IntroOverlayController _introController;
        private AssemblyTransitionController _transitionController;
        private AssemblyPickerController _pickerController;
        private ToolCursorBadgeController _toolCursorBadgeController;
        private SessionHudMediator _sessionHudMediator;

        private int _currentStepNumber = 1;
        private int _totalSteps = 1;
        private string _stepTitle = "Assembly Step";
        private string _instruction = "Instruction text will be provided by the active runtime step.";

        private string _partName = "Selected Part";
        private string _partFunction = "Function metadata will be supplied by runtime content.";
        private string _partMaterial = "Material metadata will be supplied by runtime content.";
        private string _partTool = "Tool metadata will be supplied by runtime content.";
        private string _partSearchTerms = "Search terms will be supplied by runtime content.";
        private string _activeToolId; // sync'd from _toolDock for ShowToolInfo path

        private bool _showConfirmButton;
        private bool _showHintButton;
        private ConfirmGateController _gate = new ConfirmGateController();

        private bool _isBuilt;
        private bool _isPresentationAdapterRegistered;
        private SessionMode _activeMode = SessionMode.Guided;
        private SessionUiModeProfile _activeModeProfile;
        private bool _hasActiveModeProfile;

        private void Awake()
        {
            EnsureDependencies();
            ApplySessionMode(_activeMode);
        }

        private void OnEnable()
        {
            TryInitialize();
            RuntimeEventBus.Subscribe<RepositionModeChanged>(HandleRepositionModeChangedUI);
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleRuntimeStepStateChanged);
            RuntimeEventBus.Subscribe<StepNavigated>(HandleRuntimeStepNavigated);
            RuntimeEventBus.Subscribe<SessionCompleted>(HandleRuntimeSessionCompleted);
            RuntimeEventBus.Subscribe<AssemblyTransitionRequested>(HandleAssemblyTransitionRequested);
        }

        private void Start()
        {
            // UIDocument.rootVisualElement may be null during OnEnable on the first frame.
            // Retry here where the document is guaranteed to be fully initialized.
            if (!_isBuilt)
            {
                OseLog.Info("[UI] UIRootCoordinator.Start: not yet built, calling TryInitialize.");
                TryInitialize();
                OseLog.Info($"[UI] UIRootCoordinator.Start: after TryInitialize, _isBuilt={_isBuilt}");
            }
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<RepositionModeChanged>(HandleRepositionModeChangedUI);
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleRuntimeStepStateChanged);
            RuntimeEventBus.Unsubscribe<StepNavigated>(HandleRuntimeStepNavigated);
            RuntimeEventBus.Unsubscribe<SessionCompleted>(HandleRuntimeSessionCompleted);
            RuntimeEventBus.Unsubscribe<AssemblyTransitionRequested>(HandleAssemblyTransitionRequested);
            _toolDock?.Unsubscribe();
            UnregisterPresentationAdapter();
            TeardownUi();
        }

        private void Update()
        {
            // Invariant: keep this retry path. First-frame UIDocument readiness is not stable,
            // and removing it reintroduces missing UI on initial play.
            if (!_isBuilt)
            {
                TryInitialize();
                if (!_isBuilt) return;
            }

            if (_introController != null && _introController.IsVisible && _introController.HasPendingBuild && _introController.TryBuildPending())
            {
                HideAll();
            }

            if (Application.isPlaying)
            {
                EnsureToolDock();
                _toolDock.EnsureSubscription();
            }

            _toolCursorBadgeController?.UpdateToolCursorVisual();
            _repositionUi?.RefreshScaleUi();

            if (!_isBuilt || _sessionHudPanelController == null || !_sessionHudPanelController.IsBound)
                return;

            if (_sessionHudMediator != null && _sessionHudMediator.TickTimers())
            {
                RefreshSessionHudPanel();
            }

            if (Application.isPlaying
                && _partInfoPanelController != null
                && _partInfoPanelController.IsBound
                && !HasActivePartContext())
            {
                _partInfoPanelController.Hide();
            }
        }

        public void SetSessionMode(SessionMode mode)
        {
            ApplySessionMode(mode);
        }

        public bool IsHintDisplayAllowed => _hasActiveModeProfile ? _activeModeProfile.AllowHints : true;
        public bool IsMachineIntroVisible => _introController?.IsVisible ?? false;

        public void ResetMachineIntroState()
        {
            _introController?.ResetState();
            _activeToolId = null;
            _toolDock?.Teardown();
        }

        public void ShowInstruction(string instructionKey)
        {
            _instruction = string.IsNullOrWhiteSpace(instructionKey)
                ? _instruction
                : instructionKey;

            RefreshStepPanel();
        }

        public void ShowHint(string hintKey)
        {
            OseLog.VerboseInfo($"[UI] Hint requested before HintPanel exists: {hintKey}");
        }

        public void ShowHintContent(string title, string message, string hintType)
        {
            if (_sessionHudMediator == null || !_sessionHudMediator.ShowHintContent(title, message, hintType))
                return;

            RefreshSessionHudPanel();

            if (_gate.TryUnlockOnHintRequested())
                RefreshStepPanel();
        }

        public void ShowPartInfo(string partId)
        {
            _partName = string.IsNullOrWhiteSpace(partId)
                ? _partName
                : partId;

            if (!string.IsNullOrWhiteSpace(partId))
            {
                _partSearchTerms = partId;
            }

            RefreshPartInfoPanel();
        }

        public void ShowToolInfo(string toolId)
        {
            if (!string.IsNullOrWhiteSpace(toolId))
            {
                _partTool = toolId;
                _activeToolId = toolId;
                EnsureToolDock();
                _toolDock.SetActiveToolId(toolId);
            }

            RefreshPartInfoPanel();
            RefreshToolInfoPanel();
        }

        public void ToggleToolDock()
        {
            EnsureToolDock();
            _toolDock.HandleToggleRequested();
        }

        public void ShowProgressUpdate(int completedSteps, int totalSteps)
        {
            if (totalSteps <= 0)
            {
                _currentStepNumber = 0;
                _totalSteps = 0;
            }
            else
            {
                _totalSteps = totalSteps;
                _currentStepNumber = Mathf.Clamp(completedSteps + 1, 1, totalSteps);
            }

            RefreshStepPanel();
        }

        public void ShowStepCompletionToast(string message)
        {
            _sessionHudMediator?.ShowStepCompletionToast(message);
            RefreshSessionHudPanel();
        }

        public void ShowMilestoneFeedback(string milestoneKey)
        {
            string message = string.IsNullOrWhiteSpace(milestoneKey)
                ? "Session Complete!"
                : milestoneKey;
            _sessionHudMediator?.SetMilestone(message);
            _showConfirmButton = false;
            _showHintButton = false;
            _gate.ProgressComplete = true;

            // Clear stale step content so the panel doesn't keep showing the last step.
            _stepTitle = message;
            _instruction = string.Empty;

            RefreshStepPanel();
            RefreshSessionHudPanel();
        }

        public void ShowMachineIntro(string title, string description, string difficulty,
            int estimatedMinutes, string[] learningObjectives, string imageRef,
            int savedCompletedSteps = 0, int savedTotalSteps = 0)
        {
            if (!_isBuilt)
                TryInitialize();

            EnsureIntroController();

            // Show "Choose Section" button when the package has multiple assemblies.
            bool multiAssembly = false;
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var introSession) && introSession.Package?.machine != null)
            {
                var entryIds = introSession.Package.machine.entryAssemblyIds;
                multiAssembly = (entryIds != null && entryIds.Length > 1)
                    || (entryIds == null && introSession.Package.GetAssemblies().Length > 1);
            }
            _introController.ShowSectionPicker = multiAssembly;

            _introController.Show(title, description, difficulty, estimatedMinutes,
                learningObjectives, imageRef, savedCompletedSteps, savedTotalSteps);
        }

        public void DismissMachineIntro()
        {
            _introController?.Dismiss();
        }

        private bool TryRestoreRuntimePanelsAfterIntroDismiss()
        {
            if (!Application.isPlaying)
                return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            MachinePackageDefinition package = session.Package;
            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.CurrentStepDefinition;
            if (package == null || stepController == null || !stepController.HasActiveStep || step == null)
                return false;

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            int totalSteps = orderedSteps.Length;
            int stepNumber = StepUiContentUtility.ResolveDisplayStepNumber(orderedSteps, step);
            if (stepNumber <= 0)
            {
                ProgressionController progression = session.AssemblyController?.ProgressionController;
                if (progression == null)
                    return false;

                stepNumber = progression.CurrentStepIndex + 1;
                totalSteps = progression.TotalSteps;
            }

            StepUiContentUtility.StepShellContent stepShell = StepUiContentUtility.BuildStepShellContent(step);

            ShowStepShell(
                stepNumber,
                totalSteps,
                stepShell.Title,
                stepShell.Instruction,
                stepShell.ShowConfirmButton,
                stepShell.ShowHintButton,
                stepShell.ConfirmGate);

            StepUiContentUtility.PartInfoShellContent partInfo =
                StepUiContentUtility.BuildStepPartInfoShellContent(package, step, includeFallbackWhenNoRequiredPart: false);
            if (partInfo.HasContent)
            {
                ShowPartInfoShell(
                    partInfo.PartName,
                    partInfo.Function,
                    partInfo.Material,
                    partInfo.Tool,
                    partInfo.SearchTerms);
            }

            ShowProgressUpdate(stepNumber > 0 ? stepNumber - 1 : 0, totalSteps);

            MachineSessionState state = session.SessionState;
            if (state != null)
            {
                ShowChallengeMetrics(
                    state.HintsUsed,
                    state.MistakeCount,
                    state.CurrentStepElapsedSeconds,
                    state.ElapsedSeconds,
                    state.ChallengeActive);
            }

            EnsureToolDock();
            _toolDock.EnsureSubscription();

            return true;
        }

        private void HandleRuntimeStepStateChanged(StepStateChanged evt)
        {
            if (!Application.isPlaying || IsMachineIntroVisible || evt.Current != StepState.Active)
                return;

            // Ensure the tool-runtime subscription is wired up so the auto-equip
            // mechanism fires even if ShowStepShell hasn't been called yet for this step.
            EnsureToolDock();
            _toolDock.EnsureSubscription();
            TryRestoreRuntimePanelsAfterIntroDismiss();
        }

        private void HandleRuntimeStepNavigated(StepNavigated evt)
        {
            if (!Application.isPlaying || IsMachineIntroVisible)
                return;

            EnsureToolDock();
            _toolDock.EnsureSubscription();

            // StepNavigated fires BEFORE the assembly controller activates the target
            // step, so StepController still has the old/reset state.  Resolve the
            // target step directly from the event's global index to refresh the UI.
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            MachinePackageDefinition package = session.Package;
            if (package == null)
                return;

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            if (orderedSteps.Length == 0 || evt.TargetStepIndex < 0 || evt.TargetStepIndex >= orderedSteps.Length)
                return;

            StepDefinition targetStep = orderedSteps[evt.TargetStepIndex];
            if (targetStep == null)
                return;

            int stepNumber = evt.TargetStepIndex + 1;
            int totalSteps = evt.TotalSteps;

            StepUiContentUtility.StepShellContent stepShell =
                StepUiContentUtility.BuildStepShellContent(targetStep);

            ShowStepShell(
                stepNumber,
                totalSteps,
                stepShell.Title,
                stepShell.Instruction,
                stepShell.ShowConfirmButton,
                stepShell.ShowHintButton,
                stepShell.ConfirmGate);

            StepUiContentUtility.PartInfoShellContent partInfo =
                StepUiContentUtility.BuildStepPartInfoShellContent(package, targetStep, includeFallbackWhenNoRequiredPart: false);
            if (partInfo.HasContent)
            {
                ShowPartInfoShell(
                    partInfo.PartName,
                    partInfo.Function,
                    partInfo.Material,
                    partInfo.Tool,
                    partInfo.SearchTerms);
            }

            ShowProgressUpdate(stepNumber > 0 ? stepNumber - 1 : 0, totalSteps);
        }

        private void HandleRuntimeSessionCompleted(SessionCompleted evt)
        {
            if (!Application.isPlaying)
                return;

            int minutes = (int)(evt.TotalSeconds / 60f);
            int secs = (int)(evt.TotalSeconds % 60f);
            string timeStr = minutes > 0 ? $"{minutes}m {secs}s" : $"{secs}s";

            string machineName = "Assembly";
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                machineName = session?.Package?.machine?.GetDisplayName() ?? machineName;
            }

            ShowMilestoneFeedback($"{machineName} Complete! ({timeStr})");
        }

        public void ShowChallengeMetrics(
            int hintsUsed,
            int failedAttempts,
            float currentStepSeconds,
            float totalSeconds,
            bool challengeActive)
        {
            _sessionHudMediator?.ShowChallengeMetrics(hintsUsed, failedAttempts, currentStepSeconds, totalSeconds, challengeActive);
            RefreshSessionHudPanel();
        }

        public void HidePartInfoPanel()
        {
            _showingHoverPartInfo = false;
            _partInfoPanelController?.Hide();
        }

        public void HideAll()
        {
            _showingHoverPartInfo = false;
            _stepPanelController?.Hide();
            _partInfoPanelController?.Hide();
            _sessionHudPanelController?.Hide();
            _toolInfoPanelController?.Hide();
            _toolDockPanelController?.Hide();
        }

        public void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction, bool showConfirmButton = false, bool showHintButton = false, ConfirmGate confirmGate = ConfirmGate.None)
        {
            if (Application.isPlaying)
            {
                EnsureToolDock();
                _toolDock.EnsureSubscription();
            }

            _currentStepNumber = Mathf.Max(currentStepNumber, 0);
            _totalSteps = Mathf.Max(totalSteps, 0);
            _stepTitle = title;
            _instruction = instruction;
            _showConfirmButton = showConfirmButton;
            _showHintButton = showHintButton && IsHintDisplayAllowed;
            EnsureToolDock();
            _gate.Configure(confirmGate, () => _toolDock.IsEquipToolGateSatisfied());
            RefreshStepPanel();
        }

        public void ShowPartInfoShell(
            string partName,
            string function,
            string material,
            string tool,
            string searchTerms)
        {
            _showingHoverPartInfo = false;
            string resolvedPartName = string.IsNullOrWhiteSpace(partName) ? _partName : partName;
            bool samePart = string.Equals(resolvedPartName, _partName, StringComparison.Ordinal);

            _partName = resolvedPartName;
            _partFunction = function;
            _partMaterial = material;
            if (!string.IsNullOrWhiteSpace(tool))
            {
                _partTool = tool;
            }
            else if (!samePart)
            {
                _partTool = "No specific tool required.";
            }
            _partSearchTerms = searchTerms;
            RefreshPartInfoPanel();

            if (_gate.TryUnlockOnPartSelected())
                RefreshStepPanel();
        }

        public void ShowHoverPartInfoShell(
            string partName,
            string function,
            string material,
            string tool,
            string searchTerms)
        {
            _showingHoverPartInfo = true;

            string resolvedPartName = string.IsNullOrWhiteSpace(partName) ? _partName : partName;
            bool samePart = string.Equals(resolvedPartName, _partName, StringComparison.Ordinal);

            _partName = resolvedPartName;
            _partFunction = function;
            _partMaterial = material;
            if (!string.IsNullOrWhiteSpace(tool))
            {
                _partTool = tool;
            }
            else if (!samePart)
            {
                _partTool = "No specific tool required.";
            }
            _partSearchTerms = searchTerms;
            RefreshPartInfoPanel();
        }

        public void ClearHoverPartInfo()
        {
            if (!_showingHoverPartInfo)
                return;

            _showingHoverPartInfo = false;
            if (HasSelectionContext())
                RefreshPartInfoPanel();
            else
                _partInfoPanelController?.Hide();
        }

        public bool TryInitialize()
        {
            EnsureDependencies();

            if (!_isBuilt && !BuildUi())
            {
                return false;
            }

            RegisterPresentationAdapter();
            return true;
        }

        private bool BuildUi()
        {
            VisualElement root = _documentBootstrap != null
                ? _documentBootstrap.PrepareDocumentRoot()
                : null;

            if (root == null)
            {
                OseLog.Warn($"[UI] BuildUi failed: _documentBootstrap={((_documentBootstrap == null) ? "NULL" : "ok")}, rootVisualElement={((_documentBootstrap != null && GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement == null) ? "NULL" : "ok")}");
                return false;
            }

            _rootElement = root;

            UIToolkitStyleUtility.ApplyRootLayout(root);

            VisualElement leftColumn = new VisualElement();
            leftColumn.name = "ose-ui-column-left";
            UIToolkitStyleUtility.ApplyColumnLayout(leftColumn, TextAnchor.UpperLeft);

            VisualElement rightColumn = new VisualElement();
            rightColumn.name = "ose-ui-column-right";
            UIToolkitStyleUtility.ApplyColumnLayout(rightColumn, TextAnchor.UpperRight);

            VisualElement centerDock = new VisualElement();
            centerDock.name = "ose-ui-column-center-dock";
            centerDock.style.position = Position.Absolute;
            centerDock.style.left = 0f;
            centerDock.style.right = 0f;
            centerDock.style.bottom = 14f;
            centerDock.style.flexDirection = FlexDirection.Column;
            centerDock.style.alignItems = Align.Center;
            centerDock.style.justifyContent = Justify.FlexEnd;
            centerDock.pickingMode = PickingMode.Position;

            root.Add(leftColumn);
            root.Add(rightColumn);
            root.Add(centerDock);
            _toolCursorBadgeController = new ToolCursorBadgeController(() => _toolDock);
            _toolCursorBadgeController.BuildToolCursorVisual(root);
            _sessionHudMediator = new SessionHudMediator(
                _sessionHudPresenter,
                _sessionHudPanelController,
                () => IsHintDisplayAllowed);

            _stepPanelController.Bind(leftColumn);
            _sessionHudPanelController.Bind(leftColumn);
            _partInfoPanelController.Bind(rightColumn);
            _toolInfoPanelController.Bind(rightColumn);
            _toolDockPanelController.Bind(centerDock);
            _repositionUi ??= new RepositionUiController();
            _repositionUi.Build(centerDock);

            EnsureToolDock();
            _toolDockPanelController.ToggleRequested += _toolDock.HandleToggleRequested;
            _toolDockPanelController.ToolSelected += _toolDock.HandleToolSelected;
            _toolDockPanelController.UnequipRequested += _toolDock.HandleUnequipRequested;
            _toolDockPanelController.ToolHovered += _toolDock.HandleToolHovered;
            _toolDockPanelController.ToolHoverCleared += _toolDock.HandleToolHoverCleared;

            _isBuilt = true;

            // Part info panel starts hidden — only shown when a part is selected
            _partInfoPanelController.Hide();
            _toolInfoPanelController.Hide();
            _toolDockPanelController.Hide();

            if (_showShellPlaceholders)
            {
                RefreshStepPanel();
                RefreshSessionHudPanel();
            }
            else
            {
                HideAll();
            }

            RefreshToolDockPanel();
            RefreshToolInfoPanel();

            OseLog.Info("[UI] UI Toolkit root coordinator initialized.");
            return true;
        }

        private void RefreshStepPanel()
        {
            if (!_isBuilt)
            {
                return;
            }

            if (_hasActiveModeProfile && !_activeModeProfile.ShowStepPanel)
            {
                _stepPanelController.Hide();
                return;
            }

            // Finished-panel stacking now uses in-world guided docking and preview interaction
            // as the primary affordance. Keep the step shell focused on instruction/progress.
            bool showContextActionButton = false;
            string contextActionLabel = null;
            bool contextActionEnabled = false;
            float? progressOverride = _gate.ProgressComplete ? 1f : ResolveIntraStepProgress();

            // Resolve assembly name and global progress from session
            string assemblyName = null;
            int globalStepIndex = 0;
            int globalTotalSteps = 0;
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var sessionForProgress))
            {
                var pkg = sessionForProgress.Package;
                if (pkg != null)
                {
                    string assemblyId = sessionForProgress.AssemblyController?.CurrentAssemblyId;
                    if (!string.IsNullOrEmpty(assemblyId) &&
                        pkg.TryGetAssembly(assemblyId, out var assemblyDef) &&
                        assemblyDef != null)
                    {
                        assemblyName = assemblyDef.name;
                    }

                    var orderedSteps = pkg.GetOrderedSteps();
                    globalTotalSteps = orderedSteps?.Length ?? 0;
                    string activeStepId = sessionForProgress.AssemblyController?.StepController?.HasActiveStep == true
                        ? sessionForProgress.AssemblyController.StepController.CurrentStepState.StepId
                        : sessionForProgress.SessionState?.CurrentStepId;
                    if (!string.IsNullOrEmpty(activeStepId) && orderedSteps != null)
                    {
                        for (int i = 0; i < orderedSteps.Length; i++)
                        {
                            if (orderedSteps[i] != null &&
                                string.Equals(orderedSteps[i].id, activeStepId, System.StringComparison.OrdinalIgnoreCase))
                            {
                                globalStepIndex = i;
                                break;
                            }
                        }
                    }
                }
            }

            StepPanelViewModel viewModel = _stepPresenter.Create(
                _currentStepNumber,
                _totalSteps,
                _stepTitle,
                _instruction,
                _showConfirmButton,
                _showHintButton,
                _gate.Gate,
                _gate.IsUnlocked,
                showContextActionButton,
                contextActionLabel,
                contextActionEnabled,
                progressOverride,
                assemblyName,
                globalStepIndex,
                globalTotalSteps);

            _stepPanelController.Show(viewModel);
        }

        private bool TryBuildGuidedStackActionState(out string actionLabel, out bool actionEnabled)
        {
            actionLabel = null;
            actionEnabled = false;

            if (!Application.isPlaying)
                return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.CurrentStepDefinition;
            if (stepController == null ||
                !stepController.HasActiveStep ||
                step == null ||
                !step.IsPlacement ||
                !step.RequiresSubassemblyPlacement ||
                step.targetIds == null ||
                step.targetIds.Length != 1)
            {
                return false;
            }

            string subassemblyLabel = step.requiredSubassemblyId;
            if (session.Package != null &&
                session.Package.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subassembly) &&
                subassembly != null)
            {
                subassemblyLabel = subassembly.GetDisplayName();
            }

            actionLabel = $"Place {subassemblyLabel}";

            actionEnabled =
                ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                subassemblyController.IsSubassemblyReady(step.requiredSubassemblyId);

            return true;
        }

        /// <summary>
        /// Computes a progress override that blends step-level progress with within-step
        /// tool action progress, giving a smoother bar for tool-action-heavy steps.
        /// Returns null when no tool action is active (falls back to step-level formula).
        /// </summary>
        private float? ResolveIntraStepProgress()
        {
            if (_totalSteps <= 0 || _currentStepNumber <= 0)
                return null;

            var runtime = _toolDock?.RuntimeController;
            if (runtime == null ||
                !runtime.TryGetPrimaryActionSnapshot(
                    out ToolRuntimeController.ToolActionSnapshot snapshot))
                return null;

            if (!snapshot.IsConfigured || snapshot.RequiredCount <= 0)
                return null;

            // Base progress: completed steps / total steps
            float baseProgress = (float)(_currentStepNumber - 1) / _totalSteps;
            // Intra-step progress: tool action current / required
            float intraStep = (float)snapshot.CurrentCount / snapshot.RequiredCount;
            float stepSlice = 1f / _totalSteps;

            return Mathf.Clamp01(baseProgress + intraStep * stepSlice);
        }

        private void RefreshPartInfoPanel()
        {
            if (!_isBuilt)
            {
                return;
            }

            if (_hasActiveModeProfile && !_activeModeProfile.ShowPartInfoPanel)
            {
                _partInfoPanelController.Hide();
                return;
            }

            if (!HasActivePartContext())
            {
                _partInfoPanelController.Hide();
                return;
            }

            PartInfoPanelViewModel viewModel = _partInfoPresenter.Create(
                _partName,
                _partFunction,
                _partMaterial,
                _partTool,
                _partSearchTerms);

            _partInfoPanelController.Show(viewModel);
        }

        private void RefreshSessionHudPanel()
        {
            _sessionHudMediator?.RefreshSessionHudPanel(_isBuilt, _hasActiveModeProfile, _activeModeProfile.ShowSessionHud);
        }

        // ── Tool Dock State Machine (delegated to ToolDockStateMachine) ──

        private void EnsureToolDock()
        {
            _toolDock ??= new ToolDockStateMachine(
                onStateChanged: HandleToolDockStateChanged,
                getConfirmGate: () => _gate.Gate,
                getConfirmUnlocked: () => _gate.IsUnlocked,
                setConfirmUnlocked: unlocked =>
                {
                    _gate.IsUnlocked = unlocked;
                    RefreshStepPanel();
                });
        }

        private void HandleToolDockStateChanged()
        {
            _activeToolId = _toolDock.ActiveToolId;
            if (_toolDock.TryPopulateToolInfo(_activeToolId))
                _partTool = _toolDock.ToolName;

            RefreshToolDockPanel();
            RefreshToolInfoPanel();
            RefreshPartInfoPanel();
            RefreshStepPanel();
        }

        private void RefreshToolDockPanel()
        {
            if (!_isBuilt || _toolDockPanelController == null || !_toolDockPanelController.IsBound)
                return;

            var runtime = _toolDock?.RuntimeController;
            if (!Application.isPlaying || runtime == null || !runtime.HasPackage)
            {
                _toolDockPanelController.Hide();
                return;
            }

            ToolDockPanelViewModel viewModel = _toolDockPresenter.Create(
                runtime.GetAvailableTools(),
                runtime.GetRequiredToolIds(),
                runtime.ActiveToolId,
                _toolDock.ToolDockExpanded);

            _toolDockPanelController.Show(viewModel);
        }

        private void RefreshToolInfoPanel()
        {
            if (!_isBuilt || _toolInfoPanelController == null || !_toolInfoPanelController.IsBound)
                return;

            if (_hasActiveModeProfile && !_activeModeProfile.ShowPartInfoPanel)
            {
                _toolInfoPanelController.Hide();
                return;
            }

            string hoveredToolId = _toolDock?.HoveredToolId;
            string toolId = !string.IsNullOrWhiteSpace(hoveredToolId)
                ? hoveredToolId
                : _activeToolId;

            if (string.IsNullOrWhiteSpace(toolId) || _toolDock == null || !_toolDock.TryPopulateToolInfo(toolId))
            {
                _toolInfoPanelController.Hide();
                return;
            }

            ToolInfoPanelViewModel viewModel = _toolInfoPresenter.Create(
                _toolDock.ToolName,
                _toolDock.ToolCategory,
                _toolDock.ToolPurpose,
                _toolDock.ToolUsageNotes,
                _toolDock.ToolSafetyNotes);

            _toolInfoPanelController.Show(viewModel);
        }


        private void RegisterPresentationAdapter()
        {
            if (_isPresentationAdapterRegistered)
            {
                return;
            }

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter existingAdapter) &&
                !ReferenceEquals(existingAdapter, this))
            {
                OseLog.Warn("[UI] Replacing an existing IPresentationAdapter registration.");
            }

            ServiceRegistry.Register<IPresentationAdapter>(this);
            _isPresentationAdapterRegistered = true;
        }

        private void UnregisterPresentationAdapter()
        {
            if (!_isPresentationAdapterRegistered)
            {
                return;
            }

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter existingAdapter) &&
                ReferenceEquals(existingAdapter, this))
            {
                ServiceRegistry.Unregister<IPresentationAdapter>();
            }

            _isPresentationAdapterRegistered = false;
        }

        private void TeardownUi()
        {
            if (_toolDockPanelController != null && _toolDock != null)
            {
                _toolDockPanelController.ToggleRequested -= _toolDock.HandleToggleRequested;
                _toolDockPanelController.ToolSelected -= _toolDock.HandleToolSelected;
                _toolDockPanelController.UnequipRequested -= _toolDock.HandleUnequipRequested;
                _toolDockPanelController.ToolHovered -= _toolDock.HandleToolHovered;
                _toolDockPanelController.ToolHoverCleared -= _toolDock.HandleToolHoverCleared;
            }

            _stepPanelController?.Unbind();
            _partInfoPanelController?.Unbind();
            _sessionHudPanelController?.Unbind();
            _toolInfoPanelController?.Unbind();
            _toolDockPanelController?.Unbind();
            _isBuilt = false;
            _rootElement = null;
            _toolCursorBadgeController?.Teardown();
            _toolCursorBadgeController = null;
            _sessionHudMediator = null;
            _introController?.Teardown();
            _transitionController?.Teardown();
            _pickerController?.Teardown();
            _activeToolId = null;
            _toolDock?.Teardown();
            _repositionUi?.Teardown();
            _repositionUi = null;
        }

        private void HandleRepositionModeChangedUI(RepositionModeChanged evt)
        {
            _repositionUi?.HandleRepositionModeChanged(evt);
        }

        // ── Assembly Transition Overlay ──

        private void EnsureTransitionController()
        {
            _transitionController ??= new AssemblyTransitionController(
                () => _rootElement,
                HandleTransitionContinue);
        }

        private void HandleAssemblyTransitionRequested(AssemblyTransitionRequested evt)
        {
            EnsureTransitionController();
            _transitionController.Show(evt);
        }

        private void HandleTransitionContinue()
        {
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                session.ResumeAfterTransition();

            RefreshStepPanel();
            RefreshSessionHudPanel();
            RefreshPartInfoPanel();
            RefreshToolDockPanel();
            RefreshToolInfoPanel();
        }

        // ── Assembly Picker Overlay ──

        public bool IsAssemblyPickerVisible => _pickerController?.IsVisible ?? false;

        public void ShowAssemblyPicker()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session) || session.Package == null)
                return;

            _pickerController ??= new AssemblyPickerController(() => _rootElement);

            int completedSteps = session.SessionState?.CompletedStepCount ?? 0;
            _pickerController.Show(session.Package, completedSteps);
        }

        public void DismissAssemblyPicker()
        {
            _pickerController?.Dismiss();
        }

        // ── Machine Intro Overlay (delegated to IntroOverlayController) ──

        private void EnsureIntroController()
        {
            _introController ??= new IntroOverlayController(
                () => _rootElement,
                HideAll,
                HandleIntroDismissed);
        }

        private void HandleIntroDismissed()
        {
            // Invariant: intro dismissal must eagerly rebuild runtime panels here.
            // Deferring this back to event order reintroduced the missing-shell regressions.
            if (!TryRestoreRuntimePanelsAfterIntroDismiss())
            {
                RefreshStepPanel();
                RefreshSessionHudPanel();
                RefreshPartInfoPanel();
            }
            RefreshToolDockPanel();
            RefreshToolInfoPanel();
        }

        private void Reset()
        {
            _documentBootstrap = GetComponent<UIDocumentBootstrap>();
            EnsureDependencies();
        }

        private void EnsureDependencies()
        {
            if (_documentBootstrap == null)
            {
                _documentBootstrap = GetComponent<UIDocumentBootstrap>();
            }

            EnsureModeProfiles();
            _stepPresenter ??= new StepPanelPresenter();
            _partInfoPresenter ??= new PartInfoPanelPresenter();
            _sessionHudPresenter ??= new SessionHudPanelPresenter();
            _toolDockPresenter ??= new ToolDockPanelPresenter();
            _toolInfoPresenter ??= new ToolInfoPanelPresenter();
            _stepPanelController ??= new StepPanelController();
            _partInfoPanelController ??= new PartInfoPanelController();
            _sessionHudPanelController ??= new SessionHudPanelController();
            _toolDockPanelController ??= new ToolDockPanelController();
            _toolInfoPanelController ??= new ToolInfoPanelController();

            if (_selectionService == null)
                ServiceRegistry.TryGet<SelectionService>(out _selectionService);

        }

        private bool HasActivePartContext()
        {
            if (!Application.isPlaying)
                return true;

            if (_showingHoverPartInfo)
                return true;

            return HasSelectionContext();
        }

        private bool HasSelectionContext()
        {
            if (_selectionService == null)
                ServiceRegistry.TryGet<SelectionService>(out _selectionService);

            return _selectionService != null
                && (_selectionService.CurrentSelection != null
                    || _selectionService.CurrentInspection != null);
        }

        private void EnsureModeProfiles()
        {
            if (_modeProfiles == null || _modeProfiles.Length == 0)
            {
                _modeProfiles = CreateDefaultModeProfiles();
            }
        }

        private void ApplySessionMode(SessionMode mode)
        {
            _activeMode = mode;
            _activeModeProfile = ResolveModeProfile(mode);
            _hasActiveModeProfile = true;

            if (!_activeModeProfile.AllowHints)
            {
                _sessionHudMediator?.ClearHintState();
            }

            if (_isBuilt)
            {
                RefreshStepPanel();
                RefreshPartInfoPanel();
                RefreshSessionHudPanel();
                RefreshToolDockPanel();
                RefreshToolInfoPanel();
            }
        }

        private SessionUiModeProfile ResolveModeProfile(SessionMode mode)
        {
            if (_modeProfiles != null)
            {
                for (int i = 0; i < _modeProfiles.Length; i++)
                {
                    if (_modeProfiles[i].Mode == mode)
                        return _modeProfiles[i];
                }
            }

            return new SessionUiModeProfile
            {
                Mode = mode,
                ShowStepPanel = true,
                ShowPartInfoPanel = true,
                ShowSessionHud = true,
                AllowHints = true
            };
        }

        private static SessionUiModeProfile[] CreateDefaultModeProfiles()
        {
            return new[]
            {
                new SessionUiModeProfile
                {
                    Mode = SessionMode.Tutorial,
                    ShowStepPanel = true,
                    ShowPartInfoPanel = true,
                    ShowSessionHud = true,
                    AllowHints = true
                },
                new SessionUiModeProfile
                {
                    Mode = SessionMode.Guided,
                    ShowStepPanel = true,
                    ShowPartInfoPanel = true,
                    ShowSessionHud = true,
                    AllowHints = true
                },
                new SessionUiModeProfile
                {
                    Mode = SessionMode.Standard,
                    ShowStepPanel = true,
                    ShowPartInfoPanel = true,
                    ShowSessionHud = false,
                    AllowHints = false
                },
                new SessionUiModeProfile
                {
                    Mode = SessionMode.Challenge,
                    ShowStepPanel = true,
                    ShowPartInfoPanel = true,
                    ShowSessionHud = true,
                    AllowHints = true
                },
                new SessionUiModeProfile
                {
                    Mode = SessionMode.Review,
                    ShowStepPanel = true,
                    ShowPartInfoPanel = true,
                    ShowSessionHud = false,
                    AllowHints = false
                }
            };
        }

    }
}
