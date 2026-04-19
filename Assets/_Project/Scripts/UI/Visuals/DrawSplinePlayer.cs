using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// One-shot spline mesh generation via <see cref="SplinePartFactory.Create"/>.
    /// Fires at Start (or at <c>startProgress</c> when progress-ranged) and
    /// drops a tube mesh following the resolved anchor path. Useful for
    /// wires, weld beads with bends, cable paths — anywhere a LineRenderer
    /// would look flat.
    /// </summary>
    public sealed class DrawSplinePlayer : IAnimationCuePlayer
    {
        public string AnimationType => "drawSpline";
        public bool IsPlaying { get; private set; }

        public void Start(AnimationCueContext context)
        {
            IsPlaying = true;
            var e = context.Entry;

            string[] refs = e.splineAnchorRefs;
            if (refs == null || refs.Length < 2)
            {
                // Fall back to anchor A/B if no multi-point path authored.
                refs = new[] { e.anchorARef ?? "", e.anchorBRef ?? "" };
            }

            var knots = new SceneFloat3[refs.Length];
            int knotCount = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                if (!AnimationAnchorResolver.TryResolveAnchor(refs[i], context, out Vector3 world))
                    continue;
                // Convert world → PreviewRoot local so the factory's local-space
                // knots land correctly. When no PreviewRoot is obvious, use
                // the host's parent.
                Transform host = context.Targets != null && context.Targets.Count > 0 ? context.Targets[0]?.transform : null;
                Transform parent = host != null ? host.parent : null;
                Vector3 local = parent != null ? parent.InverseTransformPoint(world) : world;
                knots[knotCount++] = new SceneFloat3 { x = local.x, y = local.y, z = local.z };
            }
            if (knotCount < 2) { IsPlaying = false; return; }

            var trimmed = new SceneFloat3[knotCount];
            System.Array.Copy(knots, trimmed, knotCount);

            var def = new SplinePathDefinition
            {
                radius     = e.splineRadius     > 0f ? e.splineRadius     : 0.003f,
                segments   = 16,
                metallic   = e.splineMetallic   > 0f ? e.splineMetallic   : 0.4f,
                smoothness = e.splineSmoothness > 0f ? e.splineSmoothness : 0.5f,
                knots      = trimmed,
            };

            Color col = e.fromColor.a > 0f
                ? new Color(e.fromColor.r, e.fromColor.g, e.fromColor.b, e.fromColor.a)
                : new Color(0.1f, 0.1f, 0.1f, 1f);

            var hostT = context.Targets != null && context.Targets.Count > 0 ? context.Targets[0]?.transform : null;
            Transform parentT = hostT != null ? hostT.parent : null;
            SplinePartFactory.Create("[Cue] DrawSpline", def, col, parentT);

            // One-shot — Tick immediately returns false.
            IsPlaying = false;
        }

        public bool Tick(float deltaTime) => false;
        public void Stop() { IsPlaying = false; }
    }
}
