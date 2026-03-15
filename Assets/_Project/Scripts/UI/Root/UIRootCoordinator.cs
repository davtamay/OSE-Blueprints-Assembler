using System;
using OSE.App;
using OSE.Core;
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
        private StepPanelController _stepPanelController;
        private PartInfoPanelController _partInfoPanelController;
        private SessionHudPanelController _sessionHudPanelController;

        private int _currentStepNumber = 1;
        private int _totalSteps = 1;
        private string _stepTitle = "Assembly Step";
        private string _instruction = "Instruction text will be provided by the active runtime step.";

        private string _partName = "Selected Part";
        private string _partFunction = "Function metadata will be supplied by runtime content.";
        private string _partMaterial = "Material metadata will be supplied by runtime content.";
        private string _partTool = "Tool metadata will be supplied by runtime content.";
        private string _partSearchTerms = "Search terms will be supplied by runtime content.";

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
        }

        private void OnDisable()
        {
            UnregisterPresentationAdapter();
            TeardownUi();
        }

        private void Update()
        {
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
        }

        public void SetSessionMode(SessionMode mode)
        {
            ApplySessionMode(mode);
        }

        public bool IsHintDisplayAllowed => _hasActiveModeProfile ? _activeModeProfile.AllowHints : true;

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
            _partTool = string.IsNullOrWhiteSpace(toolId)
                ? _partTool
                : toolId;

            RefreshPartInfoPanel();
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
            RefreshStepPanel();
            RefreshSessionHudPanel();
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

        public void HideAll()
        {
            _stepPanelController?.Hide();
            _partInfoPanelController?.Hide();
            _sessionHudPanelController?.Hide();
        }

        public void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction, bool showConfirmButton = false, bool showHintButton = false, ConfirmGate confirmGate = ConfirmGate.None)
        {
            _currentStepNumber = Mathf.Max(currentStepNumber, 0);
            _totalSteps = Mathf.Max(totalSteps, 0);
            _stepTitle = title;
            _instruction = instruction;
            _showConfirmButton = showConfirmButton;
            _showHintButton = showHintButton && IsHintDisplayAllowed;
            _confirmGate = confirmGate;
            _confirmUnlocked = confirmGate == ConfirmGate.None;
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
            _partName = partName;
            _partFunction = function;
            _partMaterial = material;
            _partTool = tool;
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
                OseLog.Warn("[UI] Root coordinator could not prepare a UIDocument root.");
                return false;
            }

            UIToolkitStyleUtility.ApplyRootLayout(root);

            VisualElement leftColumn = new VisualElement();
            leftColumn.name = "ose-ui-column-left";
            UIToolkitStyleUtility.ApplyColumnLayout(leftColumn, TextAnchor.UpperLeft);

            VisualElement rightColumn = new VisualElement();
            rightColumn.name = "ose-ui-column-right";
            UIToolkitStyleUtility.ApplyColumnLayout(rightColumn, TextAnchor.UpperRight);

            root.Add(leftColumn);
            root.Add(rightColumn);

            _stepPanelController.Bind(leftColumn);
            _sessionHudPanelController.Bind(leftColumn);
            _partInfoPanelController.Bind(rightColumn);

            _isBuilt = true;

            if (_showShellPlaceholders)
            {
                RefreshStepPanel();
                RefreshPartInfoPanel();
                RefreshSessionHudPanel();
            }
            else
            {
                HideAll();
            }

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

            float? progressOverride = _progressComplete ? 1f : null;
            StepPanelViewModel viewModel = _stepPresenter.Create(
                _currentStepNumber,
                _totalSteps,
                _stepTitle,
                _instruction,
                _showConfirmButton,
                _showHintButton,
                _confirmGate,
                _confirmUnlocked,
                progressOverride);

            _stepPanelController.Show(viewModel);
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
            _stepPanelController?.Unbind();
            _partInfoPanelController?.Unbind();
            _sessionHudPanelController?.Unbind();
            _isBuilt = false;
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
            _stepPanelController ??= new StepPanelController();
            _partInfoPanelController ??= new PartInfoPanelController();
            _sessionHudPanelController ??= new SessionHudPanelController();

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
