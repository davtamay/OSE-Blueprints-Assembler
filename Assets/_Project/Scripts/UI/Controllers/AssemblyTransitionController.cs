using System;
using OSE.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Shows a brief interstitial card when one assembly completes and the next begins.
    /// Displays completed module name, next module info, and global progress.
    /// Dismissed by user tap or auto-continues after a timeout.
    /// </summary>
    internal sealed class AssemblyTransitionController
    {
        private readonly Func<VisualElement> _getRootElement;
        private readonly Action _onContinue;

        private VisualElement _overlay;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        public AssemblyTransitionController(
            Func<VisualElement> getRootElement,
            Action onContinue)
        {
            _getRootElement = getRootElement;
            _onContinue = onContinue;
        }

        public void Show(AssemblyTransitionRequested evt)
        {
            VisualElement root = _getRootElement?.Invoke();
            if (root == null)
                return;

            Dismiss();
            _isVisible = true;

            // Fullscreen semi-transparent backdrop
            _overlay = new VisualElement();
            _overlay.name = "ose-assembly-transition-overlay";
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0f;
            _overlay.style.right = 0f;
            _overlay.style.top = 0f;
            _overlay.style.bottom = 0f;
            _overlay.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.88f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.pickingMode = PickingMode.Position;

            // Card
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
            card.style.maxWidth = 440f;
            card.style.minWidth = 300f;
            card.style.alignItems = Align.Center;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderBottomColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderLeftColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderRightColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);

            // Checkmark + completed label
            var checkLabel = new Label("\u2713  Module Complete");
            checkLabel.style.fontSize = 14f;
            checkLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            checkLabel.style.color = new Color(0.4f, 0.85f, 0.5f, 1f);
            checkLabel.style.marginBottom = 4f;
            card.Add(checkLabel);

            // Completed assembly name
            var completedLabel = new Label(evt.CompletedAssemblyName ?? "");
            completedLabel.style.fontSize = 16f;
            completedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            completedLabel.style.color = new Color(0.9f, 0.92f, 0.95f, 1f);
            completedLabel.style.marginBottom = 12f;
            completedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            completedLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(completedLabel);

            // Separator
            var sep = new VisualElement();
            sep.style.width = new Length(80f, LengthUnit.Percent);
            sep.style.height = 1f;
            sep.style.backgroundColor = new Color(0.25f, 0.27f, 0.32f, 0.5f);
            sep.style.marginBottom = 12f;
            card.Add(sep);

            // "Next:" header
            var nextHeader = new Label("Next Module");
            nextHeader.style.fontSize = 11f;
            nextHeader.style.color = new Color(0.55f, 0.65f, 0.8f, 0.8f);
            nextHeader.style.marginBottom = 4f;
            card.Add(nextHeader);

            // Next assembly name
            var nextNameLabel = new Label(evt.NextAssemblyName ?? evt.NextAssemblyId);
            nextNameLabel.style.fontSize = 18f;
            nextNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nextNameLabel.style.color = new Color(0.95f, 0.96f, 0.98f, 1f);
            nextNameLabel.style.marginBottom = 8f;
            nextNameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nextNameLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(nextNameLabel);

            // Description
            if (!string.IsNullOrWhiteSpace(evt.NextAssemblyDescription))
            {
                var descLabel = new Label(evt.NextAssemblyDescription);
                descLabel.style.fontSize = 12f;
                descLabel.style.color = new Color(0.72f, 0.75f, 0.8f, 0.9f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginBottom = 8f;
                descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                descLabel.style.maxWidth = 380f;
                card.Add(descLabel);
            }

            // Learning focus
            if (!string.IsNullOrWhiteSpace(evt.NextLearningFocus))
            {
                var focusLabel = new Label($"\u2022  {evt.NextLearningFocus.Trim()}");
                focusLabel.style.fontSize = 11f;
                focusLabel.style.color = new Color(0.6f, 0.75f, 0.9f, 0.85f);
                focusLabel.style.whiteSpace = WhiteSpace.Normal;
                focusLabel.style.marginBottom = 10f;
                focusLabel.style.alignSelf = Align.FlexStart;
                card.Add(focusLabel);
            }

            // Global progress
            if (evt.TotalStepsGlobal > 0)
            {
                string progressText = $"Module {evt.CompletedModuleIndex} of {evt.TotalModules}  \u2014  " +
                                      $"{evt.CompletedStepsGlobal} of {evt.TotalStepsGlobal} steps complete";
                var progressLabel = new Label(progressText);
                progressLabel.style.fontSize = 11f;
                progressLabel.style.color = new Color(0.55f, 0.6f, 0.68f, 0.8f);
                progressLabel.style.marginBottom = 8f;
                progressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(progressLabel);

                // Progress bar
                float ratio = (float)evt.CompletedStepsGlobal / evt.TotalStepsGlobal;
                var barBg = new VisualElement();
                barBg.style.width = new Length(80f, LengthUnit.Percent);
                barBg.style.height = 6f;
                barBg.style.backgroundColor = new Color(0.2f, 0.22f, 0.26f, 1f);
                barBg.style.borderTopLeftRadius = 3f;
                barBg.style.borderTopRightRadius = 3f;
                barBg.style.borderBottomLeftRadius = 3f;
                barBg.style.borderBottomRightRadius = 3f;
                barBg.style.marginBottom = 14f;

                var barFill = new VisualElement();
                barFill.style.width = new Length(Mathf.Clamp01(ratio) * 100f, LengthUnit.Percent);
                barFill.style.height = 6f;
                barFill.style.backgroundColor = new Color(0.42f, 0.68f, 1f, 0.9f);
                barFill.style.borderTopLeftRadius = 3f;
                barFill.style.borderTopRightRadius = 3f;
                barFill.style.borderBottomLeftRadius = 3f;
                barFill.style.borderBottomRightRadius = 3f;
                barBg.Add(barFill);
                card.Add(barBg);
            }

            // Continue button
            var continueBtn = new Button();
            continueBtn.text = "Continue";
            continueBtn.style.height = 42f;
            continueBtn.style.width = 180f;
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
            continueBtn.clicked += () => Dismiss();
            card.Add(continueBtn);

            _overlay.Add(card);
            root.Add(_overlay);
        }

        public void Dismiss()
        {
            if (!_isVisible)
                return;

            _isVisible = false;
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
            }

            _onContinue?.Invoke();
        }

        public void Teardown()
        {
            _isVisible = false;
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
            }
        }
    }
}
