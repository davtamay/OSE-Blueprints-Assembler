using OSE.Core;

namespace OSE.Platform
{
    /// <summary>
    /// Describes runtime platform capabilities used to gate features
    /// and route interaction modes without depending on XRI directly.
    ///
    /// Implement in OSE.Platform. Consume in OSE.App, OSE.Interaction, OSE.UI
    /// via ServiceRegistry to avoid coupling business logic to platform specifics.
    /// </summary>
    public interface IPlatformCapabilityProvider
    {
        /// <summary>True when an XR device (headset or simulator) is active at startup.</summary>
        bool IsXRActive { get; }

        /// <summary>True when running on Android or iOS (native mobile, not WebGL).</summary>
        bool IsMobileStandalone { get; }

        /// <summary>True when running in a WebGL build.</summary>
        bool IsWebGL { get; }

        /// <summary>Capability tier for feature gating (Full / Reduced / Minimal).</summary>
        CapabilityTier Tier { get; }

        /// <summary>Human-readable platform description for logging.</summary>
        string PlatformDescription { get; }
    }
}
