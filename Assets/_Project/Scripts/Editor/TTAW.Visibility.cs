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
//     step.requiredSubassemblyId → subassembly.partIds
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

        private bool _visibilityShowHelp;
        private int  _visibilityAddPartIdx;

        private readonly List<string> _visScratchOwnedHere       = new();
        private readonly List<string> _visScratchOwnedSubHere    = new();
        private readonly List<string> _visScratchInheritedEarlier = new();

        // ── Section drawer (called from DrawUnifiedList) ──────────────────────

        private void DrawVisibilitySection(StepDefinition step)
        {
            if (_pkg == null || step == null) return;

            // Compute the three visibility buckets from cached scene-build state
            // and the step's own part ids. The scene-build cache is populated by
            // RespawnScene the same way the spawner runs at runtime, so the
            // numbers shown here always match what's drawn in the SceneView.
            ComputeVisibilityBuckets(step,
                                     out int totalVisible,
                                     out HashSet<string> ownedSubPartIds);

            int unique = totalVisible;
            DrawUnifiedSectionHeader($"WHAT'S SHOWING ({unique})", unique);

            // ── Help blurb (collapsed by default) ─────────────────────────────
            EditorGUILayout.BeginHorizontal();
            string helpLabel = _visibilityShowHelp ? "▼ Why am I seeing what I see?" : "▶ Why am I seeing what I see?";
            if (GUILayout.Button(helpLabel, EditorStyles.miniLabel, GUILayout.ExpandWidth(true)))
                _visibilityShowHelp = !_visibilityShowHelp;
            EditorGUILayout.EndHorizontal();
            if (_visibilityShowHelp)
            {
                EditorGUILayout.HelpBox(
                    "A part is visible in a step when an earlier or current step "
                    + "claims it via 'required parts' or via the step's required "
                    + "subassembly. There is no separate 'show only' field today — "
                    + "adding a part to the visible list also marks it as required "
                    + "for task completion in this step.",
                    MessageType.Info);
            }

            // ── Bucket: parts owned by this step ──────────────────────────────
            if (_visScratchOwnedHere.Count > 0)
            {
                EditorGUILayout.LabelField($"Owned by this step  ({_visScratchOwnedHere.Count})", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                int removeIdx = -1;
                for (int i = 0; i < _visScratchOwnedHere.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("• " + _visScratchOwnedHere[i], EditorStyles.miniLabel);
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
                        removeIdx = i;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
                if (removeIdx >= 0)
                    RemoveRequiredPartFromStep(step, _visScratchOwnedHere[removeIdx]);
                EditorGUILayout.Space(2);
            }

            // ── Bucket: parts visible because of this step's subassembly ──────
            if (_visScratchOwnedSubHere.Count > 0)
            {
                EditorGUILayout.LabelField(
                    $"Inherited from this step's subassembly  ({_visScratchOwnedSubHere.Count})",
                    EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                foreach (var pid in _visScratchOwnedSubHere)
                    EditorGUILayout.LabelField("• " + pid + "   (read-only)", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            // ── Bucket: parts visible because an earlier step placed them ─────
            if (_visScratchInheritedEarlier.Count > 0)
            {
                EditorGUILayout.LabelField(
                    $"Already placed in earlier steps  ({_visScratchInheritedEarlier.Count})",
                    EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                int shown = 0;
                const int kMaxShown = 24;
                foreach (var pid in _visScratchInheritedEarlier)
                {
                    if (shown++ >= kMaxShown)
                    {
                        EditorGUILayout.LabelField(
                            $"  …and {_visScratchInheritedEarlier.Count - kMaxShown} more",
                            EditorStyles.miniLabel);
                        break;
                    }
                    EditorGUILayout.LabelField("• " + pid + "   (assembled)", EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            // ── Add picker — pick any unused part to show in this step ────────
            DrawAddPartToVisibility(step, ownedSubPartIds);
        }

        // ── Compute / categorise ──────────────────────────────────────────────

        private void ComputeVisibilityBuckets(
            StepDefinition step,
            out int totalVisible,
            out HashSet<string> ownedSubPartIds)
        {
            _visScratchOwnedHere.Clear();
            _visScratchOwnedSubHere.Clear();
            _visScratchInheritedEarlier.Clear();
            ownedSubPartIds = new HashSet<string>(StringComparer.Ordinal);
            totalVisible    = 0;

            // Collect this step's required-subassembly parts so we can label
            // them separately and exclude them from the "owned by this step"
            // bucket (the requiredPartIds bucket).
            if (!string.IsNullOrEmpty(step.requiredSubassemblyId)
                && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subDef)
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

                if (placedAt == currentSeq)
                {
                    if (ownedSubPartIds.Contains(part.id))
                        _visScratchOwnedSubHere.Add(part.id);
                    else if (ownedHere.Contains(part.id))
                        _visScratchOwnedHere.Add(part.id);
                    else
                        // Edge case — part is owned by another step that shares
                        // this sequence index. Bucket as "owned here" so it's
                        // visible to the author.
                        _visScratchOwnedHere.Add(part.id);
                }
                else // placedAt < currentSeq
                {
                    _visScratchInheritedEarlier.Add(part.id);
                }
                totalVisible++;
            }

            _visScratchOwnedHere.Sort(StringComparer.Ordinal);
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

            EditorGUILayout.Space(4);
            if (candidates.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "  All package parts are already showing here.",
                    EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Add part to show:", GUILayout.Width(110));
            _visibilityAddPartIdx = Mathf.Clamp(_visibilityAddPartIdx, 0, candidates.Count - 1);
            _visibilityAddPartIdx = EditorGUILayout.Popup(_visibilityAddPartIdx, candidates.ToArray());

            var addBtn = new GUIContent("+ Show",
                "Adds the selected part to this step's required parts so the spawner "
                + "renders it. Note: this also marks it required for task completion — "
                + "there is no 'visual only' field today.");
            if (GUILayout.Button(addBtn, EditorStyles.miniButton, GUILayout.Width(70)))
            {
                AddRequiredPartToStep(step, candidates[_visibilityAddPartIdx]);
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
    }
}
