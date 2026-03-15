namespace OSE.UI.Presenters
{
    public readonly struct HintToastViewModel
    {
        public HintToastViewModel(string eyebrow, string title, string message)
        {
            Eyebrow = eyebrow;
            Title = title;
            Message = message;
        }

        public string Eyebrow { get; }
        public string Title { get; }
        public string Message { get; }
    }

    public sealed class HintToastPanelPresenter
    {
        public HintToastViewModel Create(string hintType, string title, string message)
        {
            string eyebrow = string.IsNullOrWhiteSpace(hintType)
                ? "Hint"
                : $"Hint · {hintType.Trim()}";

            string displayTitle = string.IsNullOrWhiteSpace(title)
                ? "Guidance"
                : title.Trim();

            string displayMessage = string.IsNullOrWhiteSpace(message)
                ? "Follow the guidance to continue."
                : message.Trim();

            return new HintToastViewModel(eyebrow, displayTitle, displayMessage);
        }
    }
}
