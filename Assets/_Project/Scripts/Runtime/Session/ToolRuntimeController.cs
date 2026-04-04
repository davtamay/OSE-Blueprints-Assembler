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
    public sealed class ToolRuntimeController : IToolRuntimeController
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
        private ToolActionRuntimeState[] _toolActions = Array.Empty<ToolActionRuntimeState>();

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
            _toolActions = Array.Empty<ToolActionRuntimeState>();

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
            _toolActions = Array.Empty<ToolActionRuntimeState>();
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
                OseLog.Warn("[ToolRuntime] TryExecutePrimaryAction: NotHandled ? no package loaded.");
                return ToolActionExecutionResult.NotHandled();
            }

            TryHealStepStateFromSession();

            // Self-heal: if step state was lost (missed Active event, or Completed cleared it),
            // re-sync with the session's current active step before giving up.
            if (string.IsNullOrWhiteSpace(_activeStepId) || !HasAnyConfiguredToolActions())
            {
                bool healed = TryHealStepStateFromSession();

                if (string.IsNullOrWhiteSpace(_activeStepId) || !HasAnyConfiguredToolActions())
                {
                    OseLog.Warn($"[ToolRuntime] TryExecutePrimaryAction: NotHandled ? activeStepId='{_activeStepId}', configured={HasAnyConfiguredToolActions()}, healed={healed}.");
                    return ToolActionExecutionResult.NotHandled();
                }
            }

            int actionIndex = ResolveExecutableActionIndex(interactedTargetId);
            if (actionIndex < 0)
            {
                return ToolActionExecutionResult.Failed(
                    ToolActionFailureReason.WrongTarget,
                    "Use the active tool on the highlighted target.",
                    0,
                    0);
            }

            ToolActionRuntimeState action = _toolActions[actionIndex];

            if (string.IsNullOrWhiteSpace(_activeToolId))
            {
                return FailAction(
                    action,
                    ToolActionFailureReason.NoActiveToolEquipped,
                    $"Equip {ResolveToolDisplayName(action.ToolId)} before continuing.");
            }

            if (!string.Equals(_activeToolId, action.ToolId, StringComparison.OrdinalIgnoreCase))
            {
                return FailAction(
                    action,
                    ToolActionFailureReason.WrongToolEquipped,
                    $"Active tool is {ResolveToolDisplayName(_activeToolId)}. Required: {ResolveToolDisplayName(action.ToolId)}.");
            }

            if (!string.IsNullOrWhiteSpace(action.TargetId))
            {
                if (string.IsNullOrWhiteSpace(interactedTargetId) ||
                    !string.Equals(interactedTargetId.Trim(), action.TargetId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailAction(
                        action,
                        ToolActionFailureReason.WrongTarget,
                        $"Use {ResolveToolDisplayName(action.ToolId)} on the highlighted target.");
                }
            }

            action.CurrentCount++;
            _toolActions[actionIndex] = action;

            string progressMessage = BuildProgressMessage(action);

            RuntimeEventBus.Publish(new ToolActionProgressed(
                _activeStepId,
                action.ToolId,
                action.ActionType,
                action.CurrentCount,
                action.RequiredCount,
                progressMessage));

            if (action.CurrentCount >= action.RequiredCount)
            {
                string completionMessage = BuildCompletionMessage(action);
                RuntimeEventBus.Publish(new ToolActionCompleted(
                    _activeStepId,
                    action.ToolId,
                    action.ActionType,
                    action.RequiredCount,
                    completionMessage));

                bool allCompleted = AreAllToolActionsCompleted();
                OseLog.Info($"[ToolRuntime] Step '{_activeStepId}' tool action complete: {action.ActionType} {ResolveToolDisplayName(action.ToolId)}. allCompleted={allCompleted}.");
                RaiseStateChanged();
                return allCompleted
                    ? ToolActionExecutionResult.Complete(completionMessage, action.RequiredCount)
                    : ToolActionExecutionResult.Continue(completionMessage, action.RequiredCount, action.RequiredCount);
            }

            RaiseStateChanged();
            return ToolActionExecutionResult.Continue(progressMessage, action.CurrentCount, action.RequiredCount);
        }

        public bool TryGetPrimaryActionSnapshot(out ToolActionSnapshot snapshot)
        {
            int actionIndex = GetFirstIncompleteActionIndex();
            if (actionIndex < 0)
            {
                snapshot = default;
                return false;
            }

            ToolActionRuntimeState action = _toolActions[actionIndex];

            snapshot = new ToolActionSnapshot(
                action.IsConfigured,
                action.IsCompleted,
                action.ToolId,
                action.ActionType,
                action.TargetId,
                action.CurrentCount,
                action.RequiredCount,
                action.SuccessMessage,
                action.FailureMessage);

            return true;
        }

        public bool TryGetActionSnapshots(out ToolActionSnapshot[] snapshots)
        {
            if (_toolActions == null || _toolActions.Length == 0)
            {
                snapshots = Array.Empty<ToolActionSnapshot>();
                return false;
            }

            snapshots = new ToolActionSnapshot[_toolActions.Length];
            int count = 0;

            for (int i = 0; i < _toolActions.Length; i++)
            {
                ToolActionRuntimeState action = _toolActions[i];
                if (!action.IsConfigured)
                    continue;

                snapshots[count++] = new ToolActionSnapshot(
                    action.IsConfigured,
                    action.IsCompleted,
                    action.ToolId,
                    action.ActionType,
                    action.TargetId,
                    action.CurrentCount,
                    action.RequiredCount,
                    action.SuccessMessage,
                    action.FailureMessage);
            }

            if (count == 0)
            {
                snapshots = Array.Empty<ToolActionSnapshot>();
                return false;
            }

            if (count != snapshots.Length)
                Array.Resize(ref snapshots, count);

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
                _toolActions = ResolveToolActions(evt.StepId);

                int configuredActionCount = GetConfiguredActionCount();
                if (configuredActionCount > 0)
                {
                    ToolActionRuntimeState primaryAction = _toolActions[GetFirstConfiguredActionIndex()];
                    OseLog.Info($"[ToolRuntime] Step '{evt.StepId}' activated with {configuredActionCount} tool action(s). " +
                        $"primaryTool='{primaryAction.ToolId}', action='{primaryAction.ActionType}', " +
                        $"target='{primaryAction.TargetId}', required={primaryAction.RequiredCount}.");
                }

                if (!string.IsNullOrWhiteSpace(_activeToolId)
                    && !TryGetTool(_activeToolId, out _))
                {
                    _activeToolId = null;
                }

                // Auto-dismiss carried-over tool when entering a pure placement step.
                // Placement steps involve physically positioning a part — no tool should be active.
                if (!string.IsNullOrWhiteSpace(_activeToolId)
                    && _requiredToolIds.Length == 0
                    && _toolActions.Length == 0
                    && _package.TryGetStep(evt.StepId, out StepDefinition placementStepCheck)
                    && string.Equals(placementStepCheck.completionType, "placement", StringComparison.OrdinalIgnoreCase))
                {
                    string dismissedTool = _activeToolId;
                    _activeToolId = null;
                    OseLog.Info($"[ToolRuntime] Auto-dismissed tool '{dismissedTool}' — entering placement step '{evt.StepId}'.");
                    RuntimeEventBus.Publish(new ActiveToolChanged(dismissedTool, null, false));
                }

                RaiseStateChanged();
                TryAutoCompleteTargetlessPrimaryActionIfReady();
            }
            else if (evt.Current == StepState.Completed || evt.Current == StepState.Suspended)
            {
                _activeStepId = null;
                _requiredToolIds = Array.Empty<string>();
                _toolActions = Array.Empty<ToolActionRuntimeState>();
                RaiseStateChanged();
            }
        }

        private void TryAutoCompleteTargetlessPrimaryActionIfReady()
        {
            int actionIndex = GetSingleExecutableTargetlessActionIndex();
            if (_package == null || actionIndex < 0)
                return;

            // Suppress auto-completion during explicit step navigation.
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session) && session.IsNavigating)
                return;

            ToolActionRuntimeState action = _toolActions[actionIndex];
            if (string.IsNullOrWhiteSpace(_activeToolId) ||
                !string.Equals(_activeToolId, action.ToolId, StringComparison.OrdinalIgnoreCase))
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

        private ToolActionRuntimeState[] ResolveToolActions(string stepId)
        {
            if (_package == null || string.IsNullOrWhiteSpace(stepId))
                return Array.Empty<ToolActionRuntimeState>();

            StepDefinition step = ResolveStepDefinitionForToolAction(stepId);
            if (step == null || step.requiredToolActions == null
                || step.requiredToolActions.Length == 0)
            {
                return Array.Empty<ToolActionRuntimeState>();
            }

            string normalizedStepId = string.IsNullOrWhiteSpace(step.id) ? stepId.Trim() : step.id.Trim();
            ToolActionRuntimeState[] buffer = new ToolActionRuntimeState[step.requiredToolActions.Length];
            int count = 0;

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

                ToolActionType actionType = ParseActionType(definition.actionType);
                if (actionType == ToolActionType.None)
                {
                    OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' requiredToolActions[{i}] has unsupported actionType '{definition.actionType}'.");
                    continue;
                }

                int requiredCount = definition.requiredCount < 1 ? 1 : definition.requiredCount;

                buffer[count++] = new ToolActionRuntimeState
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

            if (count == 0)
            {
                OseLog.Warn($"[ToolRuntime] Step '{normalizedStepId}' has required tool actions but none were usable.");
                return Array.Empty<ToolActionRuntimeState>();
            }

            if (count != buffer.Length)
                Array.Resize(ref buffer, count);

            return buffer;
        }

        private StepDefinition ResolveStepDefinitionForToolAction(string stepId)
        {
            string normalizedStepId = stepId.Trim();

            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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

        private ToolActionExecutionResult FailAction(ToolActionRuntimeState action, ToolActionFailureReason reason, string fallbackMessage)
        {
            string message = string.IsNullOrWhiteSpace(action.FailureMessage)
                ? fallbackMessage
                : action.FailureMessage.Trim();

            RuntimeEventBus.Publish(new ToolActionFailed(
                _activeStepId,
                action.ToolId,
                _activeToolId,
                action.ActionType,
                reason,
                message));

            return ToolActionExecutionResult.Failed(
                reason,
                message,
                action.CurrentCount,
                action.RequiredCount);
        }

        private bool HasAnyConfiguredToolActions() => GetConfiguredActionCount() > 0;

        private int GetConfiguredActionCount()
        {
            if (_toolActions == null || _toolActions.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < _toolActions.Length; i++)
            {
                if (_toolActions[i].IsConfigured)
                    count++;
            }

            return count;
        }

        private int GetFirstConfiguredActionIndex()
        {
            if (_toolActions == null)
                return -1;

            for (int i = 0; i < _toolActions.Length; i++)
            {
                if (_toolActions[i].IsConfigured)
                    return i;
            }

            return -1;
        }

        private int GetFirstIncompleteActionIndex()
        {
            if (_toolActions == null)
                return -1;

            for (int i = 0; i < _toolActions.Length; i++)
            {
                if (_toolActions[i].IsConfigured && !_toolActions[i].IsCompleted)
                    return i;
            }

            return -1;
        }

        private bool AreAllToolActionsCompleted()
        {
            if (_toolActions == null || _toolActions.Length == 0)
                return false;

            bool hasConfigured = false;
            for (int i = 0; i < _toolActions.Length; i++)
            {
                if (!_toolActions[i].IsConfigured)
                    continue;

                hasConfigured = true;
                if (!_toolActions[i].IsCompleted)
                    return false;
            }

            return hasConfigured;
        }

        private int ResolveExecutableActionIndex(string interactedTargetId)
        {
            if (_toolActions == null || _toolActions.Length == 0)
                return -1;

            string normalizedTargetId = string.IsNullOrWhiteSpace(interactedTargetId)
                ? null
                : interactedTargetId.Trim();

            if (!string.IsNullOrWhiteSpace(normalizedTargetId))
            {
                for (int i = 0; i < _toolActions.Length; i++)
                {
                    ToolActionRuntimeState action = _toolActions[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    if (string.Equals(action.TargetId, normalizedTargetId, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return -1;
            }

            int incompleteIndex = -1;
            for (int i = 0; i < _toolActions.Length; i++)
            {
                ToolActionRuntimeState action = _toolActions[i];
                if (!action.IsConfigured || action.IsCompleted)
                    continue;

                if (!string.IsNullOrWhiteSpace(action.TargetId))
                    continue;

                if (incompleteIndex >= 0)
                    return -1;

                incompleteIndex = i;
            }

            return incompleteIndex;
        }

        private int GetSingleExecutableTargetlessActionIndex()
        {
            return ResolveExecutableActionIndex(null);
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

        private static ToolActionType ParseActionType(string actionType)
            => ToolActionTypeHelper.Parse(actionType);

        private string ResolveToolDisplayName(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                return "No tool";

            if (TryGetTool(toolId, out ToolDefinition tool))
                return tool.GetDisplayName();

            return toolId;
        }

        private static string ResolveActionVerb(ToolActionType actionType)
            => ToolActionTypeHelper.GetVerb(actionType);

        private static string ResolveActionNoun(ToolActionType actionType)
            => ToolActionTypeHelper.GetNoun(actionType);

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }

        private bool TryHealStepStateFromSession()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var healSession))
                return false;

            StepController healStepCtrl = healSession?.AssemblyController?.StepController;
            if (healStepCtrl == null || !healStepCtrl.HasActiveStep)
                return false;

            string healStepId = healStepCtrl.CurrentStepState.StepId;
            if (string.IsNullOrWhiteSpace(healStepId))
                return false;

            bool stepChanged = !string.Equals(_activeStepId, healStepId, StringComparison.OrdinalIgnoreCase);
            bool shouldHeal = stepChanged
                || string.IsNullOrWhiteSpace(_activeStepId)
                || !HasAnyConfiguredToolActions()
                || AreAllToolActionsCompleted();

            if (!shouldHeal)
                return false;

            OseLog.Warn($"[ToolRuntime] Syncing tool step state from '{_activeStepId}' to '{healStepId}' (configured={HasAnyConfiguredToolActions()}, completed={AreAllToolActionsCompleted()}).");
            _activeStepId = healStepId;
            _requiredToolIds = ResolveRequiredToolIds(healStepId);
            _toolActions = ResolveToolActions(healStepId);
            return HasAnyConfiguredToolActions();
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
