using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class HintToastPanelController : PanelControllerBase<HintToastViewModel>
    {
        private HintToastView _view;

        protected override string PanelName => "ose-hint-toast";

        protected override VisualElement CreateView() => new HintToastView();

        protected override void CacheView(VisualElement root)
        {
            _view = (HintToastView)root;
        }

        protected override void ApplyViewModel(HintToastViewModel viewModel)
        {
            _view.EyebrowLabel.text = viewModel.Eyebrow;
            _view.TitleLabel.text = viewModel.Title;
            _view.MessageLabel.text = viewModel.Message;
        }

        protected override void OnUnbind()
        {
            _view = null;
        }

        private sealed class HintToastView : VisualElement
        {
            public Label EyebrowLabel { get; }
            public Label TitleLabel { get; }
            public Label MessageLabel { get; }

            public HintToastView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;
                style.backgroundColor = new Color(0.08f, 0.12f, 0.2f, 0.94f);

                var accent = new VisualElement();
                accent.style.height = 3f;
                accent.style.backgroundColor = new Color(0.42f, 0.82f, 1f, 1f);
                accent.style.marginBottom = 10f;
                Add(accent);

                EyebrowLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Hint");
                TitleLabel = UIToolkitStyleUtility.CreateTitleLabel("Guidance");
                TitleLabel.style.fontSize = 18f;
                MessageLabel = UIToolkitStyleUtility.CreateBodyLabel("Follow the guidance to continue.");

                Add(EyebrowLabel);
                Add(TitleLabel);
                Add(MessageLabel);
            }
        }
    }
}
