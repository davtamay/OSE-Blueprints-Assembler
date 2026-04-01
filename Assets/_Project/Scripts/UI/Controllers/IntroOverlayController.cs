using System;
using System.Threading.Tasks;
using OSE.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Owns the machine intro overlay: pending state, VisualElement construction,
    /// image loading, badge helpers, and dismiss/reset logic.
    /// Extracted from UIRootCoordinator (Phase 6).
    /// </summary>
    internal sealed class IntroOverlayController
    {
        private readonly Func<VisualElement> _getRootElement;
        private readonly Action _hideAllPanels;
        private readonly Action _onDismissed;

        private VisualElement _introOverlay;
        private bool _introVisible;
        private bool _introDismissedThisSession;
        private string _introMachineId;
        private bool _pendingBuild;
        private string _pendingTitle;
        private string _pendingDescription;
        private string _pendingDifficulty;
        private int _pendingEstimatedMinutes;
        private string[] _pendingLearningObjectives;
        private string _pendingImageRef;
        private int _pendingSavedCompletedSteps;
        private int _pendingSavedTotalSteps;

        /// <summary>
        /// When true, the intro overlay shows a "Choose Section" button that
        /// publishes <see cref="AssemblyPickerRequested"/>. Set by the coordinator
        /// based on whether the package has multiple assemblies.
        /// </summary>
        public bool ShowSectionPicker { get; set; }

        public bool IsVisible => _introVisible;
        public bool HasPendingBuild => _pendingBuild;

        public IntroOverlayController(
            Func<VisualElement> getRootElement,
            Action hideAllPanels,
            Action onDismissed)
        {
            _getRootElement = getRootElement;
            _hideAllPanels = hideAllPanels;
            _onDismissed = onDismissed;
        }

        // ════════════════════════════════════════════════════════════════════
        // Public API (called by UIRootCoordinator on behalf of IPresentationAdapter)
        // ════════════════════════════════════════════════════════════════════

        public void Show(string title, string description, string difficulty,
            int estimatedMinutes, string[] learningObjectives, string imageRef,
            int savedCompletedSteps = 0, int savedTotalSteps = 0)
        {
            if (_introDismissedThisSession)
                return;

            _introMachineId = title;
            _introVisible = true;
            _pendingTitle = title;
            _pendingDescription = description;
            _pendingDifficulty = difficulty;
            _pendingEstimatedMinutes = estimatedMinutes;
            _pendingLearningObjectives = learningObjectives;
            _pendingImageRef = imageRef;
            _pendingSavedCompletedSteps = savedCompletedSteps;
            _pendingSavedTotalSteps = savedTotalSteps;

            if (TryBuildPending())
            {
                _pendingBuild = false;
                _hideAllPanels?.Invoke();
                return;
            }

            _pendingBuild = true;
        }

        public void Dismiss()
        {
            if (!_introVisible) return;

            _introVisible = false;
            _introDismissedThisSession = true;
            _pendingBuild = false;
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }

            _onDismissed?.Invoke();
        }

        public void ResetState()
        {
            _introDismissedThisSession = false;
            _pendingBuild = false;
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }
            _introVisible = false;
        }

        /// <summary>
        /// Called from Update — attempts to build a deferred intro overlay
        /// when the root VisualElement was not yet available during Show().
        /// Returns true if the overlay was successfully built.
        /// </summary>
        public bool TryBuildPending()
        {
            VisualElement root = _getRootElement?.Invoke();
            if (root == null)
                return false;

            return BuildOverlay(
                root,
                _pendingTitle,
                _pendingDescription,
                _pendingDifficulty,
                _pendingEstimatedMinutes,
                _pendingLearningObjectives,
                _pendingImageRef,
                _pendingSavedCompletedSteps,
                _pendingSavedTotalSteps);
        }

        public void Teardown()
        {
            _introDismissedThisSession = false;
            _pendingBuild = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // Overlay construction
        // ════════════════════════════════════════════════════════════════════

        private bool BuildOverlay(VisualElement root, string title, string description,
            string difficulty, int estimatedMinutes, string[] learningObjectives,
            string imageRef, int savedCompletedSteps = 0, int savedTotalSteps = 0)
        {
            if (_introOverlay != null)
            {
                _introOverlay.RemoveFromHierarchy();
                _introOverlay = null;
            }

            if (root == null)
                return false;

            bool hasSavedProgress = savedCompletedSteps > 0 && savedTotalSteps > 0;

            // Fullscreen semi-transparent backdrop
            _introOverlay = new VisualElement();
            _introOverlay.name = "ose-intro-overlay";
            _introOverlay.style.position = Position.Absolute;
            _introOverlay.style.left = 0f;
            _introOverlay.style.right = 0f;
            _introOverlay.style.top = 0f;
            _introOverlay.style.bottom = 0f;
            _introOverlay.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            _introOverlay.style.alignItems = Align.Center;
            _introOverlay.style.justifyContent = Justify.Center;
            _introOverlay.pickingMode = PickingMode.Position;

            // Card container
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 0.98f);
            card.style.borderTopLeftRadius = 16f;
            card.style.borderTopRightRadius = 16f;
            card.style.borderBottomLeftRadius = 16f;
            card.style.borderBottomRightRadius = 16f;
            card.style.paddingTop = 24f;
            card.style.paddingBottom = 28f;
            card.style.paddingLeft = 32f;
            card.style.paddingRight = 32f;
            card.style.maxWidth = 480f;
            card.style.minWidth = 320f;
            card.style.maxHeight = new Length(92f, LengthUnit.Percent);
            card.style.alignItems = Align.Center;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderBottomColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderLeftColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderRightColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);

            // Content area
            var contentArea = new ScrollView(ScrollViewMode.Vertical);
            contentArea.style.flexGrow = 1f;
            contentArea.style.flexShrink = 1f;
            contentArea.style.alignItems = Align.Center;

            // Image
            var imageContainer = new VisualElement();
            imageContainer.style.width = 240f;
            imageContainer.style.height = 140f;
            imageContainer.style.backgroundColor = new Color(0.18f, 0.20f, 0.24f, 1f);
            imageContainer.style.borderTopLeftRadius = 10f;
            imageContainer.style.borderTopRightRadius = 10f;
            imageContainer.style.borderBottomLeftRadius = 10f;
            imageContainer.style.borderBottomRightRadius = 10f;
            imageContainer.style.marginBottom = 14f;
            imageContainer.style.alignItems = Align.Center;
            imageContainer.style.justifyContent = Justify.Center;

            if (!string.IsNullOrWhiteSpace(imageRef))
                _ = TryLoadIntroImage(imageContainer, imageRef);
            else
            {
                var placeholder = new Label("[ Machine Preview ]");
                placeholder.style.color = new Color(0.5f, 0.52f, 0.58f);
                placeholder.style.fontSize = 14f;
                imageContainer.Add(placeholder);
            }

            contentArea.Add(imageContainer);

            // Title
            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 20f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.95f, 0.96f, 0.98f);
            titleLabel.style.marginBottom = 8f;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.maxWidth = 420f;
            contentArea.Add(titleLabel);

            // Difficulty + time badges
            var badgeRow = new VisualElement();
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.marginBottom = 10f;

            if (!string.IsNullOrWhiteSpace(difficulty))
            {
                var diffBadge = CreateBadge(CapitalizeFirst(difficulty), DifficultyColor(difficulty));
                badgeRow.Add(diffBadge);
            }

            if (estimatedMinutes > 0)
            {
                string timeText = estimatedMinutes >= 60
                    ? $"{estimatedMinutes / 60}h {estimatedMinutes % 60}m"
                    : $"{estimatedMinutes} min";
                var timeBadge = CreateBadge(timeText, new Color(0.22f, 0.45f, 0.7f, 1f));
                timeBadge.style.marginLeft = 8f;
                badgeRow.Add(timeBadge);
            }

            contentArea.Add(badgeRow);

            // Description
            if (!string.IsNullOrWhiteSpace(description))
            {
                var descLabel = new Label(description);
                descLabel.style.fontSize = 12f;
                descLabel.style.color = new Color(0.75f, 0.78f, 0.82f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginBottom = 10f;
                descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                descLabel.style.maxWidth = 400f;
                contentArea.Add(descLabel);
            }

            // Learning objectives
            if (learningObjectives != null && learningObjectives.Length > 0)
            {
                var objectivesHeader = new Label("What you'll learn:");
                objectivesHeader.style.fontSize = 11f;
                objectivesHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                objectivesHeader.style.color = new Color(0.6f, 0.75f, 0.9f);
                objectivesHeader.style.marginBottom = 4f;
                objectivesHeader.style.unityTextAlign = TextAnchor.MiddleLeft;
                objectivesHeader.style.alignSelf = Align.FlexStart;
                contentArea.Add(objectivesHeader);

                int maxObjectives = Mathf.Min(learningObjectives.Length, 4);
                for (int i = 0; i < maxObjectives; i++)
                {
                    if (string.IsNullOrWhiteSpace(learningObjectives[i])) continue;

                    var objLabel = new Label($"  \u2022  {learningObjectives[i].Trim()}");
                    objLabel.style.fontSize = 11f;
                    objLabel.style.color = new Color(0.68f, 0.72f, 0.78f);
                    objLabel.style.marginBottom = 2f;
                    objLabel.style.whiteSpace = WhiteSpace.Normal;
                    objLabel.style.alignSelf = Align.FlexStart;
                    contentArea.Add(objLabel);
                }
            }

            card.Add(contentArea);

            // Separator
            var separator = new VisualElement();
            separator.style.width = new Length(90f, LengthUnit.Percent);
            separator.style.height = 1f;
            separator.style.backgroundColor = new Color(0.25f, 0.27f, 0.32f, 0.5f);
            separator.style.marginTop = 10f;
            separator.style.marginBottom = 14f;
            separator.style.flexShrink = 0f;
            card.Add(separator);

            // Progress display (only when saved progress exists)
            if (hasSavedProgress)
            {
                float percent = (float)savedCompletedSteps / savedTotalSteps;
                int percentInt = Mathf.RoundToInt(percent * 100f);

                var percentLabel = new Label($"{percentInt}% Complete");
                percentLabel.style.fontSize = 15f;
                percentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                percentLabel.style.color = new Color(0.5f, 0.85f, 0.6f);
                percentLabel.style.marginBottom = 3f;
                percentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(percentLabel);

                var stepLabel = new Label($"Step {savedCompletedSteps} of {savedTotalSteps}");
                stepLabel.style.fontSize = 11f;
                stepLabel.style.color = new Color(0.6f, 0.63f, 0.68f);
                stepLabel.style.marginBottom = 6f;
                stepLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(stepLabel);

                var progressBarBg = new VisualElement();
                progressBarBg.style.width = new Length(80f, LengthUnit.Percent);
                progressBarBg.style.height = 8f;
                progressBarBg.style.backgroundColor = new Color(0.2f, 0.22f, 0.26f, 1f);
                progressBarBg.style.borderTopLeftRadius = 5f;
                progressBarBg.style.borderTopRightRadius = 5f;
                progressBarBg.style.borderBottomLeftRadius = 5f;
                progressBarBg.style.borderBottomRightRadius = 5f;
                progressBarBg.style.marginBottom = 14f;

                var progressBarFill = new VisualElement();
                progressBarFill.style.width = new Length(percent * 100f, LengthUnit.Percent);
                progressBarFill.style.height = 8f;
                progressBarFill.style.backgroundColor = new Color(0.2f, 0.65f, 0.4f, 1f);
                progressBarFill.style.borderTopLeftRadius = 5f;
                progressBarFill.style.borderTopRightRadius = 5f;
                progressBarFill.style.borderBottomLeftRadius = 5f;
                progressBarFill.style.borderBottomRightRadius = 5f;
                progressBarBg.Add(progressBarFill);

                card.Add(progressBarBg);
            }

            // Button area
            var buttonArea = new VisualElement();
            buttonArea.style.alignItems = Align.Center;
            buttonArea.style.width = new Length(100f, LengthUnit.Percent);
            buttonArea.style.flexShrink = 0f;

            var continueBtn = new Button();
            continueBtn.text = hasSavedProgress ? "Resume" : "Begin Assembly";
            continueBtn.style.height = 42f;
            continueBtn.style.width = 200f;
            continueBtn.style.fontSize = 15f;
            continueBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            continueBtn.style.backgroundColor = new Color(0.2f, 0.65f, 0.4f, 1f);
            continueBtn.style.color = Color.white;
            continueBtn.style.borderTopLeftRadius = 14f;
            continueBtn.style.borderTopRightRadius = 14f;
            continueBtn.style.borderBottomLeftRadius = 14f;
            continueBtn.style.borderBottomRightRadius = 14f;
            continueBtn.style.borderTopWidth = 0f;
            continueBtn.style.borderBottomWidth = 0f;
            continueBtn.style.borderLeftWidth = 0f;
            continueBtn.style.borderRightWidth = 0f;
            continueBtn.clicked += () =>
            {
                Dismiss();
                RuntimeEventBus.Publish(new MachineIntroDismissed(_introMachineId ?? string.Empty));
            };
            buttonArea.Add(continueBtn);

            // "Choose Section" button — only visible for multi-assembly packages
            if (ShowSectionPicker)
            {
                var sectionBtn = new Button();
                sectionBtn.text = "Choose Section";
                sectionBtn.style.height = 34f;
                sectionBtn.style.width = 200f;
                sectionBtn.style.fontSize = 13f;
                sectionBtn.style.marginTop = 8f;
                sectionBtn.style.backgroundColor = new Color(0.14f, 0.18f, 0.26f, 1f);
                sectionBtn.style.color = new Color(0.6f, 0.75f, 0.95f);
                sectionBtn.style.borderTopLeftRadius = 10f;
                sectionBtn.style.borderTopRightRadius = 10f;
                sectionBtn.style.borderBottomLeftRadius = 10f;
                sectionBtn.style.borderBottomRightRadius = 10f;
                sectionBtn.style.borderTopWidth = 1f;
                sectionBtn.style.borderBottomWidth = 1f;
                sectionBtn.style.borderLeftWidth = 1f;
                sectionBtn.style.borderRightWidth = 1f;
                sectionBtn.style.borderTopColor = new Color(0.3f, 0.4f, 0.6f, 0.4f);
                sectionBtn.style.borderBottomColor = new Color(0.3f, 0.4f, 0.6f, 0.4f);
                sectionBtn.style.borderLeftColor = new Color(0.3f, 0.4f, 0.6f, 0.4f);
                sectionBtn.style.borderRightColor = new Color(0.3f, 0.4f, 0.6f, 0.4f);
                sectionBtn.clicked += () =>
                {
                    Dismiss();
                    RuntimeEventBus.Publish(new AssemblyPickerRequested());
                };
                buttonArea.Add(sectionBtn);
            }

            if (hasSavedProgress)
            {
                var resetBtn = new Button();
                resetBtn.text = "Reset Progress";
                resetBtn.style.height = 30f;
                resetBtn.style.width = 200f;
                resetBtn.style.fontSize = 11f;
                resetBtn.style.marginTop = 8f;
                resetBtn.style.backgroundColor = new Color(0.16f, 0.16f, 0.20f, 1f);
                resetBtn.style.color = new Color(0.75f, 0.38f, 0.38f);
                resetBtn.style.borderTopLeftRadius = 10f;
                resetBtn.style.borderTopRightRadius = 10f;
                resetBtn.style.borderBottomLeftRadius = 10f;
                resetBtn.style.borderBottomRightRadius = 10f;
                resetBtn.style.borderTopWidth = 1f;
                resetBtn.style.borderBottomWidth = 1f;
                resetBtn.style.borderLeftWidth = 1f;
                resetBtn.style.borderRightWidth = 1f;
                resetBtn.style.borderTopColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderBottomColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderLeftColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.style.borderRightColor = new Color(0.5f, 0.25f, 0.25f, 0.4f);
                resetBtn.clicked += () =>
                {
                    Dismiss();
                    RuntimeEventBus.Publish(new MachineIntroReset(_introMachineId ?? string.Empty));
                };
                buttonArea.Add(resetBtn);
            }

            card.Add(buttonArea);

            _introOverlay.Add(card);
            root.Add(_introOverlay);
            return true;
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private static VisualElement CreateBadge(string text, Color bgColor)
        {
            var badge = new VisualElement();
            badge.style.backgroundColor = bgColor;
            badge.style.borderTopLeftRadius = 6f;
            badge.style.borderTopRightRadius = 6f;
            badge.style.borderBottomLeftRadius = 6f;
            badge.style.borderBottomRightRadius = 6f;
            badge.style.paddingLeft = 10f;
            badge.style.paddingRight = 10f;
            badge.style.paddingTop = 4f;
            badge.style.paddingBottom = 4f;

            var label = new Label(text);
            label.style.fontSize = 11f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Color.white;
            badge.Add(label);

            return badge;
        }

        private static Color DifficultyColor(string difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty))
                return new Color(0.4f, 0.4f, 0.45f, 1f);

            return difficulty.Trim().ToLowerInvariant() switch
            {
                "beginner" => new Color(0.2f, 0.6f, 0.35f, 1f),
                "intermediate" => new Color(0.7f, 0.55f, 0.15f, 1f),
                "advanced" => new Color(0.7f, 0.25f, 0.2f, 1f),
                _ => new Color(0.4f, 0.4f, 0.45f, 1f)
            };
        }

        private static string CapitalizeFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private static async Task TryLoadIntroImage(VisualElement container, string imageRef)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, imageRef);
            if (!System.IO.File.Exists(path))
            {
                string packageRelative = System.IO.Path.Combine(
                    Application.streamingAssetsPath, "MachinePackages", imageRef);
                if (System.IO.File.Exists(packageRelative))
                    path = packageRelative;
                else
                {
                    var placeholder = new Label("[ Image not found ]");
                    placeholder.style.color = new Color(0.5f, 0.52f, 0.58f);
                    placeholder.style.fontSize = 12f;
                    container.Add(placeholder);
                    return;
                }
            }

            byte[] data = System.IO.File.ReadAllBytes(path);
            if (data == null || data.Length == 0) return;

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(data))
            {
                container.style.backgroundImage = new StyleBackground(tex);
                container.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
        }
    }
}
