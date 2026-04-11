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

        private bool _subassemblyShowExplainer;
        private bool _subassemblyAllFoldout;
        private bool _subassemblyShowAdvanced;
        private readonly HashSet<string> _subassemblyOpenIds = new(StringComparer.Ordinal);

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────
        //
        // Plain-English rewrite of the Phase 6 MVP. Goals:
        //   • Lead with a one-line explainer that anyone can read
        //   • Use everyday labels: "Group of parts" not "partIds[]"
        //   • Hide the technical fields (isAggregate, member subassemblies,
        //     internal id) behind a single "Show advanced" foldout
        //   • Make it obvious what the *current step* is part of (or not)

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

            DrawUnifiedSectionHeader($"SUBASSEMBLY GROUPS ({count})", count);

            // ── Plain-English explainer (collapsed by default) ────────────────
            EditorGUILayout.BeginHorizontal();
            string helpLabel = _subassemblyShowExplainer
                ? "▼ What is a subassembly?"
                : "▶ What is a subassembly?";
            if (GUILayout.Button(helpLabel, EditorStyles.miniLabel, GUILayout.ExpandWidth(true)))
                _subassemblyShowExplainer = !_subassemblyShowExplainer;
            EditorGUILayout.EndHorizontal();
            if (_subassemblyShowExplainer)
            {
                EditorGUILayout.HelpBox(
                    "A subassembly is a group of parts you build into a single unit "
                    + "before adding it to the bigger machine. Example: a 'carriage' "
                    + "subassembly is bolted together first, then dropped onto the "
                    + "rails as one piece.\n\n"
                    + "Each subassembly is owned by exactly one assembly file. The "
                    + "steps that build the subassembly run together; once it's done, "
                    + "the runtime treats all its parts as one moving unit.",
                    MessageType.Info);
            }

            // ── This step's group ─────────────────────────────────────────────
            EditorGUILayout.LabelField("This step belongs to:", EditorStyles.miniLabel);
            SubassemblyDefinition active = null;
            if (!string.IsNullOrEmpty(activeId))
            {
                for (int i = 0; i < allSubs.Length; i++)
                {
                    if (allSubs[i] != null && string.Equals(allSubs[i].id, activeId, StringComparison.Ordinal))
                    { active = allSubs[i]; break; }
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (active != null)
            {
                int parts = active.partIds?.Length ?? 0;
                int steps = active.stepIds?.Length ?? 0;
                EditorGUILayout.LabelField($"  {active.GetDisplayName()}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"  {parts} part{(parts == 1 ? "" : "s")}   ·   {steps} build step{(steps == 1 ? "" : "s")}",
                    EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(active.description))
                    EditorGUILayout.LabelField("  " + active.description, EditorStyles.wordWrappedMiniLabel);
                if (active.isAggregate)
                    EditorGUILayout.LabelField(
                        "  Composite group  —  bundles other subassemblies together.",
                        EditorStyles.miniLabel);
                if (GUILayout.Button("Copy this group as a JSON template", EditorStyles.miniButton))
                    CopySubassemblyStubToClipboard(active);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "  Not part of any group.  This is a standalone step.",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("Create a JSON template for a new group",
                                     EditorStyles.miniButton))
                    CopyNewSubassemblyStubToClipboard(step);
            }
            EditorGUILayout.EndVertical();

            // ── Browse all groups in this package (collapsed by default) ─────
            if (count == 0) return;

            EditorGUILayout.Space(2);
            _subassemblyAllFoldout = EditorGUILayout.Foldout(
                _subassemblyAllFoldout,
                $"Browse all {count} group{(count == 1 ? "" : "s")} in this package",
                true);
            if (!_subassemblyAllFoldout) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < allSubs.Length; i++)
            {
                var sub = allSubs[i];
                if (sub == null) continue;

                int parts = sub.partIds?.Length ?? 0;
                int steps = sub.stepIds?.Length ?? 0;

                bool open = _subassemblyOpenIds.Contains(sub.id);
                bool newOpen = EditorGUILayout.Foldout(
                    open,
                    $"{sub.GetDisplayName()}  ·  {parts} part{(parts == 1 ? "" : "s")}",
                    true);
                if (newOpen != open)
                {
                    if (newOpen) _subassemblyOpenIds.Add(sub.id);
                    else         _subassemblyOpenIds.Remove(sub.id);
                }
                if (!newOpen) continue;

                EditorGUI.indentLevel++;
                if (!string.IsNullOrEmpty(sub.description))
                    EditorGUILayout.LabelField(sub.description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    $"{parts} part{(parts == 1 ? "" : "s")}   ·   {steps} build step{(steps == 1 ? "" : "s")}",
                    EditorStyles.miniLabel);

                if (sub.partIds != null && sub.partIds.Length > 0)
                {
                    EditorGUILayout.LabelField("Parts in this group:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (var pid in sub.partIds)
                        EditorGUILayout.LabelField("• " + pid, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                if (sub.stepIds != null && sub.stepIds.Length > 0)
                {
                    EditorGUILayout.LabelField("Build steps:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (var sid in sub.stepIds)
                        EditorGUILayout.LabelField("• " + sid, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                if (GUILayout.Button("Copy as JSON template", EditorStyles.miniButton, GUILayout.Width(160)))
                    CopySubassemblyStubToClipboard(sub);

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
            EditorGUI.indentLevel--;

            // ── Advanced foldout — only the technical fields live here ────────
            EditorGUILayout.Space(2);
            _subassemblyShowAdvanced = EditorGUILayout.Foldout(
                _subassemblyShowAdvanced,
                "Show advanced subassembly fields",
                true);
            if (_subassemblyShowAdvanced && active != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Internal id:  {active.id}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Owning assembly:  {active.assemblyId}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(active.milestoneMessage))
                    EditorGUILayout.LabelField($"Milestone message:  \"{active.milestoneMessage}\"",
                                               EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Composite (isAggregate): {active.isAggregate}",
                                           EditorStyles.miniLabel);
                if (active.memberSubassemblyIds != null && active.memberSubassemblyIds.Length > 0)
                {
                    EditorGUILayout.LabelField("Built from these sub-groups:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (var mid in active.memberSubassemblyIds)
                        EditorGUILayout.LabelField("• " + mid, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
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
