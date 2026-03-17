using OSE.App;
using OSE.Core;
using OSE.Runtime;
using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
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
            _view.ConfirmButton.clicked += HandleConfirmClicked;
            _view.HintButton.clicked += HandleHintClicked;
        }

        protected override void ApplyViewModel(StepPanelViewModel viewModel)
        {
            _view.StepLabel.text = viewModel.StepLabel;
            _view.TitleLabel.text = viewModel.Title;
            _view.InstructionLabel.text = viewModel.Instruction;
            _view.SetConfirmButtonVisible(viewModel.ShowConfirmButton);
            _view.SetConfirmEnabled(viewModel.ConfirmUnlocked);
            _view.SetHintButtonVisible(viewModel.ShowHintButton);
            _view.SetProgress(viewModel.ProgressRatio);
        }

        protected override void OnUnbind()
        {
            if (_view != null)
            {
                _view.ConfirmButton.clicked -= HandleConfirmClicked;
                _view.HintButton.clicked -= HandleHintClicked;
            }
            _view = null;
        }

        private void HandleConfirmClicked()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            var stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            if (session.ToolController != null)
            {
                // Check if this step requires a tool action to complete.
                // If it does, the Confirm button must NOT bypass it.
                if (session.ToolController.TryGetPrimaryActionSnapshot(
                        out ToolRuntimeController.ToolActionSnapshot snapshot)
                    && snapshot.IsConfigured && !snapshot.IsCompleted)
                {
                    ToolRuntimeController.ToolActionExecutionResult toolResult =
                        session.ToolController.TryExecutePrimaryAction();

                    // Block completion unless the tool action says the step is done.
                    if (!toolResult.ShouldCompleteStep)
                        return;
                }
            }

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        private void HandleHintClicked()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            session.AssemblyController?.StepController?.RequestHint();
        }

        private sealed class StepPanelView : VisualElement
        {
            public Label StepLabel { get; }
            public Label TitleLabel { get; }
            public Label InstructionLabel { get; }
            public Button ConfirmButton { get; }
            public Button HintButton { get; }

            private readonly VisualElement _progressTrack;
            private readonly VisualElement _progressFill;

            private static readonly Color ConfirmEnabledBg = new Color(0.2f, 0.7f, 0.4f, 1f);
            private static readonly Color ConfirmDisabledBg = new Color(0.25f, 0.3f, 0.35f, 0.7f);
            private static readonly Color ConfirmDisabledText = new Color(0.6f, 0.65f, 0.7f, 0.7f);

            public StepPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                StepLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Current Step");
                TitleLabel = UIToolkitStyleUtility.CreateTitleLabel("Awaiting Step Data");
                InstructionLabel = UIToolkitStyleUtility.CreateBodyLabel(
                    "Instruction text is provided by runtime presenters.");

                Add(StepLabel);

                // Progress bar
                _progressTrack = new VisualElement();
                _progressTrack.style.height = 4f;
                _progressTrack.style.backgroundColor = new Color(0.2f, 0.24f, 0.3f, 0.6f);
                _progressTrack.style.marginTop = 4f;
                _progressTrack.style.marginBottom = 6f;
                _progressTrack.style.borderTopLeftRadius = 2f;
                _progressTrack.style.borderTopRightRadius = 2f;
                _progressTrack.style.borderBottomLeftRadius = 2f;
                _progressTrack.style.borderBottomRightRadius = 2f;
                Add(_progressTrack);

                _progressFill = new VisualElement();
                _progressFill.style.height = 4f;
                _progressFill.style.backgroundColor = new Color(0.3f, 0.85f, 0.5f, 1f);
                _progressFill.style.borderTopLeftRadius = 2f;
                _progressFill.style.borderTopRightRadius = 2f;
                _progressFill.style.borderBottomLeftRadius = 2f;
                _progressFill.style.borderBottomRightRadius = 2f;
                _progressFill.style.width = Length.Percent(0f);
                _progressTrack.Add(_progressFill);

                Add(TitleLabel);
                Add(InstructionLabel);

                // Hint button (touch-friendly)
                HintButton = new Button();
                HintButton.text = "Request Hint";
                HintButton.style.height = 40f;
                HintButton.style.marginTop = 8f;
                HintButton.style.fontSize = 14f;
                HintButton.style.unityFontStyleAndWeight = FontStyle.Normal;
                HintButton.style.backgroundColor = new Color(0.15f, 0.25f, 0.4f, 0.9f);
                HintButton.style.color = new Color(0.42f, 0.82f, 1f, 1f);
                HintButton.style.borderTopLeftRadius = 6f;
                HintButton.style.borderTopRightRadius = 6f;
                HintButton.style.borderBottomLeftRadius = 6f;
                HintButton.style.borderBottomRightRadius = 6f;
                HintButton.style.borderTopWidth = 1f;
                HintButton.style.borderRightWidth = 1f;
                HintButton.style.borderBottomWidth = 1f;
                HintButton.style.borderLeftWidth = 1f;
                HintButton.style.borderTopColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                HintButton.style.borderRightColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                HintButton.style.borderBottomColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                HintButton.style.borderLeftColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                HintButton.style.display = DisplayStyle.None;
                Add(HintButton);

                // Confirm button (touch-friendly: 44px min height)
                ConfirmButton = new Button();
                ConfirmButton.text = "Continue";
                ConfirmButton.style.height = 44f;
                ConfirmButton.style.marginTop = 10f;
                ConfirmButton.style.fontSize = 16f;
                ConfirmButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                ConfirmButton.style.backgroundColor = ConfirmEnabledBg;
                ConfirmButton.style.color = Color.white;
                ConfirmButton.style.borderTopLeftRadius = 6f;
                ConfirmButton.style.borderTopRightRadius = 6f;
                ConfirmButton.style.borderBottomLeftRadius = 6f;
                ConfirmButton.style.borderBottomRightRadius = 6f;
                ConfirmButton.style.display = DisplayStyle.None;
                Add(ConfirmButton);
            }

            public void SetConfirmButtonVisible(bool visible)
            {
                ConfirmButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetConfirmEnabled(bool enabled)
            {
                ConfirmButton.SetEnabled(enabled);
                ConfirmButton.style.backgroundColor = enabled ? ConfirmEnabledBg : ConfirmDisabledBg;
                ConfirmButton.style.color = enabled ? Color.white : ConfirmDisabledText;
            }

            public void SetHintButtonVisible(bool visible)
            {
                HintButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetProgress(float ratio)
            {
                float pct = Mathf.Clamp01(ratio) * 100f;
                _progressFill.style.width = Length.Percent(pct);
            }
        }
    }
}
