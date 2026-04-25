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

        /// <summary>
        /// True when the payload carries no authored content. JsonUtility creates
        /// a default instance for any [Serializable] reference field that's
        /// absent from JSON; the normalizer uses this to drop those phantoms so
        /// downstream <c>!= null</c> checks behave like the JSON intends.
        /// </summary>
        public bool IsEmpty()
        {
            if (cues != null && cues.Length > 0) return false;
            if (previewDelaySeconds != 0f) return false;
            return true;
        }
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
        /// Animation type key. Core: "demonstratePlacement", "poseTransition",
        /// "pulse", "orientSubassembly", "shake", "particle". Phase-2 effect
        /// cues: "emissionPulse", "colorTween", "materialFade", "clickPop",
        /// "poseWobble", "toolVibration", "lineBetweenAnchors", "drawSpline",
        /// "measureLine", "screwSpin". Matched to an <c>IAnimationCuePlayer</c>
        /// factory by the coordinator.
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
        /// Package-relative prefab path for <see cref="type"/> = "particle" when
        /// <see cref="particleSourceMode"/> is "prefab". Ignored otherwise.
        /// </summary>
        public string particlePrefabRef;

        /// <summary>
        /// "preset" = instantiate a procedural preset from
        /// <c>CompletionParticleEffect.Presets</c> (weld_arc, torque_sparks, …);
        /// "prefab" = load <see cref="particlePrefabRef"/> via Resources.Load.
        /// Empty = inferred on load by the normalizer.
        /// </summary>
        public string particleSourceMode;

        /// <summary>Preset key into <c>CompletionParticleEffect.Presets</c> when <see cref="particleSourceMode"/> = "preset".</summary>
        public string particlePresetId;

        /// <summary>Scale multiplier applied to the spawned particle. 0 or unset = 1.0.</summary>
        public float particleScale;

        /// <summary>Optional colour tint multiplied into child ParticleSystem.main.startColor. Alpha = 0 means "no tint".</summary>
        public SceneFloat4 particleColorTint;

        /// <summary>
        /// "onActivate" (default), "afterDelay", "afterPartsShown",
        /// "onStepComplete", "onFirstInteraction", "onTaskComplete",
        /// "onDuringAction" (starts on tool action, stops on action end).
        /// </summary>
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

        /// <summary>
        /// Waveform mode for the shake:
        ///   "" / "sine"     — bidirectional oscillation (default, back-compat)
        ///   "positive"      — half-wave rectified: always travels in the axis's
        ///                     positive direction, returns to zero each cycle
        ///   "slide"         — single out-and-back pulse over the cue's duration
        ///                     (smoothstep ramp up to amplitude, then back to 0).
        ///                     Ideal for a rod slide test: slides forward, returns.
        /// </summary>
        public string shakeMode;

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

        // ── Pivot override (optional — default pivot is host mesh origin for
        //    parts and member centroid for subassemblies) ──

        /// <summary>
        /// When true, <see cref="pivotOffset"/> is applied to this cue's
        /// rotation / effect origin. When false (default), the host's default
        /// pivot is used (mesh origin for parts, <c>GroupRigidBody.groupCenter</c>
        /// for subassemblies) — existing content keeps identical runtime
        /// behavior.
        /// </summary>
        public bool pivotOffsetOverride;

        /// <summary>
        /// Local-space offset from the host's default pivot. Only honored
        /// when <see cref="pivotOffsetOverride"/> is true. Authored via the
        /// Scene-view pivot gizmo in TTAW; serialized at 4-decimal precision.
        /// </summary>
        public SceneFloat3 pivotOffset;

        /// <summary>
        /// When true, the player leaves children at their final animated
        /// pose at Stop instead of restoring the pre-animation baseline.
        /// Use for cues that should "stick" — e.g. an orientation flip that
        /// reveals an assembly's far face, where the trainee continues the
        /// step with the new orientation.
        /// Default false preserves the cosmetic-preview behavior (revert to
        /// fromPose), matching legacy content.
        /// </summary>
        public bool holdAtEnd;

        // ── Phase 2: progress-range scheduling (trigger=onDuringAction) ──

        /// <summary>
        /// First moment in the tool action's 0..1 progress timeline at which
        /// this cue fires (burst cues) or begins interpolating (tween cues).
        /// Only meaningful when <see cref="trigger"/> = "onDuringAction".
        /// Default 0.0 = start of the action.
        /// </summary>
        public float startProgress;

        /// <summary>
        /// Last moment in the tool action's 0..1 progress timeline at which
        /// this cue is active. Tween cues complete at endProgress. Only
        /// meaningful when <see cref="trigger"/> = "onDuringAction".
        /// Default 0.0 (treated as 1.0 by the coordinator when both start
        /// and end are zero — preserves legacy onDuringAction behaviour).
        /// </summary>
        public float endProgress;

        /// <summary>
        /// Progress value (0..1 GLOBAL action progress) at which a particle
        /// travel / lineBetweenAnchors sub-tween begins. Before this, the
        /// effect holds at its start anchor. When 0, defaults to
        /// <see cref="startProgress"/> — travel begins with the cue.
        /// Used to recreate WeldPreview's hold-at-start-then-travel profile:
        /// cue active 0.1–1.0, but travel only 0.15–0.9.
        /// </summary>
        public float travelStartProgress;

        /// <summary>
        /// Progress value (0..1 GLOBAL action progress) at which a particle
        /// travel / lineBetweenAnchors sub-tween ends. After this, the
        /// effect holds at its end anchor. When 0, defaults to
        /// <see cref="endProgress"/>.
        /// </summary>
        public float travelEndProgress;

        // ── Phase 2: emissionPulse / colorTween / materialFade ──

        /// <summary>Start colour of a color/emission tween (RGBA).</summary>
        public SceneFloat4 fromColor;

        /// <summary>End colour of a color/emission tween (RGBA).</summary>
        public SceneFloat4 toColor;

        /// <summary>Start emission colour for emissionPulse / materialFade (RGBA).</summary>
        public SceneFloat4 fromEmission;

        /// <summary>End emission colour for emissionPulse / materialFade (RGBA).</summary>
        public SceneFloat4 toEmission;

        /// <summary>Start emission intensity multiplier (default 0 = 1.0 when both from/to are zero).</summary>
        public float fromIntensity;

        /// <summary>End emission intensity multiplier.</summary>
        public float toIntensity;

        // ── Phase 2: clickPop ──

        /// <summary>Ring pulse scale for <c>clickPop</c> cues. Default 1.8.</summary>
        public float pulseScale;

        // ── Phase 2: poseWobble ──

        /// <summary>Local-space axis (Euler per-axis amplitudes) for wobble. (1,1,0) = pitch+yaw.</summary>
        public SceneFloat3 wobbleAxis;

        /// <summary>Peak rotation amplitude in radians for <c>poseWobble</c>.</summary>
        public float wobbleAmplitude;

        /// <summary>Angular frequency (rad/s) for <c>poseWobble</c>.</summary>
        public float wobbleFrequency;

        // ── Phase 2: toolVibration ──

        /// <summary>Per-axis amplitude vector (metres) for <c>toolVibration</c>.</summary>
        public SceneFloat3 vibrationAxes;

        /// <summary>Oscillation frequency (Hz) for <c>toolVibration</c>.</summary>
        public float vibrationFrequency;

        /// <summary>Ramp-in progress (0..1) — amplitude eases in over [0, rampIn].</summary>
        public float vibrationRampIn;

        /// <summary>Ramp-out progress (0..1) — amplitude eases out over [rampOut, 1].</summary>
        public float vibrationRampOut;

        // ── Phase 2: line / spline ──

        /// <summary>Anchor ref for endpoint A (e.g. "weldStart", "measureAnchorA", "literal:0,0,0").</summary>
        public string anchorARef;

        /// <summary>Anchor ref for endpoint B.</summary>
        public string anchorBRef;

        /// <summary>LineRenderer width for <c>lineBetweenAnchors</c>. Default 0.004.</summary>
        public float lineWidth;

        /// <summary>Emission intensity multiplier for line / spline materials.</summary>
        public float lineEmissionIntensity;

        /// <summary>Tube radius for <c>drawSpline</c>. Default 0.003.</summary>
        public float splineRadius;

        /// <summary>PBR metallic for <c>drawSpline</c>.</summary>
        public float splineMetallic;

        /// <summary>PBR smoothness for <c>drawSpline</c>.</summary>
        public float splineSmoothness;

        /// <summary>Ordered anchor refs for a multi-point spline path.</summary>
        public string[] splineAnchorRefs;

        // ── Phase 2: screwSpin ──

        /// <summary>Total rotation angle in degrees for <c>screwSpin</c> (120° matches torque wrench).</summary>
        public float spinAngleDegrees;

        // ── Phase 2: measure ──

        /// <summary>Display unit for <c>measureLine</c> readout: "mm", "cm", "m", "inch", "ft".</summary>
        public string measureUnit;
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
