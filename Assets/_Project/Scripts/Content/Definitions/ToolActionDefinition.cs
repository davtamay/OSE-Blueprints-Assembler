using System;

namespace OSE.Content
{
    /// <summary>
    /// Authoring override for whether the tool model snaps to the target's
    /// authored pose during the action. Three-state to keep the JSON minimal
    /// while still letting authors override either direction:
    /// <list type="bullet">
    /// <item><c>Inherit</c> (0, default) — fall back to the step's profile
    /// descriptor (<c>ToolProfileRegistry</c>). The normalizer resolves this
    /// at load.</item>
    /// <item><c>ForceOn</c> (1) — snap regardless of profile. Use when a
    /// Measure or Tape profile actually wants the tool placed on the target.</item>
    /// <item><c>ForceOff</c> (2) — never snap, regardless of profile. Use when
    /// a Square or Weld profile shouldn't pre-place the tool.</item>
    /// </list>
    /// Decouples visible behavior from the opaque <c>step.profile</c> string.
    /// </summary>
    public enum SnapToolToTargetMode
    {
        Inherit = 0,
        ForceOn = 1,
        ForceOff = 2,
    }

    [Serializable]
    public sealed class ToolActionDefinition
    {
        public string id;
        public string toolId;
        public string actionType;
        public string targetId;
        public int requiredCount = 1;
        public string successMessage;
        public string failureMessage;

        /// <summary>
        /// Optional authored description of HOW the tool drives the part.
        /// Null ≡ "lerp archetype with auto-derived axis" (pre-payload behavior).
        /// See <see cref="ToolPartInteraction"/>.
        /// </summary>
        public ToolPartInteraction interaction;

        /// <summary>
        /// Per-action override of preview behavioural constants (durations,
        /// vibration amp/freq, spark thresholds, colours, etc.) — see
        /// <see cref="ToolActionPreviewConfig"/>. Null or any unset field
        /// (0 / empty / alpha-zero) means "use the preview class's hardcoded
        /// default". Lets authors tune a specific tool action without
        /// affecting other steps that use the same profile.
        /// </summary>
        public ToolActionPreviewConfig previewConfig;

        /// <summary>
        /// Override for "tool model snaps to target during the action".
        /// <c>Inherit</c> (default) defers to the step profile's
        /// <c>SkipPreview</c> flag. See <see cref="SnapToolToTargetMode"/>
        /// and <c>CompletedStepResolver</c>-style decoupling: behavior is
        /// authored on the binding, not derived from the profile name.
        /// </summary>
        public int snapToolToTargetMode;

        /// <summary>
        /// Resolves the effective snap-to-target flag for this binding.
        /// When the author has not overridden, falls back to the inverse of
        /// the profile descriptor's <c>SkipPreview</c> — i.e. profiles that
        /// run the tool-action preview snap by default; profiles that opt
        /// out (Measure) do not.
        /// </summary>
        public bool ResolveSnapToolToTarget(bool profileSkipsPreview)
        {
            switch ((SnapToolToTargetMode)snapToolToTargetMode)
            {
                case SnapToolToTargetMode.ForceOn:  return true;
                case SnapToolToTargetMode.ForceOff: return false;
                default:                            return !profileSkipsPreview;
            }
        }
    }
}
