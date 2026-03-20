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
            private readonly VisualElement _detailContainer;
            private readonly Button _chevron;
            private bool _expanded;

            public ToolInfoPanelView()
            {
                UIToolkitStyleUtility.ApplyCompactPanelSurface(this);
                style.alignSelf = Align.FlexStart;
                style.marginTop = 4f;

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

                var eyebrow = UIToolkitStyleUtility.CreateEyebrowLabel("TOOL");
                eyebrow.style.marginBottom = 0f;
                eyebrow.style.marginRight = 8f;
                eyebrow.style.flexShrink = 0f;
                headerLeft.Add(eyebrow);
                headerLeft.Add(ToolNameLabel);

                _chevron = UIToolkitStyleUtility.CreateChevronButton(false);
                _chevron.clicked += ToggleExpanded;

                headerRow.Add(headerLeft);
                headerRow.Add(_chevron);
                Add(headerRow);

                // --- Detail container (collapsed by default) ---
                _detailContainer = new VisualElement();
                _detailContainer.style.display = DisplayStyle.None;
                _detailContainer.style.marginTop = 8f;
                _detailContainer.Add(CreateFieldBlock("Category", CategoryValueLabel));
                _detailContainer.Add(CreateFieldBlock("Purpose", PurposeValueLabel));
                _detailContainer.Add(CreateFieldBlock("Usage", UsageValueLabel));
                _detailContainer.Add(CreateFieldBlock("Safety", SafetyValueLabel));
                Add(_detailContainer);
            }

            public Label ToolNameLabel { get; } =
                UIToolkitStyleUtility.CreateCompactTitleLabel("Selected Tool");

            public Label CategoryValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label PurposeValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label UsageValueLabel { get; } =
                UIToolkitStyleUtility.CreateBodyLabel("—");

            public Label SafetyValueLabel { get; } =
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
