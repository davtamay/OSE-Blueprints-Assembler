using OSE.UI.Root;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Fallback preview for profiles without a specific implementation.
    /// Brief glow on the tool ghost, then auto-completes.
    /// </summary>
    public sealed class DefaultPreview : IToolActionPreview
    {
        public float Duration => 0.6f;

        private PreviewContext _ctx;
        private float _elapsed;
        private float _guidedProgress;
        private float _autoAssistTimer;

        private const float AutoAssistDelay = 2f;
        private const float AutoAssistRate = 1f;

        public void Begin(PreviewContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            _guidedProgress = 0f;
            _autoAssistTimer = 0f;

            // Brief emission pulse on the ghost
            if (context.ToolGhost != null)
                MaterialHelper.SetEmission(context.ToolGhost, new Color(0.3f, 1f, 0.6f, 1f));
        }

        public float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            return Mathf.Clamp01(_elapsed / Duration);
        }

        public float TickGuided(float deltaTime, Vector2 dragDelta)
        {
            // Any drag in any direction counts
            float magnitude = dragDelta.magnitude;
            if (magnitude > 0.5f)
            {
                _guidedProgress += magnitude * 0.003f;
                _autoAssistTimer = 0f;
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            if (_autoAssistTimer >= AutoAssistDelay)
                _guidedProgress += AutoAssistRate * deltaTime;

            _guidedProgress = Mathf.Clamp01(_guidedProgress);
            return _guidedProgress;
        }

        public Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            return Vector2.up; // generic upward
        }

        public void End(bool completed)
        {
            // Clear emission
            if (_ctx.ToolGhost != null)
                MaterialHelper.SetEmission(_ctx.ToolGhost, Color.black);
        }
    }
}
