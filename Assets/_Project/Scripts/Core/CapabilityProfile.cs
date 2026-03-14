namespace OSE.Core
{
    public readonly struct CapabilityProfile
    {
        public readonly CapabilityTier Tier;
        public readonly bool SupportsXR;
        public readonly bool SupportsHandTracking;
        public readonly bool SupportsVFXGraph;
        public readonly bool SupportsWebGPU;
        public readonly bool IsWebGL;
        public readonly bool IsMobile;
        public readonly int EstimatedVRAMMB;

        public CapabilityProfile(
            CapabilityTier tier,
            bool supportsXR,
            bool supportsHandTracking,
            bool supportsVFXGraph,
            bool supportsWebGPU,
            bool isWebGL,
            bool isMobile,
            int estimatedVRAMMB)
        {
            Tier = tier;
            SupportsXR = supportsXR;
            SupportsHandTracking = supportsHandTracking;
            SupportsVFXGraph = supportsVFXGraph;
            SupportsWebGPU = supportsWebGPU;
            IsWebGL = isWebGL;
            IsMobile = isMobile;
            EstimatedVRAMMB = estimatedVRAMMB;
        }

        public static CapabilityProfile Default => new CapabilityProfile(
            tier: CapabilityTier.Standard,
            supportsXR: false,
            supportsHandTracking: false,
            supportsVFXGraph: false,
            supportsWebGPU: false,
            isWebGL: false,
            isMobile: false,
            estimatedVRAMMB: 512);
    }
}
