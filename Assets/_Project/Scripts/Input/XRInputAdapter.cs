using OSE.Core;
using UnityEngine;

namespace OSE.Input
{
    public sealed class XRInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _router;

        // XR-specific input state that complements the canonical action router.
        // Continuous values (thumbstick, hand pose) are exposed here so
        // interaction systems can poll them without bypassing the action model.

        [Header("Right Hand")]
        [SerializeField] private Transform _rightHandTransform;

        [Header("Left Hand")]
        [SerializeField] private Transform _leftHandTransform;

        private void Awake()
        {
            if (_router == null)
                _router = GetComponentInParent<InputActionRouter>();
        }

        public Vector3 RightHandPosition =>
            _rightHandTransform != null ? _rightHandTransform.position : Vector3.zero;

        public Quaternion RightHandRotation =>
            _rightHandTransform != null ? _rightHandTransform.rotation : Quaternion.identity;

        public Vector3 LeftHandPosition =>
            _leftHandTransform != null ? _leftHandTransform.position : Vector3.zero;

        public Quaternion LeftHandRotation =>
            _leftHandTransform != null ? _leftHandTransform.rotation : Quaternion.identity;
    }
}
