using System;
using OSE.App;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Tracks tool availability and active tool selection for the current session.
    /// Plain C# service registered in ServiceRegistry.
    /// </summary>
    public sealed class ToolRuntimeController
    {
        public readonly struct ToolActionSnapshot
        {
            public readonly bool IsConfigured;
            public readonly bool IsCompleted;
            public readonly string ToolId;
            public readonly ToolActionType ActionType;
            public readonly string TargetId;
            public readonly int CurrentCount;
            public readonly int RequiredCount;
            public readonly string SuccessMessage;
            public readonly string FailureMessage;

            public ToolActionSnapshot(
                bool isConfigured,
                bool isCompleted,
                string toolId,
                ToolActionType actionType,
                string targetId,
                int currentCount,
                int requiredCount,
                string successMessage,
                string failureMessage)
            {
                IsConfigured = isConfigured;
                IsCompleted = isCompleted;
                ToolId = toolId;
                ActionType = actionType;
                TargetId = targetId;
                CurrentCount = currentCount;
                RequiredCount = requiredCount;
                SuccessMessage = successMessage;
                FailureMessage = failureMessage;
            }
        }

        public readonly struct ToolActionExecutionResult
        {
            public readonly bool Handled;
            public readonly bool ShouldCompleteStep;
            public readonly ToolActionFailureReason FailureReason;
            public readonly string Message;
            public readonly int CurrentCount;
            public readonly int RequiredCount;

            private ToolActionExecutionResult(
                bool handled,
                bool shouldCompleteStep,
                ToolActionFailureReason failureReason,
                string message,
                int currentCount,
                int requiredCount)
            {
                Handled = handled;
                ShouldCompleteStep = shouldCompleteStep;
                FailureReason = failureReason;
                Message = message;
                CurrentCount = currentCount;
                RequiredCount = requiredCount;
            }

            public static ToolActionExecutionResult NotHandled() =>
                new ToolActionExecutionResult(false, false, ToolActionFailureReason.None, string.Empty, 0, 0);

            public static ToolActionExecutionResult Continue(
                string message,
                int currentCount,
                int requiredCount) =>
                new ToolActionExecutionResult(true, false, ToolActionFailureReason.None, message, currentCount, requiredCount);

            public static ToolActionExecutionResult Complete(
                string message,
                int requiredCount) =>
                new ToolActionExecutionResult(true, true, ToolActionFailureReason.None, message, requiredCount, requiredCount);

            public static ToolActionExecutionResult Failed(
                ToolActionFailureReason failureReason,
                string message,
                int currentCount,
                int requiredCount) =>
                new ToolActionExecutionResult(true, false, failureReason, message, currentCount, requiredCount);
        }

        private struct ToolActionRuntimeState
        {
            public string ToolId;
            public ToolActionType ActionType;
            public string TargetId;
            public int RequiredCount;
            public int CurrentCount;
            public string SuccessMessage;
            public string FailureMessage;

            public bool IsConfigured =>
                !string.IsNullOrWhiteSpace(ToolId) &&
                ActionType != ToolActionType.None &&
                RequiredCount > 0;

            public bool IsCompleted => IsConfigured && CurrentCount >= RequiredCount;
        }

        private MachinePackageDefinition _package;
        private ToolDefinition[] _availableTools = Array.Empty<ToolDefinition>();
        private string _activeStepId;
        private string _activeToolId;
        private string[] _requiredToolIds = Array.Empty<string>();
        private ToolActionRuntimeState _primaryToolAction;

        public event Action StateChanged;

        public string ActiveStepId => _activeStepId;
        public string ActiveToolId => _activeToolId;
        public bool HasPackage => _package != null;

        public void Initialize(MachinePackageDefinition package)
        {
            _package = package;
            _availableTools = ResolveAvailableTools(package);
            _activeStepId = null;
            _activeToolId = null;
            _requiredToolIds = Array.Empty<string>();
            _primaryToolAction = default;

            OseLog.Info($"[ToolRuntime] Loaded {_availableTools.Length} tool(s) for package '{_package?.packageId ?? "unknown"}'.");

            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RaiseStateChanged();
        }

        public void Dispose()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            _package = null;
            _availableTools = Array.Empty<ToolDefinition>();
            _activeStepId = null;
            _activeToolId = null;
            _requiredToolIds = Array.Empty<string>();
            _primaryToolAction = default;
            RaiseStateChanged();
        }

        public ToolDefinition[] GetAvailableTools()
        {
            if (_availableTools == null || _availableTools.Length == 0)
                return Array.Empty<ToolDefinition>();

            ToolDefinition[] copy = new ToolDefinition[_availableTools.Length];
            Array.Copy(_availableTools, copy, _availableTools.Length);
            return copy;
        }

        public string[] GetRequiredToolIds()
        {
            if (_requiredToolIds == null || _requiredToolIds.Length == 0)
                return Array.Empty<string>();

            string[] copy = new string[_requiredToolIds.Length];
            Array.Copy(_requiredToolIds, copy, _requiredToolIds.Length);
            return copy;
        }

        public bool IsToolRequiredForActiveStep(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId) || _requiredToolIds == null)
                return false;

            for (int i = 0; i < _requiredToolIds.Length; i++)
            {
                if (string.Equals(_requiredToolIds[i], toolId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public bool TryGetTool(string toolId, out ToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(toolId))
            {
                tool = null;
                return false;
            }

            string normalizedToolId = toolId.Trim();
            for (int i = 0; i < _availableTools.Length; i++)
            {
                ToolDefinition candidate = _availableTools[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
                    continue;

                if (string.Equals(candidate.id, normalizedToolId, StringComparison.OrdinalIgnoreCase))
                {
                    tool = candidate;
                    return true;
                }
            }

            tool = null;
            return false;
        }

        public bool TryGetActiveTool(out ToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(_activeToolId))
            {
                tool = null;
                return false;
            }

            return TryGetTool(_activeToolId, out tool);
        }

        public bool EquipTool(string toolId)
        {
            if (_package == null || string.IsNullOrWhiteSpace(toolId))
                return false;

            string normalizedToolId = toolId.Trim();
            if (!TryGetTool(normalizedToolId, out _))
            {
                OseLog.Warn($"[ToolRuntime] Cannot equip unknown tool '{toolId}'.");
                return false;
            }

            if (string.Equals(_activeToolId, normalizedToolId, StringComparison.OrdinalIgnoreCase))
                return true;

            string previousTool = _activeToolId;
            _activeToolId = normalizedToolId;
            OseLog.Info($"[ToolRuntime] Equipped tool '{toolId}'.");
            RuntimeEventBus.Publish(new ActiveToolChanged(previousTool, _activeToolId, IsToolRequiredForActiveStep(_activeToolId)));
            RaiseStateChanged();
            TryAutoCompleteTargetlessPrimaryActionIfReady();
            return true;
        }

        public void UnequipTool()
        {
            if (string.IsNullOrEmpty(_activeToolId))
                return;

            string previousTool = _activeToolId;
            _activeToolId = null;
            RuntimeEventBus.Publish(new ActiveToolChanged(previousTool, _activeToolId, false));
            RaiseStateChanged();
        }

        public ToolActionExecutionResult TryExecutePrimaryAction(string interactedTargetId = null)
        {
            if (_package == null)
            {
                OseLog.Warn("[ToolRuntime] TryExecutePrimaryAction: NotHandled — no package loaded.");
                return ToolActionExecutionResult.NotHandled();
            }

            // Self-heal: if step state was lost (missed Active event, or Completed cleared it),
            // re-sync with the session's current active step before giving up.
            if (string.IsNullOrWhiteSpace(_activeStepId) || !_primaryToolAction.IsConfigured)
            {
                bool healed = false;
                if (ServiceRegistry.TryGet<MachineSessionController>(out var healSession))
                {
                    StepController healStepCtrl = healSession?.AssemblyController?.StepController;
                    if (healStepCtrl != null && healStepCtrl.HasActiveStep)
                    {
                        string healStepId = healStepCtrl.CurrentStepState.StepId;
                        if (!string.IsNullOrWhiteSpace(healStepId))
                        {
                            OseLog.Warn($"[ToolRuntime] TryExecutePrimaryAction: step state stale (stepId='{_activeStepId}', configured={_primaryToolAction.IsConfigured}). Syncing with '{healStepId}'.");
                            _activeStepId = healStepId;
                            _requiredToolIds = ResolveRequiredToolIds(healStepId);
                            _primaryToolAction = ResolvePrimaryToolAction(healStepId);
                            healed = _primaryToolAction.IsConfigured;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(_activeStepId) || !_primaryToolAction.IsConfigured)
                {
                    OseLog.Warn($"[ToolRuntime] TryExecutePrimaryAction: NotHandled — activeStepId='{_activeStepId}', configured={_primaryToolAction.IsConfigured}, healed={healed}.");
                    return ToolActionExecutionResult.NotHandled();
                }
            }

            if (_primaryToolAction.IsCompleted)
            {
                return ToolActionExecutionResult.Failed(
                    ToolActionFailureReason.AlreadyCompleted,
                    "Tool action already completed for this step.",
                    _primaryToolAction.CurrentCount,
                    _primaryToolAction.RequiredCount);
            }

            if (string.IsNullOrWhiteSpace(_activeToolId))
            {
                return FailAction(
                    ToolActionFailureReason.NoActiveToolEquipped,
                    $"Equip {ResolveToolDisplayName(_primaryToolAction.ToolId)} before continuing.");
            }

            if (!string.Equals(_activeToolId, _primaryToolAction.ToolId, StringComparison.OrdinalIgnoreCase))
            {
                return FailAction(
                    ToolActionFailureReason.WrongToolEquipped,
                    $"Active tool is {ResolveToolDisplayName(_activeToolId)}. Required: {ResolveToolDisplayName(_primaryToolAction.ToolId)}.");
            }

            if (!string.IsNullOrWhiteSpace(_primaryToolAction.TargetId))
            {
                if (string.IsNullOrWhiteSpace(interactedTargetId) ||
                    !string.Equals(interactedTargetId.Trim(), _primaryToolAction.TargetId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailAction(
                        ToolActionFailureReason.WrongTarget,
                        $"Use {ResolveToolDisplayName(_primaryToolAction.ToolId)} on the highlighted target.");
                }
            }

            _primaryToolAction.CurrentCount++;
            string progressMessage = BuildProgressMessage(_primaryToolAction);

            RuntimeEventBus.Publish(new ToolActionProgressed(
                _activeStepId,
                _primaryToolAction.ToolId,
                _primaryToolAction.ActionType,
                _primaryToolAction.CurrentCount,
                _primaryToolAction.RequiredCount,
                progressMessage));

            if (_primaryToolAction.CurrentCount >= _primaryToolAction.RequiredCount)
            {
                string completionMessage = BuildCompletionMessage(_primaryToolAction);
                RuntimeEventBus.Publish(new ToolActionCompleted(
                    _activeStepId,
                    _primaryToolAction.ToolId,
                    _primaryToolAction.ActionType,
                    _primaryToolAction.RequiredCount,
                    completionMessage));

                OseLog.Info($"[ToolRuntime] Step '{_activeStepId}' tool action complete: {_primaryToolAction.ActionType} {ResolveToolDisplayName(_primaryToolAction.ToolId)}.");
                RaiseStateChanged();
                return ToolActionExecutionResult.Complete(completionMessage, _primaryToolAction.RequiredCount);
            }

            RaiseStateChanged();
            return ToolActionExecutionResult.Continue(progressMessage, _primaryToolAction.CurrentCount, _primaryToolAction.RequiredCount);
        }

        public bool TryGetPrimaryActionSnapshot(out ToolActionSnapshot snapshot)
        {
            if (!_primaryToolAction.IsConfigured)
            {
                snapshot = default;
                return false;
            }

            snapshot = new ToolActionSnapshot(
                _primaryToolAction.IsConfigured,
                _primaryToolAction.IsCompleted,
                _primaryToolAction.ToolId,
                _primaryToolAction.ActionType,
                _primaryToolAction.TargetId,
                _primaryToolAction.CurrentCount,
                _primaryToolAction.RequiredCount,
                _primaryToolAction.SuccessMessage,
                _primaryToolAction.FailureMessage);

            return true;
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (_package == null)
                return;

            if (evt.Current == StepState.Active)
            {
                _activeStepId = evt.StepId;
                _requiredToolIds = ResolveRequiredToolIds(evt.StepId);
                _primaryToolAction = ResolvePrimaryToolAction(evt.StepId);

                if (_primaryToolAction.IsConfigured)
                {
                    OseLog.Info($"[ToolRuntime] Step '{evt.StepId}' activated with tool action: " +
                        $"tool='{_primaryToolAction.ToolId}', action='{_primaryToolAction.ActionType}', " +
                        $"target='{_primaryToolAction.TargetId}', required={_primaryToolAction.RequiredCount}.");
                }
                else if (_package != null && _package.TryGetStep(evt.StepId, out var stepDef)
                         && stepDef.requiredToolActions != null && stepDef.requiredToolActions.Length > 0)
                {
                    OseLog.Warn($"[ToolRuntime] Step '{evt.StepId}' has {stepDef.requiredToolActions.Length} " +
                        $"required tool action(s) in JSON but ResolvePrimaryToolAction returned unconfigured.");
                }

                if (!string.IsNullOrWhiteSpace(_activeToolId)
                    && !TryGetTool(_activeToolId, out _))
                {
                    _activeToolId = null;
                }

                RaiseStateChanged();
                TryAutoCompleteTargetlessPrimaryActionIfReady();
            }
            else if (evt.Current == StepState.Completed || evt.Current == StepState.Suspended)
            {
                _primaryToolAction = default;
            }
        }

        private void TryAutoCompleteTargetlessPrimaryActionIfReady()
        {
            if (_package == null || !_primaryToolAction.IsConfigured || _primaryToolAction.IsCompleted)
                return;

            // Suppress auto-completion during explicit step navigation.
            if (ServiceRegistry.TryGet<MachineSessionController>(out var session) && session.IsNavigating)
                return;

            if (!string.IsNullOrWhiteSpace(_primaryToolAction.TargetId))
                return;

            if (string.IsNullOrWhiteSpace(_activeToolId) ||
                !string.Equals(_activeToolId, _primaryToolAction.ToolId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (session == null)
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            if (string.IsNullOrWhiteSpace(_activeStepId) ||
                !string.Equals(stepController.CurrentStepState.StepId, _activeStepId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ToolActionExecutionResult toolResult = TryExecutePrimaryAction();
            if (!toolResult.Handled || !toolResult.ShouldCompleteStep)
                return;

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        private string[] ResolveRequiredToolIds(string stepId)
        {
            if (_package == null || string.IsNullOrWhiteSpace(stepId))
                return Array.Empty<string>();

            if (!_package.TryGetStep(stepId, out StepDefinition step)
                || step.relevantToolIds == null
                || step.relevantToolIds.Length == 0)
            {
                return Array.Empty<string>();
            }

            int count = 0;
            for (int i = 0; i < step.relevantToolIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(step.relevantToolIds[i]))
                    count++;
            }

            if (count == 0)
                return Array.Empty<string>();

            string[] result = new string[count];
            int index = 0;
            for (int i = 0; i < step.relevantToolIds.Length; i++)
            {
                string toolId = step.relevantToolIds[i];
                if (string.IsNullOrWhiteSpace(toolId))
                    continue;

                result[index++] = toolId.Trim();
            }

            return result;
        }

        private ToolActionRuntimeState ResolvePrimaryToolAction(string stepId)
        {
            if (_package == null || string.IsNullOrWhiteSpace(stepId))
                return default;

            StepDefinition step = ResolveStepDefinitionForToolAction(stepId);
            if (step == null || step.requiredToolActions == null
                || step.requiredToolActions.Length == 0)
            {
                return default;
            }

            string normalizedStepId = string.IsNullOrWhiteSpace(step.id) ? stepId.Trim() : step.id.Trim();
            string fallbackToolId = null;
            string fallbackTargetId = null;
            int fallbackRequiredCount = 1;
            string fallbackSuccessMessage = null;
            string fallbackFailureMessage = null;

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition definition = step.requiredToolActions[i];
                if (definition == null)
                {
                    OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' has null requiredToolActions[{i}].");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.toolId))
                {
                    OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' requiredToolActions[{i}] has no toolId.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fallbackToolId))
                    fallbackToolId = definition.toolId.Trim();
                if (string.IsNullOrWhiteSpace(fallbackTargetId) && !string.IsNullOrWhiteSpace(definition.targetId))
                    fallbackTargetId = definition.targetId.Trim();
                if (definition.requiredCount > 0)
                    fallbackRequiredCount = definition.requiredCount;
                if (string.IsNullOrWhiteSpace(fallbackSuccessMessage) && !string.IsNullOrWhiteSpace(definition.successMessage))
                    fallbackSuccessMessage = definition.successMessage;
                if (string.IsNullOrWhiteSpace(fallbackFailureMessage) && !string.IsNullOrWhiteSpace(definition.failureMessage))
                    fallbackFailureMessage = definition.failureMessage;

                ToolActionType actionType = ParseActionType(definition.actionType);
                if (actionType == ToolActionType.None)
                {
                    OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' requiredToolActions[{i}] has unsupported actionType '{definition.actionType}'.");
                    continue;
                }

                int requiredCount = definition.requiredCount < 1 ? 1 : definition.requiredCount;

                return new ToolActionRuntimeState
                {
                    ToolId = definition.toolId.Trim(),
                    ActionType = actionType,
                    TargetId = string.IsNullOrWhiteSpace(definition.targetId) ? null : definition.targetId.Trim(),
                    RequiredCount = requiredCount,
                    CurrentCount = 0,
                    SuccessMessage = definition.successMessage,
                    FailureMessage = definition.failureMessage
                };
            }

            if (string.IsNullOrWhiteSpace(fallbackToolId) && step.relevantToolIds != null)
            {
                for (int i = 0; i < step.relevantToolIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(step.relevantToolIds[i]))
                    {
                        fallbackToolId = step.relevantToolIds[i].Trim();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(fallbackTargetId) && step.targetIds != null)
            {
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(step.targetIds[i]))
                    {
                        fallbackTargetId = step.targetIds[i].Trim();
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackToolId))
            {
                OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' requiredToolActions were invalid; applying fallback 'measure' action for tool '{fallbackToolId}'.");
                return new ToolActionRuntimeState
                {
                    ToolId = fallbackToolId,
                    ActionType = ToolActionType.Measure,
                    TargetId = fallbackTargetId,
                    RequiredCount = System.Math.Max(1, fallbackRequiredCount),
                    CurrentCount = 0,
                    SuccessMessage = fallbackSuccessMessage,
                    FailureMessage = fallbackFailureMessage
                };
            }

            OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' has required tool actions but none were usable.");
            return default;
        }

        private StepDefinition ResolveStepDefinitionForToolAction(string stepId)
        {
            string normalizedStepId = stepId.Trim();

            if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
            {
                StepDefinition activeStep = session?.AssemblyController?.StepController?.CurrentStepDefinition;
                if (activeStep != null &&
                    string.Equals(activeStep.id?.Trim(), normalizedStepId, StringComparison.OrdinalIgnoreCase))
                {
                    return activeStep;
                }
            }

            return _package.TryGetStep(normalizedStepId, out StepDefinition stepFromPackage)
                ? stepFromPackage
                : null;
        }

        private ToolActionExecutionResult FailAction(ToolActionFailureReason reason, string fallbackMessage)
        {
            string message = string.IsNullOrWhiteSpace(_primaryToolAction.FailureMessage)
                ? fallbackMessage
                : _primaryToolAction.FailureMessage.Trim();

            RuntimeEventBus.Publish(new ToolActionFailed(
                _activeStepId,
                _primaryToolAction.ToolId,
                _activeToolId,
                _primaryToolAction.ActionType,
                reason,
                message));

            return ToolActionExecutionResult.Failed(
                reason,
                message,
                _primaryToolAction.CurrentCount,
                _primaryToolAction.RequiredCount);
        }

        private string BuildProgressMessage(ToolActionRuntimeState action)
        {
            string verb = ResolveActionVerb(action.ActionType);
            return $"{verb} {action.CurrentCount}/{action.RequiredCount}";
        }

        private string BuildCompletionMessage(ToolActionRuntimeState action)
        {
            if (!string.IsNullOrWhiteSpace(action.SuccessMessage))
                return action.SuccessMessage.Trim();

            string noun = ResolveActionNoun(action.ActionType);
            return $"{noun} complete.";
        }

        private ToolActionType ParseActionType(string actionType)
        {
            if (string.IsNullOrWhiteSpace(actionType))
                return ToolActionType.None;

            string value = actionType.Trim().ToLowerInvariant();
            return value switch
            {
                "measure" => ToolActionType.Measure,
                "tighten" => ToolActionType.Tighten,
                "strike" => ToolActionType.Strike,
                "weld_pass" => ToolActionType.WeldPass,
                _ => ToolActionType.None
            };
        }

        private string ResolveToolDisplayName(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return "No tool";

            if (TryGetTool(toolId, out ToolDefinition tool))
                return tool.GetDisplayName();

            return toolId;
        }

        private static string ResolveActionVerb(ToolActionType actionType)
        {
            return actionType switch
            {
                ToolActionType.Measure => "Measurement pass",
                ToolActionType.Tighten => "Tightening pass",
                ToolActionType.Strike => "Strike",
                ToolActionType.WeldPass => "Weld pass",
                _ => "Action"
            };
        }

        private static string ResolveActionNoun(ToolActionType actionType)
        {
            return actionType switch
            {
                ToolActionType.Measure => "Measurement",
                ToolActionType.Tighten => "Tightening",
                ToolActionType.Strike => "Impact",
                ToolActionType.WeldPass => "Weld pass",
                _ => "Tool action"
            };
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private ToolDefinition[] ResolveAvailableTools(MachinePackageDefinition package)
        {
            if (package == null)
                return Array.Empty<ToolDefinition>();

            ToolDefinition[] source = package.GetTools();
            if (source == null || source.Length == 0)
                return Array.Empty<ToolDefinition>();

            ToolDefinition[] buffer = new ToolDefinition[source.Length];
            int count = 0;

            for (int i = 0; i < source.Length; i++)
            {
                ToolDefinition tool = source[i];
                if (tool == null || string.IsNullOrWhiteSpace(tool.id))
                    continue;

                string toolId = tool.id.Trim();
                if (string.IsNullOrWhiteSpace(tool.assetRef))
                {
                    OseLog.Warn($"[ToolRuntime] Tool '{toolId}' ignored because it has no assetRef.");
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    if (buffer[j] == null || string.IsNullOrWhiteSpace(buffer[j].id))
                        continue;

                    if (string.Equals(buffer[j].id, toolId, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    OseLog.Warn($"[ToolRuntime] Duplicate tool id '{toolId}' ignored.");
                    continue;
                }

                buffer[count++] = tool;
            }

            if (count == 0)
                return Array.Empty<ToolDefinition>();

            ToolDefinition[] resolved = new ToolDefinition[count];
            Array.Copy(buffer, resolved, count);
            return resolved;
        }
    }
}
