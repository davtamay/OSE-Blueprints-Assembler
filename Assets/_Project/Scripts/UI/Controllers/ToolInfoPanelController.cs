using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class ToolInfoPanelController : PanelControllerBase<ToolInfoPanelViewModel>
    {
        private ToolInfoPanelView _view;

        protected override string PanelName => "ose-tool-info-panel";

        protected override VisualElement CreateView() => new ToolInfoPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (ToolInfoPanelView)root;
        }

        protected override void ApplyViewModel(ToolInfoPanelViewModel viewModel)
        {
            _view.ToolNameLabel.text = viewModel.ToolName;
            _view.CategoryValueLabel.text = viewModel.Category;
            _view.PurposeValueLabel.text = viewModel.Purpose;
            _view.UsageValueLabel.text = viewModel.UsageNotes;
            _view.SafetyValueLabel.text = viewModel.SafetyNotes;
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class ToolInfoPanelView : VisualElement
        {
            public ToolInfoPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;
                style.marginTop = 4f;

                Add(UIToolkitStyleUtility.CreateEyebrowLabel("Tool Info"));
                Add(ToolNameLabel);
                Add(CreateFieldBlock("Category", CategoryValueLabel));
                Add(CreateFieldBlock("Purpose", PurposeValueLabel));
                Add(CreateFieldBlock("Usage", UsageValueLabel));
                Add(CreateFieldBlock("Safety", SafetyValueLabel));
            }

            public Label ToolNameLabel { get; } =
                UIToolkitStyleUtility.CreateTitleLabel("Selected Tool");

            public Label CategoryValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Tool category metadata will be supplied by runtime content.");

            public Label PurposeValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Tool purpose metadata will be supplied by runtime content.");

            public Label UsageValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Tool usage notes metadata will be supplied by runtime content.");

            public Label SafetyValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Tool safety notes metadata will be supplied by runtime content.");

            private static VisualElement CreateFieldBlock(string labelText, Label valueLabel)
            {
                VisualElement block = new VisualElement();
                block.style.flexDirection = FlexDirection.Column;
                block.style.marginTop = 10f;
                block.style.paddingTop = 10f;
                block.style.borderTopWidth = 1f;
                block.style.borderTopColor = new Color(0.28f, 0.36f, 0.46f, 0.8f);
                block.Add(UIToolkitStyleUtility.CreateFieldLabel(labelText));
                block.Add(valueLabel);
                return block;
            }
        }
    }
}
