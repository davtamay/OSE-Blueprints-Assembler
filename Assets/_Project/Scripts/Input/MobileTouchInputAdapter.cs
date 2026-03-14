using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace OSE.Input
{
    public class MobileTouchInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _router;

        [Header("Pinch Zoom")]
        [SerializeField] private float _pinchZoomSensitivity = 0.01f;

        private float _lastPinchDistance;

        private void Awake()
        {
            if (_router == null)
                _router = GetComponentInParent<InputActionRouter>();
        }

        private void OnEnable()  => EnhancedTouchSupport.Enable();
        private void OnDisable() => EnhancedTouchSupport.Disable();

        public float GetPinchZoomDelta()
        {
            if (Touch.activeTouches.Count < 2)
            {
                _lastPinchDistance = 0f;
                return 0f;
            }

            var touch0 = Touch.activeTouches[0].screenPosition;
            var touch1 = Touch.activeTouches[1].screenPosition;
            float currentDistance = Vector2.Distance(touch0, touch1);

            if (_lastPinchDistance == 0f)
            {
                _lastPinchDistance = currentDistance;
                return 0f;
            }

            float delta = (currentDistance - _lastPinchDistance) * _pinchZoomSensitivity;
            _lastPinchDistance = currentDistance;
            return delta;
        }
    }
}
