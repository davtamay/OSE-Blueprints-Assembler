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
                DrawActiveSubassemblyCard(active);
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

        private void DrawActiveSubassemblyCard(SubassemblyDefinition sub)
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
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("Copy new-group JSON",
                        "Copy a fresh subassembly stub keyed to this step's assembly so you can paste it into the assembly file."),
                    EditorStyles.miniButton, GUILayout.Width(150)))
                CopyNewSubassemblyStubToClipboard(step);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
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
    }
}
