using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Fallback preview for profiles without a specific implementation.
    /// Brief glow on the tool preview, then auto-completes.
    /// </summary>
    public sealed class DefaultPreview : ToolActionPreviewBase
    {
        public override float Duration => 0.6f;

        protected override float AutoAssistDelay => 2f;
        protected override float AutoAssistRate => 1f;
        protected override float GuidedDragScale => 0.003f;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);

            // Brief emission pulse on the preview
            if (context.ToolPreview != null)
                MaterialHelper.SetEmission(context.ToolPreview, new Color(0.3f, 1f, 0.6f, 1f));
        }

        public override float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            return Mathf.Clamp01(_elapsed / Duration);
        }

        /// <summary>Any drag in any direction counts — no directional constraint.</summary>
        public override float TickGuided(float deltaTime, Vector2 dragDelta, Vector2 screenPos)
        {
            float magnitude = dragDelta.magnitude;
            if (magnitude > 0.5f)
            {
                _guidedProgress += magnitude * GuidedDragScale;
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

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            return context.ProjectDirectionToScreen(
                context.WeldAxis.sqrMagnitude > 0.001f ? context.WeldAxis : Vector3.up,
                Vector2.up);
        }

        public override void End(bool completed)
        {
            if (_ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);
        }
    }
}
