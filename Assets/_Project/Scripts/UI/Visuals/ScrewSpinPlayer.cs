using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Rotates the host around a local axis by <c>spinAngleDegrees</c>
    /// scaled by progress. Recreates TorquePreview's 120° wrench sweep
    /// and any screw-down visual. Restores baseline on Stop.
    /// </summary>
    public sealed class ScrewSpinPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "screwSpin";
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
            if (t >= 1f)
            {
                // Hold final pose — don't restore; torque leaves the wrench rotated.
                IsPlaying = false;
                return false;
            }
            return true;
        }

        public void TickProgress(float progress01) => Apply(Mathf.Clamp01(progress01));

        public void Stop()
        {
            // Pose-transform invariant: natural completion lands at the
            // rotated end pose (the bolt stays turned — that IS the post-
            // transform state). Mid-animation teardown reverts to baseline
            // so the step transition starts clean. holdAtEnd forces the
            // rotated end even on mid-animation stops.
            IsPlaying = false;

            if (_ctx.Targets == null || _baselineRot == null) return;

            bool completed = _ctx.Duration > 0f && _elapsed >= _ctx.Duration;
            bool hold      = _ctx.Entry != null && _ctx.Entry.holdAtEnd;
            bool keepEnd   = completed || hold;
            if (keepEnd) return; // last Apply(progress=1) already wrote the rotated pose

            for (int i = 0; i < _ctx.Targets.Count && i < _baselineRot.Length; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) go.transform.localRotation = _baselineRot[i];
            }
        }

        private void Apply(float progress01)
        {
            var e = _ctx.Entry;
            float totalAngle = e.spinAngleDegrees != 0f ? e.spinAngleDegrees : 120f;
            Vector3 axis = e.spinAxis.x == 0f && e.spinAxis.y == 0f && e.spinAxis.z == 0f
                ? Vector3.up
                : new Vector3(e.spinAxis.x, e.spinAxis.y, e.spinAxis.z);

            // EaseInOutCubic feels right for a wrench sweep — snap into the
            // motion, decelerate at the end.
            float eased = EaseInOutCubic(progress01);
            float angle = Mathf.Lerp(0f, totalAngle, eased);
            Quaternion delta = Quaternion.AngleAxis(angle, axis);

            if (_ctx.Targets == null || _baselineRot == null) return;
            for (int i = 0; i < _ctx.Targets.Count && i < _baselineRot.Length; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) go.transform.localRotation = _baselineRot[i] * delta;
            }
        }

        private static float EaseInOutCubic(float t)
            => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
