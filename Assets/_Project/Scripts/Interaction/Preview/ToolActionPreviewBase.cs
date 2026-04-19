using UnityEngine;
using OSE.Content;

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

            float dragScale    = Override(Cfg?.guidedDragScale ?? 0f, GuidedDragScale);
            float assistDelay  = Override(Cfg?.autoAssistDelay ?? 0f, AutoAssistDelay);
            float assistRate   = Override(Cfg?.autoAssistRate  ?? 0f, AutoAssistRate);

            if (dot > 0f)
            {
                _guidedProgress += dot * dragScale;
                _autoAssistTimer = 0f;
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            if (_autoAssistTimer >= assistDelay)
                _guidedProgress += assistRate * deltaTime;

            _guidedProgress = Mathf.Clamp01(_guidedProgress);
            ApplyEffects(_guidedProgress);
            return _guidedProgress;
        }

        public abstract Vector2 GetExpectedDragDirection(PreviewContext context);
        public abstract void End(bool completed);

        protected virtual void ApplyEffects(float progress) { }

        /// <summary>
        /// Default overlay: no displacement. Override in concrete previews
        /// (e.g. DrillPreview vibration, CutPreview x-vibration) to contribute
        /// visual motion that composes cleanly with the controller's
        /// follow-part offset when a <see cref="IPartEffect"/> is active.
        /// See <see cref="IToolActionPreview.ComputeOverlayOffset"/> for the
        /// full contract.
        /// </summary>
        public virtual Vector3 ComputeOverlayOffset(float progress) => Vector3.zero;

        // ── Per-action override helpers ──────────────────────────────────
        //
        // Each preview class authors its hardcoded constants (Duration,
        // wobble amp, spark thresholds, colours, etc.) as fallbacks. When a
        // ToolActionDefinition carries a ToolActionPreviewConfig, authored
        // values override the defaults. Sentinel convention: 0 for floats,
        // alpha=0 for colours → "use fallback". Matches the Phase-1/2
        // convention (particleScale==0 → 1.0, etc.) and avoids needing an
        // explicit per-field "override?" boolean.

        /// <summary>Returns <paramref name="authored"/> when &gt; 0, else <paramref name="fallback"/>.</summary>
        protected static float Override(float authored, float fallback)
            => authored > 0f ? authored : fallback;

        /// <summary>Returns <paramref name="authored"/> when alpha &gt; 0, else <paramref name="fallback"/>.</summary>
        protected static Color Override(SceneFloat4 authored, Color fallback)
            => authored.a > 0f
                ? new Color(authored.r, authored.g, authored.b, authored.a)
                : fallback;

        /// <summary>Access the config on the active context, or null when no action is associated.</summary>
        protected ToolActionPreviewConfig Cfg => _ctx.PreviewConfig;
    }
}
