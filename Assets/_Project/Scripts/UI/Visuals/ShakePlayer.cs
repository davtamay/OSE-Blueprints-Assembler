using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Oscillates target GameObjects around their start position using a sine wave.
    /// Useful for demonstrating shake tests — e.g. shaking a carriage to check bearing fit.
    ///
    /// Authored fields (all optional, sensible defaults):
    ///   shakeAmplitude   — peak displacement in metres (default 0.01 = 1 cm)
    ///   shakeFrequency   — oscillations per second in Hz (default 8)
    ///   shakeAxis        — local-space direction of oscillation (default (1,0,0) = side-to-side)
    ///   durationSeconds  — 0 = run indefinitely until step navigates away
    ///   loop             — true = restart after durationSeconds
    /// </summary>
    internal sealed class ShakePlayer : IAnimationCuePlayer
    {
        private const float DefaultAmplitude = 0.01f;  // 1 cm
        private const float DefaultFrequency = 8f;     // Hz

        public string AnimationType => "shake";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _amplitude;
        private float _frequency;
        private Vector3[] _originLocalPositions;
        private Vector3 _axis;
        private float _elapsed;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            var entry = context.Entry;
            _amplitude = entry.shakeAmplitude > 0f ? entry.shakeAmplitude : DefaultAmplitude;
            _frequency = entry.shakeFrequency > 0f ? entry.shakeFrequency : DefaultFrequency;

            // Resolve shake axis from authored SceneFloat3; fall back to side-to-side
            bool hasAxis = entry.shakeAxis.x != 0f || entry.shakeAxis.y != 0f || entry.shakeAxis.z != 0f;
            _axis = hasAxis
                ? new Vector3(entry.shakeAxis.x, entry.shakeAxis.y, entry.shakeAxis.z).normalized
                : Vector3.right;

            // Capture each target's current local position as the oscillation origin
            _originLocalPositions = new Vector3[context.Targets.Count];
            for (int i = 0; i < context.Targets.Count; i++)
            {
                _originLocalPositions[i] = context.Targets[i] != null
                    ? context.Targets[i].transform.localPosition
                    : Vector3.zero;
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;

            _elapsed += deltaTime;

            float displacement = _amplitude * Mathf.Sin(_elapsed * _frequency * Mathf.PI * 2f);

            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null) continue;
                _ctx.Targets[i].transform.localPosition = _originLocalPositions[i] + _axis * displacement;
            }

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

            // Restore each target to its origin position
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] != null)
                    _ctx.Targets[i].transform.localPosition = _originLocalPositions[i];
            }
        }
    }
}
