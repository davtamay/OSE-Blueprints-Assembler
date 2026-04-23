using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Moves the host's world position from anchorA → anchorB over the
    /// cue's active progress window, optionally held at A before and at B
    /// after the travel sub-window (<c>travelStartProgress</c> /
    /// <c>travelEndProgress</c>). Recreates WeldPreview's tool-along-seam
    /// motion: the torch slides from weldStart to weldEnd while the
    /// standoff from the seam surface is preserved. On Start, the offset
    /// between the host's current position and anchorA is captured and
    /// applied throughout, so an authored tool standoff (typically
    /// approachDir × workingDistance) carries through the lerp unchanged.
    /// </summary>
    public sealed class MoveBetweenAnchorsPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "moveBetweenAnchors";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private Transform _host;
        private Vector3 _anchorPosA;
        private Vector3 _anchorPosB;
        private Vector3 _hostOffsetFromA;
        private bool _hasAnchors;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            IsPlaying = true;

            if (context.Targets == null || context.Targets.Count == 0) { IsPlaying = false; return; }
            var hostGo = context.Targets[0];
            if (hostGo == null) { IsPlaying = false; return; }
            _host = hostGo.transform;

            var e = context.Entry;
            bool hasA = AnimationAnchorResolver.TryResolveAnchor(e.anchorARef, context, out _anchorPosA);
            bool hasB = AnimationAnchorResolver.TryResolveAnchor(e.anchorBRef, context, out _anchorPosB);
            _hasAnchors = hasA && hasB;
            if (!_hasAnchors) { IsPlaying = false; return; }

            // Preserve the host's current offset from anchorA so a tool's
            // standoff above the seam carries through the motion — host
            // sits at anchorA+offset at t=0, anchorB+offset at t=1.
            _hostOffsetFromA = _host.position - _anchorPosA;
        }

        public bool Tick(float deltaTime) => IsPlaying;

        public void TickProgress(float progress01)
        {
            if (!IsPlaying || !_hasAnchors || _host == null) return;

            // Map the cue's travel sub-window (global progress) onto the
            // cue-local progress01 space, then InverseLerp inside that
            // sub-window so the host holds at A before it, lerps across,
            // holds at B after. Matches ParticlePlayer's sub-window logic.
            float t = progress01;
            var e = _ctx.Entry;
            float cueRange = Mathf.Max(0.0001f, e.endProgress - e.startProgress);
            float travelStartLocal = e.travelStartProgress > 0f
                ? Mathf.Clamp01((e.travelStartProgress - e.startProgress) / cueRange)
                : 0f;
            float travelEndLocal = e.travelEndProgress > 0f
                ? Mathf.Clamp01((e.travelEndProgress - e.startProgress) / cueRange)
                : 1f;
            if (travelEndLocal > travelStartLocal)
                t = Mathf.InverseLerp(travelStartLocal, travelEndLocal, progress01);

            Vector3 target = Vector3.Lerp(_anchorPosA, _anchorPosB, Mathf.Clamp01(t)) + _hostOffsetFromA;
            _host.position = target;
        }

        public void Stop()
        {
            // No restore: the tool-action preview controller owns tool
            // position after Action phase ends (Return → FinishExit
            // re-parents + snaps to cursor). Restoring here would snap
            // the host back to anchorA mid-action on early stop.
            IsPlaying = false;
        }
    }
}
