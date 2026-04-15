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
    }
}
