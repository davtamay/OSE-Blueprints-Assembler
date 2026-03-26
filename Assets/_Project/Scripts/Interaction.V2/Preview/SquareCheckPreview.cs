using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Square-check preview: framing square settles into a corner joint,
    /// both arms press flush against the bars, then a green confirmation
    /// glow pulses to indicate the joint is square.
    ///
    /// Observe mode: auto-play settle + confirm over Duration seconds.
    /// Guided mode: user drag presses the square into the corner (progress = contact).
    /// </summary>
    public sealed class SquareCheckPreview : ToolActionPreviewBase
    {
        public override float Duration => 1.2f;

        protected override float GuidedDragScale => 0.006f;
        protected override float AutoAssistDelay => 3f;
        protected override float AutoAssistRate => 0.5f;

        private Vector3 _settledPos;
        private Vector3 _hoverPos;

        private const float SettleDistance = 0.015f;

        // Phase thresholds within 0..1 progress
        private const float SettleEnd = 0.4f;
        private const float HoldEnd = 0.7f;

        private bool _glowStarted;
        private bool _confirmSpawned;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);
            _glowStarted = false;
            _confirmSpawned = false;

            if (context.ToolPreview != null)
            {
                _hoverPos = context.ToolPreview.transform.position;
                Vector3 toSurface = (context.TargetWorldPos - _hoverPos).normalized;
                _settledPos = _hoverPos + toSurface * SettleDistance;
            }
            else
            {
                _hoverPos = context.TargetWorldPos;
                _settledPos = context.TargetWorldPos;
            }
        }

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            Vector3 pushDir = (context.TargetWorldPos - _hoverPos).normalized;
            return context.ProjectDirectionToScreen(pushDir, Vector2.down);
        }

        public override void End(bool completed)
        {
            if (_ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);
        }

        protected override void ApplyEffects(float progress)
        {
            if (_ctx.ToolPreview == null) return;

            // Settle phase: square pushes gently into the corner
            if (progress <= SettleEnd)
            {
                float settleT = Mathf.Clamp01(progress / SettleEnd);
                float eased = EaseOutQuad(settleT);
                _ctx.ToolPreview.transform.position = Vector3.Lerp(_hoverPos, _settledPos, eased);
            }
            else
            {
                _ctx.ToolPreview.transform.position = _settledPos;
            }

            // Hold phase: green glow builds
            if (progress >= SettleEnd && !_glowStarted)
                _glowStarted = true;

            if (_glowStarted && progress < HoldEnd)
            {
                float glowT = Mathf.InverseLerp(SettleEnd, HoldEnd, progress);
                Color glow = Color.Lerp(Color.black, new Color(0.1f, 0.8f, 0.2f) * 1.5f, glowT);
                MaterialHelper.SetEmission(_ctx.ToolPreview, glow);
            }

            // Confirmation phase: bright green pulse + particle
            if (progress >= HoldEnd)
            {
                float confirmT = Mathf.InverseLerp(HoldEnd, 1f, progress);
                float pulse = 1f + 0.3f * Mathf.Sin(confirmT * Mathf.PI * 2f);
                Color confirm = new Color(0.1f, 0.9f, 0.2f) * pulse * 2f;
                MaterialHelper.SetEmission(_ctx.ToolPreview, confirm);

                if (!_confirmSpawned)
                {
                    _confirmSpawned = true;
                    CompletionParticleEffect.TrySpawn("square_confirm",
                        _ctx.TargetWorldPos, Vector3.one * 0.08f);
                }
            }
        }

        private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    }
}
