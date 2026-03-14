namespace OSE.UI.Presenters
{
    public readonly struct PartInfoPanelViewModel
    {
        public PartInfoPanelViewModel(
            string partName,
            string function,
            string material,
            string tool,
            string searchTerms)
        {
            PartName = partName;
            Function = function;
            Material = material;
            Tool = tool;
            SearchTerms = searchTerms;
        }

        public string PartName { get; }
        public string Function { get; }
        public string Material { get; }
        public string Tool { get; }
        public string SearchTerms { get; }
    }

    public sealed class PartInfoPanelPresenter
    {
        public PartInfoPanelViewModel Create(
            string partName,
            string function,
            string material,
            string tool,
            string searchTerms)
        {
            return new PartInfoPanelViewModel(
                Sanitize(partName, "Selected Part"),
                Sanitize(function, "Function metadata will be supplied by runtime content."),
                Sanitize(material, "Material metadata will be supplied by runtime content."),
                Sanitize(tool, "Tool metadata will be supplied by runtime content."),
                Sanitize(searchTerms, "Search terms will be supplied by runtime content."));
        }

        private static string Sanitize(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
