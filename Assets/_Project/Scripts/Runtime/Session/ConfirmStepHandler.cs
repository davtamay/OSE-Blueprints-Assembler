using OSE.App;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime
{
    /// <summary>
    /// Handles <see cref="Content.StepFamily.Confirm"/> steps.
    /// Confirm steps have no visual setup or cleanup — the only action
    /// is completing the step when the user presses Confirm.
    /// </summary>
    public sealed class ConfirmStepHandler : IStepFamilyHandler
    {
        public void OnStepActivated(in StepHandlerContext context)
        {
            // Confirm steps have no visual setup.
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
            // Confirm steps have no per-frame work.
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            // Confirm steps have no cleanup.
        }

        public void Cleanup()
        {
            // Confirm steps have no visual artifacts.
        }
    }
}
