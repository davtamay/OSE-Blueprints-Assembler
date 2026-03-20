using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Utilities
{
    internal static class UIToolkitStyleUtility
    {
        private static readonly Color PanelBackgroundColor = new Color(0.07f, 0.11f, 0.16f, 0.92f);
        private static readonly Color PanelBorderColor = new Color(0.28f, 0.36f, 0.46f, 0.9f);
        private static readonly Color PanelTextColor = new Color(0.94f, 0.97f, 1f, 1f);
        private static readonly Color AccentTextColor = new Color(0.42f, 0.82f, 1f, 1f);
        private static readonly Color SecondaryTextColor = new Color(0.74f, 0.81f, 0.89f, 0.96f);

        public static void ApplyRootLayout(VisualElement root)
        {
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.top = 0f;
            root.style.right = 0f;
            root.style.bottom = 0f;
            root.style.paddingLeft = 24f;
            root.style.paddingRight = 24f;
            root.style.paddingTop = 24f;
            root.style.paddingBottom = 24f;
            root.style.flexDirection = FlexDirection.Row;
            root.style.justifyContent = Justify.SpaceBetween;
            root.style.alignItems = Align.FlexStart;
        }

        public static void ApplyColumnLayout(VisualElement column, TextAnchor alignment)
        {
            column.style.flexDirection = FlexDirection.Column;
            column.style.alignItems = alignment == TextAnchor.UpperRight ? Align.FlexEnd : Align.FlexStart;
            column.style.flexShrink = 0f;
            column.pickingMode = PickingMode.Ignore;
        }

        public static void ApplyPanelSurface(VisualElement element)
        {
            element.style.width = 360f;
            element.style.maxWidth = 420f;
            element.style.paddingLeft = 18f;
            element.style.paddingRight = 18f;
            element.style.paddingTop = 16f;
            element.style.paddingBottom = 16f;
            element.style.marginBottom = 16f;
            element.style.flexDirection = FlexDirection.Column;
            element.style.backgroundColor = PanelBackgroundColor;
            element.style.borderTopColor = PanelBorderColor;
            element.style.borderRightColor = PanelBorderColor;
            element.style.borderBottomColor = PanelBorderColor;
            element.style.borderLeftColor = PanelBorderColor;
            element.style.borderTopWidth = 1f;
            element.style.borderRightWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftWidth = 1f;
            element.style.borderTopLeftRadius = 14f;
            element.style.borderTopRightRadius = 14f;
            element.style.borderBottomLeftRadius = 14f;
            element.style.borderBottomRightRadius = 14f;
            element.style.unityTextAlign = TextAnchor.UpperLeft;
            element.pickingMode = PickingMode.Ignore;
        }

        public static Label CreateEyebrowLabel(string text)
        {
            Label label = new Label(text?.ToUpperInvariant());
            label.style.fontSize = 11f;
            label.style.color = AccentTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 6f;
            return label;
        }

        public static Label CreateTitleLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 20f;
            label.style.color = PanelTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Label CreateBodyLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 13f;
            label.style.color = SecondaryTextColor;
            label.style.marginTop = 6f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Label CreateFieldLabel(string text)
        {
            Label label = new Label(text?.ToUpperInvariant());
            label.style.fontSize = 11f;
            label.style.color = AccentTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        /// <summary>
        /// Narrower panel surface for compact info chips (Part/Tool info).
        /// </summary>
        public static void ApplyCompactPanelSurface(VisualElement element)
        {
            element.style.width = 280f;
            element.style.maxWidth = 320f;
            element.style.paddingLeft = 14f;
            element.style.paddingRight = 14f;
            element.style.paddingTop = 10f;
            element.style.paddingBottom = 10f;
            element.style.marginBottom = 8f;
            element.style.flexDirection = FlexDirection.Column;
            element.style.backgroundColor = PanelBackgroundColor;
            element.style.borderTopColor = PanelBorderColor;
            element.style.borderRightColor = PanelBorderColor;
            element.style.borderBottomColor = PanelBorderColor;
            element.style.borderLeftColor = PanelBorderColor;
            element.style.borderTopWidth = 1f;
            element.style.borderRightWidth = 1f;
            element.style.borderBottomWidth = 1f;
            element.style.borderLeftWidth = 1f;
            element.style.borderTopLeftRadius = 10f;
            element.style.borderTopRightRadius = 10f;
            element.style.borderBottomLeftRadius = 10f;
            element.style.borderBottomRightRadius = 10f;
            element.style.unityTextAlign = TextAnchor.UpperLeft;
            element.pickingMode = PickingMode.Ignore;
        }

        /// <summary>
        /// Small chevron toggle button for expand/collapse (\u25B8 / \u25BE).
        /// </summary>
        public static Button CreateChevronButton(bool expanded = false)
        {
            var btn = new Button();
            btn.text = expanded ? "\u25BE" : "\u25B8";
            btn.style.width = 28f;
            btn.style.height = 28f;
            btn.style.fontSize = 14f;
            btn.style.backgroundColor = Color.clear;
            btn.style.color = AccentTextColor;
            btn.style.borderTopWidth = 0f;
            btn.style.borderRightWidth = 0f;
            btn.style.borderBottomWidth = 0f;
            btn.style.borderLeftWidth = 0f;
            btn.style.paddingLeft = 0f;
            btn.style.paddingRight = 0f;
            btn.style.paddingTop = 0f;
            btn.style.paddingBottom = 0f;
            btn.style.marginLeft = 0f;
            btn.style.marginRight = 0f;
            btn.style.unityTextAlign = TextAnchor.MiddleCenter;
            return btn;
        }

        public static Label CreateCompactTitleLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 14f;
            label.style.color = PanelTextColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.flexGrow = 1f;
            label.style.flexShrink = 1f;
            return label;
        }
    }
}
