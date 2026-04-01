using OSE.App;
using OSE.Content;
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
            _view.ContextActionButton.clicked += HandleContextActionClicked;
            _view.ConfirmButton.clicked += HandleConfirmClicked;
            _view.HintButton.clicked += HandleHintClicked;
            _view.BackButton.clicked += HandleBackClicked;
            _view.ForwardButton.clicked += HandleForwardClicked;
            _view.SkipToStartButton.clicked += HandleSkipToStartClicked;
            _view.SkipToEndButton.clicked += HandleSkipToEndClicked;
            _view.SectionsButton.clicked += HandleSectionsClicked;
        }

        protected override void ApplyViewModel(StepPanelViewModel viewModel)
        {
            _view.StepLabel.text = viewModel.StepLabel;
            _view.TitleLabel.text = viewModel.Title;
            _view.InstructionLabel.text = viewModel.Instruction;
            _view.SetAssemblyName(viewModel.AssemblyName);

            // Show sections button only when package has multiple assemblies
            bool showSections = false;
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var pickerSession) && pickerSession.Package?.machine != null)
            {
                var entryIds = pickerSession.Package.machine.entryAssemblyIds;
                showSections = (entryIds != null && entryIds.Length > 1)
                    || (entryIds == null && pickerSession.Package.GetAssemblies().Length > 1);
            }
            _view.SetSectionsButtonVisible(showSections);
            _view.SetContextActionButtonVisible(viewModel.ShowContextActionButton);
            _view.SetContextActionLabel(viewModel.ContextActionLabel);
            _view.SetContextActionEnabled(viewModel.ContextActionEnabled);
            _view.SetConfirmButtonVisible(viewModel.ShowConfirmButton);
            _view.SetConfirmEnabled(viewModel.ConfirmUnlocked);
            _view.SetHintButtonVisible(viewModel.ShowHintButton);
            _view.SetProgress(viewModel.ProgressRatio);
            _view.SetGlobalProgress(viewModel.GlobalProgressRatio, viewModel.GlobalProgressLabel);

            // Update navigation button states
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session))
            {
                _view.SetSkipToStartEnabled(session.CanStepBack);
                _view.SetBackEnabled(session.CanStepBack);
                _view.SetForwardEnabled(session.CanStepForward);
                _view.SetSkipToEndEnabled(session.CanStepForward);
            }
            else
            {
                _view.SetSkipToStartEnabled(false);
                _view.SetBackEnabled(false);
                _view.SetForwardEnabled(false);
                _view.SetSkipToEndEnabled(false);
            }
        }

        protected override void OnUnbind()
        {
            if (_view != null)
            {
                _view.ContextActionButton.clicked -= HandleContextActionClicked;
                _view.ConfirmButton.clicked -= HandleConfirmClicked;
                _view.HintButton.clicked -= HandleHintClicked;
                _view.BackButton.clicked -= HandleBackClicked;
                _view.ForwardButton.clicked -= HandleForwardClicked;
                _view.SkipToStartButton.clicked -= HandleSkipToStartClicked;
                _view.SkipToEndButton.clicked -= HandleSkipToEndClicked;
                _view.SectionsButton.clicked -= HandleSectionsClicked;
            }
            _view = null;
        }

        private void HandleContextActionClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.CurrentStepDefinition;
            if (stepController == null ||
                !stepController.HasActiveStep ||
                step == null ||
                !step.IsPlacement ||
                !step.RequiresSubassemblyPlacement ||
                step.targetIds == null ||
                step.targetIds.Length != 1)
            {
                return;
            }

            if (!ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) ||
                subassemblyController == null ||
                !subassemblyController.IsSubassemblyReady(step.requiredSubassemblyId))
            {
                return;
            }

            string targetId = step.targetIds[0];
            if (!subassemblyController.TryApplyPlacement(step.requiredSubassemblyId, targetId))
            {
                OseLog.Warn($"[StepPanel] Guided stack placement failed for subassembly '{step.requiredSubassemblyId}' on target '{targetId}'.");
                return;
            }

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        private void HandleConfirmClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
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
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            session.AssemblyController?.StepController?.RequestHint();
        }

        private void HandleBackClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            session.StepBack();
        }

        private void HandleForwardClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            session.StepForward();
        }

        private void HandleSkipToStartClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            bool result = session.NavigateToGlobalStep(0);
            OseLog.Info($"[StepPanel] SkipToStart clicked — NavigateToGlobalStep(0) returned {result}");
        }

        private void HandleSkipToEndClicked()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            bool result = session.NavigateToLastStep();
            OseLog.Info($"[StepPanel] SkipToEnd clicked — NavigateToLastStep returned {result}");
        }

        private void HandleSectionsClicked()
        {
            RuntimeEventBus.Publish(new AssemblyPickerRequested());
        }

        private sealed class StepPanelView : VisualElement
        {
            public Label AssemblyLabel { get; }
            public Button SectionsButton { get; }
            public Label StepLabel { get; }
            public Label TitleLabel { get; }
            public Label InstructionLabel { get; }
            public Button ContextActionButton { get; }
            public Button ConfirmButton { get; }
            public Button HintButton { get; }
            public Button BackButton { get; }
            public Button ForwardButton { get; }
            public Button SkipToStartButton { get; }
            public Button SkipToEndButton { get; }

            private readonly VisualElement _progressTrack;
            private readonly VisualElement _progressFill;
            private readonly VisualElement _globalProgressTrack;
            private readonly VisualElement _globalProgressFill;
            private readonly Label _globalProgressLabel;

            private static readonly Color ContextEnabledBg = new Color(0.46f, 0.34f, 0.12f, 0.96f);
            private static readonly Color ContextDisabledBg = new Color(0.28f, 0.24f, 0.18f, 0.7f);
            private static readonly Color ContextEnabledText = new Color(1f, 0.94f, 0.82f, 1f);
            private static readonly Color ContextDisabledText = new Color(0.72f, 0.68f, 0.62f, 0.78f);
            private static readonly Color ConfirmEnabledBg = new Color(0.2f, 0.7f, 0.4f, 1f);
            private static readonly Color ConfirmDisabledBg = new Color(0.25f, 0.3f, 0.35f, 0.7f);
            private static readonly Color ConfirmDisabledText = new Color(0.6f, 0.65f, 0.7f, 0.7f);
            private static readonly Color NavBtnBg = new Color(0.15f, 0.2f, 0.28f, 0.8f);
            private static readonly Color NavBtnBorder = new Color(0.42f, 0.82f, 1f, 0.3f);
            private static readonly Color NavBtnText = new Color(0.42f, 0.82f, 1f, 1f);

            public StepPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;

                // Assembly name row (eyebrow above nav row) with optional Sections button
                var assemblyRow = new VisualElement();
                assemblyRow.style.flexDirection = FlexDirection.Row;
                assemblyRow.style.alignItems = Align.Center;
                assemblyRow.style.justifyContent = Justify.SpaceBetween;
                assemblyRow.style.marginBottom = 2f;
                assemblyRow.style.display = DisplayStyle.None;

                AssemblyLabel = new Label();
                AssemblyLabel.style.fontSize = 11f;
                AssemblyLabel.style.color = new Color(0.65f, 0.78f, 0.95f, 0.85f);
                AssemblyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                AssemblyLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                AssemblyLabel.style.flexGrow = 1f;
                assemblyRow.Add(AssemblyLabel);

                SectionsButton = new Button();
                SectionsButton.text = "\u2630"; // ☰ hamburger menu
                SectionsButton.style.width = 28f;
                SectionsButton.style.height = 22f;
                SectionsButton.style.fontSize = 14f;
                SectionsButton.style.backgroundColor = new Color(0.15f, 0.2f, 0.28f, 0.8f);
                SectionsButton.style.color = new Color(0.6f, 0.75f, 0.95f, 0.9f);
                SectionsButton.style.borderTopLeftRadius = 4f;
                SectionsButton.style.borderTopRightRadius = 4f;
                SectionsButton.style.borderBottomLeftRadius = 4f;
                SectionsButton.style.borderBottomRightRadius = 4f;
                SectionsButton.style.borderTopWidth = 1f;
                SectionsButton.style.borderBottomWidth = 1f;
                SectionsButton.style.borderLeftWidth = 1f;
                SectionsButton.style.borderRightWidth = 1f;
                SectionsButton.style.borderTopColor = new Color(0.3f, 0.4f, 0.6f, 0.3f);
                SectionsButton.style.borderBottomColor = new Color(0.3f, 0.4f, 0.6f, 0.3f);
                SectionsButton.style.borderLeftColor = new Color(0.3f, 0.4f, 0.6f, 0.3f);
                SectionsButton.style.borderRightColor = new Color(0.3f, 0.4f, 0.6f, 0.3f);
                SectionsButton.style.paddingLeft = 0f;
                SectionsButton.style.paddingRight = 0f;
                SectionsButton.style.paddingTop = 0f;
                SectionsButton.style.paddingBottom = 0f;
                SectionsButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                SectionsButton.style.display = DisplayStyle.None;
                assemblyRow.Add(SectionsButton);

                Add(assemblyRow);

                StepLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Current Step");

                // Navigation row: [Back] Step Label [Forward]
                var navRow = new VisualElement();
                navRow.style.flexDirection = FlexDirection.Row;
                navRow.style.alignItems = Align.Center;
                navRow.style.justifyContent = Justify.SpaceBetween;
                navRow.style.marginBottom = 2f;

                SkipToStartButton = CreateNavButton("|\u25C0"); // |◀
                SkipToStartButton.style.marginRight = 4f;
                BackButton = CreateNavButton("\u25C0"); // ◀
                ForwardButton = CreateNavButton("\u25B6"); // ▶
                SkipToEndButton = CreateNavButton("\u25B6|"); // ▶|
                SkipToEndButton.style.marginLeft = 4f;

                StepLabel.style.flexGrow = 1f;
                StepLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                StepLabel.style.marginLeft = 6f;
                StepLabel.style.marginRight = 6f;

                navRow.Add(SkipToStartButton);
                navRow.Add(BackButton);
                navRow.Add(StepLabel);
                navRow.Add(ForwardButton);
                navRow.Add(SkipToEndButton);
                Add(navRow);

                TitleLabel = UIToolkitStyleUtility.CreateTitleLabel("Awaiting Step Data");
                InstructionLabel = UIToolkitStyleUtility.CreateBodyLabel(
                    "Instruction text is provided by runtime presenters.");

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

                // Global progress bar (thin, blue-tinted)
                _globalProgressTrack = new VisualElement();
                _globalProgressTrack.style.height = 2f;
                _globalProgressTrack.style.backgroundColor = new Color(0.18f, 0.22f, 0.3f, 0.5f);
                _globalProgressTrack.style.marginBottom = 4f;
                _globalProgressTrack.style.borderTopLeftRadius = 1f;
                _globalProgressTrack.style.borderTopRightRadius = 1f;
                _globalProgressTrack.style.borderBottomLeftRadius = 1f;
                _globalProgressTrack.style.borderBottomRightRadius = 1f;
                _globalProgressTrack.style.display = DisplayStyle.None;
                Add(_globalProgressTrack);

                _globalProgressFill = new VisualElement();
                _globalProgressFill.style.height = 2f;
                _globalProgressFill.style.backgroundColor = new Color(0.42f, 0.68f, 1f, 0.8f);
                _globalProgressFill.style.borderTopLeftRadius = 1f;
                _globalProgressFill.style.borderTopRightRadius = 1f;
                _globalProgressFill.style.borderBottomLeftRadius = 1f;
                _globalProgressFill.style.borderBottomRightRadius = 1f;
                _globalProgressFill.style.width = Length.Percent(0f);
                _globalProgressTrack.Add(_globalProgressFill);

                _globalProgressLabel = new Label();
                _globalProgressLabel.style.fontSize = 10f;
                _globalProgressLabel.style.color = new Color(0.55f, 0.7f, 0.9f, 0.7f);
                _globalProgressLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                _globalProgressLabel.style.marginBottom = 4f;
                _globalProgressLabel.style.display = DisplayStyle.None;
                Add(_globalProgressLabel);

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

                ContextActionButton = new Button();
                ContextActionButton.text = "Place Assembly";
                ContextActionButton.style.height = 44f;
                ContextActionButton.style.marginTop = 10f;
                ContextActionButton.style.fontSize = 15f;
                ContextActionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                ContextActionButton.style.borderTopLeftRadius = 6f;
                ContextActionButton.style.borderTopRightRadius = 6f;
                ContextActionButton.style.borderBottomLeftRadius = 6f;
                ContextActionButton.style.borderBottomRightRadius = 6f;
                ContextActionButton.style.display = DisplayStyle.None;
                Add(ContextActionButton);

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

            public void SetContextActionButtonVisible(bool visible)
            {
                ContextActionButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetContextActionLabel(string label)
            {
                ContextActionButton.text = string.IsNullOrWhiteSpace(label)
                    ? "Place Assembly"
                    : label.Trim();
            }

            public void SetContextActionEnabled(bool enabled)
            {
                ContextActionButton.SetEnabled(enabled);
                ContextActionButton.style.backgroundColor = enabled ? ContextEnabledBg : ContextDisabledBg;
                ContextActionButton.style.color = enabled ? ContextEnabledText : ContextDisabledText;
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

            public void SetAssemblyName(string name)
            {
                bool hasName = !string.IsNullOrWhiteSpace(name);
                AssemblyLabel.parent.style.display = hasName ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasName)
                    AssemblyLabel.text = name;
            }

            public void SetSectionsButtonVisible(bool visible)
            {
                SectionsButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetGlobalProgress(float ratio, string label)
            {
                bool hasGlobal = !string.IsNullOrEmpty(label);
                _globalProgressTrack.style.display = hasGlobal ? DisplayStyle.Flex : DisplayStyle.None;
                _globalProgressLabel.style.display = hasGlobal ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasGlobal)
                {
                    float pct = Mathf.Clamp01(ratio) * 100f;
                    _globalProgressFill.style.width = Length.Percent(pct);
                    _globalProgressLabel.text = label;
                }
            }

            public void SetBackEnabled(bool enabled)
            {
                BackButton.SetEnabled(enabled);
                BackButton.style.opacity = enabled ? 1f : 0.3f;
            }

            public void SetForwardEnabled(bool enabled)
            {
                ForwardButton.SetEnabled(enabled);
                ForwardButton.style.opacity = enabled ? 1f : 0.3f;
            }

            public void SetSkipToStartEnabled(bool enabled)
            {
                SkipToStartButton.SetEnabled(enabled);
                SkipToStartButton.style.opacity = enabled ? 1f : 0.3f;
            }

            public void SetSkipToEndEnabled(bool enabled)
            {
                SkipToEndButton.SetEnabled(enabled);
                SkipToEndButton.style.opacity = enabled ? 1f : 0.3f;
            }

            private static Button CreateNavButton(string text)
            {
                var btn = new Button { text = text };
                btn.style.width = 36f;
                btn.style.height = 36f;
                btn.style.fontSize = 14f;
                btn.style.backgroundColor = NavBtnBg;
                btn.style.color = NavBtnText;
                btn.style.borderTopLeftRadius = 6f;
                btn.style.borderTopRightRadius = 6f;
                btn.style.borderBottomLeftRadius = 6f;
                btn.style.borderBottomRightRadius = 6f;
                btn.style.borderTopWidth = 1f;
                btn.style.borderRightWidth = 1f;
                btn.style.borderBottomWidth = 1f;
                btn.style.borderLeftWidth = 1f;
                btn.style.borderTopColor = NavBtnBorder;
                btn.style.borderRightColor = NavBtnBorder;
                btn.style.borderBottomColor = NavBtnBorder;
                btn.style.borderLeftColor = NavBtnBorder;
                btn.style.paddingLeft = 0f;
                btn.style.paddingRight = 0f;
                btn.style.paddingTop = 0f;
                btn.style.paddingBottom = 0f;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;
                return btn;
            }
        }
    }
}
