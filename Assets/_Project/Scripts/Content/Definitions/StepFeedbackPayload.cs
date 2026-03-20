using System;

namespace OSE.Content
{
    /// <summary>
    /// Immediate interaction-level feedback payload for a step.
    /// Groups effect triggers and visual/audio responses during the action.
    /// </summary>
    [Serializable]
    public sealed class StepFeedbackPayload
    {
        public string[] effectTriggerIds;
    }
}
