using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Translates touch input into InteractionIntents for mobile.
    ///
    /// Gesture classification:
    ///   - Tap on part      → Select
    ///   - Tap on empty     → Select (deselect)
    ///   - 1-finger drag on part  → Drag part
    ///   - 1-finger drag on empty → Orbit camera
    ///   - 2-finger drag    → Pan camera (always, regardless of what's under fingers)
    ///   - Pinch             → Zoom camera (or depth adjust while dragging part)
    ///   - Long-press        → Inspect
    ///   - Double-tap        → Focus / ResetView
    ///
    /// Commit-early: gesture is classified on touch-down (raycast) and locked
    /// for the entire gesture duration. Two-finger input always overrides to camera.
    /// </summary>
    public sealed class MobileIntentProvider : IIntentProvider
    {
        private readonly Camera _camera;
        private readonly InteractionSettings _settings;

        // Gesture tracking
        private bool _touchActive;
        private Vector2 _touchDownPos;
        private float _touchDownTime;
        private bool _classifiedAsDrag;
        private bool _wasOnPart;
        private GameObject _touchDownHitTarget;
        private int _previousTouchCount;

        // Double-tap detection
        private float _lastTapTime;
        private Vector2 _lastTapPos;

        // Pinch tracking
        private float _previousPinchDistance;

        public bool IsActive => Touchscreen.current != null;

        public MobileIntentProvider(Camera camera, InteractionSettings settings)
        {
            _camera = camera;
            _settings = settings;

            // Enable EnhancedTouch for reliable multi-touch
            if (!EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
        }

        public InteractionIntent Poll()
        {
            int touchCount = Touch.activeTouches.Count;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (overUI || touchCount == 0)
            {
                if (_touchActive && touchCount == 0)
                    return HandleTouchEnd();

                _previousTouchCount = touchCount;
                return InteractionIntent.None;
            }

            // ── Two-finger gestures always = camera ──

            if (touchCount >= 2)
            {
                // If we were dragging a part with one finger and user adds second finger,
                // cancel the part drag and switch to camera.
                if (_touchActive && _wasOnPart && _classifiedAsDrag)
                {
                    _wasOnPart = false; // Reclassify as camera
                }

                var intent = HandleTwoFingerGesture();
                _previousTouchCount = touchCount;
                return intent;
            }

            // ── Single finger ──

            var touch0 = Touch.activeTouches[0];
            Vector2 pos = touch0.screenPosition;
            Vector2 delta = touch0.delta;

            // Touch began
            if (!_touchActive && touch0.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                _touchActive = true;
                _touchDownPos = pos;
                _touchDownTime = Time.unscaledTime;
                _classifiedAsDrag = false;
                _touchDownHitTarget = Raycast(pos);
                _wasOnPart = _touchDownHitTarget != null;
                _previousTouchCount = touchCount;
                return InteractionIntent.None; // Wait for classification
            }

            if (!_touchActive)
            {
                _previousTouchCount = touchCount;
                return InteractionIntent.None;
            }

            // Touch moved — classify gesture
            if (touch0.phase == UnityEngine.InputSystem.TouchPhase.Moved ||
                touch0.phase == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                // Check for long-press (inspect)
                if (!_classifiedAsDrag)
                {
                    float holdDuration = Time.unscaledTime - _touchDownTime;
                    float movedDist = Vector2.Distance(pos, _touchDownPos);

                    if (holdDuration >= _settings.LongPressDuration && movedDist < _settings.DragThresholdPixels)
                    {
                        _touchActive = false;
                        return new InteractionIntent(
                            InteractionIntent.Kind.Inspect,
                            pos,
                            hitTarget: _touchDownHitTarget);
                    }

                    if (movedDist >= _settings.DragThresholdPixels)
                    {
                        _classifiedAsDrag = true;

                        if (_wasOnPart)
                        {
                            return new InteractionIntent(
                                InteractionIntent.Kind.BeginDrag,
                                pos,
                                hitTarget: _touchDownHitTarget);
                        }
                    }
                }

                if (_classifiedAsDrag)
                {
                    if (_wasOnPart)
                    {
                        return new InteractionIntent(
                            InteractionIntent.Kind.ContinueDrag,
                            pos,
                            screenDelta: delta,
                            hitTarget: _touchDownHitTarget);
                    }
                    else
                    {
                        // Single-finger on empty = orbit
                        return new InteractionIntent(
                            InteractionIntent.Kind.Orbit,
                            pos,
                            screenDelta: delta);
                    }
                }
            }

            // Touch ended
            if (touch0.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                touch0.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                return HandleTouchEnd();
            }

            _previousTouchCount = touchCount;
            return InteractionIntent.None;
        }

        private InteractionIntent HandleTouchEnd()
        {
            if (!_touchActive)
                return InteractionIntent.None;

            _touchActive = false;
            Vector2 pos = _touchDownPos;
            if (Touch.activeTouches.Count > 0)
                pos = Touch.activeTouches[0].screenPosition;

            if (_classifiedAsDrag && _wasOnPart)
            {
                _classifiedAsDrag = false;
                return new InteractionIntent(
                    InteractionIntent.Kind.EndDrag,
                    pos,
                    hitTarget: _touchDownHitTarget);
            }

            if (!_classifiedAsDrag)
            {
                // Tap — check for double-tap
                float now = Time.unscaledTime;
                float dist = Vector2.Distance(pos, _lastTapPos);

                if (now - _lastTapTime < _settings.DoubleTapWindow && dist < 50f)
                {
                    _lastTapTime = 0f;
                    // Double-tap = Focus (if on part) or ResetView (if on empty)
                    var kind = _touchDownHitTarget != null
                        ? InteractionIntent.Kind.Focus
                        : InteractionIntent.Kind.ResetView;
                    return new InteractionIntent(kind, pos, hitTarget: _touchDownHitTarget);
                }

                _lastTapTime = now;
                _lastTapPos = pos;

                return new InteractionIntent(
                    InteractionIntent.Kind.Select,
                    pos,
                    hitTarget: _touchDownHitTarget);
            }

            return InteractionIntent.None;
        }

        private InteractionIntent HandleTwoFingerGesture()
        {
            if (Touch.activeTouches.Count < 2)
                return InteractionIntent.None;

            var t0 = Touch.activeTouches[0];
            var t1 = Touch.activeTouches[1];
            Vector2 pos = (t0.screenPosition + t1.screenPosition) * 0.5f;

            // Pinch distance
            float currentPinchDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
            float pinchDelta = 0f;

            if (_previousTouchCount >= 2 && _previousPinchDistance > 0f)
            {
                pinchDelta = (currentPinchDist - _previousPinchDistance) * 0.005f;
            }
            _previousPinchDistance = currentPinchDist;

            // If pinch is dominant, zoom
            if (Mathf.Abs(pinchDelta) > 0.001f)
            {
                return new InteractionIntent(
                    InteractionIntent.Kind.Zoom,
                    pos,
                    pinchDelta: pinchDelta);
            }

            // Otherwise two-finger drag = pan
            Vector2 avgDelta = (t0.delta + t1.delta) * 0.5f;
            if (avgDelta.sqrMagnitude > 0.01f)
            {
                return new InteractionIntent(
                    InteractionIntent.Kind.Pan,
                    pos,
                    screenDelta: avgDelta);
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
    }
}
