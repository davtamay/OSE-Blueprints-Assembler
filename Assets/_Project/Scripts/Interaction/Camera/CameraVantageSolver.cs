using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Result of a vantage solve. Tier indicates which escalation tier produced the
    /// answer (1 = current angle clear, 2 = yaw sweep, 3 = distance bump).
    /// CastsPerformed is for profiling / cap verification.
    /// </summary>
    public struct CameraVantageResult
    {
        public float Yaw;
        public float Pitch;
        public float Distance;
        public int Tier;
        public int CastsPerformed;
    }

    /// <summary>
    /// Picks an orbital yaw/pitch/distance that has line-of-sight to a pivot,
    /// minimizing disorientation from the user's current view.
    ///
    /// Three-tier escalation, all driven by Physics.SphereCastNonAlloc:
    ///   1. Current angle at requested distance (1 cast — dominant case).
    ///   2. Yaw sweep at requested distance (±15°, ±30°, ±45°, ±60°, ±90°)
    ///      — smallest-magnitude clear offset wins to minimize visual jump.
    ///   3. Distance bump at original yaw (1.25×, 1.5×, 1.75×, clamped).
    ///
    /// Returns false only when every tier fails. Worst-case 14 casts.
    /// Pure logic — no MonoBehaviour, easy to unit test.
    ///
    /// Self-occlusion handling: the cast stops <c>nearTargetIgnore</c> meters
    /// short of the pivot so the target part itself (and any tool clustered
    /// at the target) doesn't register as an obstruction. Defaults are tuned
    /// for typical assembly part scale (~5-15 cm extents). For finer control
    /// over specific transforms, pass <paramref name="ignoreRoots"/>.
    /// </summary>
    public sealed class CameraVantageSolver
    {
        private static readonly float[] s_yawOffsets =
            { 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f, 90f, -90f };

        private static readonly float[] s_distanceMultipliers = { 1.25f, 1.5f, 1.75f };

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

        public bool TrySolve(
            Vector3 pivot,
            float requestedDistance,
            float currentYaw,
            float currentPitch,
            LayerMask occluderMask,
            float probeRadius,
            float nearTargetIgnore,
            Transform[] ignoreRoots,
            float minDistance,
            float maxDistance,
            out CameraVantageResult result)
        {
            result = default;
            int casts = 0;

            // Tier 1 — current angle.
            casts++;
            if (IsClear(pivot, currentYaw, currentPitch, requestedDistance,
                        occluderMask, probeRadius, nearTargetIgnore, ignoreRoots))
            {
                result = Build(currentYaw, currentPitch, requestedDistance, 1, casts);
                return true;
            }

            // Tier 2 — yaw sweep at requested distance.
            for (int i = 0; i < s_yawOffsets.Length; i++)
            {
                casts++;
                float candidateYaw = currentYaw + s_yawOffsets[i];
                if (IsClear(pivot, candidateYaw, currentPitch, requestedDistance,
                            occluderMask, probeRadius, nearTargetIgnore, ignoreRoots))
                {
                    result = Build(candidateYaw, currentPitch, requestedDistance, 2, casts);
                    return true;
                }
            }

            // Tier 3 — distance bump at original yaw.
            for (int i = 0; i < s_distanceMultipliers.Length; i++)
            {
                casts++;
                float candidateDist = Mathf.Clamp(
                    requestedDistance * s_distanceMultipliers[i], minDistance, maxDistance);
                if (IsClear(pivot, currentYaw, currentPitch, candidateDist,
                            occluderMask, probeRadius, nearTargetIgnore, ignoreRoots))
                {
                    result = Build(currentYaw, currentPitch, candidateDist, 3, casts);
                    return true;
                }
            }

            result.CastsPerformed = casts;
            return false;
        }

        private bool IsClear(
            Vector3 pivot,
            float yaw,
            float pitch,
            float distance,
            LayerMask occluderMask,
            float probeRadius,
            float nearTargetIgnore,
            Transform[] ignoreRoots)
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);

            Vector3 offset = new Vector3(
                cosPitch * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                cosPitch * Mathf.Cos(yawRad)) * distance;

            Vector3 cameraPos = pivot + offset;
            Vector3 toPivot = pivot - cameraPos;
            float fullLength = toPivot.magnitude;
            if (fullLength < 0.0001f) return true;
            Vector3 dir = toPivot / fullLength;

            // Stop short of the pivot so the target itself isn't flagged as
            // occluding the view of the target.
            float castLength = fullLength - Mathf.Max(0f, nearTargetIgnore);
            if (castLength <= 0f) return true;

            int count = Physics.SphereCastNonAlloc(
                cameraPos, probeRadius, dir, _hitBuffer,
                castLength, occluderMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (!IsUnderIgnoreRoot(_hitBuffer[i].collider, ignoreRoots))
                    return false;
            }
            return true;
        }

        private static bool IsUnderIgnoreRoot(Collider collider, Transform[] ignoreRoots)
        {
            if (collider == null || ignoreRoots == null || ignoreRoots.Length == 0) return false;
            Transform t = collider.transform;
            for (int i = 0; i < ignoreRoots.Length; i++)
            {
                Transform root = ignoreRoots[i];
                if (root == null) continue;
                if (t == root || t.IsChildOf(root)) return true;
            }
            return false;
        }

        private static CameraVantageResult Build(float yaw, float pitch, float distance, int tier, int casts)
        {
            return new CameraVantageResult
            {
                Yaw = yaw,
                Pitch = pitch,
                Distance = distance,
                Tier = tier,
                CastsPerformed = casts,
            };
        }
    }
}
