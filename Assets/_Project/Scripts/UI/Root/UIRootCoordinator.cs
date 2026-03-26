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
using UnityEngine.InputSystem;
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
        private ToolRuntimeController _toolRuntimeController;
        private bool _showingHoverPartInfo;
        private bool _isToolRuntimeSubscribed;
        private string _hoveredToolId;
        private bool _toolDockExpanded;
        private VisualElement _toolCursorBadge;
        private Label _toolCursorLabel;
        private Button _repositionButton;
        private Button _resetPositionButton;
        private Button _decreaseScaleButton;
        private Button _increaseScaleButton;
        private Label _assemblyScaleLabel;
        private bool _repositionActive;
        private VisualElement _rootElement;
        private VisualElement _introOverlay;
        private bool _introVisible;
        private bool _introDismissedThisSession;
        private string _introMachineId;
        private bool _pendingIntroOverlayBuild;
        private string _pendingIntroTitle;
        private string _pendingIntroDescription;
        private string _pendingIntroDifficulty;
        private int _pendingIntroEstimatedMinutes;
        private string[] _pendingIntroLearningObjectives;
        private string _pendingIntroImageRef;
        private int _pendingIntroSavedCompletedSteps;
        private int _pendingIntroSavedTotalSteps;
        private const float ToolCursorBadgeWidth = 172f;
        private const float ToolCursorBadgeHeight = 34f;
        private const float MouseCursorOffsetY = 22f;
        private const float TouchCursorOffsetY = 24f;

        private int _currentStepNumber = 1;
        private int _totalSteps = 1;
        private string _stepTitle = "Assembly Step";
        private string _instruction = "Instruction text will be provided by the active runtime step.";

        private string _partName = "Selected Part";
        private string _partFunction = "Function metadata will be supplied by runtime content.";
        private string _partMaterial = "Material metadata will be supplied by runtime content.";
        private string _partTool = "Tool metadata will be supplied by runtime content.";
        private string _partSearchTerms = "Search terms will be supplied by runtime content.";
        private string _activeToolId;
        private string _toolName = "Selected Tool";
        private string _toolCategory = "Tool category metadata will be supplied by runtime content.";
        private string _toolPurpose = "Tool purpose metadata will be supplied by runtime content.";
        private string _toolUsageNotes = "Tool usage notes metadata will be supplied by runtime content.";
        private string _toolSafetyNotes = "Tool safety notes metadata will be supplied by runtime content.";
        private bool _autoCompletingTargetlessToolStep;
        private bool _suppressAutoEquip;
        private string _lastAutoEquipStepId;
        private bool _autoCompletingEquipTaggedStep;

        private int _hintsUsed;
        private int _failedAttempts;
        private float _currentStepSeconds;
        private float _totalSeconds;
        private bool _challengeActive;

        private bool _showConfirmButton;
        private bool _showHintButton;
        private ConfirmGate _confirmGate = ConfirmGate.None;
        private bool _confirmUnlocked = true;
        private bool _progressComplete;
        private bool _suppressGateUnlock;

        private string _hintTitle = "Guidance";
        private string _hintMessage = "Follow the guidance to continue.";
        private string _hintType = "Hint";
        private float _hintHideAtSeconds;
        private bool _hintToastActive;
        private const float HintToastDuration = 6f;

        private string _stepToastMessage;
        private float _stepToastHideAtSeconds;
        private bool _stepToastActive;
        private const float StepToastDuration = 2f;

        private string _milestoneMessage;
        private bool _milestoneActive;

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
            UnsubscribeFromToolRuntime();
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

            if (_introVisible && _pendingIntroOverlayBuild && TryBuildPendingIntroOverlay())
            {
                _pendingIntroOverlayBuild = false;
                HideAll();
            }

            if (Application.isPlaying)
            {
                EnsureToolRuntimeSubscription();
            }

            UpdateToolCursorVisual();
            RefreshAssemblyScaleUi();

            if (!_isBuilt || _sessionHudPanelController == null || !_sessionHudPanelController.IsBound)
                return;

            if (Application.isPlaying && _hintHideAtSeconds > 0f && Time.time >= _hintHideAtSeconds)
            {
                _hintHideAtSeconds = 0f;
                _hintToastActive = false;
                RefreshSessionHudPanel();
            }

            if (Application.isPlaying && _stepToastHideAtSeconds > 0f && Time.time >= _stepToastHideAtSeconds)
            {
                _stepToastHideAtSeconds = 0f;
                _stepToastActive = false;
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
        public bool IsMachineIntroVisible => _introVisible;

        public void ResetMachineIntroState()
        {
            _introDismissedThisSession = false;
            _pendingIntroOverlayBuild = false;
            _activeToolId = null;
            _hoveredToolId = null;
            _suppressAutoEquip = false;
            _lastAutoEquipStepId = null;
            _autoCompletingTargetlessToolStep = false;
            _autoCompletingEquipTaggedStep = false;
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }
            _introVisible = false;
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
            if (!IsHintDisplayAllowed)
                return;

            _hintTitle = string.IsNullOrWhiteSpace(title) ? _hintTitle : title;
            _hintMessage = string.IsNullOrWhiteSpace(message) ? _hintMessage : message;
            _hintType = string.IsNullOrWhiteSpace(hintType) ? _hintType : hintType;
            _hintHideAtSeconds = Application.isPlaying ? Time.time + HintToastDuration : 0f;
            _hintToastActive = true;
            RefreshSessionHudPanel();

            if (_confirmGate == ConfirmGate.RequestHint && !_confirmUnlocked)
            {
                _confirmUnlocked = true;
                RefreshStepPanel();
            }
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
                TryPopulateToolInfo(toolId);
            }

            RefreshPartInfoPanel();
            RefreshToolInfoPanel();
        }

        public void ToggleToolDock()
        {
            _toolDockExpanded = !_toolDockExpanded;
            RefreshToolDockPanel();
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
            _stepToastMessage = string.IsNullOrWhiteSpace(message) ? "Step Complete!" : message;
            _stepToastHideAtSeconds = Application.isPlaying ? Time.time + StepToastDuration : 0f;
            _stepToastActive = true;
            RefreshSessionHudPanel();
        }

        public void ShowMilestoneFeedback(string milestoneKey)
        {
            _milestoneMessage = string.IsNullOrWhiteSpace(milestoneKey)
                ? "Session Complete!"
                : milestoneKey;
            _milestoneActive = true;
            _showConfirmButton = false;
            _showHintButton = false;
            _progressComplete = true;

            // Clear stale step content so the panel doesn't keep showing the last step.
            _stepTitle = _milestoneMessage;
            _instruction = string.Empty;

            RefreshStepPanel();
            RefreshSessionHudPanel();
        }

        public void ShowMachineIntro(string title, string description, string difficulty,
            int estimatedMinutes, string[] learningObjectives, string imageRef,
            int savedCompletedSteps = 0, int savedTotalSteps = 0)
        {
            if (_introDismissedThisSession)
                return;

            if (!_isBuilt)
                TryInitialize();

            _introMachineId = title;
            _introVisible = true;
            _pendingIntroTitle = title;
            _pendingIntroDescription = description;
            _pendingIntroDifficulty = difficulty;
            _pendingIntroEstimatedMinutes = estimatedMinutes;
            _pendingIntroLearningObjectives = learningObjectives;
            _pendingIntroImageRef = imageRef;
            _pendingIntroSavedCompletedSteps = savedCompletedSteps;
            _pendingIntroSavedTotalSteps = savedTotalSteps;

            if (TryBuildPendingIntroOverlay())
            {
                _pendingIntroOverlayBuild = false;
                HideAll();
                return;
            }

            _pendingIntroOverlayBuild = true;
        }

        public void DismissMachineIntro()
        {
            if (!_introVisible) return;

            _introVisible = false;
            _introDismissedThisSession = true;
            _pendingIntroOverlayBuild = false;
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }

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

        private bool TryRestoreRuntimePanelsAfterIntroDismiss()
        {
            if (!Application.isPlaying)
                return false;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
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

            EnsureToolRuntimeSubscription();
            HandleToolRuntimeStateChanged();

            return true;
        }

        private void HandleRuntimeStepStateChanged(StepStateChanged evt)
        {
            if (!Application.isPlaying || _introVisible || evt.Current != StepState.Active)
                return;

            // Ensure the tool-runtime subscription is wired up so the auto-equip
            // mechanism fires even if ShowStepShell hasn't been called yet for this step.
            EnsureToolRuntimeSubscription();
            TryRestoreRuntimePanelsAfterIntroDismiss();
            HandleToolRuntimeStateChanged();
        }

        private void HandleRuntimeStepNavigated(StepNavigated evt)
        {
            if (!Application.isPlaying || _introVisible)
                return;

            EnsureToolRuntimeSubscription();
            TryRestoreRuntimePanelsAfterIntroDismiss();
            HandleToolRuntimeStateChanged();
        }

        private void HandleRuntimeSessionCompleted(SessionCompleted evt)
        {
            if (!Application.isPlaying)
                return;

            int minutes = (int)(evt.TotalSeconds / 60f);
            int secs = (int)(evt.TotalSeconds % 60f);
            string timeStr = minutes > 0 ? $"{minutes}m {secs}s" : $"{secs}s";

            string machineName = "Assembly";
            if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
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
            _hintsUsed = hintsUsed;
            _failedAttempts = failedAttempts;
            _currentStepSeconds = currentStepSeconds;
            _totalSeconds = totalSeconds;
            _challengeActive = challengeActive;
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
                EnsureToolRuntimeSubscription();
            }

            _currentStepNumber = Mathf.Max(currentStepNumber, 0);
            _totalSteps = Mathf.Max(totalSteps, 0);
            _stepTitle = title;
            _instruction = instruction;
            _showConfirmButton = showConfirmButton;
            _showHintButton = showHintButton && IsHintDisplayAllowed;
            _confirmGate = confirmGate;
            _confirmUnlocked = ResolveInitialConfirmUnlock(confirmGate);
            _progressComplete = false;
            _suppressGateUnlock = confirmGate != ConfirmGate.None;
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

            if (_suppressGateUnlock)
            {
                _suppressGateUnlock = false;
            }
            else if (_confirmGate == ConfirmGate.SelectPart && !_confirmUnlocked)
            {
                _confirmUnlocked = true;
                RefreshStepPanel();
            }
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
            BuildToolCursorVisual(root);

            _stepPanelController.Bind(leftColumn);
            _sessionHudPanelController.Bind(leftColumn);
            _partInfoPanelController.Bind(rightColumn);
            _toolInfoPanelController.Bind(rightColumn);
            _toolDockPanelController.Bind(centerDock);
            BuildRepositionButtons(centerDock);

            _toolDockPanelController.ToggleRequested += HandleToolDockToggleRequested;
            _toolDockPanelController.ToolSelected += HandleToolSelected;
            _toolDockPanelController.UnequipRequested += HandleToolUnequipRequested;
            _toolDockPanelController.ToolHovered += HandleToolHovered;
            _toolDockPanelController.ToolHoverCleared += HandleToolHoverCleared;

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
            float? progressOverride = _progressComplete ? 1f : ResolveIntraStepProgress();
            StepPanelViewModel viewModel = _stepPresenter.Create(
                _currentStepNumber,
                _totalSteps,
                _stepTitle,
                _instruction,
                _showConfirmButton,
                _showHintButton,
                _confirmGate,
                _confirmUnlocked,
                showContextActionButton,
                contextActionLabel,
                contextActionEnabled,
                progressOverride);

            _stepPanelController.Show(viewModel);
        }

        private bool TryBuildGuidedStackActionState(out string actionLabel, out bool actionEnabled)
        {
            actionLabel = null;
            actionEnabled = false;

            if (!Application.isPlaying)
                return false;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
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

            if (_toolRuntimeController == null ||
                !_toolRuntimeController.TryGetPrimaryActionSnapshot(
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
            if (!_isBuilt)
            {
                return;
            }

            if (_hasActiveModeProfile && !_activeModeProfile.ShowSessionHud)
            {
                _sessionHudPanelController.Hide();
                return;
            }

            SessionHudViewModel viewModel = _sessionHudPresenter.Create(
                _hintToastActive && IsHintDisplayAllowed,
                _hintType,
                _hintTitle,
                _hintMessage,
                _challengeActive,
                _hintsUsed,
                _failedAttempts,
                _currentStepSeconds,
                _totalSeconds,
                _stepToastActive,
                _stepToastMessage,
                _milestoneActive,
                _milestoneMessage);

            if (viewModel.IsVisible)
            {
                _sessionHudPanelController.Show(viewModel);
            }
            else
            {
                _sessionHudPanelController.Hide();
            }
        }

        private void EnsureToolRuntimeSubscription()
        {
            if (_isToolRuntimeSubscribed && _toolRuntimeController != null)
                return;

            if (!ServiceRegistry.TryGet<ToolRuntimeController>(out var toolRuntime))
                return;

            _toolRuntimeController = toolRuntime;
            _toolRuntimeController.StateChanged += HandleToolRuntimeStateChanged;
            _isToolRuntimeSubscribed = true;
            HandleToolRuntimeStateChanged();
        }

        private void UnsubscribeFromToolRuntime()
        {
            if (_isToolRuntimeSubscribed && _toolRuntimeController != null)
            {
                _toolRuntimeController.StateChanged -= HandleToolRuntimeStateChanged;
            }

            _toolRuntimeController = null;
            _isToolRuntimeSubscribed = false;
        }

        private void HandleToolRuntimeStateChanged()
        {
            if (_toolRuntimeController == null)
                return;

            _activeToolId = _toolRuntimeController.ActiveToolId;

            // Clear the manual-unequip suppress flag when the step changes,
            // so the next step can auto-equip its required tool.
            string currentStepId = _toolRuntimeController.ActiveStepId;
            if (!string.Equals(_lastAutoEquipStepId, currentStepId, StringComparison.Ordinal))
            {
                _lastAutoEquipStepId = currentStepId;
                _suppressAutoEquip = false;
            }

            // Auto-equip: if the step requires a tool and nothing is equipped,
            // equip it automatically. Auto-unequip when moving to a step that
            // doesn't need one. The user still learns which tool is needed (the UI
            // shows it) and must find the target location and execute the action.
            TryAutoEquipRequiredTool();

            if (_confirmGate == ConfirmGate.EquipTool)
            {
                bool unlocked = IsEquipToolGateSatisfied();
                if (_confirmUnlocked != unlocked)
                {
                    _confirmUnlocked = unlocked;
                    RefreshStepPanel();
                }
            }

            if (!string.IsNullOrWhiteSpace(_activeToolId) && TryPopulateToolInfo(_activeToolId))
            {
                _partTool = _toolName;
            }

            if (!string.IsNullOrWhiteSpace(_hoveredToolId)
                && !_toolRuntimeController.TryGetTool(_hoveredToolId, out _))
            {
                _hoveredToolId = null;
            }

            RefreshToolDockPanel();
            RefreshToolInfoPanel();
            RefreshPartInfoPanel();
            RefreshStepPanel(); // Update progress bar with tool action progress
            TryAutoAdvanceTargetlessToolStep();
            TryAutoAdvanceEquipTaggedStepIfSatisfied();
        }

        private void RefreshToolDockPanel()
        {
            if (!_isBuilt || _toolDockPanelController == null || !_toolDockPanelController.IsBound)
                return;

            if (!Application.isPlaying || _toolRuntimeController == null || !_toolRuntimeController.HasPackage)
            {
                _toolDockPanelController.Hide();
                return;
            }

            ToolDockPanelViewModel viewModel = _toolDockPresenter.Create(
                _toolRuntimeController.GetAvailableTools(),
                _toolRuntimeController.GetRequiredToolIds(),
                _toolRuntimeController.ActiveToolId,
                _toolDockExpanded);

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

            string toolId = !string.IsNullOrWhiteSpace(_hoveredToolId)
                ? _hoveredToolId
                : _activeToolId;

            if (string.IsNullOrWhiteSpace(toolId) || !TryPopulateToolInfo(toolId))
            {
                _toolInfoPanelController.Hide();
                return;
            }

            ToolInfoPanelViewModel viewModel = _toolInfoPresenter.Create(
                _toolName,
                _toolCategory,
                _toolPurpose,
                _toolUsageNotes,
                _toolSafetyNotes);

            _toolInfoPanelController.Show(viewModel);
        }

        private void HandleToolDockToggleRequested()
        {
            ToggleToolDock();
        }

        private void HandleToolSelected(string toolId)
        {
            if (_toolRuntimeController == null || string.IsNullOrWhiteSpace(toolId))
                return;

            // User is manually choosing a tool — suppress auto-equip until next step
            _suppressAutoEquip = true;

            if (string.Equals(_toolRuntimeController.ActiveToolId, toolId, StringComparison.OrdinalIgnoreCase))
            {
                _toolRuntimeController.UnequipTool();
                _activeToolId = _toolRuntimeController.ActiveToolId;
                _hoveredToolId = null;
                RefreshToolDockPanel();
                RefreshToolInfoPanel();
                RefreshPartInfoPanel();
                return;
            }

            if (!_toolRuntimeController.EquipTool(toolId))
                return;

            _activeToolId = _toolRuntimeController.ActiveToolId;
            _hoveredToolId = null;

            if (TryPopulateToolInfo(_activeToolId))
            {
                _partTool = _toolName;
                RefreshPartInfoPanel();
            }

            RefreshToolDockPanel();
            RefreshToolInfoPanel();
            TryAutoAdvanceTargetlessToolStep();
            TryAutoAdvanceEquipTaggedStepIfSatisfied();
        }

        private void TryAutoEquipRequiredTool()
        {
            if (_suppressAutoEquip || _toolRuntimeController == null)
                return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null)
                return;

            bool hasRequiredToolActions = step.requiredToolActions != null && step.requiredToolActions.Length > 0;
            bool shouldKeepToolEquipped = step.IsToolAction;
            string toolId = ResolveRequiredToolForStep(step);

            if (hasRequiredToolActions)
            {
                if (step.IsPlacement)
                {
                    bool toolActionPending = true;
                    if (_toolRuntimeController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot) &&
                        snapshot.IsConfigured)
                    {
                        toolActionPending = !snapshot.IsCompleted;
                        if (!string.IsNullOrWhiteSpace(snapshot.ToolId))
                            toolId = snapshot.ToolId.Trim();
                    }

                    shouldKeepToolEquipped = toolActionPending;
                }
                else
                {
                    shouldKeepToolEquipped = true;
                }
            }

            if (!shouldKeepToolEquipped)
            {
                if (!string.IsNullOrWhiteSpace(_toolRuntimeController.ActiveToolId))
                {
                    _toolRuntimeController.UnequipTool();
                    _activeToolId = _toolRuntimeController.ActiveToolId;
                    OseLog.Info("[UI] Auto-unequipped tool (active step is in part-placement mode).");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(toolId))
                return;

            // Already have the right tool equipped
            if (string.Equals(_toolRuntimeController.ActiveToolId, toolId, StringComparison.OrdinalIgnoreCase))
                return;

            // Wrong tool or no tool — equip the correct one
            if (_toolRuntimeController.EquipTool(toolId))
            {
                _activeToolId = _toolRuntimeController.ActiveToolId;
                OseLog.Info($"[UI] Auto-equipped required tool '{toolId}'.");
            }
        }

        private void HandleToolUnequipRequested()
        {
            if (_toolRuntimeController == null)
                return;

            // Suppress auto-equip so it doesn't immediately re-equip on the
            // StateChanged callback. Cleared on the next step transition.
            _suppressAutoEquip = true;

            _toolRuntimeController.UnequipTool();
            _activeToolId = _toolRuntimeController.ActiveToolId;
            _hoveredToolId = null;
            RefreshToolDockPanel();
            RefreshToolInfoPanel();
            RefreshPartInfoPanel();
        }

        private void TryAutoAdvanceTargetlessToolStep()
        {
            if (_autoCompletingTargetlessToolStep || _toolRuntimeController == null)
                return;

            // Suppress auto-advance during explicit step navigation.
            if (ServiceRegistry.TryGet<MachineSessionController>(out var navCheck) && navCheck.IsNavigating)
                return;

            if (!_toolRuntimeController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot) ||
                !snapshot.IsConfigured ||
                snapshot.IsCompleted ||
                !string.IsNullOrWhiteSpace(snapshot.TargetId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_toolRuntimeController.ActiveToolId) ||
                !string.Equals(_toolRuntimeController.ActiveToolId, snapshot.ToolId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            _autoCompletingTargetlessToolStep = true;
            try
            {
                ToolRuntimeController.ToolActionExecutionResult toolResult =
                    _toolRuntimeController.TryExecutePrimaryAction();

                if (toolResult.Handled && toolResult.ShouldCompleteStep)
                {
                    stepController.CompleteStep(session.GetElapsedSeconds());
                }
            }
            finally
            {
                _autoCompletingTargetlessToolStep = false;
            }
        }

        private void TryAutoAdvanceEquipTaggedStepIfSatisfied()
        {
            if (_autoCompletingEquipTaggedStep || _toolRuntimeController == null)
                return;

            // Suppress auto-advance during explicit step navigation.
            if (ServiceRegistry.TryGet<MachineSessionController>(out var navSession) && navSession.IsNavigating)
                return;

            string activeToolId = _toolRuntimeController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(activeToolId))
                return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null || !HasEventTag(step.eventTags, "equip"))
                return;

            string requiredToolId = ResolveRequiredToolForStep(step);
            if (string.IsNullOrWhiteSpace(requiredToolId) ||
                !string.Equals(activeToolId, requiredToolId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _autoCompletingEquipTaggedStep = true;
            try
            {
                // Prefer consuming the authored tool action to preserve tool action events.
                if (_toolRuntimeController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot) &&
                    snapshot.IsConfigured &&
                    !snapshot.IsCompleted &&
                    string.IsNullOrWhiteSpace(snapshot.TargetId))
                {
                    ToolRuntimeController.ToolActionExecutionResult toolResult =
                        _toolRuntimeController.TryExecutePrimaryAction();

                    if (toolResult.Handled && toolResult.ShouldCompleteStep)
                    {
                        stepController.CompleteStep(session.GetElapsedSeconds());
                        return;
                    }
                }

                // Fallback: complete equip-tagged steps when the required tool is equipped.
                stepController.CompleteStep(session.GetElapsedSeconds());
            }
            finally
            {
                _autoCompletingEquipTaggedStep = false;
            }
        }

        private static string ResolveRequiredToolForStep(StepDefinition step)
        {
            if (step == null)
                return null;

            if (step.requiredToolActions != null)
            {
                for (int i = 0; i < step.requiredToolActions.Length; i++)
                {
                    ToolActionDefinition action = step.requiredToolActions[i];
                    if (action != null && !string.IsNullOrWhiteSpace(action.toolId))
                        return action.toolId.Trim();
                }
            }

            if (step.relevantToolIds != null)
            {
                for (int i = 0; i < step.relevantToolIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(step.relevantToolIds[i]))
                        return step.relevantToolIds[i].Trim();
                }
            }

            return null;
        }

        private static bool HasEventTag(string[] tags, string expectedTag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrWhiteSpace(expectedTag))
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], expectedTag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void HandleToolHovered(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return;

            _hoveredToolId = toolId;
            RefreshToolInfoPanel();
        }

        private void HandleToolHoverCleared()
        {
            _hoveredToolId = null;
            RefreshToolInfoPanel();
        }

        private bool TryPopulateToolInfo(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return false;

            if (_toolRuntimeController == null || !_toolRuntimeController.TryGetTool(toolId, out ToolDefinition tool))
                return false;

            _toolName = tool.GetDisplayName();
            _toolCategory = string.IsNullOrWhiteSpace(tool.category) ? "General" : tool.category.Trim();
            _toolPurpose = string.IsNullOrWhiteSpace(tool.purpose)
                ? "Tool purpose metadata will be supplied by runtime content."
                : tool.purpose.Trim();
            _toolUsageNotes = string.IsNullOrWhiteSpace(tool.usageNotes)
                ? "Tool usage notes metadata will be supplied by runtime content."
                : tool.usageNotes.Trim();
            _toolSafetyNotes = string.IsNullOrWhiteSpace(tool.safetyNotes)
                ? "Tool safety notes metadata will be supplied by runtime content."
                : tool.safetyNotes.Trim();
            return true;
        }

        private bool ResolveInitialConfirmUnlock(ConfirmGate confirmGate)
        {
            if (confirmGate == ConfirmGate.None)
                return true;

            if (confirmGate == ConfirmGate.EquipTool)
                return IsEquipToolGateSatisfied();

            return false;
        }

        private bool IsEquipToolGateSatisfied()
        {
            if (_toolRuntimeController == null)
                return false;

            string activeToolId = _toolRuntimeController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(activeToolId))
                return false;

            string requiredToolId = ResolveRequiredToolForEquipGate();
            if (string.IsNullOrWhiteSpace(requiredToolId))
                return false;

            return string.Equals(activeToolId, requiredToolId, StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveRequiredToolForEquipGate()
        {
            if (_toolRuntimeController == null)
                return null;

            if (_toolRuntimeController.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot) &&
                snapshot.IsConfigured &&
                !string.IsNullOrWhiteSpace(snapshot.ToolId))
            {
                return snapshot.ToolId.Trim();
            }

            string[] requiredToolIds = _toolRuntimeController.GetRequiredToolIds();
            if (requiredToolIds == null || requiredToolIds.Length == 0)
                return null;

            for (int i = 0; i < requiredToolIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(requiredToolIds[i]))
                    return requiredToolIds[i].Trim();
            }

            return null;
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
            if (_toolDockPanelController != null)
            {
                _toolDockPanelController.ToggleRequested -= HandleToolDockToggleRequested;
                _toolDockPanelController.ToolSelected -= HandleToolSelected;
                _toolDockPanelController.UnequipRequested -= HandleToolUnequipRequested;
                _toolDockPanelController.ToolHovered -= HandleToolHovered;
                _toolDockPanelController.ToolHoverCleared -= HandleToolHoverCleared;
            }

            _stepPanelController?.Unbind();
            _partInfoPanelController?.Unbind();
            _sessionHudPanelController?.Unbind();
            _toolInfoPanelController?.Unbind();
            _toolDockPanelController?.Unbind();
            _isBuilt = false;
            _rootElement = null;
            _toolCursorBadge = null;
            _toolCursorLabel = null;
            _introDismissedThisSession = false;
            _pendingIntroOverlayBuild = false;
            _activeToolId = null;
            _hoveredToolId = null;
            _suppressAutoEquip = false;
            _lastAutoEquipStepId = null;
            _autoCompletingTargetlessToolStep = false;
            _autoCompletingEquipTaggedStep = false;

            if (_repositionButton != null)
            {
                _repositionButton.clicked -= HandleRepositionToggleClicked;
                _repositionButton = null;
            }
            if (_resetPositionButton != null)
            {
                _resetPositionButton.clicked -= HandleResetPositionClicked;
                _resetPositionButton = null;
            }
            if (_decreaseScaleButton != null)
            {
                _decreaseScaleButton.clicked -= HandleDecreaseScaleClicked;
                _decreaseScaleButton = null;
            }
            if (_increaseScaleButton != null)
            {
                _increaseScaleButton.clicked -= HandleIncreaseScaleClicked;
                _increaseScaleButton = null;
            }
            _assemblyScaleLabel = null;
        }

        // ── Reposition UI ──

        private void BuildRepositionButtons(VisualElement parent)
        {
            var row = new VisualElement();
            row.name = "ose-reposition-row";
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            row.style.marginBottom = 8f;
            row.pickingMode = PickingMode.Ignore;

            _repositionButton = new Button();
            _repositionButton.text = "Move Assembly";
            _repositionButton.style.height = 42f;
            _repositionButton.style.paddingLeft = 18f;
            _repositionButton.style.paddingRight = 18f;
            _repositionButton.style.borderTopLeftRadius = 12f;
            _repositionButton.style.borderTopRightRadius = 12f;
            _repositionButton.style.borderBottomLeftRadius = 12f;
            _repositionButton.style.borderBottomRightRadius = 12f;
            _repositionButton.style.fontSize = 14f;
            _repositionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            ApplyRepositionButtonStyle(false);
            _repositionButton.clicked += HandleRepositionToggleClicked;

            var scaleCluster = new VisualElement();
            scaleCluster.name = "ose-assembly-scale-cluster";
            scaleCluster.style.flexDirection = FlexDirection.Row;
            scaleCluster.style.alignItems = Align.Center;
            scaleCluster.style.marginLeft = 8f;
            scaleCluster.style.paddingLeft = 8f;
            scaleCluster.style.paddingRight = 8f;
            scaleCluster.style.paddingTop = 4f;
            scaleCluster.style.paddingBottom = 4f;
            scaleCluster.style.backgroundColor = new Color(0.16f, 0.16f, 0.19f, 0.92f);
            scaleCluster.style.borderTopLeftRadius = 10f;
            scaleCluster.style.borderTopRightRadius = 10f;
            scaleCluster.style.borderBottomLeftRadius = 10f;
            scaleCluster.style.borderBottomRightRadius = 10f;

            _decreaseScaleButton = new Button();
            _decreaseScaleButton.text = "-";
            _decreaseScaleButton.style.width = 30f;
            _decreaseScaleButton.style.height = 30f;
            _decreaseScaleButton.style.fontSize = 16f;
            _decreaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _decreaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _decreaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _decreaseScaleButton.style.borderTopLeftRadius = 8f;
            _decreaseScaleButton.style.borderTopRightRadius = 8f;
            _decreaseScaleButton.style.borderBottomLeftRadius = 8f;
            _decreaseScaleButton.style.borderBottomRightRadius = 8f;
            _decreaseScaleButton.clicked += HandleDecreaseScaleClicked;

            _assemblyScaleLabel = new Label("Scale 1.00x");
            _assemblyScaleLabel.style.minWidth = 88f;
            _assemblyScaleLabel.style.marginLeft = 8f;
            _assemblyScaleLabel.style.marginRight = 8f;
            _assemblyScaleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _assemblyScaleLabel.style.color = new Color(0.95f, 0.92f, 0.82f);
            _assemblyScaleLabel.style.fontSize = 13f;
            _assemblyScaleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _increaseScaleButton = new Button();
            _increaseScaleButton.text = "+";
            _increaseScaleButton.style.width = 30f;
            _increaseScaleButton.style.height = 30f;
            _increaseScaleButton.style.fontSize = 16f;
            _increaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _increaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _increaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _increaseScaleButton.style.borderTopLeftRadius = 8f;
            _increaseScaleButton.style.borderTopRightRadius = 8f;
            _increaseScaleButton.style.borderBottomLeftRadius = 8f;
            _increaseScaleButton.style.borderBottomRightRadius = 8f;
            _increaseScaleButton.clicked += HandleIncreaseScaleClicked;

            _resetPositionButton = new Button();
            _resetPositionButton.text = "Reset";
            _resetPositionButton.style.height = 34f;
            _resetPositionButton.style.paddingLeft = 12f;
            _resetPositionButton.style.paddingRight = 12f;
            _resetPositionButton.style.marginLeft = 6f;
            _resetPositionButton.style.borderTopLeftRadius = 8f;
            _resetPositionButton.style.borderTopRightRadius = 8f;
            _resetPositionButton.style.borderBottomLeftRadius = 8f;
            _resetPositionButton.style.borderBottomRightRadius = 8f;
            _resetPositionButton.style.fontSize = 12f;
            _resetPositionButton.style.backgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.9f);
            _resetPositionButton.style.color = new Color(0.85f, 0.85f, 0.85f);
            _resetPositionButton.style.display = DisplayStyle.None;
            _resetPositionButton.clicked += HandleResetPositionClicked;

            row.Add(_repositionButton);
            scaleCluster.Add(_decreaseScaleButton);
            scaleCluster.Add(_assemblyScaleLabel);
            scaleCluster.Add(_increaseScaleButton);
            row.Add(scaleCluster);
            row.Add(_resetPositionButton);
            parent.Insert(0, row);
            RefreshAssemblyScaleUi();
        }

        private void ApplyRepositionButtonStyle(bool active)
        {
            if (_repositionButton == null) return;

            if (active)
            {
                _repositionButton.text = "Done Moving";
                _repositionButton.style.backgroundColor = new Color(0.55f, 0.40f, 0.10f, 0.95f);
                _repositionButton.style.color = new Color(1f, 0.95f, 0.8f);
            }
            else
            {
                _repositionButton.text = "Move Assembly";
                _repositionButton.style.backgroundColor = new Color(0.38f, 0.28f, 0.12f, 0.95f);
                _repositionButton.style.color = new Color(1f, 0.92f, 0.75f);
            }
        }

        private void HandleRepositionToggleClicked()
        {
            RuntimeEventBus.Publish(new RepositionModeChanged(!_repositionActive));
        }

        private void HandleResetPositionClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.ResetPosition();

            RefreshAssemblyScaleUi();
        }

        private void HandleRepositionModeChangedUI(RepositionModeChanged evt)
        {
            _repositionActive = evt.IsActive;
            ApplyRepositionButtonStyle(evt.IsActive);
            RefreshAssemblyScaleUi();
        }

        private void HandleDecreaseScaleClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.DecreaseScale();

            RefreshAssemblyScaleUi();
        }

        private void HandleIncreaseScaleClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.IncreaseScale();

            RefreshAssemblyScaleUi();
        }

        private void RefreshAssemblyScaleUi()
        {
            if (_assemblyScaleLabel == null)
                return;

            float scale = 1f;
            float defaultScale = 1f;
            bool canDecrease = false;
            bool canIncrease = false;

            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller) && controller != null)
            {
                scale = controller.CurrentScaleMultiplier;
                defaultScale = controller.DefaultScaleMultiplier;
                canDecrease = controller.CanDecreaseScale;
                canIncrease = controller.CanIncreaseScale;
            }

            _assemblyScaleLabel.text = $"Scale {scale:0.00}x";
            _assemblyScaleLabel.tooltip = $"1.00x = authored package size in Unity units (meters-based content). Reset returns to {defaultScale:0.00}x for this package.";
            _decreaseScaleButton?.SetEnabled(canDecrease);
            _increaseScaleButton?.SetEnabled(canIncrease);

            if (_resetPositionButton != null)
            {
                bool showReset = _repositionActive || Mathf.Abs(scale - defaultScale) > 0.001f;
                _resetPositionButton.style.display = showReset ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── Machine Intro Overlay ──

        private bool TryBuildPendingIntroOverlay()
        {
            if (!_isBuilt)
                return false;

            return BuildIntroOverlay(
                _pendingIntroTitle,
                _pendingIntroDescription,
                _pendingIntroDifficulty,
                _pendingIntroEstimatedMinutes,
                _pendingIntroLearningObjectives,
                _pendingIntroImageRef,
                _pendingIntroSavedCompletedSteps,
                _pendingIntroSavedTotalSteps);
        }

        private bool BuildIntroOverlay(string title, string description, string difficulty,
            int estimatedMinutes, string[] learningObjectives, string imageRef,
            int savedCompletedSteps = 0, int savedTotalSteps = 0)
        {
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }

            VisualElement root = _rootElement;
            if (root == null)
            {
                var doc = GetComponent<UIDocument>();
                root = doc != null ? doc.rootVisualElement : null;
                if (root != null)
                    _rootElement = root;
            }

            if (root == null)
                return false;

            bool hasSavedProgress = savedCompletedSteps > 0 && savedTotalSteps > 0;

            // Fullscreen semi-transparent backdrop
            _introOverlay = new VisualElement();
            _introOverlay.name = "ose-intro-overlay";
            _introOverlay.style.position = Position.Absolute;
            _introOverlay.style.left = 0f;
            _introOverlay.style.right = 0f;
            _introOverlay.style.top = 0f;
            _introOverlay.style.bottom = 0f;
            _introOverlay.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            _introOverlay.style.alignItems = Align.Center;
            _introOverlay.style.justifyContent = Justify.Center;
            _introOverlay.pickingMode = PickingMode.Position;

            // Card container — fits within screen without scrolling
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 0.98f);
            card.style.borderTopLeftRadius = 16f;
            card.style.borderTopRightRadius = 16f;
            card.style.borderBottomLeftRadius = 16f;
            card.style.borderBottomRightRadius = 16f;
            card.style.paddingTop = 24f;
            card.style.paddingBottom = 28f;
            card.style.paddingLeft = 32f;
            card.style.paddingRight = 32f;
            card.style.maxWidth = 480f;
            card.style.minWidth = 320f;
            card.style.maxHeight = new Length(92f, LengthUnit.Percent);
            card.style.alignItems = Align.Center;
            // Subtle border
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderBottomColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderLeftColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderRightColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);

            // Content area — can shrink/scroll if needed, buttons stay fixed below
            var contentArea = new ScrollView(ScrollViewMode.Vertical);
            contentArea.style.flexGrow = 1f;
            contentArea.style.flexShrink = 1f;
            contentArea.style.alignItems = Align.Center;

            // Image placeholder (will show actual image when imageRef is loaded)
            var imageContainer = new VisualElement();
            imageContainer.style.width = 240f;
            imageContainer.style.height = 140f;
            imageContainer.style.backgroundColor = new Color(0.18f, 0.20f, 0.24f, 1f);
            imageContainer.style.borderTopLeftRadius = 10f;
            imageContainer.style.borderTopRightRadius = 10f;
            imageContainer.style.borderBottomLeftRadius = 10f;
            imageContainer.style.borderBottomRightRadius = 10f;
            imageContainer.style.marginBottom = 14f;
            imageContainer.style.alignItems = Align.Center;
            imageContainer.style.justifyContent = Justify.Center;

            // Try loading the image from StreamingAssets
            if (!string.IsNullOrWhiteSpace(imageRef))
                TryLoadIntroImage(imageContainer, imageRef);
            else
            {
                var placeholder = new Label("[ Machine Preview ]");
                placeholder.style.color = new Color(0.5f, 0.52f, 0.58f);
                placeholder.style.fontSize = 14f;
                imageContainer.Add(placeholder);
            }

            contentArea.Add(imageContainer);

            // Title
            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 20f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.95f, 0.96f, 0.98f);
            titleLabel.style.marginBottom = 8f;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.maxWidth = 420f;
            contentArea.Add(titleLabel);

            // Difficulty + time badges
            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.marginBottom = 10f;

            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var diffBadge = CreateBadge(CapitalizeFirst(difficulty), DifficultyColor(difficulty));
                badgeRow.Add(diffBadge);
            }

            if (estimatedMinutes > 0)
            {
                string timeText = estimatedMinutes >= 60
                    ? $"{estimatedMinutes / 60}h {estimatedMinutes % 60}m"
                    : $"{estimatedMinutes} min";
                var timeBadge = CreateBadge(timeText, new Color(0.22f, 0.45f, 0.7f, 1f));
                timeBadge.style.marginLeft = 8f;
                badgeRow.Add(timeBadge);
            }

            contentArea.Add(badgeRow);

            // Description
            if (!string.IsNullOrWhiteSpace(description))
            {
                var descLabel = new Label(description);
                descLabel.style.fontSize = 12f;
                descLabel.style.color = new Color(0.75f, 0.78f, 0.82f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginBottom = 10f;
                descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                descLabel.style.maxWidth = 400f;
                contentArea.Add(descLabel);
            }

            // Learning objectives
            if (learningObjectives != null && learningObjectives.Length > 0)
            {
                var objectivesHeader = new Label("What you'll learn:");
                objectivesHeader.style.fontSize = 11f;
                objectivesHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                objectivesHeader.style.color = new Color(0.6f, 0.75f, 0.9f);
                objectivesHeader.style.marginBottom = 4f;
                objectivesHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                objectivesHeader.style.alignSelf = Align.FlexStart;
                contentArea.Add(objectivesHeader);

                int maxObjectives = Mathf.Min(learningObjectives.Length, 4);
                for (int i = 0; i < maxObjectives; i++)
                {
                    if (string.IsNullOrWhiteSpace(learningObjectives[i])) continue;

                    var objLabel = new Label($"  \u2022  {learningObjectives[i].Trim()}");
                    objLabel.style.fontSize = 11f;
                    objLabel.style.color = new Color(0.68f, 0.72f, 0.78f);
                    objLabel.style.marginBottom = 2f;
                    objLabel.style.whiteSpace = WhiteSpace.Normal;
                    objLabel.style.alignSelf = Align.FlexStart;
                    contentArea.Add(objLabel);
                }
            }

            card.Add(contentArea);

            // ── Separator line before action area ──
            var separator = new VisualElement();
            separator.style.width = new Length(90f, LengthUnit.Percent);
            separator.style.height = 1f;
            separator.style.backgroundColor = new Color(0.25f, 0.27f, 0.32f, 0.5f);
            separator.style.marginTop = 10f;
            separator.style.marginBottom = 14f;
            separator.style.flexShrink = 0f;
            card.Add(separator);

            // ── Progress display (only when saved progress exists) ──
            if (hasSavedProgress)
            {
                float percent = (float)savedCompletedSteps / savedTotalSteps;
                int percentInt = Mathf.RoundToInt(percent * 100f);

                // Percent label
                var percentLabel = new Label($"{percentInt}% Complete");
                percentLabel.style.fontSize = 15f;
                percentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                percentLabel.style.color = new Color(0.5f, 0.85f, 0.6f);
                percentLabel.style.marginBottom = 3f;
                percentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(percentLabel);

                // Step X of Y
                var stepLabel = new Label($"Step {savedCompletedSteps} of {savedTotalSteps}");
                stepLabel.style.fontSize = 11f;
                stepLabel.style.color = new Color(0.6f, 0.63f, 0.68f);
                stepLabel.style.marginBottom = 6f;
                stepLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(stepLabel);

                // Progress bar
                var progressBarBg = new VisualElement();
                progressBarBg.style.width = new Length(80f, LengthUnit.Percent);
                progressBarBg.style.height = 8f;
                progressBarBg.style.backgroundColor = new Color(0.2f, 0.22f, 0.26f, 1f);
                progressBarBg.style.borderTopLeftRadius = 5f;
                progressBarBg.style.borderTopRightRadius = 5f;
                progressBarBg.style.borderBottomLeftRadius = 5f;
                progressBarBg.style.borderBottomRightRadius = 5f;
                progressBarBg.style.marginBottom = 14f;

                var progressBarFill = new VisualElement();
                progressBarFill.style.width = new Length(percent * 100f, LengthUnit.Percent);
                progressBarFill.style.height = 8f;
                progressBarFill.style.backgroundColor = new Color(0.2f, 0.65f, 0.4f, 1f);
                progressBarFill.style.borderTopLeftRadius = 5f;
                progressBarFill.style.borderTopRightRadius = 5f;
                progressBarFill.style.borderBottomLeftRadius = 5f;
                progressBarFill.style.borderBottomRightRadius = 5f;
                progressBarBg.Add(progressBarFill);

                card.Add(progressBarBg);
            }

            // ── Button area — never shrinks, always visible ──
            var buttonArea = new VisualElement();
            buttonArea.style.alignItems = Align.Center;
            buttonArea.style.width = new Length(100f, LengthUnit.Percent);
            buttonArea.style.flexShrink = 0f;

            // Continue / Resume button
            var continueBtn = new Button();
            continueBtn.text = hasSavedProgress ? "Resume" : "Begin Assembly";
            continueBtn.style.height = 42f;
            continueBtn.style.width = 200f;
            continueBtn.style.fontSize = 15f;
            continueBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            continueBtn.style.backgroundColor = new Color(0.2f, 0.65f, 0.4f, 1f);
            continueBtn.style.color = Color.white;
            continueBtn.style.borderTopLeftRadius = 14f;
            continueBtn.style.borderTopRightRadius = 14f;
            continueBtn.style.borderBottomLeftRadius = 14f;
            continueBtn.style.borderBottomRightRadius = 14f;
            continueBtn.style.borderTopWidth = 0f;
            continueBtn.style.borderBottomWidth = 0f;
            continueBtn.style.borderLeftWidth = 0f;
            continueBtn.style.borderRightWidth = 0f;
            continueBtn.clicked += () =>
            {
                DismissMachineIntro();
                RuntimeEventBus.Publish(new MachineIntroDismissed(_introMachineId ?? string.Empty));
            };
            buttonArea.Add(continueBtn);

            // Reset Progress button (only when saved progress exists)
            if (hasSavedProgress)
            {
                var resetBtn = new Button();
                resetBtn.text = "Reset Progress";
                resetBtn.style.height = 30f;
                resetBtn.style.width = 200f;
                resetBtn.style.fontSize = 11f;
                resetBtn.style.marginTop = 8f;
                resetBtn.style.backgroundColor = new Color(0.16f, 0.16f, 0.20f, 1f);
                resetBtn.style.color = new Color(0.75f, 0.38f, 0.38f);
                resetBtn.style.borderTopLeftRadius = 10f;
                resetBtn.style.borderTopRightRadius = 10f;
                resetBtn.style.borderBottomLeftRadius = 10f;
                resetBtn.style.borderBottomRightRadius = 10f;
                resetBtn.style.borderTopWidth = 1f;
                resetBtn.style.borderBottomWidth = 1f;
                resetBtn.style.borderLeftWidth = 1f;
                resetBtn.style.borderRightWidth = 1f;
                resetBtn.style.borderTopColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderBottomColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderLeftColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderRightColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.clicked += () =>
                {
                    DismissMachineIntro();
                    RuntimeEventBus.Publish(new MachineIntroReset(_introMachineId ?? string.Empty));
                };
                buttonArea.Add(resetBtn);
            }

            card.Add(buttonArea);

            _introOverlay.Add(card);
            root.Add(_introOverlay);
            return true;
        }

        private static VisualElement CreateBadge(string text, Color bgColor)
        {
            var badge = new VisualElement();
            badge.style.backgroundColor = bgColor;
            badge.style.borderTopLeftRadius = 6f;
            badge.style.borderTopRightRadius = 6f;
            badge.style.borderBottomLeftRadius = 6f;
            badge.style.borderBottomRightRadius = 6f;
            badge.style.paddingLeft = 10f;
            badge.style.paddingRight = 10f;
            badge.style.paddingTop = 4f;
            badge.style.paddingBottom = 4f;

            var label = new Label(text);
            label.style.fontSize = 11f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Color.white;
            badge.Add(label);

            return badge;
        }

        private static Color DifficultyColor(string difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return new Color(0.4f, 0.4f, 0.45f, 1f);

            return difficulty.Trim().ToLowerInvariant() switch
            {
                "beginner" => new Color(0.2f, 0.6f, 0.35f, 1f),
                "intermediate" => new Color(0.7f, 0.55f, 0.15f, 1f),
                "advanced" => new Color(0.7f, 0.25f, 0.2f, 1f),
                _ => new Color(0.4f, 0.4f, 0.45f, 1f)
            };
        }

        private static string CapitalizeFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private async void TryLoadIntroImage(VisualElement container, string imageRef)
        {
            // Resolve image path from StreamingAssets or package path
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, imageRef);
            if (!System.IO.File.Exists(path))
            {
                // Try under MachinePackages
                string packageRelative = System.IO.Path.Combine(
                    Application.streamingAssetsPath, "MachinePackages", imageRef);
                if (System.IO.File.Exists(packageRelative))
                    path = packageRelative;
                else
                {
                    var placeholder = new Label("[ Image not found ]");
                    placeholder.style.color = new Color(0.5f, 0.52f, 0.58f);
                    placeholder.style.fontSize = 12f;
                    container.Add(placeholder);
                    return;
                }
            }

            // Load texture from file
            byte[] data = System.IO.File.ReadAllBytes(path);
            if (data == null || data.Length == 0) return;

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(data))
            {
                container.style.backgroundImage = new StyleBackground(tex);
                container.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
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
            {
                _selectionService = FindFirstObjectByType<SelectionService>();
            }

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
                _selectionService = FindFirstObjectByType<SelectionService>();

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
                _hintToastActive = false;
                _hintHideAtSeconds = 0f;
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

        private void BuildToolCursorVisual(VisualElement root)
        {
            _toolCursorBadge = new VisualElement();
            _toolCursorBadge.name = "ose-tool-cursor-badge";
            _toolCursorBadge.style.position = Position.Absolute;
            _toolCursorBadge.style.width = ToolCursorBadgeWidth;
            _toolCursorBadge.style.height = ToolCursorBadgeHeight;
            _toolCursorBadge.style.alignItems = Align.Center;
            _toolCursorBadge.style.justifyContent = Justify.Center;
            _toolCursorBadge.style.paddingLeft = 10f;
            _toolCursorBadge.style.paddingRight = 10f;
            _toolCursorBadge.style.paddingTop = 4f;
            _toolCursorBadge.style.paddingBottom = 4f;
            _toolCursorBadge.style.backgroundColor = new Color(0.20f, 0.14f, 0.06f, 0.92f);
            _toolCursorBadge.style.borderTopLeftRadius = 8f;
            _toolCursorBadge.style.borderTopRightRadius = 8f;
            _toolCursorBadge.style.borderBottomLeftRadius = 8f;
            _toolCursorBadge.style.borderBottomRightRadius = 8f;
            _toolCursorBadge.style.borderTopColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderRightColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderBottomColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderLeftColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.display = DisplayStyle.None;
            _toolCursorBadge.pickingMode = PickingMode.Ignore;

            _toolCursorLabel = new Label("Tool");
            _toolCursorLabel.style.fontSize = 12f;
            _toolCursorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolCursorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toolCursorLabel.style.color = new Color(1f, 0.95f, 0.75f, 1f);
            _toolCursorBadge.Add(_toolCursorLabel);

            root.Add(_toolCursorBadge);
        }

        private void UpdateToolCursorVisual()
        {
            if (!_isBuilt || _toolCursorBadge == null || _toolCursorLabel == null)
                return;

            string activeToolId = _toolRuntimeController != null ? _toolRuntimeController.ActiveToolId : null;
            if (!Application.isPlaying ||
                _toolRuntimeController == null ||
                string.IsNullOrWhiteSpace(activeToolId) ||
                !TryPopulateToolInfo(activeToolId))
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 screenPos, out bool isTouchInput))
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            IPanel panel = _toolCursorBadge.panel;
            if (panel == null || panel.visualTree == null)
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);

            float x = panelPos.x - (ToolCursorBadgeWidth * 0.5f);
            Rect panelBounds = panel.visualTree.worldBound;
            float y = (panelBounds.height - panelPos.y) + MouseCursorOffsetY;

            if (isTouchInput)
            {
                y += TouchCursorOffsetY;
            }

            float maxX = Mathf.Max(4f, panelBounds.width - ToolCursorBadgeWidth - 4f);
            float maxY = Mathf.Max(4f, panelBounds.height - ToolCursorBadgeHeight - 4f);
            x = Mathf.Clamp(x, 4f, maxX);
            y = Mathf.Clamp(y, 4f, maxY);

            _toolCursorLabel.text = $"Tool: {_toolName}";
            _toolCursorBadge.style.left = x;
            _toolCursorBadge.style.top = y;
            _toolCursorBadge.style.display = DisplayStyle.Flex;
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPos, out bool isTouchInput)
        {
            screenPos = default;
            isTouchInput = false;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                isTouchInput = true;
                return true;
            }

            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}
