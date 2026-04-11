using System;
using System.Collections.Generic;
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

        /// <summary>Legacy flat field. Use <see cref="guidance"/>.instructionText and read via <see cref="ResolvedInstructionText"/>.</summary>
        [Obsolete("Use guidance.instructionText and read via ResolvedInstructionText. Retained for JsonUtility backward-compat only.")]
        public string instructionText;

        /// <summary>Legacy flat field. Use <see cref="guidance"/>.whyItMattersText and read via <see cref="ResolvedWhyItMattersText"/>.</summary>
        [Obsolete("Use guidance.whyItMattersText and read via ResolvedWhyItMattersText. Retained for JsonUtility backward-compat only.")]
        public string whyItMattersText;

        public string[] requiredPartIds;

        /// <summary>
        /// Part IDs derived at load-time from <see cref="requiredToolActions"/> —
        /// each action's targetId → target.associatedPartId. Populated by
        /// <c>MachinePackageNormalizer.ResolveToolActionPartIds</c>. Never authored,
        /// never serialized. Used by callers that need to know which parts a
        /// Use-family step operates on (for step-completion repositioning,
        /// completed-step restore, etc.) without conflating them with authored
        /// <see cref="requiredPartIds"/> semantics.
        /// </summary>
        [NonSerialized]
        public string[] derivedToolActionPartIds;

        /// <summary>
        /// Part IDs derived at load-time from direct <see cref="targetIds"/> —
        /// each target's <c>associatedPartId</c>, minus any already in
        /// <see cref="requiredPartIds"/> or <see cref="derivedToolActionPartIds"/>.
        /// Populated by <c>MachinePackageNormalizer.ResolveDirectTargetPartIds</c>.
        /// Never authored, never serialized.
        ///
        /// Captures the "touch but don't own" case: a step that repositions a
        /// previously-placed part via its targets (e.g. anchor/mount/stage steps
        /// that move pre-built bench units into their final printer position)
        /// without claiming first-placement ownership. These parts count as
        /// touched by the step (so Rule 3 passes) but the step is not their
        /// owning Place step (so Rule 2 doesn't trigger).
        /// </summary>
        [NonSerialized]
        public string[] derivedTargetPartIds;

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
        [Obsolete("Use family + profile and read via ResolvedFamily. Retained for JsonUtility backward-compat only.")]
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

        /// <summary>Legacy flat field. Use <see cref="validation"/>.validationRuleIds and read via <see cref="ResolvedValidationRuleIds"/>.</summary>
        [Obsolete("Use validation.validationRuleIds and read via ResolvedValidationRuleIds. Retained for JsonUtility backward-compat only.")]
        public string[] validationRuleIds;

        /// <summary>Legacy flat field. Use <see cref="guidance"/>.hintIds and read via <see cref="ResolvedHintIds"/>.</summary>
        [Obsolete("Use guidance.hintIds and read via ResolvedHintIds. Retained for JsonUtility backward-compat only.")]
        public string[] hintIds;

        /// <summary>Legacy flat field. Use <see cref="feedback"/>.effectTriggerIds and read via <see cref="ResolvedEffectTriggerIds"/>.</summary>
        [Obsolete("Use feedback.effectTriggerIds and read via ResolvedEffectTriggerIds. Retained for JsonUtility backward-compat only.")]
        public string[] effectTriggerIds;
        public ToolActionDefinition[] requiredToolActions;

        /// <summary>
        /// Tool IDs whose persistent scene instances (clamps, fixtures) should be
        /// removed when this step activates. Content-driven removal point.
        /// </summary>
        public string[] removePersistentToolIds;

        /// <summary>Legacy flat field. Use <see cref="difficulty"/>.allowSkip and read via <see cref="ResolvedAllowSkip"/>.</summary>
        [Obsolete("Use difficulty.allowSkip and read via ResolvedAllowSkip. Retained for JsonUtility backward-compat only.")]
        public bool allowSkip;

        /// <summary>Legacy flat field. Use <see cref="difficulty"/>.challengeFlags and read via <see cref="ResolvedChallengeFlags"/>.</summary>
        [Obsolete("Use difficulty.challengeFlags and read via ResolvedChallengeFlags. Retained for JsonUtility backward-compat only.")]
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

        /// <summary>
        /// Optional polarity-aware wire connection payload for Connect-family steps
        /// with <c>profile: "WireConnect"</c>. Defines per-wire polarity types and
        /// connector types for port sphere color coding and mismatch detection.
        /// </summary>
        public StepWireConnectPayload wireConnect;

        /// <summary>
        /// Optional working orientation that temporarily transforms the subassembly
        /// for this step (e.g., flip 180° to access underside). Reverts automatically
        /// on step transition.
        /// </summary>
        public StepWorkingOrientationPayload workingOrientation;

        /// <summary>
        /// Optional animation cues played when this step activates.
        /// Content authors specify animation type, targets, timing, and parameters.
        /// </summary>
        public StepAnimationCuePayload animationCues;

        /// <summary>
        /// Explicit cross-section task sequence. Defines the order in which parts,
        /// tool actions, and wire/cable connections should be performed within this step.
        /// Null or empty = no explicit order (sections displayed independently).
        /// Runtime can use this for sequential task enforcement in future.
        /// </summary>
        public TaskOrderEntry[] taskOrder;

        // --- Resolved accessors (payload-first, flat-fallback) ---
        // These intentionally read the [Obsolete] flat fields as fallbacks — suppress the warning here only.
