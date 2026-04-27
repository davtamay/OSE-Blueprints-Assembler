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
        public override float Duration => Override(Cfg?.duration ?? 0f, 1.0f);

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
            // When a part-motion lerp is wired (endTransform authored on the
            // task), the trainee should swipe toward where the part will end
            // up — same direction they see during the auto-play observe
            // phase. Falls back to the legacy "drill plunge from tool to
            // target" heuristic when no part motion is set.
            if (context.PartMotionDirectionWorld.sqrMagnitude > 0.001f)
                return context.ProjectDirectionToScreen(context.PartMotionDirectionWorld, Vector2.right);

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

            // Keep rotation stable. Position is NOT written here anymore —
            // the controller owns it under the overlay contract.
            _ctx.ToolPreview.transform.rotation = _startRot;

            float glowScale       = Override(Cfg?.drillGlowIntensity ?? 0f, 0.5f);
            Color glowColor       = Override(Cfg?.drillGlowColor ?? default, new Color(0.3f, 0.7f, 1f, 1f));
            float spark1Threshold = Override(Cfg?.drillSpark1Threshold ?? 0f, 0.4f);
            float spark2Threshold = Override(Cfg?.drillSpark2Threshold ?? 0f, 0.8f);
            float spark1Scale     = Override(Cfg?.drillSpark1Scale ?? 0f, 0.06f);
            float spark2Scale     = Override(Cfg?.drillSpark2Scale ?? 0f, 0.04f);

            // Emission glow proportional to motor intensity.
            float glow = ComputeIntensity(progress) * glowScale;
            MaterialHelper.SetEmission(_ctx.ToolPreview,
                new Color(glowColor.r * glow, glowColor.g * glow, glowColor.b * glow, 1f));

            // Spark bursts.
            if (!_sparks1 && progress >= spark1Threshold)
            {
                _sparks1 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * spark1Scale);
            }
            if (!_sparks2 && progress >= spark2Threshold)
            {
                _sparks2 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * spark2Scale);
            }
        }

        /// <summary>
        /// Drill vibration in all three axes. The controller composes this on
        /// top of <c>_workingPos + _partEffectOffset</c> each frame — so the
        /// bit appears to wobble while still following the bolt downward.
        /// Amplitude scales with the ramp-up/ramp-down intensity envelope so
        /// vibration fades in at 0–15% progress and out at 85–100%.
        /// </summary>
        public override Vector3 ComputeOverlayOffset(float progress)
        {
            float shakeAmp  = Override(Cfg?.drillShakeAmplitude ?? 0f, ShakeAmplitude);
            float shakeFreq = Override(Cfg?.drillShakeFrequency ?? 0f, ShakeFrequency);
            float intensity = ComputeIntensity(progress);
            float amp = intensity * shakeAmp;
            float t = progress * shakeFreq;
            float dx = Mathf.Sin(t * 1.0f) * amp;
            float dy = Mathf.Sin(t * 1.3f + 1.7f) * amp * 0.7f;
            float dz = Mathf.Sin(t * 0.9f + 3.1f) * amp * 0.5f;
            return new Vector3(dx, dy, dz);
        }

        private float ComputeIntensity(float progress)
        {
            float rampUpEnd   = Override(Cfg?.drillRampUpEnd ?? 0f, RampUpEnd);
            float rampDownStart = Override(Cfg?.drillRampDownStart ?? 0f, RampDownStart);
            if (progress < rampUpEnd)
                return Mathf.SmoothStep(0f, 1f, progress / rampUpEnd);
            if (progress > rampDownStart)
                return Mathf.SmoothStep(1f, 0f, (progress - rampDownStart) / (1f - rampDownStart));
            return 1f;
        }
    }
}
