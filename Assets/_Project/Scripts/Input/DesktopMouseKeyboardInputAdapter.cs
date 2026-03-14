using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.Input
{
    public class DesktopMouseKeyboardInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _router;

        [Header("Orbit")]
        [SerializeField] private bool _requireRightMouseForOrbit = true;

        private InputAction _orbitAction;
        private bool _rightMouseHeld;

        private void Awake()
        {
            if (_router == null)
                _router = GetComponentInParent<InputActionRouter>();
        }

        private void Update()
        {
            if (_requireRightMouseForOrbit)
                _rightMouseHeld = Mouse.current != null && Mouse.current.rightButton.isPressed;
        }

        public bool IsOrbitActive => !_requireRightMouseForOrbit || _rightMouseHeld;

        public Vector2 GetOrbitDelta()
        {
            if (Mouse.current == null || !IsOrbitActive) return Vector2.zero;
            return Mouse.current.delta.ReadValue();
        }

        public float GetZoomDelta()
        {
            if (Mouse.current == null) return 0f;
            return Mouse.current.scroll.ReadValue().y * 0.1f;
        }
    }
}
