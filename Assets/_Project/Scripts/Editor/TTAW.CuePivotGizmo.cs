// TTAW.CuePivotGizmo.cs — Scene-view gizmo for AnimationCueEntry pivots.
// ──────────────────────────────────────────────────────────────────────────────
// For every pivot-capable cue in the active step, draws:
//   • a BLUE wire sphere at the host's default pivot (the centroid the
//     runtime will use) — ALWAYS visible so authors see where rotations
//     pivot without having to toggle the override.
//   • an ORANGE sphere + PositionHandle at (centroid + offset) ONLY when
//     pivotOffsetOverride is true. Dragging it writes the new local-space
//     offset back into the cue.
//
// Default-pivot source:
//   PartGroup hosts → PivotCentroidResolver.ComputeBodyCentroidLocal
//     (the SAME function the runtime uses; gizmo parity is guaranteed
//     by construction). When the resolver returns null (no body members
//     yet at this step — e.g. the first step introducing the group),
//     the gizmo falls back to the Group_ root's origin with a "no body
//     yet" label to keep authoring honest.
//   Part hosts → part's local origin (mesh pivot).

using System;
using OSE.Content;
using OSE.UI.Root;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private static readonly Color CuePivotCentroidTint = new(0.35f, 0.70f, 1.00f, 1f);
        private static readonly Color CuePivotOffsetTint   = new(1.00f, 0.70f, 0.25f, 1f);

        private void DrawCuePivotGizmos()
        {
            if (_pkg == null || _stepIds == null) return;
            if (_stepFilterIdx <= 0 || _stepFilterIdx >= _stepIds.Length) return;

            var step = FindStep(_stepIds[_stepFilterIdx]);
            if (step == null) return;

            int cueOrdinal = 0;

            // Step-scoped cues (legacy payload) — host is whatever the cue
            // itself points at via targetPartGroupId / targetPartIds.
            if (step.animationCues?.cues != null)
                foreach (var cue in step.animationCues.cues)
                    TryDrawPivotGizmo(cue, step, hostPartGroupId: null, hostPartId: null, ref cueOrdinal);

            // Host-owned cues: the owning partGroup/part/tool IS the host.
            // The cue entry's targetPartGroupId / targetPartIds may be
            // empty (common — authors rely on ownership) so we pass the
            // owner id down explicitly; the runtime does the same thing
            // via ResolveHostedPartGroupContext.
            if (_pkg.partGroups != null)
            {
                foreach (var sub in _pkg.partGroups)
                {
                    if (sub?.animationCues == null) continue;
                    foreach (var cue in sub.animationCues)
                        if (CueAppliesToStep(cue, step))
                            TryDrawPivotGizmo(cue, step, hostPartGroupId: sub.id, hostPartId: null, ref cueOrdinal);
                }
            }
            if (_pkg.parts != null)
            {
                foreach (var part in _pkg.parts)
                {
                    if (part?.animationCues == null) continue;
                    foreach (var cue in part.animationCues)
                        if (CueAppliesToStep(cue, step))
                            TryDrawPivotGizmo(cue, step, hostPartGroupId: null, hostPartId: part.id, ref cueOrdinal);
                }
            }
            if (_pkg.tools != null)
            {
                foreach (var tool in _pkg.tools)
                {
                    if (tool?.animationCues == null) continue;
                    foreach (var cue in tool.animationCues)
                        if (CueAppliesToStep(cue, step))
                            TryDrawPivotGizmo(cue, step, hostPartGroupId: null, hostPartId: null, ref cueOrdinal);
                }
            }
        }

        private static bool CueAppliesToStep(AnimationCueEntry cue, StepDefinition step)
        {
            if (cue == null || step == null) return false;
            if (cue.stepIds == null || cue.stepIds.Length == 0) return true;
            for (int i = 0; i < cue.stepIds.Length; i++)
                if (string.Equals(cue.stepIds[i], step.id, StringComparison.Ordinal)) return true;
            return false;
        }

        private void TryDrawPivotGizmo(AnimationCueEntry cue, StepDefinition step,
            string hostPartGroupId, string hostPartId, ref int cueOrdinal)
        {
            cueOrdinal++;
            if (cue == null) return;
            if (!IsPivotCapable(cue.type)) return;
            if (!TryResolveCueHostRoot(cue, step, hostPartGroupId, hostPartId,
                    out Transform hostRoot, out Vector3? defaultPivotLocal))
                return;

            int ordinal = cueOrdinal;

            // Centroid gizmo — always drawn. Rendered with zTest=Always so
            // it's visible even when the author has the camera inside the
            // group mesh (common when framing on a carriage). Three layers:
            //   1. camera-facing filled disc (catches the eye)
            //   2. small wire sphere (conveys 3D position)
            //   3. axis-aligned cross (readable in any view angle)
            // Null centroid means "no body yet" — fall back to host origin
            // and label the uncertainty so authors can tell the difference.
            Vector3 centroidLocal = defaultPivotLocal ?? Vector3.zero;
            Vector3 centroidWorld = hostRoot.TransformPoint(centroidLocal);
            Quaternion handleRot  = hostRoot.rotation;

            var prevZ = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            using (new Handles.DrawingScope(CuePivotCentroidTint))
            {
                float handleSize = HandleUtility.GetHandleSize(centroidWorld);
                float radius     = handleSize * 0.12f;
                Vector3 cam      = SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null
                                     ? SceneView.lastActiveSceneView.camera.transform.forward
                                     : Vector3.forward;
                Handles.DrawSolidDisc(centroidWorld, -cam, radius * 0.7f);
                Handles.DrawWireDisc(centroidWorld, Vector3.up,      radius);
                Handles.DrawWireDisc(centroidWorld, Vector3.right,   radius);
                Handles.DrawWireDisc(centroidWorld, Vector3.forward, radius);
                Handles.DrawLine(centroidWorld - Vector3.up      * radius, centroidWorld + Vector3.up      * radius);
                Handles.DrawLine(centroidWorld - Vector3.right   * radius, centroidWorld + Vector3.right   * radius);
                Handles.DrawLine(centroidWorld - Vector3.forward * radius, centroidWorld + Vector3.forward * radius);
                string label = defaultPivotLocal.HasValue
                    ? $"cue {ordinal} · {cue.type} centroid"
                    : $"cue {ordinal} · {cue.type} (no body yet · origin)";
                Handles.Label(centroidWorld + Vector3.up * (radius + 0.01f),
                    label, EditorStyles.miniLabel);
            }

            Handles.zTest = prevZ;

            // Offset handle — only when override is on.
            if (!cue.pivotOffsetOverride) return;

            Vector3 offsetLocal    = new(cue.pivotOffset.x, cue.pivotOffset.y, cue.pivotOffset.z);
            Vector3 effectiveLocal = centroidLocal + offsetLocal;
            Vector3 effectiveWorld = hostRoot.TransformPoint(effectiveLocal);

            using (new Handles.DrawingScope(CuePivotOffsetTint))
            {
                Handles.SphereHandleCap(0, effectiveWorld, handleRot,
                    HandleUtility.GetHandleSize(effectiveWorld) * 0.1f, EventType.Repaint);
                Handles.Label(effectiveWorld + Vector3.up * 0.025f,
                    $"cue {ordinal} · pivot override",
                    EditorStyles.miniBoldLabel);
            }

            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.PositionHandle(effectiveWorld, handleRot);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 newLocal  = hostRoot.InverseTransformPoint(newWorld);
                Vector3 newOffset = newLocal - centroidLocal;
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

        private static bool IsPivotCapable(string cueType)
        {
            return string.Equals(cueType, "poseTransition", StringComparison.Ordinal)
                || string.Equals(cueType, "orientPartGroup", StringComparison.Ordinal)
                || string.Equals(cueType, "particle", StringComparison.Ordinal)
                || string.Equals(cueType, "transform", StringComparison.Ordinal);
        }

        /// <summary>
        /// Resolves the host transform + default pivot (in host-local space)
        /// for a cue. PartGroup-owned cues often leave
        /// <c>targetPartGroupId</c> empty (the host IS the owning
        /// partGroup); this overload accepts explicit host-owner ids so
        /// the gizmo resolves hosts the same way the runtime does.
        /// <para>PartGroup host → centroid from
        /// <see cref="PivotCentroidResolver"/> (single source of truth). Returns null
        /// for <paramref name="defaultPivotLocal"/> when the resolver has no
        /// body members yet (first step introducing the group).</para>
        /// <para>Part host → mesh origin (zero).</para>
        /// </summary>
        private bool TryResolveCueHostRoot(AnimationCueEntry cue, StepDefinition step,
                                           string hostPartGroupId,
                                           string hostPartId,
                                           out Transform hostRoot,
                                           out Vector3? defaultPivotLocal)
        {
            hostRoot = null;
            defaultPivotLocal = null;
            if (cue == null) return false;

            // Prefer explicit owner context (host-owned cue) over the
            // cue's own target fields, which may be empty.
            string subId = !string.IsNullOrEmpty(hostPartGroupId)
                ? hostPartGroupId
                : cue.targetPartGroupId;

            if (!string.IsNullOrEmpty(subId)
                && _partGroupRootGOs != null
                && _partGroupRootGOs.TryGetValue(subId, out var groupGO)
                && groupGO != null)
            {
                hostRoot = groupGO.transform;
                defaultPivotLocal = PivotCentroidResolver.ComputeBodyCentroidLocal(hostRoot, _pkg, step);
                return true;
            }

            // Part host: explicit owner first, else first targetPartId.
            string pid = !string.IsNullOrEmpty(hostPartId)
                ? hostPartId
                : (cue.targetPartIds != null && cue.targetPartIds.Length > 0 ? cue.targetPartIds[0] : null);

            if (!string.IsNullOrEmpty(pid))
            {
                var partGO = FindLivePartGO(pid);
                if (partGO != null)
                {
                    hostRoot = partGO.transform;
                    defaultPivotLocal = Vector3.zero;
                    return true;
                }
            }

            // Fallback: iterate remaining targetPartIds until one resolves.
            if (cue.targetPartIds != null)
            {
                foreach (var tpid in cue.targetPartIds)
                {
                    if (string.IsNullOrEmpty(tpid)) continue;
                    var partGO = FindLivePartGO(tpid);
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
    }
}
