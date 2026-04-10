using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.Interaction
{
    /// <summary>
    /// Single source of truth for profile-specific tuning values.
    /// All consumers (camera framing, preview controller, factory,
    /// view-mode resolver) read from here instead of maintaining parallel switch blocks.
    ///
    /// To add a new profile: add one entry to <see cref="Register"/> — every
    /// subsystem picks it up automatically.
    /// </summary>
    public static class ToolProfileRegistry
    {
        private static readonly Dictionary<string, ToolProfileDescriptor> Profiles
            = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<StepProfile, string> ProfileNames
            = new Dictionary<StepProfile, string>
        {
            { StepProfile.Clamp,       ToolActionProfiles.Clamp },
            { StepProfile.AxisFit,     ToolActionProfiles.AxisFit },
            { StepProfile.Torque,      ToolActionProfiles.Torque },
            { StepProfile.Weld,        ToolActionProfiles.Weld },
            { StepProfile.Cut,         ToolActionProfiles.Cut },
            { StepProfile.Strike,      ToolActionProfiles.Strike },
            { StepProfile.Measure,     ToolActionProfiles.Measure },
            { StepProfile.SquareCheck, ToolActionProfiles.SquareCheck },
            { StepProfile.Cable,       ToolActionProfiles.Cable },
            { StepProfile.WireConnect, ToolActionProfiles.WireConnect },
        };

        /// <summary>Returned when no profile matches.</summary>
        public static readonly ToolProfileDescriptor Default = new()
        {
            FramingDistance = 1.2f,
            WorkingDistance = 0.03f,
            ApproachTiltDegrees = 0f,
            PreviewStyle = PreviewStyle.Default,
            PreviewSpeedCap = 2.5f,
            SpawnClickEffect = false,
        };

        static ToolProfileRegistry()
        {
            // ── Use-family profiles ──

            Register(ToolActionProfiles.Torque, new ToolProfileDescriptor
            {
                FramingDistance = 1.0f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Drill,
                SpawnClickEffect = true,
            });

            Register(ToolActionProfiles.Weld, new ToolProfileDescriptor
            {
                FramingDistance = 0.8f,
                WorkingDistance = 0.005f,
                ApproachTiltDegrees = 12f,
                PreviewStyle = PreviewStyle.Weld,
                UseViewModeOverride = ViewMode.PathView,
                SpawnClickEffect = false,
            });
            RegisterAlias("solder", ToolActionProfiles.Weld);

            Register(ToolActionProfiles.Cut, new ToolProfileDescriptor
            {
                FramingDistance = 0.8f,
                WorkingDistance = 0.008f,
                ApproachTiltDegrees = 25f,
                PreviewStyle = PreviewStyle.Cut,
                UseViewModeOverride = ViewMode.PathView,
                SpawnClickEffect = false,
            });
            RegisterAlias("grind", ToolActionProfiles.Cut);

            Register(ToolActionProfiles.Strike, new ToolProfileDescriptor
            {
                FramingDistance = 1.0f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Default,
                SpawnClickEffect = false,
            });
            RegisterAlias("hammer", ToolActionProfiles.Strike);

            Register(ToolActionProfiles.SquareCheck, new ToolProfileDescriptor
            {
                FramingDistance = 1.0f,
                WorkingDistance = 0.005f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.SquareCheck,
                ObserveOnly = true,
                SpawnClickEffect = true,
            });

            Register(ToolActionProfiles.Measure, new ToolProfileDescriptor
            {
                FramingDistance = 1.2f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Default,
                UseViewModeOverride = ViewMode.PairEndpoints,
                SkipPreview = true,
                SpawnClickEffect = true,
            });

            // ── Place-family profiles ──

            Register(ToolActionProfiles.Clamp, new ToolProfileDescriptor
            {
                FramingDistance = 1.2f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Default,
                SpawnClickEffect = false,
            });

            Register(ToolActionProfiles.AxisFit, new ToolProfileDescriptor
            {
                FramingDistance = 1.2f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Default,
                PlaceViewModeOverride = ViewMode.WorkZone,
                SpawnClickEffect = false,
            });

            // ── Connect-family profiles ──

            Register(ToolActionProfiles.WireConnect, new ToolProfileDescriptor
            {
                FramingDistance = 1.2f,
                WorkingDistance = 0.03f,
                ApproachTiltDegrees = 0f,
                PreviewStyle = PreviewStyle.Default,
                UseViewModeOverride = ViewMode.PairEndpoints,
                SpawnClickEffect = false,
            });
        }

        /// <summary>
        /// Returns the descriptor for the given profile string.
        /// Case-insensitive. Returns <see cref="Default"/> when no match is found.
        /// </summary>
        public static ToolProfileDescriptor Get(string profile)
        {
            if (string.IsNullOrEmpty(profile))
                return Default;

            if (!Profiles.TryGetValue(profile, out var desc))
                return Default;

            // Struct fields default to 0 — fill in the default speed cap if unset.
            if (desc.PreviewSpeedCap <= 0f)
                desc.PreviewSpeedCap = Default.PreviewSpeedCap;

            return desc;
        }

        /// <summary>
        /// Returns the descriptor for a type-safe <see cref="StepProfile"/> enum value.
        /// Maps to the canonical string key then delegates to the string-based lookup.
        /// </summary>
        public static ToolProfileDescriptor Get(StepProfile profile)
        {
            return Get(ProfileToString(profile));
        }

        private static string ProfileToString(StepProfile profile)
        {
            ProfileNames.TryGetValue(profile, out var name);
            return name;
        }

        /// <summary>
        /// Returns true if the registry has an explicit entry for this profile.
        /// </summary>
        public static bool Has(string profile)
        {
            return !string.IsNullOrEmpty(profile) && Profiles.ContainsKey(profile);
        }

        private static void Register(string profile, ToolProfileDescriptor descriptor)
        {
            Profiles[profile] = descriptor;
        }

        private static void RegisterAlias(string alias, string canonicalProfile)
        {
            if (Profiles.TryGetValue(canonicalProfile, out var desc))
                Profiles[alias] = desc;
        }
    }
}
