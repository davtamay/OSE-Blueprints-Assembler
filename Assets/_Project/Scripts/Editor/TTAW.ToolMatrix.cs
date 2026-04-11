// TTAW.ToolMatrix.cs — Part × Tool affinity matrix.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 7b. Closes the gap I called out in the Phase 0 audit:
//   "a part author has no way to see 'this part supports torque and weld'
//    without clicking each target individually."
//
// Renders a checkbox grid in the canvas where rows = parts, columns = tools.
// Toggling a cell mutates PartDefinition.toolIds[] in memory and stages the
// part id into _dirtyPartToolIds; the next WriteJson serialises just those
// parts via the existing entity-by-id InjectField pipeline.
//
// Two viewing modes are available via a small toggle:
//   • SCOPED — only the parts that are currently visible in this step
//              (matches the WHAT'S SHOWING section's union of buckets)
//   • ALL    — every part in the package, alphabetically sorted
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

        private bool _toolMatrixShowAll;     // false = scoped to this step's parts
        private Vector2 _toolMatrixScroll;
        private readonly List<string> _toolMatrixScratchParts = new();
        private readonly List<ToolDefinition> _toolMatrixScratchTools = new();

        // Same blue accent as the rest of the editor (subassembly + group bucket)
        // — colour reuse so the eye learns "blue = tool / group context".
        private static readonly Color ToolMatrixAccent = new(0.20f, 0.62f, 0.95f);

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────

        private void DrawToolAffinitySection(StepDefinition step)
        {
            if (_pkg == null || _pkg.tools == null || _pkg.tools.Length == 0)
                return;

            // ── Header row ────────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            GUILayout.Label(new GUIContent("PART × TOOL",
                "Which tools work on which parts. Tap a cell to toggle. Saves "
                + "to PartDefinition.toolIds[] in machine.json on Write."),
                titleStyle);
            GUILayout.FlexibleSpace();

            var modeScopedStyle = new GUIStyle(EditorStyles.miniButtonLeft)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = !_toolMatrixShowAll ? ToolMatrixAccent : new Color(0.55f, 0.55f, 0.58f) },
            };
            var modeAllStyle = new GUIStyle(EditorStyles.miniButtonRight)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = _toolMatrixShowAll ? ToolMatrixAccent : new Color(0.55f, 0.55f, 0.58f) },
            };
            if (GUILayout.Toggle(!_toolMatrixShowAll,
                    new GUIContent("THIS STEP", "Show only parts visible in this step."),
                    modeScopedStyle, GUILayout.Width(76), GUILayout.Height(18)))
                _toolMatrixShowAll = false;
            if (GUILayout.Toggle(_toolMatrixShowAll,
                    new GUIContent("ALL PARTS", "Show every part in the package."),
                    modeAllStyle, GUILayout.Width(76), GUILayout.Height(18)))
                _toolMatrixShowAll = true;
            EditorGUILayout.EndHorizontal();

            // ── Resolve rows / columns ────────────────────────────────────────
            CollectToolMatrixRows(step);
            if (_toolMatrixScratchParts.Count == 0)
            {
                var emptyStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter,
                };
                EditorGUILayout.LabelField(
                    _toolMatrixShowAll
                        ? "No parts in this package."
                        : "No parts visible in this step.",
                    emptyStyle);
                return;
            }

            CollectToolMatrixTools();
            if (_toolMatrixScratchTools.Count == 0) return;

            // ── Grid layout constants ─────────────────────────────────────────
            const float kPartLabelW = 180f;
            const float kCellW      = 64f;
            const float kCellH      = 18f;
            const float kHeaderH    = 38f;

            float gridW = kPartLabelW + (kCellW * _toolMatrixScratchTools.Count) + 8f;

            EditorGUILayout.Space(2);
            _toolMatrixScroll = EditorGUILayout.BeginScrollView(_toolMatrixScroll,
                GUILayout.Height(Mathf.Min(360f, kHeaderH + (_toolMatrixScratchParts.Count * kCellH) + 8f)));

            // ── Column header (tool names rotated would be ideal but Handles
            //    text rotation in IMGUI is fragile; horizontal labels work) ────
            var headerRect = GUILayoutUtility.GetRect(gridW, kHeaderH, GUILayout.Width(gridW));
            EditorGUI.DrawRect(headerRect, new Color(0f, 0f, 0f, 0.20f));
            // Tint the row of tool name labels with the accent
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 2f, headerRect.width, 2f), ToolMatrixAccent);

            var toolHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = ToolMatrixAccent },
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold,
                wordWrap  = true,
                clipping  = TextClipping.Clip,
            };
            for (int c = 0; c < _toolMatrixScratchTools.Count; c++)
            {
                var tool = _toolMatrixScratchTools[c];
                var cellRect = new Rect(
                    headerRect.x + kPartLabelW + (c * kCellW),
                    headerRect.y + 2f,
                    kCellW - 2f,
                    headerRect.height - 4f);
                GUI.Label(cellRect, tool.GetDisplayName(), toolHeaderStyle);
            }

            // ── Body rows ─────────────────────────────────────────────────────
            for (int r = 0; r < _toolMatrixScratchParts.Count; r++)
            {
                string partId = _toolMatrixScratchParts[r];
                var rowRect = GUILayoutUtility.GetRect(gridW, kCellH, GUILayout.Width(gridW));
                if ((r & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                // Part id label (clipped)
                var labelRect = new Rect(rowRect.x + 6f, rowRect.y, kPartLabelW - 8f, rowRect.height);
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping  = TextClipping.Clip,
                };
                GUI.Label(labelRect, partId, labelStyle);

                // Resolve current tool set once per row
                var part = FindPartById(partId);
                if (part == null) continue;
                var currentToolIds = part.toolIds != null
                    ? new HashSet<string>(part.toolIds, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);

                // Cells
                for (int c = 0; c < _toolMatrixScratchTools.Count; c++)
                {
                    var tool = _toolMatrixScratchTools[c];
                    var cellRect = new Rect(
                        rowRect.x + kPartLabelW + (c * kCellW),
                        rowRect.y + 1f,
                        kCellW - 2f,
                        rowRect.height - 2f);

                    bool isOn   = currentToolIds.Contains(tool.id);
                    bool newOn  = DrawMatrixCell(cellRect, isOn);
                    if (newOn != isOn)
                        ToggleToolForPart(part, tool.id, newOn);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Cell rendering ────────────────────────────────────────────────────

        /// <summary>
        /// Draws a single matrix cell. Filled accent square when on, hollow
        /// outline when off. Click anywhere on the cell to toggle.
        /// </summary>
        private static bool DrawMatrixCell(Rect rect, bool isOn)
        {
            // Hover-tint background
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

            // Centred indicator
            float pad = 4f;
            var indicator = new Rect(
                rect.x + (rect.width  - pad * 2f) * 0.5f - 4f + pad,
                rect.y + (rect.height - pad * 2f) * 0.5f - 4f + pad,
                8f, 8f);
            // Background border (always)
            EditorGUI.DrawRect(indicator, new Color(0.45f, 0.45f, 0.50f, 0.50f));
            if (isOn)
            {
                // Inner fill
                var fill = new Rect(indicator.x + 1f, indicator.y + 1f,
                                    indicator.width - 2f, indicator.height - 2f);
                EditorGUI.DrawRect(fill, ToolMatrixAccent);
            }

            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return !isOn;
            }
            return isOn;
        }

        // ── Row / column collection ───────────────────────────────────────────

        private void CollectToolMatrixRows(StepDefinition step)
        {
            _toolMatrixScratchParts.Clear();
            if (_pkg == null) return;

            if (_toolMatrixShowAll || step == null)
            {
                var allParts = _pkg.GetParts();
                for (int i = 0; i < allParts.Length; i++)
                {
                    if (allParts[i] == null || string.IsNullOrEmpty(allParts[i].id)) continue;
                    _toolMatrixScratchParts.Add(allParts[i].id);
                }
                _toolMatrixScratchParts.Sort(StringComparer.Ordinal);
                return;
            }

            // Scoped — union of every part visible in this step. Reuse the
            // already-computed visibility buckets if they're populated, so the
            // matrix tracks WHAT'S SHOWING exactly. ComputeVisibilityBuckets
            // is cheap (linear in package size) so re-running it here is fine.
            ComputeVisibilityBuckets(step, out _, out _);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void AddRange(List<string> src)
            {
                foreach (var pid in src) if (seen.Add(pid)) _toolMatrixScratchParts.Add(pid);
            }
            AddRange(_visScratchOwnedHere);
            AddRange(_visScratchVisualOnlyHere);
            AddRange(_visScratchOwnedSubHere);
            AddRange(_visScratchInheritedEarlier);
            _toolMatrixScratchParts.Sort(StringComparer.Ordinal);
        }

        private void CollectToolMatrixTools()
        {
            _toolMatrixScratchTools.Clear();
            if (_pkg?.tools == null) return;
            foreach (var t in _pkg.tools)
                if (t != null && !string.IsNullOrEmpty(t.id))
                    _toolMatrixScratchTools.Add(t);
            _toolMatrixScratchTools.Sort((a, b) =>
                StringComparer.Ordinal.Compare(a.GetDisplayName(), b.GetDisplayName()));
        }

        private PartDefinition FindPartById(string partId)
        {
            if (_pkg?.parts == null || string.IsNullOrEmpty(partId)) return null;
            for (int i = 0; i < _pkg.parts.Length; i++)
                if (_pkg.parts[i]?.id == partId) return _pkg.parts[i];
            return null;
        }

        // ── Mutation ──────────────────────────────────────────────────────────

        private void ToggleToolForPart(PartDefinition part, string toolId, bool desiredState)
        {
            if (part == null || string.IsNullOrEmpty(toolId)) return;

            var list = part.toolIds != null
                ? new List<string>(part.toolIds)
                : new List<string>();

            bool currentlyOn = list.Contains(toolId);
            if (desiredState && !currentlyOn) list.Add(toolId);
            else if (!desiredState && currentlyOn) list.Remove(toolId);
            else return;

            part.toolIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
            _dirtyPartToolIds.Add(part.id);
            Repaint();
        }
    }
}
