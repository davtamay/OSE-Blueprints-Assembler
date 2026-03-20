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
            private readonly VisualElement _detailContainer;
            private readonly Button _chevron;
            private bool _expanded;

            public PartInfoPanelView()
            {
                UIToolkitStyleUtility.ApplyCompactPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                // --- Header row: eyebrow + name + chevron ---
                var headerRow = new VisualElement();
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.Center;
                headerRow.style.justifyContent = Justify.SpaceBetween;
                headerRow.pickingMode = PickingMode.Position;
                headerRow.RegisterCallback<ClickEvent>(OnHeaderClicked);

                var headerLeft = new VisualElement();
                headerLeft.style.flexDirection = FlexDirection.Row;
                headerLeft.style.alignItems = Align.Center;
                headerLeft.style.flexGrow = 1f;
                headerLeft.style.flexShrink = 1f;
                headerLeft.style.overflow = Overflow.Hidden;

                var eyebrow = UIToolkitStyleUtility.CreateEyebrowLabel("PART");
                eyebrow.style.marginBottom = 0f;
                eyebrow.style.marginRight = 8f;
                eyebrow.style.flexShrink = 0f;
                headerLeft.Add(eyebrow);
                headerLeft.Add(PartNameLabel);

                _chevron = UIToolkitStyleUtility.CreateChevronButton(false);
                _chevron.clicked += ToggleExpanded;

                headerRow.Add(headerLeft);
                headerRow.Add(_chevron);
                Add(headerRow);

                // --- Detail container (collapsed by default) ---
                _detailContainer = new VisualElement();
                _detailContainer.style.display = DisplayStyle.None;
                _detailContainer.style.marginTop = 8f;
                _detailContainer.Add(CreateFieldBlock("Function", FunctionValueLabel));
                _detailContainer.Add(CreateFieldBlock("Material", MaterialValueLabel));
                _detailContainer.Add(CreateFieldBlock("Tool", ToolValueLabel));
                _detailContainer.Add(CreateFieldBlock("Search Terms", SearchTermsValueLabel));
                Add(_detailContainer);
            }

            public Label PartNameLabel { get; } =
                UIToolkitStyleUtility.CreateCompactTitleLabel("Selected Part");

            public Label FunctionValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label MaterialValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label ToolValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label SearchTermsValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            private void OnHeaderClicked(ClickEvent evt)
            {
                ToggleExpanded();
                evt.StopPropagation();
            }

            private void ToggleExpanded()
            {
                _expanded = !_expanded;
                _detailContainer.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
                _chevron.text = _expanded ? "\u25BE" : "\u25B8";
            }

            private static VisualElement CreateFieldBlock(string labelText, Label valueLabel)
            {
                VisualElement block = new VisualElement();
                block.style.flexDirection = FlexDirection.Column;
                block.style.marginTop = 8f;
                block.style.paddingTop = 8f;
                block.style.borderTopWidth = 1f;
                block.style.borderTopColor = new Color(0.28f, 0.36f, 0.46f, 0.8f);
                block.Add(UIToolkitStyleUtility.CreateFieldLabel(labelText));
                block.Add(valueLabel);
                return block;
            }
        }
    }
}
