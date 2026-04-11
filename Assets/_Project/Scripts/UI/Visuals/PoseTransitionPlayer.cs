using OSE.Content;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Arbitrary from/to pose transition. Uses explicit <c>fromPose</c>/<c>toPose</c>
    /// from the cue entry when present, otherwise falls back to start/assembled poses.
    /// Supports optional <c>spinRevolutions</c> for rotational effects.
    /// </summary>
    public sealed class PoseTransitionPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "poseTransition";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private AnimationCueResolvedPose[] _fromPoses;
        private AnimationCueResolvedPose[] _toPoses;
        private float _spinRevolutions;
        private Vector3 _spinAxis;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            _spinRevolutions = context.Entry.spinRevolutions;
            _spinAxis = new Vector3(
                context.Entry.spinAxis.x,
                context.Entry.spinAxis.y,
                context.Entry.spinAxis.z);
            if (_spinAxis.sqrMagnitude < 0.001f)
                _spinAxis = Vector3.up;

            int count = _ctx.Targets.Count;
            _fromPoses = new AnimationCueResolvedPose[count];
            _toPoses = new AnimationCueResolvedPose[count];

            for (int i = 0; i < count; i++)
            {
                _fromPoses[i] = ResolveFrom(context, i);
                _toPoses[i] = ResolveTo(context, i);

                if (_ctx.Targets[i] != null)
                {
                    var t = _ctx.Targets[i].transform;
                    t.localPosition = _fromPoses[i].Position;
                    t.localRotation = _fromPoses[i].Rotation;
                    if (_fromPoses[i].Scale.sqrMagnitude > 0.001f)
                        t.localScale = _fromPoses[i].Scale;
                }
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying || _ctx.Duration <= 0f) return false;

            _elapsed += deltaTime;
            float rawT = Mathf.Clamp01(_elapsed / _ctx.Duration);
            float easedT = EasingHelper.Apply(_ctx.Entry.easing, rawT);

            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null) continue;
                var t = _ctx.Targets[i].transform;

                t.localPosition = Vector3.Lerp(_fromPoses[i].Position, _toPoses[i].Position, easedT);

                Quaternion baseRot = Quaternion.Slerp(_fromPoses[i].Rotation, _toPoses[i].Rotation, easedT);
                if (_spinRevolutions > 0f)
                {
                    float spinAngle = easedT * _spinRevolutions * 360f;
                    baseRot = baseRot * Quaternion.AngleAxis(spinAngle, _spinAxis);
                }
                t.localRotation = baseRot;

                if (_fromPoses[i].Scale.sqrMagnitude > 0.001f && _toPoses[i].Scale.sqrMagnitude > 0.001f)
                    t.localScale = Vector3.Lerp(_fromPoses[i].Scale, _toPoses[i].Scale, easedT);
            }

            if (rawT >= 1f)
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

            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null || i >= _toPoses.Length) continue;
                var t = _ctx.Targets[i].transform;
                t.localPosition = _toPoses[i].Position;
                t.localRotation = _toPoses[i].Rotation;
                if (_toPoses[i].Scale.sqrMagnitude > 0.001f)
                    t.localScale = _toPoses[i].Scale;
            }
        }

        private static AnimationCueResolvedPose ResolveFrom(AnimationCueContext ctx, int index)
        {
            AnimationPose explicitFrom = ctx.Entry.fromPose;
            if (explicitFrom != null)
                return ConvertPose(explicitFrom);
            if (index < ctx.StartPoses.Count)
                return ctx.StartPoses[index];
            return CurrentPose(ctx, index);
        }

        private static AnimationCueResolvedPose ResolveTo(AnimationCueContext ctx, int index)
        {
            AnimationPose explicitTo = ctx.Entry.toPose;
            if (explicitTo != null)
                return ConvertPose(explicitTo);
            if (index < ctx.AssembledPoses.Count)
                return ctx.AssembledPoses[index];
            return CurrentPose(ctx, index);
        }

        private static AnimationCueResolvedPose ConvertPose(AnimationPose p) => new AnimationCueResolvedPose
        {
            Position = new Vector3(p.position.x, p.position.y, p.position.z),
            Rotation = new Quaternion(p.rotation.x, p.rotation.y, p.rotation.z, p.rotation.w),
            Scale = new Vector3(p.scale.x, p.scale.y, p.scale.z),
        };

        private static AnimationCueResolvedPose CurrentPose(AnimationCueContext ctx, int index)
        {
            if (index < ctx.Targets.Count && ctx.Targets[index] != null)
            {
                var t = ctx.Targets[index].transform;
                return new AnimationCueResolvedPose
                {
                    Position = t.localPosition,
                    Rotation = t.localRotation,
                    Scale = t.localScale,
                };
            }
            return default;
        }
    }
}
