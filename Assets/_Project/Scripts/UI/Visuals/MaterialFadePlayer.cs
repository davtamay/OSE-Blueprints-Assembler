using UnityEngine;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Combined base-colour + emission tween. Generalises the bespoke
    /// <c>WeldBeadCooler</c> pattern — fades a weld bead from hot silver
    /// (0.85, 0.82, 0.72) + orange emission 1.5× → cool silver (0.55, 0.55,
    /// 0.52) + black emission over N seconds. Any "cooling" / "fading"
    /// persistent mark can author this in place of bespoke
    /// MonoBehaviours.
    /// </summary>
    public sealed class MaterialFadePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "materialFade";
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
            float dur = _ctx.Duration > 0f ? _ctx.Duration : 2f;
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
            Color fromC = ToColor(e.fromColor, Color.white);
            Color toC   = ToColor(e.toColor,   Color.gray);
            Color fromE = ToColor(e.fromEmission, Color.black);
            Color toE   = ToColor(e.toEmission,   Color.black);
            float fi    = e.fromIntensity > 0f ? e.fromIntensity : 1f;
            float ti    = e.toIntensity   > 0f ? e.toIntensity   : 0f;

            Color baseCol = Color.Lerp(fromC, toC, t);
            Color emiCol  = Color.Lerp(fromE, toE, t) * Mathf.Lerp(fi, ti, t);

            if (_ctx.Targets == null) return;
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                var go = _ctx.Targets[i];
                if (go == null) continue;
                MaterialHelper.SetMaterialColor(go, baseCol);
                MaterialHelper.SetEmission(go, emiCol);
            }
        }

        private static Color ToColor(SceneFloat4 v, Color fallback)
            => (v.r == 0f && v.g == 0f && v.b == 0f && v.a == 0f)
               ? fallback
               : new Color(v.r, v.g, v.b, v.a);
    }
}
