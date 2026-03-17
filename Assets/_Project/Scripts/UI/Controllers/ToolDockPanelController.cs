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

        protected override string PanelName => "ose-tool-dock-panel";

        protected override VisualElement CreateView() => new ToolDockPanelView();

        protected override void CacheView(VisualElement root)
        {
            _view = (ToolDockPanelView)root;
            _view.ToggleButton.clicked += HandleToggleClicked;
        }

        protected override void ApplyViewModel(ToolDockPanelViewModel viewModel)
        {
            _view.ToggleButton.text = viewModel.ToggleLabel;
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

            private readonly VisualElement _topActionRow;
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

            public ToolDockPanelView()
            {
                pickingMode = PickingMode.Position;
                style.width = 520f;
                style.maxWidth = 620f;
                style.alignItems = Align.Center;
                style.flexDirection = FlexDirection.Column;
                style.marginBottom = 6f;

                _topActionRow = new VisualElement();
                _topActionRow.style.flexDirection = FlexDirection.Row;
                _topActionRow.style.alignItems = Align.Center;
                _topActionRow.style.justifyContent = Justify.Center;
                Add(_topActionRow);

                ToggleButton = new Button();
                ToggleButton.text = "Tools";
                ToggleButton.style.height = 42f;
                ToggleButton.style.minWidth = 180f;
                ToggleButton.style.paddingLeft = 18f;
                ToggleButton.style.paddingRight = 18f;
                ToggleButton.style.fontSize = 14f;
                ToggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                ToggleButton.style.backgroundColor = new Color(0.12f, 0.23f, 0.38f, 0.95f);
                ToggleButton.style.color = new Color(0.82f, 0.92f, 1f, 1f);
                ToggleButton.style.borderTopLeftRadius = 12f;
                ToggleButton.style.borderTopRightRadius = 12f;
                ToggleButton.style.borderBottomLeftRadius = 12f;
                ToggleButton.style.borderBottomRightRadius = 12f;
                _topActionRow.Add(ToggleButton);

                _unequipButton = new Button();
                _unequipButton.text = "Clear Active Tool";
                _unequipButton.style.height = 42f;
                _unequipButton.style.minWidth = 180f;
                _unequipButton.style.paddingLeft = 16f;
                _unequipButton.style.paddingRight = 16f;
                _unequipButton.style.fontSize = 13f;
                _unequipButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                _unequipButton.style.backgroundColor = new Color(0.29f, 0.17f, 0.17f, 0.92f);
                _unequipButton.style.color = new Color(1f, 0.9f, 0.9f, 1f);
                _unequipButton.style.borderTopLeftRadius = 12f;
                _unequipButton.style.borderTopRightRadius = 12f;
                _unequipButton.style.borderBottomLeftRadius = 12f;
                _unequipButton.style.borderBottomRightRadius = 12f;
                _unequipButton.style.marginLeft = 10f;
                _unequipButton.style.display = DisplayStyle.None;
                _topActionRow.Add(_unequipButton);

                _paletteCard = new VisualElement();
                UIToolkitStyleUtility.ApplyPanelSurface(_paletteCard);
                _paletteCard.style.width = 520f;
                _paletteCard.style.maxWidth = 620f;
                _paletteCard.style.marginTop = 8f;
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

                _emptyLabel = UIToolkitStyleUtility.CreateBodyLabel("No tools are defined for this package.");
                _emptyLabel.style.marginTop = 4f;
                _emptyLabel.style.marginBottom = 2f;
                _paletteCard.Add(_emptyLabel);

                _toolList = new ScrollView(ScrollViewMode.Horizontal);
                _toolList.style.maxHeight = 100f;
                _toolList.style.flexDirection = FlexDirection.Row;
                _toolList.style.marginTop = 2f;
                _toolList.style.marginBottom = 0f;
                // Hide scrollbars — dragging is the only scroll mechanism
                _toolList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                _toolList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                _toolList.pickingMode = PickingMode.Position;
                // Clamped elastic prevents the built-in scroll from fighting with our drag handler
                _toolList.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                _paletteCard.Add(_toolList);

                // Wire up drag-to-scroll on the tool list
                _toolList.RegisterCallback<PointerDownEvent>(OnToolListPointerDown);
                _toolList.RegisterCallback<PointerMoveEvent>(OnToolListPointerMove);
                _toolList.RegisterCallback<PointerUpEvent>(OnToolListPointerUp);
                _toolList.RegisterCallback<PointerCaptureOutEvent>(OnToolListPointerCaptureOut);
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
