// TTAW.CueContext.cs — Per-part / per-tool animation-cue affordances.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 7c. Animation cues are still authored at the step level (their data
// model lives in StepAnimationCuePayload.cues[]) but until now the editor only
// surfaced them through the canvas-level "ANIMATION CUES" section. There was
// no way to ask "which cues fire on the part / tool I'm currently inspecting?"
// or to author a new cue scoped to a specific selection without going to the
// step's canvas section, adding a generic cue, and ticking the right target.
//
// This file adds two contextual affordances rendered inside the inspector
// pane's existing per-task detail body (DrawTaskInspectorBody in
// TTAW.UnifiedList.cs):
//
//   • DrawCuesForPart(step, partId) — list every cue on the step whose
//     targetPartIds[] contains this partId, plus a "+ Add cue here" button
//     that creates a new step-level cue pre-populated with the part id and
//     opens its foldout in the canvas-level ANIMATION CUES section.
//
//   • DrawCuesForTool(step, toolId) — same idea for tools, filtering cues
//     whose targetToolIds[] contains the toolId of the current tool action.
//
// No data-model changes. No runtime changes. The runtime still resolves cues
// the same way; this is purely an authoring entry point.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using System.Linq;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        // Per-foldout open state, keyed by "<stepId>/<scopeId>" so the affordance
        // remembers whether the author opened it on a previous task selection.
        private readonly HashSet<string> _cueContextOpenKeys = new(StringComparer.Ordinal);

        // Reuse the existing animation accent (orange-ish) for visual continuity
        // with the canvas ANIMATION CUES section.
        private static readonly Color CueContextAccent = new(0.95f, 0.65f, 0.20f);

        // ── Public entry points (called from DrawTaskInspectorBody) ──────────

        /// <summary>
        /// Renders the "Cues here" foldout for a part. Lists every cue on the
        /// step whose <c>targetPartIds</c> contains <paramref name="partId"/>
        /// and offers a one-click button to author a new cue pre-scoped to it.
        /// </summary>
        private void DrawCuesForPart(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;

            var matches = new List<int>();
            var cues = step.animationCues?.cues;
            if (cues != null)
            {
                for (int i = 0; i < cues.Length; i++)
                {
                    if (cues[i]?.targetPartIds == null) continue;
                    foreach (var pid in cues[i].targetPartIds)
                    {
                        if (string.Equals(pid, partId, StringComparison.Ordinal))
                        { matches.Add(i); break; }
                    }
                }
            }

            DrawCueContextStrip(
                step:           step,
                scopeKey:       $"{step.id}/part/{partId}",
                title:          $"ANIMATION CUES FOR  {partId}",
                matches:        matches,
                onAdd:          () => AddCueScopedToPart(step, partId));
        }

        /// <summary>
        /// Renders the "Cues here" foldout for a tool. Lists every cue on the
        /// step whose <c>targetToolIds</c> contains <paramref name="toolId"/>
        /// and offers a one-click button to author a new cue pre-scoped to it.
        /// </summary>
        private void DrawCuesForTool(StepDefinition step, string toolId)
        {
            if (step == null || string.IsNullOrEmpty(toolId)) return;

            var matches = new List<int>();
            var cues = step.animationCues?.cues;
            if (cues != null)
            {
                for (int i = 0; i < cues.Length; i++)
                {
                    if (cues[i]?.targetToolIds == null) continue;
                    foreach (var tid in cues[i].targetToolIds)
                    {
                        if (string.Equals(tid, toolId, StringComparison.Ordinal))
                        { matches.Add(i); break; }
                    }
                }
            }

            string toolName = toolId;
            if (_pkg?.tools != null)
            {
                foreach (var t in _pkg.tools)
                {
                    if (t != null && string.Equals(t.id, toolId, StringComparison.Ordinal))
                    { toolName = t.GetDisplayName(); break; }
                }
            }

            DrawCueContextStrip(
                step:           step,
                scopeKey:       $"{step.id}/tool/{toolId}",
                title:          $"ANIMATION CUES FOR  {toolName}",
                matches:        matches,
                onAdd:          () => AddCueScopedToTool(step, toolId));
        }

        // ── Shared strip renderer ─────────────────────────────────────────────

        private void DrawCueContextStrip(
            StepDefinition step,
            string scopeKey,
            string title,
            List<int> matches,
            Action onAdd)
        {
            EditorGUILayout.Space(6);

            // Header strip — orange accent bar + tinted background
            var headerRect = GUILayoutUtility.GetRect(0, 18f, GUILayout.ExpandWidth(true));
            var bgColor    = new Color(CueContextAccent.r * 0.20f + 0.06f,
                                       CueContextAccent.g * 0.20f + 0.06f,
                                       CueContextAccent.b * 0.20f + 0.06f, 1f);
            EditorGUI.DrawRect(headerRect, bgColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 3f, headerRect.height), CueContextAccent);

            // Foldout chevron + title (left)
            bool isOpen = _cueContextOpenKeys.Contains(scopeKey);
            var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal    = { textColor = CueContextAccent },
                alignment = TextAnchor.MiddleLeft,
            };
            string chevron = isOpen ? "▼" : "▶";
            string fullLabel = $"{chevron}  {title}   {matches.Count}";
            var labelRect = new Rect(headerRect.x + 8f, headerRect.y,
                                     headerRect.width - 90f, headerRect.height);
            if (GUI.Button(labelRect, fullLabel, titleStyle))
            {
                if (isOpen) _cueContextOpenKeys.Remove(scopeKey);
                else        _cueContextOpenKeys.Add(scopeKey);
                isOpen = !isOpen;
            }

            // Add button (right)
            var addBtnRect = new Rect(headerRect.xMax - 78f, headerRect.y + 1f, 74f, headerRect.height - 2f);
            var addStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = CueContextAccent },
            };
            if (GUI.Button(addBtnRect, new GUIContent("+ Add cue",
                "Adds a new step-level animation cue pre-populated with this selection. "
                + "The new cue's foldout in the canvas ANIMATION CUES section opens automatically."),
                addStyle))
            {
                onAdd?.Invoke();
                _cueContextOpenKeys.Add(scopeKey); // open the foldout so the new cue is visible
                return;
            }

            if (!isOpen) return;

            // ── Body — list of matching cues ──────────────────────────────────
            if (matches.Count == 0)
            {
                var emptyStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                    fontStyle = FontStyle.Italic,
                };
                EditorGUILayout.LabelField("    No cues fire on this selection yet.", emptyStyle);
                return;
            }

            var cues = step.animationCues?.cues;
            if (cues == null) return;

            for (int i = 0; i < matches.Count; i++)
            {
                int cueIdx = matches[i];
                if (cueIdx < 0 || cueIdx >= cues.Length) continue;
                var cue = cues[cueIdx];
                if (cue == null) continue;

                var rowRect = GUILayoutUtility.GetRect(0, 18f, GUILayout.ExpandWidth(true));
                if ((i & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                // Tiny accent dot
                EditorGUI.DrawRect(new Rect(rowRect.x + 8f, rowRect.y + 7f, 4f, 4f), CueContextAccent);

                // "Cue 3 — shake (3 targets)"
                int targetCount = (cue.targetPartIds?.Length ?? 0)
                                + (cue.targetToolIds?.Length ?? 0)
                                + (string.IsNullOrEmpty(cue.targetSubassemblyId) ? 0 : 1);
                string label = $"Cue {cueIdx + 1}  ·  {(string.IsNullOrEmpty(cue.type) ? "?" : cue.type)}  ·  {targetCount} target{(targetCount == 1 ? "" : "s")}";
                var labelStyle2 = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                };
                var textRect = new Rect(rowRect.x + 18f, rowRect.y, rowRect.width - 90f, rowRect.height);
                GUI.Label(textRect, label, labelStyle2);

                // Reveal — opens the cue's foldout in the canvas ANIMATION CUES section
                var revealRect = new Rect(rowRect.xMax - 76f, rowRect.y + 1f, 70f, rowRect.height - 2f);
                if (GUI.Button(revealRect, new GUIContent("Reveal",
                    "Open this cue's foldout in the canvas ANIMATION CUES section."),
                    EditorStyles.miniButton))
                {
                    while (_cueFoldouts.Count <= cueIdx) _cueFoldouts.Add(false);
                    _cueFoldouts[cueIdx] = true;
                    Repaint();
                }
            }
        }

        // ── Mutators ──────────────────────────────────────────────────────────

        private void AddCueScopedToPart(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;

            var payload = step.animationCues ?? (step.animationCues =
                new StepAnimationCuePayload { cues = Array.Empty<AnimationCueEntry>() });
            var list = new List<AnimationCueEntry>(payload.cues ?? Array.Empty<AnimationCueEntry>());
            var newCue = new AnimationCueEntry
            {
                type           = "shake",
                targetPartIds  = new[] { partId },
            };
            list.Add(newCue);
            payload.cues = list.ToArray();

            // Open the new cue's foldout in the canvas ANIMATION CUES section
            while (_cueFoldouts.Count < payload.cues.Length) _cueFoldouts.Add(false);
            _cueFoldouts[payload.cues.Length - 1] = true;

            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        private void AddCueScopedToTool(StepDefinition step, string toolId)
        {
            if (step == null || string.IsNullOrEmpty(toolId)) return;

            var payload = step.animationCues ?? (step.animationCues =
                new StepAnimationCuePayload { cues = Array.Empty<AnimationCueEntry>() });
            var list = new List<AnimationCueEntry>(payload.cues ?? Array.Empty<AnimationCueEntry>());
            var newCue = new AnimationCueEntry
            {
                type           = "pulse",
                targetToolIds  = new[] { toolId },
            };
            list.Add(newCue);
            payload.cues = list.ToArray();

            while (_cueFoldouts.Count < payload.cues.Length) _cueFoldouts.Add(false);
            _cueFoldouts[payload.cues.Length - 1] = true;

            _dirtyStepIds.Add(step.id);
            Repaint();
        }
    }
}
