using System;

namespace OSE.UI.Presenters
{
    public readonly struct ChallengeMetricsViewModel
    {
        public ChallengeMetricsViewModel(
            string title,
            string hintsText,
            string attemptsText,
            string stepTimeText,
            string totalTimeText)
        {
            Title = title;
            HintsText = hintsText;
            AttemptsText = attemptsText;
            StepTimeText = stepTimeText;
            TotalTimeText = totalTimeText;
        }

        public string Title { get; }
        public string HintsText { get; }
        public string AttemptsText { get; }
        public string StepTimeText { get; }
        public string TotalTimeText { get; }
    }

    public sealed class ChallengeMetricsPanelPresenter
    {
        public ChallengeMetricsViewModel Create(
            bool challengeActive,
            int hintsUsed,
            int failedAttempts,
            float currentStepSeconds,
            float totalSeconds)
        {
            string title = challengeActive ? "Challenge Metrics" : "Session Metrics";

            return new ChallengeMetricsViewModel(
                title,
                $"Hints used: {Math.Max(0, hintsUsed)}",
                $"Failed attempts: {Math.Max(0, failedAttempts)}",
                $"Step time: {FormatTime(currentStepSeconds)}",
                $"Total time: {FormatTime(totalSeconds)}");
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
