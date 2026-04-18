using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Shared easing functions for <see cref="OSE.Interaction.IPartEffect"/>
    /// implementations. Matches the canonical vocabulary used by
    /// <c>AnimationCueEntry.easing</c> ("linear", "smoothStep", "easeIn",
    /// "easeOut", "easeInOut"). Unknown / empty values default to linear.
    /// </summary>
    internal static class InteractionEasing
    {
        public const string Linear     = "linear";
        public const string SmoothStep = "smoothStep";
        public const string EaseIn     = "easeIn";
        public const string EaseOut    = "easeOut";
        public const string EaseInOut  = "easeInOut";

        public static float Apply(string easing, float t)
        {
            t = Mathf.Clamp01(t);
            switch (easing)
            {
                case SmoothStep: return t * t * (3f - 2f * t);
                case EaseIn:     return t * t;
                case EaseOut:    return 1f - (1f - t) * (1f - t);
                case EaseInOut:  return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case Linear:
                case null:
                case "":
                default:         return t;
            }
        }
    }
}
