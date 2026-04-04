using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Maps between <see cref="ToolActionType"/> and its string representations.
    /// Extracted from <see cref="ToolRuntimeController"/> so the string-to-enum
    /// and enum-to-display-string logic is testable in isolation.
    /// </summary>
    internal static class ToolActionTypeHelper
    {
        /// <summary>
        /// Parses an action type string from machine.json into a <see cref="ToolActionType"/>.
        /// Returns <see cref="ToolActionType.None"/> for unrecognised or null values.
        /// </summary>
        public static ToolActionType Parse(string actionType)
        {
            if (string.IsNullOrWhiteSpace(actionType))
                return ToolActionType.None;

            return actionType.Trim().ToLowerInvariant() switch
            {
                "measure"    => ToolActionType.Measure,
                "tighten"    => ToolActionType.Tighten,
                "strike"     => ToolActionType.Strike,
                "weld_pass"  => ToolActionType.WeldPass,
                "grind_pass" => ToolActionType.GrindPass,
                _            => ToolActionType.None
            };
        }

        /// <summary>Returns the progress verb used in mid-action feedback (e.g. "Weld pass 2/4").</summary>
        public static string GetVerb(ToolActionType actionType) => actionType switch
        {
            ToolActionType.Measure   => "Measurement pass",
            ToolActionType.Tighten   => "Tightening pass",
            ToolActionType.Strike    => "Strike",
            ToolActionType.WeldPass  => "Weld pass",
            ToolActionType.GrindPass => "Grinding pass",
            _                        => "Action"
        };

        /// <summary>Returns the completion noun used in success feedback (e.g. "Weld pass complete.").</summary>
        public static string GetNoun(ToolActionType actionType) => actionType switch
        {
            ToolActionType.Measure   => "Measurement",
            ToolActionType.Tighten   => "Tightening",
            ToolActionType.Strike    => "Impact",
            ToolActionType.WeldPass  => "Weld pass",
            ToolActionType.GrindPass => "Grinding pass",
            _                        => "Tool action"
        };
    }
}
