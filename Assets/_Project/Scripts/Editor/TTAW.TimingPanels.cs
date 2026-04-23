// TTAW.TimingPanels.cs — Timing-panel cue authoring strip for parts, tools, groups.
// ──────────────────────────────────────────────────────────────────────────────
// One button at the top ("+ Add timing panel") creates a panel keyed by a
// trigger value ("onActivate", "afterDelay", "onStepComplete", etc.). Inside
// each panel, "+ Add cue" inserts AnimationCueEntry rows pre-scoped to the
// target (part / tool / subassembly). Rows are reorderable within a panel —
// panelOrder is baked on drop — and each row has a parallel (∥) / sequenced
// (⇣) toggle. All three scopes (part/tool/group) share this renderer so the
// UX is identical everywhere, and the structure is designed to be reused
// later for per-target particle-effect authoring (swap the scope enum and
// backing collection).
//
// Runtime wiring of "sequenceAfterPrevious" and the "First to Show" interactable
// gate is deliberately deferred — see the plan file's Non-goals.

using System;
using System.Collections.Generic;
using System.Linq;
using OSE.Content;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private enum CueScope { Part, Tool, Subassembly }

        // Author-facing panel catalog. Order defines strip + panel order.
        //
        // Each entry carries both a long label (used in panel headers + legacy
        // menu fallback) and a short label (used in the Slice-C timeline
        // strip — parallel event/timing forms so authors can scan all panel
        // types at a glance). The `advanced` flag is retained but no longer
        // hides entries behind a submenu; the strip shows all seven.
        private static readonly (string trigger, string label, string shortLabel, bool advanced)[] _timingPanelDefs =
        {
            ("onActivate",         "First to Show",              "Show Up",       false),
            ("afterDelay",         "Show During",                "Delayed",       false),
            ("onDuringAction",     "During Tool Action",         "Action",        false),
            ("onStepComplete",     "Show once step completed",   "Step Done",     false),
            ("onTaskComplete",     "Show once task completed",   "Task Done",     false),
            ("onFirstInteraction", "On First Interaction",       "First Tap",     true),
            ("afterPartsShown",    "After Parts Shown",          "Parts Shown",   true),
        };

        // Built-in type ids + friendly menu labels. "animationClip" is a sentinel
        // for the asset-driven row (data lives in animationClipAssetPath).
        //
        // NOTE: "orientSubassembly" is intentionally omitted from the add menu —
        // use "poseTransition" for any rotation/translation/scale. Existing
        // orientSubassembly cues in data still render and edit; the Type popup
        // in DrawCueEntry still lists it for forward-compat.
        private static readonly (string type, string label)[] _timingCueTypes =
        {
            ("shake",                 "Shake"),
            ("pulse",                 "Pulse"),
            ("demonstratePlacement",  "Demonstrate Placement"),
            ("poseTransition",        "Pose Transition"),
            ("particle",              "Particle Effect…"),
            // Phase 2 effect cues — tool-preview parity.
            ("emissionPulse",         "Emission Pulse"),
            ("colorTween",            "Color Tween"),
            ("materialFade",          "Material Fade (hot→cool)"),
            ("clickPop",              "Click Pop"),
            ("poseWobble",            "Pose Wobble"),
            ("toolVibration",         "Tool Vibration"),
            ("lineBetweenAnchors",    "Line Between Anchors"),
            ("drawSpline",            "Draw Spline Path"),
            ("measureLine",           "Measure Line"),
            ("screwSpin",             "Screw Spin"),
            ("animationClip",         "Animation Clip…"),
        };

        private readonly Dictionary<string, ReorderableList> _timingPanelLists =
            new Dictionary<string, ReorderableList>(StringComparer.Ordinal);

        // ── Host-storage abstraction ──────────────────────────────────────────
        //
        // Cues are authored in the task sequence UI (this file) but physically
        // live on the host that owns them — part.animationCues for part scope,
        // sub.animationCues for subassembly scope. Tool scope remains on
        // step.animationCues.cues for now (ToolDefinition doesn't have a
        // host-owned animationCues field yet). The storage struct lets every
        // read/write path stay scope-agnostic.
        private readonly struct HostCueStorage
        {
            public readonly AnimationCueEntry[] cues;
            public readonly Action<AnimationCueEntry[]> setter;
            public readonly Action markDirty;
            public readonly bool isHostOwned; // true for Part/Sub, false for Tool (legacy step)

            public HostCueStorage(AnimationCueEntry[] cues,
                Action<AnimationCueEntry[]> setter, Action markDirty, bool isHostOwned)
            {
                this.cues = cues ?? Array.Empty<AnimationCueEntry>();
                this.setter = setter;
                this.markDirty = markDirty;
                this.isHostOwned = isHostOwned;
            }

            public bool IsValid => setter != null;
        }

        private HostCueStorage GetHostCueStorage(StepDefinition step, CueScope scope, string scopeKey)
        {
            switch (scope)
            {
                case CueScope.Part:
                {
                    if (_pkg == null || !_pkg.TryGetPart(scopeKey, out var part) || part == null)
                        return default;
                    var captured = part;
                    return new HostCueStorage(
                        cues: captured.animationCues,
                        setter: arr => captured.animationCues = arr,
                        markDirty: () => _dirtyPartIds.Add(captured.id),
                        isHostOwned: true);
                }
                case CueScope.Subassembly:
                {
                    if (_pkg == null || !_pkg.TryGetSubassembly(scopeKey, out var sub) || sub == null)
                        return default;
                    var captured = sub;
                    return new HostCueStorage(
                        cues: captured.animationCues,
                        setter: arr => captured.animationCues = arr,
                        markDirty: () => _dirtySubassemblyIds.Add(captured.id),
                        isHostOwned: true);
                }
                case CueScope.Tool:
                {
                    if (_pkg == null || !_pkg.TryGetTool(scopeKey, out var tool) || tool == null)
                        return default;
                    var captured = tool;
                    return new HostCueStorage(
                        cues: captured.animationCues,
                        setter: arr => captured.animationCues = arr,
                        markDirty: () => _dirtyToolIds.Add(captured.id),
                        isHostOwned: true);
                }
            }
            return default;
        }

        // All three scopes (Part / Subassembly / Tool) are now host-owned —
        // the cue is stored on the host itself, and stepIds gates which
        // steps it fires on (empty = always-on wherever the host is live).
        private static bool CueAppliesHere(AnimationCueEntry c, StepDefinition step,
            CueScope scope, string scopeKey)
        {
            if (c == null) return false;
            if (c.stepIds == null || c.stepIds.Length == 0) return true;
            foreach (var sid in c.stepIds)
                if (string.Equals(sid, step.id, StringComparison.Ordinal)) return true;
            return false;
        }

        // ── Public entry ──────────────────────────────────────────────────────

        private void DrawTimingPanelsStrip(StepDefinition step, CueScope scope, string scopeKey, string title)
        {
            if (step == null || string.IsNullOrEmpty(scopeKey)) return;

            EditorGUILayout.Space(6);

            // Header bar
            var headerRect = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
            var bg = new Color(CueContextAccent.r * 0.20f + 0.06f,
                               CueContextAccent.g * 0.20f + 0.06f,
                               CueContextAccent.b * 0.20f + 0.06f, 1f);
            EditorGUI.DrawRect(headerRect, bg);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 3f, headerRect.height), CueContextAccent);

            var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal    = { textColor = CueContextAccent },
                alignment = TextAnchor.MiddleLeft,
            };
            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 160f, headerRect.height),
                      title, titleStyle);

            // Seed-from-preview button, only shown for Tool scope when the
            // step has a resolvable profile AND no cues currently apply to
            // this step. Explicit click only — no OnGUI-driven auto-seed,
            // no delayCall write-loop. Author saves via the normal Save
            // path when they're ready.
            string seedProfile = null;
            if (scope == CueScope.Tool)
                seedProfile = ResolveToolProfileForStep(step, scopeKey);
            bool showSeedBtn = !string.IsNullOrEmpty(seedProfile) && !StepHasApplicableCues(step, scope, scopeKey);

            // Seed-from-preview button (Tool scope only). Slice-C replaced
            // the adjacent "+ Add timing panel ▾" popup with the horizontal
            // timeline strip below, so this button now sits alone at the
            // header's right edge.
            if (showSeedBtn)
            {
                float seedBtnWidth = 118f;
                var seedRect = new Rect(headerRect.xMax - seedBtnWidth - 4f, headerRect.y + 2f,
                    seedBtnWidth, headerRect.height - 4f);
                var seedStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontStyle = FontStyle.Normal,
                    normal    = { textColor = CueContextAccent },
                };
                if (GUI.Button(seedRect,
                    new GUIContent($"Seed from {seedProfile} ▾",
                        $"Append the cue stack that reproduces the live {seedProfile} preview. Review, then Save to persist."),
                    seedStyle))
                {
                    PopulateFromToolPreview(step, scope, scopeKey, seedProfile);
                }
            }

            // Collect cue indices per panel (trigger), filtered to this
            // target. Host-owned storage: indices are into the host's
            // animationCues array (part/sub); runtime and editor read the
            // same source. Computed here so both the timeline strip AND the
            // panel list below share one source of truth.
            var storage = GetHostCueStorage(step, scope, scopeKey);
            var cues = storage.cues;
            var panels = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (cues != null)
            {
                for (int i = 0; i < cues.Length; i++)
                {
                    var c = cues[i];
                    if (c == null) continue;
                    if (!CueAppliesHere(c, step, scope, scopeKey)) continue;
                    string trig = string.IsNullOrEmpty(c.trigger) ? "onActivate" : c.trigger;
                    if (!panels.TryGetValue(trig, out var list))
                        panels[trig] = list = new List<int>();
                    list.Add(i);
                }
                foreach (var kv in panels)
                    kv.Value.Sort((a, b) => cues[a].panelOrder.CompareTo(cues[b].panelOrder));
            }

            // Also include "empty" placeholder panels the author just
            // created via a timeline click (or the legacy add menu).
            if (_emptyPanels.TryGetValue(EmptyPanelKey(step.id, scope, scopeKey), out var emptyTriggers))
            {
                foreach (var t in emptyTriggers)
                    if (!panels.ContainsKey(t)) panels[t] = new List<int>();
            }

            // Timeline strip (Slice C) — replaces "+ Add timing panel ▾" menu.
            // Horizontal row of cells, one per trigger in _timingPanelDefs.
            // Empty cell → click creates a panel. Populated cell → click
            // toggles the panel's foldout open/closed.
            DrawTimingPanelTimeline(step, scope, scopeKey, panels);

            if (panels.Count == 0)
            {
                var empty = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                    fontStyle = FontStyle.Italic,
                };
                EditorGUILayout.LabelField("    No timing panels yet. Click a cell above to create one.", empty);
                return;
            }

            // Draw in catalog order so advanced triggers come last.
            foreach (var def in _timingPanelDefs)
            {
                if (panels.TryGetValue(def.trigger, out var list))
                {
                    DrawTimingPanel(step, scope, scopeKey, def.trigger, def.label, list);
                    panels.Remove(def.trigger);
                }
            }
            // Any unknown trigger values (forward-compat) draw last.
            foreach (var kv in panels)
                DrawTimingPanel(step, scope, scopeKey, kv.Key, kv.Key, kv.Value);
        }

        // ── Timeline strip (Slice C) ─────────────────────────────────────────
        //
        // Horizontal row of timing-panel cells — one per trigger in
        // _timingPanelDefs. At-a-glance summary of which panels have cues
        // (count pill + accent tint) plus the "add panel" affordance (click
        // an empty cell). Replaces the legacy "+ Add timing panel ▾" popup
        // menu so authors can scan all seven panel types without opening a
        // submenu.

        private void DrawTimingPanelTimeline(StepDefinition step, CueScope scope, string scopeKey,
                                             Dictionary<string, List<int>> panels)
        {
            EditorGUILayout.Space(2);

            var row = GUILayoutUtility.GetRect(0, 24f, GUILayout.ExpandWidth(true));
            // Subtle bg behind the strip so it reads as one widget.
            EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.03f));

            const float cellSpacing = 3f;
            int cellCount = _timingPanelDefs.Length;
            float totalSpacing = cellSpacing * (cellCount - 1);
            float cellWidth = Mathf.Floor((row.width - 8f - totalSpacing) / cellCount);

            float x = row.x + 4f;
            float y = row.y + 2f;
            float h = row.height - 4f;

            for (int i = 0; i < cellCount; i++)
            {
                var def = _timingPanelDefs[i];
                bool hasPanel = panels != null && panels.ContainsKey(def.trigger);
                int cueCount = hasPanel ? panels[def.trigger].Count : 0;
                bool isOpen = IsTimingPanelOpen(step.id, scope, scopeKey, def.trigger);

                var cell = new Rect(x, y, cellWidth, h);

                // Cell background + border.
                Color cellBg, cellBorder, textColor;
                if (hasPanel)
                {
                    // Has cues OR is an empty placeholder — tinted with the
                    // cue accent, stronger when expanded.
                    float fillA = isOpen ? 0.55f : 0.28f;
                    cellBg     = new Color(CueContextAccent.r, CueContextAccent.g, CueContextAccent.b, fillA);
                    cellBorder = CueContextAccent;
                    textColor  = Color.white;
                }
                else
                {
                    cellBg     = new Color(1f, 1f, 1f, 0.04f);
                    cellBorder = new Color(0.55f, 0.55f, 0.58f, 0.55f);
                    textColor  = new Color(0.70f, 0.72f, 0.76f);
                }
                EditorGUI.DrawRect(cell, cellBg);
                DrawRectBorder(cell, cellBorder);

                // Label — short form, bold when active.
                var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = hasPanel ? FontStyle.Bold : FontStyle.Normal,
                    fontSize  = 10,
                    normal    = { textColor = textColor },
                };

                string cellText = cueCount > 0
                    ? $"{def.shortLabel}  ·  {cueCount}"
                    : def.shortLabel;
                GUI.Label(cell, cellText, labelStyle);

                // Clickable overlay.
                string tooltip = hasPanel
                    ? $"{def.label}  ({def.trigger})\n" +
                      (cueCount == 0
                          ? "Empty panel placeholder — click to toggle expand."
                          : $"{cueCount} cue(s). Click to expand/collapse this panel.")
                    : $"Add '{def.label}' panel ({def.trigger}). Click to create.";
                if (GUI.Button(cell, new GUIContent("", tooltip), GUIStyle.none))
                {
                    if (hasPanel)
                    {
                        // Toggle foldout state.
                        string openKey = $"{ScopeKeyPrefix(step.id, scope, scopeKey)}/panel/{def.trigger}";
                        if (isOpen) _cueContextOpenKeys.Remove(openKey);
                        else        _cueContextOpenKeys.Add(openKey);
                    }
                    else
                    {
                        AddEmptyPanel(step, scope, scopeKey, def.trigger);
                    }
                    Event.current.Use();
                }

                x += cellWidth + cellSpacing;
            }

            EditorGUILayout.Space(2);
        }

        /// <summary>True when the timing panel for this trigger is currently expanded.</summary>
        private bool IsTimingPanelOpen(string stepId, CueScope scope, string scopeKey, string trigger)
        {
            string key = $"{ScopeKeyPrefix(stepId, scope, scopeKey)}/panel/{trigger}";
            return _cueContextOpenKeys.Contains(key);
        }

        /// <summary>Draws a 1 px border inside the given rect. Shared by timeline cells.</summary>
        private static void DrawRectBorder(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x,           r.y,           r.width, 1f),     c);
            EditorGUI.DrawRect(new Rect(r.x,           r.yMax - 1f,   r.width, 1f),     c);
            EditorGUI.DrawRect(new Rect(r.x,           r.y,           1f,      r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1f,   r.y,           1f,      r.height), c);
        }

        // ── Menus ─────────────────────────────────────────────────────────────
        //
        // The "+ Add timing panel ▾" menu renderer was retired in Slice C when
        // the timeline strip (DrawTimingPanelTimeline) took over as the single
        // add-panel affordance. ShowAddCueMenuInPanel below is the remaining
        // menu — it adds individual cue rows inside an expanded panel.

        private void ShowAddCueMenuInPanel(StepDefinition step, CueScope scope, string scopeKey, string trigger)
        {
            var menu = new GenericMenu();
            foreach (var t in _timingCueTypes)
            {
                menu.AddItem(new GUIContent(t.label), false,
                    () => AddCueInPanel(step, scope, scopeKey, trigger, t.type));
            }
            menu.ShowAsContext();
        }

        // ── Panel body ────────────────────────────────────────────────────────

        private void DrawTimingPanel(StepDefinition step, CueScope scope, string scopeKey,
                                     string trigger, string label, List<int> cueIndices)
        {
            string scopeId  = ScopeKeyPrefix(step.id, scope, scopeKey);
            string openKey  = $"{scopeId}/panel/{trigger}";
            bool   isOpen   = _cueContextOpenKeys.Contains(openKey);

            // Header row
            var row = GUILayoutUtility.GetRect(0, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(row, new Color(1f, 1f, 1f, 0.04f));
            string chevron   = isOpen ? "▼" : "▶";
            string headerTxt = $"{chevron}  {label}  ({trigger})    {cueIndices.Count} cue{(cueIndices.Count == 1 ? "" : "s")}";
            var hStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal    = { textColor = new Color(0.95f, 0.80f, 0.50f) },
                alignment = TextAnchor.MiddleLeft,
            };
            if (GUI.Button(new Rect(row.x + 6f, row.y, row.width - 150f, row.height), headerTxt, hStyle))
            {
                if (isOpen) _cueContextOpenKeys.Remove(openKey);
                else        _cueContextOpenKeys.Add(openKey);
                isOpen = !isOpen;
            }

            // [▶▶] play panel — runs every cue back-to-back, honouring the
            // ∥ / ⇣ toggle on each row so authors can verify sequence vs
            // parallel timing without entering Play mode.
            var playPanelRect = new Rect(row.xMax - 50f, row.y + 1f, 24f, row.height - 2f);
            var playPanelColor = GUI.color;
            GUI.color = new Color(0.55f, 0.95f, 0.55f);
            if (GUI.Button(playPanelRect, new GUIContent("▶▶",
                    "Play every cue in this panel back-to-back, honouring the parallel/sequenced toggle on each row."),
                EditorStyles.miniButton))
            {
                StartPanelPreview(step, scope, scopeKey, cueIndices);
            }
            GUI.color = playPanelColor;

            // [×] delete panel
            var xRect = new Rect(row.xMax - 22f, row.y + 1f, 18f, row.height - 2f);
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (GUI.Button(xRect, new GUIContent("×", "Delete this panel and all its cues."), EditorStyles.miniButton))
            {
                if (cueIndices.Count == 0 ||
                    EditorUtility.DisplayDialog("Delete panel",
                        $"Remove the '{label}' panel and its {cueIndices.Count} cue(s)?", "Delete", "Cancel"))
                {
                    RemovePanel(step, scope, scopeKey, trigger, cueIndices);
                }
                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            if (!isOpen) return;

            var storage = GetHostCueStorage(step, scope, scopeKey);
            var cues = storage.cues;

            // "Show During" shared delaySeconds
            if (string.Equals(trigger, "afterDelay", StringComparison.Ordinal) && cues != null && cueIndices.Count > 0)
            {
                float cur = cues[cueIndices[0]].delaySeconds;
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.FloatField("    Delay (s, shared)", cur);
                if (EditorGUI.EndChangeCheck())
                {
                    next = Mathf.Max(0f, next);
                    foreach (var idx in cueIndices) cues[idx].delaySeconds = next;
                    storage.markDirty();
                }
            }

            // Reorderable rows
            var rl = GetOrBuildPanelList(step, scope, scopeKey, trigger, cueIndices);
            rl.DoLayoutList();

            // Inline edit panels — render DrawCueEntry under the row for any
            // cue whose foldout is expanded. Reuses the same full-field editor
            // so authors get the complete set of type-specific fields.
            if (cues != null)
            {
                for (int i = 0; i < cueIndices.Count; i++)
                {
                    int cueIdx = cueIndices[i];
                    if (cueIdx < 0 || cueIdx >= cues.Length) continue;
                    if (cueIdx >= _cueFoldouts.Count || !_cueFoldouts[cueIdx]) continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUI.BeginChangeCheck();
                        // Pass the cue's authoring scope so DrawAnimationPoseField
                        // can resolve the correct host (part/group/tool) without
                        // relying on ambient canvas selection.
                        string hostKindStr = scope switch
                        {
                            CueScope.Part        => "part",
                            CueScope.Subassembly => "sub",
                            CueScope.Tool        => "tool",
                            _                    => null,
                        };
                        DrawCueEntry(step, cues, cueIdx,
                            out bool removeInline, out bool _, out bool _,
                            hostKindOverride: hostKindStr, hostKeyOverride: scopeKey);
                        // DrawCueEntry mutates cues[cueIdx] in place and marks
                        // _dirtyStepIds internally. For host-owned cues we
                        // also need the host's dirty flag so WriteJson picks
                        // up the edit — the step write would never emit it.
                        if (EditorGUI.EndChangeCheck())
                            storage.markDirty();
                        if (removeInline)
                        {
                            RemoveCueAtIndex(step, scope, scopeKey, cueIdx);
                            Repaint();
                            return;
                        }
                    }
                }
            }

            // + Add cue
            if (GUILayout.Button(new GUIContent("    + Add cue ▾",
                "Add a new cue row to this panel."), EditorStyles.miniButton, GUILayout.Height(18f)))
            {
                ShowAddCueMenuInPanel(step, scope, scopeKey, trigger);
            }

            EditorGUILayout.Space(2);
        }

        // Persistent per-panel list that ReorderableList holds a stable
        // reference to. We refill it in-place each frame so cue additions /
        // deletions are reflected, without handing ReorderableList a new
        // list instance (which would destroy drag state mid-drag and make
        // rows impossible to reorder).
        private readonly Dictionary<string, List<int>> _panelIndexLists =
            new Dictionary<string, List<int>>(StringComparer.Ordinal);

        private ReorderableList GetOrBuildPanelList(StepDefinition step, CueScope scope, string scopeKey,
                                                    string trigger, List<int> cueIndices)
        {
            string key = $"{ScopeKeyPrefix(step.id, scope, scopeKey)}/list/{trigger}";

            // Keep the backing list instance stable: fill in place so Unity's
            // drag-state (internal index, active-drag flag) survives across
            // IMGUI repaint frames.
            if (!_panelIndexLists.TryGetValue(key, out var persistent))
                _panelIndexLists[key] = persistent = new List<int>(cueIndices.Count);
            persistent.Clear();
            persistent.AddRange(cueIndices);

            // Reuse the cached ReorderableList when possible so drag state is
            // not reset each frame. Only rebuild when no cache entry exists.
            if (_timingPanelLists.TryGetValue(key, out var cachedRL) && cachedRL != null)
                return cachedRL;

            var rl = new ReorderableList(persistent, typeof(int),
                draggable: true, displayHeader: false, displayAddButton: false, displayRemoveButton: false)
            {
                elementHeight = EditorGUIUtility.singleLineHeight + 2f,
            };

            rl.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                var drawStorage = GetHostCueStorage(step, scope, scopeKey);
                var cues = drawStorage.cues;
                if (cues == null || index < 0 || index >= persistent.Count) return;
                int cueIdx = persistent[index];
                if (cueIdx < 0 || cueIdx >= cues.Length) return;
                var cue = cues[cueIdx];
                if (cue == null) return;

                // Leave space on the left for Unity's ReorderableList drag
                // handle — otherwise our buttons intercept the drag click and
                // rows can't be reordered.
                const float DragHandleWidth = 18f;
                float x = rect.x + DragHandleWidth;
                float y = rect.y + 1f;
                float h = rect.height - 2f;

                // ∥ / ⇣ toggle
                var seqRect = new Rect(x, y, 22f, h);
                bool seq = cue.sequenceAfterPrevious;
                var seqStyle = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };
                if (GUI.Button(seqRect, new GUIContent(seq ? "⇣" : "∥",
                    seq ? "Sequenced — waits for the previous row to finish."
                        : "Parallel — starts together with the previous row."), seqStyle))
                {
                    cue.sequenceAfterPrevious = !seq;
                    drawStorage.markDirty();
                }
                x += 26f;

                // Summary label — leave room on the right for Play + Edit + Delete
                string summary = SummarizeCue(cue);
                var labelRect = new Rect(x, y, rect.width - (x - rect.x) - 120f, h);
                GUI.Label(labelRect, summary, EditorStyles.miniLabel);

                // Play / Stop
                bool isPreviewing = _previewPlayer != null
                    && _previewPlayer.IsPlaying
                    && _previewingCueIdx == cueIdx;
                var playRect = new Rect(rect.xMax - 120f, y, 28f, h);
                var playColor = GUI.color;
                GUI.color = isPreviewing ? new Color(1f, 0.55f, 0.55f) : new Color(0.55f, 0.95f, 0.55f);
                if (GUI.Button(playRect, new GUIContent(isPreviewing ? "■" : "▶",
                        isPreviewing ? "Stop preview." : "Play this cue in the scene preview."),
                    EditorStyles.miniButton))
                {
                    if (isPreviewing)
                    {
                        Debug.Log($"[TTAW] Stop preview (cue {cueIdx}).");
                        StopAllPreviews();
                    }
                    else
                    {
                        Debug.Log($"[TTAW] Play preview: step='{step?.id}' scope={scope} host='{scopeKey}' cueIdx={cueIdx} type='{cue.type}'");
                        // All three scopes are host-owned now — route them
                        // through the same host-aware preview so target
                        // resolution matches runtime.
                        string hostKindStr = scope switch
                        {
                            CueScope.Part        => "part",
                            CueScope.Tool        => "tool",
                            CueScope.Subassembly => "subassembly",
                            _                    => "part",
                        };
                        StartHostCuePreview(cue, cueIdx, step, hostKindStr, scopeKey);
                    }
                    Repaint();
                }
                GUI.color = playColor;

                // Edit — toggles inline expand directly beneath this panel
                while (_cueFoldouts.Count <= cueIdx) _cueFoldouts.Add(false);
                bool expanded = _cueFoldouts[cueIdx];
                var editRect = new Rect(rect.xMax - 90f, y, 56f, h);
                if (GUI.Button(editRect, new GUIContent(expanded ? "Edit ▴" : "Edit ▾",
                        "Show or hide this cue's property fields inline."),
                    EditorStyles.miniButton))
                {
                    _cueFoldouts[cueIdx] = !expanded;
                    Repaint();
                }

                // Delete
                var delRect = new Rect(rect.xMax - 30f, y, 26f, h);
                GUI.color = new Color(1f, 0.6f, 0.6f);
                if (GUI.Button(delRect, new GUIContent("×", "Remove this cue."), EditorStyles.miniButton))
                {
                    RemoveCueAtIndex(step, scope, scopeKey, cueIdx);
                    GUI.color = Color.white;
                    return;
                }
                GUI.color = Color.white;
            };

            rl.onReorderCallback = _list =>
            {
                var reorderStorage = GetHostCueStorage(step, scope, scopeKey);
                var cues = reorderStorage.cues;
                if (cues == null) return;
                for (int i = 0; i < persistent.Count; i++)
                {
                    int cueIdx = persistent[i];
                    if (cueIdx >= 0 && cueIdx < cues.Length && cues[cueIdx] != null)
                        cues[cueIdx].panelOrder = i;
                }
                reorderStorage.markDirty();
            };

            _timingPanelLists[key] = rl;
            return rl;
        }

        // ── Mutators ──────────────────────────────────────────────────────────

        // Placeholder tracking for empty panels (panels with no cues yet).
        private readonly Dictionary<string, HashSet<string>> _emptyPanels =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        private static string EmptyPanelKey(string stepId, CueScope scope, string scopeKey)
            => $"{stepId}/{scope}/{scopeKey}";

        private void AddEmptyPanel(StepDefinition step, CueScope scope, string scopeKey, string trigger)
        {
            string k = EmptyPanelKey(step.id, scope, scopeKey);
            if (!_emptyPanels.TryGetValue(k, out var set))
                _emptyPanels[k] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(trigger);
            _cueContextOpenKeys.Add($"{ScopeKeyPrefix(step.id, scope, scopeKey)}/panel/{trigger}");
            Repaint();
        }

        // PanelHasCuesOrPlaceholder: retired with ShowAddTimingPanelMenu in
        // Slice C. The timeline strip (DrawTimingPanelTimeline) reads the
        // pre-computed `panels` dictionary directly, which already includes
        // empty placeholders — no separate probe method needed.

        private void AddCueInPanel(StepDefinition step, CueScope scope, string scopeKey,
                                   string trigger, string type)
        {
            string clipPath = null;
            if (string.Equals(type, "animationClip", StringComparison.Ordinal))
            {
                string picked = EditorUtility.OpenFilePanel("Pick animation asset", Application.dataPath, "");
                if (string.IsNullOrEmpty(picked)) return;
                if (picked.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    clipPath = "Assets" + picked.Substring(Application.dataPath.Length).Replace('\\', '/');
                else
                    clipPath = picked.Replace('\\', '/');
            }

            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (!storage.IsValid)
            {
                Debug.LogWarning($"[TTAW] AddCueInPanel: no storage for {scope} '{scopeKey}'.");
                return;
            }

            // Compute next panelOrder within this trigger bucket.
            int nextOrder = 0;
            foreach (var c in storage.cues)
            {
                if (c == null) continue;
                if (!CueAppliesHere(c, step, scope, scopeKey)) continue;
                string t = string.IsNullOrEmpty(c.trigger) ? "onActivate" : c.trigger;
                if (!string.Equals(t, trigger, StringComparison.Ordinal)) continue;
                if (c.panelOrder >= nextOrder) nextOrder = c.panelOrder + 1;
            }

            var cue = new AnimationCueEntry
            {
                type                   = type,
                trigger                = trigger,
                panelOrder             = nextOrder,
                animationClipAssetPath = clipPath,
                // Scope new host-owned cues to the current step. Runtime checks
                // stepIds to decide whether the cue fires on the active step.
                stepIds                = storage.isHostOwned ? new[] { step.id } : null,
            };
            // Tool scope still writes target fields; host-owned cues don't need
            // them (the host is the target).
            if (!storage.isHostOwned)
                ApplyScope(cue, scope, scopeKey);
            ApplyTypeDefaults(cue, step, scope);

            var list = new List<AnimationCueEntry>(storage.cues);
            list.Add(cue);
            var updated = list.ToArray();
            storage.setter(updated);
            storage.markDirty();

            while (_cueFoldouts.Count < updated.Length) _cueFoldouts.Add(false);
            _cueFoldouts[updated.Length - 1] = true;

            string k = EmptyPanelKey(step.id, scope, scopeKey);
            if (_emptyPanels.TryGetValue(k, out var set)) set.Remove(trigger);

            _cueContextOpenKeys.Add($"{ScopeKeyPrefix(step.id, scope, scopeKey)}/panel/{trigger}");
            Repaint();
        }

        private void RemoveCueAtIndex(StepDefinition step, CueScope scope, string scopeKey, int cueIdx)
        {
            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (!storage.IsValid || cueIdx < 0 || cueIdx >= storage.cues.Length) return;
            var list = new List<AnimationCueEntry>(storage.cues);
            list.RemoveAt(cueIdx);
            storage.setter(list.ToArray());
            storage.markDirty();
            if (cueIdx < _cueFoldouts.Count) _cueFoldouts.RemoveAt(cueIdx);
            RefreshCueSynthAfterCueMutation();
            Repaint();
        }

        private void RemovePanel(StepDefinition step, CueScope scope, string scopeKey,
                                 string trigger, List<int> cueIndices)
        {
            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (storage.IsValid && cueIndices.Count > 0)
            {
                var toRemove = new HashSet<int>(cueIndices);
                var keep = new List<AnimationCueEntry>(storage.cues.Length - cueIndices.Count);
                for (int i = 0; i < storage.cues.Length; i++)
                    if (!toRemove.Contains(i)) keep.Add(storage.cues[i]);
                var updated = keep.ToArray();
                storage.setter(updated);
                storage.markDirty();
                _cueFoldouts.Clear();
                for (int i = 0; i < updated.Length; i++) _cueFoldouts.Add(false);
            }
            string k = EmptyPanelKey(step.id, scope, scopeKey);
            if (_emptyPanels.TryGetValue(k, out var set)) set.Remove(trigger);
            RefreshCueSynthAfterCueMutation();
            Repaint();
        }

        /// <summary>
        /// Invoked after any in-place cue-array mutation (add / remove /
        /// panel delete) to strip the synthesized stepPose entries tied
        /// to the pre-mutation cue graph and rebuild from the current
        /// cue set. Without this, future steps keep rendering the old
        /// holdAtEnd pose — e.g. deleting a poseTransition cue on
        /// step N leaves the synth on step N+1, so step N+1 still
        /// shows the old rotation until the next package reload.
        ///
        /// <para>Calls <see cref="MachinePackageNormalizer.RebakeCueSynthesisAndPoseTable"/>
        /// which is idempotent (strip-then-bake). Light enough to run
        /// on every cue mutation; heavy enough that we don't run it
        /// from hot paths.</para>
        /// </summary>
        private void RefreshCueSynthAfterCueMutation()
        {
            if (_pkg == null) return;
            OSE.Content.Loading.MachinePackageNormalizer.RebakeCueSynthesisAndPoseTable(_pkg);
            // Sync live scene to the refreshed pose table so the author
            // sees the effect immediately — otherwise the old pose
            // lingers on-screen until the next step navigation.
            SyncAllPartMeshesToActivePose();
            SyncAllGroupRootsToActivePose();
            SceneView.RepaintAll();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // ApplyScope stamps the legacy target fields on a cue. Host-owned
        // cues (Part / Subassembly) don't need this — the host itself is the
        // target. Kept for Tool scope, which still writes into
        // step.animationCues.cues until ToolDefinition gains its own
        // animationCues field.
        private static void ApplyScope(AnimationCueEntry cue, CueScope scope, string scopeKey)
        {
            switch (scope)
            {
                case CueScope.Tool:         cue.targetToolIds       = new[] { scopeKey }; break;
                // Part / Subassembly: no target fields needed — host storage
                // implies the target. Left intentionally empty.
            }
        }

        private static void ApplyTypeDefaults(AnimationCueEntry cue, StepDefinition step, CueScope scope)
        {
            switch (cue.type)
            {
                case "shake":
                    cue.shakeAmplitude = 0.01f;
                    cue.shakeFrequency = 8f;
                    cue.shakeAxis      = new SceneFloat3 { x = 1f, y = 0f, z = 0f };
                    break;
                case "orientSubassembly":
                    cue.subassemblyRotation = new SceneFloat3 { x = 0f, y = 90f, z = 0f };
                    break;
                case "pulse":
                    cue.pulseColorA = new SceneFloat4 { r = 0.1f, g = 0.3f, b = 1f, a = 1f };
                    cue.pulseColorB = new SceneFloat4 { r = 1f,   g = 0.85f, b = 0f, a = 1f };
                    cue.pulseSpeed  = 3f;
                    break;
                case "demonstratePlacement":
                case "poseTransition":
                    cue.spinAxis = new SceneFloat3 { x = 0f, y = 1f, z = 0f };
                    break;
                case "particle":
                    cue.particleSourceMode = "preset";
                    cue.particleScale      = 1f;
                    cue.particlePresetId   = DefaultParticlePresetFor(step, scope);
                    break;

                // ── Phase 2: effect-cue defaults ─────────────────────────
                //
                // These seed field values that match the existing C# tool
                // previews so an author adding, say, a poseWobble to a Weld
                // tool sees the same amplitude/frequency WeldPreview uses.
                // Trigger defaults to onDuringAction for tool-scope effect
                // cues — the common case.

                case "emissionPulse":
                    if (scope == CueScope.Tool) cue.trigger = "onDuringAction";
                    SeedEmissionDefaults(cue, step, scope);
                    break;

                case "colorTween":
                    cue.fromColor = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                    cue.toColor   = new SceneFloat4 { r = 0.55f, g = 0.55f, b = 0.52f, a = 1f };
                    break;

                case "materialFade":
                    if (scope == CueScope.Tool) cue.trigger = "onStepComplete";
                    cue.fromColor    = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                    cue.toColor      = new SceneFloat4 { r = 0.55f, g = 0.55f, b = 0.52f, a = 1f };
                    cue.fromEmission = new SceneFloat4 { r = 1.00f, g = 0.90f, b = 0.70f, a = 1f };
                    cue.toEmission   = new SceneFloat4 { r = 0f,    g = 0f,    b = 0f,    a = 1f };
                    cue.fromIntensity = 1.5f;
                    cue.toIntensity   = 0f;
                    cue.durationSeconds = 2f;
                    break;

                case "clickPop":
                    cue.fromColor  = new SceneFloat4 { r = 0.2f, g = 1f, b = 0.4f, a = 0.9f };
                    cue.pulseScale = 1.8f;
                    break;

                case "poseWobble":
                    if (scope == CueScope.Tool) cue.trigger = "onDuringAction";
                    cue.wobbleAxis      = new SceneFloat3 { x = 1f, y = 1f, z = 0f };
                    cue.wobbleAmplitude = 0.12f;
                    cue.wobbleFrequency = 40f;
                    // Weld wobbles most of the way through the action.
                    cue.startProgress = 0.05f;
                    cue.endProgress   = 0.95f;
                    break;

                case "toolVibration":
                    if (scope == CueScope.Tool) cue.trigger = "onDuringAction";
                    cue.vibrationAxes      = new SceneFloat3 { x = 0.0004f, y = 0.00028f, z = 0.0002f };
                    cue.vibrationFrequency = 55f;
                    cue.vibrationRampIn    = 0.15f;
                    cue.vibrationRampOut   = 0.85f;
                    break;

                case "lineBetweenAnchors":
                    if (scope == CueScope.Tool) cue.trigger = "onDuringAction";
                    cue.anchorARef           = "weldStart";
                    cue.anchorBRef           = "weldEnd";
                    cue.lineWidth            = 0.004f;
                    cue.fromColor            = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                    cue.fromEmission         = new SceneFloat4 { r = 1f,    g = 0.9f,  b = 0.7f,  a = 1f };
                    cue.lineEmissionIntensity = 1.5f;
                    cue.startProgress        = 0.2f;
                    cue.endProgress          = 0.9f;
                    break;

                case "drawSpline":
                    cue.splineAnchorRefs   = new[] { "weldStart", "weldEnd" };
                    cue.splineRadius       = 0.003f;
                    cue.splineMetallic     = 0.8f;
                    cue.splineSmoothness   = 0.6f;
                    cue.fromColor          = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                    break;

                case "measureLine":
                    cue.anchorARef    = "measureAnchorA";
                    cue.anchorBRef    = "measureAnchorB";
                    cue.measureUnit   = "mm";
                    cue.fromColor     = new SceneFloat4 { r = 1f, g = 0.8f, b = 0.2f, a = 1f };
                    break;

                case "screwSpin":
                    if (scope == CueScope.Tool) cue.trigger = "onDuringAction";
                    cue.spinAngleDegrees = 120f;
                    cue.spinAxis         = new SceneFloat3 { x = 0f, y = 1f, z = 0f };
                    break;
            }
        }

        // Seeds emissionPulse colour defaults that match the tool's
        // inline-preview glow (weld cyan-white, drill/cut warm, square
        // green, default neutral warm).
        private static void SeedEmissionDefaults(AnimationCueEntry cue, StepDefinition step, CueScope scope)
        {
            cue.fromEmission  = new SceneFloat4 { r = 0f, g = 0f, b = 0f, a = 1f };
            cue.toEmission    = new SceneFloat4 { r = 0.9f, g = 0.95f, b = 1f, a = 1f };
            cue.fromIntensity = 0f;
            cue.toIntensity   = 1.5f;

            if (scope != CueScope.Tool || step == null || string.IsNullOrEmpty(step.profile)) return;

            string p = step.profile;
            if (string.Equals(p, "Drill", StringComparison.OrdinalIgnoreCase))
            {
                cue.toEmission    = new SceneFloat4 { r = 0.3f, g = 0.7f, b = 1f, a = 1f };
                cue.toIntensity   = 0.5f;
            }
            else if (string.Equals(p, "Cut", StringComparison.OrdinalIgnoreCase))
            {
                cue.toEmission    = new SceneFloat4 { r = 1f, g = 0.5f, b = 0.1f, a = 1f };
                cue.toIntensity   = 1f;
            }
            else if (string.Equals(p, "Square", StringComparison.OrdinalIgnoreCase))
            {
                cue.toEmission    = new SceneFloat4 { r = 0.1f, g = 0.9f, b = 0.2f, a = 1f };
                cue.toIntensity   = 2f;
            }
        }

        // Maps a step's tool profile to its signature particle preset so
        // authors adding a particle cue on a Tool host get the aesthetic
        // that already matches the tool's inline preview (weld_arc for a
        // weld step, torque_sparks for drill / torque / cut, square_confirm
        // for square-check). Falls back to torque_sparks so non-tool hosts
        // still pick up a sensible default.
        private static string DefaultParticlePresetFor(StepDefinition step, CueScope scope)
        {
            if (scope != CueScope.Tool || step == null || string.IsNullOrEmpty(step.profile))
                return "torque_sparks";

            string p = step.profile;
            if (string.Equals(p, "Weld",   StringComparison.OrdinalIgnoreCase)) return "weld_arc";
            if (string.Equals(p, "Glue",   StringComparison.OrdinalIgnoreCase)) return "weld_arc";
            if (string.Equals(p, "Square", StringComparison.OrdinalIgnoreCase)) return "square_confirm";
            if (string.Equals(p, "Drill",  StringComparison.OrdinalIgnoreCase)) return "torque_sparks";
            if (string.Equals(p, "Torque", StringComparison.OrdinalIgnoreCase)) return "torque_sparks";
            if (string.Equals(p, "Cut",    StringComparison.OrdinalIgnoreCase)) return "torque_sparks";
            return "torque_sparks";
        }

        private static string SummarizeCue(AnimationCueEntry cue)
        {
            string typ = string.IsNullOrEmpty(cue.type) ? "?" : cue.type;
            switch (cue.type)
            {
                case "shake":
                {
                    string mode = string.IsNullOrEmpty(cue.shakeMode) ? "sine" : cue.shakeMode;
                    if (string.Equals(mode, "slide", StringComparison.Ordinal))
                        return $"shake · slide · {cue.shakeAmplitude:0.###} m";
                    return $"shake · {mode} · {cue.shakeAmplitude:0.###} m · {cue.shakeFrequency:0.#} Hz";
                }
                case "orientSubassembly":
                    return $"rotate   ·  ({cue.subassemblyRotation.x:0}°, {cue.subassemblyRotation.y:0}°, {cue.subassemblyRotation.z:0}°)";
                case "pulse":
                    return $"pulse   ·  {(cue.pulseSpeed > 0 ? cue.pulseSpeed : 3f):0.#} rad/s";
                case "demonstratePlacement":
                    return $"demonstrate   ·  {cue.spinRevolutions:0.#} rev";
                case "poseTransition":
                    return $"poseTransition";
                case "animationClip":
                    string p = cue.animationClipAssetPath ?? "(no asset)";
                    int slash = p.LastIndexOf('/');
                    if (slash >= 0) p = p.Substring(slash + 1);
                    return $"clip   ·  {p}";
                case "particle":
                {
                    string mode = string.IsNullOrEmpty(cue.particleSourceMode) ? "?" : cue.particleSourceMode;
                    string label;
                    if (mode == "preset")
                        label = string.IsNullOrEmpty(cue.particlePresetId) ? "(no preset)" : cue.particlePresetId;
                    else
                    {
                        label = cue.particlePrefabRef ?? "(no prefab)";
                        int sl = label.LastIndexOf('/');
                        if (sl >= 0) label = label.Substring(sl + 1);
                    }
                    return $"particle   ·  {mode}  ·  {label}";
                }
                case "emissionPulse":
                    return $"emissionPulse   ·  {(cue.toIntensity > 0f ? cue.toIntensity : 1f):0.#}×";
                case "colorTween":
                    return $"colorTween";
                case "materialFade":
                    return $"materialFade (hot→cool)";
                case "clickPop":
                    return $"clickPop   ·  ×{(cue.pulseScale > 0f ? cue.pulseScale : 1.8f):0.#}";
                case "poseWobble":
                    return $"poseWobble   ·  {(cue.wobbleAmplitude > 0f ? cue.wobbleAmplitude : 0.12f):0.###} rad @ {(cue.wobbleFrequency > 0f ? cue.wobbleFrequency : 40f):0.#} rad/s";
                case "toolVibration":
                    return $"toolVibration   ·  {(cue.vibrationFrequency > 0f ? cue.vibrationFrequency : 55f):0.#} Hz";
                case "lineBetweenAnchors":
                    return $"line   ·  {cue.anchorARef ?? "?"} → {cue.anchorBRef ?? "?"}";
                case "drawSpline":
                {
                    int n = cue.splineAnchorRefs != null ? cue.splineAnchorRefs.Length : 0;
                    return $"drawSpline   ·  {n} knot(s)";
                }
                case "measureLine":
                    return $"measureLine   ·  {cue.anchorARef ?? "?"} → {cue.anchorBRef ?? "?"}  ·  {cue.measureUnit ?? "mm"}";
                case "screwSpin":
                    return $"screwSpin   ·  {(cue.spinAngleDegrees != 0f ? cue.spinAngleDegrees : 120f):0.#}°";
                default:
                    return typ;
            }
        }

        private static string ScopeKeyPrefix(string stepId, CueScope scope, string scopeKey)
        {
            string s = scope == CueScope.Part ? "part" : scope == CueScope.Tool ? "tool" : "sub";
            return $"{stepId}/{s}/{scopeKey}";
        }
    }
}
