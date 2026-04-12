// TTAW.Visibility.cs — "What's showing in this step?" section.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 6 follow-up. The user reported they couldn't tell which parts the
// scene was actually rendering for the active step, and that the existing
// "parts in task" UI conflated visibility with task completion.
//
// This section reveals the visibility computation that the runtime spawner
// already performs (mirrored in TTAW.Parts.cs TryGetStepAwarePose):
//
//   • A part is visible in step N if its "owning step" sequence index ≤ N
//   • The owning step is the FIRST step (lowest sequenceIndex) that lists
//     the part in step.requiredPartIds OR includes it via
//     step.requiredSubassemblyId → subassembly.partIds
//   • Otherwise the part is hidden in the scene
//
// So the section groups everything currently visible in this step into three
// buckets and lets the author add more parts via the existing requiredPartIds
// channel (which is the only knob the runtime currently honours). The
// "Required for completion" coupling is called out in the help text so authors
// understand they're not adding a separate "visual only" reference — that's a
// Phase 7 data-model addition (visualPartIds[]).
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private int  _visibilityAddPartIdx;
        private bool _visibilityAddAsOptional; // Phase 7 — toggle on the add picker
        private bool _visibilityBucketsExpanded; // collapsed by default — just count pills

        private readonly List<string> _visScratchOwnedHere       = new();
        private readonly List<string> _visScratchOptionalHere  = new();
        private readonly List<string> _visScratchOwnedSubHere    = new();
        private readonly List<string> _visScratchInheritedEarlier = new();

        // Bucket accent colours — communicate the *source* of visibility
        // through hue so the author doesn't have to read labels.
        private static readonly Color VisColorOwned    = new(0.30f, 0.78f, 0.36f); // green
        private static readonly Color VisColorOptional  = new(0.95f, 0.70f, 0.20f); // amber
        private static readonly Color VisColorSub      = new(0.20f, 0.62f, 0.95f); // blue
        private static readonly Color VisColorEarlier  = new(0.62f, 0.62f, 0.66f); // grey

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────

        private void DrawVisibilitySection(StepDefinition step)
        {
            if (_pkg == null || step == null) return;

            // Compute the four visibility buckets from cached scene-build state
            // and the step's own part ids. The scene-build cache is populated by
            // RespawnScene the same way the spawner runs at runtime, so the
            // numbers shown here always match what's drawn in the SceneView.
            ComputeVisibilityBuckets(step,
                                     out int totalVisible,
                                     out HashSet<string> ownedSubPartIds);

            // ── Section header — count pills are always visible (1 line) ─────
            // The full bucket detail is collapsed by default. Click the
            // header to expand. This keeps the canvas compact on most steps
            // while still showing "yes, 12 parts are on screen" at a glance.
            EditorGUILayout.BeginHorizontal();
            string chevron = _visibilityBucketsExpanded ? "▼" : "▶";
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            if (GUILayout.Button($"{chevron} WHAT'S SHOWING", titleStyle, GUILayout.ExpandWidth(false)))
                _visibilityBucketsExpanded = !_visibilityBucketsExpanded;
            GUILayout.FlexibleSpace();
            DrawCountPill(VisColorOwned,   _visScratchOwnedHere.Count,        "here");
            DrawCountPill(VisColorOptional, _visScratchOptionalHere.Count,   "optional");
            DrawCountPill(VisColorSub,     _visScratchOwnedSubHere.Count,     "group");
            DrawCountPill(VisColorEarlier, _visScratchInheritedEarlier.Count, "earlier");
            EditorGUILayout.EndHorizontal();

            if (!_visibilityBucketsExpanded) return;

            // Compact one-line legend
            var legendStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                fontStyle = FontStyle.Italic,
            };
            EditorGUILayout.LabelField(
                "  green = required here    amber = optional    blue = from group    grey = built earlier",
                legendStyle);

            // ── Four visual buckets (read-only — editing happens on task rows) ─
            if (_visScratchOwnedHere.Count > 0)
            {
                DrawVisibilityBucket(
                    "REQUIRED IN THIS STEP",
                    VisColorOwned,
                    _visScratchOwnedHere,
                    allowRemove: false,
                    step: null);
            }

            if (_visScratchOptionalHere.Count > 0)
            {
                DrawVisibilityBucket(
                    "OPTIONAL",
                    VisColorOptional,
                    _visScratchOptionalHere,
                    allowRemove: false,
                    step: null);
            }

            if (_visScratchOwnedSubHere.Count > 0)
            {
                DrawVisibilityBucket(
                    "FROM THIS STEP'S GROUP",
                    VisColorSub,
                    _visScratchOwnedSubHere,
                    allowRemove: false,
                    step: null);
            }

            if (_visScratchInheritedEarlier.Count > 0)
            {
                DrawVisibilityBucket(
                    "BUILT IN EARLIER STEPS",
                    VisColorEarlier,
                    _visScratchInheritedEarlier,
                    allowRemove: false,
                    step: null,
                    maxRows: 12);
            }

            // Add picker removed — parts are added via the task sequence [+]
            // button. Required/Optional is toggled on each task row directly.
        }

        private enum VisibilityRemoveKind { Required, VisualOnly }

        // ── Visual helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Draws an inline rounded count badge: a small coloured rectangle with
        /// a number + label centred inside. Used in the section header so the
        /// author can read "5 here · 3 group · 12 earlier" at a glance.
        /// </summary>
        private static void DrawCountPill(Color color, int count, string label)
        {
            string text = $"{count} {label}";
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            var content = new GUIContent(text);
            var size    = style.CalcSize(content);
            var rect    = GUILayoutUtility.GetRect(size.x + 12f, 16f,
                              GUILayout.Width(size.x + 12f), GUILayout.Height(16f));
            // Faded background when the bucket is empty so empty pills don't shout
            var bgColor = count > 0 ? color : new Color(color.r, color.g, color.b, 0.30f);
            EditorGUI.DrawRect(rect, bgColor);
            GUI.Label(rect, content, style);
            GUILayout.Space(3);
        }

        /// <summary>
        /// Draws a visually-grouped bucket: a coloured 4-px left edge bar +
        /// tinted background header strip with the bucket title in coloured
        /// bold text + an indented list of items. Each item gets a tiny dot
        /// in the bucket colour so the eye groups by colour, not by label.
        /// </summary>
        private void DrawVisibilityBucket(
            string title,
            Color accent,
            List<string> items,
            bool allowRemove,
            StepDefinition step,
            int maxRows = 0,
            VisibilityRemoveKind removeKind = VisibilityRemoveKind.Required)
        {
            EditorGUILayout.Space(3);

            // Header strip — accent bar on the left, tinted background, bold colour title
            var headerRect = GUILayoutUtility.GetRect(0, 18f, GUILayout.ExpandWidth(true));
            var bgColor    = new Color(accent.r * 0.20f + 0.06f,
                                       accent.g * 0.20f + 0.06f,
                                       accent.b * 0.20f + 0.06f,
                                       1f);
            EditorGUI.DrawRect(headerRect, bgColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 3f, headerRect.height), accent);

            var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleLeft,
            };
            var labelRect = new Rect(headerRect.x + 8f, headerRect.y,
                                     headerRect.width - 60f, headerRect.height);
            GUI.Label(labelRect, $"{title}   {items.Count}", titleStyle);

            // Item rows — tiny dot + part id, optionally a × button
            int  removeIdx = -1;
            int  cap       = maxRows > 0 ? Math.Min(items.Count, maxRows) : items.Count;

            for (int i = 0; i < cap; i++)
            {
                var rowRect = GUILayoutUtility.GetRect(0, 16f, GUILayout.ExpandWidth(true));
                // Subtle alternating row tint to distinguish entries on a dark background
                if ((i & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                // Coloured dot at the left edge
                var dotRect = new Rect(rowRect.x + 8f, rowRect.y + 6f, 4f, 4f);
                EditorGUI.DrawRect(dotRect, accent);

                // Part id label
                var textRect = new Rect(rowRect.x + 18f, rowRect.y,
                                        rowRect.width - 26f, rowRect.height);
                GUI.Label(textRect, items[i], EditorStyles.miniLabel);

                // Remove button — present on both editable buckets
                if (allowRemove)
                {
                    var btnRect = new Rect(rowRect.xMax - 22f, rowRect.y + 1f, 20f, 14f);
                    if (GUI.Button(btnRect, "×", EditorStyles.miniButton))
                        removeIdx = i;
                }
            }

            if (maxRows > 0 && items.Count > maxRows)
            {
                var moreStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.5f, 0.5f, 0.55f) },
                    fontStyle = FontStyle.Italic,
                };
                EditorGUILayout.LabelField($"     +{items.Count - maxRows} more", moreStyle);
            }

            if (removeIdx >= 0 && step != null)
            {
                if (removeKind == VisibilityRemoveKind.Required)
                    RemoveRequiredPartFromStep(step, items[removeIdx]);
                else
                    RemoveOptionalPartFromStep(step, items[removeIdx]);
            }
        }

        // ── Compute / categorise ──────────────────────────────────────────────

        private void ComputeVisibilityBuckets(
            StepDefinition step,
            out int totalVisible,
            out HashSet<string> ownedSubPartIds)
        {
            _visScratchOwnedHere.Clear();
            _visScratchOptionalHere.Clear();
            _visScratchOwnedSubHere.Clear();
            _visScratchInheritedEarlier.Clear();
            ownedSubPartIds = new HashSet<string>(StringComparer.Ordinal);
            totalVisible    = 0;

            // Collect this step's required-subassembly parts so we can label
            // them separately and exclude them from the "owned by this step"
            // bucket (the requiredPartIds bucket).
            if (!string.IsNullOrEmpty(step.requiredSubassemblyId)
                && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subDef)
                && subDef?.partIds != null)
            {
                foreach (var pid in subDef.partIds)
                    if (!string.IsNullOrEmpty(pid))
                        ownedSubPartIds.Add(pid);
            }

            // Required-parts owned by this step
            var ownedHere = new HashSet<string>(StringComparer.Ordinal);
            if (step.requiredPartIds != null)
            {
                foreach (var pid in step.requiredPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (ownedSubPartIds.Contains(pid)) continue; // shown under sub bucket
                    ownedHere.Add(pid);
                }
            }

            // Visual-only parts authored on this step (Phase 7).
            var visualOnlyHere = new HashSet<string>(StringComparer.Ordinal);
            if (step.optionalPartIds != null)
            {
                foreach (var pid in step.optionalPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (ownedSubPartIds.Contains(pid)) continue;
                    if (ownedHere.Contains(pid))       continue; // requiredPartIds wins
                    visualOnlyHere.Add(pid);
                }
            }

            int currentSeq = step.sequenceIndex;

            // Walk every part in the package and classify it
            var allParts = _pkg.GetParts();
            for (int i = 0; i < allParts.Length; i++)
            {
                var part = allParts[i];
                if (part == null || string.IsNullOrEmpty(part.id)) continue;

                // The cached _sceneBuildPartStepSeq is the same map the spawner
                // and TryGetStepAwarePose use, so visibility here exactly tracks
                // the SceneView. Fall back to "not visible" if no entry.
                if (_sceneBuildPartStepSeq == null
                    || !_sceneBuildPartStepSeq.TryGetValue(part.id, out int placedAt))
                    continue;
                if (placedAt > currentSeq) continue; // future — hidden

                // Classify by the part's relationship to THIS step, not just
                // when it first appeared. A part can be placed in an earlier
                // step but still be required/optional in the current step
                // (e.g. a Use step that operates on parts placed in a prior
                // Place step).
                if (ownedSubPartIds.Contains(part.id))
                    _visScratchOwnedSubHere.Add(part.id);
                else if (ownedHere.Contains(part.id))
                    _visScratchOwnedHere.Add(part.id);
                else if (visualOnlyHere.Contains(part.id))
                    _visScratchOptionalHere.Add(part.id);
                else if (placedAt < currentSeq)
                    _visScratchInheritedEarlier.Add(part.id);
                else
                    _visScratchOwnedHere.Add(part.id);
                totalVisible++;
            }

            _visScratchOwnedHere.Sort(StringComparer.Ordinal);
            _visScratchOptionalHere.Sort(StringComparer.Ordinal);
            _visScratchOwnedSubHere.Sort(StringComparer.Ordinal);
            _visScratchInheritedEarlier.Sort(StringComparer.Ordinal);
        }

        // ── Add picker ────────────────────────────────────────────────────────

        private void DrawAddPartToVisibility(StepDefinition step, HashSet<string> ownedSubPartIds)
        {
            // Build candidate list: every package part that is NOT already
            // visible in this step (so it would be a real addition).
            var candidates = new List<string>();
            var alreadyVisible = new HashSet<string>(StringComparer.Ordinal);
            alreadyVisible.UnionWith(_visScratchOwnedHere);
            alreadyVisible.UnionWith(_visScratchOptionalHere);
            alreadyVisible.UnionWith(_visScratchOwnedSubHere);
            alreadyVisible.UnionWith(_visScratchInheritedEarlier);

            var allParts = _pkg.GetParts();
            for (int i = 0; i < allParts.Length; i++)
            {
                var p = allParts[i];
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                if (alreadyVisible.Contains(p.id))           continue;
                candidates.Add(p.id);
            }

            EditorGUILayout.Space(6);

            if (candidates.Count == 0)
            {
                var allInStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter,
                };
                EditorGUILayout.LabelField("Every package part is already on screen.", allInStyle);
                return;
            }

            // Required / Visible-only mode toggle — two segments that share width.
            EditorGUILayout.BeginHorizontal();
            var modeRequiredStyle = new GUIStyle(EditorStyles.miniButtonLeft)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = !_visibilityAddAsOptional ? VisColorOwned   : new Color(0.55f, 0.55f, 0.58f) },
            };
            var modeVisualStyle = new GUIStyle(EditorStyles.miniButtonRight)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = _visibilityAddAsOptional ? VisColorOptional : new Color(0.55f, 0.55f, 0.58f) },
            };
            if (GUILayout.Toggle(!_visibilityAddAsOptional,
                    new GUIContent("REQUIRED",
                        "Add the part as a required step part — the user must interact with it to advance the step."),
                    modeRequiredStyle, GUILayout.Width(80), GUILayout.Height(18)))
                _visibilityAddAsOptional = false;
            if (GUILayout.Toggle(_visibilityAddAsOptional,
                    new GUIContent("OPTIONAL",
                        "Add the part as optional — the spawner renders it but it is not required for task completion."),
                    modeVisualStyle, GUILayout.Width(80), GUILayout.Height(18)))
                _visibilityAddAsOptional = true;
            GUILayout.Space(4);

            _visibilityAddPartIdx = Mathf.Clamp(_visibilityAddPartIdx, 0, candidates.Count - 1);
            _visibilityAddPartIdx = EditorGUILayout.Popup(_visibilityAddPartIdx, candidates.ToArray(),
                GUILayout.Height(18));

            var accent = _visibilityAddAsOptional ? VisColorOptional : VisColorOwned;
            var addStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = accent },
            };
            if (GUILayout.Button(new GUIContent("+ ADD",
                    _visibilityAddAsOptional
                        ? "Adds the selected part to this step's optionalPartIds — visible in the scene but not required for task completion."
                        : "Adds the selected part to this step's requiredPartIds — visible in the scene AND required for task completion."),
                addStyle, GUILayout.Width(54), GUILayout.Height(18)))
            {
                if (_visibilityAddAsOptional)
                    AddOptionalPartToStep(step, candidates[_visibilityAddPartIdx]);
                else
                    AddRequiredPartToStep(step, candidates[_visibilityAddPartIdx]);
                _visibilityAddPartIdx = 0;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Mutations ─────────────────────────────────────────────────────────

        private void AddRequiredPartToStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;
            var list = step.requiredPartIds != null
                ? new List<string>(step.requiredPartIds)
                : new List<string>();
            if (list.Contains(partId)) return;
            list.Add(partId);
            step.requiredPartIds = list.ToArray();
            _dirtyStepIds.Add(step.id);
            InvalidateTaskOrderCache();
            BuildPartList();
            BuildTargetList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        private void RemoveRequiredPartFromStep(StepDefinition step, string partId)
        {
            if (step?.requiredPartIds == null || string.IsNullOrEmpty(partId)) return;
            var list = new List<string>(step.requiredPartIds);
            if (!list.Remove(partId)) return;
            step.requiredPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
            _dirtyStepIds.Add(step.id);
            InvalidateTaskOrderCache();
            BuildPartList();
            BuildTargetList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        // ── Visual-only mutators (Phase 7) ────────────────────────────────────

        private void AddOptionalPartToStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;
            var list = step.optionalPartIds != null
                ? new List<string>(step.optionalPartIds)
                : new List<string>();
            if (list.Contains(partId)) return;
            list.Add(partId);
            step.optionalPartIds = list.ToArray();
            _dirtyStepIds.Add(step.id);
            // No task-order cache invalidation needed — visual-only parts do
            // not affect the task sequence. We still rebuild the part list and
            // respawn the scene so the new part shows up immediately.
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        private void RemoveOptionalPartFromStep(StepDefinition step, string partId)
        {
            if (step?.visualPartIds == null || string.IsNullOrEmpty(partId)) return;
            var list = new List<string>(step.optionalPartIds);
            if (!list.Remove(partId)) return;
            step.optionalPartIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
            _dirtyStepIds.Add(step.id);
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }
    }
}
