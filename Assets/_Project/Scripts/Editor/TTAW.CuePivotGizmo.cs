// TTAW.CuePivotGizmo.cs — Scene-view gizmo for AnimationCueEntry.pivotOffset.
// ──────────────────────────────────────────────────────────────────────────────
// For every cue in the active step whose pivotOffsetOverride is true, draws a
// Handles.PositionHandle at (host default pivot + authored offset) in world
// space. Dragging the handle writes the new local-space offset back into the
// cue. When no override is set the handle is not drawn — the runtime falls
// back to the host's natural pivot (mesh origin for parts, member centroid
// for groups).
//
// Host resolution:
//   - orientSubassembly / particle with a targetSubassemblyId → group root
//   - particle with targetPartIds → live part GO
// Other cue types don't render a gizmo (their pivot fields aren't wired yet).

using System;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private static readonly Color CuePivotGizmoTint = new(1.00f, 0.70f, 0.25f, 1f);

        private void DrawCuePivotGizmos()
        {
            if (_pkg == null || _stepIds == null) return;
            if (_stepFilterIdx <= 0 || _stepFilterIdx >= _stepIds.Length) return;

            var step = FindStep(_stepIds[_stepFilterIdx]);
            var cues = step?.animationCues?.cues;
            if (cues == null || cues.Length == 0) return;

            for (int i = 0; i < cues.Length; i++)
            {
                var cue = cues[i];
                if (cue == null || !cue.pivotOffsetOverride) continue;
                if (!TryResolveCueHostRoot(cue, out Transform hostRoot, out Vector3 defaultPivotLocal)) continue;

                Vector3 offsetLocal   = new(cue.pivotOffset.x, cue.pivotOffset.y, cue.pivotOffset.z);
                Vector3 pivotLocal    = defaultPivotLocal + offsetLocal;
                Vector3 handleWorld   = hostRoot.TransformPoint(pivotLocal);
                Quaternion handleRot  = hostRoot.rotation;

                // Labeled sphere + handle.
                using (new Handles.DrawingScope(CuePivotGizmoTint))
                {
                    Handles.SphereHandleCap(0, handleWorld, handleRot,
                        HandleUtility.GetHandleSize(handleWorld) * 0.08f, EventType.Repaint);
                    Handles.Label(handleWorld + Vector3.up * 0.02f,
                        $"cue {i + 1} · {(string.IsNullOrEmpty(cue.type) ? "?" : cue.type)} pivot",
                        EditorStyles.miniBoldLabel);
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newWorld = Handles.PositionHandle(handleWorld, handleRot);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 newLocal = hostRoot.InverseTransformPoint(newWorld);
                    Vector3 newOffset = newLocal - defaultPivotLocal;
                    cue.pivotOffset = new SceneFloat3
                    {
                        x = Mathf.Round(newOffset.x * 10000f) / 10000f,
                        y = Mathf.Round(newOffset.y * 10000f) / 10000f,
                        z = Mathf.Round(newOffset.z * 10000f) / 10000f,
                    };
                    _dirtyStepIds.Add(step.id);
                    Repaint();
                }
            }
        }

        /// <summary>
        /// Resolves the host transform + default-pivot (in host-local space)
        /// for a cue that carries a pivotOffset. Returns false when no live
        /// host can be located for the current scene state.
        /// </summary>
        private bool TryResolveCueHostRoot(AnimationCueEntry cue,
                                           out Transform hostRoot,
                                           out Vector3 defaultPivotLocal)
        {
            hostRoot = null;
            defaultPivotLocal = Vector3.zero;
            if (cue == null) return false;

            // Prefer a subassembly host when present — a group pivot defaults
            // to the children centroid, matching OrientSubassemblyPlayer and
            // ParticlePlayer.
            if (!string.IsNullOrEmpty(cue.targetSubassemblyId)
                && _subassemblyRootGOs != null
                && _subassemblyRootGOs.TryGetValue(cue.targetSubassemblyId, out var groupGO)
                && groupGO != null)
            {
                hostRoot = groupGO.transform;
                defaultPivotLocal = ChildrenCentroidLocal(hostRoot);
                return true;
            }

            // Fall back to the first target part for single-part hosts
            // (particle cues scoped to a part). Default pivot is origin.
            if (cue.targetPartIds != null && cue.targetPartIds.Length > 0)
            {
                foreach (var pid in cue.targetPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    var partGO = FindLivePartGO(pid);
                    if (partGO != null)
                    {
                        hostRoot = partGO.transform;
                        defaultPivotLocal = Vector3.zero;
                        return true;
                    }
                }
            }
            return false;
        }

        private static Vector3 ChildrenCentroidLocal(Transform root)
        {
            if (root == null || root.childCount == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                sum += c.localPosition;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }
    }
}
