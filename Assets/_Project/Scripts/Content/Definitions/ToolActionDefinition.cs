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
    }
}
