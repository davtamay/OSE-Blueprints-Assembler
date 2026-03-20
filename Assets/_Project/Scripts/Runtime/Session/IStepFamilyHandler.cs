using OSE.Content;
using UnityEngine;

namespace OSE.Runtime
{
    /// <summary>
    /// Context passed to <see cref="IStepFamilyHandler"/> lifecycle methods.
    /// Provides the minimal surface needed to query step state and trigger completion.
    /// </summary>
    public readonly struct StepHandlerContext
    {
        public readonly StepDefinition Step;
        public readonly StepController StepController;
        public readonly string StepId;
        public readonly float ElapsedSeconds;

        public StepHandlerContext(StepDefinition step, StepController stepController, string stepId, float elapsedSeconds)
        {
            Step = step;
            StepController = stepController;
            StepId = stepId;
            ElapsedSeconds = elapsedSeconds;
        }
    }

    /// <summary>
    /// Per-family handler for step lifecycle events.
    /// Each <see cref="StepFamily"/> can have one handler registered with
    /// <see cref="StepExecutionRouter"/>.
    /// </summary>
    public interface IStepFamilyHandler
    {
        /// <summary>Called when the step transitions to Active (new step, not fail-retry).</summary>
        void OnStepActivated(in StepHandlerContext context);

        /// <summary>
        /// Attempts to handle a pointer/confirm action for this family.
        /// Returns <c>true</c> if the action was consumed and the caller should not
        /// process it further.
        /// </summary>
        bool TryHandlePointerAction(in StepHandlerContext context);

        /// <summary>
        /// Attempts to handle a pointer-down event for this family.
        /// Returns <c>true</c> if the event was consumed.
        /// </summary>
        bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos);

        /// <summary>Called every frame while the step is active (animation ticks, pulse updates, etc.).</summary>
        void Update(in StepHandlerContext context, float deltaTime);

        /// <summary>Called when the step transitions to Completed.</summary>
        void OnStepCompleted(in StepHandlerContext context);

        /// <summary>
        /// Unconditionally clears all visual artifacts owned by this handler.
        /// Called during step navigation and teardown so that every family
        /// gets a chance to clean up, not just the active one.
        /// </summary>
        void Cleanup();
    }
}
