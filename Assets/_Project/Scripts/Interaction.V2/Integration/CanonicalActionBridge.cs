using OSE.Core;
using OSE.Input;
using UnityEngine;

namespace OSE.Interaction.Integration
{
    /// <summary>
    /// Bridges V2 interaction events into the existing canonical action pipeline.
    /// When the V2 orchestrator selects/grabs/releases a part, this bridge
    /// calls InputActionRouter.InjectAction() and SelectionService.Notify*()
    /// so that all existing downstream systems (PartRuntimeController, UI panels,
    /// SessionDriver, etc.) continue to receive events without modification.
    /// </summary>
    public sealed class CanonicalActionBridge
    {
        private readonly InputActionRouter _router;
        private readonly SelectionService _selectionService;

        public CanonicalActionBridge(InputActionRouter router, SelectionService selectionService)
        {
            _router = router;
            _selectionService = selectionService;
        }

        public void OnPartSelected(GameObject part)
        {
            if (_selectionService != null)
                _selectionService.NotifySelected(part);
            if (_router != null)
                _router.InjectAction(CanonicalAction.Select);
        }

        public void OnExternallyResolvedPartSelected(GameObject part)
        {
            if (_selectionService != null)
                _selectionService.NotifySelected(part);
        }

        public void OnPartInspected(GameObject part)
        {
            if (_selectionService != null)
                _selectionService.NotifyInspected(part);
            if (_router != null)
                _router.InjectAction(CanonicalAction.Inspect);
        }

        public void OnExternallyResolvedPartInspected(GameObject part)
        {
            if (_selectionService != null)
                _selectionService.NotifyInspected(part);
        }

        public void OnPartGrabbed(GameObject part)
        {
            if (_router != null)
                _router.InjectAction(CanonicalAction.Grab);
        }

        public void OnPartReleased()
        {
            if (_router != null)
                _router.InjectAction(CanonicalAction.Place);
        }

        public void OnDeselected()
        {
            if (_selectionService != null)
                _selectionService.Deselect();
            if (_router != null)
                _router.InjectAction(CanonicalAction.Cancel);
        }

        public void OnExternallyResolvedDeselected()
        {
            if (_selectionService != null)
                _selectionService.Deselect();
        }

        public void OnHintRequested()
        {
            if (_router != null)
                _router.InjectAction(CanonicalAction.RequestHint);
        }

        public void OnToolPrimaryAction()
        {
            if (_router != null)
                _router.InjectAction(CanonicalAction.ToolPrimaryAction);
        }
    }
}
