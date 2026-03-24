using System;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Maps Use-family profiles to their default <see cref="GestureType"/> and configuration.
    /// This is the second tier in the gesture resolution cascade:
    /// step payload → profile default → family default (Tap).
    /// </summary>
    public static class GestureProfileRegistry
    {
        public struct ProfileDefaults
        {
            public GestureType GestureType;
            public float TargetAngleDegrees;
            public float TargetPullDistance;
            public float HoldDurationSeconds;
            public float StrikeSpeedThreshold;
        }

        /// <summary>
        /// Returns the default gesture configuration for the given Use-family profile.
        /// Returns null if the profile has no registered defaults (falls through to Tap).
        /// </summary>
        public static ProfileDefaults? GetDefaults(string profile)
        {
            if (string.IsNullOrEmpty(profile))
                return null;

            if (profile.Equals(ToolActionProfiles.Torque, StringComparison.OrdinalIgnoreCase))
                return new ProfileDefaults
                {
                    GestureType = GestureType.RotaryTorque,
                    TargetAngleDegrees = 90f,
                };

            if (profile.Equals(ToolActionProfiles.Measure, StringComparison.OrdinalIgnoreCase))
                return new ProfileDefaults
                {
                    GestureType = GestureType.LinearPull,
                    TargetPullDistance = 0.3f,
                };

            if (profile.Equals(ToolActionProfiles.Weld, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("solder", StringComparison.OrdinalIgnoreCase))
                return new ProfileDefaults
                {
                    GestureType = GestureType.SteadyHold,
                    HoldDurationSeconds = 2f,
                };

            if (profile.Equals(ToolActionProfiles.Cut, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("grind", StringComparison.OrdinalIgnoreCase))
                return new ProfileDefaults
                {
                    GestureType = GestureType.PathTrace,
                };

            if (profile.Equals(ToolActionProfiles.Strike, StringComparison.OrdinalIgnoreCase)
                || profile.Equals("hammer", StringComparison.OrdinalIgnoreCase))
                return new ProfileDefaults
                {
                    GestureType = GestureType.ImpactStrike,
                    StrikeSpeedThreshold = 800f,
                };

            return null;
        }
    }
}
