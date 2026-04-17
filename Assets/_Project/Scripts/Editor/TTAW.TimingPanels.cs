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

        // Author-facing panel catalog. Order defines menu order.
        private static readonly (string trigger, string label, bool advanced)[] _timingPanelDefs =
        {
            ("onActivate",         "First to Show",              false),
            ("afterDelay",         "Show During",                false),
            ("onStepComplete",     "Show once step completed",   false),
            ("onTaskComplete",     "Show once task completed",   false),
            ("onFirstInteraction", "On First Interaction",       true),
            ("afterPartsShown",    "After Parts Shown",          true),
        };

        // Built-in type ids + friendly menu labels. "animationClip" is a sentinel
        // for the asset-driven row (data lives in animationClipAssetPath).
        private static readonly (string type, string label)[] _timingCueTypes =
        {
            ("shake",                 "Shake"),
            ("orientSubassembly",     "Rotate (orientSubassembly)"),
            ("pulse",                 "Pulse"),
            ("demonstratePlacement",  "Demonstrate Placement"),
            ("poseTransition",        "Pose Transition"),
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
                    // Legacy step-level storage — ToolDefinition doesn't own
                    // animationCues yet. Runtime reads host-owned cues only,
                    // so tool cues authored here don't fire until the tool
                    // host migration lands (TODO).
                    var payload = step.animationCues ?? (step.animationCues =
                        new StepAnimationCuePayload { cues = Array.Empty<AnimationCueEntry>() });
                    var capturedStep = step;
                    return new HostCueStorage(
                        cues: payload.cues,
                        setter: arr => capturedStep.animationCues.cues = arr,
                        markDirty: () => _dirtyStepIds.Add(capturedStep.id),
                        isHostOwned: false);
                }
            }
            return default;
        }

        // Tool cues are filtered by targetToolIds (legacy). Host-owned cues
        // are already stored on the host — only filter by stepIds, which
        // defines which steps the cue applies to (empty = always-on).
        private static bool CueAppliesHere(AnimationCueEntry c, StepDefinition step,
            CueScope scope, string scopeKey)
        {
            if (c == null) return false;
            if (scope == CueScope.Tool)
            {
                if (c.targetToolIds == null) return false;
                foreach (var t in c.targetToolIds)
                    if (string.Equals(t, scopeKey, StringComparison.Ordinal)) return true;
                return false;
            }
            // Host-owned (Part/Subassembly): cue is stored on the host itself,
            // stepIds gates which steps it fires on.
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

            var addBtnRect = new Rect(headerRect.xMax - 150f, headerRect.y + 2f, 146f, headerRect.height - 4f);
            var addStyle   = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = CueContextAccent },
            };
            if (GUI.Button(addBtnRect, new GUIContent("+ Add timing panel ▾",
                "Create a panel that groups cues by when they fire."), addStyle))
            {
                ShowAddTimingPanelMenu(step, scope, scopeKey);
            }

            // Collect cue indices per panel (trigger), filtered to this
            // target. Host-owned storage: indices are into the host's
            // animationCues array (part/sub); runtime and editor read the
            // same source.
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

            // Also render any "empty" placeholder panels the author just created.
            if (_emptyPanels.TryGetValue(EmptyPanelKey(step.id, scope, scopeKey), out var emptyTriggers))
            {
                foreach (var t in emptyTriggers)
                    if (!panels.ContainsKey(t)) panels[t] = new List<int>();
            }

            if (panels.Count == 0)
            {
                var empty = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.55f, 0.58f) },
                    fontStyle = FontStyle.Italic,
                };
                EditorGUILayout.LabelField("    No timing panels yet. Use + Add timing panel.", empty);
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

        // ── Menus ─────────────────────────────────────────────────────────────

        private void ShowAddTimingPanelMenu(StepDefinition step, CueScope scope, string scopeKey)
        {
            var menu    = new GenericMenu();
            var emptyKey = EmptyPanelKey(step.id, scope, scopeKey);
            if (!_emptyPanels.TryGetValue(emptyKey, out var existingEmpty))
                existingEmpty = new HashSet<string>(StringComparer.Ordinal);

            foreach (var def in _timingPanelDefs)
            {
                bool present = PanelHasCuesOrPlaceholder(step, scope, scopeKey, def.trigger, existingEmpty);
                string label = def.advanced ? $"Advanced/{def.label}" : def.label;
                if (present)
                    menu.AddDisabledItem(new GUIContent($"{label}  (already present)"));
                else
                    menu.AddItem(new GUIContent(label), false,
                        () => AddEmptyPanel(step, scope, scopeKey, def.trigger));
            }
            menu.ShowAsContext();
        }

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
                        DrawCueEntry(step, cues, cueIdx,
                            out bool removeInline, out bool _, out bool _);
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
                        // Host-owned cues (Part/Subassembly) use the host-aware
                        // preview so target resolution matches runtime. Tool
                        // scope still reads from step storage via legacy path.
                        if (scope == CueScope.Tool)
                            StartCuePreview(step, cueIdx);
                        else
                            StartHostCuePreview(cue, cueIdx, step,
                                scope == CueScope.Part ? "part" : "subassembly",
                                scopeKey);
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

        private bool PanelHasCuesOrPlaceholder(StepDefinition step, CueScope scope, string scopeKey,
                                               string trigger, HashSet<string> emptySet)
        {
            if (emptySet != null && emptySet.Contains(trigger)) return true;
            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (storage.cues == null) return false;
            foreach (var c in storage.cues)
            {
                if (c == null) continue;
                if (!CueAppliesHere(c, step, scope, scopeKey)) continue;
                string t = string.IsNullOrEmpty(c.trigger) ? "onActivate" : c.trigger;
                if (string.Equals(t, trigger, StringComparison.Ordinal)) return true;
            }
            return false;
        }

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
            ApplyTypeDefaults(cue);

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
            Repaint();
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

        private static void ApplyTypeDefaults(AnimationCueEntry cue)
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
            }
        }

        private static string SummarizeCue(AnimationCueEntry cue)
        {
            string typ = string.IsNullOrEmpty(cue.type) ? "?" : cue.type;
            switch (cue.type)
            {
                case "shake":
                    return $"shake   ·  {cue.shakeAmplitude:0.###} m · {cue.shakeFrequency:0.#} Hz";
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
