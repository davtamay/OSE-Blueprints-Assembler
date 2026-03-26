using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime
{
    /// <summary>
    /// Routes step lifecycle calls to the <see cref="IStepFamilyHandler"/> registered
    /// for the active step's <see cref="StepFamily"/>.
    /// Families without a registered handler return <c>null</c>, signalling the caller
    /// to fall back to its existing inline logic.
    /// </summary>
    public sealed class StepExecutionRouter
    {
        private readonly Dictionary<StepFamily, IStepFamilyHandler> _handlers = new();

        public void Register(StepFamily family, IStepFamilyHandler handler)
        {
            _handlers[family] = handler;
            OseLog.Info($"[StepRouter] Registered handler for {family}: {handler.GetType().Name}");
        }

        /// <summary>
        /// Returns the handler for <paramref name="family"/>, or <c>null</c> if none is registered.
        /// </summary>
        public IStepFamilyHandler GetHandler(StepFamily family)
        {
            _handlers.TryGetValue(family, out var handler);
            return handler;
        }

        public void OnStepActivated(in StepHandlerContext context)
        {
            if (_handlers.TryGetValue(context.Step.ResolvedFamily, out var handler))
                handler.OnStepActivated(in context);
        }

        /// <summary>
        /// Attempts to route a pointer/confirm action to the registered handler.
        /// Returns <c>true</c> if a handler consumed the action.
        /// </summary>
        public bool TryHandlePointerAction(in StepHandlerContext context)
        {
            if (_handlers.TryGetValue(context.Step.ResolvedFamily, out var handler))
                return handler.TryHandlePointerAction(in context);
            return false;
        }

        /// <summary>
        /// Attempts to route a pointer-down event to the registered handler.
        /// Returns <c>true</c> if a handler consumed the event.
        /// </summary>
        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos)
        {
            if (_handlers.TryGetValue(context.Step.ResolvedFamily, out var handler))
                return handler.TryHandlePointerDown(in context, screenPos);
            return false;
        }

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            if (_handlers.TryGetValue(context.Step.ResolvedFamily, out var handler))
                handler.Update(in context, deltaTime);
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            if (_handlers.TryGetValue(context.Step.ResolvedFamily, out var handler))
                handler.OnStepCompleted(in context);
        }

        /// <summary>
        /// Calls <see cref="IStepFamilyHandler.Cleanup"/> on every registered handler
        /// so that all families release visual artifacts (port spheres, cable previews,
        /// rendered pipes, emission pulses, etc.).
        /// </summary>
        public void CleanupAll()
        {
            foreach (var handler in _handlers.Values)
                handler.Cleanup();
        }
    }
}