#pragma warning disable CS0618

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

#pragma warning restore CS0618

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

        private static readonly Dictionary<string, StepProfile> ProfileLookup =
            new Dictionary<string, StepProfile>(StringComparer.Ordinal)
            {
                { "Clamp",       StepProfile.Clamp       },
                { "AxisFit",     StepProfile.AxisFit     },
                { "Torque",      StepProfile.Torque      },
                { "Weld",        StepProfile.Weld        },
                { "Cut",         StepProfile.Cut         },
                { "Strike",      StepProfile.Strike      },
                { "Measure",     StepProfile.Measure     },
                { "SquareCheck", StepProfile.SquareCheck },
                { "Cable",       StepProfile.Cable       },
                { "WireConnect", StepProfile.WireConnect },
            };

        private static readonly Dictionary<string, StepFamily> FamilyLookup =
            new Dictionary<string, StepFamily>(StringComparer.Ordinal)
            {
                { "Place",   StepFamily.Place   },
                { "Use",     StepFamily.Use     },
                { "Connect", StepFamily.Connect },
                { "Confirm", StepFamily.Confirm },
            };

        /// <summary>
        /// Resolves the step profile enum from the <see cref="profile"/> string.
        /// Returns <see cref="StepProfile.None"/> when the string is null, empty, or unrecognized.
        /// Adding a new profile requires only a new entry in <see cref="ProfileLookup"/>.
        /// </summary>
        public StepProfile ResolvedProfile =>
            !string.IsNullOrEmpty(profile) && ProfileLookup.TryGetValue(profile, out var p)
                ? p
                : StepProfile.None;

        /// <summary>True when this place step uses the constrained adjustable-fit profile.</summary>
        public bool IsAxisFitPlacement =>
            IsPlacement && ResolvedProfile == StepProfile.AxisFit;

        /// <summary>True when the resolved family is Place.</summary>
        public bool IsPlacement => ResolvedFamily == StepFamily.Place;

        /// <summary>True when the resolved family is Use.</summary>
        public bool IsToolAction => ResolvedFamily == StepFamily.Use;

        /// <summary>True when the resolved family is Confirm.</summary>
        public bool IsConfirmation => ResolvedFamily == StepFamily.Confirm;

        /// <summary>True when the resolved family is Connect.</summary>
        public bool IsPipeConnection => ResolvedFamily == StepFamily.Connect;

        /// <summary>
        /// Returns the step's required part IDs.
        /// Connect-family steps carry no required parts — wire appearance (color, width)
        /// is embedded directly in each <see cref="WireConnectEntry"/>.
        /// </summary>
        public string[] GetEffectiveRequiredPartIds()
            => requiredPartIds ?? System.Array.Empty<string>();

        /// <summary>
        /// Returns the union of authored <see cref="requiredPartIds"/> and
        /// derived tool-action part IDs. Use this when you need to know every
        /// part this step touches (for completion repositioning, restore on
        /// navigation, highlighting, etc.).
        ///
        /// For Place-family steps the result is identical to
        /// <see cref="GetEffectiveRequiredPartIds"/> since they have no
        /// tool-action derivations. For Use-family steps this returns the
        /// parts their tool actions operate on — parts that were originally
        /// placed in a prior step.
        ///
        /// Callers that care about "which parts is this step authoring"
        /// (reveal, hide-on-navigation, owning-step lookup) should keep using
        /// <see cref="GetEffectiveRequiredPartIds"/>.
        /// </summary>
        public string[] GetAllTouchedPartIds()
        {
            string[] authored  = requiredPartIds;
            string[] fromTools = derivedToolActionPartIds;
            string[] fromTargs = derivedTargetPartIds;

            int authoredLen  = authored  != null ? authored.Length  : 0;
            int fromToolsLen = fromTools != null ? fromTools.Length : 0;
            int fromTargsLen = fromTargs != null ? fromTargs.Length : 0;
            int total = authoredLen + fromToolsLen + fromTargsLen;
            if (total == 0) return System.Array.Empty<string>();

            // Fast paths when only one source has entries.
            if (authoredLen  == total) return authored;
            if (fromToolsLen == total) return fromTools;
            if (fromTargsLen == total) return fromTargs;

            // Merge without duplicates (ordinal ignore-case).
            var merged = new List<string>(total);
            MergeInto(merged, authored);
            MergeInto(merged, fromTools);
            MergeInto(merged, fromTargs);
            return merged.ToArray();
        }

        private static void MergeInto(List<string> merged, string[] source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Length; i++)
            {
                string s = source[i];
                if (string.IsNullOrEmpty(s)) continue;
                bool dup = false;
                for (int j = 0; j < merged.Count; j++)
                {
                    if (string.Equals(merged[j], s, StringComparison.OrdinalIgnoreCase))
                    { dup = true; break; }
                }
                if (!dup) merged.Add(s);
            }
        }

        /// <summary>True when the resolved family is Confirm (alias for <see cref="IsConfirmation"/>).</summary>
        public bool IsConfirm => ResolvedFamily == StepFamily.Confirm;

        // Legacy completionType → StepFamily mapping (used when family field is absent).
        private static readonly Dictionary<string, StepFamily> LegacyCompletionTypeLookup =
            new Dictionary<string, StepFamily>(StringComparer.OrdinalIgnoreCase)
            {
                { "placement",       StepFamily.Place   },
                { "tool_action",     StepFamily.Use     },
                { "pipe_connection", StepFamily.Connect },
                { "confirmation",    StepFamily.Confirm },
            };

        /// <summary>
        /// Resolves the step family enum. Returns the parsed <see cref="family"/> if set,
        /// otherwise derives from <see cref="completionType"/> using the legacy mapping.
        /// Adding a new family requires only a new entry in <see cref="FamilyLookup"/>.
        /// </summary>
