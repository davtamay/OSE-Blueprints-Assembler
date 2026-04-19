using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
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
        [SerializeField] private UIDocumentBootstrap _documentBootstrap;
        [SerializeField] private bool _showShellPlaceholders = true;
        [Header("Session UI Modes")]
        [SerializeField] private UiSessionModeManager.SessionUiModeProfile[] _modeProfiles = UiSessionModeManager.CreateDefaultProfiles();

        private ToolDockStateMachine _toolDock;
        private RepositionUiController _repositionUi;
        private VisualElement _rootElement;
        private IntroOverlayController _introController;
        private AssemblyTransitionController _transitionController;
        private AssemblyPickerController _pickerController;
        private ToolCursorBadgeController _toolCursorBadgeController;
        private SessionHudMediator _sessionHudMediator;
        private ConfirmGateController _gate = new ConfirmGateController();

        private bool _isBuilt;
        private bool _isPresentationAdapterRegistered;
        private UiSessionModeManager _modeManager;
        private PresentationPanelOrchestrator _orchestrator;

        private void Awake()
        {
            EnsureDependencies();
            _modeManager.SetMode(_modeManager.ActiveMode);
        }

        private void OnEnable()
        {
            TryInitialize();
            RuntimeEventBus.Subscribe<RepositionModeChanged>(HandleRepositionModeChangedUI);
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleRuntimeStepStateChanged);
            RuntimeEventBus.Subscribe<StepNavigated>(HandleRuntimeStepNavigated);
            RuntimeEventBus.Subscribe<SessionCompleted>(HandleRuntimeSessionCompleted);
            RuntimeEventBus.Subscribe<AssemblyTransitionRequested>(HandleAssemblyTransitionRequested);
            RuntimeEventBus.Subscribe<ObserveTargetsCompleted>(HandleObserveTargetsCompleted);
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
            RuntimeEventBus.Unsubscribe<ObserveTargetsCompleted>(HandleObserveTargetsCompleted);
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

            if (!_isBuilt || _orchestrator?.SessionHudPanelController == null || !_orchestrator.SessionHudPanelController.IsBound)
                return;

            if (_sessionHudMediator != null && _sessionHudMediator.TickTimers())
            {
                _orchestrator.RefreshSessionHudPanel();
            }

            if (Application.isPlaying
                && _orchestrator?.PartInfoPanelController != null
                && _orchestrator.PartInfoPanelController.IsBound)
            {
                _orchestrator.RefreshPartInfoPanel();
            }
        }

        public void SetSessionMode(SessionMode mode)
        {
            _modeManager.SetMode(mode);
        }

        public bool IsHintDisplayAllowed => _modeManager.IsHintDisplayAllowed;
        public bool IsMachineIntroVisible => _introController?.IsVisible ?? false;

        public void ResetMachineIntroState()
        {
            _introController?.ResetState();
            if (_orchestrator != null) _orchestrator.ActiveToolId = null;
            _toolDock?.Teardown();
        }

        public void ShowInstruction(string instructionKey)
        {
            _orchestrator?.SetInstructionOnly(instructionKey);
            _orchestrator?.RefreshStepPanel();
        }

        public void ShowHint(string hintKey)
        {
            OseLog.VerboseInfo($"[UI] Hint requested before HintPanel exists: {hintKey}");
        }

        public void ShowHintContent(string title, string message, string hintType)
        {
            if (_sessionHudMediator == null || !_sessionHudMediator.ShowHintContent(title, message, hintType))
                return;

            _orchestrator.RefreshSessionHudPanel();

            if (_gate.TryUnlockOnHintRequested())
                _orchestrator.RefreshStepPanel();
        }

        public void ShowPartInfo(string partId)
        {
            _orchestrator?.SetPartNameOnly(partId);
            _orchestrator?.RefreshPartInfoPanel();
        }

        public void ShowToolInfo(string toolId)
        {
            if (!string.IsNullOrWhiteSpace(toolId))
            {
                _orchestrator?.SetToolForPart(toolId);
                if (_orchestrator != null) _orchestrator.ActiveToolId = toolId;
                EnsureToolDock();
                _toolDock.SetActiveToolId(toolId);
            }

            _orchestrator?.RefreshPartInfoPanel();
            _orchestrator?.RefreshToolInfoPanel();
        }

        public void ToggleToolDock()
        {
            EnsureToolDock();
            _toolDock.HandleToggleRequested();
        }

        public void ShowProgressUpdate(int completedSteps, int totalSteps)
        {
            _orchestrator?.SetProgressContent(completedSteps, totalSteps);
            _orchestrator?.RefreshStepPanel();
        }

        public void ShowStepCompletionToast(string message)
        {
            _sessionHudMediator?.ShowStepCompletionToast(message);
            _orchestrator?.RefreshSessionHudPanel();
        }

        public void ShowMilestoneFeedback(string milestoneKey)
        {
            string message = string.IsNullOrWhiteSpace(milestoneKey)
                ? "Session Complete!"
                : milestoneKey;
            _sessionHudMediator?.SetMilestone(message);
            _gate.ProgressComplete = true;

            // Clear stale step content so the panel doesn't keep showing the last step.
            _orchestrator?.SetMilestoneContent(message, string.Empty);

            _orchestrator?.RefreshStepPanel();
            _orchestrator?.RefreshSessionHudPanel();
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
                    partInfo.SearchTerms);
            }

            ShowProgressUpdate(stepNumber > 0 ? stepNumber - 1 : 0, totalSteps);
        }

        private void HandleObserveTargetsCompleted(ObserveTargetsCompleted evt)
        {
            if (!Application.isPlaying) return;
            if (_gate.TryUnlockOnObserveComplete())
                _orchestrator?.RefreshStepPanel();
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
            _orchestrator?.RefreshSessionHudPanel();
        }

        public void HidePartInfoPanel()
        {
            _orchestrator?.ClearHoverPartInfo();
            _orchestrator?.PartInfoPanelController?.Hide();
        }

        public void HideAll()
        {
            _orchestrator?.HideAll();
        }

        public void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction, bool showConfirmButton = false, bool showHintButton = false, ConfirmGate confirmGate = ConfirmGate.None)
        {
            if (Application.isPlaying)
            {
                EnsureToolDock();
                _toolDock.EnsureSubscription();
            }

            EnsureToolDock();
            _gate.Configure(confirmGate, () => _toolDock.IsEquipToolGateSatisfied());

            _orchestrator?.SetStepContent(
                Mathf.Max(currentStepNumber, 0),
                Mathf.Max(totalSteps, 0),
                title,
                instruction,
                showConfirmButton,
                showHintButton && IsHintDisplayAllowed);

            _orchestrator?.RefreshStepPanel();
        }

        public void ShowPartInfoShell(
            string partName,
            string function,
            string material,
            string searchTerms)
        {
            if (_orchestrator == null) return;
            _orchestrator.SetPartInfoContent(partName, function, material, searchTerms);
            _orchestrator.RefreshPartInfoPanel();

            if (_gate.TryUnlockOnPartSelected())
                _orchestrator.RefreshStepPanel();
        }

        public void ShowHoverPartInfoShell(
            string partName,
            string function,
            string material,
            string searchTerms)
        {
            if (_orchestrator == null) return;
            _orchestrator.SetHoverPartInfoContent(partName, function, material, searchTerms);
            _orchestrator.RefreshPartInfoPanel();
        }

        public void ClearHoverPartInfo()
        {
            _orchestrator?.ClearHoverPartInfo();
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
            centerDock.pickingMode = PickingMode.Ignore;

            root.Add(leftColumn);
            root.Add(rightColumn);
            root.Add(centerDock);
            _toolCursorBadgeController = new ToolCursorBadgeController(() => _toolDock);
            _toolCursorBadgeController.BuildToolCursorVisual(root);
            _sessionHudMediator = new SessionHudMediator(
                _orchestrator.SessionHudPresenter,
                _orchestrator.SessionHudPanelController,
                () => IsHintDisplayAllowed);

            _orchestrator.StepPanelController.Bind(leftColumn);
            _orchestrator.SessionHudPanelController.Bind(leftColumn);
            _orchestrator.PartInfoPanelController.Bind(rightColumn);
            _orchestrator.ToolInfoPanelController.Bind(rightColumn);

            // ── Tool palette (popover area — floats above action bar) ──
            _orchestrator.ToolDockPanelController.Bind(centerDock);

            // ── Scale popover (floats above action bar) ──
            _repositionUi ??= new RepositionUiController();
            _repositionUi.Build(centerDock);

            // ── Compact action bar pill ──
            var actionBar = new VisualElement();
            actionBar.name = "ose-action-bar";
            actionBar.style.flexDirection = FlexDirection.Row;
            actionBar.style.alignItems = Align.Center;
            actionBar.style.justifyContent = Justify.Center;
            actionBar.style.height = 44f;
            actionBar.style.paddingLeft = 4f;
            actionBar.style.paddingRight = 4f;
            actionBar.style.backgroundColor = new Color(0.10f, 0.13f, 0.18f, 0.94f);
            actionBar.style.borderTopLeftRadius = 22f;
            actionBar.style.borderTopRightRadius = 22f;
            actionBar.style.borderBottomLeftRadius = 22f;
            actionBar.style.borderBottomRightRadius = 22f;
            actionBar.style.borderTopColor = new Color(0.28f, 0.36f, 0.46f, 0.7f);
            actionBar.style.borderRightColor = new Color(0.28f, 0.36f, 0.46f, 0.7f);
            actionBar.style.borderBottomColor = new Color(0.28f, 0.36f, 0.46f, 0.7f);
            actionBar.style.borderLeftColor = new Color(0.28f, 0.36f, 0.46f, 0.7f);
            actionBar.style.borderTopWidth = 1f;
            actionBar.style.borderRightWidth = 1f;
            actionBar.style.borderBottomWidth = 1f;
            actionBar.style.borderLeftWidth = 1f;
            actionBar.pickingMode = PickingMode.Position;
            centerDock.Add(actionBar);

            // Place icon buttons into the pill
            var toolBtn = _orchestrator.ToolDockPanelController.ActionBarButton;
            if (toolBtn != null) actionBar.Add(toolBtn);
            AddSeparator(actionBar);
            if (_repositionUi.MoveButton != null) actionBar.Add(_repositionUi.MoveButton);
            AddSeparator(actionBar);
            if (_repositionUi.ScaleButton != null) actionBar.Add(_repositionUi.ScaleButton);

            EnsureToolDock();
            _repositionUi.ScalePopoverOpened += HandleScalePopoverOpenedExclusion;
            _orchestrator.ToolDockPanelController.ToggleRequested += HandleToolToggleWithExclusion;
            _orchestrator.ToolDockPanelController.ToolSelected    += _toolDock.HandleToolSelected;
            _orchestrator.ToolDockPanelController.UnequipRequested += _toolDock.HandleUnequipRequested;
            _orchestrator.ToolDockPanelController.ToolHovered     += _toolDock.HandleToolHovered;
            _orchestrator.ToolDockPanelController.ToolHoverCleared += _toolDock.HandleToolHoverCleared;

            _isBuilt = true;

            // Part info panel starts hidden — only shown when a part is selected
            _orchestrator.PartInfoPanelController.Hide();
            _orchestrator.ToolInfoPanelController.Hide();
            _orchestrator.ToolDockPanelController.Hide();

            if (_showShellPlaceholders)
            {
                _orchestrator.RefreshStepPanel();
                _orchestrator.RefreshSessionHudPanel();
            }
            else
            {
                _orchestrator.HideAll();
            }

            _orchestrator.RefreshToolDockPanel();
            _orchestrator.RefreshToolInfoPanel();

            OseLog.Info("[UI] UI Toolkit root coordinator initialized.");
            return true;
        }

        private static void AddSeparator(VisualElement parent)
        {
            var sep = new VisualElement();
            sep.style.width = 1f;
            sep.style.height = 20f;
            sep.style.backgroundColor = new Color(0.4f, 0.46f, 0.54f, 0.4f);
            sep.style.marginLeft = 2f;
            sep.style.marginRight = 2f;
            sep.pickingMode = PickingMode.Ignore;
            parent.Add(sep);
        }

        /// <summary>
        /// Wraps the tool dock toggle to close the scale popover (mutual exclusion).
        /// </summary>
        private void HandleToolToggleWithExclusion()
        {
            _repositionUi?.SetScalePopoverVisible(false);
            _toolDock?.HandleToggleRequested();
        }

        private void HandleScalePopoverOpenedExclusion()
        {
            if (_toolDock != null && _toolDock.ToolDockExpanded)
                _toolDock.HandleToggleRequested();
        }

        // ── Tool Dock State Machine (delegated to ToolDockStateMachine) ──

        private void EnsureToolDock()
        {
            _toolDock ??= new ToolDockStateMachine(
                onStateChanged: HandleToolDockStateChanged,
                onHoverChanged: HandleToolDockHoverChanged,
                getConfirmGate: () => _gate.Gate,
                getConfirmUnlocked: () => _gate.IsUnlocked,
                setConfirmUnlocked: unlocked =>
                {
                    _gate.IsUnlocked = unlocked;
                    _orchestrator?.RefreshStepPanel();
                });
        }

        private void HandleToolDockStateChanged()
        {
            if (_orchestrator != null)
            {
                _orchestrator.ActiveToolId = _toolDock.ActiveToolId;
                if (_toolDock.TryPopulateToolInfo(_orchestrator.ActiveToolId))
                    _orchestrator.SetToolForPart(_toolDock.ToolName);
            }

            _orchestrator?.RefreshToolDockPanel();
            _orchestrator?.RefreshToolInfoPanel();
            _orchestrator?.RefreshPartInfoPanel();
            _orchestrator?.RefreshStepPanel();
        }

        /// <summary>
        /// Lightweight refresh for tool hover — only updates the tool info panel
        /// without rebuilding the dock chip list (which would destroy the hovered chip).
        /// </summary>
        private void HandleToolDockHoverChanged()
        {
            _orchestrator?.RefreshToolInfoPanel();
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
            ServiceRegistry.Register<IHintPresenter>(this);
            ServiceRegistry.Register<IStepPresenter>(this);
            ServiceRegistry.Register<IPartInfoPresenter>(this);
            ServiceRegistry.Register<IMachineIntroPresenter>(this);
            ServiceRegistry.Register<IAssemblyPickerPresenter>(this);
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
                ServiceRegistry.Unregister<IHintPresenter>();
                ServiceRegistry.Unregister<IStepPresenter>();
                ServiceRegistry.Unregister<IPartInfoPresenter>();
                ServiceRegistry.Unregister<IMachineIntroPresenter>();
                ServiceRegistry.Unregister<IAssemblyPickerPresenter>();
            }

            _isPresentationAdapterRegistered = false;
        }

        private void TeardownUi()
        {
            if (_repositionUi != null)
                _repositionUi.ScalePopoverOpened -= HandleScalePopoverOpenedExclusion;

            if (_orchestrator?.ToolDockPanelController != null && _toolDock != null)
            {
                _orchestrator.ToolDockPanelController.ToggleRequested  -= HandleToolToggleWithExclusion;
                _orchestrator.ToolDockPanelController.ToolSelected     -= _toolDock.HandleToolSelected;
                _orchestrator.ToolDockPanelController.UnequipRequested -= _toolDock.HandleUnequipRequested;
                _orchestrator.ToolDockPanelController.ToolHovered      -= _toolDock.HandleToolHovered;
                _orchestrator.ToolDockPanelController.ToolHoverCleared -= _toolDock.HandleToolHoverCleared;
            }

            _orchestrator?.StepPanelController?.Unbind();
            _orchestrator?.PartInfoPanelController?.Unbind();
            _orchestrator?.SessionHudPanelController?.Unbind();
            _orchestrator?.ToolInfoPanelController?.Unbind();
            _orchestrator?.ToolDockPanelController?.Unbind();
            _isBuilt = false;
            _rootElement = null;
            _toolCursorBadgeController?.Teardown();
            _toolCursorBadgeController = null;
            _sessionHudMediator = null;
            _introController?.Teardown();
            _transitionController?.Teardown();
            _pickerController?.Teardown();
            if (_orchestrator != null) _orchestrator.ActiveToolId = null;
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

            _orchestrator?.RefreshAll();
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
                _orchestrator?.RefreshStepPanel();
                _orchestrator?.RefreshSessionHudPanel();
                _orchestrator?.RefreshPartInfoPanel();
            }
            _orchestrator?.RefreshToolDockPanel();
            _orchestrator?.RefreshToolInfoPanel();
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

            if (_modeManager == null)
            {
                _modeManager = new UiSessionModeManager(_modeProfiles);
                _modeManager.OnModeChanged     = () => _orchestrator?.RefreshAll();
                _modeManager.OnHintsDisabled   = () => _sessionHudMediator?.ClearHintState();
            }

            if (_orchestrator == null)
            {
                _orchestrator = new PresentationPanelOrchestrator(
                    () => _isBuilt,
                    () => _modeManager,
                    () => _toolDock,
                    () => _gate,
                    () => _sessionHudMediator);
            }
        }

    }
}
