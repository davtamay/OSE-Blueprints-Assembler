namespace OSE.UI.Presenters
{
    public readonly struct StepPanelViewModel
    {
        public StepPanelViewModel(string stepLabel, string title, string instruction)
        {
            StepLabel = stepLabel;
            Title = title;
            Instruction = instruction;
        }

        public string StepLabel { get; }
        public string Title { get; }
        public string Instruction { get; }
    }

    public sealed class StepPanelPresenter
    {
        public StepPanelViewModel Create(
            int currentStepNumber,
            int totalSteps,
            string title,
            string instruction)
        {
            string stepLabel = currentStepNumber > 0 && totalSteps > 0
                ? $"Step {currentStepNumber} of {totalSteps}"
                : "Current Step";

            string displayTitle = string.IsNullOrWhiteSpace(title)
                ? "Awaiting Step Data"
                : title.Trim();

            string displayInstruction = string.IsNullOrWhiteSpace(instruction)
                ? "Instruction text is provided by runtime presenters."
                : instruction.Trim();

            return new StepPanelViewModel(stepLabel, displayTitle, displayInstruction);
        }
    }
}
