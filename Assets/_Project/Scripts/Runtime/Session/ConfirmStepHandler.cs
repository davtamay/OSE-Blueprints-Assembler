using OSE.App;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime
{
    /// <summary>
    /// Handles <see cref="Content.StepFamily.Confirm"/> steps.
    ///
    /// When the step has <c>targetIds</c>, an observe-phase runs first:
    /// inspection markers are spawned at each target location and the camera
    /// frustum is tested each frame via <see cref="IConfirmInspectionService"/>.
    /// Once all locations are framed, <see cref="ObserveTargetsCompleted"/> is
    /// published and the UI layer unlocks the Confirm button.
    ///
    /// Pure Confirm steps (no targetIds) complete on button press immediately.
    /// </summary>
    public sealed class ConfirmStepHandler : IStepFamilyHandler
    {
        public void OnStepActivated(in StepHandlerContext context)
        {
            if (ServiceRegistry.TryGet<IConfirmInspectionService>(out var svc))
                svc.ShowMarkersForStep(context.Step);
        }

        public bool TryHandlePointerAction(in StepHandlerContext context)
        {
            if (!context.Step.IsConfirmation)
                return false;

            OseLog.VerboseInfo("[ConfirmHandler] Completing confirmation step.");
            if (ServiceRegistry.TryGet<IEffectPlayer>(out var fx))
                fx.PlayHaptic(EffectRole.HapticFeedback);
            context.StepController.CompleteStep(context.ElapsedSeconds);
            return true;
        }

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos)
        {
            return false;
        }

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            if (ServiceRegistry.TryGet<IConfirmInspectionService>(out var svc))
                svc.UpdateObservations();
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            // Clear markers immediately on step completion so they don't persist
            // into the next step. Cleanup() is only called during navigation, not
            // on normal step completion, so we must clear here as well.
            if (ServiceRegistry.TryGet<IConfirmInspectionService>(out var svc))
                svc.ClearMarkers();
        }

        public void Cleanup()
        {
            if (ServiceRegistry.TryGet<IConfirmInspectionService>(out var svc))
                svc.ClearMarkers();
        }
    }
}