#pragma warning disable CS0618
        public StepFamily ResolvedFamily
        {
            get
            {
                if (!string.IsNullOrEmpty(family) && FamilyLookup.TryGetValue(family, out var f))
                    return f;

                if (!string.IsNullOrEmpty(completionType) &&
                    LegacyCompletionTypeLookup.TryGetValue(completionType, out var legacy))
                    return legacy;

                return StepFamily.Place;
            }
        }
#pragma warning restore CS0618

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
            string resolvedInstruction = ResolvedInstructionText;
            string instruction = string.IsNullOrWhiteSpace(resolvedInstruction)
                ? "Instruction text is missing from this step definition."
                : resolvedInstruction.Trim();

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

            if (workingOrientation != null)
            {
                string orientationText = !string.IsNullOrEmpty(workingOrientation.hint)
                    ? workingOrientation.hint
                    : "The assembly has been rotated to expose the work area for this step. " +
                      "It will return to its normal orientation when you move to the next step.";
                instruction += Environment.NewLine + Environment.NewLine + orientationText;
            }

            string resolvedWhyItMatters = ResolvedWhyItMattersText;
            if (string.IsNullOrWhiteSpace(resolvedWhyItMatters))
            {
                return instruction;
            }

            StringBuilder builder = new StringBuilder(instruction.Length + resolvedWhyItMatters.Length + 22);
            builder.Append(instruction);
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Why it matters: ");
            builder.Append(resolvedWhyItMatters.Trim());
            return builder.ToString();
        }
    }

    /// <summary>
    /// One entry in <see cref="StepDefinition.taskOrder"/>: identifies a single
    /// task (part placement, tool action, or wire/cable connection) by kind and id.
    /// </summary>
    [Serializable]
    public sealed class TaskOrderEntry
    {
        /// <summary>
        /// Task kind: "part" | "toolAction" | "wire" | "target"
        /// <list type="bullet">
        ///   <item>"part"       → id is a requiredPartIds entry</item>
        ///   <item>"toolAction" → id is a requiredToolActions[].id</item>
        ///   <item>"wire"       → id is a targetIds entry (Cable / WireConnect profile)</item>
        ///   <item>"target"     → id is a targetIds entry (Place / Use profile)</item>
        /// </list>
        /// </summary>
        public string kind;

        /// <summary>
        /// The identifier of the referenced part, action, or target.
        /// </summary>
        public string id;
    }
}
