using System;

namespace OSE.Content
{
    /// <summary>
    /// Learning-level reinforcement payload for a step.
    /// Communicates why this step matters after successful completion.
    /// </summary>
    [Serializable]
    public sealed class StepReinforcementPayload
    {
        public string milestoneMessage;
        public string consequenceText;
        public string safetyNote;
        public string counterfactualText;
    }
}
