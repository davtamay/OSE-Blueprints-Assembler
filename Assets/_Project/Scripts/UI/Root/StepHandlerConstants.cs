using System.Collections.Generic;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Shared tuning constants and utility methods used across step family handlers
    /// (PlaceStepHandler, UseStepHandler, ConnectStepHandler).
    /// Centralizes values that were previously duplicated per handler.
    /// </summary>
    internal static class StepHandlerConstants
    {
        public static class Proximity
        {
            public const float DesktopPixels = 120f;
            public const float MobilePixels = 180f;
            public const float SubassemblyDesktopPixels = 220f;
            public const float SubassemblyMobilePixels = 300f;

            /// <summary>
            /// Returns the platform-appropriate screen proximity threshold in pixels.
            /// </summary>
            public static float GetThreshold(bool isSubassembly = false)
            {
                if (isSubassembly)
                    return Application.isMobilePlatform ? SubassemblyMobilePixels : SubassemblyDesktopPixels;
                return Application.isMobilePlatform ? MobilePixels : DesktopPixels;
            }
        }

        public static class Animation
        {
            public const float SnapLerpSpeed = 12f;
            public const float InvalidFlashDuration = 0.3f;
        }

        public static class Colors
        {
            public static readonly Color InvalidFlash = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        }

        /// <summary>
        /// Finds the nearest GameObject from a list by screen-space distance to a screen point.
        /// Returns null if no object is within the proximity threshold.
        /// </summary>
        public static GameObject FindNearestByScreenProximity(
            List<GameObject> candidates,
            Vector2 screenPos,
            float threshold)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            float closestDist = threshold;
            GameObject closest = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                GameObject go = candidates[i];
                if (go == null) continue;

                Vector3 sp = cam.WorldToScreenPoint(go.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = go;
                }
            }

            return closest;
        }
    }
}
