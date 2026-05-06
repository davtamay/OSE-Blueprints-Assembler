using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime.Preview;
using OSE.UI.Root;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.SceneView.cs  —  OnSceneGUI, weld/port/connector gizmos, click-to-snap.
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── SceneView ─────────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sv)
        {
            // Editor/runtime isolation: TTAW manipulates the live scene
            // GameObjects (PartHandles, WriteBackPartGroup*, hierarchy
            // polling, gizmo drags). During Play, those same GameObjects
            // are owned by the runtime — any TTAW write here fights the
            // runtime's visibility / transform writers and corrupts the
            // play-mode visual state. Authoring is inert during Play, BUT
            // read-only display (target icons, connection overlay) still
            // renders so the author can see what task is selected without
            // losing context when they click into a tool task during Play.
            bool isPlaying = Application.isPlaying;

            Transform root = GetPreviewRoot();
            if (root == null) return;

            bool poseCooldownActive = EditorApplication.timeSinceStartup < _poseSwitchCooldownUntil;

            // Lazy-init wire preview: ApplyStepFilter may have run before the spawner
            // service was ready, so we create it here on the first valid SceneView frame.
            // Wrapped in try-catch so any failure does not abort the rest of OnSceneGUI
            // (which would hide the portA/portB PositionHandle gizmos).
            if (_wirePreviewRoot == null && _stepFilterIdx > 0
                && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                try
                {
                    var lazyStep = FindStep(_stepIds[_stepFilterIdx]);
                    RefreshWirePreview(lazyStep);
                }
                catch (System.Exception e)
                {
                    OseLog.Warn($"[TTAW] Wire preview init failed: {e.Message}");
                }
            }

            bool hasTaskFilter   = _selectedTaskSeqIdx >= 0;
            bool isConfirmAction = hasTaskFilter && _activeTaskKind == "confirm_action";

            // Write-capable / poll-based sections — runtime owns these GameObjects
            // during Play, so authoring is inert there. Read-only display below
            // still runs so target icons remain visible during Play.
            if (!isPlaying)
            {
                DrawPartSceneHandles(sv);

                // ── Tool × Part interaction axis gizmo ────────────────────────────
                // Renders a yellow arrow along the authored motion axis when a
                // tool-action task with an interaction payload is selected.
                DrawInteractionAxisGizmo();

                // ── Phase A3: partGroup root rotation gizmo ─────────────────────
                DrawPartGroupRootGizmo();

                // Animation-cue pivot-offset gizmos — one PositionHandle per cue
                // whose pivotOffsetOverride is set. Lets authors drag the rotation
                // / effect origin in-scene instead of typing numbers.
                DrawCuePivotGizmos();

                // Hierarchy-rearrangement detection is event-driven via
                // EditorApplication.hierarchyChanged → MarkHierarchyDirty,
                // not paint-driven. Polling here ran ~60×/sec and silently
                // mass-rewrote partGroup.partIds[] on every repaint while
                // parts were transiently parented to PreviewRoot (post-spawn,
                // post-step-switch), producing phantom "● 32 unsaved" on
                // open and "● 34 unsaved" per step switch — and silently
                // corrupting authored membership on save.

                // Phase A: pose-pill end-transform gizmo for the active
                // tool×part interaction task. Renders only when the End pill
                // is active and the active task has an authored endTransform —
                // otherwise no-ops.
                DrawEndPoseHandleForActiveInteractionTask();
            }

            // confirm_action = terminal button-press — no targets, skip all target gizmos.
            if (isConfirmAction) return;

            DrawConnectionsSceneOverlay();
            if (_targets == null || _targets.Length == 0) return;
            bool      hasStepFilter = _activeStepTargetIds != null;
            var       sceneProfile  = TaskFieldRegistry.Get(_activeTaskKind ?? "");

            // No associated target for this task — draw nothing.
            if (hasTaskFilter && _selectedIdx < 0) return;

            for (int i = 0; i < _targets.Length; i++)
            {
                ref TargetEditState t = ref _targets[i];
                // Display position resolution (priority order):
                //   1. Pill override — when a tool×part pose pill is active
                //      and this target backs the selected/multi-selected
                //      task, snap to the pill's chosen pose so icon + gizmo
                //      + tool tip stay glued.
                //   2. Live anchor part — for anchor-resolved targets
                //      (no static .position field, anchorRef = part), follow
                //      the part's current PreviewRoot-local pose so the
                //      icon tracks the bolt as it moves through the chain.
                //      Mirrors how the runtime tool-target spawner places
                //      its sphere at the anchor part's resolved pose.
                //   3. Static t.position — fallback for targets whose
                //      placement was authored explicitly.
                Vector3 localForDisplay = t.position;
                bool selectionScope = i == _selectedIdx || _multiSelected.Contains(i);
                if (selectionScope && TryGetActivePosePillPositionForTarget(t.def?.id, out Vector3 pillIconPos))
                {
                    localForDisplay = pillIconPos;
                }
                else if (t.def != null && t.def.useLocalOffsetFromPart
                         && !string.IsNullOrEmpty(t.def.associatedPartId))
                {
                    // Live-part-anchored offset (preferred). Mirrors the
                    // runtime resolver in ToolTargetSpawner so editor and
                    // Play render the marker at identical positions.
                    var partGO = FindLivePartGO(t.def.associatedPartId);
                    if (partGO != null)
                    {
                        Vector3 localOffset = new Vector3(
                            t.def.localOffsetFromPart.x,
                            t.def.localOffsetFromPart.y,
                            t.def.localOffsetFromPart.z);
                        localForDisplay = root.InverseTransformPoint(
                            partGO.transform.TransformPoint(localOffset));
                    }
                }
                else if (t.def != null && !string.IsNullOrEmpty(t.def.anchorRef))
                {
                    var anchorGO = FindLivePartGO(t.def.anchorRef);
                    if (anchorGO != null)
                        localForDisplay = root.InverseTransformPoint(anchorGO.transform.position);
                }
                Vector3 worldPos = root.TransformPoint(localForDisplay);
                // Always derive gizmo size from camera distance so it stays readable
                // at any zoom level. Using t.scale directly caused giant 1-metre spheres
                // on targets whose scale was never explicitly reduced from (1,1,1).
                float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                bool isSelected  = i == _selectedIdx;
                bool inStep      = !hasStepFilter || _activeStepTargetIds.Contains(t.def.id);

                // When a step is selected but this target doesn't belong to it, skip entirely.
                if (!inStep) continue;

                // When a task is selected, keep ALL of the step's target icons
                // visible (cyan dots) — only the selected one gets the brighter
                // ColSelected color + wire-disc + PositionHandle below. Hiding
                // the non-selected dots loses the spatial context of where the
                // other tool targets sit relative to the active one.

                Color col = isSelected ? ColSelected
                          : t.isDirty  ? ColDirty
                          : t.hasPlacement ? ColAuthored
                          : ColNoPlacement;
                Handles.color = col;

                if (Handles.Button(worldPos, Quaternion.identity, size, size * 1.5f, Handles.SphereHandleCap))
                {
                    _selectedIdx       = i;
                    _selectedTargetId  = _targets[i].def.id;
                    _clickToSnapActive = false;
                    _snapshotPending   = false;
                    RefreshToolPreview(ref _targets[i]);
                    Repaint();
                }

                if (sceneProfile.SceneWeldArrow)    DrawWeldAxisArrow(ref t, worldPos, 1f);
                if (sceneProfile.ScenePortPoints)   DrawPortPoints(ref t, root, 1f);
                if (sceneProfile.ScenePartConnector) DrawPartConnector(ref t, worldPos, 1f);
            }

            // F key → frame on selected target gizmo
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length
                && Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.F)
            {
                ref TargetEditState ft = ref _targets[_selectedIdx];
                Vector3 worldPos = root.TransformPoint(ft.position);
                float frameSize = HandleUtility.GetHandleSize(worldPos) * 0.5f;
                sv.Frame(new Bounds(worldPos, Vector3.one * frameSize), false);
                Event.current.Use();
            }

            // Handles for selected target — write-capable PositionHandle /
            // RotationHandle drag updates target.position / target.rotation,
            // so disable during Play to avoid fighting the runtime. The
            // wire disc above is purely indicator-style and stays visible.
            if (!isPlaying && _selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                ref TargetEditState sel     = ref _targets[_selectedIdx];
                // Display priority — pill override → live anchor part →
                // static placement. Same chain the per-target sphere icon
                // loop uses, so the gizmo never desynchs from the icon.
                Vector3 displayLocal = sel.position;
                if (TryGetActivePosePillPositionForTarget(sel.def?.id, out Vector3 pillGizmoPos))
                {
                    displayLocal = pillGizmoPos;
                }
                else if (sel.def != null && sel.def.useLocalOffsetFromPart
                         && !string.IsNullOrEmpty(sel.def.associatedPartId))
                {
                    var partGO = FindLivePartGO(sel.def.associatedPartId);
                    if (partGO != null)
                    {
                        Vector3 localOffset = new Vector3(
                            sel.def.localOffsetFromPart.x,
                            sel.def.localOffsetFromPart.y,
                            sel.def.localOffsetFromPart.z);
                        displayLocal = root.InverseTransformPoint(
                            partGO.transform.TransformPoint(localOffset));
                    }
                }
                else if (sel.def != null && !string.IsNullOrEmpty(sel.def.anchorRef))
                {
                    var anchorGO = FindLivePartGO(sel.def.anchorRef);
                    if (anchorGO != null)
                        displayLocal = root.InverseTransformPoint(anchorGO.transform.position);
                }
                Vector3    worldPos = root.TransformPoint(displayLocal);
                Quaternion worldRot = Quaternion.Normalize(root.rotation * sel.rotation);
                float      size = HandleUtility.GetHandleSize(worldPos) * 0.15f;

                Handles.color = ColSelected;
                Handles.DrawWireDisc(worldPos, sv.camera.transform.forward, size * 1.6f);

                bool startPillReadOnly = IsActiveStartPillReadOnly();

                // Visual hint when followPart authoring sync is active and
                // the handle is interactive — i.e. End pill, or Start pill
                // with an authored override. Suppressed on read-only Start
                // since dragging there is a no-op until the author opts in.
                if (IsActiveTaskFollowedInteraction() && !startPillReadOnly)
                {
                    var hintStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.55f, 1.00f, 0.20f, 0.95f) },
                        fontStyle = FontStyle.Bold,
                    };
                    Handles.Label(worldPos + sv.camera.transform.up * (size * 2.2f),
                        "🔗 part follows", hintStyle);
                }

                if (startPillReadOnly)
                {
                    // Read-only Start pill: show a hint label so the author
                    // knows where the override lives without offering a drag
                    // surface that would silently auto-promote startTransform.
                    var roStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.65f, 0.85f, 1f, 0.95f) },
                        fontStyle = FontStyle.Italic,
                    };
                    Handles.Label(worldPos + sv.camera.transform.up * (size * 2.2f),
                        "inherited start (read-only — click \"Author override\" to edit)", roStyle);
                }
                EditorGUI.BeginChangeCheck();
                Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                Vector3 newWorldPos = startPillReadOnly
                    ? worldPos
                    : Handles.PositionHandle(worldPos, handleRot);
                if (!startPillReadOnly && EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldPos - worldPos).sqrMagnitude > 1e-10f)
                {
                    Vector3 newLocal = root.InverseTransformPoint(newWorldPos);
                    // Delta is computed against the displayed local (which may
                    // be the pill-overridden pose), not sel.position. Without
                    // this, an active pill override would inject a (pillPos -
                    // sel.position) jump into the first drag — sel.position
                    // would suddenly snap to the pill pose, dirtying the
                    // authored target with the override offset.
                    Vector3 delta = newLocal - displayLocal;

                    if (IsActiveStartPillFollowedOverride())
                    {
                        // Start-pill on a tool×part interaction: route the
                        // drag to entry.startTransform (the per-task override)
                        // and snap the part. Do NOT touch sel.position or
                        // entry.endTransform — target placement and end pose
                        // are unrelated to the start-pose override.
                        TryApplyStartOverridePositionDelta(delta);
                    }
                    else
                    {
                        BeginEdit();
                        sel.position += delta;
                        sel.isDirty  = true;

                        if (_multiSelected.Count > 1)
                        {
                            foreach (int idx in _multiSelected)
                            {
                                if (idx == _selectedIdx) continue;
                                if (idx < 0 || idx >= _targets.Length) continue;
                                ref var t = ref _targets[idx];
                                t.position += delta;
                                t.isDirty = true;
                            }
                        }
                        // followPart authoring sync — when on, also move the
                        // part's end pose by the same delta so the bolt stays
                        // glued to wherever the author drags the target.
                        TryApplyTargetPositionDeltaToFollowedPart(delta);
                    }
                    Repaint();
                }

                if (sceneProfile.SceneRotationHandle && !startPillReadOnly)
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion rotHandleOrientation = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                    Quaternion newWorldRot = Handles.RotationHandle(rotHandleOrientation, worldPos);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && Quaternion.Angle(newWorldRot, rotHandleOrientation) > 0.01f)
                    {
                        // Snapshot baselines on first frame of drag (for batch rotation)
                        if (!_rotDragActive)
                        {
                            _rotDragActive      = true;
                            _rotDragStartHandle = rotHandleOrientation;
                            _rotDragStartLocal  = sel.rotation;
                            _rotDragStartMulti  = new Dictionary<int, Quaternion>();
                            if (_multiSelected.Count > 1)
                                foreach (int idx in _multiSelected)
                                    if (idx != _selectedIdx)
                                        _rotDragStartMulti[idx] = _targets[idx].rotation;
                        }

                        // World-space delta from the handle, applied directly (no damping).
                        Quaternion worldDelta = newWorldRot * Quaternion.Inverse(_rotDragStartHandle);
                        Quaternion newLocalRot = Quaternion.Inverse(root.rotation) * (worldDelta * (root.rotation * _rotDragStartLocal));
                        Quaternion localDelta = newLocalRot * Quaternion.Inverse(_rotDragStartLocal);

                        if (IsActiveStartPillFollowedOverride())
                        {
                            // Start-pill override path: rotate startTransform
                            // and the part GO; leave sel.rotation alone.
                            TryApplyStartOverrideRotationDelta(localDelta);
                        }
                        else
                        {
                            BeginEdit();
                            sel.rotation = newLocalRot;
                            sel.isDirty  = true;
                            if (_multiSelected.Count > 1)
                            {
                                foreach (int idx in _multiSelected)
                                {
                                    if (idx == _selectedIdx) continue;
                                    ref var t = ref _targets[idx];
                                    Quaternion startRot = _rotDragStartMulti.TryGetValue(idx, out var sr) ? sr : t.rotation;
                                    t.rotation = localDelta * startRot;
                                    t.isDirty = true;
                                }
                            }
                            // followPart authoring sync — rotation delta also
                            // applied to the part's end pose so the part stays
                            // oriented to wherever the author rotates the target.
                            TryApplyTargetRotationDeltaToFollowedPart(localDelta);
                        }
                        Repaint();
                    }
                    else if (_rotDragActive)
                    {
                        _rotDragActive = false;
                    }
                }

                if (Event.current.type == EventType.MouseUp)
                    EndEdit();

                // Tool preview — tracks the position/rotation gizmo in real-time
                UpdateToolPreview(ref sel);

                // portA / portB drag handles — any Connect-family step
                if (_activeStepIsConnect)
                {
                    Handles.color = ColPortPoint;

                    // Resolve the wire entry that owns this target so we can keep
                    // the wire entry, _targets, and the spline preview in sync.
                    StepDefinition dragStep = _stepFilterIdx > 0 && _stepIds != null
                        && _stepFilterIdx < _stepIds.Length
                        ? FindStep(_stepIds[_stepFilterIdx]) : null;
                    WireConnectEntry dragWire = null;
                    if (dragStep?.wireConnect?.wires != null && sel.def != null)
                        foreach (var w in dragStep.wireConnect.wires)
                            if (w?.targetId == sel.def.id) { dragWire = w; break; }

                    // Use wire entry positions as authoritative source so gizmo matches spline.
                    if (dragWire != null)
                    {
                        sel.portA = new Vector3(dragWire.portA.x, dragWire.portA.y, dragWire.portA.z);
                        sel.portB = new Vector3(dragWire.portB.x, dragWire.portB.y, dragWire.portB.z);
                    }

                    Vector3 portAWorld = root.TransformPoint(sel.portA);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortA = Handles.PositionHandle(portAWorld, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newPortA - portAWorld).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.portA = root.InverseTransformPoint(newPortA);
                        sel.isDirty = true;
                        if (dragWire != null) dragWire.portA = PackageJsonUtils.ToFloat3(sel.portA);
                        if (dragStep != null) { _dirtyStepIds.Add(dragStep.id); RefreshWirePreview(dragStep); }
                        Repaint();
                    }

                    Vector3 portBWorld = root.TransformPoint(sel.portB);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPortB = Handles.PositionHandle(portBWorld, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newPortB - portBWorld).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.portB = root.InverseTransformPoint(newPortB);
                        sel.isDirty = true;
                        if (dragWire != null) dragWire.portB = PackageJsonUtils.ToFloat3(sel.portB);
                        if (dragStep != null) { _dirtyStepIds.Add(dragStep.id); RefreshWirePreview(dragStep); }
                        Repaint();
                    }

                    if (Event.current.type == EventType.MouseUp) EndEdit();
                }

                // Weld gizmo handles — two draggable PositionHandles defining axis + length
                if (sel.weldGizmoActive && ShowWeldGroup())
                {
                    Vector3 worldA = root.TransformPoint(sel.weldGizmoA);
                    Vector3 worldB = root.TransformPoint(sel.weldGizmoB);

                    // Handle A (orange — start)
                    Handles.color = new Color(1f, 0.5f, 0f, 1f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldA = Handles.PositionHandle(worldA, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldA - worldA).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.weldGizmoA = root.InverseTransformPoint(newWorldA);
                        RecomputeWeldFromGizmo(ref sel);
                        sel.isDirty = true;
                        Repaint();
                    }

                    // Handle B (yellow — tip / direction)
                    Handles.color = new Color(1f, 0.9f, 0f, 1f);
                    EditorGUI.BeginChangeCheck();
                    Vector3 newWorldB = Handles.PositionHandle(worldB, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldB - worldB).sqrMagnitude > 1e-10f)
                    {
                        BeginEdit();
                        sel.weldGizmoB = root.InverseTransformPoint(newWorldB);
                        RecomputeWeldFromGizmo(ref sel);
                        sel.isDirty = true;
                        Repaint();
                    }

                    // Visual line A→B with arrow
                    Handles.color = Color.white;
                    Handles.DrawLine(worldA, worldB, 2f);
                    if ((worldB - worldA).sqrMagnitude > 0.0001f)
                    {
                        float arrowSize = HandleUtility.GetHandleSize(worldA) * 0.3f;
                        Handles.ArrowHandleCap(0, worldA,
                            Quaternion.LookRotation((worldB - worldA).normalized),
                            arrowSize, EventType.Repaint);
                    }

                    // Labels
                    Handles.Label(worldA, "A", EditorStyles.boldLabel);
                    Handles.Label(worldB, $"B  ({sel.weldLength:F3} m)", EditorStyles.boldLabel);

                    if (Event.current.type == EventType.MouseUp) EndEdit();
                }
            }

            HandleClickToSnap();
        }

        private void DrawWeldAxisArrow(ref TargetEditState t, Vector3 worldPos, float alpha = 1f)
        {
            if (t.weldAxis.sqrMagnitude < 0.001f) return;
            Transform weldRoot = GetPreviewRoot();
            if (weldRoot == null) return;
            Vector3 worldAxis = weldRoot.TransformDirection(t.weldAxis.normalized);
            float   arrowLen  = HandleUtility.GetHandleSize(worldPos) * 1.2f;
            Color   c         = ColWeldAxis; c.a *= alpha;
            Handles.color = c;
            Handles.DrawAAPolyLine(2.5f,
                worldPos - worldAxis * arrowLen * 0.5f,
                worldPos + worldAxis * arrowLen * 0.5f);
            Handles.ConeHandleCap(0,
                worldPos + worldAxis * arrowLen * 0.5f,
                Quaternion.LookRotation(worldAxis),
                arrowLen * 0.14f,
                EventType.Repaint);
        }

        /// <summary>
        /// Draws a thin dashed line from the target sphere to the associated part's
        /// origin, so authors can visually confirm the target is in the right coordinate
        /// space relative to its part.
        /// </summary>
        private void DrawPartConnector(ref TargetEditState t, Vector3 worldPos, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(t.def.associatedPartId)) return;
            var partGo = FindLivePartGO(t.def.associatedPartId);
            if (partGo == null) return;

            Color c = Handles.color;
            c.a = alpha * 0.25f;
            Handles.color = c;
            Handles.DrawDottedLine(worldPos, partGo.transform.position, 3f);
        }

        private void InitWeldGizmo(ref TargetEditState t)
        {
            t.weldGizmoA = t.position;
            float   len = t.weldLength > 0.0001f ? t.weldLength : 0.05f;
            Vector3 dir = t.weldAxis.sqrMagnitude > 0.001f ? t.weldAxis.normalized : Vector3.forward;
            t.weldGizmoB = t.position + dir * len;
        }

        private static void RecomputeWeldFromGizmo(ref TargetEditState t)
        {
            Vector3 delta = t.weldGizmoB - t.weldGizmoA;
            t.weldLength  = delta.magnitude;
            t.weldAxis    = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
        }

        private void DrawPortPoints(ref TargetEditState t, Transform root, float alpha = 1f)
        {
            // Show port spheres for any Connect-family step, or in All Steps mode
            if (!string.IsNullOrEmpty(_activeStepProfile) && !_activeStepIsConnect) return;
            if (t.portA.sqrMagnitude < 0.00001f && t.portB.sqrMagnitude < 0.00001f) return;

            Color c = ColPortPoint; c.a *= alpha;
            Handles.color = c;
            float sz = HandleUtility.GetHandleSize(root.TransformPoint(t.portA)) * 0.07f;

            Vector3 wA = root.TransformPoint(t.portA);
            Vector3 wB = root.TransformPoint(t.portB);
            Handles.SphereHandleCap(0, wA, Quaternion.identity, sz, EventType.Repaint);
            Handles.SphereHandleCap(0, wB, Quaternion.identity, sz, EventType.Repaint);
            Handles.DrawDottedLine(wA, wB, 4f);
        }

        // ── Phase A3: partGroup root rotation gizmo ───────────────────────

        /// <summary>
        /// When a partGroup root GO exists (Phase A2 created it), draws a
        /// rotation handle on it. Rotating the handle updates the step's
        /// workingOrientation.partGroupRotation and marks dirty. The author
        /// sees all member parts rotate in real-time via Unity parenting.
        /// </summary>
        private void DrawPartGroupRootGizmo()
        {
            if (_partGroupRootGOs == null || _partGroupRootGOs.Count == 0) return;

            // Only draw the full gizmo (rotation + position handles) on the
            // selected group. Other groups get a compact wire disc + label.
            foreach (var kvp in _partGroupRootGOs)
            {
                if (kvp.Value == null) continue;
                // Full gizmo shows when the group is selected via either:
                // - Canvas GROUPS section click (_canvasSelectedSubId), OR
                // - Task sequence PART [G] row click (_selectedGroupIdx)
                bool isSelected = string.Equals(_canvasSelectedSubId, kvp.Key, System.StringComparison.Ordinal);
                if (!isSelected && _selectedGroupIdx >= 0 && _groups != null && _selectedGroupIdx < _groups.Length)
                    isSelected = string.Equals(_groups[_selectedGroupIdx].def?.id, kvp.Key, System.StringComparison.Ordinal);
                if (isSelected)
                    DrawPartGroupRootGizmoFull(kvp.Value, kvp.Key);
                else
                    DrawPartGroupRootGizmoCompact(kvp.Value, kvp.Key);
            }
        }

        private void DrawPartGroupRootGizmoCompact(GameObject rootGO, string subId)
        {
            var rootT = rootGO.transform;
            Vector3 worldPos = rootT.position;
            float gizmoSize = HandleUtility.GetHandleSize(worldPos);

            Handles.color = new Color(0.20f, 0.62f, 0.95f, 0.15f);
            Handles.DrawWireDisc(worldPos,
                SceneView.lastActiveSceneView?.camera?.transform.forward ?? Vector3.forward,
                gizmoSize * 0.3f);

            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.20f, 0.62f, 0.95f, 0.5f) },
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
            };
            Handles.Label(worldPos + Vector3.up * gizmoSize * 0.35f, subId, labelStyle);
        }

        private void DrawPartGroupRootGizmoFull(GameObject rootGO, string subId)
        {
            var rootT = rootGO.transform;
            Vector3 worldPos = rootT.position;

            // Visual indicator: solid dot + wire ring at the partGroup centroid.
            // Marks the group currently being edited — this is the "● in the
            // center of the gizmo" that mirrors the task-row modifiable indicator.
            float gizmoSize = HandleUtility.GetHandleSize(worldPos);
            Vector3 camForward = SceneView.lastActiveSceneView?.camera?.transform.forward ?? Vector3.forward;
            Handles.color = new Color(0.20f, 0.62f, 0.95f, 0.95f);
            Handles.DrawSolidDisc(worldPos, camForward, gizmoSize * 0.10f);
            Handles.color = new Color(0.20f, 0.62f, 0.95f, 0.35f);
            Handles.DrawWireDisc(worldPos, camForward, gizmoSize * 0.5f);

            // Label
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
            };
            Handles.Label(worldPos + Vector3.up * gizmoSize * 0.55f,
                $"Group: {subId}", labelStyle);

            // Rotation handle
            EditorGUI.BeginChangeCheck();
            Quaternion currentRot = rootT.rotation;
            Handles.color = new Color(0.20f, 0.62f, 0.95f, 0.85f);
            Quaternion newRot = Handles.RotationHandle(currentRot, worldPos);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rootGO.transform, "Rotate PartGroup");
                rootT.rotation = newRot;

                // Write to GroupEditState if a group is selected via task sequence
                if (_selectedGroupIdx >= 0 && _groups != null && _selectedGroupIdx < _groups.Length
                    && string.Equals(_groups[_selectedGroupIdx].def?.id, subId, StringComparison.Ordinal))
                {
                    var root = GetPreviewRoot();
                    Quaternion localRot = root != null ? Quaternion.Inverse(root.rotation) * newRot : newRot;
                    ApplyRotationToGroup(ref _groups[_selectedGroupIdx], localRot);
                    _groups[_selectedGroupIdx].isDirty = true;
                    MarkPartGroupDirty(subId);
                }
                else
                {
                    WriteBackPartGroupRotation(newRot);
                }

                SceneView.RepaintAll();
                Repaint();
            }

            // Position handle
            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(0.20f, 0.62f, 0.95f, 0.65f);
            Vector3 newWorldPos = Handles.PositionHandle(worldPos, currentRot);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rootGO.transform, "Move PartGroup");
                rootT.position = newWorldPos;

                if (_selectedGroupIdx >= 0 && _groups != null && _selectedGroupIdx < _groups.Length
                    && string.Equals(_groups[_selectedGroupIdx].def?.id, subId, StringComparison.Ordinal))
                {
                    var root = GetPreviewRoot();
                    Vector3 localPos = root != null ? root.InverseTransformPoint(newWorldPos) : newWorldPos;
                    ApplyPositionToGroup(ref _groups[_selectedGroupIdx], localPos);
                    _groups[_selectedGroupIdx].isDirty = true;
                    MarkPartGroupDirty(subId);
                }
                else
                {
                    WriteBackPartGroupOffset(newWorldPos);
                }

                SceneView.RepaintAll();
                Repaint();
            }
        }

        /// <summary>
        /// Converts the partGroup root's world rotation back to euler angles
        /// and writes them to step.workingOrientation.partGroupRotation.
        /// </summary>
        private void WriteBackPartGroupRotation(Quaternion worldRot)
        {
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length) return;
            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            // Convert world rotation to PreviewRoot-local, then to euler
            var root = GetPreviewRoot();
            Quaternion localRot = root != null
                ? Quaternion.Inverse(root.rotation) * worldRot
                : worldRot;
            Vector3 euler = localRot.eulerAngles;

            // Normalize angles to [-180, 180] for readability
            if (euler.x > 180f) euler.x -= 360f;
            if (euler.y > 180f) euler.y -= 360f;
            if (euler.z > 180f) euler.z -= 360f;

            // Round to 2 decimal places
            euler.x = Mathf.Round(euler.x * 100f) / 100f;
            euler.y = Mathf.Round(euler.y * 100f) / 100f;
            euler.z = Mathf.Round(euler.z * 100f) / 100f;

            step.workingOrientation ??= new StepWorkingOrientationPayload();
            step.workingOrientation.partGroupRotation = new SceneFloat3
            {
                x = euler.x, y = euler.y, z = euler.z
            };
            _dirtyStepIds.Add(step.id);
        }

        /// <summary>
        /// Converts the partGroup root's world position back to the offset
        /// field on step.workingOrientation.partGroupPositionOffset.
        /// </summary>
        private void WriteBackPartGroupOffset(Vector3 worldPos)
        {
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length) return;
            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            var root = GetPreviewRoot();
            Vector3 localPos = root != null ? root.InverseTransformPoint(worldPos) : worldPos;

            // The offset is relative to the partGroup frame center
            Vector3 offset = localPos - _sceneBuildPartGroupFramePos;

            offset.x = Mathf.Round(offset.x * 10000f) / 10000f;
            offset.y = Mathf.Round(offset.y * 10000f) / 10000f;
            offset.z = Mathf.Round(offset.z * 10000f) / 10000f;

            step.workingOrientation ??= new StepWorkingOrientationPayload();
            step.workingOrientation.partGroupPositionOffset = new SceneFloat3
            {
                x = offset.x, y = offset.y, z = offset.z
            };
            _dirtyStepIds.Add(step.id);
        }

        // ── Hierarchy → authoring sync ─────────────────────────────────────

        /// <summary>
        /// Detects when the author drags a part from one group root to another
        /// in the Unity Hierarchy and updates the partGroup partIds[]
        /// accordingly. Called every OnSceneGUI frame (cheap — just checks
        /// parent references against the root GO dictionary).
        /// </summary>
        private void PollHierarchyGroupChanges()
        {
            // DISABLED 2026-05-05. This method's intent — auto-detect when an
            // author drags parts between Group_* roots in the Unity Hierarchy
            // and reflect that into partGroup.partIds — is sound, but it cannot
            // distinguish author drags from our own reparenting (RespawnScene,
            // EnsureAllPartGroupRoots, ResetAllGroupRootsToOriginPreservingChildren,
            // and the spawner). Both manifest as EditorApplication.hierarchyChanged
            // events with no provenance. Without that distinction, every step
            // switch fired this method while parts were transiently under
            // PreviewRoot (Group_* re-parenting hadn't completed yet), and the
            // method silently stripped authored membership from every group.
            //
            // Group membership is authored via the TTAW PartGroup inspector
            // (add/remove buttons, drag-drop into the group's parts list, +Add
            // from Selection). Those paths mark dirty correctly. The "drag in
            // Unity Hierarchy" affordance is recoverable later if we route every
            // internal reparent through a single suppression flag, but that's a
            // larger architectural change — not worth shipping a corrupting
            // poller while waiting.
            //
            // See feedback_no_per_paint_pollers_mutating_authored_data.md.
            return;
#pragma warning disable CS0162 // unreachable code — kept for future re-enable
            if (_partGroupRootGOs == null || _partGroupRootGOs.Count == 0 || _pkg == null)
                return;

            if (!ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner)
                || spawner?.SpawnedParts == null)
                return;

            // Build reverse lookup: root GO instance ID → partGroup id
            var rootToSubId = new Dictionary<int, string>();
            foreach (var kvp in _partGroupRootGOs)
            {
                if (kvp.Value != null)
                    rootToSubId[kvp.Value.GetInstanceID()] = kvp.Key;
            }

            foreach (var partGO in spawner.SpawnedParts)
            {
                if (partGO == null) continue;
                string partId = partGO.name;
                if (string.IsNullOrEmpty(partId)) continue;

                // What group root is this part currently parented under?
                Transform parent = partGO.transform.parent;
                string currentParentSubId = null;
                if (parent != null && rootToSubId.TryGetValue(parent.gameObject.GetInstanceID(), out var sid))
                    currentParentSubId = sid;

                // What group does the data model say this part belongs to?
                string authoredSubId = null;
                foreach (var sub in _pkg.GetPartGroups())
                {
                    if (sub == null || sub.isAggregate || sub.partIds == null) continue;
                    foreach (var pid in sub.partIds)
                    {
                        if (string.Equals(pid, partId, StringComparison.Ordinal))
                        { authoredSubId = sub.id; break; }
                    }
                    if (authoredSubId != null) break;
                }

                // If they differ, the author rearranged in the Hierarchy
                if (string.Equals(currentParentSubId, authoredSubId, StringComparison.Ordinal))
                    continue;

                // Remove from old group
                if (!string.IsNullOrEmpty(authoredSubId)
                    && _pkg.TryGetPartGroup(authoredSubId, out var oldSub)
                    && oldSub?.partIds != null)
                {
                    var list = new List<string>(oldSub.partIds);
                    if (list.Remove(partId))
                    {
                        oldSub.partIds = list.Count > 0 ? list.ToArray() : Array.Empty<string>();
                        MarkPartGroupDirty(oldSub.id);
                    }
                }

                // Add to new group
                if (!string.IsNullOrEmpty(currentParentSubId)
                    && _pkg.TryGetPartGroup(currentParentSubId, out var newSub)
                    && newSub != null)
                {
                    var set = new HashSet<string>(newSub.partIds ?? Array.Empty<string>(), StringComparer.Ordinal);
                    if (set.Add(partId))
                    {
                        newSub.partIds = set.ToArray();
                        MarkPartGroupDirty(newSub.id);
                    }
                }

                if (!string.IsNullOrEmpty(currentParentSubId) || !string.IsNullOrEmpty(authoredSubId))
                    Repaint();
            }
#pragma warning restore CS0162
        }

        private void HandleClickToSnap()
        {
            if (!_clickToSnapActive) return;
            if (_selectedIdx < 0 || _targets == null || _selectedIdx >= _targets.Length) return;

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt || e.control || e.shift)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return;

            // Only snap if we hit one of our live spawned part meshes
            bool hitPartMesh = false;
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var snapSpawner) && snapSpawner?.SpawnedParts != null)
            {
                foreach (var go in snapSpawner.SpawnedParts)
                    if (go != null && hit.transform.IsChildOf(go.transform))
                    { hitPartMesh = true; break; }
            }
            if (!hitPartMesh) return;

            Transform root = GetPreviewRoot();
            if (root == null) return;

            ref TargetEditState sel = ref _targets[_selectedIdx];
            BeginEdit();
            sel.position    = root.InverseTransformPoint(hit.point);
            Vector3 localN  = root.InverseTransformDirection(hit.normal).normalized;
            sel.rotation    = Quaternion.FromToRotation(Vector3.up, localN);
            sel.weldAxis    = localN;
            sel.isDirty     = true;
            EndEdit();

            _clickToSnapActive = false;
            e.Use();
            Repaint();
            SceneView.RepaintAll();
        }
    }
}
