using System.Collections.Generic;
using OSE.Content;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Interface for a single animation cue player implementation.
    /// Each player handles one animation type (e.g., demonstratePlacement, pulse).
    /// </summary>
    public interface IAnimationCuePlayer
    {
        string AnimationType { get; }
        void Start(AnimationCueContext context);
        /// <summary>Returns true while still animating.</summary>
        bool Tick(float deltaTime);
        void Stop();
        bool IsPlaying { get; }

        /// <summary>
        /// Progress-driven tick for cues with <c>trigger = "onDuringAction"</c>
        /// and an authored <c>[startProgress, endProgress]</c> range. The
        /// coordinator calls this with <paramref name="progress01"/> already
        /// remapped to the cue's local 0..1 window (0 at startProgress, 1 at
        /// endProgress). Tween-style players should sample their animation at
        /// <paramref name="progress01"/>. Default implementation is a no-op —
        /// time-based players stay on their wall-clock Tick() path.
        /// </summary>
        void TickProgress(float progress01) { /* no-op default */ }
    }

    /// <summary>
    /// Resolved runtime context passed to <see cref="IAnimationCuePlayer.Start"/>.
    /// Contains the cue entry, resolved target GameObjects, and pose data.
    /// </summary>
    public readonly struct AnimationCueContext
    {
        /// <summary>The raw cue entry from machine.json.</summary>
        public readonly AnimationCueEntry Entry;

        /// <summary>Resolved target GameObjects (parts, tools, or partGroup roots).</summary>
        public readonly List<GameObject> Targets;

        /// <summary>
        /// Per-target start poses (local position/rotation/scale).
        /// Parallel to <see cref="Targets"/>. Null for types that don't use poses.
        /// </summary>
        public readonly List<AnimationCueResolvedPose> StartPoses;

        /// <summary>
        /// Per-target assembled poses (local position/rotation/scale).
        /// Parallel to <see cref="Targets"/>. Null for types that don't use poses.
        /// </summary>
        public readonly List<AnimationCueResolvedPose> AssembledPoses;

        /// <summary>Effective duration (entry override or type default).</summary>
        public readonly float Duration;

        /// <summary>
        /// Ghost GameObjects created for "ghost" target mode.
        /// The coordinator owns destruction; players just animate them.
        /// Null when target mode is "part".
        /// </summary>
        public readonly List<GameObject> Ghosts;

        /// <summary>
        /// Optional pivot hint in the first target's local frame. When set
        /// (non-null), players that do centroid-pivot rotation (e.g.
        /// PoseTransitionPlayer, OrientPartGroupPlayer) should use this
        /// instead of computing centroid from live children. Lets callers
        /// pass a stable pivot derived from authored data (e.g. the average
        /// of member parts' assembledPosition) so rotation pivots correctly
        /// regardless of hierarchy / active-state flakiness at runtime.
        /// </summary>
        public readonly Vector3? PivotHintLocal;

        /// <summary>
        /// Optional package reference the coordinator supplies for cues that
        /// need to query authored metadata (firstVisibleSeq, stepPoses, etc.)
        /// at play time. Null for contexts where no such lookup is needed.
        /// </summary>
        public readonly Content.MachinePackageDefinition Package;

        /// <summary>
        /// Sequence index of the step this cue is firing on. Used by
        /// <see cref="PoseTransitionPlayer"/> to filter baseline capture —
        /// children whose <c>firstVisibleSeq &gt;= StepSeq</c> are
        /// being introduced on this step and sit at their
        /// <c>startPosition</c> (tray/staging). Animating them drags them
        /// away from the tray and breaks task pickup. <c>-1</c> means "no
        /// filter" (player falls back to "animate every active child").
        /// </summary>
        public readonly int StepSeq;

        public AnimationCueContext(
            AnimationCueEntry entry,
            List<GameObject> targets,
            List<AnimationCueResolvedPose> startPoses,
            List<AnimationCueResolvedPose> assembledPoses,
            float duration,
            List<GameObject> ghosts = null,
            Vector3? pivotHintLocal = null,
            Content.MachinePackageDefinition package = null,
            int stepSeq = -1)
        {
            Entry = entry;
            Targets = targets;
            StartPoses = startPoses;
            AssembledPoses = assembledPoses;
            Duration = duration;
            Ghosts = ghosts;
            PivotHintLocal = pivotHintLocal;
            Package = package;
            StepSeq = stepSeq;
        }
    }

    /// <summary>
    /// Resolved local-space pose for a single target.
    /// </summary>
    public struct AnimationCueResolvedPose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }
}
