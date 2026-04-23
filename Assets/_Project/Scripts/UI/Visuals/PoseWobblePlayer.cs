using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Continuous sine-based rotation perturbation. Recreates WeldPreview's
    /// torch wobble (amp 0.12°, freq 40 cycles/progress on pitch + yaw)
    /// and any similar perturbation. <c>wobbleAmplitude</c> is in DEGREES
    /// — passed straight into <see cref="Quaternion.Euler"/>. Adds an Euler
    /// delta derived from <c>sin(progress * frequency)</c> to the host's
    /// local rotation each tick, restoring the baseline on Stop.
    /// </summary>
    public sealed class PoseWobblePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "poseWobble";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private Quaternion[] _baselineRot;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;
            if (context.Targets != null)
            {
                _baselineRot = new Quaternion[context.Targets.Count];
                for (int i = 0; i < context.Targets.Count; i++)
                    _baselineRot[i] = context.Targets[i] != null ? context.Targets[i].transform.localRotation : Quaternion.identity;
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;
            float dur = _ctx.Duration > 0f ? _ctx.Duration : 1f;
            float t = Mathf.Clamp01(_elapsed / dur);
            Apply(t);
            if (t >= 1f) { Stop(); return false; }
            return true;
        }

        public void TickProgress(float progress01) => Apply(Mathf.Clamp01(progress01));

        public void Stop()
        {
            // Restore baseline so the host doesn't drift when the cue ends.
            if (_ctx.Targets != null && _baselineRot != null)
            {
                for (int i = 0; i < _ctx.Targets.Count && i < _baselineRot.Length; i++)
                {
                    var go = _ctx.Targets[i];
                    if (go != null) go.transform.localRotation = _baselineRot[i];
                }
            }
            IsPlaying = false;
        }

        private void Apply(float progress01)
        {
            var e = _ctx.Entry;
            float amp  = e.wobbleAmplitude > 0f ? e.wobbleAmplitude : 0.12f;
            float freq = e.wobbleFrequency > 0f ? e.wobbleFrequency : 40f;
            Vector3 axis = new Vector3(
                e.wobbleAxis.x != 0f ? e.wobbleAxis.x : 1f,
                e.wobbleAxis.y != 0f ? e.wobbleAxis.y : 1f,
                e.wobbleAxis.z);

            // amp is interpreted in DEGREES to match WeldPreview's original
            // Quaternion.Euler(wobble, 0, wobble*0.5) — which took 0.12 as
            // degrees directly. The prior * Rad2Deg conversion multiplied
            // the intended amplitude by ~57×, producing a violent shake.
            float phase = progress01 * freq;
            float s = Mathf.Sin(phase);
            Vector3 eulerDeg = new Vector3(s * amp * axis.x, s * amp * axis.y, s * amp * axis.z);
            Quaternion delta = Quaternion.Euler(eulerDeg);

            if (_ctx.Targets == null || _baselineRot == null) return;
            for (int i = 0; i < _ctx.Targets.Count && i < _baselineRot.Length; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) go.transform.localRotation = _baselineRot[i] * delta;
            }
        }
    }
}
