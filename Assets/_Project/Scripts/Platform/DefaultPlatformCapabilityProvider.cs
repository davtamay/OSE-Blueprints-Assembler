using OSE.Core;
using UnityEngine;
using UnityEngine.XR;

namespace OSE.Platform
{
    /// <summary>
    /// Default platform capability provider. Detects XR, mobile, and WebGL
    /// using base Unity APIs only — no XRI dependency required.
    ///
    /// Register in AppBootstrap before OSE.Interaction initializes:
    /// <code>
    ///     ServiceRegistry.Register&lt;IPlatformCapabilityProvider&gt;(new DefaultPlatformCapabilityProvider());
    /// </code>
    /// </summary>
    public sealed class DefaultPlatformCapabilityProvider : IPlatformCapabilityProvider
    {
        private readonly bool _isXRActive;
        private readonly bool _isMobileStandalone;
        private readonly bool _isWebGL;
        private readonly CapabilityTier _tier;
        private readonly string _platformDescription;

        public DefaultPlatformCapabilityProvider()
        {
            _isXRActive = XRSettings.isDeviceActive;

#if UNITY_WEBGL && !UNITY_EDITOR
            _isWebGL = true;
            _isMobileStandalone = false;
#elif UNITY_ANDROID || UNITY_IOS
            _isWebGL = false;
            _isMobileStandalone = !_isXRActive; // XR Android (Quest) is not treated as mobile
#else
            _isWebGL = false;
            _isMobileStandalone = false;
#endif

            _tier = _isXRActive
                ? CapabilityTier.Full
                : _isMobileStandalone
                    ? CapabilityTier.Standard
                    : CapabilityTier.Full;

            _platformDescription = BuildDescription();
        }

        public bool IsXRActive => _isXRActive;
        public bool IsMobileStandalone => _isMobileStandalone;
        public bool IsWebGL => _isWebGL;
        public CapabilityTier Tier => _tier;
        public string PlatformDescription => _platformDescription;

        private string BuildDescription()
        {
#if UNITY_EDITOR
            string context = "Editor";
#elif UNITY_WEBGL
            string context = "WebGL";
#elif UNITY_ANDROID
            string context = "Android";
#elif UNITY_IOS
            string context = "iOS";
#else
            string context = "Standalone";
#endif
            string xr = _isXRActive ? $"+XR({XRSettings.loadedDeviceName})" : string.Empty;
            return $"{context}{xr} [Tier:{_tier}]";
        }
    }
}
