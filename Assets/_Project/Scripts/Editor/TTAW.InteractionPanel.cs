using System;
using System.Collections.Generic;
using System.Text;
using OSE.Content;
using OSE.Interaction;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.InteractionPanel.cs — Tool × Part Interaction authoring panel for
// tool-target tasks. Three bands: Endpoints (🔩 info / jump-to-part), Motion
// Shape (🔧 writes to ToolActionDefinition.interaction payload), Tool Companion
// (🔧 cues + particle link-out). Field visibility is data-driven by
// ArchetypeFieldProfile; adding a new archetype needs zero panel changes.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private static readonly string[] s_easingOptions = new[]
        {
            "linear", "smoothStep", "easeIn", "easeOut", "easeInOut",
        };

        /// <summary>
        /// Draws the Tool × Part Interaction panel for a <see cref="ToolActionDefinition"/>.
        /// Creates an <see cref="ToolPartInteraction"/> on first edit; safe to skip rendering
        /// when no tool target is selected.
        /// </summary>
        private void DrawInteractionPanel(StepDefinition step, ToolActionDefinition taskAction)
        {
            if (taskAction == null || step == null) return;

            EditorGUILayout.Space(6);
            DrawInteractionBandHeader("TOOL × PART INTERACTION");

            DrawEndpointsBand(step, taskAction);
            EditorGUILayout.Space(4);
            DrawMotionShapeBand(step, taskAction);
            EditorGUILayout.Space(4);
            DrawPreviewControls(step, taskAction);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ▶ Preview controls — runs the same factory the runtime uses against
        // the part's live scene GameObject. Pose is snapshotted at Start and
        // restored at Stop so the preview is replayable without drift.
        // ══════════════════════════════════════════════════════════════════════

        private void DrawPreviewControls(StepDefinition step, ToolActionDefinition taskAction)
        {
            EditorGUILayout.BeginHorizontal();
            bool running = InteractionPreviewRunner.IsRunning;
            GUI.backgroundColor = running ? new Color(1f, 0.55f, 0.35f) : new Color(0.55f, 0.85f, 0.55f);
            if (GUILayout.Button(
                new GUIContent(running ? "⏹ Stop preview" : "▶ Preview in scene",
                    "Runs the authored interaction against the live scene part so the motion " +
                    "can be tuned without entering Play mode. Original pose is restored on stop."),
                GUILayout.Height(22)))
            {
                if (running) InteractionPreviewRunner.Stop();
                else         StartInteractionPreview(step, taskAction);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Resolves the part GO, end pose, and archetype the same way the runtime
        /// bridge layer does, then hands the built <see cref="IPartEffect"/> to
        /// <see cref="InteractionPreviewRunner"/>.
        /// </summary>
        private void StartInteractionPreview(StepDefinition step, ToolActionDefinition taskAction)
        {
            string partId = ResolvePartIdForTarget(taskAction.targetId);
            if (string.IsNullOrEmpty(partId))
            {
                Debug.LogWarning("[TTAW] Preview aborted: target has no associatedPartId.");
                return;
            }

            var partGo = FindLivePartGO(partId);
            if (partGo == null)
            {
                Debug.LogWarning($"[TTAW] Preview aborted: no live GameObject for part '{partId}'. " +
                                 "Spawn the scene via the TTAW preview first.");
                return;
            }

            // Honor taskAction.interaction.toPose (Phase F) so edit-mode preview
            // matches runtime end-pose resolution bit-for-bit. Null/auto falls back
            // to the legacy stepPoses[stepId] → assembled chain inside the helper.
            string toPoseToken = taskAction.interaction?.toPose;
            if (!TryResolvePreviewEndPose(partId, step.id, toPoseToken,
                                          out Vector3 endPos, out Quaternion endRot, out Vector3 endScale))
            {
                Debug.LogWarning($"[TTAW] Preview aborted: no end pose for part '{partId}' in step '{step.id}'. " +
                                 "Author a stepPose in the Part task first.");
                return;
            }

            Vector3    startPos   = partGo.transform.localPosition;
            Quaternion startRot   = partGo.transform.localRotation;
            Vector3    startScale = partGo.transform.localScale;

            var args = new PartEffectBuildArgs
            {
                PartTransform = partGo.transform,
                PreviewRoot   = partGo.transform.parent,
                Start         = new PoseSnapshot(startPos, startRot, startScale),
                End           = new PoseSnapshot(endPos,   endRot,   endScale),
                Payload       = taskAction.interaction,
                ToolPose      = ResolveToolPoseForAction(taskAction),
            };

            string archetype = !string.IsNullOrEmpty(taskAction.interaction?.archetype)
                ? taskAction.interaction.archetype
                : PartEffectArchetypes.Lerp;

            var effect = PartEffectRegistry.Build(archetype, args);
            if (effect == null)
            {
                Debug.LogWarning($"[TTAW] Preview aborted: no effect factory registered for '{archetype}'.");
                return;
            }

            // Make sure the cyan tool preview is present so the author sees the drill
            // body tracking the part — matches runtime "tool follows part" behavior.
            // If the author has "Show tool preview" off, we flip it on for the preview
            // duration and leave it on after (same UX as TTAW's other preview affordances).
            Transform toolTransform = EnsureToolPreviewForTarget(taskAction.targetId);
            bool followPart = taskAction.interaction?.followPart ?? true;

            InteractionPreviewRunner.Start(effect, partGo.transform, 1.5f,
                tool: toolTransform, followPart: followPart);
        }

        /// <summary>
        /// Returns the cyan tool-preview GameObject's transform for the target, spawning
        /// it via <c>RefreshToolPreview</c> if needed. The tool is parented under the
        /// preview root so parent-local deltas match the runtime accumulation path.
        /// Returns null when no tool is mapped to the target or the asset can't be loaded.
        /// </summary>
        private Transform EnsureToolPreviewForTarget(string targetId)
        {
            if (_targets == null) return null;
            int idx = -1;
            for (int i = 0; i < _targets.Length; i++)
                if (_targets[i].def?.id == targetId) { idx = i; break; }
            if (idx < 0) return null;

            // Flip on the tool-preview toggle if needed, then rebuild.
            if (!_showToolPreview)
            {
                _showToolPreview = true;
                RefreshToolPreview(ref _targets[idx]);
            }
            else if (_toolPreviewGO == null)
            {
                RefreshToolPreview(ref _targets[idx]);
            }
            return _toolPreviewGO != null ? _toolPreviewGO.transform : null;
        }

        /// <summary>
        /// Edit-time equivalent of <c>ToolActionExecutor.ResolveEndPose</c>. Shares
        /// the same grammar so edit-mode preview and runtime can never drift.
        /// </summary>
        private bool TryResolvePreviewEndPose(string partId, string stepId, string toPoseToken,
            out Vector3 endPos, out Quaternion endRot, out Vector3 endScale)
        {
            endPos = default; endRot = Quaternion.identity; endScale = Vector3.one;
            var pp = FindPartPlacement(partId);
            if (pp == null) return false;

            // Explicit token dispatch (Phase F).
            if (!ToPoseTokens.IsAuto(toPoseToken))
            {
                if (toPoseToken == ToPoseTokens.Start)
                {
                    AssignPreviewPose(pp.startPosition, pp.startRotation, pp.startScale,
                                      out endPos, out endRot, out endScale);
                    return true;
                }
                if (toPoseToken == ToPoseTokens.Assembled)
                {
                    AssignPreviewPose(pp.assembledPosition, pp.assembledRotation, pp.assembledScale,
                                      out endPos, out endRot, out endScale);
                    return true;
                }
                if (toPoseToken.StartsWith(ToPoseTokens.StepPrefix, StringComparison.Ordinal))
                {
                    string refStepId = toPoseToken.Substring(ToPoseTokens.StepPrefix.Length);

                    // Pose-chain invariant (G.1): only self-step refs are valid. Cross-task
                    // refs degrade to auto and log so the author can re-pick. This mirrors
                    // the runtime guard in ToolActionExecutor.ResolveEndPose.
                    if (!string.IsNullOrEmpty(refStepId) && !string.Equals(refStepId, stepId, StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"[TTAW Preview] Cross-task pose reference ignored: " +
                                         $"toPose='{toPoseToken}' on step '{stepId}'. Only self-step refs are valid.");
                    }
                    else if (pp.stepPoses != null)
                    {
                        foreach (var sp in pp.stepPoses)
                        {
                            if (sp == null || sp.stepId != refStepId) continue;
                            AssignPreviewPose(sp.position, sp.rotation, sp.scale,
                                              out endPos, out endRot, out endScale);
                            return true;
                        }
                    }
                    // fall through to implicit chain
                }
            }

            // Implicit chain: stepPoses[currentStepId] → assembledPosition.
            if (pp.stepPoses != null)
            {
                foreach (var sp in pp.stepPoses)
                {
                    if (sp == null || sp.stepId != stepId) continue;
                    AssignPreviewPose(sp.position, sp.rotation, sp.scale,
                                      out endPos, out endRot, out endScale);
                    return true;
                }
            }

            AssignPreviewPose(pp.assembledPosition, pp.assembledRotation, pp.assembledScale,
                              out endPos, out endRot, out endScale);
            return true;
        }

        private static void AssignPreviewPose(SceneFloat3 p, SceneQuaternion r, SceneFloat3 s,
            out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            pos = new Vector3(p.x, p.y, p.z);
            rot = !r.IsIdentity ? new Quaternion(r.x, r.y, r.z, r.w) : Quaternion.identity;
            scale = new Vector3(s.x, s.y, s.z);
        }

        private ToolPoseConfig ResolveToolPoseForAction(ToolActionDefinition taskAction)
        {
            if (_pkg?.tools == null || string.IsNullOrEmpty(taskAction?.toolId)) return null;
            foreach (var t in _pkg.tools)
                if (t != null && t.id == taskAction.toolId) return t.toolPose;
            return null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // Scene-view axis gizmo. Called from OnSceneGUI when the selected task
        // is a tool-target with an authored interaction axis. Renders a yellow
        // arrow at the part's current world position along the resolved axis.
        // ══════════════════════════════════════════════════════════════════════

        internal void DrawInteractionAxisGizmo()
        {
            if (_activeTaskKind != "toolAction") return;
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length) return;

            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step?.requiredToolActions == null) return;

            var order = GetOrDeriveTaskOrder(step);
            if (order == null || _selectedTaskSeqIdx < 0 || _selectedTaskSeqIdx >= order.Count) return;
            var entry = order[_selectedTaskSeqIdx];
            if (entry == null || entry.kind != "toolAction") return;

            ToolActionDefinition taskAction = null;
            foreach (var a in step.requiredToolActions)
                if (a != null && a.id == entry.id) { taskAction = a; break; }
            if (taskAction?.interaction?.axis == null) return;

            string partId = ResolvePartIdForTarget(taskAction.targetId);
            if (string.IsNullOrEmpty(partId)) return;

            var partGo = FindLivePartGO(partId);
            if (partGo == null) return;

            // Resolve the axis to world space. Only part_local is visualized for now;
            // tool_action_axis would need a live tool GO (Phase E stretch).
            Vector3 worldAxis;
            var ax = taskAction.interaction.axis;
            Vector3 local = new Vector3(ax.vec.x, ax.vec.y, ax.vec.z);
            if (local.sqrMagnitude < 1e-6f) return;
            local.Normalize();

            switch (ax.space)
            {
                case AxisSpaces.PartLocal:
                    worldAxis = partGo.transform.TransformDirection(local);
                    break;
                case AxisSpaces.World:
                    worldAxis = local;
                    break;
                default:
                    // TargetLocal and ToolActionAxis fall through — authoring preview
                    // treats them as part-local until Phase E wiring resolves the transforms.
                    worldAxis = partGo.transform.TransformDirection(local);
                    break;
            }

            Vector3 origin = partGo.transform.position;
            float   size   = HandleUtility.GetHandleSize(origin);
            float   length = Mathf.Max(size * 1.2f, (taskAction.interaction.distance > 0f
                ? taskAction.interaction.distance
                : size * 1.2f));

            var prev = Handles.color;
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
            Handles.ArrowHandleCap(0, origin, Quaternion.LookRotation(worldAxis), length, EventType.Repaint);
            Handles.color = prev;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 🔩 ENDPOINTS — pose-chain authoring (Phase F).
        // Single authored field: End pose. Start is never authored — it is always
        // inherited from whatever the previous task left the part at (runtime uses
        // the part's current transform). See the pose-chain invariant in the plan.
        // ══════════════════════════════════════════════════════════════════════

        private void DrawEndpointsBand(StepDefinition step, ToolActionDefinition taskAction)
        {
            EditorGUILayout.LabelField("🔩 Endpoints (writes to Part)", EditorStyles.boldLabel);

            // The Part association picker — always visible, so the cross-entity
            // link is both transparent and editable without leaving the window.
            DrawPartAssociationPicker(taskAction.targetId);

            string partId = ResolvePartIdForTarget(taskAction.targetId);
            if (string.IsNullOrEmpty(partId))
            {
                EditorGUILayout.HelpBox(
                    "No part associated with this target. Pick one in the 🔩 Part dropdown above " +
                    "to link a part — motion can't be authored without one.",
                    MessageType.Warning);
                return;
            }

            // ── G.2.5.a: inline end-transform (task-owned, per pose-chain invariant) ──
            TaskOrderEntry taskEntry = FindTaskOrderEntryForAction(step, taskAction);
            DrawEndTransformFields(step, taskAction, taskEntry, partId);
        }

        /// <summary>
        /// Inline end-transform authoring for a Tool task. Replaces the Phase F
        /// pose-dropdown with position/rotation/scale fields that write directly to
        /// <see cref="TaskOrderEntry.endTransform"/>. When null the task falls through
        /// to the legacy Phase F <c>toPose</c> chain (→ assembled), so pre-G.2.5
        /// content keeps working while new authoring uses inline transforms.
        /// </summary>
        private void DrawEndTransformFields(
            StepDefinition step, ToolActionDefinition taskAction, TaskOrderEntry taskEntry, string partId)
        {
            if (taskEntry == null)
            {
                EditorGUILayout.HelpBox(
                    "Tool task entry not found in step.taskOrder — cannot author an inline end-transform.",
                    MessageType.Warning);
                return;
            }

            // Legacy Phase F banner + migration button when toPose is still set.
            string legacyToPose = taskAction.interaction?.toPose;
            if (!string.IsNullOrEmpty(legacyToPose) && taskEntry.endTransform == null)
            {
                EditorGUILayout.HelpBox(
                    $"Legacy Phase F toPose token: '{legacyToPose}'. The runtime still resolves it, " +
                    "but new authoring should use the inline End Transform below. Click \"Migrate to inline\" " +
                    "to bake the current resolved pose into endTransform and clear the token.",
                    MessageType.Info);
                if (GUILayout.Button("Migrate legacy toPose → inline End Transform", GUILayout.Height(20)))
                {
                    MigrateLegacyToPoseToInline(step, taskAction, taskEntry, partId);
                }
            }

            bool hasInline = taskEntry.endTransform != null;

            // Header row — status + live-scene capture.
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                hasInline ? "🔩 End Transform (inline):" : "🔩 End Transform:",
                EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (hasInline && GUILayout.Button(
                new GUIContent("Clear", "Clear the inline transform. Runtime falls back to assembledPosition."),
                GUILayout.Width(56)))
            {
                taskEntry.endTransform = null;
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
            if (GUILayout.Button(
                new GUIContent(hasInline ? "From scene" : "Capture from scene",
                    "Capture the part's live scene transform into this task's inline end-transform."),
                GUILayout.Width(hasInline ? 90 : 140)))
            {
                CaptureLiveTransformIntoTaskEndTransform(step, taskEntry, partId);
            }
            if (!hasInline && GUILayout.Button(
                new GUIContent("From assembled",
                    "Initialize the inline transform from the part's assembledPosition."),
                GUILayout.Width(110)))
            {
                CaptureAssembledIntoTaskEndTransform(step, taskEntry, partId);
            }
            EditorGUILayout.EndHorizontal();

            if (!hasInline)
            {
                EditorGUILayout.HelpBox(
                    "No inline end-transform set. On task completion the runtime snaps the part to its assembled pose " +
                    "(unless a legacy toPose token resolves otherwise). Use the buttons above to author one.",
                    MessageType.None);
            }
            else
            {
                DrawInlineTransformEditor(step, taskEntry.endTransform);
            }

            // Motion readout — plain English, always shows "(inherited)" on the left.
            string endLabel = hasInline ? "inline transform" : "Assembled";
            EditorGUILayout.LabelField(
                $"ℹ  Motion: (inherited from previous task)  →  {endLabel}",
                EditorStyles.miniLabel);

            if (GUILayout.Button(
                new GUIContent("Edit the Part's own Start / Assembled poses →",
                    "Jumps the task-sequence selection to the Part task for this part so you can " +
                    "author its intrinsic Start and Assembled poses."),
                GUILayout.Height(20)))
            {
                JumpSelectionToPart(partId);
            }
        }

        /// <summary>
        /// Renders the inline position / rotation (euler) / scale fields that mutate
        /// <paramref name="t"/> directly. Marks the step dirty on any change so the
        /// existing <c>TTAW.WriteJson.cs</c> pipeline persists the new field.
        /// </summary>
        private void DrawInlineTransformEditor(StepDefinition step, TaskEndTransform t)
        {
            if (t == null) return;

            EditorGUI.BeginChangeCheck();
            Vector3 pos = new Vector3(t.position.x, t.position.y, t.position.z);
            Vector3 newPos = EditorGUILayout.Vector3Field("Position", pos);
            Quaternion rot = t.rotation.IsIdentity
                ? Quaternion.identity
                : new Quaternion(t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w);
            Vector3 eul = rot.eulerAngles;
            Vector3 newEul = EditorGUILayout.Vector3Field("Rotation (Euler)", eul);
            Vector3 scl = new Vector3(t.scale.x, t.scale.y, t.scale.z);
            Vector3 newScl = EditorGUILayout.Vector3Field("Scale", scl);
            if (EditorGUI.EndChangeCheck())
            {
                t.position = new SceneFloat3 { x = newPos.x, y = newPos.y, z = newPos.z };
                Quaternion newRot = Quaternion.Euler(newEul);
                t.rotation = new SceneQuaternion { x = newRot.x, y = newRot.y, z = newRot.z, w = newRot.w };
                t.scale = new SceneFloat3 { x = newScl.x, y = newScl.y, z = newScl.z };
                _dirtyStepIds.Add(step.id);
            }
        }

        /// <summary>Finds the <see cref="TaskOrderEntry"/> in <paramref name="step"/>.taskOrder
        /// whose kind is "toolAction" and id matches <paramref name="taskAction"/>.id.</summary>
        private static TaskOrderEntry FindTaskOrderEntryForAction(StepDefinition step, ToolActionDefinition taskAction)
        {
            if (step?.taskOrder == null || taskAction == null) return null;
            foreach (var e in step.taskOrder)
            {
                if (e != null && e.kind == "toolAction" && e.id == taskAction.id)
                    return e;
            }
            return null;
        }

        /// <summary>
        /// Snapshots the part's live scene transform into the task's end-transform.
        /// The scene transform at authoring time represents "where the author posed
        /// the part to end up," so this is the canonical way to author inline poses.
        /// </summary>
        private void CaptureLiveTransformIntoTaskEndTransform(StepDefinition step, TaskOrderEntry taskEntry, string partId)
        {
            var go = FindLivePartGO(partId);
            if (go == null)
            {
                Debug.LogWarning($"[TTAW] Capture aborted: no live GameObject for part '{partId}'. " +
                                 "Spawn the scene via the TTAW preview first.");
                return;
            }
            var tf = go.transform;
            taskEntry.endTransform ??= new TaskEndTransform();
            taskEntry.endTransform.position = new SceneFloat3
                { x = tf.localPosition.x, y = tf.localPosition.y, z = tf.localPosition.z };
            taskEntry.endTransform.rotation = new SceneQuaternion
                { x = tf.localRotation.x, y = tf.localRotation.y, z = tf.localRotation.z, w = tf.localRotation.w };
            taskEntry.endTransform.scale = new SceneFloat3
                { x = tf.localScale.x, y = tf.localScale.y, z = tf.localScale.z };
            _dirtyStepIds.Add(step.id);
            Repaint();
            SceneView.RepaintAll();
        }

        private void CaptureAssembledIntoTaskEndTransform(StepDefinition step, TaskOrderEntry taskEntry, string partId)
        {
            var pp = FindPartPlacement(partId);
            if (pp == null) { Debug.LogWarning($"[TTAW] No PartPreviewPlacement for '{partId}'."); return; }
            taskEntry.endTransform ??= new TaskEndTransform();
            taskEntry.endTransform.position = pp.assembledPosition;
            taskEntry.endTransform.rotation = pp.assembledRotation;
            taskEntry.endTransform.scale    = pp.assembledScale;
            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        /// <summary>
        /// One-click migration from Phase F's <c>toPose</c> token to an inline
        /// <see cref="TaskEndTransform"/>. Uses the runtime-equivalent resolver to
        /// compute the pose the token WOULD snap to, bakes it inline, and clears the
        /// token so subsequent loads skip the legacy fallback branch entirely.
        /// </summary>
        private void MigrateLegacyToPoseToInline(
            StepDefinition step, ToolActionDefinition taskAction, TaskOrderEntry taskEntry, string partId)
        {
            if (!TryResolvePreviewEndPose(partId, step.id, taskAction.interaction?.toPose,
                                          out Vector3 p, out Quaternion r, out Vector3 s))
                return;
            taskEntry.endTransform = new TaskEndTransform
            {
                position = new SceneFloat3 { x = p.x, y = p.y, z = p.z },
                rotation = new SceneQuaternion { x = r.x, y = r.y, z = r.z, w = r.w },
                scale    = new SceneFloat3 { x = s.x, y = s.y, z = s.z },
            };
            if (taskAction.interaction != null)
                taskAction.interaction.toPose = null;
            _dirtyStepIds.Add(step.id);
            Repaint();
        }

        /// <summary>
        /// Renders the Tool task's End-pose dropdown. Writes to
        /// <see cref="ToolPartInteraction.toPose"/>. Lazy-creates the
        /// <see cref="ToolPartInteraction"/> on first pick so authors don't need
        /// to touch archetype fields first.
        /// </summary>
        private void DrawEndPoseRow(StepDefinition step, ToolActionDefinition taskAction, string partId)
        {
            PartPreviewPlacement pp = FindPartPlacement(partId);
            string current = taskAction.interaction?.toPose;
            string label   = PosePickerDropdown.ResolveLabel(current, pp, step.id) ?? "(stale)";
            if (ToPoseTokens.IsAuto(current)) label += "  [auto]";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔩 End pose:",
                    "Which pose the part is in when this tool action completes.\n" +
                    "Start pose is never authored — it is always inherited from the previous task " +
                    "that touched this part (pose-chain invariant)."),
                GUILayout.Width(110));
            if (EditorGUILayout.DropdownButton(new GUIContent(label), FocusType.Keyboard))
            {
                var captured = taskAction;
                var capturedStep = step;
                var capturedPartId = partId;
                PosePickerDropdown.Open(pp, step.id, token =>
                {
                    if (token == PosePickerDropdown.CreateNewToken)
                    {
                        // Lazily author a stepPose at the current step from the part's live
                        // scene transform, then bind the tool's toPose to it.
                        string newToken = CreateStepPoseAtCurrentStepFromLiveTransform(capturedPartId, capturedStep.id);
                        if (!string.IsNullOrEmpty(newToken))
                            EnsureInteraction(captured).toPose = newToken;
                    }
                    else
                    {
                        EnsureInteraction(captured).toPose = ToPoseTokens.IsAuto(token) ? null : token;
                    }
                    _dirtyStepIds.Add(capturedStep.id);
                    Repaint();
                }, offerCreateNew: true);
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Ensures a <see cref="StepPoseEntry"/> exists on <paramref name="partId"/>'s
        /// editor state anchored at <paramref name="stepId"/>. If absent, delegates to
        /// <c>AddStepPoseForCurrentStep</c> (the canonical authoring helper that
        /// initialises transforms from the active pose mode and marks the part dirty,
        /// so the change round-trips through the standard write pipeline).
        /// Returns the <c>step:&lt;stepId&gt;</c> token for immediate binding.
        /// </summary>
        private string CreateStepPoseAtCurrentStepFromLiveTransform(string partId, string stepId)
        {
            if (string.IsNullOrEmpty(partId) || string.IsNullOrEmpty(stepId)) return null;

            int partIdx = -1;
            if (_parts != null)
            {
                for (int i = 0; i < _parts.Length; i++)
                    if (_parts[i].def?.id == partId) { partIdx = i; break; }
            }
            if (partIdx < 0) return null;

            // Skip if a stepPose entry already exists for this stepId.
            var existing = _parts[partIdx].stepPoses;
            if (existing != null)
            {
                foreach (var sp in existing)
                    if (sp != null && sp.stepId == stepId) return ToPoseTokens.StepPrefix + stepId;
            }

            AddStepPoseForCurrentStep(partIdx, stepId);
            return ToPoseTokens.StepPrefix + stepId;
        }

        /// <summary>
        /// Part picker for the target's <c>associatedPartId</c>. Renders a search-
        /// enabled <see cref="PartPickerDropdown"/> (Unity's <c>AdvancedDropdown</c>)
        /// so it scales to hundreds of parts without the Unity <c>Popup</c> scroll
        /// slog. Writes directly to the <see cref="TargetDefinition"/> and marks the
        /// matching <see cref="TargetEditState"/> dirty so the existing TTAW save
        /// pipeline persists the change.
        /// </summary>
        private void DrawPartAssociationPicker(string targetId)
        {
            if (string.IsNullOrEmpty(targetId) || _pkg == null) return;

            // Resolve the target via the package accessor (handles split-file merges).
            TargetDefinition targetDef = null;
            var targets = _pkg.GetTargets();
            if (targets != null)
            {
                foreach (var t in targets)
                    if (t != null && t.id == targetId) { targetDef = t; break; }
            }

            // Fall back to the editor's TargetEditState if the package array hasn't
            // surfaced the entry yet (fresh tool-target task, just-loaded package).
            if (targetDef == null && _targets != null)
            {
                for (int i = 0; i < _targets.Length; i++)
                    if (_targets[i].def?.id == targetId) { targetDef = _targets[i].def; break; }
            }
            if (targetDef == null)
            {
                EditorGUILayout.HelpBox(
                    $"Target '{targetId}' isn't in this package yet. Save the tool-target " +
                    "task (click Save, or re-select the row) then reopen this panel to link a part.",
                    MessageType.Warning);
                return;
            }

            string current = targetDef.associatedPartId;
            string label   = string.IsNullOrEmpty(current)
                ? "(none — click to pick a part)"
                : ResolvePartDisplayLabel(current);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔩 Part:",
                    "Which part this target drives.\n" +
                    "Writes target.associatedPartId. Searchable — type to filter " +
                    "the list by id or name."),
                GUILayout.Width(110));
            if (EditorGUILayout.DropdownButton(new GUIContent(label), FocusType.Keyboard))
            {
                var parts = _pkg.GetParts();
                string capturedTargetId = targetId;
                var capturedTargetDef   = targetDef;
                PartPickerDropdown.Open(parts, "Pick a part", picked =>
                {
                    capturedTargetDef.associatedPartId = string.IsNullOrEmpty(picked) ? null : picked;
                    if (_targets != null)
                    {
                        for (int i = 0; i < _targets.Length; i++)
                        {
                            if (_targets[i].def?.id == capturedTargetId)
                            {
                                _targets[i].isDirty = true;
                                RefreshToolPreview(ref _targets[i]);
                                break;
                            }
                        }
                    }
                    SceneView.RepaintAll();
                    Repaint();
                });
            }
            EditorGUILayout.EndHorizontal();
        }

        private string ResolvePartDisplayLabel(string partId)
        {
            var parts = _pkg?.GetParts();
            if (parts != null)
            {
                foreach (var p in parts)
                {
                    if (p != null && p.id == partId)
                        return string.IsNullOrEmpty(p.name) || p.name == p.id
                            ? p.id
                            : $"{p.id}  —  {p.name}";
                }
            }
            return partId; // resolve best-effort when id not in package
        }

        // ══════════════════════════════════════════════════════════════════════
        // 🔧 MOTION SHAPE — archetype dropdown + per-archetype fields.
        // Writes to taskAction.interaction payload.
        // ══════════════════════════════════════════════════════════════════════

        private void DrawMotionShapeBand(StepDefinition step, ToolActionDefinition taskAction)
        {
            EditorGUILayout.LabelField("🔧 Motion shape (writes to Tool Action)", EditorStyles.boldLabel);

            // Archetype dropdown
            string[] keys = ArchetypeFieldRegistry.KnownArchetypes;
            string current = taskAction.interaction?.archetype;
            if (string.IsNullOrEmpty(current)) current = "lerp";

            int idx = 0;
            for (int i = 0; i < keys.Length; i++)
                if (keys[i] == current) { idx = i; break; }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Archetype:",
                    "How the part moves during this tool action.\n" +
                    "lerp: generic A→B. thread_in: plunge + rotate (drill/screw). " +
                    "axis_plunge: push along axis. rotate_in_place: spin without translating. " +
                    "clamp_hold: no motion."),
                GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(idx, keys);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck() && newIdx != idx)
            {
                EnsureInteraction(taskAction).archetype = keys[newIdx] == "lerp" ? null : keys[newIdx];
                _dirtyStepIds.Add(step.id);
                Repaint();
                current = keys[newIdx];
            }

            var profile = ArchetypeFieldRegistry.Get(current);
            if (!string.IsNullOrEmpty(profile.HelpText))
                EditorGUILayout.LabelField(profile.HelpText, EditorStyles.miniLabel);

            // Per-archetype fields — each gated by the profile, so irrelevant fields never appear.
            if (profile.ShowAxis)            DrawAxisField(step, taskAction);
            if (profile.ShowDistance)        DrawDistanceField(step, taskAction);
            if (profile.ShowTotalRotations)  DrawTotalRotationsField(step, taskAction);
            if (profile.ShowRotationPerUnit) DrawRotationPerUnitField(step, taskAction);
            if (profile.ShowEasing)          DrawEasingField(step, taskAction);
            if (profile.ShowFollowPart)      DrawFollowPartField(step, taskAction);
        }

        // ── Per-field drawers ────────────────────────────────────────────────

        private void DrawAxisField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            payload.axis ??= new AxisSpec { space = AxisSpaces.PartLocal, vec = new SceneFloat3 { x = 0, y = -1, z = 0 } };

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Axis:",
                    "Motion axis. part_local = part's own frame (most common). " +
                    "tool_action_axis = inherits from the tool's ToolPose.actionAxis."),
                GUILayout.Width(110));
            string[] spaces = { AxisSpaces.PartLocal, AxisSpaces.ToolActionAxis, AxisSpaces.TargetLocal, AxisSpaces.World };
            int sIdx = System.Array.IndexOf(spaces, payload.axis.space);
            if (sIdx < 0) sIdx = 0;
            EditorGUI.BeginChangeCheck();
            int newSIdx = EditorGUILayout.Popup(sIdx, spaces, GUILayout.Width(130));
            if (EditorGUI.EndChangeCheck() && newSIdx != sIdx)
            {
                payload.axis.space = spaces[newSIdx];
                _dirtyStepIds.Add(step.id);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            Vector3 v = new Vector3(payload.axis.vec.x, payload.axis.vec.y, payload.axis.vec.z);
            Vector3 newV = EditorGUILayout.Vector3Field(" ", v);
            if (EditorGUI.EndChangeCheck() && newV != v)
            {
                payload.axis.vec = new SceneFloat3 { x = newV.x, y = newV.y, z = newV.z };
                _dirtyStepIds.Add(step.id);
            }
        }

        private void DrawDistanceField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Distance:",
                    "Meters along the axis. 0 = auto (derived from start→end pose delta)."),
                GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            float newDist = EditorGUILayout.FloatField(payload.distance);
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newDist, payload.distance))
            {
                payload.distance = Mathf.Max(0f, newDist);
                _dirtyStepIds.Add(step.id);
            }
            EditorGUILayout.LabelField(payload.distance == 0f ? "(auto)" : "m",
                EditorStyles.miniLabel, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTotalRotationsField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Total rot:",
                    "Total rotation in degrees across the whole action.\n" +
                    "Overridden by 'Deg / meter' if that is also set > 0."),
                GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            float newVal = EditorGUILayout.FloatField(payload.totalRotationsDeg);
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newVal, payload.totalRotationsDeg))
            {
                payload.totalRotationsDeg = newVal;
                _dirtyStepIds.Add(step.id);
            }
            EditorGUILayout.LabelField("°", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRotationPerUnitField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Deg / meter:",
                    "Thread-pitch model: degrees of rotation per meter of axial travel.\n" +
                    "When > 0, takes precedence over Total rot. 1080 ≈ 3 full turns per cm."),
                GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            float newVal = EditorGUILayout.FloatField(payload.rotationDegPerUnit);
            if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newVal, payload.rotationDegPerUnit))
            {
                payload.rotationDegPerUnit = Mathf.Max(0f, newVal);
                _dirtyStepIds.Add(step.id);
            }
            EditorGUILayout.LabelField("°/m", EditorStyles.miniLabel, GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEasingField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            string current = string.IsNullOrEmpty(payload.easing) ? "linear" : payload.easing;
            int idx = System.Array.IndexOf(s_easingOptions, current);
            if (idx < 0) idx = 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("🔧 Easing:", "Curve applied to progress before motion is computed."),
                GUILayout.Width(110));
            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(idx, s_easingOptions);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck() && newIdx != idx)
            {
                payload.easing = s_easingOptions[newIdx] == "linear" ? null : s_easingOptions[newIdx];
                _dirtyStepIds.Add(step.id);
            }
        }

        private void DrawFollowPartField(StepDefinition step, ToolActionDefinition taskAction)
        {
            var payload = EnsureInteraction(taskAction);
            EditorGUI.BeginChangeCheck();
            bool newVal = EditorGUILayout.ToggleLeft(
                new GUIContent("🔧 Tool follows part",
                    "When on (default), the tool transform tracks the part's world-space " +
                    "displacement each frame. Off for clamp_hold-style interactions."),
                payload.followPart);
            if (EditorGUI.EndChangeCheck() && newVal != payload.followPart)
            {
                payload.followPart = newVal;
                _dirtyStepIds.Add(step.id);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════════

        private static ToolPartInteraction EnsureInteraction(ToolActionDefinition taskAction)
        {
            return taskAction.interaction ??= new ToolPartInteraction();
        }

        private string ResolvePartIdForTarget(string targetId)
        {
            if (_pkg?.targets == null || string.IsNullOrEmpty(targetId)) return null;
            foreach (var t in _pkg.targets)
                if (t != null && t.id == targetId) return t.associatedPartId;
            return null;
        }

        private bool HasStepPose(string partId, string stepId)
        {
            var pp = FindPartPlacement(partId);
            if (pp?.stepPoses == null) return false;
            foreach (var sp in pp.stepPoses)
                if (sp != null && sp.stepId == stepId) return true;
            return false;
        }

        /// <summary>
        /// Builds a one-line summary of downstream steps that will see the part
        /// in the end pose authored for the current step, detecting which (if any)
        /// later step overrides it with another <see cref="StepPoseEntry"/>.
        /// Resolution is ordinal by <see cref="StepDefinition.sequenceIndex"/>.
        /// </summary>
        private string BuildPropagationSummary(string partId, string currentStepId)
        {
            var pp = FindPartPlacement(partId);
            if (_pkg?.steps == null) return null;

            int currentSeq = -1;
            foreach (var s in _pkg.steps)
                if (s?.id == currentStepId) { currentSeq = s.sequenceIndex; break; }
            if (currentSeq < 0) return null;

            var laterSteps = new List<(int seq, string id)>();
            foreach (var s in _pkg.steps)
                if (s != null && s.sequenceIndex > currentSeq)
                    laterSteps.Add((s.sequenceIndex, s.id));
            laterSteps.Sort((a, b) => a.seq.CompareTo(b.seq));

            string overrideStepId = null;
            if (pp?.stepPoses != null)
            {
                foreach (var (seq, id) in laterSteps)
                {
                    foreach (var sp in pp.stepPoses)
                    {
                        if (sp != null && sp.stepId == id) { overrideStepId = id; break; }
                    }
                    if (overrideStepId != null) break;
                }
            }

            if (laterSteps.Count == 0) return "Downstream: no later steps in this package.";
            if (overrideStepId == null)
                return $"Downstream: pose inherited by {laterSteps.Count} later step(s) (no override).";
            int inheritedCount = 0;
            foreach (var (_, id) in laterSteps)
            {
                if (id == overrideStepId) break;
                inheritedCount++;
            }
            return $"Downstream: inherited by {inheritedCount} step(s); overridden in {overrideStepId}.";
        }

        /// <summary>
        /// Jumps the task-sequence selection to the Part task matching
        /// <paramref name="partId"/>, so the author can edit start/end poses in the
        /// existing Part UI without leaving the window.
        /// </summary>
        private void JumpSelectionToPart(string partId)
        {
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length) return;
            var step = FindStep(_stepIds[_stepFilterIdx]);
            var order = step != null ? GetOrDeriveTaskOrder(step) : null;
            if (order == null) return;
            for (int i = 0; i < order.Count; i++)
            {
                var e = order[i];
                if (e != null && e.kind == "part" && TaskInstanceId.ToPartId(e.id) == partId)
                {
                    _selectedTaskSeqIdx = i;
                    Repaint();
                    return;
                }
            }
        }

        private void DrawInteractionBandHeader(string title)
        {
            var rect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.28f, 0.38f));
            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                normal   = { textColor = new Color(0.88f, 0.9f, 0.95f) },
                padding  = new RectOffset(6, 0, 1, 0),
            };
            GUI.Label(rect, title, style);
        }
    }
}
