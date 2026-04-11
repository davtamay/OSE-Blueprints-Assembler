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
    }

    /// <summary>
    /// Resolved runtime context passed to <see cref="IAnimationCuePlayer.Start"/>.
    /// Contains the cue entry, resolved target GameObjects, and pose data.
    /// </summary>
    public readonly struct AnimationCueContext
    {
        /// <summary>The raw cue entry from machine.json.</summary>
        public readonly AnimationCueEntry Entry;

        /// <summary>Resolved target GameObjects (parts, tools, or subassembly roots).</summary>
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

        public AnimationCueContext(
            AnimationCueEntry entry,
            List<GameObject> targets,
            List<AnimationCueResolvedPose> startPoses,
            List<AnimationCueResolvedPose> assembledPoses,
            float duration,
            List<GameObject> ghosts = null)
        {
            Entry = entry;
            Targets = targets;
            StartPoses = startPoses;
            AssembledPoses = assembledPoses;
            Duration = duration;
            Ghosts = ghosts;
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
