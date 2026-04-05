using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// XR grab proxy attached to the reposition handle disc.
    /// When grabbed by an XR controller, translates PreviewRoot on XZ
    /// and rotates around Y to follow controller movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RepositionGrabProxy : MonoBehaviour
    {
        private Transform _previewRoot;
        private bool _isGrabbed;
        private Vector3 _grabOffset;
        private float _initialControllerYaw;
        private float _initialRootYaw;
        private float _originalY;

        public void Initialize(Transform previewRoot)
        {
            _previewRoot = previewRoot;
            _originalY = previewRoot.position.y;
        }

        /// <summary>Called by XRGrabInteractable selectEntered event.</summary>
        public void OnGrabbed(Transform controllerTransform)
        {
            if (_previewRoot == null) return;

            _isGrabbed = true;
            Vector3 controllerPosXZ = controllerTransform.position;
            controllerPosXZ.y = _originalY;
            _grabOffset = _previewRoot.position - controllerPosXZ;
            _initialControllerYaw = controllerTransform.eulerAngles.y;
            _initialRootYaw = _previewRoot.eulerAngles.y;
        }

        /// <summary>Called by XRGrabInteractable selectExited event.</summary>
        public void OnReleased()
        {
            _isGrabbed = false;
        }

        private void Update()
        {
            if (!_isGrabbed || _previewRoot == null)
                return;

            // Find the grabbing controller — use parent since XRGrabInteractable
            // reparents the object under the interactor
            Transform controller = transform.parent;
            if (controller == null) return;

            // Translate on XZ, clamp Y
            Vector3 targetPos = controller.position + _grabOffset;
            targetPos.y = _originalY;
            _previewRoot.position = targetPos;

            // Rotate around Y based on controller yaw delta
            float currentControllerYaw = controller.eulerAngles.y;
            float yawDelta = currentControllerYaw - _initialControllerYaw;
            _previewRoot.rotation = Quaternion.Euler(0f, _initialRootYaw + yawDelta, 0f);
        }
    }
}
