using OSE.Core;

namespace OSE.UI.Presenters
{
    public readonly struct StepPanelViewModel
    {
        public StepPanelViewModel(string stepLabel, string title, string instruction,
            bool showConfirmButton, float progressRatio, bool showHintButton,
            ConfirmGate confirmGate, bool confirmUnlocked)
        {
            StepLabel = stepLabel;
            Title = title;
            Instruction = instruction;
            ShowConfirmButton = showConfirmButton;
            ProgressRatio = progressRatio;
            ShowHintButton = showHintButton;
            ConfirmGate = confirmGate;
            ConfirmUnlocked = confirmUnlocked;
        }

        public string StepLabel { get; }
        public string Title { get; }
        public string Instruction { get; }
        public bool ShowConfirmButton { get; }
        public float ProgressRatio { get; }
        public bool ShowHintButton { get; }
        public ConfirmGate ConfirmGate { get; }
        public bool ConfirmUnlocked { get; }
    }

    public sealed class StepPanelPresenter
    {
        public StepPanelViewModel Create(
            int currentStepNumber,
            int totalSteps,
            string title,
            string instruction,
            bool showConfirmButton = false,
            bool showHintButton = false,
            ConfirmGate confirmGate = ConfirmGate.None,
            bool confirmUnlocked = true,
            float? progressOverride = null)
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

            float progressRatio;
            if (progressOverride.HasValue)
            {
                progressRatio = progressOverride.Value;
            }
            else
            {
                progressRatio = totalSteps > 0
                    ? (float)(currentStepNumber - 1) / totalSteps
                    : 0f;
            }
            if (progressRatio < 0f) progressRatio = 0f;
            if (progressRatio > 1f) progressRatio = 1f;

            return new StepPanelViewModel(stepLabel, displayTitle, displayInstruction,
                showConfirmButton, progressRatio, showHintButton, confirmGate, confirmUnlocked);
        }
    }
}
