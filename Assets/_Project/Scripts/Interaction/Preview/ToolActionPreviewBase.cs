using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Base class for tool action previews. Provides the common lifecycle
    /// (context storage, timer reset, auto-assist accumulation) so concrete
    /// previews only supply constants, visual effects, and any input overrides.
    ///
    /// Default TickObserve: elapsed-time progress with ApplyEffects.
    /// Default TickGuided: directional drag (dot-product) with auto-assist.
    /// Override either for non-standard input (e.g. circular gesture, any-direction).
    /// </summary>
    public abstract class ToolActionPreviewBase : IToolActionPreview
    {
        protected PreviewContext _ctx;
        protected float _elapsed;
        protected float _guidedProgress;
        protected float _autoAssistTimer;

        public abstract float Duration { get; }

        protected virtual float AutoAssistDelay => 3f;
        protected virtual float AutoAssistRate => 0.5f;
        protected virtual float GuidedDragScale => 0.005f;

        public virtual void Begin(PreviewContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            _guidedProgress = 0f;
            _autoAssistTimer = 0f;
        }

        public virtual float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyEffects(t);
            return t;
        }

        /// <summary>
        /// Directional drag with auto-assist. Projects dragDelta onto the
        /// expected direction; only forward motion accumulates progress.
        /// </summary>
        public virtual float TickGuided(float deltaTime, Vector2 dragDelta, Vector2 screenPos)
        {
            Vector2 expected = GetExpectedDragDirection(_ctx);
            float dot = Vector2.Dot(dragDelta, expected);

            if (dot > 0f)
            {
                _guidedProgress += dot * GuidedDragScale;
                _autoAssistTimer = 0f;
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            if (_autoAssistTimer >= AutoAssistDelay)
                _guidedProgress += AutoAssistRate * deltaTime;

            _guidedProgress = Mathf.Clamp01(_guidedProgress);
            ApplyEffects(_guidedProgress);
            return _guidedProgress;
        }

        public abstract Vector2 GetExpectedDragDirection(PreviewContext context);
        public abstract void End(bool completed);

        protected virtual void ApplyEffects(float progress) { }
    }
}
