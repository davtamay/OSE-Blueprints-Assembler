namespace OSE.UI.Presenters
{
    public readonly struct ToolInfoPanelViewModel
    {
        public ToolInfoPanelViewModel(
            string toolName,
            string category,
            string purpose,
            string usageNotes,
            string safetyNotes)
        {
            ToolName = toolName;
            Category = category;
            Purpose = purpose;
            UsageNotes = usageNotes;
            SafetyNotes = safetyNotes;
        }

        public string ToolName { get; }
        public string Category { get; }
        public string Purpose { get; }
        public string UsageNotes { get; }
        public string SafetyNotes { get; }
    }

    public sealed class ToolInfoPanelPresenter
    {
        public ToolInfoPanelViewModel Create(
            string toolName,
            string category,
            string purpose,
            string usageNotes,
            string safetyNotes)
        {
            return new ToolInfoPanelViewModel(
                Sanitize(toolName, "Selected Tool"),
                Sanitize(category, "General"),
                Sanitize(purpose, "Tool purpose metadata will be supplied by runtime content."),
                Sanitize(usageNotes, "Usage notes metadata will be supplied by runtime content."),
                Sanitize(safetyNotes, "Safety notes metadata will be supplied by runtime content."));
        }

        private static string Sanitize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
