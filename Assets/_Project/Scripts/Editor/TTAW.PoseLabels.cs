using System;
using OSE.Content;
using OSE.Content.Loading;

namespace OSE.Editor
{
    /// <summary>
    /// UI display helpers for pose-pill labels. Keeps JSON labels opaque to
    /// the UI layer so author-facing names follow a consistent
    /// "when (timing) : what (pose)" convention while JSON labels remain
    /// long / uniquely-keyed for idempotency in normalizer strip passes.
    ///
    /// <para><b>Convention:</b></para>
    /// <list type="bullet">
    ///   <item><c>Start</c>          — intrinsic staging pose (pre-task).</item>
    ///   <item><c>Assembled</c>      — intrinsic final-placement pose (post-task).</item>
    ///   <item><c>NO TASK</c>        — visual-only waypoint at this step.</item>
    ///   <item><c>Before cue[ N]</c> — <c>cue.fromPose</c> (only when authored; editor preview only).</item>
    ///   <item><c>After cue[ N]</c>  — <c>cue.toPose</c> baked from <c>holdAtEnd</c>.</item>
    ///   <item><c>Custom</c> / author label — everything else, displayed verbatim.</item>
    /// </list>
    /// <para>Ordinals <c>1</c>, <c>2</c>, … only appear when multiple cues
    /// of the same timing category exist on this part/group at this step.</para>
    /// </summary>
    internal static class PoseLabels
    {
        /// <summary>
        /// Returns the author-facing pill label for a <see cref="StepPoseEntry"/>.
        /// <paramref name="synthOrdinal"/> is the 1-based ordinal of this synth
        /// entry among all synthesized <c>holdAtEnd</c> entries on the same
        /// part/group — pass 1 when there's only one. Returns tooltip in the
        /// out parameter so UI can show rich info on hover without cluttering
        /// the pill text.
        /// </summary>
        public static string FriendlyName(StepPoseEntry entry, int synthOrdinal, int synthTotal, out string tooltip)
        {
            tooltip = null;
            if (entry == null) return "Custom";

            string raw = entry.label;
            if (string.IsNullOrEmpty(raw)) return "Custom";

            // NO TASK auto-bake — synthesized every load from visualPartIds.
            if (raw.StartsWith(MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
            {
                tooltip = $"Visual-only waypoint at step '{entry.stepId}' (auto-baked from visualPartIds).";
                return "NO TASK pose";
            }

            // Part-hosted synthesized: "synthesized:holdAtEnd (part=X cue=Y[Z])"
            // Group-hosted synthesized: "synthGroup:holdAtEnd (sub=X cue=Y[Z])"
            bool isPartSynth  = raw.StartsWith("synthesized:holdAtEnd", StringComparison.Ordinal);
            bool isGroupSynth = raw.StartsWith(MachinePackageNormalizer.SynthesizedGroupStepPoseLabelPrefix, StringComparison.Ordinal);
            if (isPartSynth || isGroupSynth)
            {
                tooltip = $"After cue finishes — baked from cue.toPose with holdAtEnd=true. Anchor: step '{entry.stepId}'.\nSource: {raw}";
                return synthTotal <= 1 ? "After cue" : $"After cue {synthOrdinal}";
            }

            // Author-assigned label — keep verbatim.
            tooltip = $"Step '{entry.stepId}' — {raw}";
            return raw;
        }

        /// <summary>
        /// True when this stepPose was auto-baked by the normalizer (NO TASK
        /// or holdAtEnd synthesis) vs author-written. Used to decide whether
        /// the pill should render a delete (×) affordance — synthesized
        /// entries regenerate on every load, so deleting them is confusing.
        /// </summary>
        public static bool IsSynthesized(StepPoseEntry entry)
        {
            if (entry?.label == null) return false;
            return entry.label.StartsWith(MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal)
                || entry.label.StartsWith("synthesized:holdAtEnd", StringComparison.Ordinal)
                || entry.label.StartsWith(MachinePackageNormalizer.SynthesizedGroupStepPoseLabelPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when this stepPose is specifically the holdAtEnd synth (part or group).
        /// Used by the pill renderer to assign ordinals among siblings.
        /// </summary>
        public static bool IsHoldAtEndSynth(StepPoseEntry entry)
        {
            if (entry?.label == null) return false;
            return entry.label.StartsWith("synthesized:holdAtEnd", StringComparison.Ordinal)
                || entry.label.StartsWith(MachinePackageNormalizer.SynthesizedGroupStepPoseLabelPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Builds the "Before cue[ N]" pill label for a poseTransition cue
        /// with an authored fromPose. <paramref name="ordinal"/> is 1-based
        /// among before-cue pills; pass 1 when only one cue qualifies.
        /// </summary>
        public static string BeforeCueName(int ordinal, int total, out string tooltip, string cueType, string stepId, int cueIndex)
        {
            tooltip = $"Before cue fires — jumps the target to cue.fromPose.\n{cueType} cue #{cueIndex} on step '{stepId}'.";
            return total <= 1 ? "Before cue" : $"Before cue {ordinal}";
        }

        /// <summary>
        /// Extracts the cue array index from a synthesized holdAtEnd
        /// stepPose label. Labels are emitted by the normalizer in the form:
        ///   <c>synthesized:holdAtEnd (part=X cue=TYPE[INDEX])</c>
        ///   <c>synthGroup:holdAtEnd  (sub=X  cue=TYPE[INDEX])</c>
        /// Returns the INDEX so callers can cross-reference back to the
        /// source cue on the part/partGroup definition. Returns -1 when
        /// the label isn't a synth label or the index can't be parsed.
        /// </summary>
        public static int TryExtractCueIndexFromSynthLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return -1;
            int bracketOpen  = label.LastIndexOf('[');
            int bracketClose = label.LastIndexOf(']');
            if (bracketOpen < 0 || bracketClose <= bracketOpen) return -1;
            string inside = label.Substring(bracketOpen + 1, bracketClose - bracketOpen - 1);
            if (int.TryParse(inside, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int idx))
                return idx;
            return -1;
        }
    }
}
