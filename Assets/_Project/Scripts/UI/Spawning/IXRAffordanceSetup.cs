using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Abstracts XR affordance-state operations so callers are not directly coupled
    /// to the XRI Affordance System SDK types.
    ///
    /// ADR 005: this interface file is intentionally XRI-free.
    /// The only files that may import XRI affordance namespaces are:
    ///   <see cref="XRPartInteractionSetup"/> (wiring utility) and
    ///   <see cref="XRAffordanceSetupAdapter"/> (this interface's adapter).
    /// </summary>
    internal interface IXRAffordanceSetup
    {
        /// <summary>
        /// Strips <c>XRInteractableAffordanceStateProvider</c>,
        /// <c>ColorMaterialPropertyAffordanceReceiver</c>, and
        /// <c>MaterialPropertyBlockHelper</c> components from <paramref name="target"/>.
        /// Safe to call on objects that never had these components.
        /// </summary>
        void DisableColorAffordance(GameObject target);

        /// <summary>
        /// Pushes <paramref name="stateIndex"/> into the
        /// <c>XRInteractableAffordanceStateProvider</c> on <paramref name="target"/>.
        /// Returns false when no provider component is present.
        /// </summary>
        bool TryApplyAffordanceState(GameObject target, byte stateIndex, float transitionAmount = 1f);
    }

    /// <summary>
    /// Default implementation — delegates to the static
    /// <see cref="XRPartInteractionSetup"/> utility.
    /// </summary>
    internal sealed class XRAffordanceSetupAdapter : IXRAffordanceSetup
    {
        public void DisableColorAffordance(GameObject target)
            => XRPartInteractionSetup.DisablePartColorAffordance(target);

        public bool TryApplyAffordanceState(GameObject target, byte stateIndex, float transitionAmount = 1f)
            => XRPartInteractionSetup.TryApplyAffordanceState(target, stateIndex, transitionAmount);
    }
}
