using System;

namespace OSE.Content
{
    /// <summary>
    /// Correctness-checking payload for a step.
    /// Groups validation rule references that define acceptance criteria.
    /// </summary>
    [Serializable]
    public sealed class StepValidationPayload
    {
        public string[] validationRuleIds;
    }
}
