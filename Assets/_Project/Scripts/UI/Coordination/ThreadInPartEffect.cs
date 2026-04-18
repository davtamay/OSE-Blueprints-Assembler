using OSE.Content;
using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Helical tool-driven part motion: the part translates from its start pose
    /// to its end pose along a specified axis while rotating about the same axis.
    /// Canonical use case — drilling/screwing a bolt into a tapped hole: the bolt
    /// plunges down and spins simultaneously as the tool engages it.
    ///
    /// <para><b>Axis resolution</b> (in part-local space):</para>
    /// <list type="number">
    ///   <item>Authored <c>payload.axis</c> with space = <c>part_local</c> is used verbatim.</item>
    ///   <item>Spaces <c>target_local</c> and <c>world</c> are not yet supported — Phase E wiring.</item>
    ///   <item>Space = <c>tool_action_axis</c> reads from <see cref="ToolPoseConfig.actionAxis"/>.</item>
    ///   <item>Fallback: normalized (endPos − startPos) — valid whenever translation direction
    ///         equals threading axis (true for straight-plunge bolts/screws).</item>
    /// </list>
    ///
    /// <para><b>Rotation amount</b> — resolved from payload in this priority:</para>
    /// <list type="number">
    ///   <item><c>rotationDegPerUnit</c> &gt; 0 ⇒ angle = |endPos − startPos| × ratio (thread-pitch model).</item>
    ///   <item><c>totalRotationsDeg</c> &gt; 0 ⇒ angle = totalRotationsDeg (fixed turn count).</item>
    ///   <item>Otherwise zero rotation — effect degenerates to an easing-aware <c>axis_plunge</c>.</item>
    /// </list>
    /// </summary>
    internal sealed class ThreadInPartEffect : IPartEffect
    {
        private readonly Transform _part;
        private readonly Transform _previewRoot;
        private readonly Vector3 _startPos, _endPos;
        private readonly Quaternion _startRot;
        private readonly Vector3 _startScale, _endScale;
        private readonly Vector3 _axisLocal;   // unit, part-local
        private readonly float _totalRotDeg;   // resolved total rotation across [0..1]
        private readonly string _easing;
        private Vector3 _prevWorldPos;

        public ThreadInPartEffect(
            Transform part, Transform previewRoot,
            Vector3 startPos, Quaternion startRot, Vector3 startScale,
            Vector3 endPos, Vector3 endScale,
            Vector3 axisLocal, float totalRotDeg, string easing)
        {
            _part        = part;
            _previewRoot = previewRoot;
            _startPos    = startPos;
            _endPos      = endPos;
            _startRot    = startRot;
            _startScale  = startScale;
            _endScale    = endScale;
            _axisLocal   = axisLocal.sqrMagnitude > 1e-6f ? axisLocal.normalized : Vector3.up;
            _totalRotDeg = totalRotDeg;
            _easing      = easing;
        }

        public void Begin()
        {
            _part.localPosition = _startPos;
            _part.localRotation = _startRot;
            _part.localScale    = _startScale;
            _prevWorldPos = _previewRoot != null
                ? _previewRoot.TransformPoint(_startPos)
                : _part.position;
        }

        public Vector3 Apply(float progress)
        {
            float t = InteractionEasing.Apply(_easing, progress);

            _part.localPosition = Vector3.Lerp(_startPos, _endPos, t);
            _part.localScale    = Vector3.Lerp(_startScale, _endScale, t);

            if (_totalRotDeg != 0f)
            {
                Quaternion spin = Quaternion.AngleAxis(_totalRotDeg * t, _axisLocal);
                _part.localRotation = _startRot * spin;
            }

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
            _part.localScale    = _endScale;
            _part.localRotation = _totalRotDeg != 0f
                ? _startRot * Quaternion.AngleAxis(_totalRotDeg, _axisLocal)
                : _startRot;
        }

        /// <summary>
        /// Resolves the final rotation axis (part-local) from the authored payload,
        /// falling back to the normalized start→end translation direction when no
        /// axis is authored. Handles the <c>tool_action_axis</c> space.
        /// </summary>
        public static Vector3 ResolveAxisLocal(
            ToolPartInteraction payload, ToolPoseConfig toolPose,
            Vector3 startPos, Vector3 endPos)
        {
            if (payload?.axis != null && !string.IsNullOrEmpty(payload.axis.space))
            {
                Vector3 v = new Vector3(payload.axis.vec.x, payload.axis.vec.y, payload.axis.vec.z);
                if (v.sqrMagnitude > 1e-6f)
                {
                    switch (payload.axis.space)
                    {
                        case AxisSpaces.PartLocal:
                            return v.normalized;
                        case AxisSpaces.ToolActionAxis:
                            if (toolPose != null && toolPose.HasActionAxis)
                            {
                                var tp = toolPose.actionAxis;
                                Vector3 ta = new Vector3(tp.x, tp.y, tp.z);
                                if (ta.sqrMagnitude > 1e-6f) return ta.normalized;
                            }
                            return v.normalized;
                        // TargetLocal / World: fall through to auto-derive until Phase E
                        // wires target transform and world→part conversion.
                    }
                }
            }

            Vector3 delta = endPos - startPos;
            return delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.up;
        }

        /// <summary>
        /// Resolves the total rotation (degrees, across progress 0→1) from the payload.
        /// </summary>
        public static float ResolveTotalRotation(
            ToolPartInteraction payload, Vector3 startPos, Vector3 endPos)
        {
            if (payload == null) return 0f;
            if (payload.rotationDegPerUnit > 0f)
            {
                float distance = (endPos - startPos).magnitude;
                return payload.rotationDegPerUnit * distance;
            }
            return payload.totalRotationsDeg;
        }
    }
}
