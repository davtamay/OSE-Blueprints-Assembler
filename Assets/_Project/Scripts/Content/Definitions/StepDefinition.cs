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
        public string requiredSubassemblyId;
        public string[] optionalPartIds;
        public string[] relevantToolIds;
        public string[] targetIds;
        /// <summary>
        /// Legacy field — how the step is completed.
        /// <para>Accepted values: "placement", "tool_action", "pipe_connection", "confirmation".</para>
        /// <para>Target direction: use <see cref="family"/> + <see cref="profile"/> instead.
        /// When <see cref="family"/> is present it takes precedence.
        /// When absent, <see cref="ResolvedFamily"/> derives the family from this field.</para>
        /// </summary>
        public string completionType;

        /// <summary>
        /// Step family — the fundamental interaction shape.
        /// <para>Accepted values: "Place", "Use", "Connect", "Confirm".</para>
        /// <para>Optional. When null/empty, derived from <see cref="completionType"/> via <see cref="ResolvedFamily"/>.</para>
        /// </summary>
        public string family;

        /// <summary>
        /// Family-scoped profile that refines behavior within a family.
        /// <para>Examples: "Clamp" (Place), "Torque"/"Weld"/"Cut" (Use), "Cable" (Connect).</para>
        /// <para>Optional. When null/empty, the family default behavior applies.</para>
        /// </summary>
        public string profile;

        /// <summary>
        /// Semantic view mode override for camera framing.
        /// <para>Accepted values: "SourceAndTarget", "PairEndpoints", "WorkZone", "PathView", "Overview", "Inspect", "ToolFocus".</para>
        /// <para>Optional. When null/empty, resolved from family + profile via <see cref="ViewModeResolver"/>.</para>
        /// </summary>
        public string viewMode;

        public string[] validationRuleIds;
        public string[] hintIds;
        public string[] effectTriggerIds;
        public ToolActionDefinition[] requiredToolActions;

        /// <summary>
        /// Tool IDs whose persistent scene instances (clamps, fixtures) should be
        /// removed when this step activates. Content-driven removal point.
        /// </summary>
        public string[] removePersistentToolIds;

        public bool allowSkip;
        public StepChallengeFlagsDefinition challengeFlags;
        public string[] eventTags;

        // --- Capability payloads (Phase 3 — optional grouped objects) ---

        /// <summary>Optional grouped guidance payload. When present, its fields take precedence over flat equivalents.</summary>
        public StepGuidancePayload guidance;

        /// <summary>Optional grouped validation payload. When present, its fields take precedence over flat equivalents.</summary>
        public StepValidationPayload validation;

        /// <summary>Optional grouped feedback payload. When present, its fields take precedence over flat equivalents.</summary>
        public StepFeedbackPayload feedback;

        /// <summary>Optional grouped reinforcement payload. Entirely new — no flat-field equivalent.</summary>
        public StepReinforcementPayload reinforcement;

        /// <summary>Optional grouped difficulty payload. When present, its fields take precedence over flat equivalents.</summary>
        public StepDifficultyPayload difficulty;

        /// <summary>Optional measurement payload for anchor-to-anchor measurement steps (Use.Measure profile).</summary>
        public StepMeasurementPayload measurement;

        /// <summary>Optional gesture payload for tool-use engagement (Use family). Controls gesture type, thresholds, and guides.</summary>
        public StepGesturePayload gesture;

        // --- Resolved accessors (payload-first, flat-fallback) ---

        /// <summary>Guidance instruction text: payload first, then flat field.</summary>
        public string ResolvedInstructionText =>
            !string.IsNullOrEmpty(guidance?.instructionText) ? guidance.instructionText : instructionText;

        /// <summary>Guidance why-it-matters text: payload first, then flat field.</summary>
        public string ResolvedWhyItMattersText =>
            !string.IsNullOrEmpty(guidance?.whyItMattersText) ? guidance.whyItMattersText : whyItMattersText;

        /// <summary>Guidance hint IDs: payload first, then flat field.</summary>
        public string[] ResolvedHintIds =>
            guidance?.hintIds ?? hintIds;

        /// <summary>Validation rule IDs: payload first, then flat field.</summary>
        public string[] ResolvedValidationRuleIds =>
            validation?.validationRuleIds ?? validationRuleIds;

        /// <summary>Feedback effect trigger IDs: payload first, then flat field.</summary>
        public string[] ResolvedEffectTriggerIds =>
            feedback?.effectTriggerIds ?? effectTriggerIds;

        /// <summary>Difficulty allow-skip: payload first, then flat field.</summary>
        public bool ResolvedAllowSkip =>
            difficulty != null ? difficulty.allowSkip : allowSkip;

        /// <summary>Difficulty challenge flags: payload first, then flat field.</summary>
        public StepChallengeFlagsDefinition ResolvedChallengeFlags =>
            difficulty?.challengeFlags ?? challengeFlags;

        /// <summary>
        /// Controls whether targets within this step are processed all at once or one at a time.
        /// <list type="bullet">
        ///   <item>"parallel" (default / null) — all previews and tool targets visible simultaneously; complete in any order.</item>
        ///   <item>"sequential" — one target at a time, in <see cref="targetIds"/> array order.</item>
        /// </list>
        /// </summary>
        public string targetOrder;

        public bool IsSequential =>
            string.Equals(targetOrder, "sequential", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this place step expects a completed subassembly proxy instead of loose parts.</summary>
        public bool RequiresSubassemblyPlacement =>
            !string.IsNullOrWhiteSpace(requiredSubassemblyId);

        /// <summary>True when this place step uses the constrained adjustable-fit profile.</summary>
        public bool IsAxisFitPlacement =>
            IsPlacement && string.Equals(profile, "AxisFit", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the resolved family is Place.</summary>
        public bool IsPlacement => ResolvedFamily == StepFamily.Place;

        /// <summary>True when the resolved family is Use.</summary>
        public bool IsToolAction => ResolvedFamily == StepFamily.Use;

        /// <summary>True when the resolved family is Confirm.</summary>
        public bool IsConfirmation => ResolvedFamily == StepFamily.Confirm;

        /// <summary>True when the resolved family is Connect.</summary>
        public bool IsPipeConnection => ResolvedFamily == StepFamily.Connect;

        /// <summary>True when the resolved family is Confirm (alias for <see cref="IsConfirmation"/>).</summary>
        public bool IsConfirm => ResolvedFamily == StepFamily.Confirm;

        /// <summary>
        /// Resolves the step family enum. Returns the parsed <see cref="family"/> if set,
        /// otherwise derives from <see cref="completionType"/> using the legacy mapping.
        /// </summary>
        public StepFamily ResolvedFamily
        {
            get
            {
                if (!string.IsNullOrEmpty(family))
                {
                    switch (family)
                    {
                        case "Place":   return StepFamily.Place;
                        case "Use":     return StepFamily.Use;
                        case "Connect": return StepFamily.Connect;
                        case "Confirm": return StepFamily.Confirm;
                        default:        return StepFamily.Place;
                    }
                }

                if (string.IsNullOrEmpty(completionType))
                    return StepFamily.Place;

                switch (completionType.ToLowerInvariant())
                {
                    case "placement":       return StepFamily.Place;
                    case "tool_action":     return StepFamily.Use;
                    case "pipe_connection": return StepFamily.Connect;
                    case "confirmation":    return StepFamily.Confirm;
                    default:                return StepFamily.Place;
                }
            }
        }

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

            if (RequiresSubassemblyPlacement)
            {
                instruction += Environment.NewLine + Environment.NewLine +
                    "Move the completed panel as one finished unit. Drag it toward the highlighted target and it will rotate into place as it docks.";
            }

            if (IsAxisFitPlacement)
            {
                instruction += Environment.NewLine + Environment.NewLine +
                    "Adjust the completed axis as one unit while the anchored side stays fixed. Drag along the fit direction until the gap closes and the axis seats fully.";
            }

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
