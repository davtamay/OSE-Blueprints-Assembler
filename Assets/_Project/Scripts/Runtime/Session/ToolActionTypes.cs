using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Immutable snapshot of a single tool action's current progress.
    /// Consumed by UI and interaction layers to render tool-action state
    /// without holding a direct reference to <see cref="ToolRuntimeController"/>.
    /// </summary>
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

    /// <summary>
    /// Result returned by <see cref="IToolRuntimeController.TryExecutePrimaryAction"/>.
    /// Uses factory methods instead of a constructor to make the intent at each call site explicit.
    /// </summary>
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
}
