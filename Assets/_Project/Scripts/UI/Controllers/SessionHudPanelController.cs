using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class SessionHudPanelController : PanelControllerBase<SessionHudViewModel>
    {
        private SessionHudPanelView _view;

        protected override string PanelName => "ose-session-hud-panel";

        protected override VisualElement CreateView() => new SessionHudPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (SessionHudPanelView)root;
        }

        protected override void ApplyViewModel(SessionHudViewModel viewModel)
        {
            _view.HintEyebrow.text = viewModel.HintEyebrow;
            _view.HintTitle.text = viewModel.HintTitle;
            _view.HintMessage.text = viewModel.HintMessage;
            _view.MetricsLabel.text = viewModel.MetricsLine;
            _view.StepToastLabel.text = viewModel.StepToastMessage;
            _view.MilestoneLabel.text = viewModel.MilestoneMessage;

            _view.SetHintVisible(viewModel.ShowHint);
            _view.SetMetricsVisible(viewModel.ShowMetrics, viewModel.ShowHint);
            _view.SetStepToastVisible(viewModel.ShowStepToast);
            _view.SetMilestoneVisible(viewModel.ShowMilestone);
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class SessionHudPanelView : VisualElement
        {
            public Label HintEyebrow { get; }
            public Label HintTitle { get; }
            public Label HintMessage { get; }
            public Label MetricsLabel { get; }
            public Label StepToastLabel { get; }
            public Label MilestoneLabel { get; }

            private readonly VisualElement _hintBlock;
            private readonly VisualElement _metricsBlock;
            private readonly VisualElement _metricsDivider;
            private readonly VisualElement _stepToastBlock;
            private readonly VisualElement _milestoneBlock;

            public SessionHudPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;
                style.width = 320f;
                style.maxWidth = 360f;
                style.paddingLeft = 14f;
                style.paddingRight = 14f;
                style.paddingTop = 12f;
                style.paddingBottom = 12f;
                style.marginBottom = 12f;
                style.backgroundColor = new Color(0.08f, 0.12f, 0.2f, 0.94f);

                // Step completion toast — subtle feedback banner, not a button
                _stepToastBlock = new VisualElement();
                _stepToastBlock.style.flexDirection = FlexDirection.Row;
                _stepToastBlock.style.alignItems = Align.Center;
                _stepToastBlock.style.backgroundColor = new Color(0.12f, 0.28f, 0.18f, 0.85f);
                _stepToastBlock.style.borderLeftWidth = 3f;
                _stepToastBlock.style.borderLeftColor = new Color(0.3f, 0.85f, 0.5f, 1f);
                _stepToastBlock.style.borderTopLeftRadius = 0f;
                _stepToastBlock.style.borderTopRightRadius = 2f;
                _stepToastBlock.style.borderBottomLeftRadius = 0f;
                _stepToastBlock.style.borderBottomRightRadius = 2f;
                _stepToastBlock.style.paddingLeft = 10f;
                _stepToastBlock.style.paddingRight = 12f;
                _stepToastBlock.style.paddingTop = 6f;
                _stepToastBlock.style.paddingBottom = 6f;
                _stepToastBlock.style.marginBottom = 8f;
                _stepToastBlock.style.display = DisplayStyle.None;
                _stepToastBlock.pickingMode = PickingMode.Ignore;
                Add(_stepToastBlock);

                var toastIcon = new Label("\u2713");
                toastIcon.style.fontSize = 16f;
                toastIcon.style.color = new Color(0.3f, 0.85f, 0.5f, 1f);
                toastIcon.style.marginRight = 8f;
                toastIcon.style.unityFontStyleAndWeight = FontStyle.Bold;
                _stepToastBlock.Add(toastIcon);

                StepToastLabel = new Label("Step Complete!");
                StepToastLabel.style.color = new Color(0.78f, 0.92f, 0.82f, 1f);
                StepToastLabel.style.fontSize = 13f;
                StepToastLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
                StepToastLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                _stepToastBlock.Add(StepToastLabel);

                // Milestone card — completion feedback, visually distinct from buttons
                _milestoneBlock = new VisualElement();
                _milestoneBlock.style.flexDirection = FlexDirection.Column;
                _milestoneBlock.style.alignItems = Align.Center;
                _milestoneBlock.style.backgroundColor = new Color(0.08f, 0.18f, 0.12f, 0.92f);
                _milestoneBlock.style.borderTopWidth = 2f;
                _milestoneBlock.style.borderTopColor = new Color(0.3f, 0.85f, 0.5f, 1f);
                _milestoneBlock.style.borderBottomWidth = 2f;
                _milestoneBlock.style.borderBottomColor = new Color(0.3f, 0.85f, 0.5f, 1f);
                _milestoneBlock.style.borderTopLeftRadius = 2f;
                _milestoneBlock.style.borderTopRightRadius = 2f;
                _milestoneBlock.style.borderBottomLeftRadius = 2f;
                _milestoneBlock.style.borderBottomRightRadius = 2f;
                _milestoneBlock.style.paddingLeft = 16f;
                _milestoneBlock.style.paddingRight = 16f;
                _milestoneBlock.style.paddingTop = 14f;
                _milestoneBlock.style.paddingBottom = 14f;
                _milestoneBlock.style.marginBottom = 8f;
                _milestoneBlock.style.display = DisplayStyle.None;
                _milestoneBlock.pickingMode = PickingMode.Ignore;
                Add(_milestoneBlock);

                var milestoneIcon = new Label("\u2605");
                milestoneIcon.style.fontSize = 22f;
                milestoneIcon.style.color = new Color(0.3f, 0.85f, 0.5f, 1f);
                milestoneIcon.style.marginBottom = 4f;
                _milestoneBlock.Add(milestoneIcon);

                MilestoneLabel = new Label("Session Complete!");
                MilestoneLabel.style.color = new Color(0.82f, 0.95f, 0.86f, 1f);
                MilestoneLabel.style.fontSize = 16f;
                MilestoneLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                MilestoneLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                _milestoneBlock.Add(MilestoneLabel);

                _hintBlock = new VisualElement();
                _hintBlock.style.flexDirection = FlexDirection.Column;
                _hintBlock.style.marginBottom = 6f;
                Add(_hintBlock);

                var accent = new VisualElement();
                accent.style.height = 2f;
                accent.style.backgroundColor = new Color(0.42f, 0.82f, 1f, 1f);
                accent.style.marginBottom = 8f;
                _hintBlock.Add(accent);

                HintEyebrow = UIToolkitStyleUtility.CreateEyebrowLabel("Hint");
                HintEyebrow.style.marginBottom = 4f;
                HintTitle = UIToolkitStyleUtility.CreateTitleLabel("Guidance");
                HintTitle.style.fontSize = 16f;
                HintMessage = UIToolkitStyleUtility.CreateBodyLabel("Follow the guidance to continue.");
                HintMessage.style.fontSize = 12f;
                HintMessage.style.marginTop = 4f;

                _hintBlock.Add(HintEyebrow);
                _hintBlock.Add(HintTitle);
                _hintBlock.Add(HintMessage);

                _metricsBlock = new VisualElement();
                _metricsBlock.style.flexDirection = FlexDirection.Column;
                _metricsBlock.style.marginTop = 4f;
                Add(_metricsBlock);

                _metricsDivider = new VisualElement();
                _metricsDivider.style.height = 1f;
                _metricsDivider.style.backgroundColor = new Color(0.28f, 0.36f, 0.46f, 0.85f);
                _metricsDivider.style.marginBottom = 6f;
                _metricsBlock.Add(_metricsDivider);

                MetricsLabel = UIToolkitStyleUtility.CreateBodyLabel("Step 00:00 | Total 00:00 | Fails 0 | Hints 0");
                MetricsLabel.style.fontSize = 12f;
                MetricsLabel.style.marginTop = 0f;
                _metricsBlock.Add(MetricsLabel);
            }

            public void SetHintVisible(bool visible)
            {
                _hintBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetMetricsVisible(bool visible, bool showDivider)
            {
                _metricsBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                _metricsDivider.style.display = visible && showDivider ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetStepToastVisible(bool visible)
            {
                _stepToastBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetMilestoneVisible(bool visible)
            {
                _milestoneBlock.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
