using OSE.Content;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Abstracts XR Grab Interactable component setup so callers
    /// (<see cref="PackagePartSpawner"/>, <see cref="PartInteractionBridge"/>,
    /// <see cref="PartVisualFeedbackManager"/>) are not directly coupled to the XRI SDK.
    /// Swap the registered implementation to target a different XR backend.
    ///
    /// ADR 005: this interface file is intentionally XRI-free.
    /// All XRI-specific logic lives in <see cref="XRGrabSetupAdapter"/>.
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
}
