using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Spawns a <see cref="MeasurementLineVisual"/> between two resolved
    /// anchors with a live-updating distance readout. Recreates the
    /// tape-measure A→B line with label. In progress-range mode, the B
    /// endpoint extends from A toward B as progress ramps 0→1 so authors
    /// can demonstrate the measurement being drawn.
    /// </summary>
    public sealed class MeasureLinePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "measureLine";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private MeasurementLineVisual _visual;
        private Vector3 _posA;
        private Vector3 _posB;
        private bool _hasAnchors;
        private string _unit;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            _hasAnchors = ResolveAnchors(context, out _posA, out _posB);
            if (!_hasAnchors) { IsPlaying = false; return; }

            var e = context.Entry;
            _unit = string.IsNullOrEmpty(e.measureUnit) ? "mm" : e.measureUnit;
            float dist = Vector3.Distance(_posA, _posB);
            string label = MeasurementLineVisual.FormatDistance(dist, _unit);

            Color col = e.fromColor.a > 0f
                ? new Color(e.fromColor.r, e.fromColor.g, e.fromColor.b, e.fromColor.a)
                : new Color(1f, 0.8f, 0.2f, 1f);

            _visual = MeasurementLineVisual.Spawn(_posA, _posA, label, col);
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

        public void Stop()
        {
            // Leave the visual in place — it's a persistent measurement mark.
            // Cleanup on step exit destroys it via scene lifecycle.
            IsPlaying = false;
        }

        private void Apply(float t)
        {
            if (!_hasAnchors || _visual == null) return;
            Vector3 endpoint = Vector3.Lerp(_posA, _posB, t);
            _visual.UpdateEndpoint(endpoint);
            float dist = Vector3.Distance(_posA, endpoint);
            _visual.UpdateLabel(MeasurementLineVisual.FormatDistance(dist, _unit));
        }

        private static bool ResolveAnchors(AnimationCueContext ctx, out Vector3 a, out Vector3 b)
        {
            a = Vector3.zero;
            b = Vector3.zero;
            var e = ctx.Entry;
            bool hasA = AnimationAnchorResolver.TryResolveAnchor(e.anchorARef, ctx, out a);
            bool hasB = AnimationAnchorResolver.TryResolveAnchor(e.anchorBRef, ctx, out b);
            return hasA && hasB;
        }
    }
}
