using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>Sine-wave colour pulse helper shared by pulsing material effects.</summary>
    internal static class ColorPulseHelper
    {
        /// <summary>
        /// Returns a colour lerped between <paramref name="a"/> and <paramref name="b"/>
        /// using a sine wave at <paramref name="angularSpeed"/> radians per second.
        /// </summary>
        public static Color Lerp(Color a, Color b, float angularSpeed)
            => Color.Lerp(a, b, 0.5f + 0.5f * Mathf.Sin(Time.time * angularSpeed));
    }
}
