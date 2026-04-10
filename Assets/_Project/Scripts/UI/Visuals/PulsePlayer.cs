using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Emission color pulse on target GameObjects using sine-wave interpolation.
    /// Uses <see cref="MaterialHelper.SetEmission"/> for URP/glTF compatibility.
    /// Always targets the real part (ghost mode ignored for pulse).
    /// </summary>
    internal sealed class PulsePlayer : IAnimationCuePlayer
    {
        private static readonly Color DefaultColorA = new Color(0f, 0.6f, 1f, 1f);
        private static readonly Color DefaultColorB = new Color(1f, 0.85f, 0f, 1f);
        private const float DefaultPulseSpeed = 3f;

        public string AnimationType => "pulse";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private Color _colorA;
        private Color _colorB;
        private float _speed;
        private float _elapsed;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            var entry = context.Entry;
            _colorA = entry.pulseColorA.a > 0f
                ? new Color(entry.pulseColorA.r, entry.pulseColorA.g, entry.pulseColorA.b, entry.pulseColorA.a)
                : DefaultColorA;
            _colorB = entry.pulseColorB.a > 0f
                ? new Color(entry.pulseColorB.r, entry.pulseColorB.g, entry.pulseColorB.b, entry.pulseColorB.a)
                : DefaultColorB;
            _speed = entry.pulseSpeed > 0f ? entry.pulseSpeed : DefaultPulseSpeed;
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;

            _elapsed += deltaTime;

            Color emission = ColorPulseHelper.Lerp(_colorA, _colorB, _speed);
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] != null)
                    MaterialHelper.SetEmission(_ctx.Targets[i], emission);
            }

            // When duration > 0, stop after elapsed time; otherwise pulse indefinitely
            if (_ctx.Duration > 0f && _elapsed >= _ctx.Duration)
            {
                IsPlaying = false;
                return false;
            }
            return true;
        }

        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;

            // Clear emission
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] != null)
                    MaterialHelper.SetEmission(_ctx.Targets[i], Color.black);
            }
        }
    }
}
