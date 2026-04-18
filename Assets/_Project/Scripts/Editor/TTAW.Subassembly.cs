// TTAW.Subassembly.cs — Subassembly authoring section (MVP).
// ──────────────────────────────────────────────────────────────────────────────
// Phase 6 of the UX redesign. Surfaces the existing SubassemblyDefinition data
// (already in MachinePackageDefinition.subassemblies[]) as a first-class
// section in the canvas so authors can SEE what subassemblies the current step
// belongs to, which parts are members of which subassemblies, and how many
// subassemblies the package has overall.
//
// MVP scope (per the user's "we can build it up" sign-off):
//   • Per-step section showing the active subassembly + its part membership
//   • All-subassemblies foldout listing every subassembly in the package
//   • "Copy stub JSON to clipboard" button on each subassembly so the author
//     has a starting template for hand-editing assembly files
//   • "New subassembly stub" button — generates a fresh stub keyed to the
//     current step's assemblyId, copied straight to the clipboard
//
// NOT in this MVP:
//   • Direct JSON writes (subassemblies live in 17 split-layout files for
//     d3d_v18_10; serialising back is a Phase-7 job)
//   • Drag-drop part assignment, inline rename, member-subassembly editing
//   • Aggregate / member-subassembly hierarchy editing (still authored by hand)
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private bool _subassemblyAllFoldout;
        private bool _subassemblyShowAdvanced;
        private readonly HashSet<string> _subassemblyOpenIds = new(StringComparer.Ordinal);

        // Section accent — same blue as the "FROM THIS STEP'S GROUP" bucket in
        // TTAW.Visibility.cs so the eye learns "blue = subassembly".
        private static readonly Color SubAccent = new(0.20f, 0.62f, 0.95f);

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────
        //
        // Visual rewrite: cards instead of bullet lists, colours instead of
        // labels, no walls of help text. The author can read the structure
        // (this step is part of X, X has N parts, N steps) at a glance.

        private void DrawSubassemblySection(StepDefinition step)
        {
            if (_pkg == null) return;

            var allSubs = _pkg.GetSubassemblies();
            int count   = allSubs?.Length ?? 0;

            // Resolve the subassembly the current step belongs to. The data
            // model has two fields for historical reasons (subassemblyId is the
            // author-facing label; requiredSubassemblyId is the runtime gate)
            // — either one is enough to consider the step "scoped".
            string activeId = !string.IsNullOrWhiteSpace(step?.subassemblyId)
                ? step.subassemblyId
                : step?.requiredSubassemblyId;
            SubassemblyDefinition active = null;
            if (!string.IsNullOrEmpty(activeId))
            {
                for (int i = 0; i < allSubs.Length; i++)
                {
                    if (allSubs[i] != null && string.Equals(allSubs[i].id, activeId, StringComparison.Ordinal))
                    { active = allSubs[i]; break; }
                }
            }

            // ── Section header — title + tooltip + count pill ────────────────
            EditorGUILayout.BeginHorizontal();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            GUILayout.Label(new GUIContent("GROUPS",
                "A subassembly is a small kit of parts (e.g. a carriage) that "
                + "you build as one unit and drop into the larger machine. Each "
                + "step belongs to at most one group; the runtime treats every "
                + "part in the group as one moving piece once the group is built."),
                titleStyle);
            GUILayout.FlexibleSpace();
            DrawSubCountPill(SubAccent, count, "in package");
            EditorGUILayout.EndHorizontal();

            // ── "This step belongs to" card ──────────────────────────────────
            if (active != null)
                DrawActiveSubassemblyCard(active, step);
            else
                DrawStandaloneStepCard(step);

            // ── Browse foldout ────────────────────────────────────────────────
            if (count > 0)
            {
                EditorGUILayout.Space(3);
                _subassemblyAllFoldout = EditorGUILayout.Foldout(
                    _subassemblyAllFoldout,
                    $"Browse all {count} group{(count == 1 ? "" : "s")}",
                    true);
                if (_subassemblyAllFoldout)
                    DrawSubassemblyBrowseList(allSubs, activeId);
            }

            // ── Advanced foldout (only when there's an active group) ─────────
            if (active != null)
            {
                EditorGUILayout.Space(2);
                _subassemblyShowAdvanced = EditorGUILayout.Foldout(
                    _subassemblyShowAdvanced,
                    "Advanced fields",
                    true);
                if (_subassemblyShowAdvanced)
                    DrawSubassemblyAdvancedFields(active);
            }
        }

        // ── Active group card — visual hero, no walls of text ────────────────

        private void DrawActiveSubassemblyCard(SubassemblyDefinition sub, StepDefinition step)
        {
            int parts = sub.partIds?.Length ?? 0;
            int steps = sub.stepIds?.Length ?? 0;

            // Tinted header bar with accent edge
            var headerRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            var bgColor    = new Color(SubAccent.r * 0.20f + 0.06f,
                                       SubAccent.g * 0.20f + 0.06f,
                                       SubAccent.b * 0.20f + 0.06f, 1f);
            EditorGUI.DrawRect(headerRect, bgColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), SubAccent);

            var nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal    = { textColor = SubAccent },
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            var nameRect = new Rect(headerRect.x + 10f, headerRect.y, headerRect.width - 20f, headerRect.height);
            GUI.Label(nameRect, $"THIS STEP IS PART OF:  {sub.GetDisplayName()}", nameStyle);

            // Body row — counts + copy button
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            DrawSubCountPill(SubAccent, parts, parts == 1 ? "part" : "parts");
            DrawSubCountPill(SubAccent, steps, steps == 1 ? "build step" : "build steps");
            if (sub.isAggregate)
            {
                var compStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.7f, 0.7f, 0.75f) },
                    fontStyle = FontStyle.Italic,
                };
                GUILayout.Label("(composite — bundles other groups)", compStyle);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Copy JSON",
                        "Copy this group's definition as a JSON template you can paste into another assembly file."),
                    EditorStyles.miniButton, GUILayout.Width(80)))
                CopySubassemblyStubToClipboard(sub);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(sub.description))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                var descStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.7f, 0.72f, 0.78f) },
                    wordWrap = true,
                };
                EditorGUILayout.LabelField(sub.description, descStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Phase 7e — inline editor for name, parts, steps
            DrawSubassemblyInlineEditor(sub, step);

            EditorGUILayout.EndVertical();
        }

        private void DrawStandaloneStepCard(StepDefinition step)
        {
            var headerRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            var muted      = new Color(0.45f, 0.45f, 0.50f);
            var bgColor    = new Color(0.16f, 0.16f, 0.18f, 1f);
            EditorGUI.DrawRect(headerRect, bgColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), muted);

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal    = { textColor = muted },
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            GUI.Label(new Rect(headerRect.x + 10f, headerRect.y, headerRect.width - 20f, headerRect.height),
                "STANDALONE STEP — not in any group", labelStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            // Direct-create: writes a new subassembly to the assembly file now.
            if (GUILayout.Button(
                    new GUIContent("+ Create group here",
                        "Create a new subassembly in this step's assembly file, "
                        + "assigned to this step. You can add parts and build steps "
                        + "inline afterwards."),
                    EditorStyles.miniButton, GUILayout.Width(140)))
                CreateSubassemblyForStep(step);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Copy stub JSON",
                        "Copy a fresh subassembly stub to the clipboard for hand-editing."),
                    EditorStyles.miniButton, GUILayout.Width(100)))
                CopyNewSubassemblyStubToClipboard(step);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── Inline editor for a subassembly's fields (name, partIds, stepIds) ─

        private int _subAddPartIdx;
        private int _subAddStepIdx;

        private void DrawSubassemblyInlineEditor(SubassemblyDefinition sub, StepDefinition step)
        {
            if (sub == null || _pkg == null) return;

            EditorGUILayout.Space(4);

            // ── Name ──────────────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField("Name", sub.name ?? "");
            if (EditorGUI.EndChangeCheck() && newName != sub.name)
            {
                sub.name = newName;
                _dirtySubassemblyIds.Add(sub.id);
            }

            // ── Description ───────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            string newDesc = EditorGUILayout.TextField("Description", sub.description ?? "");
            if (EditorGUI.EndChangeCheck() && newDesc != sub.description)
            {
                sub.description = string.IsNullOrWhiteSpace(newDesc) ? null : newDesc;
                _dirtySubassemblyIds.Add(sub.id);
            }

            // ── Parts in this group — listed with × to remove ────────────────
            var partHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = SubAccent },
            };
            EditorGUILayout.LabelField($"PARTS  ({sub.partIds?.Length ?? 0})", partHeaderStyle);

            int removePartIdx = -1;
            if (sub.partIds != null)
            {
                for (int i = 0; i < sub.partIds.Length; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12);
                    // Clickable part name — click to select + ping in Hierarchy
                    var partLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.60f, 0.78f, 0.95f) },
                    };
                    if (GUILayout.Button(sub.partIds[i], partLabelStyle))
                    {
                        var liveGO = FindLivePartGO(sub.partIds[i]);
                        if (liveGO != null)
                        {
                            Selection.activeGameObject = liveGO;
                            EditorGUIUtility.PingObject(liveGO);
                            SceneView.RepaintAll();
                        }
                    }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20)))
                        removePartIdx = i;
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (removePartIdx >= 0)
            {
                var list = new List<string>(sub.partIds);
                list.RemoveAt(removePartIdx);
                sub.partIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                _dirtySubassemblyIds.Add(sub.id);
            }

            // + Add part picker + Add from Selection + drop zone
            {
                var currentSet = new HashSet<string>(sub.partIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                var candidates = new List<string>();
                foreach (var p in _pkg.GetParts())
                    if (p != null && !string.IsNullOrEmpty(p.id) && !currentSet.Contains(p.id))
                        candidates.Add(p.id);

                // Row 1: dropdown + Add button
                if (candidates.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12);
                    _subAddPartIdx = Mathf.Clamp(_subAddPartIdx, 0, candidates.Count - 1);
                    _subAddPartIdx = EditorGUILayout.Popup(_subAddPartIdx, candidates.ToArray(), GUILayout.Height(16));
                    var addStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = SubAccent },
                    };
                    if (GUILayout.Button("+ Add", addStyle, GUILayout.Width(50), GUILayout.Height(16)))
                    {
                        var list = new List<string>(sub.partIds ?? Array.Empty<string>()) { candidates[_subAddPartIdx] };
                        sub.partIds = list.ToArray();
                        _dirtySubassemblyIds.Add(sub.id);
                        _subAddPartIdx = 0;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // Row 2: "Add from Selection" button — grabs currently selected GOs in the Hierarchy
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                if (GUILayout.Button(
                        new GUIContent("+ Add from Selection",
                            "Add any GameObjects currently selected in the Hierarchy that match a package part ID."),
                        EditorStyles.miniButton, GUILayout.Height(16)))
                {
                    int added = 0;
                    foreach (var selGO in Selection.gameObjects)
                    {
                        if (selGO == null) continue;
                        string pid = selGO.name;
                        if (string.IsNullOrEmpty(pid)) continue;
                        // Verify it's a real package part
                        bool isPart = false;
                        foreach (var p in _pkg.GetParts())
                            if (p != null && string.Equals(p.id, pid, StringComparison.Ordinal))
                            { isPart = true; break; }
                        if (isPart && currentSet.Add(pid)) added++;
                    }
                    if (added > 0)
                    {
                        sub.partIds = currentSet.ToArray();
                        _dirtySubassemblyIds.Add(sub.id);
                        ShowNotification(new GUIContent($"Added {added} part(s) from selection"));
                    }
                    else
                    {
                        ShowNotification(new GUIContent("No new parts in selection"));
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Row 3: compact drop zone — drag parts from Hierarchy
                var dropRect = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));
                var ev = Event.current;
                bool isHover = dropRect.Contains(ev.mousePosition);
                bool isDrag = (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                              && DragAndDrop.objectReferences != null
                              && DragAndDrop.objectReferences.Length > 0;

                var accent = isHover && isDrag ? SubAccent : new Color(0.40f, 0.40f, 0.45f);
                EditorGUI.DrawRect(dropRect, isHover && isDrag
                    ? new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.12f)
                    : new Color(0f, 0f, 0f, 0.08f));
                EditorGUI.DrawRect(new Rect(dropRect.x + 12f, dropRect.y, dropRect.width - 12f, 1f), accent);
                EditorGUI.DrawRect(new Rect(dropRect.x + 12f, dropRect.yMax - 1f, dropRect.width - 12f, 1f), accent);
                var dropLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = accent },
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = isHover && isDrag ? FontStyle.Bold : FontStyle.Italic,
                };
                GUI.Label(dropRect, isHover && isDrag ? "Drop to add parts" : "Drag parts here", dropLabel);

                if (isHover && ev.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    ev.Use();
                }
                else if (isHover && ev.type == EventType.DragPerform && isDrag)
                {
                    DragAndDrop.AcceptDrag();
                    int added = 0;
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj == null) continue;
                        string pid = obj.name;
                        if (string.IsNullOrEmpty(pid)) continue;
                        bool isPart = false;
                        foreach (var p in _pkg.GetParts())
                            if (p != null && string.Equals(p.id, pid, StringComparison.Ordinal))
                            { isPart = true; break; }
                        if (isPart && currentSet.Add(pid)) added++;
                    }
                    if (added > 0)
                    {
                        sub.partIds = currentSet.ToArray();
                        _dirtySubassemblyIds.Add(sub.id);
                        ShowNotification(new GUIContent($"Added {added} part(s)"));
                    }
                    ev.Use();
                    Repaint();
                }
            }

            EditorGUILayout.Space(4);

            // ── Build steps — listed with × to remove ────────────────────────
            EditorGUILayout.LabelField($"BUILD STEPS  ({sub.stepIds?.Length ?? 0})", partHeaderStyle);

            int removeStepIdx = -1;
            if (sub.stepIds != null)
            {
                for (int i = 0; i < sub.stepIds.Length; i++)
                {
                    string sid = sub.stepIds[i];
                    var s = FindStep(sid);
                    string label = s != null ? $"[{s.sequenceIndex}] {s.GetDisplayName()}" : sid;
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12);
                    EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20)))
                        removeStepIdx = i;
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (removeStepIdx >= 0)
            {
                var list = new List<string>(sub.stepIds);
                list.RemoveAt(removeStepIdx);
                sub.stepIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                _dirtySubassemblyIds.Add(sub.id);
            }

            // + Add step picker
            {
                var currentSet = new HashSet<string>(sub.stepIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                var candidates = new List<(string id, string label)>();
                foreach (var s in _pkg.GetSteps())
                {
                    if (s == null || string.IsNullOrEmpty(s.id)) continue;
                    if (currentSet.Contains(s.id)) continue;
                    if (!string.IsNullOrEmpty(sub.assemblyId)
                        && !string.IsNullOrEmpty(s.assemblyId)
                        && !string.Equals(s.assemblyId, sub.assemblyId, StringComparison.Ordinal))
                        continue;
                    candidates.Add((s.id, $"[{s.sequenceIndex}] {s.GetDisplayName()}"));
                }

                if (candidates.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(12);
                    _subAddStepIdx = Mathf.Clamp(_subAddStepIdx, 0, candidates.Count - 1);
                    _subAddStepIdx = EditorGUILayout.Popup(_subAddStepIdx,
                        candidates.ConvertAll(c => c.label).ToArray(), GUILayout.Height(16));
                    var addStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = SubAccent },
                    };
                    if (GUILayout.Button("+ Add", addStyle, GUILayout.Width(50), GUILayout.Height(16)))
                    {
                        var list = new List<string>(sub.stepIds ?? Array.Empty<string>()) { candidates[_subAddStepIdx].id };
                        sub.stepIds = list.ToArray();
                        _dirtySubassemblyIds.Add(sub.id);
                        _subAddStepIdx = 0;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        // ── Create ────────────────────────────────────────────────────────────

        private void CreateSubassemblyForStep(StepDefinition step)
        {
            if (step == null || _pkg == null || string.IsNullOrEmpty(_pkgId)) return;

            string assemblyId = step.assemblyId ?? "";
            string subId = string.IsNullOrEmpty(assemblyId)
                ? $"sub_{step.id}"
                : $"{assemblyId}_sub_{step.id}";

            // Ensure unique
            int suffix = 1;
            string candidate = subId;
            while (_pkg.TryGetSubassembly(candidate, out _)) candidate = $"{subId}_{suffix++}";
            subId = candidate;

            var newSub = new SubassemblyDefinition
            {
                id         = subId,
                name       = "New Group",
                assemblyId = assemblyId,
                partIds    = step.requiredPartIds ?? Array.Empty<string>(),
                stepIds    = new[] { step.id },
            };

            // Find the target file
            string targetFile;
            if (PackageJsonUtils.IsSplitLayout(_pkgId) && !string.IsNullOrEmpty(assemblyId))
            {
                targetFile = System.IO.Path.Combine(
                    PackageJsonUtils.AuthoringRoot, _pkgId, "assemblies", $"{assemblyId}.json");
                if (!System.IO.File.Exists(targetFile))
                    targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }
            else
            {
                targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }

            if (string.IsNullOrEmpty(targetFile) || !System.IO.File.Exists(targetFile))
            {
                EditorUtility.DisplayDialog("Error", "Could not locate the target JSON file.", "OK");
                return;
            }

            try
            {
                PackageJsonUtils.InsertSubassembly(targetFile, newSub);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to insert subassembly:\n{ex.Message}", "OK");
                return;
            }

            // Reload and wire the step to the new subassembly
            LoadPkg(_pkgId);
            // Set the step's subassemblyId to the newly created one so the UI
            // shows the active card immediately.
            var reloadedStep = FindStep(step.id);
            if (reloadedStep != null && string.IsNullOrEmpty(reloadedStep.subassemblyId))
            {
                reloadedStep.subassemblyId = subId;
                _dirtyStepIds.Add(reloadedStep.id);
            }
            ShowNotification(new GUIContent($"Created subassembly '{subId}'"));
            Repaint();
        }

        // ── Browse list (compact rows, accent dot per group) ─────────────────

        private void DrawSubassemblyBrowseList(SubassemblyDefinition[] allSubs, string activeId)
        {
            for (int i = 0; i < allSubs.Length; i++)
            {
                var sub = allSubs[i];
                if (sub == null) continue;

                int parts = sub.partIds?.Length ?? 0;
                int steps = sub.stepIds?.Length ?? 0;

                bool isActive = !string.IsNullOrEmpty(activeId)
                                && string.Equals(sub.id, activeId, StringComparison.Ordinal);

                // Compact row — accent dot, name, counts, foldout chevron
                var rowRect = GUILayoutUtility.GetRect(0, 18f, GUILayout.ExpandWidth(true));
                if (isActive)
                    EditorGUI.DrawRect(rowRect, new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.10f));
                else if ((i & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                EditorGUI.DrawRect(new Rect(rowRect.x + 6f, rowRect.y + 7f, 5f, 5f), SubAccent);

                bool open = _subassemblyOpenIds.Contains(sub.id);
                var nameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal,
                    alignment = TextAnchor.MiddleLeft,
                };
                var nameRect = new Rect(rowRect.x + 18f, rowRect.y, rowRect.width - 110f, rowRect.height);
                if (GUI.Button(nameRect, sub.GetDisplayName(), nameStyle))
                {
                    if (open) _subassemblyOpenIds.Remove(sub.id);
                    else      _subassemblyOpenIds.Add(sub.id);
                }

                var countsStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.58f, 0.62f) },
                    alignment = TextAnchor.MiddleRight,
                };
                var countsRect = new Rect(rowRect.xMax - 110f, rowRect.y, 100f, rowRect.height);
                GUI.Label(countsRect, $"{parts}p · {steps}s", countsStyle);

                if (!open) continue;

                // Expanded body — indented, compact
                EditorGUI.indentLevel++;
                if (!string.IsNullOrEmpty(sub.description))
                {
                    var descStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.65f, 0.68f, 0.72f) },
                        wordWrap = true,
                    };
                    EditorGUILayout.LabelField(sub.description, descStyle);
                }
                if (sub.partIds != null && sub.partIds.Length > 0)
                {
                    var partsHeader = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = SubAccent },
                    };
                    EditorGUILayout.LabelField($"PARTS  ({parts})", partsHeader);
                    foreach (var pid in sub.partIds)
                        EditorGUILayout.LabelField("    " + pid, EditorStyles.miniLabel);
                }
                if (sub.stepIds != null && sub.stepIds.Length > 0)
                {
                    var stepsHeader = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = SubAccent },
                    };
                    EditorGUILayout.LabelField($"BUILD STEPS  ({steps})", stepsHeader);
                    foreach (var sid in sub.stepIds)
                        EditorGUILayout.LabelField("    " + sid, EditorStyles.miniLabel);
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy JSON", EditorStyles.miniButton, GUILayout.Width(80)))
                    CopySubassemblyStubToClipboard(sub);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
        }

        // ── Advanced fields (technical, only on demand) ───────────────────────

        private void DrawSubassemblyAdvancedFields(SubassemblyDefinition active)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"id:  {active.id}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"assemblyId:  {active.assemblyId}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(active.milestoneMessage))
                EditorGUILayout.LabelField($"milestoneMessage:  \"{active.milestoneMessage}\"",
                                           EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"isAggregate:  {active.isAggregate}",
                                       EditorStyles.miniLabel);
            if (active.memberSubassemblyIds != null && active.memberSubassemblyIds.Length > 0)
            {
                EditorGUILayout.LabelField("memberSubassemblyIds:", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                foreach (var mid in active.memberSubassemblyIds)
                    EditorGUILayout.LabelField("• " + mid, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        // ── Local count pill (matches DrawCountPill in TTAW.Visibility.cs) ────

        private static void DrawSubCountPill(Color color, int count, string label)
        {
            string text  = $"{count} {label}";
            var style    = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            var content  = new GUIContent(text);
            var size     = style.CalcSize(content);
            var rect     = GUILayoutUtility.GetRect(size.x + 12f, 16f,
                              GUILayout.Width(size.x + 12f), GUILayout.Height(16f));
            var bgColor  = count > 0 ? color : new Color(color.r, color.g, color.b, 0.30f);
            EditorGUI.DrawRect(rect, bgColor);
            GUI.Label(rect, content, style);
            GUILayout.Space(3);
        }

        // ── Stub generation (clipboard, no JSON writes in MVP) ────────────────

        private void CopySubassemblyStubToClipboard(SubassemblyDefinition src)
        {
            var stub = new SubassemblyDefinition
            {
                id         = src.id,
                name       = src.name,
                assemblyId = src.assemblyId,
                description = src.description,
                partIds    = src.partIds,
                stepIds    = src.stepIds,
                milestoneMessage = src.milestoneMessage,
                isAggregate = src.isAggregate,
                memberSubassemblyIds = src.memberSubassemblyIds,
            };
            string json = PackageJsonUtils.RoundFloatsInJson(JsonUtility.ToJson(stub, prettyPrint: true));
            EditorGUIUtility.systemCopyBuffer = json;
            ShowNotification(new GUIContent($"Copied stub JSON for {src.GetDisplayName()}"));
        }

        private void CopyNewSubassemblyStubToClipboard(StepDefinition step)
        {
            // Pre-fill the assemblyId from the current step if possible so the
            // author can drop the stub straight into the matching assembly file.
            string assemblyId = step?.assemblyId ?? string.Empty;

            // Generate a unique-ish id seed from the assemblyId so the stub is
            // immediately editable but unlikely to collide with existing ids.
            string idSeed = string.IsNullOrEmpty(assemblyId)
                ? "new_subassembly"
                : $"{assemblyId}_subassembly";

            var stub = new SubassemblyDefinition
            {
                id         = idSeed,
                name       = "New Subassembly",
                assemblyId = assemblyId,
                description = "",
                partIds    = Array.Empty<string>(),
                stepIds    = Array.Empty<string>(),
                milestoneMessage = "",
                isAggregate = false,
            };
            string json = JsonUtility.ToJson(stub, prettyPrint: true);

            // Wrap as a single-element JSON array snippet so the author can paste
            // it directly inside an existing "subassemblies": [ … ] block.
            var sb = new StringBuilder();
            sb.AppendLine("// Paste inside the assembly file's \"subassemblies\": [ … ] array.");
            sb.AppendLine("// Make sure the id is unique across the package, then assign");
            sb.AppendLine("// partIds[] and stepIds[] to the parts and steps that belong.");
            sb.Append(json);
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            ShowNotification(new GUIContent("Copied new subassembly stub to clipboard"));
        }

        // ── Create group from selection (right-click in task sequence) ──────

        /// <summary>
        /// Creates a new subassembly from a set of selected part IDs.
        /// Auto-populates: partIds from the selection, stepIds from every step
        /// that references any of these parts (via requiredPartIds). Writes to
        /// disk immediately, reloads, and wires the current step's subassemblyId.
        /// </summary>
        private void CreateGroupFromSelection(StepDefinition step, List<string> partIds)
        {
            if (step == null || _pkg == null || partIds == null || partIds.Count == 0) return;

            string assemblyId = step.assemblyId ?? "";

            // Generate a readable id from the first part's prefix
            string seed = partIds[0];
            int underscoreCount = 0;
            int cutAt = seed.Length;
            for (int i = 0; i < seed.Length; i++)
            {
                if (seed[i] == '_') underscoreCount++;
                if (underscoreCount >= 2) { cutAt = i; break; }
            }
            string prefix = seed.Substring(0, cutAt);
            string subId = $"subassembly_{prefix}_group";

            // Ensure unique
            int suffix = 1;
            string candidate = subId;
            while (_pkg.TryGetSubassembly(candidate, out _)) candidate = $"{subId}_{suffix++}";
            subId = candidate;

            // Auto-find steps that reference any of these parts
            var partSet = new HashSet<string>(partIds, StringComparer.Ordinal);
            var autoStepIds = new List<string>();
            foreach (var s in _pkg.GetSteps())
            {
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                // Filter to same assembly
                if (!string.IsNullOrEmpty(assemblyId) && !string.IsNullOrEmpty(s.assemblyId)
                    && !string.Equals(s.assemblyId, assemblyId, StringComparison.Ordinal))
                    continue;

                bool touches = false;
                if (s.requiredPartIds != null)
                    foreach (var pid in s.requiredPartIds)
                        if (partSet.Contains(pid)) { touches = true; break; }
                if (!touches && s.visualPartIds != null)
                    foreach (var pid in s.visualPartIds)
                        if (partSet.Contains(pid)) { touches = true; break; }
                if (touches)
                    autoStepIds.Add(s.id);
            }

            var newSub = new SubassemblyDefinition
            {
                id         = subId,
                name       = $"{prefix} group",
                assemblyId = assemblyId,
                partIds    = partIds.ToArray(),
                stepIds    = autoStepIds.ToArray(),
            };

            // Find target file
            string targetFile;
            if (PackageJsonUtils.IsSplitLayout(_pkgId) && !string.IsNullOrEmpty(assemblyId))
            {
                targetFile = System.IO.Path.Combine(
                    PackageJsonUtils.AuthoringRoot, _pkgId, "assemblies", $"{assemblyId}.json");
                if (!System.IO.File.Exists(targetFile))
                    targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }
            else
            {
                targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }

            if (string.IsNullOrEmpty(targetFile) || !System.IO.File.Exists(targetFile))
            {
                EditorUtility.DisplayDialog("Error", "Could not locate the target JSON file.", "OK");
                return;
            }

            try
            {
                PackageJsonUtils.InsertSubassembly(targetFile, newSub);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create group:\n{ex.Message}", "OK");
                return;
            }

            // Reload and wire the current step
            LoadPkg(_pkgId);
            var reloadedStep = FindStep(step.id);
            if (reloadedStep != null && string.IsNullOrEmpty(reloadedStep.subassemblyId))
            {
                reloadedStep.subassemblyId = subId;
                _dirtyStepIds.Add(reloadedStep.id);
            }

            // Also set subassemblyId on all auto-found steps
            foreach (var sid in autoStepIds)
            {
                var s = FindStep(sid);
                if (s != null && string.IsNullOrEmpty(s.subassemblyId))
                {
                    s.subassemblyId = subId;
                    _dirtyStepIds.Add(s.id);
                }
            }

            _canvasSelectedSubId = subId;
            ShowNotification(new GUIContent($"Created group '{newSub.name}' with {partIds.Count} parts, {autoStepIds.Count} steps"));
            Repaint();
        }

        /// <summary>
        /// Creates a new empty SCOPE (aggregate subassembly) so the author can
        /// immediately drag child groups into it. The aggregate flag is
        /// auto-derived once <c>memberSubassemblyIds</c> is populated, so this
        /// helper only needs to seed an empty <c>memberSubassemblyIds</c> array
        /// for the normalizer to promote it. The new scope is tied to the
        /// current step via <c>subassemblyId</c>.
        /// </summary>
        private void CreateEmptyScopeForStep(StepDefinition step)
        {
            if (step == null || _pkg == null) return;

            string assemblyId = step.assemblyId ?? "";
            string baseId     = string.IsNullOrEmpty(assemblyId) ? "scope" : $"{assemblyId}_scope";
            string subId      = $"subassembly_{baseId}";
            int suffix = 1;
            string candidate = subId;
            while (_pkg.TryGetSubassembly(candidate, out _)) candidate = $"{subId}_{suffix++}";
            subId = candidate;

            var newSub = new SubassemblyDefinition
            {
                id                   = subId,
                name                 = "New Scope",
                assemblyId           = assemblyId,
                partIds              = Array.Empty<string>(),
                stepIds              = new[] { step.id },
                memberSubassemblyIds = Array.Empty<string>(),
                isAggregate          = true,   // explicit — normalizer would re-derive anyway
            };

            string targetFile;
            if (PackageJsonUtils.IsSplitLayout(_pkgId) && !string.IsNullOrEmpty(assemblyId))
            {
                targetFile = System.IO.Path.Combine(
                    PackageJsonUtils.AuthoringRoot, _pkgId, "assemblies", $"{assemblyId}.json");
                if (!System.IO.File.Exists(targetFile))
                    targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }
            else
            {
                targetFile = PackageJsonUtils.GetJsonPath(_pkgId);
            }

            if (string.IsNullOrEmpty(targetFile) || !System.IO.File.Exists(targetFile))
            {
                EditorUtility.DisplayDialog("Error", "Could not locate the target JSON file.", "OK");
                return;
            }

            try
            {
                PackageJsonUtils.InsertSubassembly(targetFile, newSub);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create scope:\n{ex.Message}", "OK");
                return;
            }

            LoadPkg(_pkgId);
            var reloadedStep = FindStep(step.id);
            if (reloadedStep != null && string.IsNullOrEmpty(reloadedStep.subassemblyId))
            {
                reloadedStep.subassemblyId = subId;
                _dirtyStepIds.Add(reloadedStep.id);
            }

            _canvasSelectedSubId = subId;
            ShowNotification(new GUIContent($"Created empty scope '{newSub.name}' — drag group root GOs into its drop zone to populate."));
            Repaint();
        }

        /// <summary>
        /// Multi-selects task sequence rows whose part IDs are members of the
        /// given group. This visually highlights which tasks belong to the group
        /// the author just clicked.
        /// </summary>
        private void SelectGroupMembersInTaskSequence(SubassemblyDefinition sub, StepDefinition step)
        {
            _multiSelectedTaskSeqIdxs.Clear();
            if (sub?.partIds == null || step == null) return;

            var memberSet = new HashSet<string>(sub.partIds, StringComparer.Ordinal);
            // Also include stepIds as task matches (for non-part tasks)
            var stepSet = sub.stepIds != null
                ? new HashSet<string>(sub.stepIds, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            var order = GetOrDeriveTaskOrder(step);
            for (int i = 0; i < order.Count; i++)
            {
                var entry = order[i];
                if (entry.kind == "part" && memberSet.Contains(TaskInstanceId.ToPartId(entry.id)))
                    _multiSelectedTaskSeqIdxs.Add(i);
                // Also highlight tool actions / confirms whose step is in the group
                else if (stepSet.Contains(step.id))
                    _multiSelectedTaskSeqIdxs.Add(i);
            }
        }

        // ── Smart drop zone (one strip: add-to-group OR create-new-group) ────

        /// <summary>
        /// Single drop zone that adapts its behavior:
        ///   • Group selected → adds dropped parts to that group (blue accent)
        ///   • No group selected → creates a new group from the drop (green accent)
        /// </summary>
        private void DrawSmartGroupDropZone(StepDefinition step)
        {
            if (step == null || _pkg == null) return;

            bool hasSelectedGroup = !string.IsNullOrEmpty(_canvasSelectedSubId);
            SubassemblyDefinition selectedSub = null;
            if (hasSelectedGroup)
                _pkg.TryGetSubassembly(_canvasSelectedSubId, out selectedSub);

            EditorGUILayout.Space(2);
            var dropRect = GUILayoutUtility.GetRect(0, 24f, GUILayout.ExpandWidth(true));
            var ev       = Event.current;
            bool isHover = dropRect.Contains(ev.mousePosition);
            bool isDrag  = (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                           && DragAndDrop.objectReferences != null
                           && DragAndDrop.objectReferences.Length > 0;

            // Blue = add to existing group, Green = create new group
            var accentIdle   = hasSelectedGroup ? SubAccent : new Color(0.45f, 0.45f, 0.50f);
            var accentActive = hasSelectedGroup
                ? SubAccent
                : new Color(0.30f, 0.85f, 0.40f);

            var accent = isHover && isDrag ? accentActive : accentIdle;
            var bg = isHover && isDrag
                ? new Color(accent.r, accent.g, accent.b, 0.15f)
                : new Color(0f, 0f, 0f, 0.12f);
            EditorGUI.DrawRect(dropRect, bg);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.y, dropRect.width, 1f), accent);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.yMax - 1f, dropRect.width, 1f), accent);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isHover && isDrag ? FontStyle.Bold : FontStyle.Italic,
            };

            // Aggregates (isAggregate=true OR already has memberSubassemblyIds)
            // accept GROUPS — [G] suffix on labels so the author knows group
            // drops are legal here. Leaf subassemblies accept parts only.
            bool targetAcceptsGroups = selectedSub != null
                && (selectedSub.isAggregate
                    || (selectedSub.memberSubassemblyIds != null && selectedSub.memberSubassemblyIds.Length > 0));
            string acceptsSuffix = targetAcceptsGroups ? "  (parts or [G] groups)" : "";

            string idleLabel = hasSelectedGroup
                ? $"Drag parts here to add to \"{selectedSub?.GetDisplayName() ?? _canvasSelectedSubId}\"{acceptsSuffix}"
                : "Drag parts here to create a new group";
            string activeLabel = hasSelectedGroup
                ? $"Drop to add to \"{selectedSub?.GetDisplayName() ?? _canvasSelectedSubId}\"{acceptsSuffix}"
                : $"Drop to create a new group from {DragAndDrop.objectReferences?.Length ?? 0} item(s)";
            GUI.Label(dropRect, isHover && isDrag ? activeLabel : idleLabel, labelStyle);

            if (!isHover) return;

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = isDrag ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                ev.Use();
                return;
            }

            if (ev.type == EventType.DragPerform && isDrag)
            {
                DragAndDrop.AcceptDrag();

                var partIds = new List<string>();
                var groupIds = new List<string>();
                var allParts = _pkg.GetParts();
                var allSubs  = _pkg.GetSubassemblies();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;
                    string name = obj.name;
                    if (string.IsNullOrEmpty(name)) continue;

                    bool matched = false;
                    foreach (var p in allParts)
                    {
                        if (p != null && string.Equals(p.id, name, StringComparison.Ordinal))
                        { partIds.Add(p.id); matched = true; break; }
                    }
                    if (matched) continue;

                    // Subassembly match — either the raw id, or the scene root
                    // naming convention "Group_<DisplayName>" from
                    // EnsureAllSubassemblyRoots. Lets the author drag group
                    // root GOs from the Hierarchy straight into an aggregate.
                    string candidateDisplay = name.StartsWith("Group_", StringComparison.Ordinal)
                        ? name.Substring("Group_".Length)
                        : null;
                    if (allSubs != null)
                    {
                        foreach (var s in allSubs)
                        {
                            if (s == null) continue;
                            if (string.Equals(s.id, name, StringComparison.Ordinal)
                                || (candidateDisplay != null && string.Equals(s.GetDisplayName(), candidateDisplay, StringComparison.Ordinal)))
                            { groupIds.Add(s.id); break; }
                        }
                    }
                }

                if (partIds.Count == 0 && groupIds.Count == 0)
                {
                    ShowNotification(new GUIContent("No dropped items matched a package part or group"));
                }
                else if (hasSelectedGroup && selectedSub != null)
                {
                    int partsAdded  = 0;
                    int groupsAdded = 0;

                    if (partIds.Count > 0)
                    {
                        var currentSet = new HashSet<string>(selectedSub.partIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                        foreach (var pid in partIds)
                            if (currentSet.Add(pid)) partsAdded++;
                        if (partsAdded > 0)
                            selectedSub.partIds = currentSet.ToArray();
                    }

                    if (groupIds.Count > 0)
                    {
                        // Route group drops into memberSubassemblyIds. The
                        // aggregate flag is auto-derived by the normalizer
                        // from memberSubassemblyIds, so no manual flag set.
                        var currentSet = new HashSet<string>(selectedSub.memberSubassemblyIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                        foreach (var gid in groupIds)
                            if (!string.Equals(gid, selectedSub.id, StringComparison.Ordinal) && currentSet.Add(gid))
                                groupsAdded++;
                        if (groupsAdded > 0)
                            selectedSub.memberSubassemblyIds = currentSet.ToArray();
                    }

                    if (partsAdded + groupsAdded > 0)
                    {
                        _dirtySubassemblyIds.Add(selectedSub.id);
                        string gLabel = groupsAdded > 0 ? $"{groupsAdded} [G] group(s)" : "";
                        string pLabel = partsAdded  > 0 ? $"{partsAdded} part(s)"      : "";
                        string joined = string.Join(" + ", new[] { pLabel, gLabel }.Where(s => !string.IsNullOrEmpty(s)));
                        ShowNotification(new GUIContent($"Added {joined} to {selectedSub.GetDisplayName()}"));
                    }
                    else
                    {
                        ShowNotification(new GUIContent("All items already in group"));
                    }
                }
                else
                {
                    // Create new group from parts only (groups can't seed a new group)
                    if (partIds.Count > 0)
                        CreateGroupFromSelection(step, partIds);
                    else
                        ShowNotification(new GUIContent("Select an aggregate group first to add [G] groups"));
                }

                ev.Use();
                Repaint();
            }
        }

        // ── Legacy drop zones (kept for reference, no longer called) ──────────

        private void DrawGroupDropZone(string subId)
        {
            if (string.IsNullOrEmpty(subId) || _pkg == null) return;
            if (!_pkg.TryGetSubassembly(subId, out SubassemblyDefinition sub) || sub == null)
                return;

            var dropRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            var ev       = Event.current;
            bool isHover = dropRect.Contains(ev.mousePosition);
            bool isDrag  = (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                           && DragAndDrop.objectReferences != null
                           && DragAndDrop.objectReferences.Length > 0;

            var accent = isHover && isDrag ? SubAccent : new Color(0.45f, 0.45f, 0.50f);
            var bg     = isHover && isDrag
                ? new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.15f)
                : new Color(0f, 0f, 0f, 0.12f);
            EditorGUI.DrawRect(dropRect, bg);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.y, dropRect.width, 1f), accent);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.yMax - 1f, dropRect.width, 1f), accent);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isHover && isDrag ? FontStyle.Bold : FontStyle.Italic,
            };
            GUI.Label(dropRect, isHover && isDrag
                ? "Drop to add parts to this group"
                : "Drag parts here to add to group", labelStyle);

            if (!isHover) return;

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = isDrag ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                ev.Use();
                return;
            }

            if (ev.type == EventType.DragPerform && isDrag)
            {
                DragAndDrop.AcceptDrag();
                var currentSet = new HashSet<string>(sub.partIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                int added = 0;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;
                    string name = obj.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    // Check if name matches a package part
                    bool isPart = false;
                    foreach (var p in _pkg.GetParts())
                        if (p != null && string.Equals(p.id, name, StringComparison.Ordinal))
                        { isPart = true; break; }
                    if (isPart && currentSet.Add(name)) added++;
                }
                if (added > 0)
                {
                    sub.partIds = currentSet.ToArray();
                    _dirtySubassemblyIds.Add(sub.id);
                    ShowNotification(new GUIContent($"Added {added} part{(added == 1 ? "" : "s")} to group"));
                }
                else
                {
                    ShowNotification(new GUIContent("No new parts matched"));
                }
                ev.Use();
                Repaint();
            }
        }

        // ── Phase A4: canvas subassembly list ─────────────────────────────────

        // Which subassembly row is "selected" in the canvas list — clicking it
        // shows its properties in the inspector (orientation gizmo, membership).
        private string _canvasSelectedSubId;

        /// <summary>
        /// Compact subassembly list in the canvas. Shows subassemblies that are
        /// relevant to this step (the step's own subassembly + any aggregates
        /// that include it). Clicking a row selects it — the inspector shows
        /// its detail and the SceneView shows the gizmo.
        /// </summary>
        private void DrawCanvasSubassemblyList(StepDefinition step)
        {
            if (_pkg == null) return;

            var allSubs = _pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0) return;

            // Collect subassemblies relevant to this step. A step can reference
            // TWO groups simultaneously: its owning aggregate (step.subassemblyId,
            // e.g. "cube joining") and the leaf being placed in this step
            // (step.requiredSubassemblyId, e.g. "left frame side"). Both must
            // surface in GROUPS so the author can edit the leaf's pose while
            // seeing its parent aggregate context.
            string ownerSubId     = step?.subassemblyId;
            string requiredSubId  = step?.requiredSubassemblyId;

            var relevant = new List<SubassemblyDefinition>();
            for (int i = 0; i < allSubs.Length; i++)
            {
                var sub = allSubs[i];
                if (sub == null) continue;
                bool isOwner    = !string.IsNullOrEmpty(ownerSubId)
                                  && string.Equals(sub.id, ownerSubId, StringComparison.Ordinal);
                bool isRequired = !string.IsNullOrEmpty(requiredSubId)
                                  && string.Equals(sub.id, requiredSubId, StringComparison.Ordinal);
                bool containsStep = false;
                if (!isOwner && !isRequired && sub.stepIds != null && step != null)
                {
                    foreach (var sid in sub.stepIds)
                        if (string.Equals(sid, step.id, StringComparison.Ordinal))
                        { containsStep = true; break; }
                }
                if (isOwner || isRequired || containsStep)
                    relevant.Add(sub);
            }

            // Leaves first, aggregates (phase scopes) last — authorable things
            // should be the default click target.
            relevant.Sort((a, b) =>
            {
                int aScore = a.isAggregate ? 1 : 0;
                int bScore = b.isAggregate ? 1 : 0;
                return aScore.CompareTo(bScore);
            });

            // Header — always show, even when 0 groups (so the create-drop-zone is reachable)
            EditorGUILayout.BeginHorizontal();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            GUILayout.Label(new GUIContent($"GROUPS ({relevant.Count})",
                "Part groups that this step belongs to. Click to inspect. " +
                "Drag parts from the Hierarchy onto the drop zone to create/add."),
                titleStyle);
            GUILayout.FlexibleSpace();
            // [+] manual create button
            var addBtnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = SubAccent },
            };
            if (GUILayout.Button(new GUIContent("+", "Create an empty group for this step"),
                    addBtnStyle, GUILayout.Width(22), GUILayout.Height(16)))
                CreateGroupFromSelection(step, new List<string>(step.requiredPartIds ?? Array.Empty<string>()));

            // Create an empty SCOPE (aggregate) so the author can immediately
            // drag other groups into it. Without this, there's a chicken-and-egg
            // bootstrap: drop zones only accept groups once the target is
            // already an aggregate.
            if (GUILayout.Button(new GUIContent("+S", "Create a new SCOPE (aggregate — contains other groups). Drag group root GOs into its drop zone afterward."),
                    addBtnStyle, GUILayout.Width(28), GUILayout.Height(16)))
                CreateEmptyScopeForStep(step);

            EditorGUILayout.EndHorizontal();

            if (relevant.Count == 0)
            {
                DrawSmartGroupDropZone(step);
                return;
            }

            // Rows
            for (int i = 0; i < relevant.Count; i++)
            {
                var sub = relevant[i];
                int parts = sub.partIds?.Length ?? 0;
                int steps = sub.stepIds?.Length ?? 0;
                bool isSelected = string.Equals(_canvasSelectedSubId, sub.id, StringComparison.Ordinal);

                var rowRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));

                // Background — highlight selected
                if (isSelected)
                    EditorGUI.DrawRect(rowRect, new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.20f));
                else if ((i & 1) == 0)
                    EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.025f));

                // Accent dot
                EditorGUI.DrawRect(new Rect(rowRect.x + 6f, rowRect.y + 9f, 5f, 5f), SubAccent);

                // Name + counts
                var nameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                    normal    = { textColor = isSelected ? SubAccent : new Color(0.78f, 0.78f, 0.78f) },
                    alignment = TextAnchor.MiddleLeft,
                };
                var nameRect = new Rect(rowRect.x + 18f, rowRect.y, rowRect.width - 140f, rowRect.height);
                GUI.Label(nameRect, sub.GetDisplayName(), nameStyle);

                // Aggregate/phase flag badge — same row, just a tag.
                // Hovering explains: "a group whose members are other groups."
                if (sub.isAggregate)
                {
                    var badgeRect = new Rect(nameRect.xMax + 4f, rowRect.y + 4f, 40f, rowRect.height - 8f);
                    EditorGUI.DrawRect(badgeRect, new Color(SubAccent.r, SubAccent.g, SubAccent.b, 0.28f));
                    var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal    = { textColor = new Color(0.70f, 0.85f, 1f) },
                        fontSize  = 8,
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                    };
                    GUI.Label(badgeRect,
                        new GUIContent("SCOPE", "This group is an aggregate — its members are other groups. Select to move the whole scope (e.g. the entire frame cube) as one unit."),
                        badgeStyle);
                }

                var countStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = new Color(0.55f, 0.58f, 0.62f) },
                    alignment = TextAnchor.MiddleRight,
                };
                // Cue-count badges — inline at-a-glance affordance so authors
                // see which groups own animation/particle cues without having
                // to select each row.
                var badgeArea = new Rect(rowRect.xMax - 160f, rowRect.y, 78f, rowRect.height);
                DrawCueCountBadges(badgeArea, sub);

                var countRect = new Rect(rowRect.xMax - 80f, rowRect.y, 74f, rowRect.height);
                int groupChildren = sub.memberSubassemblyIds?.Length ?? 0;
                string countText = sub.isAggregate && groupChildren > 0
                    ? $"{groupChildren}g · {steps}s"
                    : $"{parts}p · {steps}s";
                GUI.Label(countRect, countText, countStyle);

                // Click to select/deselect
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && rowRect.Contains(Event.current.mousePosition))
                {
                    _canvasSelectedSubId = isSelected ? null : sub.id;
                    if (!isSelected)
                    {
                        // Clear task-sequence selection so the inspector
                        // switches to the subassembly view.
                        _selectedTaskSeqIdx = -1;
                        _multiSelectedTaskSeqIdxs.Clear();
                        _selectedPartIdx = -1;
                        _selectedIdx     = -1;
                        _multiSelectedParts.Clear();
                        _multiSelected.Clear();

                        // Select the group's root GO in the Hierarchy so the
                        // author can see all children at a glance.
                        // Ping the group root in Hierarchy but don't select it
                        // (HideFlags.DontSave causes Inspector NullReferenceException).
                        if (_subassemblyRootGOs.TryGetValue(sub.id, out var rootGO) && rootGO != null)
                            EditorGUIUtility.PingObject(rootGO);
                        Selection.activeGameObject = null;

                        // Highlight the group's member parts in the task sequence
                        // by multi-selecting their rows.
                        SelectGroupMembersInTaskSequence(sub, step);
                    }
                    else
                    {
                        // Deselecting — clear highlights
                        _multiSelectedTaskSeqIdxs.Clear();
                        Selection.activeGameObject = null;
                    }
                    Event.current.Use();
                    SceneView.RepaintAll();
                    Repaint();
                }
            }

            // Gizmo hint (when a group is selected)
            if (!string.IsNullOrEmpty(_canvasSelectedSubId))
            {
                var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = SubAccent },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter,
                };
                EditorGUILayout.LabelField("Rotate/move via the gizmo in SceneView", hintStyle);
            }

            // ── Single smart drop zone ────────────────────────────────────────
            // If a group is selected → dropping adds parts to that group.
            // If no group is selected → dropping creates a new group.
            // One strip, zero ambiguity.
            DrawSmartGroupDropZone(step);
        }

        /// <summary>
        /// Drop zone that creates a NEW group from dragged GameObjects.
        /// Resolves GO names to part IDs, auto-finds related steps, writes to disk.
        /// </summary>
        private void DrawCreateGroupDropZone(StepDefinition step)
        {
            if (step == null || _pkg == null) return;

            EditorGUILayout.Space(2);
            var dropRect = GUILayoutUtility.GetRect(0, 24f, GUILayout.ExpandWidth(true));
            var ev       = Event.current;
            bool isHover = dropRect.Contains(ev.mousePosition);
            bool isDrag  = (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform)
                           && DragAndDrop.objectReferences != null
                           && DragAndDrop.objectReferences.Length > 0;

            var accent = isHover && isDrag
                ? new Color(0.30f, 0.85f, 0.40f)  // green = create
                : new Color(0.45f, 0.45f, 0.50f);
            var bg = isHover && isDrag
                ? new Color(0.30f, 0.85f, 0.40f, 0.15f)
                : new Color(0f, 0f, 0f, 0.12f);
            EditorGUI.DrawRect(dropRect, bg);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.y, dropRect.width, 1f), accent);
            EditorGUI.DrawRect(new Rect(dropRect.x, dropRect.yMax - 1f, dropRect.width, 1f), accent);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = isHover && isDrag ? FontStyle.Bold : FontStyle.Italic,
            };
            GUI.Label(dropRect, isHover && isDrag
                ? $"Drop {DragAndDrop.objectReferences.Length} item(s) to create a new group"
                : "Drag parts here to create a new group", labelStyle);

            if (!isHover) return;

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = isDrag ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                ev.Use();
                return;
            }

            if (ev.type == EventType.DragPerform && isDrag)
            {
                DragAndDrop.AcceptDrag();

                // Resolve GO names to part IDs
                var partIds = new List<string>();
                var allParts = _pkg.GetParts();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null) continue;
                    string name = obj.name;
                    if (string.IsNullOrEmpty(name)) continue;
                    foreach (var p in allParts)
                    {
                        if (p != null && string.Equals(p.id, name, StringComparison.Ordinal))
                        { partIds.Add(p.id); break; }
                    }
                }

                if (partIds.Count > 0)
                    CreateGroupFromSelection(step, partIds);
                else
                    ShowNotification(new GUIContent("No dropped items matched a package part ID"));

                ev.Use();
                Repaint();
            }
        }
    }
}
