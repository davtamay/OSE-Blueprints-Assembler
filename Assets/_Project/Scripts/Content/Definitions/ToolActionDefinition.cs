using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class ToolActionDefinition
    {
        public string id;
        public string toolId;
        public string actionType;
        public string targetId;
        public int requiredCount = 1;
        public string successMessage;
        public string failureMessage;

        /// <summary>
        /// Optional authored description of HOW the tool drives the part.
        /// Null ≡ "lerp archetype with auto-derived axis" (pre-payload behavior).
        /// See <see cref="ToolPartInteraction"/>.
        /// </summary>
        public ToolPartInteraction interaction;

        /// <summary>
        /// Per-action override of preview behavioural constants (durations,
        /// vibration amp/freq, spark thresholds, colours, etc.) — see
        /// <see cref="ToolActionPreviewConfig"/>. Null or any unset field
        /// (0 / empty / alpha-zero) means "use the preview class's hardcoded
        /// default". Lets authors tune a specific tool action without
        /// affecting other steps that use the same profile.
        /// </summary>
        public ToolActionPreviewConfig previewConfig;
    }
}
