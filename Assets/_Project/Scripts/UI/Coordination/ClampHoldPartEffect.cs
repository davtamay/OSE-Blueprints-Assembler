using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// "No motion" archetype. The part stays pinned to its start pose for the
    /// entire action phase, and the effect reports zero displacement per frame
    /// so the tool stays put too. Use for welding torches held against a seam,
    /// multimeter probes, clamps that don't translate during use — any case
    /// where the tool engages the part without moving it.
    ///
    /// The authored stepPose is still respected as the end pose: <see cref="End"/>
    /// snaps the part there on completion. That way a clamp_hold tool action can
    /// still coincide with a pose change that the sequence commits when the action
    /// finishes (e.g. "the carriage is clamped, mark the half-open pose as sealed").
    /// </summary>
    internal sealed class ClampHoldPartEffect : IPartEffect
    {
        private readonly Transform _part;
        private readonly Vector3 _startPos, _endPos;
        private readonly Quaternion _startRot, _endRot;
        private readonly Vector3 _startScale, _endScale;

        public ClampHoldPartEffect(
            Transform part,
            Vector3 startPos, Quaternion startRot, Vector3 startScale,
            Vector3 endPos,   Quaternion endRot,   Vector3 endScale)
        {
            _part       = part;
            _startPos   = startPos;   _endPos   = endPos;
            _startRot   = startRot;   _endRot   = endRot;
            _startScale = startScale; _endScale = endScale;
        }

        public void Begin()
        {
            _part.localPosition = _startPos;
            _part.localRotation = _startRot;
            _part.localScale    = _startScale;
        }

        public Vector3 Apply(float _) => Vector3.zero;

        public void End()
        {
            _part.localPosition = _endPos;
            _part.localRotation = _endRot;
            _part.localScale    = _endScale;
        }
    }
}
