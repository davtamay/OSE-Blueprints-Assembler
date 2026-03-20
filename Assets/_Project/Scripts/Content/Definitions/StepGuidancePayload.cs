using System;

namespace OSE.Content
{
    /// <summary>
    /// Pre-action instruction payload for a step.
    /// Groups instructional content and progressive hints.
    /// </summary>
    [Serializable]
    public sealed class StepGuidancePayload
    {
        public string instructionText;
        public string whyItMattersText;
        public string[] hintIds;
        public string contextualDiagramRef;
    }
}
