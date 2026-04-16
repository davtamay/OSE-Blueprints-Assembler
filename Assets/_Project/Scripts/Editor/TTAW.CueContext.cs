// TTAW.CueContext.cs — Per-target animation-cue affordances.
// ──────────────────────────────────────────────────────────────────────────────
// Each scope (part / tool / group) renders the same Timing-Panel strip defined
// in TTAW.TimingPanels.cs. Authors get one entry button ("+ Add timing panel")
// that creates a panel grouping cues by when they fire, and "+ Add cue" inside
// the panel inserts AnimationCueEntry rows pre-scoped to this target.
//
// This file used to host a flat list with a single "+ Add cue" button and
// Rotate/Shake-only menu (for subassemblies). That UX has been replaced by the
// timing-panel strip so parts, tools, and groups share one authoring model.
// Particle-effect authoring will plug into the same strip later — see the plan.

using System;
using UnityEditor;
using UnityEngine;
using OSE.Content;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Per-foldout open state, keyed by "<stepId>/<scope>/<scopeKey>/panel/<trigger>".
        private readonly System.Collections.Generic.HashSet<string> _cueContextOpenKeys =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        // Reuse the existing animation accent (orange-ish) for visual continuity.
        private static readonly Color CueContextAccent = new(0.95f, 0.65f, 0.20f);

        // ── Public entry points (called from DrawTaskInspectorBody) ──────────

        private void DrawCuesForPart(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;
            DrawTimingPanelsStrip(step, CueScope.Part, partId,
                title: $"ANIMATION CUES FOR  {partId}");
        }

        private void DrawCuesForTool(StepDefinition step, string toolId)
        {
            if (step == null || string.IsNullOrEmpty(toolId)) return;

            string toolName = toolId;
            if (_pkg?.tools != null)
            {
                foreach (var t in _pkg.tools)
                {
                    if (t != null && string.Equals(t.id, toolId, StringComparison.Ordinal))
                    { toolName = t.GetDisplayName(); break; }
                }
            }
            DrawTimingPanelsStrip(step, CueScope.Tool, toolId,
                title: $"ANIMATION CUES FOR  {toolName}");
        }

        private void DrawCuesForSubassembly(StepDefinition step, string subassemblyId)
        {
            if (step == null || string.IsNullOrEmpty(subassemblyId)) return;

            string subName = subassemblyId;
            if (_pkg != null && _pkg.TryGetSubassembly(subassemblyId, out var sub) && sub != null)
                subName = sub.GetDisplayName();

            DrawTimingPanelsStrip(step, CueScope.Subassembly, subassemblyId,
                title: $"ANIMATION CUES FOR  {subName}");
        }

        // ── Row-level cue-count badges (inline affordance on list rows) ──────
        //
        // Renders a compact "🎬 N" / "✨ M" pill beside part and group rows so
        // authors can see at a glance which hosts carry animation cues or
        // particle effects — without selecting the row first. Returns the
        // total pill width consumed so callers can lay out adjacent content.

        private static readonly Color CueBadgeAnim     = new(0.95f, 0.65f, 0.20f); // orange (animation accent)
        private static readonly Color CueBadgeParticle = new(0.55f, 0.80f, 1.00f); // blue-cyan (particles)

        /// <summary>
        /// Counts animation cues and particle cues hosted on an
        /// <see cref="IAnimationHost"/>. Returns (animCount, particleCount).
        /// Particle cues are those with <c>type == "particle"</c>; everything
        /// else is counted as an animation cue.
        /// </summary>
        private static (int anim, int particle) CountHostCues(IAnimationHost host)
        {
            if (host == null) return (0, 0);
            var cues = host.AnimationCues;
            if (cues == null || cues.Length == 0) return (0, 0);

            int anim = 0, particle = 0;
            for (int i = 0; i < cues.Length; i++)
            {
                var c = cues[i];
                if (c == null) continue;
                if (string.Equals(c.type, "particle", StringComparison.Ordinal))
                    particle++;
                else
                    anim++;
            }
            return (anim, particle);
        }

        /// <summary>
        /// Draws cue-count pills within <paramref name="area"/> right-aligned.
        /// Returns the total width used (0 when no cues). Use to reserve
        /// space in parent row layouts.
        /// </summary>
        private float DrawCueCountBadges(Rect area, IAnimationHost host)
        {
            var (anim, particle) = CountHostCues(host);
            if (anim == 0 && particle == 0) return 0f;

            var pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize  = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };

            float x = area.xMax;
            float h = Mathf.Min(14f, area.height - 4f);
            float y = area.y + (area.height - h) * 0.5f;

            if (particle > 0)
            {
                string label = $"✨ {particle}";
                float w = Mathf.Max(24f, pillStyle.CalcSize(new GUIContent(label)).x + 6f);
                x -= w;
                var r = new Rect(x, y, w, h);
                EditorGUI.DrawRect(r, new Color(CueBadgeParticle.r, CueBadgeParticle.g, CueBadgeParticle.b, 0.22f));
                var s = new GUIStyle(pillStyle) { normal = { textColor = CueBadgeParticle } };
                GUI.Label(r, label, s);
                x -= 3f;
            }
            if (anim > 0)
            {
                string label = $"🎬 {anim}";
                float w = Mathf.Max(24f, pillStyle.CalcSize(new GUIContent(label)).x + 6f);
                x -= w;
                var r = new Rect(x, y, w, h);
                EditorGUI.DrawRect(r, new Color(CueBadgeAnim.r, CueBadgeAnim.g, CueBadgeAnim.b, 0.22f));
                var s = new GUIStyle(pillStyle) { normal = { textColor = CueBadgeAnim } };
                GUI.Label(r, label, s);
            }

            return area.xMax - x;
        }
    }
}
