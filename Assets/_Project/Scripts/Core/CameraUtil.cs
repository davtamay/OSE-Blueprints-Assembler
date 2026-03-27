using UnityEngine;

namespace OSE.Core
{
    /// <summary>
    /// Drop-in replacement for <c>Camera.main</c>. When a provider is registered
    /// via <see cref="SetProvider"/>, returns the cached camera; otherwise falls back
    /// to <c>Camera.main</c>. <see cref="OSE.Interaction.CameraMainProvider"/> calls
    /// <see cref="SetProvider"/> on Awake and <see cref="ClearProvider"/> on OnDestroy.
    /// </summary>
    public static class CameraUtil
    {
        private static System.Func<Camera> _provider;

        /// <summary>Called by CameraMainProvider.Awake to register the scene camera.</summary>
        public static void SetProvider(System.Func<Camera> provider) => _provider = provider;

        /// <summary>Called by CameraMainProvider.OnDestroy to release the reference.</summary>
        public static void ClearProvider() => _provider = null;

        /// <summary>
        /// Returns the active scene camera. Uses the registered provider when available;
        /// falls back to <c>Camera.main</c>.
        /// </summary>
        public static Camera GetMain() => _provider?.Invoke() ?? Camera.main;
    }
}
