using UnityEngine;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Tweens the host's base material colour from <c>fromColor</c> to
    /// <c>toColor</c>. Drives <see cref="MaterialHelper.SetMaterialColor"/>
    /// each tick. Useful for hot→cool transitions that don't need an
    /// emission component.
    /// </summary>
    public sealed class ColorTweenPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "colorTween";
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
            Color from = new Color(e.fromColor.r, e.fromColor.g, e.fromColor.b, e.fromColor.a <= 0f ? 1f : e.fromColor.a);
            Color to   = new Color(e.toColor.r,   e.toColor.g,   e.toColor.b,   e.toColor.a   <= 0f ? 1f : e.toColor.a);
            Color col  = Color.Lerp(from, to, t);

            if (_ctx.Targets == null) return;
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) MaterialHelper.SetMaterialColor(go, col);
            }
        }
    }
}
