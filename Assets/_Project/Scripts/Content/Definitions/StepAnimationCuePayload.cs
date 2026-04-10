using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step animation cue payload. Content authors specify animation cues
    /// in machine.json and the runtime plays them at step activation time.
    /// </summary>
    [Serializable]
    public sealed class StepAnimationCuePayload
    {
        public AnimationCueEntry[] cues;

        /// <summary>
        /// When &gt; 0, preview ghosts are deferred until this many seconds after
        /// step activation. Use to let orientation/demonstration cues play before
        /// ghosts appear. 0 = spawn immediately (default).
        /// </summary>
        public float previewDelaySeconds;
    }

    /// <summary>
    /// One animation cue entry. Each entry drives a single procedural animation
    /// (placement demonstration, pose transition, pulse, orientation flip, etc.)
    /// on one or more target GameObjects.
    /// </summary>
    [Serializable]
    public sealed class AnimationCueEntry
    {
        /// <summary>
        /// Animation type key: "demonstratePlacement", "poseTransition", "pulse", "orientSubassembly".
        /// Matched to an <c>IAnimationCuePlayer</c> factory by the coordinator.
        /// </summary>
        public string type;

        /// <summary>Part IDs to animate (resolved via FindSpawnedPart).</summary>
        public string[] targetPartIds;

        /// <summary>Tool IDs to animate (resolved via ToolCursorManager / PersistentToolController).</summary>
        public string[] targetToolIds;

        /// <summary>Subassembly ID to animate (resolved via SubassemblyPlacementController proxy).</summary>
        public string targetSubassemblyId;

        /// <summary>"onActivate" (default) or "afterDelay".</summary>
        public string trigger;

        /// <summary>Delay in seconds when trigger is "afterDelay".</summary>
        public float delaySeconds;

        /// <summary>Duration in seconds. 0 = type default.</summary>
        public float durationSeconds;

        /// <summary>When true, animation restarts on completion instead of stopping.</summary>
        public bool loop;

        /// <summary>"smoothStep" (default), "linear", or "easeInOut".</summary>
        public string easing;

        /// <summary>
        /// "part" (default) = animate the actual spawned part/tool.
        /// "ghost" = create a transparent clone and animate that instead.
        /// </summary>
        public string target;

        // ── Type-specific (optional, ignored by other types) ──

        /// <summary>Explicit start pose for poseTransition.</summary>
        public AnimationPose fromPose;

        /// <summary>Explicit end pose for poseTransition.</summary>
        public AnimationPose toPose;

        /// <summary>Euler rotation for orientSubassembly.</summary>
        public SceneFloat3 subassemblyRotation;

        /// <summary>Pulse color A (RGBA).</summary>
        public SceneFloat4 pulseColorA;

        /// <summary>Pulse color B (RGBA).</summary>
        public SceneFloat4 pulseColorB;

        /// <summary>Pulse speed in rad/s. Default 3.0.</summary>
        public float pulseSpeed;

        // ── Bolt drill-down ──

        /// <summary>
        /// Number of full rotations during demonstratePlacement (bolt screw effect).
        /// 0 = no spin. e.g., 4 = bolt makes 4 turns while traveling to assembled pose.
        /// </summary>
        public float spinRevolutions;

        /// <summary>
        /// Local axis for spin rotation. Defaults to (0,1,0) = Y-up (bolt shaft).
        /// </summary>
        public SceneFloat3 spinAxis;

        // ── Future: GLB-embedded animation support ──

        /// <summary>
        /// When set, the player looks for an Animator/Animation component on the
        /// spawned part and plays the named clip instead of procedural lerp.
        /// Not implemented in Phase 1 — data field reserved for forward compatibility.
        /// </summary>
        public string animationClipName;
    }

    /// <summary>
    /// Explicit pose for animation cue from/to endpoints.
    /// </summary>
    [Serializable]
    public sealed class AnimationPose
    {
        public SceneFloat3 position;
        public SceneQuaternion rotation;
        public SceneFloat3 scale;
    }
}
