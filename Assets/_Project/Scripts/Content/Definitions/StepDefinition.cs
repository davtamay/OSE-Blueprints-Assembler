using System;
using System.Text;

namespace OSE.Content
{
    [Serializable]
    public sealed class StepDefinition
    {
        public string id;
        public string name;
        public string assemblyId;
        public string subassemblyId;
        public int sequenceIndex;
        public string instructionText;
        public string whyItMattersText;
        public string[] requiredPartIds;
        public string[] optionalPartIds;
        public string[] relevantToolIds;
        public string[] targetIds;
        /// <summary>
        /// How the step is completed.
        /// <list type="bullet">
        ///   <item>"placement" — user drags parts onto ghost targets.</item>
        ///   <item>"tool_action" — user performs tool actions (e.g. tighten bolts).</item>
        ///   <item>"confirmation" — user presses a Continue/Confirm button.</item>
        /// </list>
        /// </summary>
        public string completionType;

        public string[] validationRuleIds;
        public string[] hintIds;
        public string[] effectTriggerIds;
        public ToolActionDefinition[] requiredToolActions;
        public bool allowSkip;
        public StepChallengeFlagsDefinition challengeFlags;
        public string[] eventTags;

        /// <summary>
        /// Controls whether targets within this step are processed all at once or one at a time.
        /// <list type="bullet">
        ///   <item>"parallel" (default / null) — all ghosts and tool targets visible simultaneously; complete in any order.</item>
        ///   <item>"sequential" — one target at a time, in <see cref="targetIds"/> array order.</item>
        /// </list>
        /// </summary>
        public string targetOrder;

        public bool IsSequential =>
            string.Equals(targetOrder, "sequential", System.StringComparison.OrdinalIgnoreCase);

        public bool IsPlacement =>
            string.IsNullOrEmpty(completionType) ||
            string.Equals(completionType, "placement", System.StringComparison.OrdinalIgnoreCase);

        public bool IsToolAction =>
            string.Equals(completionType, "tool_action", System.StringComparison.OrdinalIgnoreCase);

        public bool IsConfirmation =>
            string.Equals(completionType, "confirmation", System.StringComparison.OrdinalIgnoreCase);

        public bool IsPipeConnection =>
            string.Equals(completionType, "pipe_connection", System.StringComparison.OrdinalIgnoreCase);

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                return id.Trim();
            }

            return "Unnamed Step";
        }

        public string BuildInstructionBody()
        {
            string instruction = string.IsNullOrWhiteSpace(instructionText)
                ? "Instruction text is missing from this step definition."
                : instructionText.Trim();

            if (string.IsNullOrWhiteSpace(whyItMattersText))
            {
                return instruction;
            }

            StringBuilder builder = new StringBuilder(instruction.Length + whyItMattersText.Length + 22);
            builder.Append(instruction);
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Why it matters: ");
            builder.Append(whyItMattersText.Trim());
            return builder.ToString();
        }
    }
}
