using System;

namespace OSE.UI.Presenters
{
    public readonly struct SessionHudViewModel
    {
        public SessionHudViewModel(
            bool showHint,
            string hintEyebrow,
            string hintTitle,
            string hintMessage,
            bool showMetrics,
            string metricsLine,
            bool showStepToast,
            string stepToastMessage,
            bool showMilestone,
            string milestoneMessage)
        {
            ShowHint = showHint;
            HintEyebrow = hintEyebrow;
            HintTitle = hintTitle;
            HintMessage = hintMessage;
            ShowMetrics = showMetrics;
            MetricsLine = metricsLine;
            ShowStepToast = showStepToast;
            StepToastMessage = stepToastMessage;
            ShowMilestone = showMilestone;
            MilestoneMessage = milestoneMessage;
        }

        public bool ShowHint { get; }
        public string HintEyebrow { get; }
        public string HintTitle { get; }
        public string HintMessage { get; }
        public bool ShowMetrics { get; }
        public string MetricsLine { get; }
        public bool ShowStepToast { get; }
        public string StepToastMessage { get; }
        public bool ShowMilestone { get; }
        public string MilestoneMessage { get; }
        public bool IsVisible => ShowHint || ShowMetrics || ShowStepToast || ShowMilestone;
    }

    public sealed class SessionHudPanelPresenter
    {
        public SessionHudViewModel Create(
            bool hintActive,
            string hintType,
            string hintTitle,
            string hintMessage,
            bool challengeActive,
            int hintsUsed,
            int failedAttempts,
            float currentStepSeconds,
            float totalSeconds,
            bool stepToastActive = false,
            string stepToastMessage = null,
            bool milestoneActive = false,
            string milestoneMessage = null)
        {
            bool showHint = hintActive;
            string eyebrow = string.IsNullOrWhiteSpace(hintType)
                ? "Hint"
                : $"Hint - {hintType.Trim()}";

            string title = string.IsNullOrWhiteSpace(hintTitle)
                ? "Guidance"
                : hintTitle.Trim();

            string message = string.IsNullOrWhiteSpace(hintMessage)
                ? "Follow the guidance to continue."
                : hintMessage.Trim();

            bool showMetrics = challengeActive;
            string metricsLine = showMetrics
                ? BuildMetricsLine(hintsUsed, failedAttempts, currentStepSeconds, totalSeconds)
                : string.Empty;

            return new SessionHudViewModel(
                showHint,
                eyebrow,
                title,
                message,
                showMetrics,
                metricsLine,
                stepToastActive,
                stepToastMessage ?? string.Empty,
                milestoneActive,
                milestoneMessage ?? string.Empty);
        }

        private static string BuildMetricsLine(
            int hintsUsed,
            int failedAttempts,
            float currentStepSeconds,
            float totalSeconds)
        {
            string stepTime = FormatTime(currentStepSeconds);
            string totalTime = FormatTime(totalSeconds);
            int safeHints = Math.Max(0, hintsUsed);
            int safeFails = Math.Max(0, failedAttempts);

            return $"Step {stepTime} | Total {totalTime} | Fails {safeFails} | Hints {safeHints}";
        }

        private static string FormatTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                seconds = 0f;

            if (seconds < 0f)
                seconds = 0f;

            int totalSeconds = (int)Math.Floor(seconds);
            int minutes = totalSeconds / 60;
            int secs = totalSeconds % 60;
            int hours = minutes / 60;
            minutes %= 60;

            if (hours > 0)
                return $"{hours}:{minutes:00}:{secs:00}";

            return $"{minutes:00}:{secs:00}";
        }
    }
}
