using System;
using OSE.UI.Controllers;
using OSE.UI.Presenters;
using UnityEngine;

namespace OSE.UI.Root
{
    internal sealed class SessionHudMediator
    {
        private const float HintToastDuration = 6f;
        private const float StepToastDuration = 2f;

        private string _hintTitle = "Guidance";
        private string _hintMessage = "Follow the guidance to continue.";
        private string _hintType = "Hint";
        private float _hintHideAtSeconds;
        private bool _hintToastActive;

        private string _stepToastMessage;
        private float _stepToastHideAtSeconds;
        private bool _stepToastActive;

        private string _milestoneMessage;
        private bool _milestoneActive;

        private int _hintsUsed;
        private int _failedAttempts;
        private float _currentStepSeconds;
        private float _totalSeconds;
        private bool _challengeActive;

        private readonly SessionHudPanelPresenter _presenter;
        private readonly SessionHudPanelController _controller;
        private readonly Func<bool> _isHintDisplayAllowed;

        public bool MilestoneActive => _milestoneActive;
        public string MilestoneMessage => _milestoneMessage;

        public SessionHudMediator(
            SessionHudPanelPresenter presenter,
            SessionHudPanelController controller,
            Func<bool> isHintDisplayAllowed)
        {
            _presenter = presenter;
            _controller = controller;
            _isHintDisplayAllowed = isHintDisplayAllowed;
        }

        /// <summary>
        /// Shows a hint toast with the given content. Returns true if the toast was activated.
        /// </summary>
        public bool ShowHintContent(string title, string message, string hintType)
        {
            if (!_isHintDisplayAllowed())
                return false;

            _hintTitle = string.IsNullOrWhiteSpace(title) ? _hintTitle : title;
            _hintMessage = string.IsNullOrWhiteSpace(message) ? _hintMessage : message;
            _hintType = string.IsNullOrWhiteSpace(hintType) ? _hintType : hintType;
            _hintHideAtSeconds = Application.isPlaying ? Time.time + HintToastDuration : 0f;
            _hintToastActive = true;
            return true;
        }

        public void ShowStepCompletionToast(string message)
        {
            _stepToastMessage = string.IsNullOrWhiteSpace(message) ? "Step Complete!" : message;
            _stepToastHideAtSeconds = Application.isPlaying ? Time.time + StepToastDuration : 0f;
            _stepToastActive = true;
        }

        public void ShowChallengeMetrics(
            int hintsUsed,
            int failedAttempts,
            float currentStepSeconds,
            float totalSeconds,
            bool challengeActive)
        {
            _hintsUsed = hintsUsed;
            _failedAttempts = failedAttempts;
            _currentStepSeconds = currentStepSeconds;
            _totalSeconds = totalSeconds;
            _challengeActive = challengeActive;
        }

        public void SetMilestone(string message)
        {
            _milestoneMessage = message;
            _milestoneActive = true;
        }

        public void ClearHintState()
        {
            _hintToastActive = false;
            _hintHideAtSeconds = 0f;
        }

        /// <summary>
        /// Tick hint/step-toast auto-hide timers. Returns true if a refresh is needed.
        /// </summary>
        public bool TickTimers()
        {
            bool needsRefresh = false;

            if (Application.isPlaying && _hintHideAtSeconds > 0f && Time.time >= _hintHideAtSeconds)
            {
                _hintHideAtSeconds = 0f;
                _hintToastActive = false;
                needsRefresh = true;
            }

            if (Application.isPlaying && _stepToastHideAtSeconds > 0f && Time.time >= _stepToastHideAtSeconds)
            {
                _stepToastHideAtSeconds = 0f;
                _stepToastActive = false;
                needsRefresh = true;
            }

            return needsRefresh;
        }

        public void RefreshSessionHudPanel(bool isBuilt, bool hasActiveModeProfile, bool showSessionHud)
        {
            if (!isBuilt)
                return;

            if (hasActiveModeProfile && !showSessionHud)
            {
                _controller.Hide();
                return;
            }

            SessionHudViewModel viewModel = _presenter.Create(
                _hintToastActive && _isHintDisplayAllowed(),
                _hintType,
                _hintTitle,
                _hintMessage,
                _challengeActive,
                _hintsUsed,
                _failedAttempts,
                _currentStepSeconds,
                _totalSeconds,
                _stepToastActive,
                _stepToastMessage,
                _milestoneActive,
                _milestoneMessage);

            if (viewModel.IsVisible)
            {
                _controller.Show(viewModel);
            }
            else
            {
                _controller.Hide();
            }
        }
    }
}
