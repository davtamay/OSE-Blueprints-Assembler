using System.Collections.Generic;
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

        // Cached on first successful lookup; never re-fetches null once set.
        private IMachineSessionController _session;
        private IMachineSessionController Session
        {
            get
            {
                if (_session == null)
                    ServiceRegistry.TryGet<IMachineSessionController>(out _session);
                return _session;
            }
        }

        protected override string PanelName => "ose-step-panel";

        protected override VisualElement CreateView() => new StepPanelView();

        // Tracks the last global step index pushed to the view — prevents feedback loops
        // when the input field value changes programmatically during ApplyViewModel.
        private int _lastAppliedGlobalIndex = -1;
        private int _lastAppliedGlobalTotal;

        // Step IDs the trainee has already seen in this session. First visit auto-expands
        // Details; revisits stay collapsed. Marked seen on Continue click.
        private readonly HashSet<string> _seenStepIds = new HashSet<string>();
        private string _lastAppliedStepId;

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
            _view.ReadAloudButton.clicked += HandleReadAloudClicked;
            _view.StepNumberField.RegisterCallback<WheelEvent>(HandleStepScroll);
            _view.StepNumberField.RegisterValueChangedCallback(HandleStepTextChanged);
            _view.InstallClickAwayDismiss();
        }

        protected override void ApplyViewModel(StepPanelViewModel viewModel)
        {
            _view.StepLabel.text = viewModel.StepLabel;

            // Update step number display — 1-based (user sees "Step 1" for index 0)
            int displayStep = viewModel.GlobalStepIndex + 1;
            int displayTotal = viewModel.GlobalTotalSteps;
            _lastAppliedGlobalIndex = viewModel.GlobalStepIndex;
            _lastAppliedGlobalTotal = displayTotal;
            _view.StepNumberField.SetValueWithoutNotify(displayStep.ToString());
            _view.StepSuffixLabel.text = $"of {displayTotal}";
            _view.SetNavChipText(displayStep, displayTotal);
            _view.TitleLabel.text = viewModel.Title;
            _view.SetToolChip(viewModel.ToolDisplayName);
            _view.SetInstructionDetails(viewModel.InstructionDetails ?? viewModel.Instruction);
            _view.SetWhyItMatters(viewModel.WhyItMatters);

            // First-visit policy: auto-expand Details for steps the trainee hasn't seen yet.
            string currentStepId = viewModel.StepId;
            bool isFirstVisit = !string.IsNullOrEmpty(currentStepId) && !_seenStepIds.Contains(currentStepId);
            // Only re-apply expansion when the step actually changed; preserves user toggles
            // on re-renders triggered by progress / gate updates.
            if (currentStepId != _lastAppliedStepId)
            {
                _view.SetDetailsExpanded(isFirstVisit);
                _lastAppliedStepId = currentStepId;
            }

            _view.SetAssemblyName(viewModel.AssemblyName);

            // Show sections button only when package has multiple assemblies
            bool showSections = false;
            if (Session?.Package?.machine != null)
            {
                var entryIds = Session.Package.machine.entryAssemblyIds;
                showSections = (entryIds != null && entryIds.Length > 1)
                    || (entryIds == null && Session.Package.GetAssemblies().Length > 1);
            }
            _view.SetSectionsButtonVisible(showSections);
            _view.SetContextActionButtonVisible(viewModel.ShowContextActionButton);
            _view.SetContextActionLabel(viewModel.ContextActionLabel);
            _view.SetContextActionEnabled(viewModel.ContextActionEnabled);
            _view.SetConfirmButtonVisible(viewModel.ShowConfirmButton);
            _view.SetConfirmEnabled(viewModel.ConfirmUnlocked);
            _view.SetHintButtonVisible(viewModel.ShowHintButton);
            _view.SetMicroProgress(viewModel.GlobalProgressRatio, displayTotal > 0);

            // Update navigation button states
            bool canBack = Session?.CanStepBack ?? false;
            bool canForward = Session?.CanStepForward ?? false;
            _view.SetSkipToStartEnabled(canBack);
            _view.SetBackEnabled(canBack);
            _view.SetForwardEnabled(canForward);
            _view.SetSkipToEndEnabled(canForward);
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
                _view.ReadAloudButton.clicked -= HandleReadAloudClicked;
                _view.StepNumberField.UnregisterCallback<WheelEvent>(HandleStepScroll);
                _view.StepNumberField.UnregisterValueChangedCallback(HandleStepTextChanged);
            }
            _view = null;
            _lastAppliedStepId = null;
        }

        private void HandleStepScroll(WheelEvent evt)
        {
            if (Session == null) return;

            int delta = evt.delta.y > 0 ? -1 : 1;
            int targetGlobal = _lastAppliedGlobalIndex + delta;
            if (targetGlobal < 0) targetGlobal = 0;
            if (_lastAppliedGlobalTotal > 0 && targetGlobal >= _lastAppliedGlobalTotal)
                targetGlobal = _lastAppliedGlobalTotal - 1;

            if (targetGlobal == _lastAppliedGlobalIndex) return;

            Session.NavigateToGlobalStep(targetGlobal);
            evt.StopPropagation();
        }

        private void HandleStepTextChanged(ChangeEvent<string> evt)
        {
            if (Session == null) return;
            if (!int.TryParse(evt.newValue, out int typed)) return;

            int targetGlobal = typed - 1; // 1-based display → 0-based index
            if (targetGlobal < 0) targetGlobal = 0;
            if (_lastAppliedGlobalTotal > 0 && targetGlobal >= _lastAppliedGlobalTotal)
                targetGlobal = _lastAppliedGlobalTotal - 1;

            if (targetGlobal == _lastAppliedGlobalIndex) return;

            Session.NavigateToGlobalStep(targetGlobal);
        }

        private void HandleContextActionClicked()
        {
            if (Session == null)
                return;

            StepController stepController = Session.AssemblyController?.StepController;
            StepDefinition step = stepController?.CurrentStepDefinition;
            if (stepController == null ||
                !stepController.HasActiveStep ||
                step == null ||
                !step.IsPlacement ||
                !step.RequiresPartGroupPlacement ||
                step.targetIds == null ||
                step.targetIds.Length != 1)
            {
                return;
            }

            if (!ServiceRegistry.TryGet<IPartGroupPlacementService>(out var partGroupController) ||
                partGroupController == null ||
                !partGroupController.IsPartGroupReady(step.requiredPartGroupId))
            {
                return;
            }

            string targetId = step.targetIds[0];
            if (!partGroupController.TryApplyPlacement(step.requiredPartGroupId, targetId))
            {
                OseLog.Warn($"[StepPanel] Guided stack placement failed for partGroup '{step.requiredPartGroupId}' on target '{targetId}'.");
                return;
            }

            stepController.CompleteStep(Session.GetElapsedSeconds());
        }

        private void HandleConfirmClicked()
        {
            if (Session == null)
                return;

            var stepController = Session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            // Mark this step as seen the moment the trainee commits to advancing.
            // Doing it here (rather than on render) means a quick scroll-through still
            // surfaces details on first read.
            if (!string.IsNullOrEmpty(_lastAppliedStepId))
                _seenStepIds.Add(_lastAppliedStepId);

            if (Session.ToolController != null)
            {
                // Check if this step requires a tool action to complete.
                // If it does, the Confirm button must NOT bypass it.
                if (Session.ToolController.TryGetPrimaryActionSnapshot(
                        out ToolActionSnapshot snapshot)
                    && snapshot.IsConfigured && !snapshot.IsCompleted)
                {
                    ToolActionExecutionResult toolResult =
                        Session.ToolController.TryExecutePrimaryAction();

                    // Block completion unless the tool action says the step is done.
                    if (!toolResult.ShouldCompleteStep)
                        return;
                }
            }

            stepController.CompleteStep(Session.GetElapsedSeconds());
        }

        private void HandleHintClicked()
        {
            Session?.AssemblyController?.StepController?.RequestHint();
        }

        private void HandleBackClicked()
        {
            // Don't close the popup — trainees often step through several at a time.
            // Click-away or chip-tap dismisses when they're done.
            Session?.StepBack();
        }

        private void HandleForwardClicked()
        {
            Session?.StepForward();
        }

        private void HandleSkipToStartClicked()
        {
            _view?.CloseNavOverlay();
            if (Session == null) return;
            bool result = Session.NavigateToGlobalStep(0);
            OseLog.Info($"[StepPanel] SkipToStart clicked — NavigateToGlobalStep(0) returned {result}");
        }

        private void HandleSkipToEndClicked()
        {
            _view?.CloseNavOverlay();
            if (Session == null) return;
            bool result = Session.NavigateToLastStep();
            OseLog.Info($"[StepPanel] SkipToEnd clicked — NavigateToLastStep returned {result}");
        }

        private void HandleSectionsClicked()
        {
            RuntimeEventBus.Publish(new AssemblyPickerRequested());
        }

        private void HandleReadAloudClicked()
        {
            // OS-native TTS hook — implementations differ per platform. Until a runtime
            // service is wired, the button is informational only; it stays in place so
            // the UI affordance ships and TTS can be added behind it without layout churn.
            RuntimeEventBus.Publish(new StepReadAloudRequested(_lastAppliedStepId));
        }

        private sealed class StepPanelView : VisualElement
        {
            // Buttons exposed to the controller
            public Label AssemblyLabel { get; }
            public Button SectionsButton { get; }
            public Label StepLabel { get; }
            public TextField StepNumberField { get; }
            public Label StepSuffixLabel { get; }
            public Label TitleLabel { get; }
            public Button ContextActionButton { get; }
            public Button ConfirmButton { get; }
            public Button HintButton { get; }
            public Button BackButton { get; }
            public Button ForwardButton { get; }
            public Button SkipToStartButton { get; }
            public Button SkipToEndButton { get; }
            public Button ReadAloudButton { get; }

            // Internals
            private readonly Button _navChipButton;
            private readonly VisualElement _navOverlay;
            private readonly VisualElement _microProgressTrack;
            private readonly VisualElement _microProgressFill;
            private readonly VisualElement _toolChip;
            private readonly Label _toolChipLabel;
            private readonly Disclosure _detailsDisclosure;
            private readonly Disclosure _whyDisclosure;

            // Palette (carried over from prior version for consistency)
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
            private static readonly Color MetaText = new Color(0.65f, 0.78f, 0.95f, 0.85f);
            private static readonly Color SubtleText = new Color(0.55f, 0.7f, 0.9f, 0.7f);
            private static readonly Color ToolChipBg = new Color(0.20f, 0.14f, 0.06f, 0.92f);
            private static readonly Color ToolChipBorder = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            private static readonly Color ToolChipText = new Color(1f, 0.95f, 0.75f, 1f);

            public StepPanelView()
            {
                UIToolkitStyleUtility.ApplyPanelSurface(this);
                style.alignSelf = Align.FlexStart;
                style.maxWidth = 380f;
                style.minWidth = 320f;

                // ── Row 1: assembly name (left) · Sections (small, right, only if multi-asm) ──
                var assemblyRow = new VisualElement();
                assemblyRow.style.flexDirection = FlexDirection.Row;
                assemblyRow.style.alignItems = Align.Center;
                assemblyRow.style.marginBottom = 4f;

                AssemblyLabel = new Label();
                AssemblyLabel.style.fontSize = 11f;
                AssemblyLabel.style.color = MetaText;
                AssemblyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                AssemblyLabel.style.flexGrow = 1f;
                AssemblyLabel.style.flexShrink = 1f;
                AssemblyLabel.style.overflow = Overflow.Hidden;
                AssemblyLabel.style.textOverflow = TextOverflow.Ellipsis;
                assemblyRow.Add(AssemblyLabel);

                SectionsButton = new Button { text = "Sections" };
                SectionsButton.tooltip = "Switch to a different section";
                SectionsButton.style.height = 18f;
                SectionsButton.style.fontSize = 10f;
                SectionsButton.style.paddingLeft = 8f;
                SectionsButton.style.paddingRight = 8f;
                SectionsButton.style.paddingTop = 0f;
                SectionsButton.style.paddingBottom = 0f;
                SectionsButton.style.marginLeft = 6f;
                SectionsButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                SectionsButton.style.color = MetaText;
                SectionsButton.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                SectionsButton.style.borderTopLeftRadius = 4f;
                SectionsButton.style.borderTopRightRadius = 4f;
                SectionsButton.style.borderBottomLeftRadius = 4f;
                SectionsButton.style.borderBottomRightRadius = 4f;
                SectionsButton.style.borderTopWidth = 1f;
                SectionsButton.style.borderRightWidth = 1f;
                SectionsButton.style.borderBottomWidth = 1f;
                SectionsButton.style.borderLeftWidth = 1f;
                SectionsButton.style.borderTopColor = new Color(0.32f, 0.42f, 0.58f, 0.4f);
                SectionsButton.style.borderRightColor = new Color(0.32f, 0.42f, 0.58f, 0.4f);
                SectionsButton.style.borderBottomColor = new Color(0.32f, 0.42f, 0.58f, 0.4f);
                SectionsButton.style.borderLeftColor = new Color(0.32f, 0.42f, 0.58f, 0.4f);
                SectionsButton.style.display = DisplayStyle.None;
                assemblyRow.Add(SectionsButton);

                Add(assemblyRow);

                // ── Row 2: locator — [Step 85 of 305 ▼]   ▰▰▰▱▱▱▱▱  ──
                // Single chip is the affordance. Tap opens the popup that groups ALL nav
                // controls together (|◀ ◀ Step [#] of N ▶ ▶|) — keeps related controls
                // visually unified and matches the original arrangement.
                var locatorRow = new VisualElement();
                locatorRow.style.flexDirection = FlexDirection.Row;
                locatorRow.style.alignItems = Align.Center;
                locatorRow.style.marginBottom = 8f;

                _navChipButton = new Button { text = "Step 1 of 1  ▼" }; // ▼ U+25BC
                _navChipButton.tooltip = "Tap to navigate steps";
                _navChipButton.style.fontSize = 11f;
                _navChipButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                _navChipButton.style.height = 24f;
                _navChipButton.style.paddingLeft = 12f;
                _navChipButton.style.paddingRight = 12f;
                _navChipButton.style.paddingTop = 0f;
                _navChipButton.style.paddingBottom = 0f;
                _navChipButton.style.marginLeft = 0f;
                _navChipButton.style.marginRight = 10f;
                _navChipButton.style.color = NavBtnText;
                _navChipButton.style.backgroundColor = NavBtnBg;
                _navChipButton.style.borderTopLeftRadius = 12f;
                _navChipButton.style.borderTopRightRadius = 12f;
                _navChipButton.style.borderBottomLeftRadius = 12f;
                _navChipButton.style.borderBottomRightRadius = 12f;
                _navChipButton.style.borderTopWidth = 1f;
                _navChipButton.style.borderRightWidth = 1f;
                _navChipButton.style.borderBottomWidth = 1f;
                _navChipButton.style.borderLeftWidth = 1f;
                _navChipButton.style.borderTopColor = NavBtnBorder;
                _navChipButton.style.borderRightColor = NavBtnBorder;
                _navChipButton.style.borderBottomColor = NavBtnBorder;
                _navChipButton.style.borderLeftColor = NavBtnBorder;
                _navChipButton.clicked += ToggleNavOverlay;
                locatorRow.Add(_navChipButton);

                // Progress bar fills the remaining space.
                _microProgressTrack = new VisualElement();
                _microProgressTrack.style.height = 4f;
                _microProgressTrack.style.flexGrow = 1f;
                _microProgressTrack.style.backgroundColor = new Color(0.18f, 0.22f, 0.3f, 0.6f);
                _microProgressTrack.style.borderTopLeftRadius = 2f;
                _microProgressTrack.style.borderTopRightRadius = 2f;
                _microProgressTrack.style.borderBottomLeftRadius = 2f;
                _microProgressTrack.style.borderBottomRightRadius = 2f;
                locatorRow.Add(_microProgressTrack);

                _microProgressFill = new VisualElement();
                _microProgressFill.style.height = 4f;
                _microProgressFill.style.backgroundColor = new Color(0.30f, 0.85f, 0.55f, 0.95f);
                _microProgressFill.style.borderTopLeftRadius = 2f;
                _microProgressFill.style.borderTopRightRadius = 2f;
                _microProgressFill.style.borderBottomLeftRadius = 2f;
                _microProgressFill.style.borderBottomRightRadius = 2f;
                _microProgressFill.style.width = Length.Percent(0f);
                _microProgressTrack.Add(_microProgressFill);

                Add(locatorRow);

                // 1px divider — separates meta region from instructional content.
                var divider = new VisualElement();
                divider.style.height = 1f;
                divider.style.marginBottom = 10f;
                divider.style.backgroundColor = new Color(0.32f, 0.42f, 0.58f, 0.32f);
                Add(divider);

                // ── Hero block: imperative title with left accent bar ──
                var heroContainer = new VisualElement();
                heroContainer.style.flexDirection = FlexDirection.Row;
                heroContainer.style.alignItems = Align.FlexStart;
                heroContainer.style.marginBottom = 8f;

                var accentBar = new VisualElement();
                accentBar.style.width = 3f;
                accentBar.style.minHeight = 28f;
                accentBar.style.backgroundColor = new Color(0.30f, 0.85f, 0.55f, 0.95f);
                accentBar.style.marginRight = 10f;
                accentBar.style.borderTopLeftRadius = 1.5f;
                accentBar.style.borderTopRightRadius = 1.5f;
                accentBar.style.borderBottomLeftRadius = 1.5f;
                accentBar.style.borderBottomRightRadius = 1.5f;
                heroContainer.Add(accentBar);

                TitleLabel = new Label("Awaiting Step Data");
                TitleLabel.style.fontSize = 20f;
                TitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                TitleLabel.style.color = Color.white;
                TitleLabel.style.whiteSpace = WhiteSpace.Normal;
                TitleLabel.style.flexGrow = 1f;
                TitleLabel.style.flexShrink = 1f;
                heroContainer.Add(TitleLabel);

                Add(heroContainer);

                // Tool chip row — indented to align with hero title text (past the accent bar).
                // Listen does NOT live here anymore; it sits on the Details disclosure header so
                // both content-access affordances (read / hear) cohabit a single row.
                var heroRow = new VisualElement();
                heroRow.style.flexDirection = FlexDirection.Row;
                heroRow.style.alignItems = Align.Center;
                heroRow.style.marginLeft = 13f;
                heroRow.style.marginBottom = 10f;

                _toolChip = new VisualElement();
                _toolChip.style.flexDirection = FlexDirection.Row;
                _toolChip.style.alignItems = Align.Center;
                _toolChip.style.paddingLeft = 10f;
                _toolChip.style.paddingRight = 10f;
                _toolChip.style.paddingTop = 3f;
                _toolChip.style.paddingBottom = 3f;
                _toolChip.style.marginRight = 8f;
                _toolChip.style.backgroundColor = ToolChipBg;
                _toolChip.style.borderTopLeftRadius = 10f;
                _toolChip.style.borderTopRightRadius = 10f;
                _toolChip.style.borderBottomLeftRadius = 10f;
                _toolChip.style.borderBottomRightRadius = 10f;
                _toolChip.style.borderTopWidth = 1f;
                _toolChip.style.borderRightWidth = 1f;
                _toolChip.style.borderBottomWidth = 1f;
                _toolChip.style.borderLeftWidth = 1f;
                _toolChip.style.borderTopColor = ToolChipBorder;
                _toolChip.style.borderRightColor = ToolChipBorder;
                _toolChip.style.borderBottomColor = ToolChipBorder;
                _toolChip.style.borderLeftColor = ToolChipBorder;
                _toolChip.style.display = DisplayStyle.None;

                _toolChipLabel = new Label();
                _toolChipLabel.style.fontSize = 10f;
                _toolChipLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _toolChipLabel.style.color = ToolChipText;
                _toolChipLabel.style.letterSpacing = 0.5f;
                _toolChip.Add(_toolChipLabel);
                heroRow.Add(_toolChip);

                Add(heroRow);

                // Listen — created here but parented into the Details disclosure header below,
                // so the two "consume content" affordances share a row.
                ReadAloudButton = new Button { text = "Listen" };
                ReadAloudButton.tooltip = "Read this step aloud";
                ReadAloudButton.style.height = 20f;
                ReadAloudButton.style.fontSize = 10f;
                ReadAloudButton.style.paddingLeft = 10f;
                ReadAloudButton.style.paddingRight = 10f;
                ReadAloudButton.style.paddingTop = 0f;
                ReadAloudButton.style.paddingBottom = 0f;
                ReadAloudButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                ReadAloudButton.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                ReadAloudButton.style.color = NavBtnText;
                ReadAloudButton.style.borderTopLeftRadius = 4f;
                ReadAloudButton.style.borderTopRightRadius = 4f;
                ReadAloudButton.style.borderBottomLeftRadius = 4f;
                ReadAloudButton.style.borderBottomRightRadius = 4f;
                ReadAloudButton.style.borderTopWidth = 1f;
                ReadAloudButton.style.borderRightWidth = 1f;
                ReadAloudButton.style.borderBottomWidth = 1f;
                ReadAloudButton.style.borderLeftWidth = 1f;
                ReadAloudButton.style.borderTopColor = NavBtnBorder;
                ReadAloudButton.style.borderRightColor = NavBtnBorder;
                ReadAloudButton.style.borderBottomColor = NavBtnBorder;
                ReadAloudButton.style.borderLeftColor = NavBtnBorder;

                // ── Disclosures: Details + Why it matters ──
                _detailsDisclosure = new Disclosure(
                    "Details",
                    bodyFontSize: 13f,
                    bodyColor: new Color(0.92f, 0.95f, 0.98f, 1f));
                _detailsDisclosure.HeaderActionSlot.Add(ReadAloudButton);
                Add(_detailsDisclosure);

                _whyDisclosure = new Disclosure(
                    "Why it matters",
                    bodyFontSize: 12f,
                    bodyColor: SubtleText,
                    accentBg: new Color(0.16f, 0.20f, 0.28f, 0.55f));
                _whyDisclosure.style.display = DisplayStyle.None;
                _whyDisclosure.style.marginTop = 4f;
                Add(_whyDisclosure);

                // Legacy StepLabel kept (hidden) so any external readers do not NRE.
                StepLabel = new Label();
                StepLabel.style.display = DisplayStyle.None;
                Add(StepLabel);

                // ── Hint button ──
                HintButton = new Button { text = "Request Hint" };
                HintButton.style.height = 36f;
                HintButton.style.marginTop = 4f;
                HintButton.style.marginBottom = 4f;
                HintButton.style.fontSize = 13f;
                HintButton.style.backgroundColor = new Color(0.15f, 0.25f, 0.4f, 0.9f);
                HintButton.style.color = NavBtnText;
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

                ContextActionButton = new Button { text = "Place Assembly" };
                ContextActionButton.style.height = 44f;
                ContextActionButton.style.marginTop = 6f;
                ContextActionButton.style.fontSize = 15f;
                ContextActionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                ContextActionButton.style.borderTopLeftRadius = 6f;
                ContextActionButton.style.borderTopRightRadius = 6f;
                ContextActionButton.style.borderBottomLeftRadius = 6f;
                ContextActionButton.style.borderBottomRightRadius = 6f;
                ContextActionButton.style.display = DisplayStyle.None;
                Add(ContextActionButton);

                ConfirmButton = new Button { text = "Continue" };
                ConfirmButton.style.height = 44f;
                ConfirmButton.style.marginTop = 6f;
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

                // ── Nav overlay: floats absolutely below the locator chip, so opening it
                // does NOT push hero / details / action buttons down. Behaves like a
                // dropdown menu — overlays content beneath it, content stays put.
                _navOverlay = new VisualElement();
                _navOverlay.style.position = Position.Absolute;
                // Anchor below the locator chip (assembly row 18 + margin 4 + chip 24 + small gap).
                _navOverlay.style.top = 50f;
                _navOverlay.style.left = 0f;
                _navOverlay.style.flexDirection = FlexDirection.Row;
                _navOverlay.style.alignItems = Align.Center;
                _navOverlay.style.justifyContent = Justify.Center;
                _navOverlay.style.paddingTop = 8f;
                _navOverlay.style.paddingBottom = 8f;
                _navOverlay.style.paddingLeft = 8f;
                _navOverlay.style.paddingRight = 8f;
                _navOverlay.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.98f);
                _navOverlay.style.borderTopLeftRadius = 8f;
                _navOverlay.style.borderTopRightRadius = 8f;
                _navOverlay.style.borderBottomLeftRadius = 8f;
                _navOverlay.style.borderBottomRightRadius = 8f;
                _navOverlay.style.borderTopWidth = 1f;
                _navOverlay.style.borderRightWidth = 1f;
                _navOverlay.style.borderBottomWidth = 1f;
                _navOverlay.style.borderLeftWidth = 1f;
                _navOverlay.style.borderTopColor = NavBtnBorder;
                _navOverlay.style.borderRightColor = NavBtnBorder;
                _navOverlay.style.borderBottomColor = NavBtnBorder;
                _navOverlay.style.borderLeftColor = NavBtnBorder;
                _navOverlay.style.display = DisplayStyle.None;

                SkipToStartButton = CreateNavButton("|◀"); // |◀ U+25C0
                SkipToStartButton.tooltip = "Jump to first step";
                SkipToStartButton.style.marginRight = 4f;
                BackButton = CreateNavButton("◀"); // ◀ U+25C0
                BackButton.tooltip = "Previous step";
                BackButton.style.marginRight = 4f;
                ForwardButton = CreateNavButton("▶"); // ▶ U+25B6
                ForwardButton.tooltip = "Next step";
                ForwardButton.style.marginLeft = 4f;
                SkipToEndButton = CreateNavButton("▶|"); // ▶| U+25B6
                SkipToEndButton.tooltip = "Jump to last step";
                SkipToEndButton.style.marginLeft = 4f;

                StepNumberField = BuildStepNumberField();
                StepSuffixLabel = new Label("of 0");
                StepSuffixLabel.style.fontSize = 11f;
                StepSuffixLabel.style.color = MetaText;
                StepSuffixLabel.style.marginLeft = 0f;
                StepSuffixLabel.style.marginRight = 4f;

                var prefix = new Label("Step ");
                prefix.style.fontSize = 11f;
                prefix.style.color = MetaText;
                prefix.style.marginLeft = 4f;

                _navOverlay.Add(SkipToStartButton);
                _navOverlay.Add(BackButton);
                _navOverlay.Add(prefix);
                _navOverlay.Add(StepNumberField);
                _navOverlay.Add(StepSuffixLabel);
                _navOverlay.Add(ForwardButton);
                _navOverlay.Add(SkipToEndButton);
                Add(_navOverlay);
            }

            // ── Public surface used by the controller ──

            public void SetNavChipText(int displayStep, int displayTotal)
            {
                _navChipButton.text = displayTotal > 0
                    ? $"Step {displayStep} of {displayTotal}  ▼" // ▼ U+25BC (renders in default font)
                    : "—";
            }

            public void SetMicroProgress(float ratio, bool visible)
            {
                _microProgressTrack.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;
                float pct = Mathf.Clamp01(ratio) * 100f;
                _microProgressFill.style.width = Length.Percent(pct);
            }

            public void SetToolChip(string toolDisplayName)
            {
                bool has = !string.IsNullOrWhiteSpace(toolDisplayName);
                _toolChip.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has) _toolChipLabel.text = $"TOOL  ·  {toolDisplayName}";
            }

            public void SetInstructionDetails(string text)
            {
                bool has = !string.IsNullOrWhiteSpace(text);
                _detailsDisclosure.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has) _detailsDisclosure.SetBody(text);
            }

            public void SetWhyItMatters(string text)
            {
                bool has = !string.IsNullOrWhiteSpace(text);
                _whyDisclosure.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has) _whyDisclosure.SetBody(text);
            }

            public void SetDetailsExpanded(bool expanded)
            {
                _detailsDisclosure.SetExpanded(expanded);
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

            public void SetAssemblyName(string name)
            {
                bool hasName = !string.IsNullOrWhiteSpace(name);
                AssemblyLabel.style.display = hasName ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasName) AssemblyLabel.text = name;
            }

            public void SetSectionsButtonVisible(bool visible)
            {
                SectionsButton.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetBackEnabled(bool enabled) => SetEnabledWithFade(BackButton, enabled);
            public void SetForwardEnabled(bool enabled) => SetEnabledWithFade(ForwardButton, enabled);
            public void SetSkipToStartEnabled(bool enabled) => SetEnabledWithFade(SkipToStartButton, enabled);
            public void SetSkipToEndEnabled(bool enabled) => SetEnabledWithFade(SkipToEndButton, enabled);

            private static void SetEnabledWithFade(Button btn, bool enabled)
            {
                btn.SetEnabled(enabled);
                btn.style.opacity = enabled ? 1f : 0.3f;
            }

            private bool _navOverlayShown;

            private void ToggleNavOverlay()
            {
                SetNavOverlayShown(!_navOverlayShown);
            }

            public void CloseNavOverlay() => SetNavOverlayShown(false);

            private void SetNavOverlayShown(bool shown)
            {
                _navOverlayShown = shown;
                _navOverlay.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Dismiss the popup on any click outside it (and outside the chip itself, so
            // the chip's own toggle still works). Hooked once when the view attaches.
            public void InstallClickAwayDismiss()
            {
                this.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (!_navOverlayShown) return;
                    var target = evt.target as VisualElement;
                    while (target != null)
                    {
                        if (target == _navOverlay || target == _navChipButton) return;
                        target = target.parent;
                    }
                    CloseNavOverlay();
                }, TrickleDown.TrickleDown);
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

            private static TextField BuildStepNumberField()
            {
                var field = new TextField { value = "1" };
                field.style.width = 52f;
                field.style.height = 26f;
                field.style.marginLeft = 2f;
                field.style.marginRight = 2f;
                field.style.borderTopLeftRadius = 4f;
                field.style.borderTopRightRadius = 4f;
                field.style.borderBottomLeftRadius = 4f;
                field.style.borderBottomRightRadius = 4f;
                field.style.borderTopWidth = 1f;
                field.style.borderRightWidth = 1f;
                field.style.borderBottomWidth = 1f;
                field.style.borderLeftWidth = 1f;
                field.style.borderTopColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                field.style.borderRightColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                field.style.borderBottomColor = new Color(0.42f, 0.82f, 1f, 0.4f);
                field.style.borderLeftColor = new Color(0.42f, 0.82f, 1f, 0.4f);

                field.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    var inputEl = field.Q(className: "unity-text-field__input");
                    if (inputEl != null)
                    {
                        inputEl.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f, 0.95f);
                        inputEl.style.color = new Color(0.42f, 0.82f, 1f, 1f);
                        inputEl.style.fontSize = 13f;
                        inputEl.style.unityTextAlign = TextAnchor.MiddleCenter;
                        inputEl.style.unityFontStyleAndWeight = FontStyle.Bold;
                        inputEl.style.paddingLeft = 2f;
                        inputEl.style.paddingRight = 2f;
                        inputEl.style.paddingTop = 0f;
                        inputEl.style.paddingBottom = 0f;
                        inputEl.style.borderTopLeftRadius = 4f;
                        inputEl.style.borderTopRightRadius = 4f;
                        inputEl.style.borderBottomLeftRadius = 4f;
                        inputEl.style.borderBottomRightRadius = 4f;
                    }
                    var labelEl = field.Q<Label>(className: "unity-text-field__label");
                    if (labelEl != null) labelEl.style.display = DisplayStyle.None;
                });

                return field;
            }
        }

        // Lightweight disclosure: a clickable header with a chevron + label, plus a body
        // panel that shows/hides. Built so we can fully control typography and contrast,
        // which Unity's built-in Foldout makes awkward.
        private sealed class Disclosure : VisualElement
        {
            private readonly Label _arrow;
            private readonly Label _title;
            private readonly Label _body;
            private readonly VisualElement _bodyHost;
            private readonly VisualElement _headerActionSlot;
            private bool _expanded;

            // Right-aligned slot on the header row for trailing controls (e.g. a Listen
            // button on the Details disclosure). Clicks inside the slot must NOT propagate
            // to the header toggle, so the trailing button can be tapped without expanding.
            public VisualElement HeaderActionSlot => _headerActionSlot;

            private static readonly Color HeaderText = new Color(0.78f, 0.88f, 1f, 1f);
            private static readonly Color HeaderTextHover = new Color(1f, 1f, 1f, 1f);

            public Disclosure(string title, float bodyFontSize, Color bodyColor, Color? accentBg = null)
            {
                style.flexDirection = FlexDirection.Column;
                style.marginBottom = 4f;

                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;
                header.style.height = 24f;
                header.style.paddingLeft = 2f;
                header.style.paddingRight = 2f;
                header.RegisterCallback<MouseDownEvent>(_ => Toggle());
                header.RegisterCallback<MouseEnterEvent>(_ => _title.style.color = HeaderTextHover);
                header.RegisterCallback<MouseLeaveEvent>(_ => _title.style.color = HeaderText);

                // Use the large triangles (U+25B6 ▶ / U+25BC ▼) — Unity's default font covers
                // these but NOT the small variants (U+25B8 ▸ / U+25BE ▾), which show as ☐.
                _arrow = new Label("▶");
                _arrow.style.fontSize = 9f;
                _arrow.style.color = HeaderText;
                _arrow.style.width = 14f;
                _arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
                header.Add(_arrow);

                _title = new Label(title);
                _title.style.fontSize = 12f;
                _title.style.unityFontStyleAndWeight = FontStyle.Bold;
                _title.style.color = HeaderText;
                _title.style.flexGrow = 1f;
                _title.style.unityTextAlign = TextAnchor.MiddleLeft;
                header.Add(_title);

                _headerActionSlot = new VisualElement();
                _headerActionSlot.style.flexDirection = FlexDirection.Row;
                _headerActionSlot.style.alignItems = Align.Center;
                _headerActionSlot.RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                header.Add(_headerActionSlot);

                Add(header);

                _bodyHost = new VisualElement();
                _bodyHost.style.paddingLeft = 14f;
                _bodyHost.style.paddingRight = 4f;
                _bodyHost.style.paddingTop = 4f;
                _bodyHost.style.paddingBottom = 6f;
                _bodyHost.style.display = DisplayStyle.None;
                if (accentBg.HasValue)
                {
                    _bodyHost.style.backgroundColor = accentBg.Value;
                    _bodyHost.style.borderTopLeftRadius = 4f;
                    _bodyHost.style.borderTopRightRadius = 4f;
                    _bodyHost.style.borderBottomLeftRadius = 4f;
                    _bodyHost.style.borderBottomRightRadius = 4f;
                }

                _body = new Label();
                _body.style.fontSize = bodyFontSize;
                _body.style.color = bodyColor;
                _body.style.whiteSpace = WhiteSpace.Normal;
                _bodyHost.Add(_body);

                Add(_bodyHost);
            }

            public void SetBody(string text) => _body.text = text;

            public void SetExpanded(bool expanded)
            {
                _expanded = expanded;
                _arrow.text = expanded ? "▼" : "▶"; // ▼ U+25BC / ▶ U+25B6 — both render in default font
                _bodyHost.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private void Toggle() => SetExpanded(!_expanded);
        }
    }
}
