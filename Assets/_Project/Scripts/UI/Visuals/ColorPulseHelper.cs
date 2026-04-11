using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>Sine-wave colour pulse helper shared by pulsing material effects.</summary>
    internal static class ColorPulseHelper
    {
        /// <summary>
        /// Returns a colour lerped between <paramref name="a"/> and <paramref name="b"/>
        /// using a sine wave at <paramref name="angularSpeed"/> radians per second.
        /// Uses <c>Time.time</c> — suitable for runtime MonoBehaviour Update calls.
        /// </summary>
        public static Color Lerp(Color a, Color b, float angularSpeed)
            => Lerp(a, b, angularSpeed, Time.time);

        /// <summary>
        /// Returns a colour lerped between <paramref name="a"/> and <paramref name="b"/>
        /// using a sine wave at <paramref name="angularSpeed"/> radians per second,
        /// driven by an explicit <paramref name="elapsedTime"/>.
        /// Use this overload in editor preview code where <c>Time.time</c> is always 0.
        /// </summary>
        public static Color Lerp(Color a, Color b, float angularSpeed, float elapsedTime)
            => Color.Lerp(a, b, 0.5f + 0.5f * Mathf.Sin(elapsedTime * angularSpeed));
    }
}
