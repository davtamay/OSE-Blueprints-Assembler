namespace OSE.Interaction
{
    /// <summary>
    /// Canonical profile identifiers used in machine.json step definitions.
    /// Centralizes magic strings so profile-dependent logic across the codebase
    /// stays consistent and discoverable.
    /// </summary>
    public static class ToolActionProfiles
    {
        // ── Use-family profiles ──
        public const string Torque = "Torque";
        public const string Weld = "Weld";
        public const string Cut = "Cut";
        public const string Strike = "Strike";
        public const string Measure = "Measure";
        public const string SquareCheck = "SquareCheck";

        // ── Place-family profiles ──
        public const string Clamp = "Clamp";
        public const string AxisFit = "AxisFit";

        // ── Connect-family profiles ──
        public const string Cable = "Cable";

        // ── Tool action types (requiredToolActions[].type) ──
        public static class ActionTypes
        {
            public const string Tighten = "tighten";
            public const string WeldPass = "weld_pass";
            public const string GrindPass = "grind_pass";
            public const string MeasureAction = "measure";
            public const string StrikeAction = "strike";
        }

        // ── Tool persistence (fallback) ──
        // Prefer ToolDefinition.persistent from machine.json (checked in PersistentToolController).
        // This substring fallback exists for backward compatibility.

        public static bool IsToolPersistent(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return false;
            return toolId.IndexOf("clamp", System.StringComparison.OrdinalIgnoreCase) >= 0
                || toolId.IndexOf("fixture", System.StringComparison.OrdinalIgnoreCase) >= 0
                || toolId.IndexOf("vise", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
