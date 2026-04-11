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
                    Debug.LogWarning($"[TTAW] Wire preview init failed: {e.Message}");
                }
            }

            bool hasTaskFilter   = _selectedTaskSeqIdx >= 0;
            bool isConfirmAction = hasTaskFilter && _activeTaskKind == "confirm_action";

            DrawPartSceneHandles(sv);

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
                Vector3 worldPos = root.TransformPoint(t.position);
                // Always derive gizmo size from camera distance so it stays readable
                // at any zoom level. Using t.scale directly caused giant 1-metre spheres
                // on targets whose scale was never explicitly reduced from (1,1,1).
                float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                bool isSelected  = i == _selectedIdx;
                bool inStep      = !hasStepFilter || _activeStepTargetIds.Contains(t.def.id);

                // When a step is selected but this target doesn't belong to it, skip entirely.
                if (!inStep) continue;

                // When a task is selected, only draw that task's own target.
                if (hasTaskFilter && !isSelected) continue;

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

            // Handles for selected target
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                ref TargetEditState sel     = ref _targets[_selectedIdx];
                Vector3    worldPos = root.TransformPoint(sel.position);
                Quaternion worldRot = Quaternion.Normalize(root.rotation * sel.rotation);
                float      size = HandleUtility.GetHandleSize(worldPos) * 0.15f;

                Handles.color = ColSelected;
                Handles.DrawWireDisc(worldPos, sv.camera.transform.forward, size * 1.6f);

                EditorGUI.BeginChangeCheck();
                Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, handleRot);
                if (EditorGUI.EndChangeCheck() && !poseCooldownActive && (newWorldPos - worldPos).sqrMagnitude > 1e-10f)
                {
                    BeginEdit();
                    Vector3 newLocal = root.InverseTransformPoint(newWorldPos);
                    Vector3 delta = newLocal - sel.position;
                    sel.position = newLocal;
                    sel.isDirty  = true;
                    if (_multiSelected.Count > 1)
                    {
                        foreach (int idx in _multiSelected)
                        {
                            if (idx == _selectedIdx) continue;
                            ref var t = ref _targets[idx];
                            t.position += delta;
                            t.isDirty = true;
                        }
                    }
                    Repaint();
                }

                if (sceneProfile.SceneRotationHandle)
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion rotHandleOrientation = Tools.pivotRotation == PivotRotation.Local ? worldRot : Quaternion.identity;
                    Quaternion newWorldRot = Handles.RotationHandle(rotHandleOrientation, worldPos);
                    if (EditorGUI.EndChangeCheck() && !poseCooldownActive && Quaternion.Angle(newWorldRot, rotHandleOrientation) > 0.01f)
                    {
                        BeginEdit();

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
