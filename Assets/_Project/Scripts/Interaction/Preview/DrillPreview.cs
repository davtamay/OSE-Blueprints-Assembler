using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Drill preview: tool body stays stable with low-intensity vibration
    /// to simulate a power drill tightening a bolt.
    /// Sparks burst at 40% and 80% progress.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: horizontal drag drives progress with auto-assist.
    /// </summary>
    public sealed class DrillPreview : ToolActionPreviewBase
    {
        public override float Duration => 1.0f;

        protected override float GuidedDragScale => 0.006f;
        protected override float AutoAssistDelay => 2.5f;
        protected override float AutoAssistRate => 0.6f;

        private Vector3 _startPos;
        private Quaternion _startRot;
        private bool _sparks1;
        private bool _sparks2;

        // Vibration: subtle shake to convey motor running
        private const float ShakeFrequency = 55f;
        private const float ShakeAmplitude = 0.0004f;
        // Ramp-up: vibration builds then fades
        private const float RampUpEnd = 0.15f;
        private const float RampDownStart = 0.85f;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);
            _startPos = context.ToolPreview != null
                ? context.ToolPreview.transform.position
                : Vector3.zero;
            _startRot = context.ToolPreview != null
                ? context.ToolPreview.transform.rotation
                : Quaternion.identity;

            _sparks1 = false;
            _sparks2 = false;
        }

        public override float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyEffects(t);
            return t;
        }

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            // Drag should follow the drill's plunge direction — i.e. from the
            // tool's current position toward the target surface. This is the
            // direction the bit is heading, regardless of how the tool model
            // is internally oriented (Drill profile aligns actionAxis, not
            // tipDirection, so multiplying by transform.rotation * tipDir is
            // unreliable here).
            Vector3 plungeDir = Vector3.down;
            if (context.ToolPreview != null)
            {
                Vector3 delta = context.TargetWorldPos - context.ToolPreview.transform.position;
                if (delta.sqrMagnitude > 0.0001f)
                    plungeDir = delta.normalized;
            }

            return context.ProjectDirectionToScreen(plungeDir, Vector2.right);
        }

        public override void End(bool completed)
        {
            if (_ctx.ToolPreview != null)
            {
                _ctx.ToolPreview.transform.position = _startPos;
                _ctx.ToolPreview.transform.rotation = _startRot;
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);
            }
        }

        protected override void ApplyEffects(float progress)
        {
            if (_ctx.ToolPreview == null) return;

            // Speed envelope: ramp up, sustain, ramp down
            float intensity;
            if (progress < RampUpEnd)
                intensity = Mathf.SmoothStep(0f, 1f, progress / RampUpEnd);
            else if (progress > RampDownStart)
                intensity = Mathf.SmoothStep(1f, 0f, (progress - RampDownStart) / (1f - RampDownStart));
            else
                intensity = 1f;

            // Keep rotation stable
            _ctx.ToolPreview.transform.rotation = _startRot;

            // Subtle random-feeling vibration in all axes
            float amp = intensity * ShakeAmplitude;
            float t = progress * ShakeFrequency;
            float dx = Mathf.Sin(t * 1.0f) * amp;
            float dy = Mathf.Sin(t * 1.3f + 1.7f) * amp * 0.7f;
            float dz = Mathf.Sin(t * 0.9f + 3.1f) * amp * 0.5f;
            _ctx.ToolPreview.transform.position = _startPos + new Vector3(dx, dy, dz);

            // Emission glow proportional to motor intensity
            float glow = intensity * 0.5f;
            MaterialHelper.SetEmission(_ctx.ToolPreview,
                new Color(0.3f * glow, 0.7f * glow, 1f * glow, 1f));

            // Spark bursts
            if (!_sparks1 && progress >= 0.4f)
            {
                _sparks1 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * 0.06f);
            }
            if (!_sparks2 && progress >= 0.8f)
            {
                _sparks2 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * 0.04f);
            }
        }
    }
}
