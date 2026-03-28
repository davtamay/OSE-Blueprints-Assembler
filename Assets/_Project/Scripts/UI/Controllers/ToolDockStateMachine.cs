using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Owns tool auto-equip, tool dock toggle, tool info population,
    /// auto-advance logic, and tool runtime subscription.
    /// Extracted from UIRootCoordinator (Phase 7).
    /// </summary>
    internal sealed class ToolDockStateMachine
    {
        private readonly Action _onStateChanged;
        private readonly Func<ConfirmGate> _getConfirmGate;
        private readonly Func<bool> _getConfirmUnlocked;
        private readonly Action<bool> _setConfirmUnlocked;

        private IToolRuntimeController _toolRuntimeController;
        private bool _isSubscribed;
        private string _activeToolId;
        private string _hoveredToolId;
        private bool _toolDockExpanded;
        private bool _suppressAutoEquip;
        private string _lastAutoEquipStepId;
        private bool _autoCompletingTargetlessToolStep;
        private bool _autoCompletingEquipTaggedStep;

        private string _toolName = "Selected Tool";
        private string _toolCategory = "Tool category metadata will be supplied by runtime content.";
        private string _toolPurpose = "Tool purpose metadata will be supplied by runtime content.";
        private string _toolUsageNotes = "Tool usage notes metadata will be supplied by runtime content.";
        private string _toolSafetyNotes = "Tool safety notes metadata will be supplied by runtime content.";

        // ── Public accessors ──
        public string ActiveToolId => _activeToolId;
        public string HoveredToolId => _hoveredToolId;
        public bool ToolDockExpanded => _toolDockExpanded;
        public IToolRuntimeController RuntimeController => _toolRuntimeController;
        public string ToolName => _toolName;
        public string ToolCategory => _toolCategory;
        public string ToolPurpose => _toolPurpose;
        public string ToolUsageNotes => _toolUsageNotes;
        public string ToolSafetyNotes => _toolSafetyNotes;

        /// <param name="onStateChanged">
        /// Invoked after any tool state mutation so the host can refresh panels.
        /// </param>
        /// <param name="getConfirmGate">Returns the current confirm gate.</param>
        /// <param name="getConfirmUnlocked">Returns whether the confirm gate is unlocked.</param>
        /// <param name="setConfirmUnlocked">Sets confirm gate unlocked state (triggers step panel refresh in host).</param>
        public ToolDockStateMachine(
            Action onStateChanged,
            Func<ConfirmGate> getConfirmGate,
            Func<bool> getConfirmUnlocked,
            Action<bool> setConfirmUnlocked)
        {
            _onStateChanged = onStateChanged;
            _getConfirmGate = getConfirmGate;
            _getConfirmUnlocked = getConfirmUnlocked;
            _setConfirmUnlocked = setConfirmUnlocked;
        }

        // ════════════════════════════════════════════════════════════════════
        // Subscription
        // ════════════════════════════════════════════════════════════════════

        public void EnsureSubscription()
        {
            if (_isSubscribed && _toolRuntimeController != null)
                return;

            if (!ServiceRegistry.TryGet<IToolRuntimeController>(out var toolRuntime))
                return;

            _toolRuntimeController = toolRuntime;
            _toolRuntimeController.StateChanged += HandleStateChanged;
            _isSubscribed = true;
            HandleStateChanged();
        }

        public void Unsubscribe()
        {
            if (_isSubscribed && _toolRuntimeController != null)
            {
                _toolRuntimeController.StateChanged -= HandleStateChanged;
            }

            _toolRuntimeController = null;
            _isSubscribed = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // State change handler
        // ════════════════════════════════════════════════════════════════════

        private void HandleStateChanged()
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

            TryAutoEquipRequiredTool();

            if (_getConfirmGate() == ConfirmGate.EquipTool)
            {
                bool unlocked = IsEquipToolGateSatisfied();
                if (_getConfirmUnlocked() != unlocked)
                {
                    _setConfirmUnlocked(unlocked);
                }
            }

            if (!string.IsNullOrWhiteSpace(_activeToolId))
                TryPopulateToolInfo(_activeToolId);

            if (!string.IsNullOrWhiteSpace(_hoveredToolId)
                && !_toolRuntimeController.TryGetTool(_hoveredToolId, out _))
            {
                _hoveredToolId = null;
            }

            _onStateChanged?.Invoke();

            TryAutoAdvanceTargetlessToolStep();
            TryAutoAdvanceEquipTaggedStepIfSatisfied();
        }

        // ════════════════════════════════════════════════════════════════════
        // Tool dock events (wired to ToolDockPanelController)
        // ════════════════════════════════════════════════════════════════════

        public void HandleToggleRequested()
        {
            _toolDockExpanded = !_toolDockExpanded;
            _onStateChanged?.Invoke();
        }

        public void HandleToolSelected(string toolId)
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
                _onStateChanged?.Invoke();
                return;
            }

            if (!_toolRuntimeController.EquipTool(toolId))
                return;

            _activeToolId = _toolRuntimeController.ActiveToolId;
            _hoveredToolId = null;

            TryPopulateToolInfo(_activeToolId);

            _onStateChanged?.Invoke();
            TryAutoAdvanceTargetlessToolStep();
            TryAutoAdvanceEquipTaggedStepIfSatisfied();
        }

        public void HandleUnequipRequested()
        {
            if (_toolRuntimeController == null)
                return;

            _suppressAutoEquip = true;
            _toolRuntimeController.UnequipTool();
            _activeToolId = _toolRuntimeController.ActiveToolId;
            _hoveredToolId = null;
            _onStateChanged?.Invoke();
        }

        public void HandleToolHovered(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return;

            _hoveredToolId = toolId;
            _onStateChanged?.Invoke();
        }

        public void HandleToolHoverCleared()
        {
            _hoveredToolId = null;
            _onStateChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════════════════
        // External API
        // ════════════════════════════════════════════════════════════════════

        public void SetActiveToolId(string toolId)
        {
            _activeToolId = toolId;
            if (!string.IsNullOrWhiteSpace(toolId))
                TryPopulateToolInfo(toolId);
        }

        public bool TryPopulateToolInfo(string toolId)
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

        public bool IsEquipToolGateSatisfied()
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

        public void Teardown()
        {
            _activeToolId = null;
            _hoveredToolId = null;
            _suppressAutoEquip = false;
            _lastAutoEquipStepId = null;
            _autoCompletingTargetlessToolStep = false;
            _autoCompletingEquipTaggedStep = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // Auto-equip
        // ════════════════════════════════════════════════════════════════════

        private void TryAutoEquipRequiredTool()
        {
            if (_suppressAutoEquip || _toolRuntimeController == null)
                return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

            if (string.Equals(_toolRuntimeController.ActiveToolId, toolId, StringComparison.OrdinalIgnoreCase))
                return;

            if (_toolRuntimeController.EquipTool(toolId))
            {
                _activeToolId = _toolRuntimeController.ActiveToolId;
                OseLog.Info($"[UI] Auto-equipped required tool '{toolId}'.");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Auto-advance
        // ════════════════════════════════════════════════════════════════════

        private void TryAutoAdvanceTargetlessToolStep()
        {
            if (_autoCompletingTargetlessToolStep || _toolRuntimeController == null)
                return;

            // Suppress auto-advance during explicit step navigation.
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var navCheck) && navCheck.IsNavigating)
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

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var navSession) && navSession.IsNavigating)
                return;

            string activeToolId = _toolRuntimeController.ActiveToolId;
            if (string.IsNullOrWhiteSpace(activeToolId))
                return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

        // ════════════════════════════════════════════════════════════════════
        // Static helpers
        // ════════════════════════════════════════════════════════════════════

        internal static string ResolveRequiredToolForStep(StepDefinition step)
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

        internal static bool HasEventTag(string[] tags, string expectedTag)
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
    }
}
