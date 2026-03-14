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
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocumentBootstrap))]
    public sealed class UIRootCoordinator : MonoBehaviour, IPresentationAdapter
    {
        [SerializeField] private UIDocumentBootstrap _documentBootstrap;
        [SerializeField] private bool _showShellPlaceholders = true;

        private StepPanelPresenter _stepPresenter;
        private PartInfoPanelPresenter _partInfoPresenter;
        private StepPanelController _stepPanelController;
        private PartInfoPanelController _partInfoPanelController;

        private int _currentStepNumber = 1;
        private int _totalSteps = 1;
        private string _stepTitle = "Assembly Step";
        private string _instruction = "Instruction text will be provided by the active runtime step.";

        private string _partName = "Selected Part";
        private string _partFunction = "Function metadata will be supplied by runtime content.";
        private string _partMaterial = "Material metadata will be supplied by runtime content.";
        private string _partTool = "Tool metadata will be supplied by runtime content.";
        private string _partSearchTerms = "Search terms will be supplied by runtime content.";

        private bool _isBuilt;

        private void Awake()
        {
            if (_documentBootstrap == null)
            {
                _documentBootstrap = GetComponent<UIDocumentBootstrap>();
            }

            _stepPresenter = new StepPanelPresenter();
            _partInfoPresenter = new PartInfoPanelPresenter();
            _stepPanelController = new StepPanelController();
            _partInfoPanelController = new PartInfoPanelController();
        }

        private void OnEnable()
        {
            if (!BuildUi())
            {
                return;
            }

            RegisterPresentationAdapter();
        }

        private void OnDisable()
        {
            UnregisterPresentationAdapter();
            TeardownUi();
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

        public void ShowMilestoneFeedback(string milestoneKey)
        {
            OseLog.VerboseInfo($"[UI] Milestone feedback requested before milestone panels exist: {milestoneKey}");
        }

        public void HideAll()
        {
            _stepPanelController?.Hide();
            _partInfoPanelController?.Hide();
        }

        public void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction)
        {
            _currentStepNumber = Mathf.Max(currentStepNumber, 0);
            _totalSteps = Mathf.Max(totalSteps, 0);
            _stepTitle = title;
            _instruction = instruction;
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
            _partInfoPanelController.Bind(rightColumn);

            _isBuilt = true;

            if (_showShellPlaceholders)
            {
                RefreshStepPanel();
                RefreshPartInfoPanel();
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

            StepPanelViewModel viewModel = _stepPresenter.Create(
                _currentStepNumber,
                _totalSteps,
                _stepTitle,
                _instruction);

            _stepPanelController.Show(viewModel);
        }

        private void RefreshPartInfoPanel()
        {
            if (!_isBuilt)
            {
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

        private void RegisterPresentationAdapter()
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter existingAdapter) &&
                !ReferenceEquals(existingAdapter, this))
            {
                OseLog.Warn("[UI] Replacing an existing IPresentationAdapter registration.");
            }

            ServiceRegistry.Register<IPresentationAdapter>(this);
        }

        private void UnregisterPresentationAdapter()
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter existingAdapter) &&
                ReferenceEquals(existingAdapter, this))
            {
                ServiceRegistry.Unregister<IPresentationAdapter>();
            }
        }

        private void TeardownUi()
        {
            _stepPanelController?.Unbind();
            _partInfoPanelController?.Unbind();
            _isBuilt = false;
        }

        private void Reset()
        {
            _documentBootstrap = GetComponent<UIDocumentBootstrap>();
        }
    }
}
