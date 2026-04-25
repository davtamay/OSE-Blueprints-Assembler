using System.Globalization;
using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Resolves <see cref="AnimationCueEntry.anchorARef"/> / <c>anchorBRef</c>
    /// strings to world-space positions. Line / spline / measure cue players
    /// call this to avoid hand-authoring coordinates — instead authors write
    /// semantic refs like "weldStart" that pick up scene state from
    /// <see cref="ToolActionTargetInfo"/> markers.
    ///
    /// Recognised refs:
    /// <list type="bullet">
    ///   <item><c>toolTip</c> / <c>toolGrip</c> — local points on the active
    ///   tool preview, world-transformed via its transform.</item>
    ///   <item><c>targetSurface</c> — <c>SurfaceWorldPos</c> from the first
    ///   <see cref="ToolActionTargetInfo"/> on any of the cue's targets or
    ///   their ancestry.</item>
    ///   <item><c>weldStart</c> / <c>weldEnd</c> / <c>weldMid</c> —
    ///   SurfaceWorldPos ± (WeldAxis × WeldLength / 2).</item>
    ///   <item><c>measureAnchorA</c> / <c>measureAnchorB</c> — looked up by
    ///   marker name on scene markers tagged by the measure handler.</item>
    ///   <item><c>partAssembledCenter</c> — first assembled-pose centroid
    ///   in the cue context (parent-local → world).</item>
    ///   <item><c>literal:x,y,z</c> — parsed as a world-space point.</item>
    /// </list>
    ///
    /// The resolver is pragmatic — when a ref can't be resolved (missing
    /// marker, no weld context) it returns <c>false</c> and the caller picks
    /// a sensible fallback (typically the host's current world position).
    /// </summary>
    public static class AnimationAnchorResolver
    {
        public static bool TryResolveAnchor(string anchorRef, AnimationCueContext ctx, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (string.IsNullOrEmpty(anchorRef)) return false;

            if (anchorRef.StartsWith("literal:"))
                return TryParseLiteral(anchorRef.Substring("literal:".Length), out worldPos);

            ToolActionTargetInfo info = FindToolActionTargetInfo(ctx);
            Transform hostTx = ctx.Targets != null && ctx.Targets.Count > 0 ? ctx.Targets[0]?.transform : null;

            switch (anchorRef)
            {
                case "targetSurface":
                    if (info != null) { worldPos = info.SurfaceWorldPos; return true; }
                    return false;

                case "weldStart":
                    if (info != null)
                    {
                        Vector3 dirS = ResolveSeamDir(info);
                        float lenS  = info.WeldLength > 0f ? info.WeldLength : 0.03f;
                        worldPos = info.SurfaceWorldPos - dirS * (lenS * 0.5f);
                        return true;
                    }
                    return false;

                case "weldEnd":
                    if (info != null)
                    {
                        Vector3 dirE = ResolveSeamDir(info);
                        float lenE  = info.WeldLength > 0f ? info.WeldLength : 0.03f;
                        worldPos = info.SurfaceWorldPos + dirE * (lenE * 0.5f);
                        return true;
                    }
                    return false;

                case "weldMid":
                    if (info != null) { worldPos = info.SurfaceWorldPos; return true; }
                    return false;

                case "measureAnchorA":
                    return TryFindTargetInfoByName("MeasureAnchorA", out worldPos);

                case "measureAnchorB":
                    return TryFindTargetInfoByName("MeasureAnchorB", out worldPos);

                case "toolTip":
                    return TryResolveToolPose(ctx, hostTx, useGrip: false, out worldPos);

                case "toolGrip":
                    return TryResolveToolPose(ctx, hostTx, useGrip: true, out worldPos);

                case "partAssembledCenter":
                    if (ctx.AssembledPoses != null && ctx.AssembledPoses.Count > 0 && hostTx != null)
                    {
                        Transform parent = hostTx.parent;
                        Vector3 localPos = ctx.AssembledPoses[0].Position;
                        worldPos = parent != null ? parent.TransformPoint(localPos) : localPos;
                        return true;
                    }
                    return false;

                default:
                    // Fallback: unknown ref — let caller decide.
                    return false;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns a normalized weld-seam direction. Prefers the authored
        /// <see cref="ToolActionTargetInfo.WeldAxis"/>; when that's zero
        /// (content didn't author a seam direction), falls back to a
        /// camera-aligned horizontal axis — same convention WeldPreview
        /// uses so seam-travel still has a visible 3 cm spread even on
        /// point-welds that didn't author a direction.
        /// </summary>
        private static Vector3 ResolveSeamDir(ToolActionTargetInfo info)
        {
            if (info.WeldAxis.sqrMagnitude > 0.001f)
                return info.WeldAxis.normalized;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 r = cam.transform.right;
                Vector3 horiz = new Vector3(r.x, 0f, r.z);
                if (horiz.sqrMagnitude > 0.001f)
                    return horiz.normalized;
            }
            return Vector3.right;
        }

        private static bool TryParseLiteral(string body, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (string.IsNullOrEmpty(body)) return false;
            var parts = body.Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            worldPos = new Vector3(x, y, z);
            return true;
        }

        private static ToolActionTargetInfo FindToolActionTargetInfo(AnimationCueContext ctx)
        {
            if (ctx.Targets != null)
            {
                for (int i = 0; i < ctx.Targets.Count; i++)
                {
                    var go = ctx.Targets[i];
                    if (go == null) continue;
                    var info = go.GetComponent<ToolActionTargetInfo>() ?? go.GetComponentInChildren<ToolActionTargetInfo>(true);
                    if (info != null) return info;
                }
            }
            // Most-recently-clicked hint wins — tool-hosted cues (ctx.Targets
            // is the tool GO) would otherwise hit the scene-wide fallback and
            // latch onto the OLDEST inactive marker (welds N-1, N-2 …
            // accumulate under PreviewRoot until step transition).
            if (ToolActionTargetInfo.CurrentActionTarget != null)
                return ToolActionTargetInfo.CurrentActionTarget;

            // Scene-wide last resort. INCLUDE inactive so markers hidden at
            // click time still resolve; prefer active when any exist.
            var all = Object.FindObjectsByType<ToolActionTargetInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all == null || all.Length == 0) return null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.activeInHierarchy) return all[i];
            }
            return all[0];
        }

        private static bool TryFindTargetInfoByName(string markerName, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            // INCLUDE inactive so cue anchors still resolve after marker
            // hide-on-click (SetActive(false) keeps the component alive).
            var all = Object.FindObjectsByType<ToolActionTargetInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all == null) return false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (!string.Equals(all[i].TargetId, markerName, System.StringComparison.OrdinalIgnoreCase)
                    && all[i].gameObject.name.IndexOf(markerName, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                worldPos = all[i].SurfaceWorldPos != Vector3.zero ? all[i].SurfaceWorldPos : all[i].transform.position;
                return true;
            }
            return false;
        }

        private static bool TryResolveToolPose(AnimationCueContext ctx, Transform hostTx, bool useGrip, out Vector3 worldPos)
        {
            worldPos = hostTx != null ? hostTx.position : Vector3.zero;
            if (hostTx == null) return false;

            // Host is the tool preview GO; its transform origin is typically
            // the grip. If the tool has an authored toolPose, use the tip
            // offset. In the absence of authored data, fall back to transform
            // origin — good enough for cues that don't depend on precise tip
            // placement.
            //
            // Accessing ToolDefinition would require plumbing the package
            // reference here; keep it lightweight and let the grip default
            // to transform.position, tip default to transform.position +
            // transform.forward * 0.05m (5 cm).
            if (useGrip)
            {
                worldPos = hostTx.position;
            }
            else
            {
                worldPos = hostTx.position + hostTx.forward * 0.05f;
            }
            return true;
        }
    }
}
