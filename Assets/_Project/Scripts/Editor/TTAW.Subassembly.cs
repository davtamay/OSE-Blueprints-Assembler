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
        private readonly HashSet<string> _subassemblyOpenIds = new(StringComparer.Ordinal);

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────

        private void DrawSubassemblySection(StepDefinition step)
        {
            if (_pkg == null) return;

            var allSubs = _pkg.GetSubassemblies();
            int count   = allSubs?.Length ?? 0;

            // Resolve the active subassembly for this step (subassemblyId is the
            // primary author-facing field; requiredSubassemblyId is the runtime
            // gate). Either field is enough to call the step "scoped".
            string activeId = !string.IsNullOrWhiteSpace(step?.subassemblyId)
                ? step.subassemblyId
                : step?.requiredSubassemblyId;

            DrawUnifiedSectionHeader($"SUBASSEMBLY ({count})", count);

            if (count == 0)
            {
                EditorGUILayout.LabelField(
                    "  No subassemblies authored yet. Click \"+ New Stub\" below to start.",
                    EditorStyles.miniLabel);
                DrawNewSubassemblyStubButton(step);
                return;
            }

            // ── Active subassembly card ──────────────────────────────────────
            SubassemblyDefinition active = null;
            if (!string.IsNullOrEmpty(activeId))
            {
                for (int i = 0; i < allSubs.Length; i++)
                {
                    if (allSubs[i] != null && string.Equals(allSubs[i].id, activeId, StringComparison.Ordinal))
                    { active = allSubs[i]; break; }
                }
            }

            if (active != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Active: {active.GetDisplayName()}", EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(active.assemblyId))
                    EditorGUILayout.LabelField($"  Assembly: {active.assemblyId}", EditorStyles.miniLabel);
                int memberCount = active.partIds?.Length ?? 0;
                int stepCount   = active.stepIds?.Length ?? 0;
                EditorGUILayout.LabelField($"  Parts: {memberCount}   Steps: {stepCount}", EditorStyles.miniLabel);
                if (active.isAggregate)
                    EditorGUILayout.LabelField("  ⚙ Aggregate (composes other subassemblies)", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(active.description))
                    EditorGUILayout.LabelField("  " + active.description, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy stub JSON", EditorStyles.miniButton, GUILayout.Width(110)))
                    CopySubassemblyStubToClipboard(active);
                if (GUILayout.Button("New stub for this assembly", EditorStyles.miniButton))
                    CopyNewSubassemblyStubToClipboard(step);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.LabelField(
                    "  This step is not scoped to a subassembly.  (Set step.subassemblyId in JSON.)",
                    EditorStyles.miniLabel);
                DrawNewSubassemblyStubButton(step);
            }

            // ── All subassemblies foldout ────────────────────────────────────
            EditorGUILayout.Space(2);
            _subassemblyAllFoldout = EditorGUILayout.Foldout(
                _subassemblyAllFoldout,
                $"All subassemblies in package ({count})",
                true);
            if (!_subassemblyAllFoldout) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < allSubs.Length; i++)
            {
                var sub = allSubs[i];
                if (sub == null) continue;

                bool open = _subassemblyOpenIds.Contains(sub.id);
                bool newOpen = EditorGUILayout.Foldout(
                    open,
                    $"{sub.GetDisplayName()}  ·  {(sub.partIds?.Length ?? 0)} parts" +
                    (sub.isAggregate ? "  (aggregate)" : ""),
                    true);
                if (newOpen != open)
                {
                    if (newOpen) _subassemblyOpenIds.Add(sub.id);
                    else         _subassemblyOpenIds.Remove(sub.id);
                }
                if (!newOpen) continue;

                EditorGUI.indentLevel++;
                if (!string.IsNullOrEmpty(sub.assemblyId))
                    EditorGUILayout.LabelField($"Assembly: {sub.assemblyId}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(sub.description))
                    EditorGUILayout.LabelField(sub.description, EditorStyles.wordWrappedMiniLabel);

                if (sub.partIds != null && sub.partIds.Length > 0)
                {
                    EditorGUILayout.LabelField("Parts:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (var pid in sub.partIds)
                        EditorGUILayout.LabelField("• " + pid, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                if (sub.stepIds != null && sub.stepIds.Length > 0)
                {
                    EditorGUILayout.LabelField("Steps:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (var sid in sub.stepIds)
                        EditorGUILayout.LabelField("• " + sid, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                }

                if (GUILayout.Button("Copy stub JSON", EditorStyles.miniButton, GUILayout.Width(110)))
                    CopySubassemblyStubToClipboard(sub);

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
            EditorGUI.indentLevel--;
        }

        private void DrawNewSubassemblyStubButton(StepDefinition step)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ New subassembly stub", EditorStyles.miniButton, GUILayout.Width(180)))
                CopyNewSubassemblyStubToClipboard(step);
            EditorGUILayout.EndHorizontal();
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
