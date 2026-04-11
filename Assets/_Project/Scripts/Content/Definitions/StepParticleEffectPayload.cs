using System;

namespace OSE.Content
{
    /// <summary>
    /// Per-step particle effect payload. Content authors configure particle bursts
    /// or continuous effects that play at runtime (and can be previewed in the editor).
    /// </summary>
    [Serializable]
    public sealed class StepParticleEffectPayload
    {
        public ParticleEffectEntry[] effects;
    }

    /// <summary>
    /// One particle effect entry. Maps a preset name to one or more target positions
    /// and controls duration and looping behaviour.
    /// </summary>
    [Serializable]
    public sealed class ParticleEffectEntry
    {
        /// <summary>
        /// Preset identifier. Must match a key registered in <c>CompletionParticleEffect</c>:
        /// "torque_sparks", "weld_glow", "weld_arc".
        /// </summary>
        public string presetId;

        /// <summary>Part IDs whose assembled positions are used as spawn points.</summary>
        public string[] targetPartIds;

        /// <summary>
        /// "onActivate" (default) or "afterDelay".
        /// </summary>
        public string trigger;

        /// <summary>Delay in seconds when trigger is "afterDelay".</summary>
        public float delaySeconds;

        /// <summary>
        /// Duration in seconds. 0 = run indefinitely (until step navigation or manual stop).
        /// </summary>
        public float durationSeconds;

        /// <summary>
        /// When true, the effect restarts when its duration elapses instead of stopping.
        /// Ignored when durationSeconds is 0 (already indefinite).
        /// </summary>
        public bool loop;

        /// <summary>
        /// Uniform scale multiplier applied to the spawned effect.
        /// 1.0 = default size as configured in the preset.
        /// </summary>
        public float scale;
    }
}
