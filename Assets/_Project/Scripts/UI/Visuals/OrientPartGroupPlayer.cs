using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Animates a partGroup proxy root to a target rotation specified by
    /// <see cref="OSE.Content.AnimationCueEntry.partGroupRotation"/>.
    /// Uses Lerp/Slerp like the existing working orientation system but
    /// driven by the cue coordinator rather than step lifecycle.
    /// </summary>
    public sealed class OrientPartGroupPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "orientPartGroup";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private Quaternion _deltaRot;       // authored rotation delta (in target's local frame)
        private Vector3 _pivotLocal;        // members' centroid in target's local frame; (0,0,0) when no children

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

            // Authored delta — rotate by this much, around the members' centroid.
            var entry = context.Entry;
            _deltaRot = Quaternion.Euler(
                entry.partGroupRotation.x,
                entry.partGroupRotation.y,
                entry.partGroupRotation.z);

            // Centroid C in the target's local frame. The transform identity:
            //   childWorld = root.position + root.rotation * child.localPosition
            // implies that to rotate every child around centroid C by R while
            // leaving Group_'s authored baseline pose (_fromPos / _fromRot)
            // intact, we set
            //   root.position = _fromPos + (_fromRot * C) - (_fromRot * R * C)
            //                 = _fromPos + _fromRot * (I - R) * C
            //   root.rotation = _fromRot * R
            // Prefer the caller-provided hint (PivotCentroidResolver, the
            // single source of truth shared with runtime + authoring). Falls
            // back to zero for single-part targets or when no body members
            // exist yet — which collapses to "rotate-in-place" (I - R) * 0 = 0.
            _pivotLocal = context.PivotHintLocal ?? Vector3.zero;

            // Optional authored pivot override — shift the rotation center
            // by a local-space offset. Default behaviour (override == false)
            // leaves _pivotLocal at the derived centroid unchanged.
            if (entry.pivotOffsetOverride)
            {
                _pivotLocal += new Vector3(
                    entry.pivotOffset.x,
                    entry.pivotOffset.y,
                    entry.pivotOffset.z);
            }
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying || _ctx.Duration <= 0f) return false;
            if (_ctx.Targets.Count == 0 || _ctx.Targets[0] == null) return false;

            _elapsed += deltaTime;
            float rawT = Mathf.Clamp01(_elapsed / _ctx.Duration);
            float easedT = EasingHelper.Apply(_ctx.Entry.easing, rawT);

            Quaternion currentR = Quaternion.Slerp(Quaternion.identity, _deltaRot, easedT);
            Vector3 counter = _pivotLocal - currentR * _pivotLocal; // (I - R) * C in local frame

            Transform root = _ctx.Targets[0].transform;
            root.localRotation = _fromRot * currentR;
            root.localPosition = _fromPos + _fromRot * counter;

            if (rawT >= 1f)
            {
                IsPlaying = false;
                return false;
            }
            return true;
        }

        public void Stop()
        {
            // Always restore, even after Tick already set IsPlaying=false
            // on natural completion. Previously the early-return here
            // skipped restoration when the cue finished normally, leaving
            // the Group_ root rotated and causing pose accumulation when
            // stepping forwards / backwards.
            IsPlaying = false;

            if (_ctx.Targets != null && _ctx.Targets.Count > 0 && _ctx.Targets[0] != null
                && !(_ctx.Entry != null && _ctx.Entry.holdAtEnd))
            {
                // Restore to baseline so the persistent Group_ root is
                // ready for the next interaction / animation. Final
                // post-rotation pose is achieved by step-pose data, not
                // by leaving Group_ rotated.
                Transform root = _ctx.Targets[0].transform;
                root.localPosition = _fromPos;
                root.localRotation = _fromRot;
            }
        }

    }
}
