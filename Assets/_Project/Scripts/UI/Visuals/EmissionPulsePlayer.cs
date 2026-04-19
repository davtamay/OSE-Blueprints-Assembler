using UnityEngine;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Tweens the host's emission colour and intensity from <c>(fromColor,
    /// fromIntensity)</c> to <c>(toColor, toIntensity)</c>. Recreates the
    /// cyan-white arc glow in WeldPreview, the proportional blue glow in
    /// DrillPreview, the orange glow in CutPreview, and the green settle
    /// glow in SquareCheckPreview. Drives
    /// <see cref="MaterialHelper.SetEmission"/> each tick.
    /// </summary>
    public sealed class EmissionPulsePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "emissionPulse";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;
            Apply(0f);
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;
            float dur = _ctx.Duration > 0f ? _ctx.Duration : 1f;
            float t = Mathf.Clamp01(_elapsed / dur);
            Apply(t);
            if (t >= 1f) { IsPlaying = false; return false; }
            return true;
        }

        public void TickProgress(float progress01) => Apply(Mathf.Clamp01(progress01));

        public void Stop() { IsPlaying = false; }

        private void Apply(float t)
        {
            var e = _ctx.Entry;
            Color from = ToColor(e.fromEmission, Color.black);
            Color to   = ToColor(e.toEmission,   Color.white);
            float fi   = e.fromIntensity > 0f ? e.fromIntensity : 0f;
            float ti   = e.toIntensity   > 0f ? e.toIntensity   : 1f;

            Color col = Color.Lerp(from, to, t) * Mathf.Lerp(fi, ti, t);

            if (_ctx.Targets == null) return;
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) MaterialHelper.SetEmission(go, col);
            }
        }

        private static Color ToColor(SceneFloat4 v, Color fallback)
            => (v.r == 0f && v.g == 0f && v.b == 0f && v.a == 0f)
               ? fallback
               : new Color(v.r, v.g, v.b, v.a);
    }
}
