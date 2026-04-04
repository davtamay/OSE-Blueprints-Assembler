using OSE.Content;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace OSE.UI.Root
{
    /// <summary>
    /// Default implementation of <see cref="IXRGrabSetup"/> — delegates to
    /// <see cref="XRPartInteractionSetup"/> and accesses XRI SDK types directly.
    ///
    /// ADR 005: this is the ONLY file in OSE.UI that may import XRI grab types.
    /// Callers must use <see cref="IXRGrabSetup"/> resolved from <c>ServiceRegistry</c>.
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
