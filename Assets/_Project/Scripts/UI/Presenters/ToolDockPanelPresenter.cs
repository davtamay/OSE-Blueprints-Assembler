using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.UI.Presenters
{
    public readonly struct ToolDockEntryViewModel
    {
        public ToolDockEntryViewModel(
            string toolId,
            string displayName,
            string category,
            bool isRequired,
            bool isEquipped)
        {
            ToolId = toolId;
            DisplayName = displayName;
            Category = category;
            IsRequired = isRequired;
            IsEquipped = isEquipped;
        }

        public string ToolId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public bool IsRequired { get; }
        public bool IsEquipped { get; }
    }

    public readonly struct ToolDockPanelViewModel
    {
        public ToolDockPanelViewModel(
            bool isExpanded,
            string toggleLabel,
            string activeToolLabel,
            ToolDockEntryViewModel[] entries,
            string emptyStateMessage,
            bool showUnequipAction,
            string unequipLabel)
        {
            IsExpanded = isExpanded;
            ToggleLabel = toggleLabel;
            ActiveToolLabel = activeToolLabel;
            Entries = entries;
            EmptyStateMessage = emptyStateMessage;
            ShowUnequipAction = showUnequipAction;
            UnequipLabel = unequipLabel;
        }

        public bool IsExpanded { get; }
        public string ToggleLabel { get; }
        public string ActiveToolLabel { get; }
        public ToolDockEntryViewModel[] Entries { get; }
        public string EmptyStateMessage { get; }
        public bool ShowUnequipAction { get; }
        public string UnequipLabel { get; }
    }

    public sealed class ToolDockPanelPresenter
    {
        public ToolDockPanelViewModel Create(
            ToolDefinition[] tools,
            string[] requiredToolIds,
            string activeToolId,
            bool isExpanded)
        {
            ToolDefinition[] safeTools = tools ?? Array.Empty<ToolDefinition>();
            HashSet<string> required = BuildRequiredSet(requiredToolIds);

            ToolDockEntryViewModel[] entries = BuildEntries(safeTools, required, activeToolId);
            string activeToolLabel = ResolveActiveToolLabel(safeTools, activeToolId);
            string toggleLabel = isExpanded ? "Hide Tools" : "Tools";
            string emptyMessage = safeTools.Length == 0
                ? "No tools are defined for this package."
                : string.Empty;
            bool showUnequipAction = !string.IsNullOrWhiteSpace(activeToolId);

            return new ToolDockPanelViewModel(
                isExpanded,
                toggleLabel,
                activeToolLabel,
                entries,
                emptyMessage,
                showUnequipAction,
                "Clear Active Tool");
        }

        private static HashSet<string> BuildRequiredSet(string[] requiredToolIds)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (requiredToolIds == null)
                return set;

            for (int i = 0; i < requiredToolIds.Length; i++)
            {
                string toolId = requiredToolIds[i];
                if (string.IsNullOrWhiteSpace(toolId))
                    continue;

                set.Add(toolId.Trim());
            }

            return set;
        }

        private static ToolDockEntryViewModel[] BuildEntries(
            ToolDefinition[] tools,
            HashSet<string> requiredToolIds,
            string activeToolId)
        {
            if (tools == null || tools.Length == 0)
                return Array.Empty<ToolDockEntryViewModel>();

            List<ToolDockEntryViewModel> required = new List<ToolDockEntryViewModel>();
            List<ToolDockEntryViewModel> optional = new List<ToolDockEntryViewModel>();

            for (int i = 0; i < tools.Length; i++)
            {
                ToolDefinition tool = tools[i];
                if (tool == null || string.IsNullOrWhiteSpace(tool.id))
                    continue;

                string toolId = tool.id.Trim();
                bool isRequired = requiredToolIds.Contains(toolId);
                bool isEquipped = !string.IsNullOrWhiteSpace(activeToolId)
                    && string.Equals(activeToolId, toolId, StringComparison.OrdinalIgnoreCase);

                ToolDockEntryViewModel entry = new ToolDockEntryViewModel(
                    toolId,
                    tool.GetDisplayName(),
                    Sanitize(tool.category, "general"),
                    isRequired,
                    isEquipped);

                if (isRequired)
                    required.Add(entry);
                else
                    optional.Add(entry);
            }

            ToolDockEntryViewModel[] result = new ToolDockEntryViewModel[required.Count + optional.Count];
            int index = 0;
            for (int i = 0; i < required.Count; i++)
                result[index++] = required[i];
            for (int i = 0; i < optional.Count; i++)
                result[index++] = optional[i];
            return result;
        }

        private static string ResolveActiveToolLabel(ToolDefinition[] tools, string activeToolId)
        {
            if (string.IsNullOrWhiteSpace(activeToolId))
                return "Active tool: None";

            if (tools != null)
            {
                for (int i = 0; i < tools.Length; i++)
                {
                    ToolDefinition tool = tools[i];
                    if (tool == null || string.IsNullOrWhiteSpace(tool.id))
                        continue;

                    if (string.Equals(tool.id, activeToolId, StringComparison.OrdinalIgnoreCase))
                        return $"Active tool: {tool.GetDisplayName()}";
                }
            }

            return $"Active tool: {activeToolId}";
        }

        private static string Sanitize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
