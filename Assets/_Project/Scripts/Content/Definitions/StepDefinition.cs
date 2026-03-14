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
        public string completionMode;
        public string[] validationRuleIds;
        public string[] hintIds;
        public string[] effectTriggerIds;
        public bool allowAutoSnap;
        public bool allowSkip;
        public bool requiresConfirmation;
        public StepChallengeFlagsDefinition challengeFlags;
        public string[] eventTags;

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
