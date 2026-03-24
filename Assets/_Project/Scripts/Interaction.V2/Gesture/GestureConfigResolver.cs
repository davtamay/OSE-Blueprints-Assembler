using OSE.Content;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Cascading resolver that produces a <see cref="GestureConfig"/> from:
    /// 1. Explicit <see cref="StepGesturePayload"/> on the step
    /// 2. Profile defaults from <see cref="GestureProfileRegistry"/>
    /// 3. Family default (Tap — backward compatible)
    /// </summary>
    public static class GestureConfigResolver
    {
        /// <summary>
        /// Resolve the gesture type and configuration for a step.
        /// All modes engage the gesture — difficulty controls thresholds and guidance:
        ///   "easy" / null  → full guides, relaxed thresholds, fast auto-assist (3s)
        ///   "standard"     → guides, normal thresholds, slower auto-assist (5s)
        ///   "realistic"    → no guides, strict thresholds, no auto-assist
        /// </summary>
        public static GestureConfig Resolve(StepDefinition step)
        {
            string gestureMode = step.difficulty?.gestureMode;
            if (string.IsNullOrEmpty(gestureMode))
                gestureMode = "easy";

            bool isEasy = gestureMode.Equals("easy", System.StringComparison.OrdinalIgnoreCase);
            bool isRealistic = gestureMode.Equals("realistic", System.StringComparison.OrdinalIgnoreCase);

            // Start with defaults
            GestureConfig config = GestureConfig.Default;
            config.GestureMode = gestureMode;
            config.ShowGuide = !isRealistic;  // guides for easy + standard
            config.AutoAssistDelay = isRealistic ? 0f : (isEasy ? 3f : 5f);

            // Easy mode: halve thresholds so gestures complete faster
            if (isEasy)
            {
                config.TargetAngleDegrees *= 0.5f;
                config.TargetPullDistance *= 0.5f;
                config.HoldDurationSeconds *= 0.5f;
                config.StrikeSpeedThreshold *= 0.6f;
            }

            // Layer 2: Profile defaults
            GestureProfileRegistry.ProfileDefaults? profileDefaults =
                GestureProfileRegistry.GetDefaults(step.profile);

            if (profileDefaults.HasValue)
            {
                var pd = profileDefaults.Value;
                if (pd.TargetAngleDegrees > 0f)
                    config.TargetAngleDegrees = pd.TargetAngleDegrees;
                if (pd.TargetPullDistance > 0f)
                    config.TargetPullDistance = pd.TargetPullDistance;
                if (pd.HoldDurationSeconds > 0f)
                    config.HoldDurationSeconds = pd.HoldDurationSeconds;
                if (pd.StrikeSpeedThreshold > 0f)
                    config.StrikeSpeedThreshold = pd.StrikeSpeedThreshold;
            }

            // Layer 1: Explicit step payload overrides
            StepGesturePayload payload = step.gesture;
            if (payload != null)
            {
                if (payload.targetAngleDegrees > 0f)
                    config.TargetAngleDegrees = payload.targetAngleDegrees;
                if (payload.targetPullDistance > 0f)
                    config.TargetPullDistance = payload.targetPullDistance;
                if (payload.holdDurationSeconds > 0f)
                    config.HoldDurationSeconds = payload.holdDurationSeconds;
                config.ShowGuide = payload.showGestureGuide;
            }

            return config;
        }

        /// <summary>
        /// Resolve the gesture type for a step using the cascade:
        /// explicit payload → profile default → Tap.
        /// </summary>
        public static GestureType ResolveGestureType(StepDefinition step)
        {
            // 1. Explicit gesture type on the step payload
            if (step.gesture != null && !string.IsNullOrEmpty(step.gesture.gestureType))
            {
                if (TryParseGestureType(step.gesture.gestureType, out GestureType explicit_))
                    return explicit_;
            }

            // 2. Profile default
            GestureProfileRegistry.ProfileDefaults? profileDefaults =
                GestureProfileRegistry.GetDefaults(step.profile);
            if (profileDefaults.HasValue)
                return profileDefaults.Value.GestureType;

            // 3. Family default
            return GestureType.Tap;
        }

        /// <summary>
        /// Returns true if the step should use gesture engagement.
        /// True for all Use-family steps — the master toggle in InteractionSettings
        /// gates whether the orchestrator enters ToolFocus at all.
        /// Returns false only for non-Use families or when gesture type resolves to Tap
        /// with no profile (pure click-to-complete fallback).
        /// </summary>
        public static bool IsGestureEngaged(StepDefinition step)
        {
            // Only Use-family steps have gesture engagement
            if (step.ResolvedFamily != Content.StepFamily.Use)
                return false;

            // If gesture type resolves to something beyond Tap, always engage
            GestureType type = ResolveGestureType(step);
            return type != GestureType.Tap;
        }

        private static bool TryParseGestureType(string value, out GestureType type)
        {
            switch (value)
            {
                case "Tap":           type = GestureType.Tap;           return true;
                case "RotaryTorque":  type = GestureType.RotaryTorque;  return true;
                case "LinearPull":    type = GestureType.LinearPull;    return true;
                case "SteadyHold":    type = GestureType.SteadyHold;    return true;
                case "PathTrace":     type = GestureType.PathTrace;     return true;
                case "ImpactStrike":  type = GestureType.ImpactStrike;  return true;
                default:              type = GestureType.Tap;           return false;
            }
        }
    }
}
