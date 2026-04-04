using OSE.Content;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace OSE.UI.Root
{
    /// <summary>
    /// Abstracts XR Grab Interactable component setup so callers (e.g.
    /// <see cref="PackagePartSpawner"/>, <see cref="PartInteractionBridge"/>,
    /// <see cref="PartVisualFeedbackManager"/>) are not directly coupled to the XRI SDK.
    /// Swap the registered implementation to target a different XR backend.
    ///
    /// ADR 005: all XRI-specific logic lives in this interface's implementation.
    /// UI callers must not import XRI SDK types directly.
    /// </summary>
    internal interface IXRGrabSetup
    {
        /// <summary>
        /// Adds (or finds) an <c>XRGrabInteractable</c> on <paramref name="target"/>
        /// and applies the authored grip-point configuration.
        /// </summary>
        void EnableGrab(GameObject target, PartGrabConfig grabConfig = null);

        /// <summary>
        /// Returns true when the XR grab interactable on <paramref name="target"/>
        /// is currently hovered by a controller ray. Returns false if no interactable exists.
        /// </summary>
        bool IsHovered(GameObject target);

        /// <summary>
        /// Enables or disables the XR grab interactable on <paramref name="target"/>.
        /// Resets Rigidbody to kinematic + no-gravity when disabling.
        /// No-op if no interactable component is present.
        /// </summary>
        void SetGrabEnabled(GameObject target, bool enabled);
    }

    /// <summary>
    /// Default implementation — delegates to the static <see cref="XRPartInteractionSetup"/>
    /// utility which requires the XRI SDK to be present.
    /// </summary>
    internal sealed class XRGrabSetupAdapter : IXRGrabSetup
    {
        public void EnableGrab(GameObject target, PartGrabConfig grabConfig = null)
            => XRPartInteractionSetup.TryEnableXRGrabInteractable(target, grabConfig);

        public bool IsHovered(GameObject target)
        {
            if (target == null) return false;
            var grab = target.GetComponent<XRGrabInteractable>();
            return grab != null && grab.isHovered;
        }

        public void SetGrabEnabled(GameObject target, bool enabled)
        {
            if (target == null) return;
            var grab = target.GetComponent<XRGrabInteractable>();
            if (grab == null || grab.enabled == enabled) return;

            grab.enabled = enabled;

            var rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity  = false;
            }
        }
    }
}
