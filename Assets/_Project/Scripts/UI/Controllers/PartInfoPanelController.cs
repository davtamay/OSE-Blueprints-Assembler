using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class PartInfoPanelController : PanelControllerBase<PartInfoPanelViewModel>
    {
        private PartInfoPanelView _view;

        protected override string PanelName => "ose-part-info-panel";

        protected override VisualElement CreateView() => new PartInfoPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (PartInfoPanelView)root;
        }

        protected override void ApplyViewModel(PartInfoPanelViewModel viewModel)
        {
            _view.PartNameLabel.text = viewModel.PartName;
            _view.FunctionValueLabel.text = viewModel.Function;
            _view.MaterialValueLabel.text = viewModel.Material;
            _view.ToolValueLabel.text = viewModel.Tool;
            _view.SearchTermsValueLabel.text = viewModel.SearchTerms;
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class PartInfoPanelView : VisualElement
        {
            public PartInfoPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                Add(UIToolkitStyleUtility.CreateEyebrowLabel("Part Info"));
                Add(PartNameLabel);
                Add(CreateFieldBlock("Function", FunctionValueLabel));
                Add(CreateFieldBlock("Material", MaterialValueLabel));
                Add(CreateFieldBlock("Tool", ToolValueLabel));
                Add(CreateFieldBlock("Search Terms", SearchTermsValueLabel));
            }

            public Label PartNameLabel { get; } =
                UIToolkitStyleUtility.CreateTitleLabel("Selected Part");

            public Label FunctionValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Function metadata will be supplied by runtime content.");

            public Label MaterialValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Material metadata will be supplied by runtime content.");

            public Label ToolValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Tool metadata will be supplied by runtime content.");

            public Label SearchTermsValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("Search terms will be supplied by runtime content.");

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
