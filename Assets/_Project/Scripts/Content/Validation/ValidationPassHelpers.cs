using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Primitive validation helpers shared by all built-in
    /// <see cref="IPackageValidationPass"/> implementations.
    /// Exposes the id-set enum constants and the small helper methods
    /// so pass classes stay focused on their domain logic.
    /// </summary>
    internal static class ValidationPassHelpers
    {
        // ── Allowed enum value sets ───────────────────────────────────────────

        internal static readonly HashSet<string> DifficultyValues =
            S("beginner", "intermediate", "advanced");

        internal static readonly HashSet<string> RecommendedModeValues =
            S("tutorial", "guided", "standard", "challenge");

        internal static readonly HashSet<string> PartCategoryValues =
            S("plate", "bracket", "fastener", "shaft", "panel", "housing", "pipe", "custom");

        internal static readonly HashSet<string> ToolCategoryValues =
            S("hand_tool", "power_tool", "measurement", "safety", "specialty");

        internal static readonly HashSet<string> CompletionTypeValues =
            S("placement", "tool_action", "confirmation", "pipe_connection");

        internal static readonly HashSet<string> FamilyValues =
            S("Place", "Use", "Connect", "Confirm");

        internal static readonly HashSet<string> ProfileValues = S(
            "None",
            "Clamp", "AxisFit",
            "Torque", "Weld", "Cut", "Strike", "Measure", "SquareCheck",
            "Cable", "WireConnect");

        internal static readonly HashSet<string> ViewModeValues =
            S("SourceAndTarget", "PairEndpoints", "WorkZone", "PathView", "Overview", "Inspect");

        internal static readonly HashSet<string> TargetOrderValues =
            S("sequential", "parallel");

        internal static readonly HashSet<string> ValidationTypeValues =
            S("placement", "orientation", "part_identity", "dependency", "multi_part", "confirmation");

        internal static readonly HashSet<string> HintTypeValues =
            S("text", "highlight", "ghost", "directional", "explanatory", "tool_reminder");

        internal static readonly HashSet<string> HintPriorityValues =
            S("low", "medium", "high");

        internal static readonly HashSet<string> ToolActionTypeValues =
            S("measure", "tighten", "strike", "weld_pass", "grind_pass");

        internal static readonly HashSet<string> EffectTypeValues =
            S("placement_feedback", "success_feedback", "error_feedback", "welding", "sparks", "heat_glow", "fire", "dust", "milestone");

        internal static readonly HashSet<string> EffectTriggerValues =
            S("on_step_enter", "on_valid_candidate", "on_success", "on_failure", "on_completion");

        internal static readonly HashSet<string> SourceTypeValues =
            S("blueprint", "photo", "diagram", "author_note", "reference_doc");

        internal static readonly HashSet<string> HintAvailabilityValues =
            S("always", "limited", "none");

        // ── Issue factories ───────────────────────────────────────────────────

        internal static MachinePackageValidationIssue Error(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Error, path, message);

        internal static MachinePackageValidationIssue Warning(string path, string message) =>
            new MachinePackageValidationIssue(MachinePackageIssueSeverity.Warning, path, message);

        // ── Primitive validators ──────────────────────────────────────────────

        internal static void ValidateRequiredText(
            string value, string path, List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
                issues.Add(Error(path, "A non-empty value is required."));
        }

        internal static void ValidateRequiredEnum(
            string value, HashSet<string> allowed, string path, List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(Error(path, "A non-empty enum value is required."));
                return;
            }
            if (!allowed.Contains(value))
                issues.Add(Error(path, $"Value '{value}' is not allowed here. Valid values: {string.Join(", ", allowed)}."));
        }

        internal static void ValidateOptionalEnum(
            string value, HashSet<string> allowed, string path, List<MachinePackageValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(value) && !allowed.Contains(value))
                issues.Add(Error(path, $"Value '{value}' is not allowed here. Valid values: {string.Join(", ", allowed)}."));
        }

        internal static void ValidateSingleReference(
            string id, HashSet<string> refs, string path, List<MachinePackageValidationIssue> issues)
        {
            ValidateRequiredText(id, path, issues);
            if (!string.IsNullOrWhiteSpace(id) && !refs.Contains(id))
                issues.Add(Error(path, $"Reference '{id}' does not resolve."));
        }

        internal static void ValidateOptionalReference(
            string id, HashSet<string> refs, string path, List<MachinePackageValidationIssue> issues)
        {
            if (!string.IsNullOrWhiteSpace(id) && !refs.Contains(id))
                issues.Add(Error(path, $"Reference '{id}' does not resolve."));
        }

        internal static void ValidateRequiredReferences(
            string[] ids, HashSet<string> refs, string path, List<MachinePackageValidationIssue> issues)
        {
            if (ids == null || ids.Length == 0)
            {
                issues.Add(Error(path, "At least one reference is required."));
                return;
            }
            ValidateOptionalReferences(ids, refs, path, issues);
        }

        internal static void ValidateOptionalReferences(
            string[] ids, HashSet<string> refs, string path, List<MachinePackageValidationIssue> issues)
        {
            if (ids == null) return;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                string itemPath = $"{path}[{i}]";
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(Error(itemPath, "Reference id cannot be empty."));
                    continue;
                }
                if (!refs.Contains(id))
                    issues.Add(Error(itemPath, $"Reference '{id}' does not resolve."));
            }
        }

        internal static bool HasAnyValues(string[] values)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
                if (!string.IsNullOrWhiteSpace(values[i])) return true;
            return false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static HashSet<string> S(params string[] values) =>
            new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }
}
