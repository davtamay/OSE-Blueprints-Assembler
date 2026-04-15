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

        /// <summary>
        /// Part IDs to animate. Legacy / step-scoped authoring only — new
        /// authoring puts cues on the host (<see cref="PartDefinition.animationCues"/>
        /// or <see cref="SubassemblyDefinition.animationCues"/>) where the
        /// host is the implicit target. Runtime still reads this as a
        /// fallback so unmigrated JSON keeps working.
        /// </summary>
        public string[] targetPartIds;

        /// <summary>Tool IDs to animate (resolved via ToolCursorManager / PersistentToolController).</summary>
        public string[] targetToolIds;

        /// <summary>Subassembly ID to animate (resolved via SubassemblyPlacementController proxy).</summary>
        public string targetSubassemblyId;

        /// <summary>
        /// Step ids at which this cue fires. Empty / null = every step where
        /// the host is visible. Only meaningful when the cue is authored on
        /// a host (part / subassembly / aggregate); step-owned legacy cues
        /// implicitly scope to their owning step.
        /// </summary>
        public string[] stepIds;

        /// <summary>
        /// When true, the player restarts its loop on every qualifying step
        /// instead of running once. Equivalent to authoring the cue once
        /// with <see cref="stepIds"/> empty plus <see cref="loop"/> true —
        /// a shorthand for "always-on while host visible".
        /// </summary>
        public bool always;

        /// <summary>
        /// Package-relative prefab path for <see cref="type"/> = "particle".
        /// Coordinator instantiates the prefab at the host's world pose on
        /// trigger, destroys it when the step ends (or on loop restart).
        /// Ignored for non-particle cue types.
        /// </summary>
        public string particlePrefabRef;

        /// <summary>"onActivate" (default), "afterDelay", "onStepComplete", or "always".</summary>
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

        // ── Shake ──

        /// <summary>
        /// Peak displacement in metres for the "shake" animation type.
        /// Default 0.01 (1 cm). Oscillation is centred on the target's
        /// position at the moment <c>Start()</c> is called.
        /// </summary>
        public float shakeAmplitude;

        /// <summary>
        /// Oscillations per second for the "shake" type. Default 8 Hz.
        /// </summary>
        public float shakeFrequency;

        /// <summary>
        /// Local-space axis along which the shake oscillates.
        /// Default (1, 0, 0) — side-to-side. Normalised at runtime.
        /// </summary>
        public SceneFloat3 shakeAxis;

        // ── Future: GLB-embedded animation support ──

        /// <summary>
        /// When set, the player looks for an Animator/Animation component on the
        /// spawned part and plays the named clip instead of procedural lerp.
        /// Not implemented in Phase 1 — data field reserved for forward compatibility.
        /// </summary>
        public string animationClipName;

        // ── Timing-panel authoring (parallel/sequenced rows grouped by trigger) ──

        /// <summary>Order within this cue's (scope, trigger) timing panel.</summary>
        public int panelOrder;

        /// <summary>
        /// When true, this row waits for the previous row in the same panel to
        /// finish before starting. When false, it runs in parallel with prior
        /// rows. Runtime wiring lands in Phase 2 — authored/persisted now.
        /// </summary>
        public bool sequenceAfterPrevious;

        /// <summary>
        /// Optional asset path for a custom animation clip/asset, paired with
        /// <c>type = "animationClip"</c>. Distinct from <c>animationClipName</c>
        /// (which targets a GLB-embedded clip).
        /// </summary>
        public string animationClipAssetPath;
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
