using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class StepPanelController : PanelControllerBase<StepPanelViewModel>
    {
        private StepPanelView _view;

        protected override string PanelName => "ose-step-panel";

        protected override VisualElement CreateView() => new StepPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (StepPanelView)root;
        }

        protected override void ApplyViewModel(StepPanelViewModel viewModel)
        {
            _view.StepLabel.text = viewModel.StepLabel;
            _view.TitleLabel.text = viewModel.Title;
            _view.InstructionLabel.text = viewModel.Instruction;
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class StepPanelView : VisualElement
        {
            public Label StepLabel { get; }
            public Label TitleLabel { get; }
            public Label InstructionLabel { get; }

            public StepPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                StepLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Current Step");
                TitleLabel = UIToolkitStyleUtility.CreateTitleLabel("Awaiting Step Data");
                InstructionLabel = UIToolkitStyleUtility.CreateBodyLabel(
                    "Instruction text is provided by runtime presenters.");

                Add(StepLabel);
                Add(TitleLabel);
                Add(InstructionLabel);
            }
        }
    }
}
