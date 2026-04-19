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
    public sealed class ShakePlayer : IAnimationCuePlayer
    {
        private const float DefaultAmplitude = 0.01f;  // 1 cm
        private const float DefaultFrequency = 3f;     // Hz — 8 was nauseating; 3 is perceptible but smooth

        private enum ShakeMode { Sine, Positive, Slide }

        public string AnimationType => "shake";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _amplitude;
        private float _frequency;
        private Vector3[] _originLocalPositions;
        private Vector3 _axis;
        private float _elapsed;
        private ShakeMode _mode;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            var entry = context.Entry;
            _amplitude = entry.shakeAmplitude > 0f ? entry.shakeAmplitude : DefaultAmplitude;
            _frequency = entry.shakeFrequency > 0f ? entry.shakeFrequency : DefaultFrequency;
            _mode      = ParseMode(entry.shakeMode);

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

            float displacement;
            switch (_mode)
            {
                case ShakeMode.Positive:
                    // Half-wave rectified: always in the axis's positive
                    // direction. Rod-slide style: out-and-back each cycle.
                    displacement = _amplitude * Mathf.Abs(Mathf.Sin(_elapsed * _frequency * Mathf.PI * 2f));
                    break;

                case ShakeMode.Slide:
                {
                    // Single out-and-back pulse over the cue's Duration.
                    // Smoothstep ramps 0 → 1 over the first half, 1 → 0 over
                    // the second half. If Duration is 0 (indefinite), the
                    // pulse runs over 1 s as a sensible default cadence.
                    float pulseLen = _ctx.Duration > 0f ? _ctx.Duration : 1f;
                    float t = Mathf.Clamp01(_elapsed / pulseLen);
                    float pulse = t < 0.5f
                        ? Mathf.SmoothStep(0f, 1f, t * 2f)
                        : Mathf.SmoothStep(1f, 0f, (t - 0.5f) * 2f);
                    displacement = _amplitude * pulse;
                    break;
                }

                case ShakeMode.Sine:
                default:
                    displacement = _amplitude * Mathf.Sin(_elapsed * _frequency * Mathf.PI * 2f);
                    break;
            }

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

        private static ShakeMode ParseMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return ShakeMode.Sine;
            if (string.Equals(s, "positive", System.StringComparison.OrdinalIgnoreCase)) return ShakeMode.Positive;
            if (string.Equals(s, "slide",    System.StringComparison.OrdinalIgnoreCase)) return ShakeMode.Slide;
            return ShakeMode.Sine;
        }

        public void Stop()
        {
            // Always restore, even after Tick set IsPlaying=false on natural
            // completion. Previously the early-return here skipped the
            // restore when the cue finished normally, leaving the target
            // at whatever sine-wave displacement the last Tick produced.
            // Contributed to the pose-accumulation bug when stepping
            // forwards / backwards through steps.
            IsPlaying = false;

            if (_originLocalPositions == null) return;
            for (int i = 0; i < _ctx.Targets.Count && i < _originLocalPositions.Length; i++)
            {
                if (_ctx.Targets[i] != null)
                    _ctx.Targets[i].transform.localPosition = _originLocalPositions[i];
            }
        }
    }
}
