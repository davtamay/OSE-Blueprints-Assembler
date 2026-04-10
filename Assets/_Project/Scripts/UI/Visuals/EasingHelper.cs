using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Easing functions for animation cue players.
    /// </summary>
    internal static class EasingHelper
    {
        public static float Apply(string easing, float t) => easing switch
        {
            "linear"    => t,
            "easeInOut" => t * t * (3f - 2f * t),
            _           => Mathf.SmoothStep(0f, 1f, t),
        };
    }
}
