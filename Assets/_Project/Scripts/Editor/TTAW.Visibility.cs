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
//     step.requiredPartGroupId → partGroup.partIds
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

        // Slice ME-B: per-(stepId, kindTag, transitionLabel) expand state for
        // WHAT'S CHANGING aggregate rows. When 3+ rows share a (kind, label),
        // they collapse into one summary row with a chevron that unfolds the
        // children. Cleared whenever the cached step changes so expansion
        // state doesn't leak across steps.
        private readonly HashSet<string> _whatsChangingExpandedKeys =
            new(StringComparer.Ordinal);
        private int _whatsChangingExpandedForStepSeq = int.MinValue;

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

        // Cached delta between (N-1, N). Recomputed only when currentSeq
        // or the package's dirty fingerprint changes — redraws inside the
        // same step reuse the cache.
        private sealed class WhatsChangingRow
        {
            public string partId;
            public string kindTag;                     // "ENTERED" / "LEFT" / "SOURCE" / "STACKED" / "VALUE"
            public string transitionLabel;              // "A → B" for display
            public string gotoStepId;                   // step to jump to when Go is clicked
            public string tooltip;                      // full tag details
        }
        private List<WhatsChangingRow> _whatsChangingCache;
        private int _whatsChangingCachedSeq = int.MinValue;

        private const string PrefWhatsChangingOpen = "OSE.TTAW.WhatsChangingOpen";

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────

        private void DrawVisibilitySection(StepDefinition step, bool drawOwnChrome = true)
        {
            if (_pkg == null || step == null) return;

            // Compute the four visibility buckets from cached scene-build state
            // and the step's own part ids. The scene-build cache is populated by
            // RespawnScene the same way the spawner runs at runtime, so the
            // numbers shown here always match what's drawn in the SceneView.
            ComputeVisibilityBuckets(step,
                                     out int totalVisible,
                                     out HashSet<string> ownedSubPartIds);

            int toolCount = step.requiredToolActions?.Length ?? 0;
            int wireCount = string.Equals(step.ResolvedFamily.ToString(), "Connect", StringComparison.Ordinal)
                ? (step.targetIds?.Length ?? 0) : 0;

            // Internal header + foldout — drawn only when the caller isn't
            // already providing card chrome. The Slice-ME-C card wrapper
            // supplies its own chevron + collapse state, so drawOwnChrome=false
            // skips the inner foldout entirely (no double-nested collapse) and
            // renders the count pills + body unconditionally.
            if (drawOwnChrome)
            {
                EditorGUILayout.BeginHorizontal();
                string chevron = _visibilityBucketsExpanded ? "▼" : "▶";
                var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
                if (GUILayout.Button($"{chevron} WHAT'S SHOWING", titleStyle, GUILayout.ExpandWidth(false)))
                    _visibilityBucketsExpanded = !_visibilityBucketsExpanded;
                GUILayout.FlexibleSpace();
                DrawCountPill(VisColorOwned,                    _visScratchOwnedHere.Count,  "parts");
                DrawCountPill(new Color(0.80f, 0.55f, 0.95f),  toolCount,                   "tools");
                DrawCountPill(new Color(0.95f, 0.55f, 0.35f),  wireCount,                   "wires");
                DrawCountPill(VisColorOptional,                 _visScratchOptionalHere.Count,"optional");
                DrawCountPill(VisColorSub,                      _visScratchOwnedSubHere.Count,"group");
                DrawCountPill(VisColorEarlier,                  _visScratchInheritedEarlier.Count, "earlier");
                EditorGUILayout.EndHorizontal();

                if (!_visibilityBucketsExpanded) return;
            }
            else
            {
                // Count pills still render at the top of the body (they're
                // information, not chrome) so authors see the summary without
                // needing to read each bucket.
                EditorGUILayout.BeginHorizontal();
                DrawCountPill(VisColorOwned,                    _visScratchOwnedHere.Count,  "parts");
                DrawCountPill(new Color(0.80f, 0.55f, 0.95f),  toolCount,                   "tools");
                DrawCountPill(new Color(0.95f, 0.55f, 0.35f),  wireCount,                   "wires");
                DrawCountPill(VisColorOptional,                 _visScratchOptionalHere.Count,"optional");
                DrawCountPill(VisColorSub,                      _visScratchOwnedSubHere.Count,"group");
                DrawCountPill(VisColorEarlier,                  _visScratchInheritedEarlier.Count, "earlier");
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            // Compact one-line legend
            var legendStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                fontStyle = FontStyle.Italic,
            };
            EditorGUILayout.LabelField(
                "  green = parts    purple = tools    orange = wires    amber = optional    blue = group    grey = earlier",
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

            // ── Tool actions bucket ───────────────────────────────────────────
            if (toolCount > 0)
            {
                var toolItems = new List<string>();
                foreach (var a in step.requiredToolActions)
                {
                    if (a == null) continue;
                    string toolName = a.toolId ?? "?";
                    if (_pkg?.tools != null)
                        foreach (var t in _pkg.tools)
                            if (t != null && t.id == a.toolId) { toolName = t.GetDisplayName(); break; }
                    toolItems.Add($"{a.id}  ({toolName})");
                }
                DrawVisibilityBucket("TOOL ACTIONS", new Color(0.80f, 0.55f, 0.95f),
                    toolItems, allowRemove: false, step: null);
            }

            // ── Wire connections bucket ───────────────────────────────────────
            if (wireCount > 0 && step.targetIds != null)
            {
                var wireItems = new List<string>(step.targetIds);
                DrawVisibilityBucket("WIRE CONNECTIONS", new Color(0.95f, 0.55f, 0.35f),
                    wireItems, allowRemove: false, step: null);
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
            // Hide zero-count pills entirely — they were visual noise on most
            // steps (a typical Place step shows "0 tools 0 wires 0 optional
            // 0 group 0 earlier" right next to the actually-meaningful "X parts"
            // pill). Only the buckets that have content take screen space.
            if (count <= 0) return;

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
            EditorGUI.DrawRect(rect, color);
            GUI.Label(rect, content, style);
            GUILayout.Space(3);
        }

        /// <summary>
        /// Diagnostic panel: for the currently-selected step, lists every
        /// part whose pose source or visibility changed versus the previous
        /// step. Surfaces "why did this part move" at a glance — the source
        /// tag identifies which field in previewConfig drove the pose.
        /// </summary>
        private void DrawWhatsChangingSection(StepDefinition step, bool drawOwnChrome = true)
        {
            if (_pkg == null || step == null) return;
            int currentSeq = step.sequenceIndex;

            // Refresh the delta cache when the step changes. We keep it per
            // step so redraws inside the same step are cheap.
            if (_whatsChangingCachedSeq != currentSeq || _whatsChangingCache == null)
            {
                _whatsChangingCache = BuildWhatsChangingRows(currentSeq);
                _whatsChangingCachedSeq = currentSeq;
            }
            var rows = _whatsChangingCache;

            int total = rows.Count;
            int cSource = 0, cEntered = 0, cLeft = 0, cStacked = 0, cValue = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                switch (rows[i].kindTag)
                {
                    case "ENTERED": cEntered++; break;
                    case "LEFT":    cLeft++;    break;
                    case "STACKED": cStacked++; break;
                    case "VALUE":   cValue++;   break;
                    default:        cSource++;  break;
                }
            }

            string summary = $"  {cSource} source · {cEntered} entered · {cLeft} left · {cStacked} stacked · {cValue} value";
            var summaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = new Color(0.68f, 0.68f, 0.72f) },
                alignment = TextAnchor.MiddleLeft,
            };

            // Internal header + foldout — drawn only when the caller isn't
            // providing card chrome. The Slice-ME-C card wrapper supplies its
            // own chevron + collapse state, so drawOwnChrome=false renders
            // the summary line inline and then the body directly.
            if (drawOwnChrome)
            {
                EditorGUILayout.Space(4);
                bool open = EditorPrefs.GetBool(PrefWhatsChangingOpen, false);

                EditorGUILayout.BeginHorizontal();
                var arrow = open ? "▼" : "▶";
                var hdrStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    normal = { textColor = total > 0 ? new Color(0.95f, 0.70f, 0.30f) : new Color(0.62f, 0.62f, 0.66f) },
                };
                if (GUILayout.Button($"{arrow} WHAT'S CHANGING ({total})", hdrStyle, GUILayout.ExpandWidth(false)))
                {
                    open = !open;
                    EditorPrefs.SetBool(PrefWhatsChangingOpen, open);
                }
                GUILayout.Label(summary, summaryStyle);
                EditorGUILayout.EndHorizontal();

                if (!open || total == 0) return;
            }
            else
            {
                // Card chrome handled outside. Render the summary line at the
                // top of the body, then bail early on zero rows so the card
                // shows an empty state naturally.
                EditorGUILayout.LabelField(summary, summaryStyle);
                if (total == 0) return;
            }

            // Slice ME-B: aggregate rows by (kindTag, transitionLabel). When
            // 3+ rows share a key, render one summary row "[kind] N parts —
            // transitionLabel [▸]" that expands to the children. Threshold
            // 3 chosen because 2 rows fit on screen and reading them costs
            // less than a click; at 3+, scanning breaks down.
            if (_whatsChangingExpandedForStepSeq != currentSeq)
            {
                _whatsChangingExpandedKeys.Clear();
                _whatsChangingExpandedForStepSeq = currentSeq;
            }

            var groupIndices = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var groupOrder   = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                string gKey = rows[i].kindTag + "\u0001" + rows[i].transitionLabel;
                if (!groupIndices.TryGetValue(gKey, out var list))
                {
                    groupIndices[gKey] = list = new List<int>();
                    groupOrder.Add(gKey);
                }
                list.Add(i);
            }

            const int aggregateThreshold = 3;
            foreach (var gKey in groupOrder)
            {
                var indices = groupIndices[gKey];
                if (indices.Count < aggregateThreshold)
                {
                    foreach (int idx in indices)
                        DrawWhatsChangingRow(rows[idx], indent: 0f);
                    continue;
                }

                // Aggregate row.
                string expandKey = step.id + "\u0002" + gKey;
                bool isExpanded = _whatsChangingExpandedKeys.Contains(expandKey);
                DrawWhatsChangingAggregateRow(rows, indices, isExpanded,
                    onToggle: () =>
                    {
                        if (isExpanded) _whatsChangingExpandedKeys.Remove(expandKey);
                        else            _whatsChangingExpandedKeys.Add(expandKey);
                    });

                if (isExpanded)
                {
                    foreach (int idx in indices)
                        DrawWhatsChangingRow(rows[idx], indent: 18f);
                }
            }
        }

        /// <summary>
        /// Draws a single WHAT'S CHANGING row — kind pill, part id, transition
        /// label, Go button. Slice ME-B extracted from the original inline
        /// loop so the aggregate path can reuse it for indented children.
        /// </summary>
        private void DrawWhatsChangingRow(WhatsChangingRow r, float indent)
        {
            EditorGUILayout.BeginHorizontal();
            if (indent > 0f) GUILayout.Space(indent);

            Color pillCol = r.kindTag switch
            {
                "ENTERED" => new Color(0.30f, 0.78f, 0.36f, 0.30f),
                "LEFT"    => new Color(0.85f, 0.40f, 0.40f, 0.30f),
                "STACKED" => new Color(0.95f, 0.65f, 0.30f, 0.30f),
                "VALUE"   => new Color(0.95f, 0.95f, 0.45f, 0.30f),
                _         => new Color(0.55f, 0.78f, 0.95f, 0.30f), // SOURCE
            };
            Color pillTxt = r.kindTag switch
            {
                "ENTERED" => new Color(0.55f, 0.95f, 0.55f),
                "LEFT"    => new Color(1.00f, 0.60f, 0.60f),
                "STACKED" => new Color(1.00f, 0.80f, 0.45f),
                "VALUE"   => new Color(1.00f, 1.00f, 0.55f),
                _         => new Color(0.70f, 0.88f, 1.00f),
            };
            var pillRect = GUILayoutUtility.GetRect(62f, 16f, GUILayout.Width(62f), GUILayout.Height(16f));
            EditorGUI.DrawRect(pillRect, pillCol);
            var pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = pillTxt },
                fontSize  = 8,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(pillRect, r.kindTag, pillStyle);

            var idStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            GUILayout.Label(r.partId, idStyle, GUILayout.MinWidth(160));

            var traceStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = new Color(0.80f, 0.82f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
            };
            GUILayout.Label(new GUIContent(r.transitionLabel, r.tooltip), traceStyle, GUILayout.ExpandWidth(true));

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(r.gotoStepId));
            if (GUILayout.Button("Go", EditorStyles.miniButton, GUILayout.Width(28)))
            {
                if (_stepIds != null)
                {
                    for (int k = 0; k < _stepIds.Length; k++)
                        if (string.Equals(_stepIds[k], r.gotoStepId, StringComparison.Ordinal))
                        { ApplyStepFilter(k); break; }
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the Slice-ME-B aggregate row that collapses N rows sharing a
        /// (kindTag, transitionLabel) pair into one line: kind pill +
        /// "N parts" count + transition label + Go button (enabled only when
        /// every child shares the same gotoStepId) + chevron that toggles
        /// the children's visibility.
        /// </summary>
        private void DrawWhatsChangingAggregateRow(List<WhatsChangingRow> rows,
            List<int> indices, bool isExpanded, Action onToggle)
        {
            if (indices == null || indices.Count == 0) return;
            var sample = rows[indices[0]];

            EditorGUILayout.BeginHorizontal();

            // Kind pill — same palette as individual rows.
            Color pillCol = sample.kindTag switch
            {
                "ENTERED" => new Color(0.30f, 0.78f, 0.36f, 0.30f),
                "LEFT"    => new Color(0.85f, 0.40f, 0.40f, 0.30f),
                "STACKED" => new Color(0.95f, 0.65f, 0.30f, 0.30f),
                "VALUE"   => new Color(0.95f, 0.95f, 0.45f, 0.30f),
                _         => new Color(0.55f, 0.78f, 0.95f, 0.30f),
            };
            Color pillTxt = sample.kindTag switch
            {
                "ENTERED" => new Color(0.55f, 0.95f, 0.55f),
                "LEFT"    => new Color(1.00f, 0.60f, 0.60f),
                "STACKED" => new Color(1.00f, 0.80f, 0.45f),
                "VALUE"   => new Color(1.00f, 1.00f, 0.55f),
                _         => new Color(0.70f, 0.88f, 1.00f),
            };
            var pillRect = GUILayoutUtility.GetRect(62f, 16f, GUILayout.Width(62f), GUILayout.Height(16f));
            EditorGUI.DrawRect(pillRect, pillCol);
            var pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = pillTxt },
                fontSize  = 8,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            GUI.Label(pillRect, sample.kindTag, pillStyle);

            // "N parts" count in the id column (bold to signal aggregation).
            var countStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.92f, 0.92f, 0.95f) },
            };
            GUILayout.Label($"{indices.Count} parts", countStyle, GUILayout.MinWidth(160));

            // Shared transition label.
            var traceStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = new Color(0.80f, 0.82f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
            };
            GUILayout.Label(new GUIContent(sample.transitionLabel,
                $"{indices.Count} parts share this pose transition. Click ▸ to expand."),
                traceStyle, GUILayout.ExpandWidth(true));

            // Go button: enabled only when every child shares the same
            // gotoStepId. Otherwise disabled with an explanatory tooltip —
            // expand and use a specific row's Go.
            string sharedGotoStepId = sample.gotoStepId;
            bool allShareGoto = true;
            for (int i = 1; i < indices.Count; i++)
            {
                string g = rows[indices[i]].gotoStepId;
                if (!string.Equals(g, sharedGotoStepId, StringComparison.Ordinal))
                { allShareGoto = false; break; }
            }
            EditorGUI.BeginDisabledGroup(!allShareGoto || string.IsNullOrEmpty(sharedGotoStepId));
            string goTip = allShareGoto
                ? (string.IsNullOrEmpty(sharedGotoStepId) ? "No gotoStep on this aggregate." : $"Jump to step '{sharedGotoStepId}'.")
                : "Children have different gotoStepIds — expand and use a specific row's Go.";
            if (GUILayout.Button(new GUIContent("Go", goTip), EditorStyles.miniButton, GUILayout.Width(28)))
            {
                if (_stepIds != null)
                {
                    for (int k = 0; k < _stepIds.Length; k++)
                        if (string.Equals(_stepIds[k], sharedGotoStepId, StringComparison.Ordinal))
                        { ApplyStepFilter(k); break; }
                }
            }
            EditorGUI.EndDisabledGroup();

            // Chevron toggles the aggregate's expanded state.
            var chevStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.95f, 0.70f, 0.30f) },
            };
            string chev = isExpanded ? "▾" : "▸";
            if (GUILayout.Button(new GUIContent(chev,
                    isExpanded ? "Collapse aggregate." : $"Expand {indices.Count} individual rows."),
                chevStyle, GUILayout.Width(22)))
            {
                onToggle?.Invoke();
            }

            EditorGUILayout.EndHorizontal();
        }

        private List<WhatsChangingRow> BuildWhatsChangingRows(int currentSeq)
        {
            var result = new List<WhatsChangingRow>();
            if (_pkg?.steps == null) return result;

            int prevSeq = currentSeq - 1;

            // Collect every partId that could be visible in the package
            // window around currentSeq. Iterate over _parts so the list
            // mirrors what BuildPartList already includes (Req / Opt / Vis).
            if (_parts == null) return result;
            for (int i = 0; i < _parts.Length; i++)
            {
                var def = _parts[i].def;
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                string pid = def.id;

                TracePartPoseAtStep(pid, currentSeq, out Vector3 posN, out Quaternion rotN, out Vector3 _, out PoseSourceTag tagN);
                Vector3 posP = Vector3.zero;
                Quaternion rotP = Quaternion.identity;
                PoseSourceTag tagP = new PoseSourceTag(PoseSourceKind.Hidden);
                if (prevSeq >= 0)
                    TracePartPoseAtStep(pid, prevSeq, out posP, out rotP, out Vector3 _ps, out tagP);

                bool nVisible = tagN.kind != PoseSourceKind.Hidden;
                bool pVisible = prevSeq >= 0 && tagP.kind != PoseSourceKind.Hidden;
                if (!nVisible && !pVisible) continue;

                var row = new WhatsChangingRow { partId = pid };

                if (!pVisible && nVisible)
                {
                    row.kindTag = "ENTERED";
                    row.transitionLabel = $"— → {tagN.PrettyLabel()}";
                    row.gotoStepId = FindStepIdBySeq(currentSeq);
                    row.tooltip = $"First appearance at step [{currentSeq}]";
                    result.Add(row);
                    continue;
                }
                if (pVisible && !nVisible)
                {
                    row.kindTag = "LEFT";
                    row.transitionLabel = $"{tagP.PrettyLabel()} → hidden";
                    row.gotoStepId = FindStepIdBySeq(currentSeq);
                    row.tooltip = $"No longer visible at step [{currentSeq}]";
                    result.Add(row);
                    continue;
                }

                // Both visible: source-change vs value-change.
                if (!tagN.ValueEquals(tagP))
                {
                    bool isStacked =
                        (tagP.kind == PoseSourceKind.AssembledPosition || tagP.kind == PoseSourceKind.StartPosition)
                        && tagN.kind == PoseSourceKind.Integrated;
                    row.kindTag = isStacked ? "STACKED" : "SOURCE";
                    row.transitionLabel = $"{tagP.PrettyLabel()} → {tagN.PrettyLabel()}";
                    row.gotoStepId = tagN.kind == PoseSourceKind.StepPose
                        ? tagN.anchorStepId
                        : FindStepIdBySeq(currentSeq);
                    row.tooltip = $"Pose source changed between steps [{prevSeq}] → [{currentSeq}]";
                    result.Add(row);
                    continue;
                }

                // Same source, check value drift.
                float posDelta = (posN - posP).magnitude;
                float rotDelta = Quaternion.Angle(rotN, rotP);
                if (posDelta > 0.0005f || rotDelta > 0.01f)
                {
                    row.kindTag = "VALUE";
                    row.transitionLabel = $"{tagN.PrettyLabel()}   Δpos={posDelta:0.0000}m  Δrot={rotDelta:0.00}°";
                    row.gotoStepId = tagN.kind == PoseSourceKind.StepPose
                        ? tagN.anchorStepId
                        : FindStepIdBySeq(currentSeq);
                    row.tooltip = $"Same source, numeric drift between steps [{prevSeq}] → [{currentSeq}]";
                    result.Add(row);
                }
            }

            // Order: STACKED, SOURCE, ENTERED, LEFT, VALUE — severity-first.
            int Rank(string k) => k switch { "STACKED" => 0, "SOURCE" => 1, "ENTERED" => 2, "LEFT" => 3, "VALUE" => 4, _ => 99 };
            result.Sort((a, b) => Rank(a.kindTag).CompareTo(Rank(b.kindTag)));
            return result;
        }

        private string FindStepIdBySeq(int seq)
        {
            if (_pkg?.steps == null) return null;
            foreach (var s in _pkg.steps)
                if (s != null && s.sequenceIndex == seq) return s.id;
            return null;
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

            // Collect this step's required-partGroup parts so we can label
            // them separately and exclude them from the "owned by this step"
            // bucket (the requiredPartIds bucket).
            if (!string.IsNullOrEmpty(step.requiredPartGroupId)
                && _pkg.TryGetPartGroup(step.requiredPartGroupId, out PartGroupDefinition subDef)
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
                string chosen = candidates[_visibilityAddPartIdx];
                if (_visibilityAddAsOptional)
                {
                    AddOptionalPartToStep(step, chosen);
                }
                else
                {
                    // Auto-route: on Confirm-family steps that have no targets
                    // and no tool actions, default new parts to NoTask so the
                    // author doesn't accidentally turn a no-action step into
                    // a task-laden one. Place/Use/Connect families still land
                    // in requiredPartIds.
                    PartRole role = ResolveDefaultPartRole(step);
                    if (role == PartRole.NoTask) AddVisualPartToStep(step, chosen);
                    else                         AddRequiredPartToStep(step, chosen);
                }
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

        // ── No-Task (visualPartIds) mutators ──────────────────────────────────

        /// <summary>
        /// Adds <paramref name="partId"/> to <see cref="StepDefinition.visualPartIds"/>
        /// so the part is visible at the step but attached to no task. Mirrors
        /// <see cref="AddRequiredPartToStep"/> / <see cref="AddOptionalPartToStep"/>.
        /// </summary>
        private void AddVisualPartToStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;
            var list = step.visualPartIds != null
                ? new List<string>(step.visualPartIds)
                : new List<string>();
            if (list.Contains(partId)) return;
            list.Add(partId);
            step.visualPartIds = list.ToArray();
            _dirtyStepIds.Add(step.id);
            InvalidateTaskOrderCache();
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        /// <summary>
        /// Removes <paramref name="partId"/> from <see cref="StepDefinition.requiredPartIds"/>
        /// and <see cref="StepDefinition.optionalPartIds"/> and appends it to
        /// <see cref="StepDefinition.visualPartIds"/>. Primary retroactive-fix
        /// affordance behind the row's "Mark as No Task" right-click action.
        /// </summary>
        private void MarkPartAsNoTask(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;

            if (step.requiredPartIds != null)
            {
                var req = new List<string>(step.requiredPartIds);
                if (req.Remove(partId)) step.requiredPartIds = req.Count > 0 ? req.ToArray() : Array.Empty<string>();
            }
            if (step.optionalPartIds != null)
            {
                var opt = new List<string>(step.optionalPartIds);
                if (opt.Remove(partId)) step.optionalPartIds = opt.Count > 0 ? opt.ToArray() : Array.Empty<string>();
            }

            var vis = step.visualPartIds != null
                ? new List<string>(step.visualPartIds)
                : new List<string>();
            if (!vis.Contains(partId)) vis.Add(partId);
            step.visualPartIds = vis.ToArray();

            _dirtyStepIds.Add(step.id);
            InvalidateTaskOrderCache();
            BuildPartList();
            BuildTargetList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        /// <summary>
        /// Inverse of <see cref="MarkPartAsNoTask"/> — used by the `R|O` toggle
        /// when the author clicks it on a no-task row, promoting the part into
        /// <paramref name="requiredNotOptional"/> == true ? required : optional.
        /// </summary>
        private void SetPartRoleForStep(StepDefinition step, string partId, PartRole role)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return;

            // Warn (don't block) when another Place-family step already
            // requires this partId. Matches CommitAddPart — authoring should
            // never refuse a mutation the author explicitly asked for; the
            // save-time dialog offers auto-fix when the runtime rule needs
            // enforcing.
            if (role == PartRole.Required && step.ResolvedFamily == StepFamily.Place && _pkg?.steps != null)
            {
                foreach (var other in _pkg.steps)
                {
                    if (other == null || other == step) continue;
                    if (other.ResolvedFamily != StepFamily.Place) continue;
                    if (other.requiredPartIds == null) continue;
                    foreach (var op in other.requiredPartIds)
                    {
                        if (string.Equals(op, partId, StringComparison.Ordinal))
                        {
                            ShowNotification(new GUIContent(
                                $"⚠ '{partId}' is also Required in Place step '{other.id}'. Resolve at save time (auto-fix) or demote the other step."));
                            goto PromoteConflictHandled;
                        }
                    }
                }
            }
            PromoteConflictHandled:

            if (step.requiredPartIds != null)
            {
                var req = new List<string>(step.requiredPartIds);
                if (req.Remove(partId)) step.requiredPartIds = req.Count > 0 ? req.ToArray() : Array.Empty<string>();
            }
            if (step.optionalPartIds != null)
            {
                var opt = new List<string>(step.optionalPartIds);
                if (opt.Remove(partId)) step.optionalPartIds = opt.Count > 0 ? opt.ToArray() : Array.Empty<string>();
            }
            if (step.visualPartIds != null)
            {
                var vis = new List<string>(step.visualPartIds);
                if (vis.Remove(partId)) step.visualPartIds = vis.Count > 0 ? vis.ToArray() : Array.Empty<string>();
            }

            switch (role)
            {
                case PartRole.Required:
                {
                    var list = step.requiredPartIds != null ? new List<string>(step.requiredPartIds) : new List<string>();
                    list.Add(partId);
                    step.requiredPartIds = list.ToArray();
                    break;
                }
                case PartRole.Optional:
                {
                    var list = step.optionalPartIds != null ? new List<string>(step.optionalPartIds) : new List<string>();
                    list.Add(partId);
                    step.optionalPartIds = list.ToArray();
                    break;
                }
                case PartRole.NoTask:
                {
                    var list = step.visualPartIds != null ? new List<string>(step.visualPartIds) : new List<string>();
                    list.Add(partId);
                    step.visualPartIds = list.ToArray();
                    // When marking as NO TASK, capture the current displayed
                    // pose as a stepPose on this step so the part "sticks"
                    // where the author sees it and propagates forward by
                    // default. Without this, authoring NO TASK would require
                    // a second manual pose step.
                    CaptureCurrentPoseAsStepPose(step, partId);
                    break;
                }
            }

            _dirtyStepIds.Add(step.id);
            // Keep taskOrder in sync with the role arrays we just mutated so
            // the part we promoted/demoted also appears (or disappears) in
            // the authoring task sequence — no more "invisibly Required"
            // drift between taskOrder and requiredPartIds.
            ReconcileStepTaskOrder(step);
            InvalidateTaskOrderCache();
            BuildPartList();
            BuildTargetList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        /// <summary>
        /// Materialises a <see cref="StepPoseEntry"/> on the part's
        /// <c>stepPoses</c> list at <paramref name="step"/>, using the pose
        /// the editor is currently displaying for the part. Defaults the span
        /// to "this step → end" (empty <c>propagateThroughStep</c>, forward
        /// fallthrough) so the pose automatically carries into subsequent
        /// steps — author doesn't have to touch the pose UI separately.
        /// </summary>
        private void CaptureCurrentPoseAsStepPose(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId) || _parts == null) return;

            int partIdx = -1;
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i].def != null && string.Equals(_parts[i].def.id, partId, StringComparison.Ordinal))
                { partIdx = i; break; }
            if (partIdx < 0) return;

            ref PartEditState p = ref _parts[partIdx];

            // Prefer the live GO's current world pose so whatever the author
            // sees on-screen is exactly what gets captured. Fall back to
            // assembledPosition when the live GO isn't available.
            Vector3 pos; Quaternion rot; Vector3 scl;
            var liveGO = FindLivePartGO(partId);
            var previewRoot = GetPreviewRoot();
            if (liveGO != null && previewRoot != null)
            {
                pos = previewRoot.InverseTransformPoint(liveGO.transform.position);
                rot = Quaternion.Inverse(previewRoot.rotation) * liveGO.transform.rotation;
                scl = liveGO.transform.localScale;
            }
            else if (liveGO != null)
            {
                pos = liveGO.transform.localPosition;
                rot = liveGO.transform.localRotation;
                scl = liveGO.transform.localScale;
            }
            else
            {
                pos = p.assembledPosition;
                rot = p.assembledRotation;
                scl = p.assembledScale;
            }
            if (scl.sqrMagnitude < 0.00001f) scl = Vector3.one;

            if (p.stepPoses == null) p.stepPoses = new List<StepPoseEntry>();

            // If an entry already exists for this step, update it instead
            // of adding a duplicate so repeated toggling doesn't stack poses.
            StepPoseEntry target = null;
            for (int i = 0; i < p.stepPoses.Count; i++)
                if (p.stepPoses[i] != null && string.Equals(p.stepPoses[i].stepId, step.id, StringComparison.Ordinal))
                { target = p.stepPoses[i]; break; }

            if (target == null)
            {
                target = new StepPoseEntry { stepId = step.id };
                p.stepPoses.Add(target);
            }
            target.position = PackageJsonUtils.ToFloat3(pos);
            target.rotation = PackageJsonUtils.ToQuaternion(rot);
            target.scale    = PackageJsonUtils.ToFloat3(scl);
            // Default span: this step → end (forward fallthrough). Author
            // can narrow via the propagation row if they want.
            target.propagateFromStep    = step.id;
            target.propagateThroughStep = "";
            // Mark the part as placed so SyncAllPartMeshesToActivePose no
            // longer skips it (the loop early-returns on !hasPlacement).
            // Also seed assembled* fields with the captured pose so any
            // fallback code path (editor or runtime) at least lands close
            // to the right spot until the stepPose span resolver picks up.
            p.hasPlacement       = true;
            p.assembledPosition  = pos;
            p.assembledRotation  = rot;
            p.assembledScale     = scl;
            p.isDirty            = true;

            // Mirror the entry into the backing PartPreviewPlacement so a
            // subsequent BuildPartList() rebuild (which re-reads stepPoses
            // from previewConfig) picks the entry up instead of discarding
            // the unsaved in-memory edit. Without this, NO TASK-captured
            // poses vanished on the next UI tick, making later steps fall
            // back to assembledPosition.
            var ppRef = FindPartPlacement(partId);
            if (ppRef != null)
            {
                var existing = ppRef.stepPoses != null ? new List<StepPoseEntry>(ppRef.stepPoses) : new List<StepPoseEntry>();
                bool replaced = false;
                for (int i = 0; i < existing.Count; i++)
                {
                    if (existing[i] != null && string.Equals(existing[i].stepId, step.id, StringComparison.Ordinal))
                    { existing[i] = target; replaced = true; break; }
                }
                if (!replaced) existing.Add(target);
                ppRef.stepPoses = existing.ToArray();
            }
        }

        /// <summary>
        /// Which role a newly-added part should land in. Confirm-family steps
        /// with no targets or tool-actions default to NoTask — parts added
        /// there are for introduction / demo / context and shouldn't pretend
        /// to be tasks. Every other step family keeps Required as the default.
        /// </summary>
        private PartRole ResolveDefaultPartRole(StepDefinition step)
        {
            if (step == null) return PartRole.Required;
            if (!step.IsConfirm) return PartRole.Required;
            bool hasTargets     = step.targetIds          != null && step.targetIds.Length          > 0;
            bool hasToolActions = step.requiredToolActions != null && step.requiredToolActions.Length > 0;
            return (!hasTargets && !hasToolActions) ? PartRole.NoTask : PartRole.Required;
        }

        private enum PartRole { Required, Optional, NoTask }
    }
}
