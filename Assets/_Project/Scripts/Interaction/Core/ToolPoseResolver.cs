using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Single entry point for resolving tool spatial data using a three-tier fallback:
    ///   Tier 1: <see cref="ToolPoseConfig"/> (authored or auto-detected)
    ///   Tier 2: Legacy <c>orientationEuler</c> override
    ///   Tier 3: <see cref="ComputeUprightCorrection"/> (vertex-based shaft detection)
    /// </summary>
    public static class ToolPoseResolver
    {
        /// <summary>
        /// Resolves the preview cursor rotation for the desktop camera-parented preview.
        /// Tier 1: toolPose.cursorRotation (explicit cursor orientation)
        /// Tier 2: toolPose.gripRotation (natural "held" orientation from Grab Pose Editor)
        /// Tier 3: orientationEuler legacy override
        /// Tier 4: automatic vertex-based detection
        /// </summary>
        public static Quaternion ResolvePreviewRotation(ToolDefinition tool, GameObject preview)
        {
            // Tier 1: explicit cursorRotation (authored in Grab Pose Editor)
            if (tool.HasToolPose && tool.toolPose.HasCursorRotation)
                return tool.toolPose.GetCursorRotation();

            // Tier 2: gripRotation — the natural "held" orientation.
            // gripRotation encodes hand-to-mesh; the cursor needs mesh-to-camera,
            // which is the inverse. This matches the editor's visual tool rotation:
            //   toolRot = Inverse(Euler(gripRotation))
            if (tool.HasToolPose && tool.toolPose.HasGripRotation)
                return Quaternion.Inverse(tool.toolPose.GetGripRotation());

            // Tier 3: orientationEuler legacy override
            if (tool.HasOrientationOverride)
                return Quaternion.Euler(tool.orientationEuler);

            // Tier 4: automatic vertex-based detection
            return ComputeUprightCorrection(preview) * Quaternion.Euler(0f, 180f, 180f);
        }

        /// <summary>
        /// Resolves the model-local grip rotation for XR grab attach transform.
        /// This is the hand orientation when physically gripping the tool in XR.
        /// Returns <c>Quaternion.identity</c> when not authored.
        /// </summary>
        public static Quaternion ResolveXRGripRotation(ToolDefinition tool)
        {
            if (tool.HasToolPose && tool.toolPose.HasGripRotation)
                return tool.toolPose.GetGripRotation();

            return Quaternion.identity;
        }

        /// <summary>
        /// Resolves the local-space grip-to-tip distance.
        /// Tier 1 uses authored <c>tipPoint</c> and <c>gripPoint</c>.
        /// Tier 2/3 returns -1 to signal "use AABB-based EstimateTipDistance fallback".
        /// </summary>
        public static float ResolveTipDistance(ToolDefinition tool)
        {
            if (tool.HasToolPose && tool.toolPose.HasTipPoint)
            {
                Vector3 grip = tool.toolPose.HasGripPoint
                    ? tool.toolPose.GetGripPoint()
                    : Vector3.zero;
                return (tool.toolPose.GetTipPoint() - grip).magnitude;
            }

            return -1f; // signal: use AABB fallback
        }

        /// <summary>
        /// Resolves the local-space tip direction for approach alignment.
        /// Derived from <c>tipPoint - gripPoint</c> when authored.
        /// Fallback: <c>Vector3.down</c> (legacy convention).
        /// </summary>
        public static Vector3 ResolveTipAxis(ToolDefinition tool)
        {
            if (tool.HasToolPose && tool.toolPose.HasTipPoint)
                return tool.toolPose.GetTipDirection();

            return Vector3.down;
        }

        /// <summary>
        /// Resolves the local-space grip offset from mesh origin.
        /// Returns <c>Vector3.zero</c> when not authored (mesh origin = grip).
        /// Used by the XR grab path and as the base for cursor positioning.
        /// </summary>
        public static Vector3 ResolveGripOffset(ToolDefinition tool)
        {
            if (tool.HasToolPose && tool.toolPose.HasGripPoint)
                return tool.toolPose.GetGripPoint();

            return Vector3.zero;
        }

        /// <summary>
        /// Resolves the cursor offset for the desktop/mobile preview.
        /// This is <c>gripPoint + cursorOffset</c>: the grip point shifts the model
        /// so the grip sits at the cursor, and cursorOffset provides additional
        /// adjustment. When cursorOffset is (0,0,0), cursor is exactly at the grip.
        /// </summary>
        public static Vector3 ResolveCursorOffset(ToolDefinition tool)
        {
            Vector3 grip = ResolveGripOffset(tool);

            if (tool.HasToolPose && tool.toolPose.HasCursorOffset)
                return grip + tool.toolPose.GetCursorOffset();

            return grip;
        }

        // ────────────────────────────────────────────────────────────────
        //  Tier 2 fallback — vertex-based shaft/puck detection
        //  Moved from ToolCursorManager to centralize orientation logic.
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes a local rotation that aligns the model's principal axis with local Y (up).
        /// Uses actual mesh vertices to find the two farthest-apart points (shaft axis).
        /// For puck/disc shapes, aligns the thinnest axis to camera-forward.
        /// </summary>
        public static Quaternion ComputeUprightCorrection(GameObject root)
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return Quaternion.identity;

            var allPoints = new System.Collections.Generic.List<Vector3>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var verts = mf.sharedMesh.vertices;
                var localToRoot = root.transform.InverseTransformPoint(mf.transform.position);
                var rot = Quaternion.Inverse(root.transform.rotation) * mf.transform.rotation;
                var scale = mf.transform.lossyScale;
                var rootScale = root.transform.lossyScale;
                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 v = rot * Vector3.Scale(verts[i],
                        new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z))
                        + localToRoot;
                    allPoints.Add(v);
                }
            }

            if (allPoints.Count < 2) return Quaternion.identity;

            Vector3 bmin = allPoints[0], bmax = allPoints[0];
            for (int i = 1; i < allPoints.Count; i++)
            {
                bmin = Vector3.Min(bmin, allPoints[i]);
                bmax = Vector3.Max(bmax, allPoints[i]);
            }
            Vector3 extents = bmax - bmin;

            float[] sorted = { extents.x, extents.y, extents.z };
            System.Array.Sort(sorted);
            float mid = sorted[1];
            float longest = sorted[2];

            // Puck/disc: two longest extents are similar → align thinnest to forward
            const float PuckThreshold = 1.2f;
            if (longest > 0.001f && mid > 0.001f && longest / mid < PuckThreshold)
            {
                Vector3 thinAxis;
                if (extents.x <= extents.y && extents.x <= extents.z)
                    thinAxis = Vector3.right;
                else if (extents.y <= extents.x && extents.y <= extents.z)
                    thinAxis = Vector3.up;
                else
                    thinAxis = Vector3.forward;

                return Quaternion.FromToRotation(thinAxis, Vector3.forward);
            }

            // Shaft: farthest vertex pair → align to Y
            int step = Mathf.Max(1, allPoints.Count / 200);
            Vector3 bestA = Vector3.zero, bestB = Vector3.zero;
            float bestDistSq = 0f;
            for (int i = 0; i < allPoints.Count; i += step)
            {
                for (int j = i + step; j < allPoints.Count; j += step)
                {
                    float dSq = (allPoints[i] - allPoints[j]).sqrMagnitude;
                    if (dSq > bestDistSq)
                    {
                        bestDistSq = dSq;
                        bestA = allPoints[i];
                        bestB = allPoints[j];
                    }
                }
            }

            Vector3 shaftDir = (bestB - bestA).normalized;
            if (shaftDir.sqrMagnitude < 0.001f) return Quaternion.identity;

            if (shaftDir.y < 0f) shaftDir = -shaftDir;

            return Quaternion.FromToRotation(shaftDir, Vector3.up);
        }
    }
}
