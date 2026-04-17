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
        private Vector3[] _pivotsLocal;      // children centroid per target (zero → no counter-translate)
        private Quaternion[] _totalRotDelta;  // inv(fromRot) * toRot — precomputed rotation delta
        private float _lastLogTime;
        private Vector3 _lastWrotePos;       // diagnostic: detect post-Tick overrides
        private Quaternion _lastWroteRot;

        // Remember rigidbody state per target so we can restore on Stop.
        // When a target has a non-kinematic Rigidbody, physics fights our
        // transform writes every FixedUpdate — force it kinematic for the
        // duration of the animation.
        private Rigidbody[] _targetRigidbodies;
        private bool[]      _rbWasKinematic;

        // Per-child baseline snapshots. The runtime aggressively resets
        // Group_.localPosition every frame, so we cannot pivot by moving
        // the root. Snapshot each active child's local pose at Start, apply
        // rotation-around-centroid per child in Tick, restore on Stop.
        // O(N) per frame — fine for typical group sizes (10-100 members).
        private struct ChildBaseline
        {
            public Transform t;
            public Vector3 localPos;
            public Quaternion localRot;
        }
        private ChildBaseline[][] _childBaselines;

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
            _pivotsLocal = new Vector3[count];
            _totalRotDelta = new Quaternion[count];
            _targetRigidbodies = new Rigidbody[count];
            _rbWasKinematic    = new bool[count];
            _childBaselines    = new ChildBaseline[count][];

            for (int i = 0; i < count; i++)
            {
                _fromPoses[i] = ResolveFrom(context, i);
                _toPoses[i] = ResolveTo(context, i);

                if (_ctx.Targets[i] != null)
                {
                    var t = _ctx.Targets[i].transform;

                    // Force Rigidbody (if any) to kinematic so physics does
                    // not override our transform writes during the cue.
                    _targetRigidbodies[i] = _ctx.Targets[i].GetComponent<Rigidbody>();
                    if (_targetRigidbodies[i] != null)
                    {
                        _rbWasKinematic[i] = _targetRigidbodies[i].isKinematic;
                        if (!_rbWasKinematic[i])
                        {
                            _targetRigidbodies[i].linearVelocity = Vector3.zero;
                            _targetRigidbodies[i].angularVelocity = Vector3.zero;
                            _targetRigidbodies[i].isKinematic = true;
                        }
                    }

                    // Same centroid logic as OrientSubassemblyPlayer: compute
                    // children centroid in the target's local frame. For group
                    // roots this gives the geometric centre of the parts; for
                    // single-part targets (no children) it is (0,0,0) and the
                    // counter-translate in Tick collapses to zero.
                    // Prefer caller-provided pivot hint (derived from
                    // authored package data) over live-children centroid.
                    // Hint applies only to target[0] — that's where groups
                    // always land.
                    if (i == 0 && context.PivotHintLocal.HasValue)
                        _pivotsLocal[i] = context.PivotHintLocal.Value;
                    else
                        _pivotsLocal[i] = ComputeChildrenCentroidLocal(t);
                    int activeKids = 0, placedKids = 0;
                    for (int c = 0; c < t.childCount; c++)
                    {
                        var c_ = t.GetChild(c);
                        if (c_ == null) continue;
                        if (c_.gameObject.activeInHierarchy) activeKids++;
                        if (c_.localPosition.sqrMagnitude >= 0.0001f) placedKids++;
                    }
                    Debug.Log($"[PoseTransition] '{t.name}' active={activeKids}/{t.childCount} placed={placedKids} (animated) pivot={_pivotsLocal[i]}");
                    if (context.Entry != null && context.Entry.pivotOffsetOverride)
                    {
                        _pivotsLocal[i] += new Vector3(
                            context.Entry.pivotOffset.x,
                            context.Entry.pivotOffset.y,
                            context.Entry.pivotOffset.z);
                    }

                    // Precompute rotation delta: same concept as
                    // OrientSubassemblyPlayer._deltaRot but derived from
                    // authored from/to poses instead of a single Euler.
                    _totalRotDelta[i] = Quaternion.Inverse(_fromPoses[i].Rotation)
                                        * _toPoses[i].Rotation;

                    // Snapshot each child's local pose — INCLUDE inactive
                    // children as long as they have an authored (non-zero)
                    // localPosition. Inactive members represent parts not
                    // yet revealed; they must still move with the rotation
                    // so when a future step makes them visible they appear
                    // at the correct rotated position. Children sitting at
                    // localPosition (0,0,0) are truly unplaced placeholders —
                    // skip them so the pivot math doesn't snap them off origin.
                    int kids = 0;
                    for (int c = 0; c < t.childCount; c++)
                    {
                        var c_ = t.GetChild(c);
                        if (c_ == null) continue;
                        if (c_.localPosition.sqrMagnitude < 0.0001f) continue;
                        kids++;
                    }
                    var baselines = new ChildBaseline[kids];
                    int bi = 0;
                    for (int c = 0; c < t.childCount; c++)
                    {
                        var ch = t.GetChild(c);
                        if (ch == null) continue;
                        if (ch.localPosition.sqrMagnitude < 0.0001f) continue;
                        baselines[bi++] = new ChildBaseline
                        {
                            t = ch,
                            localPos = ch.localPosition,
                            localRot = ch.localRotation,
                        };
                    }
                    _childBaselines[i] = baselines;

                    t.localPosition = _fromPoses[i].Position;
                    t.localRotation = _fromPoses[i].Rotation;
                    if (_fromPoses[i].Scale.sqrMagnitude > 0.001f)
                        t.localScale = _fromPoses[i].Scale;
                }
            }
        }

        private static Vector3 ComputeChildrenCentroidLocal(Transform root)
        {
            if (root == null || root.childCount == 0) return Vector3.zero;
            // Count only active children with non-zero localPosition: those
            // are members already placed at their real positions. Inactive
            // members and freshly-parented members sit at (0,0,0) and would
            // drag the centroid toward the origin.
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.localPosition.sqrMagnitude < 0.0001f) continue; // skip unplaced
                sum += c.localPosition;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
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

                // Diagnostic: what's on the transform NOW vs what we wrote
                // last frame. If they diverge, something else wrote between
                // frames (post-Tick override, physics, etc).
                if (i == 0 && _elapsed > 0f)
                {
                    Vector3 posDrift = t.localPosition - _lastWrotePos;
                    float rotDrift = Quaternion.Angle(t.localRotation, _lastWroteRot);
                    if (posDrift.sqrMagnitude > 0.0001f || rotDrift > 0.1f)
                        Debug.LogWarning($"[PoseTransition.Drift] actualPos={t.localPosition} ourLastPos={_lastWrotePos} driftPos={posDrift} driftRotDeg={rotDrift:0.00}");
                }

                Quaternion fromRot = _fromPoses[i].Rotation;
                Quaternion toRot   = _toPoses[i].Rotation;

                // Rotation delta: how much rotation has been applied so far.
                Quaternion deltaRot = Quaternion.Slerp(fromRot, toRot, easedT)
                                      * Quaternion.Inverse(fromRot);
                if (_spinRevolutions > 0f)
                {
                    float spinAngle = easedT * _spinRevolutions * 360f;
                    deltaRot = deltaRot * Quaternion.AngleAxis(spinAngle, _spinAxis);
                }

                // Per-child animation around centroid. Bypasses root —
                // runtime aggressively resets root.localPosition every
                // frame, so we animate children directly. Formula in
                // root-local space:
                //   newLocalPos = C + deltaRot * (baseline - C)
                //   newLocalRot = deltaRot * baselineRot
                Vector3 C = _pivotsLocal[i];
                var baselines = _childBaselines != null && i < _childBaselines.Length ? _childBaselines[i] : null;
                if (baselines != null && baselines.Length > 0)
                {
                    for (int k = 0; k < baselines.Length; k++)
                    {
                        var ch = baselines[k].t;
                        if (ch == null) continue;
                        ch.localPosition = C + deltaRot * (baselines[k].localPos - C);
                        ch.localRotation = deltaRot * baselines[k].localRot;
                    }
                }
                else
                {
                    // Single-part target → animate directly with counter-translate.
                    t.localRotation = fromRot * deltaRot;
                    Vector3 counter = C - deltaRot * C;
                    Vector3 posLerp = Vector3.Lerp(_fromPoses[i].Position, _toPoses[i].Position, easedT);
                    t.localPosition = posLerp + fromRot * counter;
                }

                if (_fromPoses[i].Scale.sqrMagnitude > 0.001f && _toPoses[i].Scale.sqrMagnitude > 0.001f)
                    t.localScale = Vector3.Lerp(_fromPoses[i].Scale, _toPoses[i].Scale, easedT);

                if (i == 0)
                {
                    _lastWrotePos = t.localPosition;
                    _lastWroteRot = t.localRotation;
                    if (_elapsed - _lastLogTime > 0.2f)
                    {
                        _lastLogTime = _elapsed;
                        int animatedChildren = _childBaselines != null && i < _childBaselines.Length && _childBaselines[i] != null ? _childBaselines[i].Length : 0;
                        Debug.Log($"[PoseTransition.Tick] t={rawT:0.00} pivot={C} animatedChildren={animatedChildren} deltaEuler={deltaRot.eulerAngles}");
                    }
                }
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
            IsPlaying = false;

            // Authored opt-in: when true, leave children at their final
            // animated pose. Otherwise revert to baseline (default).
            bool hold = _ctx.Entry != null && _ctx.Entry.holdAtEnd;

            for (int i = 0; i < _ctx.Targets.Count; i++)
            {
                if (_ctx.Targets[i] == null || i >= _fromPoses.Length) continue;
                var t = _ctx.Targets[i].transform;

                if (!hold)
                {
                    // Restore to fromPose so other editor systems aren't
                    // confused by a displaced root.
                    t.localPosition = _fromPoses[i].Position;
                    t.localRotation = _fromPoses[i].Rotation;
                    if (_fromPoses[i].Scale.sqrMagnitude > 0.001f)
                        t.localScale = _fromPoses[i].Scale;

                    // Restore each child's baseline local pose.
                    var baselines = _childBaselines != null && i < _childBaselines.Length ? _childBaselines[i] : null;
                    if (baselines != null)
                    {
                        for (int k = 0; k < baselines.Length; k++)
                        {
                            var ch = baselines[k].t;
                            if (ch == null) continue;
                            ch.localPosition = baselines[k].localPos;
                            ch.localRotation = baselines[k].localRot;
                        }
                    }
                }
                // When hold == true: leave children/target at whatever pose
                // the last Tick wrote. Subsequent step transitions will
                // re-establish positions via the normal spawner refresh.

                // Always restore Rigidbody kinematic state — that's a
                // control concern, not a pose concern.
                if (_targetRigidbodies != null && i < _targetRigidbodies.Length
                    && _targetRigidbodies[i] != null
                    && !_rbWasKinematic[i])
                {
                    _targetRigidbodies[i].isKinematic = false;
                }
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
