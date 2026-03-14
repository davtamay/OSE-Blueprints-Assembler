using OSE.Core;
using OSE.Input;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace OSE.Interaction
{
    /// <summary>
    /// Routes XRI interactor events into the canonical OSE action model.
    /// Attach alongside an XRI interactor. Never call runtime logic directly
    /// from here — always dispatch through IInputRouter.
    ///
    /// Note: Confirm/Activate is handled by the Input System path
    /// (XRControllers control scheme in OSEInputActions) — the XRI 3.x
    /// activate model does not expose a public event on the interactor side.
    /// </summary>
    public class XRIInteractionAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _actionRouter;

        private IXRSelectInteractor _selectInteractor;

        private void Awake()
        {
            _selectInteractor = GetComponent<IXRSelectInteractor>();

            if (_actionRouter == null)
                _actionRouter = FindFirstObjectByType<InputActionRouter>();

            if (_actionRouter == null)
                OseLog.Warn("[XRIInteractionAdapter] No InputActionRouter found in scene.");
        }

        private void OnEnable()
        {
            if (_selectInteractor is XRBaseInteractor baseInteractor)
            {
                baseInteractor.selectEntered.AddListener(OnSelectEntered);
                baseInteractor.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            if (_selectInteractor is XRBaseInteractor baseInteractor)
            {
                baseInteractor.selectEntered.RemoveListener(OnSelectEntered);
                baseInteractor.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args) =>
            DispatchToRouter(CanonicalAction.Grab);

        private void OnSelectExited(SelectExitEventArgs args) =>
            DispatchToRouter(CanonicalAction.Place);

        private void DispatchToRouter(CanonicalAction action)
        {
            // XRI events are forwarded as canonical actions via InjectAction,
            // ensuring the runtime never has a direct XRI dependency.
            _actionRouter?.InjectAction(action);
        }
    }
}
