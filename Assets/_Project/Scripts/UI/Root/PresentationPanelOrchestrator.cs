using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using OSE.UI.Controllers;
using OSE.UI.Presenters;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns all panel controllers, presenters, and UI data state for
    /// <see cref="UIRootCoordinator"/>. Extracted for single-responsibility:
    /// UIRootCoordinator manages MonoBehaviour lifecycle, event subscriptions,
    /// and IPresentationAdapter routing; this class owns what panels show and
    /// how they refresh.
    /// </summary>
    internal sealed class PresentationPanelOrchestrator
    {
        // ── Panel infrastructure (owned) ──

        internal readonly StepPanelPresenter        StepPresenter        = new StepPanelPresenter();
        internal readonly PartInfoPanelPresenter     PartInfoPresenter    = new PartInfoPanelPresenter();
        internal readonly SessionHudPanelPresenter   SessionHudPresenter  = new SessionHudPanelPresenter();
        internal readonly ToolDockPanelPresenter     ToolDockPresenter    = new ToolDockPanelPresenter();
        internal readonly ToolInfoPanelPresenter     ToolInfoPresenter    = new ToolInfoPanelPresenter();

        internal readonly StepPanelController       StepPanelController       = new StepPanelController();
        internal readonly PartInfoPanelController   PartInfoPanelController   = new PartInfoPanelController();
        internal readonly SessionHudPanelController SessionHudPanelController = new SessionHudPanelController();
        internal readonly ToolDockPanelController   ToolDockPanelController   = new ToolDockPanelController();
        internal readonly ToolInfoPanelController   ToolInfoPanelController   = new ToolInfoPanelController();

        // ── Injected dependencies ──

        private readonly Func<bool>                 _isBuilt;
        private readonly Func<UiSessionModeManager> _getMode;
        private readonly Func<ToolDockStateMachine> _getToolDock;
        private readonly Func<ConfirmGateController> _getGate;
        private readonly Func<SessionHudMediator>   _getHudMediator;

        // ── Presentation data state ──

        private int    _currentStepNumber = 1;
        private int    _totalSteps        = 1;
        private string _stepTitle         = "Assembly Step";
        private string _instruction       = "Instruction text will be provided by the active runtime step.";
        private bool   _showConfirmButton;
        private bool   _showHintButton;

        private string _partName        = "Selected Part";
        private string _partFunction    = "Function metadata will be supplied by runtime content.";
        private string _partMaterial    = "Material metadata will be supplied by runtime content.";
        private string _partTool        = "Tool metadata will be supplied by runtime content.";
        private string _partSearchTerms = "Search terms will be supplied by runtime content.";
        private bool   _showingHoverPartInfo;

        internal string ActiveToolId { get; set; }

        private SelectionService _selectionService;

        // ──────────────────────────────────────────────────────────────────────

        public PresentationPanelOrchestrator(
            Func<bool>                  isBuilt,
            Func<UiSessionModeManager>  getMode,
            Func<ToolDockStateMachine>  getToolDock,
            Func<ConfirmGateController> getGate,
            Func<SessionHudMediator>    getHudMediator)
        {
            _isBuilt        = isBuilt;
            _getMode        = getMode;
            _getToolDock    = getToolDock;
            _getGate        = getGate;
            _getHudMediator = getHudMediator;
        }

        // ── Data mutation ──

        public void SetStepContent(
            int currentStepNumber, int totalSteps,
            string stepTitle, string instruction,
            bool showConfirmButton, bool showHintButton)
        {
            _currentStepNumber = currentStepNumber;
            _totalSteps        = totalSteps;
            _stepTitle         = stepTitle;
            _instruction       = instruction;
            _showConfirmButton = showConfirmButton;
            _showHintButton    = showHintButton;
        }

        public void SetProgressContent(int completedSteps, int totalSteps)
        {
            if (totalSteps <= 0)
            {
                _currentStepNumber = 0;
                _totalSteps        = 0;
            }
            else
            {
                _totalSteps        = totalSteps;
                _currentStepNumber = Mathf.Clamp(completedSteps + 1, 1, totalSteps);
            }
        }

        public void SetPartInfoContent(
            string partName, string function,
            string material, string tool,
            string searchTerms)
        {
            string resolved = string.IsNullOrWhiteSpace(partName) ? _partName : partName;
            bool samePart   = string.Equals(resolved, _partName, System.StringComparison.Ordinal);
            _showingHoverPartInfo = false;
            _partName     = resolved;
            _partFunction = function;
            _partMaterial = material;
            if (!string.IsNullOrWhiteSpace(tool))
                _partTool = tool;
            else if (!samePart)
                _partTool = "No specific tool required.";
            _partSearchTerms = searchTerms;
        }

        public void SetHoverPartInfoContent(
            string partName, string function,
            string material, string tool,
            string searchTerms)
        {
            string resolved = string.IsNullOrWhiteSpace(partName) ? _partName : partName;
            bool samePart   = string.Equals(resolved, _partName, System.StringComparison.Ordinal);
            _showingHoverPartInfo = true;
            _partName     = resolved;
            _partFunction = function;
            _partMaterial = material;
            if (!string.IsNullOrWhiteSpace(tool))
                _partTool = tool;
            else if (!samePart)
                _partTool = "No specific tool required.";
            _partSearchTerms = searchTerms;
        }

        public void SetPartNameOnly(string partName)
        {
            _partName        = string.IsNullOrWhiteSpace(partName) ? _partName : partName;
            _partSearchTerms = partName;
        }

        public void SetToolForPart(string toolId)
        {
            if (!string.IsNullOrWhiteSpace(toolId))
                _partTool = toolId;
        }

        public void SetInstructionOnly(string instruction)
        {
            if (!string.IsNullOrWhiteSpace(instruction))
                _instruction = instruction;
        }

        public void SetMilestoneContent(string stepTitle, string instruction)
        {
            _stepTitle         = stepTitle;
            _instruction       = instruction;
            _showConfirmButton = false;
            _showHintButton    = false;
        }

        public void ClearHoverPartInfo()
        {
            bool wasShowing = _showingHoverPartInfo;
            _showingHoverPartInfo = false;
            if (wasShowing)
                RefreshPartInfoPanel();
        }

        // ── Public refresh API ──

        public void HideAll()
        {
            _showingHoverPartInfo = false;
            StepPanelController?.Hide();
            PartInfoPanelController?.Hide();
            SessionHudPanelController?.Hide();
            ToolInfoPanelController?.Hide();
            ToolDockPanelController?.Hide();
        }

        public void RefreshAll()
        {
            if (!_isBuilt()) return;
            RefreshStepPanel();
            RefreshPartInfoPanel();
            RefreshSessionHudPanel();
            RefreshToolDockPanel();
            RefreshToolInfoPanel();
        }

        public void RefreshStepPanel()
        {
            if (!_isBuilt()) return;

            var mode = _getMode();
            if (mode != null && !mode.ShowStepPanel)
            {
                StepPanelController.Hide();
                return;
            }

            var gate          = _getGate();
            float? progressOverride = (gate?.ProgressComplete == true) ? 1f : ResolveIntraStepProgress();

            string assemblyName    = null;
            int    globalStepIndex = 0;
            int    globalTotalSteps = 0;

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

                    StepDefinition[] orderedSteps = pkg.GetOrderedSteps();
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

            StepPanelViewModel viewModel = StepPresenter.Create(
                _currentStepNumber,
                _totalSteps,
                _stepTitle,
                _instruction,
                _showConfirmButton,
                _showHintButton,
                gate?.Gate ?? ConfirmGate.None,
                gate?.IsUnlocked ?? false,
                false, null, false,
                progressOverride,
                assemblyName,
                globalStepIndex,
                globalTotalSteps);

            StepPanelController.Show(viewModel);
        }

        public void RefreshPartInfoPanel()
        {
            if (!_isBuilt()) return;

            var mode = _getMode();
            if (mode != null && !mode.ShowPartInfoPanel)
            {
                PartInfoPanelController.Hide();
                return;
            }

            if (!HasActivePartContext())
            {
                PartInfoPanelController.Hide();
                return;
            }

            PartInfoPanelViewModel viewModel = PartInfoPresenter.Create(
                _partName, _partFunction, _partMaterial, _partTool, _partSearchTerms);
            PartInfoPanelController.Show(viewModel);
        }

        public void RefreshSessionHudPanel()
        {
            var mode = _getMode();
            _getHudMediator()?.RefreshSessionHudPanel(_isBuilt(), true, mode?.ShowSessionHud ?? true);
        }

        public void RefreshToolDockPanel()
        {
            if (!_isBuilt() || ToolDockPanelController == null || !ToolDockPanelController.IsBound)
                return;

            var toolDock = _getToolDock();
            var runtime  = toolDock?.RuntimeController;
            if (!Application.isPlaying || runtime == null || !runtime.HasPackage)
            {
                ToolDockPanelController.Hide();
                return;
            }

            ToolDockPanelViewModel viewModel = ToolDockPresenter.Create(
                runtime.GetAvailableTools(),
                runtime.GetRequiredToolIds(),
                runtime.ActiveToolId,
                toolDock.ToolDockExpanded);

            ToolDockPanelController.Show(viewModel);
        }

        public void RefreshToolInfoPanel()
        {
            if (!_isBuilt() || ToolInfoPanelController == null || !ToolInfoPanelController.IsBound)
                return;

            var mode = _getMode();
            if (mode != null && !mode.ShowPartInfoPanel)
            {
                ToolInfoPanelController.Hide();
                return;
            }

            var    toolDock     = _getToolDock();
            string hoveredToolId = toolDock?.HoveredToolId;
            string toolId = !string.IsNullOrWhiteSpace(hoveredToolId) ? hoveredToolId : ActiveToolId;

            if (string.IsNullOrWhiteSpace(toolId) || toolDock == null || !toolDock.TryPopulateToolInfo(toolId))
            {
                ToolInfoPanelController.Hide();
                return;
            }

            ToolInfoPanelViewModel viewModel = ToolInfoPresenter.Create(
                toolDock.ToolName,
                toolDock.ToolCategory,
                toolDock.ToolPurpose,
                toolDock.ToolUsageNotes,
                toolDock.ToolSafetyNotes);

            ToolInfoPanelController.Show(viewModel);
        }

        // ── Private helpers ──

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

        private float? ResolveIntraStepProgress()
        {
            if (_totalSteps <= 0 || _currentStepNumber <= 0)
                return null;

            var runtime = _getToolDock()?.RuntimeController;
            if (runtime == null ||
                !runtime.TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot))
                return null;

            if (!snapshot.IsConfigured || snapshot.RequiredCount <= 0)
                return null;

            float baseProgress = (float)(_currentStepNumber - 1) / _totalSteps;
            float intraStep    = (float)snapshot.CurrentCount / snapshot.RequiredCount;
            float stepSlice    = 1f / _totalSteps;

            return Mathf.Clamp01(baseProgress + intraStep * stepSlice);
        }
    }
}
