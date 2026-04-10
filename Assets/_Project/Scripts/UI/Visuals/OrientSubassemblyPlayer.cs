using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Animates a subassembly proxy root to a target rotation specified by
    /// <see cref="OSE.Content.AnimationCueEntry.subassemblyRotation"/>.
    /// Uses Lerp/Slerp like the existing working orientation system but
    /// driven by the cue coordinator rather than step lifecycle.
    /// </summary>
    internal sealed class OrientSubassemblyPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "orientSubassembly";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private Vector3 _toPos;
        private Quaternion _toRot;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            if (_ctx.Targets.Count == 0 || _ctx.Targets[0] == null)
            {
                IsPlaying = false;
                return;
            }

            Transform root = _ctx.Targets[0].transform;
            _fromPos = root.localPosition;
            _fromRot = root.localRotation;

            // Apply the authored rotation delta to the current rotation
            var entry = context.Entry;
            Quaternion delta = Quaternion.Euler(
                entry.subassemblyRotation.x,
                entry.subassemblyRotation.y,
                entry.subassemblyRotation.z);
            _toRot = delta * _fromRot;
            _toPos = _fromPos; // position stays unless we add offset support later
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying || _ctx.Duration <= 0f) return false;
            if (_ctx.Targets.Count == 0 || _ctx.Targets[0] == null) return false;

            _elapsed += deltaTime;
            float rawT = Mathf.Clamp01(_elapsed / _ctx.Duration);
            float easedT = EasingHelper.Apply(_ctx.Entry.easing, rawT);

            Transform root = _ctx.Targets[0].transform;
            root.localPosition = Vector3.Lerp(_fromPos, _toPos, easedT);
            root.localRotation = Quaternion.Slerp(_fromRot, _toRot, easedT);

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

            if (_ctx.Targets.Count > 0 && _ctx.Targets[0] != null)
            {
                Transform root = _ctx.Targets[0].transform;
                root.localPosition = _toPos;
                root.localRotation = _toRot;
            }
        }
    }
}
