using System;
using OSE.UI.Controllers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    internal sealed class ToolCursorBadgeController
    {
        private const float ToolCursorBadgeWidth = 172f;
        private const float ToolCursorBadgeHeight = 34f;
        private const float MouseCursorOffsetY = 22f;
        private const float TouchCursorOffsetY = 24f;

        private VisualElement _toolCursorBadge;
        private Label _toolCursorLabel;
        private readonly Func<ToolDockStateMachine> _getToolDock;

        public ToolCursorBadgeController(Func<ToolDockStateMachine> getToolDock)
        {
            _getToolDock = getToolDock;
        }

        public void BuildToolCursorVisual(VisualElement root)
        {
            _toolCursorBadge = new VisualElement();
            _toolCursorBadge.name = "ose-tool-cursor-badge";
            _toolCursorBadge.style.position = Position.Absolute;
            _toolCursorBadge.style.width = ToolCursorBadgeWidth;
            _toolCursorBadge.style.height = ToolCursorBadgeHeight;
            _toolCursorBadge.style.alignItems = Align.Center;
            _toolCursorBadge.style.justifyContent = Justify.Center;
            _toolCursorBadge.style.paddingLeft = 10f;
            _toolCursorBadge.style.paddingRight = 10f;
            _toolCursorBadge.style.paddingTop = 4f;
            _toolCursorBadge.style.paddingBottom = 4f;
            _toolCursorBadge.style.backgroundColor = new Color(0.20f, 0.14f, 0.06f, 0.92f);
            _toolCursorBadge.style.borderTopLeftRadius = 8f;
            _toolCursorBadge.style.borderTopRightRadius = 8f;
            _toolCursorBadge.style.borderBottomLeftRadius = 8f;
            _toolCursorBadge.style.borderBottomRightRadius = 8f;
            _toolCursorBadge.style.borderTopColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderRightColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderBottomColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.borderLeftColor = new Color(0.98f, 0.82f, 0.42f, 0.95f);
            _toolCursorBadge.style.display = DisplayStyle.None;
            _toolCursorBadge.pickingMode = PickingMode.Ignore;

            _toolCursorLabel = new Label("Tool");
            _toolCursorLabel.style.fontSize = 12f;
            _toolCursorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolCursorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toolCursorLabel.style.color = new Color(1f, 0.95f, 0.75f, 1f);
            _toolCursorBadge.Add(_toolCursorLabel);

            root.Add(_toolCursorBadge);
        }

        public void UpdateToolCursorVisual()
        {
            if (_toolCursorBadge == null || _toolCursorLabel == null)
                return;

            ToolDockStateMachine toolDock = _getToolDock();
            string activeToolId = toolDock?.ActiveToolId;
            if (!Application.isPlaying ||
                toolDock == null ||
                string.IsNullOrWhiteSpace(activeToolId) ||
                !toolDock.TryPopulateToolInfo(activeToolId))
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            if (!TryGetPointerScreenPosition(out Vector2 screenPos, out bool isTouchInput))
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            IPanel panel = _toolCursorBadge.panel;
            if (panel == null || panel.visualTree == null)
            {
                _toolCursorBadge.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, screenPos);

            float x = panelPos.x - (ToolCursorBadgeWidth * 0.5f);
            Rect panelBounds = panel.visualTree.worldBound;
            float y = (panelBounds.height - panelPos.y) + MouseCursorOffsetY;

            if (isTouchInput)
            {
                y += TouchCursorOffsetY;
            }

            float maxX = Mathf.Max(4f, panelBounds.width - ToolCursorBadgeWidth - 4f);
            float maxY = Mathf.Max(4f, panelBounds.height - ToolCursorBadgeHeight - 4f);
            x = Mathf.Clamp(x, 4f, maxX);
            y = Mathf.Clamp(y, 4f, maxY);

            _toolCursorLabel.text = $"Tool: {toolDock.ToolName}";
            _toolCursorBadge.style.left = x;
            _toolCursorBadge.style.top = y;
            _toolCursorBadge.style.display = DisplayStyle.Flex;
        }

        public void Teardown()
        {
            _toolCursorBadge = null;
            _toolCursorLabel = null;
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPos, out bool isTouchInput)
        {
            screenPos = default;
            isTouchInput = false;

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
                isTouchInput = true;
                return true;
            }

            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}
