using System;
using OSE.App;
using OSE.Core;
using OSE.UI.Root;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Owns the reposition/move-assembly and assembly scale UI.
    /// Compact variant: two icon buttons for the action bar pill, plus a
    /// scale popover panel that appears above the bar.
    /// </summary>
    internal sealed class RepositionUiController
    {
        // ── Icon characters ──
        // Move: left-right arrow (U+2194)
        private const string MoveIcon = "\u2194";
        // Scale: squared plus (U+229E)
        private const string ScaleIcon = "\u229E";

        private Button _moveButton;
        private Button _scaleButton;
        private VisualElement _scalePopover;
        private Button _decreaseScaleButton;
        private Button _increaseScaleButton;
        private Button _resetPositionButton;
        private Label _assemblyScaleLabel;
        private bool _repositionActive;
        private bool _scalePopoverVisible;

        /// <summary>Fired when the scale popover opens, so the host can close other popovers.</summary>
        public event Action ScalePopoverOpened;

        public bool IsRepositionActive => _repositionActive;

        /// <summary>Action bar icon button for Move Assembly.</summary>
        public Button MoveButton => _moveButton;

        /// <summary>Action bar icon button for Scale.</summary>
        public Button ScaleButton => _scaleButton;

        /// <summary>Popover panel shown above the action bar when scale is toggled.</summary>
        public VisualElement ScalePopover => _scalePopover;

        public void Build(VisualElement popoverParent)
        {
            BuildMoveButton();
            BuildScaleButton();
            BuildScalePopover(popoverParent);
            RefreshScaleUi();
        }

        public void Teardown()
        {
            if (_moveButton != null)
            {
                _moveButton.clicked -= HandleRepositionToggleClicked;
                _moveButton = null;
            }
            if (_scaleButton != null)
            {
                _scaleButton.clicked -= HandleScaleToggleClicked;
                _scaleButton = null;
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
            _scalePopover = null;
        }

        public void HandleRepositionModeChanged(RepositionModeChanged evt)
        {
            _repositionActive = evt.IsActive;
            ApplyMoveButtonStyle();
            RefreshScaleUi();
        }

        public void SetScalePopoverVisible(bool visible)
        {
            _scalePopoverVisible = visible;
            if (_scalePopover != null)
                _scalePopover.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyScaleButtonStyle();
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
            _assemblyScaleLabel.tooltip = $"1.00x = authored size. Reset returns to {defaultScale:0.00}x.";
            _decreaseScaleButton?.SetEnabled(canDecrease);
            _increaseScaleButton?.SetEnabled(canIncrease);

            if (_resetPositionButton != null)
            {
                bool showReset = _repositionActive || Mathf.Abs(scale - defaultScale) > 0.001f;
                _resetPositionButton.style.display = showReset ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ── Build helpers ──

        private void BuildMoveButton()
        {
            _moveButton = new Button();
            _moveButton.text = MoveIcon;
            _moveButton.tooltip = "Move Assembly";
            StyleIconButton(_moveButton);
            _moveButton.clicked += HandleRepositionToggleClicked;
            ApplyMoveButtonStyle();
        }

        private void BuildScaleButton()
        {
            _scaleButton = new Button();
            _scaleButton.text = ScaleIcon;
            _scaleButton.tooltip = "Scale Assembly";
            StyleIconButton(_scaleButton);
            _scaleButton.clicked += HandleScaleToggleClicked;
            ApplyScaleButtonStyle();
        }

        private void BuildScalePopover(VisualElement parent)
        {
            _scalePopover = new VisualElement();
            _scalePopover.name = "ose-scale-popover";
            _scalePopover.style.flexDirection = FlexDirection.Row;
            _scalePopover.style.alignItems = Align.Center;
            _scalePopover.style.justifyContent = Justify.Center;
            _scalePopover.style.paddingLeft = 10f;
            _scalePopover.style.paddingRight = 10f;
            _scalePopover.style.paddingTop = 8f;
            _scalePopover.style.paddingBottom = 8f;
            _scalePopover.style.marginBottom = 8f;
            _scalePopover.style.backgroundColor = new Color(0.07f, 0.11f, 0.18f, 0.96f);
            _scalePopover.style.borderTopLeftRadius = 12f;
            _scalePopover.style.borderTopRightRadius = 12f;
            _scalePopover.style.borderBottomLeftRadius = 12f;
            _scalePopover.style.borderBottomRightRadius = 12f;
            _scalePopover.style.borderTopColor = new Color(0.28f, 0.36f, 0.46f, 0.9f);
            _scalePopover.style.borderRightColor = new Color(0.28f, 0.36f, 0.46f, 0.9f);
            _scalePopover.style.borderBottomColor = new Color(0.28f, 0.36f, 0.46f, 0.9f);
            _scalePopover.style.borderLeftColor = new Color(0.28f, 0.36f, 0.46f, 0.9f);
            _scalePopover.style.borderTopWidth = 1f;
            _scalePopover.style.borderRightWidth = 1f;
            _scalePopover.style.borderBottomWidth = 1f;
            _scalePopover.style.borderLeftWidth = 1f;
            _scalePopover.pickingMode = PickingMode.Position;
            _scalePopover.style.display = DisplayStyle.None;

            _decreaseScaleButton = new Button();
            _decreaseScaleButton.text = "\u2212"; // minus sign
            _decreaseScaleButton.style.width = 32f;
            _decreaseScaleButton.style.height = 32f;
            _decreaseScaleButton.style.fontSize = 16f;
            _decreaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _decreaseScaleButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _decreaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _decreaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _decreaseScaleButton.style.borderTopLeftRadius = 8f;
            _decreaseScaleButton.style.borderTopRightRadius = 8f;
            _decreaseScaleButton.style.borderBottomLeftRadius = 8f;
            _decreaseScaleButton.style.borderBottomRightRadius = 8f;
            _decreaseScaleButton.clicked += HandleDecreaseScaleClicked;

            _assemblyScaleLabel = new Label("Scale 1.00x");
            _assemblyScaleLabel.style.minWidth = 80f;
            _assemblyScaleLabel.style.marginLeft = 8f;
            _assemblyScaleLabel.style.marginRight = 8f;
            _assemblyScaleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _assemblyScaleLabel.style.color = new Color(0.95f, 0.92f, 0.82f);
            _assemblyScaleLabel.style.fontSize = 13f;
            _assemblyScaleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _increaseScaleButton = new Button();
            _increaseScaleButton.text = "+";
            _increaseScaleButton.style.width = 32f;
            _increaseScaleButton.style.height = 32f;
            _increaseScaleButton.style.fontSize = 16f;
            _increaseScaleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _increaseScaleButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            _increaseScaleButton.style.backgroundColor = new Color(0.28f, 0.28f, 0.31f, 0.95f);
            _increaseScaleButton.style.color = new Color(0.92f, 0.92f, 0.92f);
            _increaseScaleButton.style.borderTopLeftRadius = 8f;
            _increaseScaleButton.style.borderTopRightRadius = 8f;
            _increaseScaleButton.style.borderBottomLeftRadius = 8f;
            _increaseScaleButton.style.borderBottomRightRadius = 8f;
            _increaseScaleButton.clicked += HandleIncreaseScaleClicked;

            _resetPositionButton = new Button();
            _resetPositionButton.text = "Reset";
            _resetPositionButton.style.height = 32f;
            _resetPositionButton.style.paddingLeft = 10f;
            _resetPositionButton.style.paddingRight = 10f;
            _resetPositionButton.style.marginLeft = 8f;
            _resetPositionButton.style.borderTopLeftRadius = 8f;
            _resetPositionButton.style.borderTopRightRadius = 8f;
            _resetPositionButton.style.borderBottomLeftRadius = 8f;
            _resetPositionButton.style.borderBottomRightRadius = 8f;
            _resetPositionButton.style.fontSize = 12f;
            _resetPositionButton.style.backgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.9f);
            _resetPositionButton.style.color = new Color(0.85f, 0.85f, 0.85f);
            _resetPositionButton.style.display = DisplayStyle.None;
            _resetPositionButton.clicked += HandleResetPositionClicked;

            _scalePopover.Add(_decreaseScaleButton);
            _scalePopover.Add(_assemblyScaleLabel);
            _scalePopover.Add(_increaseScaleButton);
            _scalePopover.Add(_resetPositionButton);
            parent.Add(_scalePopover);
        }

        private static readonly Color HoverBg = new Color(0.3f, 0.36f, 0.44f, 0.25f);

        private static void StyleIconButton(Button btn)
        {
            btn.style.width = 40f;
            btn.style.height = 40f;
            btn.style.fontSize = 18f;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.backgroundColor = Color.clear;
            btn.style.borderTopLeftRadius = 10f;
            btn.style.borderTopRightRadius = 10f;
            btn.style.borderBottomLeftRadius = 10f;
            btn.style.borderBottomRightRadius = 10f;
            btn.style.borderTopWidth = 0f;
            btn.style.borderRightWidth = 0f;
            btn.style.borderBottomWidth = 0f;
            btn.style.borderLeftWidth = 0f;
            btn.style.paddingLeft = 0f;
            btn.style.paddingRight = 0f;
            btn.style.paddingTop = 0f;
            btn.style.paddingBottom = 0f;
            btn.RegisterCallback<PointerEnterEvent>(_ => btn.style.backgroundColor = HoverBg);
            btn.RegisterCallback<PointerLeaveEvent>(_ => btn.style.backgroundColor = Color.clear);
        }

        private void ApplyMoveButtonStyle()
        {
            if (_moveButton == null) return;
            if (_repositionActive)
            {
                _moveButton.style.color = new Color(1f, 0.85f, 0.3f, 1f);
                _moveButton.tooltip = "Done Moving";
            }
            else
            {
                _moveButton.style.color = new Color(1f, 0.92f, 0.75f, 1f);
                _moveButton.tooltip = "Move Assembly";
            }
        }

        private void ApplyScaleButtonStyle()
        {
            if (_scaleButton == null) return;
            _scaleButton.style.color = _scalePopoverVisible
                ? new Color(0.42f, 0.82f, 1f, 1f)
                : new Color(0.82f, 0.92f, 1f, 1f);
        }

        // ── Handlers ──

        private void HandleRepositionToggleClicked()
        {
            RuntimeEventBus.Publish(new RepositionModeChanged(!_repositionActive));
        }

        private void HandleScaleToggleClicked()
        {
            bool opening = !_scalePopoverVisible;
            SetScalePopoverVisible(opening);
            if (opening)
                ScalePopoverOpened?.Invoke();
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
