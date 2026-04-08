using System;
using OSE.UI.Presenters;
using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    public sealed class ToolDockPanelController : PanelControllerBase<ToolDockPanelViewModel>
    {
        private ToolDockPanelView _view;

        public event Action ToggleRequested;
        public event Action<string> ToolSelected;
        public event Action UnequipRequested;
        public event Action<string> ToolHovered;
        public event Action ToolHoverCleared;

        /// <summary>
        /// The compact icon button for the action bar pill.
        /// Available after <see cref="PanelControllerBase{T}.Bind"/> is called.
        /// </summary>
        public Button ActionBarButton => _view?.ToggleButton;

        protected override string PanelName => "ose-tool-dock-panel";

        protected override VisualElement CreateView() => new ToolDockPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (ToolDockPanelView)root;
            _view.ToggleButton.clicked += HandleToggleClicked;
        }

        protected override void ApplyViewModel(ToolDockPanelViewModel viewModel)
        {
            _view.ApplyToggleStyle(viewModel.IsExpanded, viewModel.ShowUnequipAction);
            _view.ActiveToolLabel.text = viewModel.ActiveToolLabel;
            _view.SetUnequipAction(viewModel.ShowUnequipAction, viewModel.UnequipLabel, HandleUnequipRequested);
            _view.SetPaletteVisible(viewModel.IsExpanded);
            _view.SetEntries(
                viewModel.Entries,
                viewModel.EmptyStateMessage,
                HandleToolSelected,
                HandleToolHovered,
                HandleToolHoverCleared);
        }

        protected override void OnUnbind()
        {
            if (_view != null)
            {
                _view.ToggleButton.clicked -= HandleToggleClicked;
                _view.SetUnequipAction(false, string.Empty, HandleUnequipRequested);
            }

            _view = null;
        }

        private void HandleToggleClicked()
        {
            ToggleRequested?.Invoke();
        }

        private void HandleToolSelected(string toolId)
        {
            ToolSelected?.Invoke(toolId);
        }

        private void HandleUnequipRequested()
        {
            UnequipRequested?.Invoke();
        }

        private void HandleToolHovered(string toolId)
        {
            ToolHovered?.Invoke(toolId);
        }

        private void HandleToolHoverCleared()
        {
            ToolHoverCleared?.Invoke();
        }

        private sealed class ToolDockPanelView : VisualElement
        {
            public Button ToggleButton { get; }
            public Label ActiveToolLabel { get; }

            private readonly VisualElement _paletteCard;
            private readonly ScrollView _toolList;
            private readonly Label _emptyLabel;
            private readonly Button _unequipButton;

            // Drag-to-scroll state
            private bool _isDragging;
            private float _dragStartX;
            private float _dragStartScrollOffset;
            private float _velocity;
            private long _lastDragTime;
            private const float DragDeadzone = 4f;
            private const float Deceleration = 0.92f;
            private const float MinVelocity = 0.5f;
            private bool _dragExceededDeadzone;
            private IVisualElementScheduledItem _momentumSchedule;

            // Icon: hammer-and-pick U+2692
            private const string ToolIcon = "\u2692";

            public ToolDockPanelView()
            {
                pickingMode = PickingMode.Ignore;
                style.alignItems = Align.Center;
                style.flexDirection = FlexDirection.Column;
                style.marginBottom = 0f;

                // ── Compact icon toggle button (lives in action bar pill) ──
                ToggleButton = new Button();
                ToggleButton.text = ToolIcon;
                ToggleButton.tooltip = "Tools";
                ToggleButton.style.width = 40f;
                ToggleButton.style.height = 40f;
                ToggleButton.style.fontSize = 18f;
                ToggleButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                ToggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                ToggleButton.style.backgroundColor = Color.clear;
                ToggleButton.style.color = new Color(0.82f, 0.92f, 1f, 1f);
                ToggleButton.style.borderTopLeftRadius = 10f;
                ToggleButton.style.borderTopRightRadius = 10f;
                ToggleButton.style.borderBottomLeftRadius = 10f;
                ToggleButton.style.borderBottomRightRadius = 10f;
                ToggleButton.style.borderTopWidth = 0f;
                ToggleButton.style.borderRightWidth = 0f;
                ToggleButton.style.borderBottomWidth = 0f;
                ToggleButton.style.borderLeftWidth = 0f;
                ToggleButton.style.paddingLeft = 0f;
                ToggleButton.style.paddingRight = 0f;
                ToggleButton.style.paddingTop = 0f;
                ToggleButton.style.paddingBottom = 0f;
                ToggleButton.RegisterCallback<PointerEnterEvent>(_ =>
                    ToggleButton.style.backgroundColor = new Color(0.3f, 0.36f, 0.44f, 0.25f));
                ToggleButton.RegisterCallback<PointerLeaveEvent>(_ =>
                    ToggleButton.style.backgroundColor = Color.clear);
                // Toggle button is NOT added here — UIRootCoordinator places it
                // in the shared action bar pill.

                // ── Palette card (floats above action bar) ──
                _paletteCard = new VisualElement();
                UIToolkitStyleUtility.ApplyPanelSurface(_paletteCard);
                _paletteCard.style.width = 520f;
                _paletteCard.style.maxWidth = 620f;
                _paletteCard.style.marginBottom = 8f;
                _paletteCard.style.paddingTop = 12f;
                _paletteCard.style.paddingBottom = 12f;
                _paletteCard.style.paddingLeft = 14f;
                _paletteCard.style.paddingRight = 14f;
                _paletteCard.style.backgroundColor = new Color(0.07f, 0.11f, 0.18f, 0.96f);
                _paletteCard.pickingMode = PickingMode.Position;
                Add(_paletteCard);

                var title = UIToolkitStyleUtility.CreateEyebrowLabel("Tools");
                title.style.marginBottom = 2f;
                _paletteCard.Add(title);

                ActiveToolLabel = UIToolkitStyleUtility.CreateBodyLabel("Active tool: None");
                ActiveToolLabel.style.marginTop = 2f;
                ActiveToolLabel.style.marginBottom = 8f;
                ActiveToolLabel.style.color = new Color(0.82f, 0.92f, 1f, 1f);
                _paletteCard.Add(ActiveToolLabel);

                _unequipButton = new Button();
                _unequipButton.text = "Clear Active Tool";
                _unequipButton.style.height = 34f;
                _unequipButton.style.paddingLeft = 14f;
                _unequipButton.style.paddingRight = 14f;
                _unequipButton.style.fontSize = 12f;
                _unequipButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                _unequipButton.style.backgroundColor = new Color(0.29f, 0.17f, 0.17f, 0.92f);
                _unequipButton.style.color = new Color(1f, 0.9f, 0.9f, 1f);
                _unequipButton.style.borderTopLeftRadius = 8f;
                _unequipButton.style.borderTopRightRadius = 8f;
                _unequipButton.style.borderBottomLeftRadius = 8f;
                _unequipButton.style.borderBottomRightRadius = 8f;
                _unequipButton.style.marginBottom = 6f;
                _unequipButton.style.display = DisplayStyle.None;
                _paletteCard.Add(_unequipButton);

                _emptyLabel = UIToolkitStyleUtility.CreateBodyLabel("No tools are defined for this package.");
                _emptyLabel.style.marginTop = 4f;
                _emptyLabel.style.marginBottom = 2f;
                _paletteCard.Add(_emptyLabel);

                _toolList = new ScrollView(ScrollViewMode.Horizontal);
                _toolList.style.maxHeight = 100f;
                _toolList.style.flexDirection = FlexDirection.Row;
                _toolList.style.marginTop = 2f;
                _toolList.style.marginBottom = 0f;
                _toolList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _toolList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                _toolList.pickingMode = PickingMode.Position;
                _toolList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                _paletteCard.Add(_toolList);

                _toolList.RegisterCallback<PointerDownEvent>(OnToolListPointerDown);
                _toolList.RegisterCallback<PointerMoveEvent>(OnToolListPointerMove);
                _toolList.RegisterCallback<PointerUpEvent>(OnToolListPointerUp);
                _toolList.RegisterCallback<PointerCaptureOutEvent>(OnToolListPointerCaptureOut);
            }

            /// <summary>
            /// Tints the toggle icon when the palette is open or a tool is equipped.
            /// </summary>
            public void ApplyToggleStyle(bool isExpanded, bool hasEquippedTool)
            {
                if (isExpanded)
                {
                    ToggleButton.style.color = new Color(0.42f, 0.82f, 1f, 1f);
                }
                else if (hasEquippedTool)
                {
                    ToggleButton.style.color = new Color(0.36f, 0.95f, 0.58f, 1f);
                }
                else
                {
                    ToggleButton.style.color = new Color(0.82f, 0.92f, 1f, 1f);
                }
            }

            private void OnToolListPointerDown(PointerDownEvent evt)
            {
                StopMomentum();
                _isDragging = true;
                _dragExceededDeadzone = false;
                _dragStartX = evt.position.x;
                _dragStartScrollOffset = _toolList.scrollOffset.x;
                _lastDragTime = System.Diagnostics.Stopwatch.GetTimestamp();
                _toolList.CapturePointer(evt.pointerId);
            }

            private void OnToolListPointerMove(PointerMoveEvent evt)
            {
                if (!_isDragging) return;

                float deltaX = evt.position.x - _dragStartX;

                if (!_dragExceededDeadzone)
                {
                    if (Mathf.Abs(deltaX) < DragDeadzone) return;
                    _dragExceededDeadzone = true;
                }

                float newOffset = ClampScrollOffset(_dragStartScrollOffset - deltaX);
                _toolList.scrollOffset = new Vector2(newOffset, 0f);

                // Track velocity for momentum
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                float dt = (float)(now - _lastDragTime) / System.Diagnostics.Stopwatch.Frequency;
                if (dt > 0.001f)
                {
                    _velocity = -evt.deltaPosition.x / dt;
                    _lastDragTime = now;
                }

                evt.StopPropagation();
            }

            private void OnToolListPointerUp(PointerUpEvent evt)
            {
                if (!_isDragging) return;
                _isDragging = false;
                _toolList.ReleasePointer(evt.pointerId);

                // Start momentum animation if velocity is significant
                if (_dragExceededDeadzone && Mathf.Abs(_velocity) > MinVelocity)
                    StartMomentum();

                // If user dragged past deadzone, suppress the click so tool buttons
                // don't accidentally fire on release.
                if (_dragExceededDeadzone)
                    evt.StopPropagation();
            }

            private void OnToolListPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                _isDragging = false;
            }

            private void StartMomentum()
            {
                StopMomentum();
                _momentumSchedule = schedule.Execute(ApplyMomentum).Every(16);
            }

            private void StopMomentum()
            {
                if (_momentumSchedule != null)
                {
                    _momentumSchedule.Pause();
                    _momentumSchedule = null;
                }
                _velocity = 0f;
            }

            private void ApplyMomentum(TimerState timerState)
            {
                _velocity *= Deceleration;
                if (Mathf.Abs(_velocity) < MinVelocity)
                {
                    StopMomentum();
                    return;
                }

                float offset = ClampScrollOffset(_toolList.scrollOffset.x + _velocity * 0.016f);
                _toolList.scrollOffset = new Vector2(offset, 0f);

                // Stop momentum if we hit an edge
                if (offset <= 0f || offset >= MaxScrollOffset())
                    StopMomentum();
            }

            private float MaxScrollOffset()
            {
                // Use the content container's actual laid-out bounds vs the viewport.
                // contentContainer.layout.width reflects the flex container's resolved width,
                // while contentViewport is the visible clipping area.
                var content = _toolList.contentContainer;
                var viewport = _toolList.contentViewport;

                float contentWidth = content.layout.width;
                float viewportWidth = viewport.layout.width;

                // If layout hasn't resolved yet, fall back to summing children
                if (float.IsNaN(contentWidth) || contentWidth <= 0f)
                {
                    contentWidth = 0f;
                    for (int i = 0; i < content.childCount; i++)
                    {
                        var child = content[i];
                        float w = child.resolvedStyle.width;
                        float mr = child.resolvedStyle.marginRight;
                        float ml = child.resolvedStyle.marginLeft;
                        if (!float.IsNaN(w)) contentWidth += w;
                        if (!float.IsNaN(mr)) contentWidth += mr;
                        if (!float.IsNaN(ml)) contentWidth += ml;
                    }
                }

                if (float.IsNaN(viewportWidth) || viewportWidth <= 0f)
                    viewportWidth = _toolList.layout.width;

                return Mathf.Max(0f, contentWidth - viewportWidth);
            }

            private float ClampScrollOffset(float offset)
            {
                return Mathf.Clamp(offset, 0f, MaxScrollOffset());
            }

            public void SetUnequipAction(bool show, string label, Action onUnequipRequested)
            {
                _unequipButton.clicked -= onUnequipRequested;

                _unequipButton.text = string.IsNullOrWhiteSpace(label)
                    ? "Clear Active Tool"
                    : label;

                _unequipButton.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                {
                    _unequipButton.clicked += onUnequipRequested;
                }
            }

            public void SetPaletteVisible(bool visible)
            {
                _paletteCard.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void SetEntries(
                ToolDockEntryViewModel[] entries,
                string emptyStateMessage,
                Action<string> onSelected,
                Action<string> onHovered,
                Action onHoverCleared)
            {
                _toolList.contentContainer.Clear();

                ToolDockEntryViewModel[] safeEntries = entries ?? Array.Empty<ToolDockEntryViewModel>();
                bool hasEntries = safeEntries.Length > 0;

                _emptyLabel.text = string.IsNullOrWhiteSpace(emptyStateMessage)
                    ? "No tools are defined for this package."
                    : emptyStateMessage;
                _emptyLabel.style.display = hasEntries ? DisplayStyle.None : DisplayStyle.Flex;
                _toolList.style.display = hasEntries ? DisplayStyle.Flex : DisplayStyle.None;

                for (int i = 0; i < safeEntries.Length; i++)
                {
                    ToolDockEntryViewModel entry = safeEntries[i];
                    Button chip = CreateToolChip(entry);
                    string toolId = entry.ToolId;

                    chip.clicked += () => onSelected?.Invoke(toolId);
                    chip.RegisterCallback<PointerEnterEvent>(_ => onHovered?.Invoke(toolId));
                    chip.RegisterCallback<PointerLeaveEvent>(_ => onHoverCleared?.Invoke());

                    _toolList.Add(chip);
                }
            }

            private static Button CreateToolChip(ToolDockEntryViewModel entry)
            {
                Button chip = new Button();
                chip.style.marginRight = 10f;
                chip.style.minWidth = 200f;
                chip.style.height = 82f;
                chip.style.paddingLeft = 14f;
                chip.style.paddingRight = 14f;
                chip.style.paddingTop = 8f;
                chip.style.paddingBottom = 8f;
                chip.style.whiteSpace = WhiteSpace.Normal;
                chip.style.unityTextAlign = TextAnchor.MiddleLeft;
                chip.style.fontSize = 14f;
                chip.style.borderTopLeftRadius = 10f;
                chip.style.borderTopRightRadius = 10f;
                chip.style.borderBottomLeftRadius = 10f;
                chip.style.borderBottomRightRadius = 10f;
                chip.style.borderTopWidth = 2f;
                chip.style.borderRightWidth = 2f;
                chip.style.borderBottomWidth = 2f;
                chip.style.borderLeftWidth = 2f;

                string requiredPrefix = entry.IsRequired ? "[Required] " : string.Empty;
                string equippedSuffix = entry.IsEquipped ? "\nEQUIPPED" : string.Empty;
                chip.text = $"{requiredPrefix}{entry.DisplayName}\n{entry.Category}{equippedSuffix}";

                if (entry.IsEquipped)
                {
                    chip.style.backgroundColor = new Color(0.16f, 0.45f, 0.28f, 0.95f);
                    chip.style.color = new Color(0.85f, 1f, 0.9f, 1f);
                    chip.style.borderTopColor = new Color(0.36f, 0.95f, 0.58f, 0.9f);
                    chip.style.borderRightColor = new Color(0.36f, 0.95f, 0.58f, 0.9f);
                    chip.style.borderBottomColor = new Color(0.36f, 0.95f, 0.58f, 0.9f);
                    chip.style.borderLeftColor = new Color(0.36f, 0.95f, 0.58f, 0.9f);
                }
                else if (entry.IsRequired)
                {
                    chip.style.backgroundColor = new Color(0.15f, 0.26f, 0.45f, 0.95f);
                    chip.style.color = new Color(0.85f, 0.93f, 1f, 1f);
                    chip.style.borderTopColor = new Color(0.42f, 0.82f, 1f, 0.9f);
                    chip.style.borderRightColor = new Color(0.42f, 0.82f, 1f, 0.9f);
                    chip.style.borderBottomColor = new Color(0.42f, 0.82f, 1f, 0.9f);
                    chip.style.borderLeftColor = new Color(0.42f, 0.82f, 1f, 0.9f);
                }
                else
                {
                    chip.style.backgroundColor = new Color(0.12f, 0.18f, 0.28f, 0.9f);
                    chip.style.color = new Color(0.78f, 0.86f, 0.95f, 1f);
                    chip.style.borderTopColor = new Color(0.28f, 0.36f, 0.46f, 0.85f);
                    chip.style.borderRightColor = new Color(0.28f, 0.36f, 0.46f, 0.85f);
                    chip.style.borderBottomColor = new Color(0.28f, 0.36f, 0.46f, 0.85f);
                    chip.style.borderLeftColor = new Color(0.28f, 0.36f, 0.46f, 0.85f);
                }

                return chip;
            }
        }
    }
}
