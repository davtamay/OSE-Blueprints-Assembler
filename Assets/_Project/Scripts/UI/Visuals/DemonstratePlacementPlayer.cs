using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Lerps each target from its start pose to its assembled pose.
    /// Supports optional <c>spinRevolutions</c> for bolt screw effect:
    /// the target rotates around <c>spinAxis</c> while translating.
    /// </summary>
    internal sealed class DemonstratePlacementPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "demonstratePlacement";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
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

            // Position targets at start pose
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null) continue;
                var t = _ctx.Targets[i].transform;
                if (i < _ctx.StartPoses.Count)
                {
                    t.localPosition = _ctx.StartPoses[i].Position;
                    t.localRotation = _ctx.StartPoses[i].Rotation;
                    if (_ctx.StartPoses[i].Scale.sqrMagnitude > 0.001f)
                        t.localScale = _ctx.StartPoses[i].Scale;
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
                if (i >= _ctx.StartPoses.Count || i >= _ctx.AssembledPoses.Count) continue;

                var startPose = _ctx.StartPoses[i];
                var assembledPose = _ctx.AssembledPoses[i];
                var t = _ctx.Targets[i].transform;

                t.localPosition = Vector3.Lerp(startPose.Position, assembledPose.Position, easedT);

                Quaternion baseRot = Quaternion.Slerp(startPose.Rotation, assembledPose.Rotation, easedT);
                if (_spinRevolutions > 0f)
                {
                    float spinAngle = easedT * _spinRevolutions * 360f;
                    baseRot = baseRot * Quaternion.AngleAxis(spinAngle, _spinAxis);
                }
                t.localRotation = baseRot;

                if (startPose.Scale.sqrMagnitude > 0.001f && assembledPose.Scale.sqrMagnitude > 0.001f)
                    t.localScale = Vector3.Lerp(startPose.Scale, assembledPose.Scale, easedT);
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

            // Snap to assembled pose
            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null) continue;
                if (i >= _ctx.AssembledPoses.Count) continue;

                var t = _ctx.Targets[i].transform;
                t.localPosition = _ctx.AssembledPoses[i].Position;
                t.localRotation = _ctx.AssembledPoses[i].Rotation;
                if (_ctx.AssembledPoses[i].Scale.sqrMagnitude > 0.001f)
                    t.localScale = _ctx.AssembledPoses[i].Scale;
            }
        }
    }
}
