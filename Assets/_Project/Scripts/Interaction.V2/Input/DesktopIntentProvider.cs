using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Translates mouse + keyboard input into InteractionIntents for desktop.
    ///
    /// Input mapping:
    ///   Left-click on part     → Select / BeginDrag / ContinueDrag / EndDrag
    ///   Left-click on empty    → Select (deselect)
    ///   Left-drag on empty     → Orbit (trackpad-friendly)
    ///   Shift + left-drag      → Pan (trackpad-friendly, replaces middle-drag)
    ///   Right-drag             → Orbit
    ///   Middle-drag            → Pan
    ///   Scroll                 → Zoom (no selection) / Depth adjust (while dragging)
    ///   F key                  → Focus
    ///   Home key               → ResetView
    ///   Alt+click              → Inspect
    ///   Escape                 → Cancel
    ///
    /// Gesture classification uses commit-early: once a left-click is classified
    /// as drag (moved > threshold), it stays drag until pointer up.
    /// </summary>
    public sealed class DesktopIntentProvider : IIntentProvider
    {
        private readonly Camera _camera;
        private readonly InteractionSettings _settings;

        // Scroll via InputAction — reads from the same pipeline as UI,
        // which correctly receives touchpad scroll on Windows precision touchpads.
        // Mouse.current.scroll does NOT receive touchpad events on some drivers.
        private readonly InputAction _scrollAction;

        // Pointer tracking for commit-early gesture classification
        private bool _leftDown;
        private bool _rightDown;
        private bool _middleDown;
        private Vector2 _leftDownPos;
        private bool _leftClassifiedAsDrag;
        private GameObject _leftDownHitTarget;
        private bool _leftShiftHeldOnDown;

        // Cached UIDocument lookup to avoid per-frame FindObjectsByType.
        private static UIDocument[] _uiDocCache;
        private static int _uiDocCacheFrame = -1;

        public bool IsActive => Mouse.current != null;

        public DesktopIntentProvider(Camera camera, InteractionSettings settings)
        {
            _camera = camera;
            _settings = settings;

            // Bind to <Pointer>/scroll — works with both physical mice and touchpads.
            // The UI's InputSystemUIInputModule uses the same binding path internally.
            _scrollAction = new InputAction("V2Scroll", InputActionType.Value, "<Pointer>/scroll");
            _scrollAction.Enable();
        }

        public InteractionIntent Poll()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InteractionIntent.None;

            Vector2 pos = mouse.position.ReadValue();
            Vector2 delta = mouse.delta.ReadValue();
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI)
                overUI = IsPointerOverUIToolkit(pos);

            float scroll = _scrollAction.ReadValue<Vector2>().y;

            var keyboard = Keyboard.current;

            // ── Scroll → Zoom or Depth Adjust (always processed, even over UI) ──
            // Includes a raycast hit so the orchestrator can decide:
            //   - Scroll over selected part → depth adjust (forward/backward)
            //   - Scroll over empty / unselected → camera zoom

            if (Mathf.Abs(scroll) > 0.1f)
            {
                return new InteractionIntent(
                    InteractionIntent.Kind.Zoom,
                    pos,
                    scrollDelta: scroll * 0.02f,
                    hitTarget: Raycast(pos));
            }

            // ── Keyboard shortcuts (always processed, even over UI) ──

            if (keyboard != null)
            {
                if (keyboard.fKey.wasPressedThisFrame)
                    return new InteractionIntent(InteractionIntent.Kind.Focus, pos);

                if (keyboard.homeKey.wasPressedThisFrame)
                    return new InteractionIntent(InteractionIntent.Kind.ResetView, pos);

                if (keyboard.escapeKey.wasPressedThisFrame)
                    return new InteractionIntent(InteractionIntent.Kind.Cancel, pos);
            }

            // UI takes priority for pointer-based intents (click, drag, orbit, pan)
            if (overUI)
                return InteractionIntent.None;

            // ── Right mouse → Orbit ──

            if (mouse.rightButton.wasPressedThisFrame)
                _rightDown = true;

            if (_rightDown && mouse.rightButton.isPressed)
            {
                if (delta.sqrMagnitude > 0.01f)
                {
                    return new InteractionIntent(
                        InteractionIntent.Kind.Orbit,
                        pos,
                        screenDelta: delta);
                }
            }

            if (mouse.rightButton.wasReleasedThisFrame)
                _rightDown = false;

            // ── Middle mouse → Pan ──

            if (mouse.middleButton.wasPressedThisFrame)
                _middleDown = true;

            if (_middleDown && mouse.middleButton.isPressed)
            {
                if (delta.sqrMagnitude > 0.01f)
                {
                    return new InteractionIntent(
                        InteractionIntent.Kind.Pan,
                        pos,
                        screenDelta: delta);
                }
            }

            if (mouse.middleButton.wasReleasedThisFrame)
                _middleDown = false;

            // ── Left mouse → Select / Drag / Inspect ──

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _leftDown = true;
                _leftDownPos = pos;
                _leftClassifiedAsDrag = false;
                _leftDownHitTarget = Raycast(pos);
                _leftShiftHeldOnDown = keyboard != null && keyboard.shiftKey.isPressed;
            }

            if (_leftDown && mouse.leftButton.isPressed)
            {
                if (!_leftClassifiedAsDrag)
                {
                    float dist = Vector2.Distance(pos, _leftDownPos);
                    if (dist >= _settings.DragThresholdPixels)
                    {
                        _leftClassifiedAsDrag = true;

                        // Shift=pan, neither+part=drag
                        if (_leftDownHitTarget != null && !_leftShiftHeldOnDown)
                        {
                            return new InteractionIntent(
                                InteractionIntent.Kind.BeginDrag,
                                pos,
                                hitTarget: _leftDownHitTarget);
                        }
                    }
                }

                if (_leftClassifiedAsDrag)
                {
                    if (_leftDownHitTarget != null && !_leftShiftHeldOnDown)
                    {
                        return new InteractionIntent(
                            InteractionIntent.Kind.ContinueDrag,
                            pos,
                            screenDelta: delta,
                            hitTarget: _leftDownHitTarget);
                    }
                    else if (_leftShiftHeldOnDown)
                    {
                        // Shift + left-drag = Pan
                        return new InteractionIntent(
                            InteractionIntent.Kind.Pan,
                            pos,
                            screenDelta: delta);
                    }
                    else
                    {
                        // Left-drag on empty = orbit
                        return new InteractionIntent(
                            InteractionIntent.Kind.Orbit,
                            pos,
                            screenDelta: delta);
                    }
                }
            }

            if (mouse.leftButton.wasReleasedThisFrame && _leftDown)
            {
                _leftDown = false;

                if (_leftClassifiedAsDrag && _leftDownHitTarget != null)
                {
                    return new InteractionIntent(
                        InteractionIntent.Kind.EndDrag,
                        pos,
                        hitTarget: _leftDownHitTarget);
                }

                if (!_leftClassifiedAsDrag)
                {
                    bool altHeld = keyboard != null && keyboard.altKey.isPressed;
                    var kind = altHeld
                        ? InteractionIntent.Kind.Inspect
                        : InteractionIntent.Kind.Select;

                    return new InteractionIntent(kind, pos, hitTarget: _leftDownHitTarget);
                }

                // When drag was classified but there's no hit target (e.g., clicked on
                // tool target sphere which isn't on part layer), still emit a Select
                // so tool actions can fire.
                if (_leftClassifiedAsDrag && _leftDownHitTarget == null)
                {
                    _leftClassifiedAsDrag = false;
                    return new InteractionIntent(InteractionIntent.Kind.Select, pos, hitTarget: null);
                }

                _leftClassifiedAsDrag = false;
                _leftDownHitTarget = null;
            }

            return InteractionIntent.None;
        }

        private GameObject Raycast(Vector2 screenPos)
        {
            if (_camera == null) return null;
            Ray ray = _camera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _settings.PartLayerMask))
                return hit.rigidbody != null ? hit.rigidbody.gameObject : hit.collider.gameObject;
            return null;
        }

        /// <summary>
        /// Returns true when the pointer is over a UIToolkit VisualElement
        /// (buttons, panels, etc.) that EventSystem.IsPointerOverGameObject
        /// does not detect.
        /// </summary>
        private static bool IsPointerOverUIToolkit(Vector2 screenPosition)
        {
            int frame = Time.frameCount;
            if (_uiDocCacheFrame != frame)
            {
                _uiDocCache = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                _uiDocCacheFrame = frame;
            }

            foreach (var doc in _uiDocCache)
            {
                if (doc == null) continue;
                var root = doc.rootVisualElement;
                if (root?.panel == null) continue;

                var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);
                var picked = root.panel.Pick(panelPos);
                if (picked != null && picked != root)
                    return true;
            }

            return false;
        }
    }
}
