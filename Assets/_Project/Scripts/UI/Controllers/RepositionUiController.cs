using OSE.App;
using OSE.Core;
using OSE.UI.Root;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Owns the reposition/move-assembly buttons and assembly scale UI.
    /// Extracted from UIRootCoordinator (Phase 8).
    /// </summary>
    internal sealed class RepositionUiController
    {
        private Button _repositionButton;
        private Button _resetPositionButton;
        private Button _decreaseScaleButton;
        private Button _increaseScaleButton;
        private Label _assemblyScaleLabel;
        private bool _repositionActive;

        public bool IsRepositionActive => _repositionActive;

        public void Build(VisualElement parent)
        {
            var row = new VisualElement();
            row.name = "ose-reposition-row";
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.Center;
            row.style.marginBottom = 8f;
            row.pickingMode = PickingMode.Ignore;

            _repositionButton = new Button();
            _repositionButton.text = "Move Assembly";
            _repositionButton.style.height = 42f;
            _repositionButton.style.paddingLeft = 18f;
            _repositionButton.style.paddingRight = 18f;
            _repositionButton.style.borderTopLeftRadius = 12f;
            _repositionButton.style.borderTopRightRadius = 12f;
            _repositionButton.style.borderBottomLeftRadius = 12f;
            _repositionButton.style.borderBottomRightRadius = 12f;
            _repositionButton.style.fontSize = 14f;
            _repositionButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            ApplyRepositionButtonStyle(false);
            _repositionButton.clicked += HandleRepositionToggleClicked;

            var scaleCluster = new VisualElement();
            scaleCluster.name = "ose-assembly-scale-cluster";
            scaleCluster.style.flexDirection = FlexDirection.Row;
            scaleCluster.style.alignItems = Align.Center;
            scaleCluster.style.marginLeft = 8f;
            scaleCluster.style.paddingLeft = 8f;
            scaleCluster.style.paddingRight = 8f;
            scaleCluster.style.paddingTop = 4f;
            scaleCluster.style.paddingBottom = 4f;
            scaleCluster.style.backgroundColor = new Color(0.16f, 0.16f, 0.19f, 0.92f);
            scaleCluster.style.borderTopLeftRadius = 10f;
            scaleCluster.style.borderTopRightRadius = 10f;
            scaleCluster.style.borderBottomLeftRadius = 10f;
            scaleCluster.style.borderBottomRightRadius = 10f;

            _decreaseScaleButton = new Button();
            _decreaseScaleButton.text = "-";
            _decreaseScaleButton.style.width = 30f;
            _decreaseScaleButton.style.height = 30f;
            _decreaseScaleButton.style.fontSize = 16f;
            _decreaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _decreaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _decreaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _decreaseScaleButton.style.borderTopLeftRadius = 8f;
            _decreaseScaleButton.style.borderTopRightRadius = 8f;
            _decreaseScaleButton.style.borderBottomLeftRadius = 8f;
            _decreaseScaleButton.style.borderBottomRightRadius = 8f;
            _decreaseScaleButton.clicked += HandleDecreaseScaleClicked;

            _assemblyScaleLabel = new Label("Scale 1.00x");
            _assemblyScaleLabel.style.minWidth = 88f;
            _assemblyScaleLabel.style.marginLeft = 8f;
            _assemblyScaleLabel.style.marginRight = 8f;
            _assemblyScaleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _assemblyScaleLabel.style.color = new Color(0.95f, 0.92f, 0.82f);
            _assemblyScaleLabel.style.fontSize = 13f;
            _assemblyScaleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _increaseScaleButton = new Button();
            _increaseScaleButton.text = "+";
            _increaseScaleButton.style.width = 30f;
            _increaseScaleButton.style.height = 30f;
            _increaseScaleButton.style.fontSize = 16f;
            _increaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _increaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _increaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _increaseScaleButton.style.borderTopLeftRadius = 8f;
            _increaseScaleButton.style.borderTopRightRadius = 8f;
            _increaseScaleButton.style.borderBottomLeftRadius = 8f;
            _increaseScaleButton.style.borderBottomRightRadius = 8f;
            _increaseScaleButton.clicked += HandleIncreaseScaleClicked;

            _resetPositionButton = new Button();
            _resetPositionButton.text = "Reset";
            _resetPositionButton.style.height = 34f;
            _resetPositionButton.style.paddingLeft = 12f;
            _resetPositionButton.style.paddingRight = 12f;
            _resetPositionButton.style.marginLeft = 6f;
            _resetPositionButton.style.borderTopLeftRadius = 8f;
            _resetPositionButton.style.borderTopRightRadius = 8f;
            _resetPositionButton.style.borderBottomLeftRadius = 8f;
            _resetPositionButton.style.borderBottomRightRadius = 8f;
            _resetPositionButton.style.fontSize = 12f;
            _resetPositionButton.style.backgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.9f);
            _resetPositionButton.style.color = new Color(0.85f, 0.85f, 0.85f);
            _resetPositionButton.style.display = DisplayStyle.None;
            _resetPositionButton.clicked += HandleResetPositionClicked;

            row.Add(_repositionButton);
            scaleCluster.Add(_decreaseScaleButton);
            scaleCluster.Add(_assemblyScaleLabel);
            scaleCluster.Add(_increaseScaleButton);
            row.Add(scaleCluster);
            row.Add(_resetPositionButton);
            parent.Insert(0, row);
            RefreshScaleUi();
        }

        public void Teardown()
        {
            if (_repositionButton != null)
            {
                _repositionButton.clicked -= HandleRepositionToggleClicked;
                _repositionButton = null;
            }
            if (_resetPositionButton != null)
            {
                _resetPositionButton.clicked -= HandleResetPositionClicked;
                _resetPositionButton = null;
            }
            if (_decreaseScaleButton != null)
            {
                _decreaseScaleButton.clicked -= HandleDecreaseScaleClicked;
                _decreaseScaleButton = null;
            }
            if (_increaseScaleButton != null)
            {
                _increaseScaleButton.clicked -= HandleIncreaseScaleClicked;
                _increaseScaleButton = null;
            }
            _assemblyScaleLabel = null;
        }

        public void HandleRepositionModeChanged(RepositionModeChanged evt)
        {
            _repositionActive = evt.IsActive;
            ApplyRepositionButtonStyle(evt.IsActive);
            RefreshScaleUi();
        }

        public void RefreshScaleUi()
        {
            if (_assemblyScaleLabel == null)
                return;

            float scale = 1f;
            float defaultScale = 1f;
            bool canDecrease = false;
            bool canIncrease = false;

            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller) && controller != null)
            {
                scale = controller.CurrentScaleMultiplier;
                defaultScale = controller.DefaultScaleMultiplier;
                canDecrease = controller.CanDecreaseScale;
                canIncrease = controller.CanIncreaseScale;
            }

            _assemblyScaleLabel.text = $"Scale {scale:0.00}x";
            _assemblyScaleLabel.tooltip = $"1.00x = authored package size in Unity units (meters-based content). Reset returns to {defaultScale:0.00}x for this package.";
            _decreaseScaleButton?.SetEnabled(canDecrease);
            _increaseScaleButton?.SetEnabled(canIncrease);

            if (_resetPositionButton != null)
            {
                bool showReset = _repositionActive || Mathf.Abs(scale - defaultScale) > 0.001f;
                _resetPositionButton.style.display = showReset ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void ApplyRepositionButtonStyle(bool active)
        {
            if (_repositionButton == null) return;

            if (active)
            {
                _repositionButton.text = "Done Moving";
                _repositionButton.style.backgroundColor = new Color(0.55f, 0.40f, 0.10f, 0.95f);
                _repositionButton.style.color = new Color(1f, 0.95f, 0.8f);
            }
            else
            {
                _repositionButton.text = "Move Assembly";
                _repositionButton.style.backgroundColor = new Color(0.38f, 0.28f, 0.12f, 0.95f);
                _repositionButton.style.color = new Color(1f, 0.92f, 0.75f);
            }
        }

        private void HandleRepositionToggleClicked()
        {
            RuntimeEventBus.Publish(new RepositionModeChanged(!_repositionActive));
        }

        private void HandleResetPositionClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.ResetPosition();

            RefreshScaleUi();
        }

        private void HandleDecreaseScaleClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.DecreaseScale();

            RefreshScaleUi();
        }

        private void HandleIncreaseScaleClicked()
        {
            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var controller))
                controller.IncreaseScale();

            RefreshScaleUi();
        }
    }
}
