using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Animates a subassembly proxy root to a target rotation specified by
    /// <see cref="OSE.Content.AnimationCueEntry.subassemblyRotation"/>.
    /// Uses Lerp/Slerp like the existing working orientation system but
    /// driven by the cue coordinator rather than step lifecycle.
    /// </summary>
    public sealed class OrientSubassemblyPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "orientSubassembly";
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
                entry.subassemblyRotation.x,
                entry.subassemblyRotation.y,
                entry.subassemblyRotation.z);

            // Compute the centroid of immediate active children in the
            // target's local frame. The transform identity:
            //   childWorld = root.position + root.rotation * child.localPosition
            // implies that to rotate every child around centroid C (in
            // root-local space) by R while leaving Group_'s authored
            // baseline pose (_fromPos / _fromRot) intact, we set
            //   root.position = _fromPos + (_fromRot * C) - (_fromRot * R * C)
            //                 = _fromPos + _fromRot * (I - R) * C
            //   root.rotation = _fromRot * R
            // C falls out as (0,0,0) when the root has no children, which
            // collapses to the legacy "rotate-in-place" behaviour for a
            // single-part target.
            _pivotLocal = ComputeChildrenCentroidLocal(root);
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
            if (!IsPlaying) return;
            IsPlaying = false;

            if (_ctx.Targets.Count > 0 && _ctx.Targets[0] != null)
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

        private static Vector3 ComputeChildrenCentroidLocal(Transform root)
        {
            if (root == null || root.childCount == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                sum += c.localPosition;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }
    }
}
