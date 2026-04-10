using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Lerps a part's local transform from a start pose to an end pose
    /// as tool action progress goes from 0 to 1. Zero-allocation per frame.
    /// Reusable for any tool type: drill drives bolt down, wrench rotates nut,
    /// press pushes bearing in, etc.
    /// </summary>
    internal sealed class LerpPosePartEffect : IPartEffect
    {
        private readonly Transform _part;
        private readonly Transform _previewRoot;
        private readonly Vector3 _startPos, _endPos;
        private readonly Quaternion _startRot, _endRot;
        private readonly Vector3 _startScale, _endScale;
        private readonly bool _lerpRotation;
        private readonly bool _lerpScale;
        private Vector3 _prevWorldPos;

        public LerpPosePartEffect(
            Transform partTransform,
            Transform previewRoot,
            Vector3 startLocalPos, Quaternion startLocalRot, Vector3 startLocalScale,
            Vector3 endLocalPos, Quaternion endLocalRot, Vector3 endLocalScale)
        {
            _part = partTransform;
            _previewRoot = previewRoot;
            _startPos = startLocalPos;
            _endPos = endLocalPos;
            _startRot = startLocalRot;
            _endRot = endLocalRot;
            _startScale = startLocalScale;
            _endScale = endLocalScale;
            _lerpRotation = Quaternion.Angle(startLocalRot, endLocalRot) > 0.01f;
            _lerpScale = Vector3.Distance(startLocalScale, endLocalScale) > 0.0001f;
        }

        public void Begin()
        {
            _part.localPosition = _startPos;
            _part.localRotation = _startRot;
            _part.localScale = _startScale;
            _prevWorldPos = _previewRoot != null
                ? _previewRoot.TransformPoint(_startPos)
                : _part.position;
        }

        public Vector3 Apply(float progress)
        {
            _part.localPosition = Vector3.Lerp(_startPos, _endPos, progress);
            if (_lerpRotation)
                _part.localRotation = Quaternion.Slerp(_startRot, _endRot, progress);
            if (_lerpScale)
                _part.localScale = Vector3.Lerp(_startScale, _endScale, progress);

            Vector3 currentWorld = _previewRoot != null
                ? _previewRoot.TransformPoint(_part.localPosition)
                : _part.position;
            Vector3 delta = currentWorld - _prevWorldPos;
            _prevWorldPos = currentWorld;
            return delta;
        }

        public void End()
        {
            _part.localPosition = _endPos;
            _part.localRotation = _endRot;
            _part.localScale = _endScale;
        }
    }
}
