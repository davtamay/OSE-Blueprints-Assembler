// TTAW.ToolMatrix.cs — Part × Tool affinity matrix.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 7b + polish. Renders a checkbox grid where rows = parts, columns =
// tools. Toggling a cell mutates PartDefinition.toolIds[] in memory and stages
// the part id into _dirtyPartToolIds; the next WriteJson serialises just those
// parts via the existing entity-by-id InjectField pipeline.
//
// Polish additions:
//   • Column hide/show — per-tool eye toggles above the header strip so
//     authors can focus on the tools they care about
//   • Row sorting — sort alphabetically (default), by tool count descending,
//     or by part category; compact mode-toggle at the top
//   • Summary counts — per-row tool-count pill on the right edge, per-column
//     part-count at the bottom
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
        private int  _toolMatrixSortMode;    // 0 = alpha, 1 = tool count desc, 2 = category
        private Vector2 _toolMatrixScroll;
        private readonly List<string> _toolMatrixScratchParts = new();
        private readonly List<ToolDefinition> _toolMatrixScratchTools = new();
        private readonly HashSet<string> _toolMatrixHiddenTools = new(StringComparer.Ordinal);

        private static readonly Color ToolMatrixAccent = new(0.20f, 0.62f, 0.95f);
        private static readonly string[] _toolMatrixSortLabels = { "A-Z", "# Tools", "Category" };

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

            // Sort mode
            var sortStyle = new GUIStyle(EditorStyles.miniButton) { fontSize = 9 };
            for (int s = 0; s < _toolMatrixSortLabels.Length; s++)
            {
                bool active = _toolMatrixSortMode == s;
                var style = new GUIStyle(s == 0 ? EditorStyles.miniButtonLeft
                    : s == _toolMatrixSortLabels.Length - 1 ? EditorStyles.miniButtonRight
                    : EditorStyles.miniButtonMid)
                {
                    fontSize  = 9,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = active ? ToolMatrixAccent : new Color(0.55f, 0.55f, 0.58f) },
                };
                if (GUILayout.Toggle(active, _toolMatrixSortLabels[s], style,
                        GUILayout.Width(52), GUILayout.Height(16)))
                    _toolMatrixSortMode = s;
            }

            GUILayout.Space(6);

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
                    modeScopedStyle, GUILayout.Width(76), GUILayout.Height(16)))
                _toolMatrixShowAll = false;
            if (GUILayout.Toggle(_toolMatrixShowAll,
                    new GUIContent("ALL PARTS", "Show every part in the package."),
                    modeAllStyle, GUILayout.Width(76), GUILayout.Height(16)))
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

            // Filter out hidden tools
            var visibleTools = new List<ToolDefinition>();
            foreach (var t in _toolMatrixScratchTools)
                if (!_toolMatrixHiddenTools.Contains(t.id))
                    visibleTools.Add(t);

            // ── Grid layout constants ─────────────────────────────────────────
            const float kPartLabelW = 180f;
            const float kCellW      = 64f;
            const float kCellH      = 18f;
            const float kHeaderH    = 38f;
            const float kCountW     = 30f; // right-edge count pill per row

            float gridW = kPartLabelW + (kCellW * visibleTools.Count) + kCountW + 8f;

            EditorGUILayout.Space(2);

            // ── Column visibility toggles (eye row) ───────────────────────────
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(kPartLabelW + 4f);
            foreach (var tool in _toolMatrixScratchTools)
            {
                bool visible = !_toolMatrixHiddenTools.Contains(tool.id);
                var eyeStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontSize  = 9,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = visible ? ToolMatrixAccent : new Color(0.4f, 0.4f, 0.44f) },
                };
                string label = visible ? "●" : "○";
                if (GUILayout.Button(new GUIContent(label, $"{(visible ? "Hide" : "Show")} {tool.GetDisplayName()}"),
                        eyeStyle, GUILayout.Width(kCellW - 2f), GUILayout.Height(14)))
                {
                    if (visible) _toolMatrixHiddenTools.Add(tool.id);
                    else         _toolMatrixHiddenTools.Remove(tool.id);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (visibleTools.Count == 0)
            {
                EditorGUILayout.LabelField("  All tools hidden — click ○ above to show.",
                    EditorStyles.miniLabel);
                return;
            }

            _toolMatrixScroll = EditorGUILayout.BeginScrollView(_toolMatrixScroll,
                GUILayout.Height(Mathf.Min(360f, kHeaderH + (_toolMatrixScratchParts.Count * kCellH) + 28f)));

            // ── Column header ─────────────────────────────────────────────────
            var headerRect = GUILayoutUtility.GetRect(gridW, kHeaderH, GUILayout.Width(gridW));
            EditorGUI.DrawRect(headerRect, new Color(0f, 0f, 0f, 0.20f));
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 2f, headerRect.width, 2f), ToolMatrixAccent);

            var toolHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = ToolMatrixAccent },
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold,
                wordWrap  = true,
                clipping  = TextClipping.Clip,
            };
            for (int c = 0; c < visibleTools.Count; c++)
            {
                var cellRect = new Rect(
                    headerRect.x + kPartLabelW + (c * kCellW),
                    headerRect.y + 2f,
                    kCellW - 2f,
                    headerRect.height - 4f);
                GUI.Label(cellRect, visibleTools[c].GetDisplayName(), toolHeaderStyle);
            }
            // "#" count header
            var countHeaderRect = new Rect(headerRect.x + kPartLabelW + (visibleTools.Count * kCellW),
                headerRect.y + 2f, kCountW, headerRect.height - 4f);
            var countHeaderStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.6f, 0.6f, 0.65f) },
            };
            GUI.Label(countHeaderRect, "#", countHeaderStyle);

            // ── Body rows ─────────────────────────────────────────────────────
            var colCounts = new int[visibleTools.Count]; // accumulate per-column counts

            for (int r = 0; r < _toolMatrixScratchParts.Count; r++)
            {
                string partId = _toolMatrixScratchParts[r];
                var rowRect = GUILayoutUtility.GetRect(gridW, kCellH, GUILayout.Width(gridW));
                if ((r & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                var labelRect = new Rect(rowRect.x + 6f, rowRect.y, kPartLabelW - 8f, rowRect.height);
                GUI.Label(labelRect, partId, new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping  = TextClipping.Clip,
                });

                var part = FindPartById(partId);
                if (part == null) continue;
                var currentToolIds = part.toolIds != null
                    ? new HashSet<string>(part.toolIds, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);

                int rowCount = 0;
                for (int c = 0; c < visibleTools.Count; c++)
                {
                    var tool = visibleTools[c];
                    var cellRect = new Rect(
                        rowRect.x + kPartLabelW + (c * kCellW),
                        rowRect.y + 1f,
                        kCellW - 2f,
                        rowRect.height - 2f);

                    bool isOn  = currentToolIds.Contains(tool.id);
                    bool newOn = DrawMatrixCell(cellRect, isOn);
                    if (newOn != isOn)
                        ToggleToolForPart(part, tool.id, newOn);

                    if (isOn) { rowCount++; colCounts[c]++; }
                }

                // Row count pill
                var countRect = new Rect(
                    rowRect.x + kPartLabelW + (visibleTools.Count * kCellW),
                    rowRect.y, kCountW, rowRect.height);
                var countStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = rowCount > 0
                        ? new Color(0.6f, 0.8f, 0.95f)
                        : new Color(0.4f, 0.4f, 0.44f) },
                };
                GUI.Label(countRect, rowCount.ToString(), countStyle);
            }

            // ── Column count footer ───────────────────────────────────────────
            var footerRect = GUILayoutUtility.GetRect(gridW, kCellH, GUILayout.Width(gridW));
            EditorGUI.DrawRect(footerRect, new Color(0f, 0f, 0f, 0.15f));
            EditorGUI.DrawRect(new Rect(footerRect.x, footerRect.y, footerRect.width, 1f),
                new Color(0.5f, 0.5f, 0.55f, 0.3f));

            var footLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Italic,
                normal    = { textColor = new Color(0.55f, 0.55f, 0.60f) },
            };
            GUI.Label(new Rect(footerRect.x + 4f, footerRect.y, kPartLabelW - 8f, footerRect.height),
                "parts using →", footLabelStyle);

            var colCountStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.6f, 0.8f, 0.95f) },
            };
            for (int c = 0; c < visibleTools.Count; c++)
            {
                var ccRect = new Rect(
                    footerRect.x + kPartLabelW + (c * kCellW),
                    footerRect.y,
                    kCellW - 2f,
                    footerRect.height);
                GUI.Label(ccRect, colCounts[c].ToString(), colCountStyle);
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Cell rendering ────────────────────────────────────────────────────

        private static bool DrawMatrixCell(Rect rect, bool isOn)
        {
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.05f));

            float pad = 4f;
            var indicator = new Rect(
                rect.x + (rect.width  - pad * 2f) * 0.5f - 4f + pad,
                rect.y + (rect.height - pad * 2f) * 0.5f - 4f + pad,
                8f, 8f);
            EditorGUI.DrawRect(indicator, new Color(0.45f, 0.45f, 0.50f, 0.50f));
            if (isOn)
            {
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
            }
            else
            {
                ComputeVisibilityBuckets(step, out _, out _);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                void AddRange(List<string> src)
                {
                    foreach (var pid in src) if (seen.Add(pid)) _toolMatrixScratchParts.Add(pid);
                }
                AddRange(_visScratchOwnedHere);
                AddRange(_visScratchOptionalHere);
                AddRange(_visScratchOwnedSubHere);
                AddRange(_visScratchInheritedEarlier);
            }

            // Apply sort mode
            switch (_toolMatrixSortMode)
            {
                case 0: // A-Z
                    _toolMatrixScratchParts.Sort(StringComparer.Ordinal);
                    break;
                case 1: // # tools descending (most-wired first)
                    _toolMatrixScratchParts.Sort((a, b) =>
                    {
                        int ca = FindPartById(a)?.toolIds?.Length ?? 0;
                        int cb = FindPartById(b)?.toolIds?.Length ?? 0;
                        int cmp = cb.CompareTo(ca); // desc
                        return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a, b);
                    });
                    break;
                case 2: // category (alpha), then name within category
                    _toolMatrixScratchParts.Sort((a, b) =>
                    {
                        string catA = FindPartById(a)?.category ?? "";
                        string catB = FindPartById(b)?.category ?? "";
                        int cmp = StringComparer.OrdinalIgnoreCase.Compare(catA, catB);
                        return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a, b);
                    });
                    break;
            }
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
