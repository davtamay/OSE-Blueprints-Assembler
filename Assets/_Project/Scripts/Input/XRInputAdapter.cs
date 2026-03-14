using OSE.Core;
using UnityEngine;

namespace OSE.Input
{
    public class XRInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _router;

        // XR-specific input state that complements the canonical action router.
        // Continuous values (thumbstick, hand pose) are exposed here so
        // interaction systems can poll them without bypassing the action model.

        [Header("Right Hand")]
        public Transform RightHandTransform;

        [Header("Left Hand")]
        public Transform LeftHandTransform;

        private void Awake()
        {
            if (_router == null)
                _router = GetComponentInParent<InputActionRouter>();
        }

        public Vector3 RightHandPosition =>
            RightHandTransform != null ? RightHandTransform.position : Vector3.zero;

        public Quaternion RightHandRotation =>
            RightHandTransform != null ? RightHandTransform.rotation : Quaternion.identity;

        public Vector3 LeftHandPosition =>
            LeftHandTransform != null ? LeftHandTransform.position : Vector3.zero;

        public Quaternion LeftHandRotation =>
            LeftHandTransform != null ? LeftHandTransform.rotation : Quaternion.identity;
    }
}
