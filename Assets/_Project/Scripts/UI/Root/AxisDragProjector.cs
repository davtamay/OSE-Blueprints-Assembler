using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Reusable utility for projecting a screen-space pointer onto a world-space axis line.
    /// Useful for measurement dragging, cable routing, alignment tools, and any interaction
    /// that constrains pointer movement to a single world-space direction.
    /// </summary>
    internal static class AxisDragProjector
    {
        /// <summary>
        /// Projects the current screen pointer position onto a world-space axis line.
        /// Returns the closest point on the axis to the camera ray through the pointer.
        /// </summary>
        /// <param name="cam">Active camera.</param>
        /// <param name="screenPos">Pointer position in screen pixels.</param>
        /// <param name="axisOrigin">World-space start of the axis (parameter t = 0).</param>
        /// <param name="axisEnd">World-space end of the axis (parameter t = 1).</param>
        /// <param name="projected">Closest point on the axis to the pointer ray.</param>
        /// <param name="t">Parameter along the axis: 0 = origin, 1 = end. Clamped to [0, 1].</param>
        /// <returns>True if projection succeeded (camera and axis are valid).</returns>
        public static bool TryProject(
            Camera cam,
            Vector2 screenPos,
            Vector3 axisOrigin,
            Vector3 axisEnd,
            out Vector3 projected,
            out float t)
        {
            projected = axisOrigin;
            t = 0f;

            if (cam == null)
                return false;

            Vector3 axisDir = axisEnd - axisOrigin;
            float axisLength = axisDir.magnitude;
            if (axisLength < 1e-6f)
                return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            ClosestPointsBetweenRays(
                axisOrigin, axisDir,
                ray.origin, ray.direction,
                out float tAxis, out _);

            t = Mathf.Clamp01(tAxis / axisLength);
            projected = axisOrigin + axisDir * t;
            return true;
        }

        /// <summary>
        /// Returns the screen-space distance in pixels between the pointer and a world point.
        /// </summary>
        public static float ScreenDistance(Camera cam, Vector2 screenPos, Vector3 worldPoint)
        {
            if (cam == null)
                return float.MaxValue;

            Vector3 sp = cam.WorldToScreenPoint(worldPoint);
            if (sp.z <= 0f)
                return float.MaxValue;

            return Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
        }

        /// <summary>
        /// Finds the closest points between two rays (line segments treated as infinite lines).
        /// tA is the parameter on ray A; tB is the parameter on ray B.
        /// </summary>
        private static void ClosestPointsBetweenRays(
            Vector3 originA, Vector3 dirA,
            Vector3 originB, Vector3 dirB,
            out float tA, out float tB)
        {
            Vector3 w = originA - originB;
            float a = Vector3.Dot(dirA, dirA);
            float b = Vector3.Dot(dirA, dirB);
            float c = Vector3.Dot(dirB, dirB);
            float d = Vector3.Dot(dirA, w);
            float e = Vector3.Dot(dirB, w);

            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-8f)
            {
                // Rays are nearly parallel — project origin onto A
                tA = 0f;
                tB = e / c;
                return;
            }

            tA = (b * e - c * d) / denom;
            tB = (a * e - b * d) / denom;
        }
    }
}
