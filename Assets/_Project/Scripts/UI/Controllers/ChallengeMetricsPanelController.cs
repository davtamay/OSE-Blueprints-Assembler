using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class ChallengeMetricsPanelController : PanelControllerBase<ChallengeMetricsViewModel>
    {
        private ChallengeMetricsPanelView _view;

        protected override string PanelName => "ose-challenge-metrics-panel";

        protected override VisualElement CreateView() => new ChallengeMetricsPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (ChallengeMetricsPanelView)root;
        }

        protected override void ApplyViewModel(ChallengeMetricsViewModel viewModel)
        {
            _view.TitleLabel.text = viewModel.Title;
            _view.HintsLabel.text = viewModel.HintsText;
            _view.AttemptsLabel.text = viewModel.AttemptsText;
            _view.StepTimeLabel.text = viewModel.StepTimeText;
            _view.TotalTimeLabel.text = viewModel.TotalTimeText;
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class ChallengeMetricsPanelView : VisualElement
        {
            public Label TitleLabel { get; }
            public Label HintsLabel { get; }
            public Label AttemptsLabel { get; }
            public Label StepTimeLabel { get; }
            public Label TotalTimeLabel { get; }

            public ChallengeMetricsPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                TitleLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Challenge Metrics");
                HintsLabel = UIToolkitStyleUtility.CreateBodyLabel("Hints used: 0");
                AttemptsLabel = UIToolkitStyleUtility.CreateBodyLabel("Failed attempts: 0");
                StepTimeLabel = UIToolkitStyleUtility.CreateBodyLabel("Step time: 00:00");
                TotalTimeLabel = UIToolkitStyleUtility.CreateBodyLabel("Total time: 00:00");

                Add(TitleLabel);
                Add(HintsLabel);
                Add(AttemptsLabel);
                Add(StepTimeLabel);
                Add(TotalTimeLabel);
            }
        }
    }
}
