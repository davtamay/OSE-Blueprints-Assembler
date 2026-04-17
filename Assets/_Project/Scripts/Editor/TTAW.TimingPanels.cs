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

            // Collect cue indices per panel (trigger), scoped to this target.
            var cues = step.animationCues?.cues;
            var panels = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (cues != null)
            {
                for (int i = 0; i < cues.Length; i++)
                {
                    var c = cues[i];
                    if (c == null) continue;
                    if (!CueMatchesScope(c, scope, scopeKey)) continue;
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
                StartPanelPreview(step, cueIndices);
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

            var cues = step.animationCues?.cues;

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
                    _dirtyStepIds.Add(step.id);
                }
            }

            // Reorderable rows
            var rl = GetOrBuildPanelList(step, scope, scopeKey, trigger, cueIndices);
            rl.DoLayoutList();

            // Inline edit panels — render DrawCueEntry under the row for any
            // cue whose foldout is expanded. Reuses the same full-field editor
            // used by the legacy canvas section so authors get the complete
            // set of type-specific fields (shake amplitude/frequency/axis,
            // rotation Euler, pulse colors, pivot override, etc).
            if (cues != null)
            {
                for (int i = 0; i < cueIndices.Count; i++)
                {
                    int cueIdx = cueIndices[i];
                    if (cueIdx < 0 || cueIdx >= cues.Length) continue;
                    if (cueIdx >= _cueFoldouts.Count || !_cueFoldouts[cueIdx]) continue;

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        DrawCueEntry(step, cues, cueIdx,
                            out bool removeInline, out bool _, out bool _);
                        if (removeInline)
                        {
                            RemoveCueAtIndex(step, cueIdx);
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
                var cues = step.animationCues?.cues;
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
                    _dirtyStepIds.Add(step.id);
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
                        Debug.Log($"[TTAW] Play preview: step='{step?.id}' cueIdx={cueIdx} type='{cue.type}' targetSub='{cue.targetSubassemblyId}' targetParts={(cue.targetPartIds?.Length ?? 0)}");
                        StartCuePreview(step, cueIdx);
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
                    RemoveCueAtIndex(step, cueIdx);
                    GUI.color = Color.white;
                    return;
                }
                GUI.color = Color.white;
            };

            rl.onReorderCallback = _list =>
            {
                var cues = step.animationCues?.cues;
                if (cues == null) return;
                for (int i = 0; i < persistent.Count; i++)
                {
                    int cueIdx = persistent[i];
                    if (cueIdx >= 0 && cueIdx < cues.Length && cues[cueIdx] != null)
                        cues[cueIdx].panelOrder = i;
                }
                _dirtyStepIds.Add(step.id);
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
            var cues = step.animationCues?.cues;
            if (cues == null) return false;
            foreach (var c in cues)
            {
                if (c == null) continue;
                if (!CueMatchesScope(c, scope, scopeKey)) continue;
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
                // Relativize to Assets/ when possible.
                if (picked.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    clipPath = "Assets" + picked.Substring(Application.dataPath.Length).Replace('\\', '/');
                else
                    clipPath = picked.Replace('\\', '/');
            }

            var payload = step.animationCues ?? (step.animationCues =
                new StepAnimationCuePayload { cues = Array.Empty<AnimationCueEntry>() });

            // Compute next panelOrder within this panel.
            int nextOrder = 0;
            if (payload.cues != null)
            {
                foreach (var c in payload.cues)
                {
                    if (c == null) continue;
                    if (!CueMatchesScope(c, scope, scopeKey)) continue;
                    string t = string.IsNullOrEmpty(c.trigger) ? "onActivate" : c.trigger;
                    if (!string.Equals(t, trigger, StringComparison.Ordinal)) continue;
                    if (c.panelOrder >= nextOrder) nextOrder = c.panelOrder + 1;
                }
            }

            var cue = new AnimationCueEntry
            {
                type                   = type,
                trigger                = trigger,
                panelOrder             = nextOrder,
                animationClipAssetPath = clipPath,
            };
            ApplyScope(cue, scope, scopeKey);
            ApplyTypeDefaults(cue);

            var list = new List<AnimationCueEntry>(payload.cues ?? Array.Empty<AnimationCueEntry>());
            list.Add(cue);
            payload.cues = list.ToArray();

            while (_cueFoldouts.Count < payload.cues.Length) _cueFoldouts.Add(false);
            _cueFoldouts[payload.cues.Length - 1] = true;

            // Clear the placeholder for this panel now that a real cue exists.
            string k = EmptyPanelKey(step.id, scope, scopeKey);
            if (_emptyPanels.TryGetValue(k, out var set)) set.Remove(trigger);

            _cueContextOpenKeys.Add($"{ScopeKeyPrefix(step.id, scope, scopeKey)}/panel/{trigger}");
            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        private void RemoveCueAtIndex(StepDefinition step, int cueIdx)
        {
            var cues = step.animationCues?.cues;
            if (cues == null || cueIdx < 0 || cueIdx >= cues.Length) return;
            var list = new List<AnimationCueEntry>(cues);
            list.RemoveAt(cueIdx);
            step.animationCues.cues = list.ToArray();
            if (cueIdx < _cueFoldouts.Count) _cueFoldouts.RemoveAt(cueIdx);
            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        private void RemovePanel(StepDefinition step, CueScope scope, string scopeKey,
                                 string trigger, List<int> cueIndices)
        {
            var cues = step.animationCues?.cues;
            if (cues != null && cueIndices.Count > 0)
            {
                var toRemove = new HashSet<int>(cueIndices);
                var keep = new List<AnimationCueEntry>(cues.Length - cueIndices.Count);
                for (int i = 0; i < cues.Length; i++)
                    if (!toRemove.Contains(i)) keep.Add(cues[i]);
                step.animationCues.cues = keep.ToArray();
                // Rebuild _cueFoldouts sized to the new array.
                _cueFoldouts.Clear();
                for (int i = 0; i < step.animationCues.cues.Length; i++) _cueFoldouts.Add(false);
            }
            string k = EmptyPanelKey(step.id, scope, scopeKey);
            if (_emptyPanels.TryGetValue(k, out var set)) set.Remove(trigger);
            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool CueMatchesScope(AnimationCueEntry cue, CueScope scope, string scopeKey)
        {
            if (cue == null) return false;
            switch (scope)
            {
                case CueScope.Part:
                    if (cue.targetPartIds == null) return false;
                    foreach (var p in cue.targetPartIds)
                        if (string.Equals(p, scopeKey, StringComparison.Ordinal)) return true;
                    return false;
                case CueScope.Tool:
                    if (cue.targetToolIds == null) return false;
                    foreach (var t in cue.targetToolIds)
                        if (string.Equals(t, scopeKey, StringComparison.Ordinal)) return true;
                    return false;
                case CueScope.Subassembly:
                    return string.Equals(cue.targetSubassemblyId, scopeKey, StringComparison.Ordinal);
            }
            return false;
        }

        private static void ApplyScope(AnimationCueEntry cue, CueScope scope, string scopeKey)
        {
            switch (scope)
            {
                case CueScope.Part:         cue.targetPartIds       = new[] { scopeKey }; break;
                case CueScope.Tool:         cue.targetToolIds       = new[] { scopeKey }; break;
                case CueScope.Subassembly:  cue.targetSubassemblyId = scopeKey;           break;
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
