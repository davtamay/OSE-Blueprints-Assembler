using OSE.Core;

namespace OSE.UI.Presenters
{
    public readonly struct StepPanelViewModel
    {
        public StepPanelViewModel(string stepLabel, string title, string instruction,
            bool showConfirmButton, float progressRatio, bool showHintButton,
            ConfirmGate confirmGate, bool confirmUnlocked,
            bool showContextActionButton, string contextActionLabel, bool contextActionEnabled,
            string assemblyName = null, float globalProgressRatio = 0f,
            string globalProgressLabel = null)
        {
            StepLabel = stepLabel;
            Title = title;
            Instruction = instruction;
            ShowConfirmButton = showConfirmButton;
            ProgressRatio = progressRatio;
            ShowHintButton = showHintButton;
            ConfirmGate = confirmGate;
            ConfirmUnlocked = confirmUnlocked;
            ShowContextActionButton = showContextActionButton;
            ContextActionLabel = contextActionLabel;
            ContextActionEnabled = contextActionEnabled;
            AssemblyName = assemblyName;
            GlobalProgressRatio = globalProgressRatio;
            GlobalProgressLabel = globalProgressLabel;
        }

        public string StepLabel { get; }
        public string Title { get; }
        public string Instruction { get; }
        public bool ShowConfirmButton { get; }
        public float ProgressRatio { get; }
        public bool ShowHintButton { get; }
        public ConfirmGate ConfirmGate { get; }
        public bool ConfirmUnlocked { get; }
        public bool ShowContextActionButton { get; }
        public string ContextActionLabel { get; }
        public bool ContextActionEnabled { get; }
        public string AssemblyName { get; }
        public float GlobalProgressRatio { get; }
        public string GlobalProgressLabel { get; }
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
            bool showContextActionButton = false,
            string contextActionLabel = null,
            bool contextActionEnabled = false,
            float? progressOverride = null,
            string assemblyName = null,
            int globalStepIndex = 0,
            int globalTotalSteps = 0)
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

            float globalProgressRatio = globalTotalSteps > 0
                ? (float)globalStepIndex / globalTotalSteps
                : 0f;
            string globalProgressLabel = globalTotalSteps > 0
                ? $"Overall: {globalStepIndex} of {globalTotalSteps}"
                : null;

            return new StepPanelViewModel(stepLabel, displayTitle, displayInstruction,
                showConfirmButton, progressRatio, showHintButton, confirmGate, confirmUnlocked,
                showContextActionButton, contextActionLabel, contextActionEnabled,
                assemblyName, globalProgressRatio, globalProgressLabel);
        }
    }
}
