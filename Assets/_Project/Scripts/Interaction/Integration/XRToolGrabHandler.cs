using OSE.Content;
using OSE.Core;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace OSE.Interaction
{
    /// <summary>
    /// Configures an <see cref="XRGrabInteractable"/> on a tool preview using
    /// <see cref="ToolPoseConfig"/> spatial metadata.
    ///
    /// Leverages XRI 3.3 built-in features:
    ///   - <c>attachTransform</c>: positioned at <c>toolPose.gripPoint</c> so the hand
    ///     grabs at the correct location on the tool model.
    ///   - <c>secondaryAttachTransform</c>: positioned at <c>toolPose.tipPoint</c>
    ///     for two-handed tools (<c>poseHint == "two_hand"</c>).
    ///   - <c>useDynamicAttach</c>: disabled — we want the authored grip, not wherever
    ///     the hand happened to touch.
    ///   - <c>attachEaseInTime</c>: smooth snap so the tool doesn't teleport.
    ///   - <c>movementType</c>: Instantaneous for hand tracking (lowest latency),
    ///     VelocityTracking for controllers (feels more physical).
    ///
    /// Two visual modes controlled by <see cref="SetControllerMode"/>:
    ///   - **Hand tracking**: tool renders at full cursor alpha — hand mesh wraps
    ///     around it naturally via XR Hands skeleton driver.
    ///   - **Controller**: tool renders semi-transparent so the controller model
    ///     remains visible through it.
    /// </summary>
    public sealed class XRToolGrabHandler : MonoBehaviour
    {
        private const float ControllerAlpha = 0.35f;
        private const float HandAlpha = 0.85f;
        private const float AttachEaseInSeconds = 0.12f;

        private XRGrabInteractable _grabInteractable;
        private Transform _gripAttach;
        private Transform _tipAttach;
        private ToolPoseConfig _toolPose;
        private bool _isControllerMode;
        private bool _isGrabbed;

        /// <summary>True while an XR interactor is holding this tool.</summary>
        public bool IsGrabbed => _isGrabbed;

        /// <summary>
        /// Configures the tool preview for XR grab interaction.
        /// Call once after the preview is instantiated.
        /// </summary>
        public void Setup(ToolPoseConfig toolPose, bool isControllerMode)
        {
            _toolPose = toolPose;
            _isControllerMode = isControllerMode;

            EnsureRigidbody();
            CreateAttachTransforms();
            ConfigureGrabInteractable();
            ApplyVisualMode();
        }

        /// <summary>
        /// Switches between controller mode (faded) and hand mode (full alpha).
        /// Also switches movement type: Instantaneous for hands, VelocityTracking
        /// for controllers.
        /// </summary>
        public void SetControllerMode(bool isController)
        {
            if (_isControllerMode == isController) return;
            _isControllerMode = isController;

            if (_grabInteractable != null)
            {
                _grabInteractable.movementType = isController
                    ? XRBaseInteractable.MovementType.VelocityTracking
                    : XRBaseInteractable.MovementType.Instantaneous;
            }

            ApplyVisualMode();
        }

        private void OnDestroy()
        {
            if (_gripAttach != null) Destroy(_gripAttach.gameObject);
            if (_tipAttach != null) Destroy(_tipAttach.gameObject);
        }

        private void EnsureRigidbody()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = true;
            rb.useGravity = false;
        }

        private void CreateAttachTransforms()
        {
            // Primary attach: grip point — where the dominant hand grabs
            _gripAttach = CreateChildTransform("GripAttach");
            if (_toolPose != null && _toolPose.HasGripPoint)
            {
                _gripAttach.localPosition = _toolPose.GetGripPoint();
                if (_toolPose.HasGripRotation)
                    _gripAttach.localRotation = _toolPose.GetGripRotation();
                // gripRotation is model-local — correct for XR attach transform
            }

            // Secondary attach: tip point — for two-handed tools (poseHint == "two_hand")
            // XRI uses this when a second interactor grabs while already held
            bool isTwoHanded = _toolPose != null
                && !string.IsNullOrEmpty(_toolPose.poseHint)
                && _toolPose.poseHint == "two_hand";

            if (isTwoHanded && _toolPose.HasTipPoint)
            {
                _tipAttach = CreateChildTransform("TipAttach");
                _tipAttach.localPosition = _toolPose.GetTipPoint();
            }
        }

        private Transform CreateChildTransform(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void ConfigureGrabInteractable()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            if (_grabInteractable == null)
                _grabInteractable = gameObject.AddComponent<XRGrabInteractable>();

            // Grip point from toolPose drives the attach — don't let XRI override
            // with wherever the hand happened to touch the mesh
            _grabInteractable.useDynamicAttach = false;
            _grabInteractable.attachTransform = _gripAttach;

            if (_tipAttach != null)
            {
                _grabInteractable.secondaryAttachTransform = _tipAttach;
                // Allow a second hand to grab for two-handed tools
                _grabInteractable.selectMode = InteractableSelectMode.Multiple;
            }

            // Smooth snap — tool eases to the hand rather than teleporting
            _grabInteractable.attachEaseInTime = AttachEaseInSeconds;

            // Movement: Instantaneous for hand tracking (minimal latency),
            // VelocityTracking for controllers (feels heavier/more physical)
            _grabInteractable.movementType = _isControllerMode
                ? XRBaseInteractable.MovementType.VelocityTracking
                : XRBaseInteractable.MovementType.Instantaneous;

            // Don't throw tools when released — they return to cursor or holster
            _grabInteractable.throwOnDetach = false;
            _grabInteractable.retainTransformParent = false;

            // Position/rotation tracking with gentle smoothing for controllers
            _grabInteractable.trackPosition = true;
            _grabInteractable.trackRotation = true;
            _grabInteractable.smoothPosition = _isControllerMode;
            _grabInteractable.smoothRotation = _isControllerMode;
            _grabInteractable.smoothPositionAmount = 8f;
            _grabInteractable.smoothRotationAmount = 6f;

            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            _isGrabbed = true;

            // Identify which hand/controller grabbed for handedness-aware feedback
            string interactorName = args.interactorObject?.transform.name ?? "unknown";
            OseLog.Info($"[XRToolGrab] Grabbed '{gameObject.name}' by {interactorName}");
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            _isGrabbed = false;
            OseLog.Info($"[XRToolGrab] Released '{gameObject.name}'");
        }

        private void ApplyVisualMode()
        {
            float alpha = _isControllerMode ? ControllerAlpha : HandAlpha;
            MaterialHelper.MakeTransparent(gameObject, alpha);
        }
    }
}
