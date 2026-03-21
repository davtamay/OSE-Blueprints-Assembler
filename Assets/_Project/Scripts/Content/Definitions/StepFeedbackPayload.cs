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

        /// <summary>Hex color for the completion click effect, e.g. "#33FF66". Null = profile/family default.</summary>
        public string completionEffectColor;

        /// <summary>Scale multiplier for the completion pulse effect. 0 = profile/family default.</summary>
        public float completionPulseScale;
    }
}
