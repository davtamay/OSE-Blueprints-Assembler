using OSE.Content;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Abstracts XR Grab Interactable component setup so callers (e.g.
    /// <see cref="PackagePartSpawner"/>) are not directly coupled to the XRI SDK.
    /// Swap the registered implementation to target a different XR backend.
    /// </summary>
    internal interface IXRGrabSetup
    {
        /// <summary>
        /// Adds (or finds) an <c>XRGrabInteractable</c> on <paramref name="target"/>
        /// and applies the authored grip-point configuration.
        /// </summary>
        void EnableGrab(GameObject target, PartGrabConfig grabConfig = null);
    }

    /// <summary>
    /// Default implementation — delegates to the static <see cref="XRPartInteractionSetup"/>
    /// utility which requires the XRI SDK to be present.
    /// </summary>
    internal sealed class XRGrabSetupAdapter : IXRGrabSetup
    {
        public void EnableGrab(GameObject target, PartGrabConfig grabConfig = null)
            => XRPartInteractionSetup.TryEnableXRGrabInteractable(target, grabConfig);
    }
}
