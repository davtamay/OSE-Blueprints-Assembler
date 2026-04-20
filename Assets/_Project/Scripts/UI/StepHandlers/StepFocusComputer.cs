using System;
using System.Collections.Generic;
using System.Reflection;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Computes camera focus bounds for a step and invokes camera framing.
    /// Extracted from PartInteractionBridge (Phase 3).
    /// </summary>
    internal sealed class StepFocusComputer
    {
        private readonly IBridgeContext _ctx;

        private string _lastCameraFramedStepId;
        private float _lastCameraFramedTime;

        public StepFocusComputer(IBridgeContext context)
        {
            _ctx = context;
        }

        public void FocusCameraOnStepArea(string stepId, bool resetToDefaultView = false)
        {
            // When orchestrator owns interaction, camera framing is handled by StepGuidanceService.
            if (_ctx.IsExternalControlEnabled)
                return;

            // Debounce: skip if the same step was already framed within 0.5s.
            float now = Time.unscaledTime;
            if (string.Equals(_lastCameraFramedStepId, stepId, StringComparison.Ordinal)
                && now - _lastCameraFramedTime < 0.5f)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(stepId) || !TryResolveStepFocusBounds(stepId, out Bounds focusBounds))
            {
                OseLog.Info($"[FocusCamera] Step '{stepId}' — no bounds resolved, skipping.");
                return;
            }

            _lastCameraFramedStepId = stepId;
            _lastCameraFramedTime = now;

            focusBounds.Expand(new Vector3(0.18f, 0.12f, 0.18f));
            OseLog.Info($"[FocusCamera] Step '{stepId}' — bounds center={focusBounds.center}, size={focusBounds.size}");

            if (resetToDefaultView)
                TryInvokeCameraMethod("ResetToDefault", Array.Empty<object>());

            if (TryInvokeCameraMethod("FrameBounds", new object[] { focusBounds }, typeof(Bounds)))
            {
                OseLog.Info($"[FocusCamera] Step '{stepId}' — FrameBounds applied.");
                return;
            }

            OseLog.Info($"[FocusCamera] Step '{stepId}' — FrameBounds failed, falling back to FocusOn.");
            TryInvokeCameraMethod("FocusOn", new object[] { focusBounds.center, -1f }, typeof(Vector3), typeof(float));
            TryInvokeCameraMethod("FocusOn", new object[] { focusBounds.center }, typeof(Vector3));
        }

        public bool TryResolveStepFocusBounds(string stepId, out Bounds bounds)
        {
            bounds = default;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out StepDefinition step) || step == null)
                return false;

            Bounds accumulatedBounds = default;
            bool hasBounds = false;
            int previewCount = 0, partCount = 0, toolTargetCount = 0, fallbackTargetCount = 0;

            void Encapsulate(Bounds candidate)
            {
                if (!hasBounds)
                {
                    accumulatedBounds = candidate;
                    hasBounds = true;
                    return;
                }

                accumulatedBounds.Encapsulate(candidate);
            }

            var spawnedPreviews = _ctx.PreviewManager.SpawnedPreviews;
            for (int i = 0; i < spawnedPreviews.Count; i++)
            {
                GameObject preview = spawnedPreviews[i];
                if (preview == null)
                    continue;

                previewCount++;
                if (PreviewSpawnManager.TryGetRenderableBounds(preview, out Bounds previewBounds))
                    Encapsulate(previewBounds);
                else
                    Encapsulate(new Bounds(preview.transform.position, Vector3.one * 0.08f));
            }

            HashSet<string> focusPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectStepFocusPartIds(package, step, focusPartIds);

            foreach (string partId in focusPartIds)
            {
                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo == null || !partGo.activeInHierarchy)
                    continue;

                partCount++;
                if (PreviewSpawnManager.TryGetRenderableBounds(partGo, out Bounds partBounds))
                    Encapsulate(partBounds);
                else
                    Encapsulate(new Bounds(partGo.transform.position, Vector3.one * 0.08f));
            }

            var subCtrl = _ctx.SubassemblyController;
            if (step.RequiresSubassemblyPlacement &&
                subCtrl != null &&
                subCtrl.TryGetProxy(step.requiredSubassemblyId, out GameObject proxy) &&
                proxy != null)
            {
                if (PreviewSpawnManager.TryGetRenderableBounds(proxy, out Bounds proxyBounds))
                    Encapsulate(proxyBounds);
                else
                    Encapsulate(new Bounds(proxy.transform.position, Vector3.one * 0.18f));
            }

            var useHandler = _ctx.UseHandler;
            if (useHandler != null && useHandler.TryGetSpawnedTargetBounds(out Bounds toolTargetBounds))
            {
                toolTargetCount++;
                Encapsulate(toolTargetBounds);
            }

            // Include target positions from previewConfig ONLY when the spawned
            // tool-target markers don't already cover them — otherwise it's a
            // duplicate source of truth and unauthored (0,0,0) placements
            // would yank the camera to world origin below the assembly.
            if (toolTargetCount == 0 && step.targetIds != null && step.targetIds.Length > 0)
            {
                Transform previewRoot = GetPreviewRoot();
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(step.targetIds[i]);
                    if (targetPlacement == null || IsZeroPosition(targetPlacement.position))
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            if (toolTargetCount == 0 && step.requiredToolActions != null)
            {
                Transform previewRoot = GetPreviewRoot();
                for (int i = 0; i < step.requiredToolActions.Length; i++)
                {
                    ToolActionDefinition action = step.requiredToolActions[i];
                    if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                        continue;

                    TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(action.targetId);
                    if (targetPlacement == null || IsZeroPosition(targetPlacement.position))
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            // Re-center the bounds on the active cursor tool-action target
            // when present. FrameBounds sets pivot to bounds.center — if the
            // accumulated box averages across multiple context parts, the
            // pivot drifts off the target and the user sees "camera points
            // somewhere near the target, not at it." Building a box centered
            // ON the target that still fully contains the accumulated content
            // keeps the context in-frame while locking the pivot to the
            // work point.
            if (hasBounds && TryGetActiveToolTargetWorldPosition(step, out Vector3 activeTargetPos))
            {
                Vector3 posOffset = activeTargetPos - accumulatedBounds.min;
                Vector3 negOffset = accumulatedBounds.max - activeTargetPos;
                Vector3 halfExtents = Vector3.Max(
                    new Vector3(Mathf.Abs(posOffset.x), Mathf.Abs(posOffset.y), Mathf.Abs(posOffset.z)),
                    new Vector3(Mathf.Abs(negOffset.x), Mathf.Abs(negOffset.y), Mathf.Abs(negOffset.z)));
                accumulatedBounds = new Bounds(activeTargetPos, halfExtents * 2f);
            }

            // Enforce a minimum bounds size so tiny parts (single bolts, etc.)
            // still get a readable frame rather than an extreme close-up.
            // FrameBounds already applies a 1.35× FOV padding and the caller
            // adds Expand(0.18, 0.12, 0.18) — so only a fixed floor is needed here.
            // The previous contentExtent * 2f multiplier was triple-stacking margin
            // and caused the camera to pull far back on multi-part subassemblies.
            if (hasBounds)
            {
                const float minSize = 0.15f;
                Vector3 size = accumulatedBounds.size;
                size.x = Mathf.Max(size.x, minSize);
                size.y = Mathf.Max(size.y, minSize);
                size.z = Mathf.Max(size.z, minSize);
                accumulatedBounds.size = size;
            }

            OseLog.Info($"[FocusBounds] Step '{stepId}' — previews={previewCount}, parts={partCount}/{focusPartIds.Count}, toolTargets={toolTargetCount}, fallbackTargets={fallbackTargetCount}, hasBounds={hasBounds}");

            if (hasBounds)
                bounds = accumulatedBounds;

            return hasBounds;
        }

        internal static void CollectStepFocusPartIds(MachinePackageDefinition package, StepDefinition step, HashSet<string> results)
        {
            if (package == null || step == null || results == null)
                return;

            // Prefer the TaskCursor's open tasks when available. Mirrors the
            // cursor-gate invariant already applied to tray emission, ghosts,
            // and hints: "required this step" telegraphs must scope to what
            // the trainee has to touch RIGHT NOW, not every part in the
            // subassembly. Without this, the camera frame encapsulates parts
            // still sitting in the staging tray and the centroid drops below
            // the actual tool target. Fall back to step.requiredPartIds when
            // no cursor is available (bootstrap, session teardown, etc.).
            bool fromCursor = TryCollectFromCursor(results);

            if (!fromCursor)
            {
                string[] stepParts = step.GetEffectiveRequiredPartIds();
                for (int i = 0; i < stepParts.Length; i++)
                {
                    string partId = stepParts[i];
                    if (!string.IsNullOrWhiteSpace(partId))
                        results.Add(partId);
                }
            }

            // Always supplement with the parts associated with this step's
            // targets. Use steps typically have no requiredPartIds (they act
            // on existing structure), so without this the scope collapses to
            // just the tool-target marker positions — a tight cluster that
            // makes the camera zoom past the surrounding geometry. The
            // associatedPartId on each target is the bracket / bolt / frame
            // piece being acted upon, which is exactly the context the
            // trainee needs in frame.
            AddTargetAssociatedParts(package, step, results);

            // Visual-context parts: Confirm/Use steps that author
            // visualPartIds explicitly are saying "look at these." Pull them
            // in too so a "check every carriage half" QC step centers on the
            // halves rather than falling through to the subassembly-member
            // fallback (which might span more than the author intended).
            if (step.visualPartIds != null)
            {
                for (int i = 0; i < step.visualPartIds.Length; i++)
                {
                    string pid = step.visualPartIds[i];
                    if (!string.IsNullOrWhiteSpace(pid)) results.Add(pid);
                }
            }

            // Final fallback: step.subassemblyId members. Some Confirm / Use
            // steps describe an action that applies to "the whole group" with
            // no explicit part list — e.g. step 52 "QC: verify all carriage
            // holes are clean" has empty requiredPartIds / targets / task
            // parts and only lives under subassembly_carriage_batch_all. With
            // nothing else to anchor the camera, framing falls through to
            // whatever the previous step framed (often the 3D printer frame
            // from an earlier assembly stage) instead of the carriages the
            // trainee is inspecting.
            if (results.Count == 0 && !string.IsNullOrWhiteSpace(step.subassemblyId))
                AddSubassemblyMembers(package, step.subassemblyId, results);
        }

        /// <summary>
        /// Recursively walks a subassembly id and adds every member partId to
        /// <paramref name="results"/>. Aggregates (batch-all-carriages) are
        /// expanded through <see cref="SubassemblyDefinition.memberSubassemblyIds"/>
        /// so the fallback catches every relevant carriage half, not just
        /// the top-level entry.
        /// </summary>
        private static void AddSubassemblyMembers(
            MachinePackageDefinition package, string subassemblyId, HashSet<string> results)
        {
            if (string.IsNullOrEmpty(subassemblyId)) return;
            if (!package.TryGetSubassembly(subassemblyId, out var sub) || sub == null) return;

            if (sub.partIds != null)
            {
                for (int i = 0; i < sub.partIds.Length; i++)
                {
                    string pid = sub.partIds[i];
                    if (!string.IsNullOrWhiteSpace(pid)) results.Add(pid);
                }
            }

            if (sub.isAggregate && sub.memberSubassemblyIds != null)
            {
                for (int i = 0; i < sub.memberSubassemblyIds.Length; i++)
                {
                    string childId = sub.memberSubassemblyIds[i];
                    if (!string.IsNullOrWhiteSpace(childId))
                        AddSubassemblyMembers(package, childId, results);
                }
            }
        }

        private static void AddTargetAssociatedParts(MachinePackageDefinition package, StepDefinition step, HashSet<string> results)
        {
            if (step.targetIds != null)
            {
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    string tid = step.targetIds[i];
                    if (string.IsNullOrWhiteSpace(tid)) continue;
                    if (!package.TryGetTarget(tid, out var target) || target == null) continue;
                    if (!string.IsNullOrWhiteSpace(target.associatedPartId))
                        results.Add(target.associatedPartId);
                }
            }

            if (step.requiredToolActions != null)
            {
                for (int i = 0; i < step.requiredToolActions.Length; i++)
                {
                    var action = step.requiredToolActions[i];
                    if (action == null || string.IsNullOrWhiteSpace(action.targetId)) continue;
                    if (!package.TryGetTarget(action.targetId, out var target) || target == null) continue;
                    if (!string.IsNullOrWhiteSpace(target.associatedPartId))
                        results.Add(target.associatedPartId);
                }
            }
        }

        /// <summary>
        /// Resolves the WORLD position of the tool-action target currently
        /// open in the TaskCursor. Returns false if no cursor, no toolAction
        /// entry, or the target placement can't be found. Used to re-anchor
        /// camera framing to the active work point on Use steps.
        /// </summary>
        private bool TryGetActiveToolTargetWorldPosition(StepDefinition step, out Vector3 worldPos)
        {
            worldPos = default;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;
            var cursor = session?.AssemblyController?.StepController?.CurrentTaskCursor;
            if (cursor == null || cursor.IsComplete)
                return false;

            string activeActionId = null;
            foreach (var entry in cursor.OpenTasks)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (!string.Equals(entry.kind, "toolAction", StringComparison.Ordinal)) continue;
                activeActionId = entry.id;
                break;
            }
            if (string.IsNullOrEmpty(activeActionId))
                return false;

            string activeTargetId = null;
            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                var action = step.requiredToolActions[i];
                if (action == null) continue;
                if (!string.Equals(action.id, activeActionId, StringComparison.Ordinal)) continue;
                activeTargetId = action.targetId;
                break;
            }
            if (string.IsNullOrEmpty(activeTargetId))
                return false;

            TargetPreviewPlacement placement = _ctx.Spawner?.FindTargetPlacement(activeTargetId);
            if (placement == null || IsZeroPosition(placement.position))
                return false;

            Vector3 localPos = new Vector3(placement.position.x, placement.position.y, placement.position.z);
            Transform previewRoot = GetPreviewRoot();
            worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
            return true;
        }

        private static bool TryCollectFromCursor(HashSet<string> results)
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;
            var cursor = session?.AssemblyController?.StepController?.CurrentTaskCursor;
            if (cursor == null || cursor.IsComplete)
                return false;

            bool any = false;
            foreach (var entry in cursor.OpenTasks)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (!string.Equals(entry.kind, "part", StringComparison.Ordinal)) continue;
                string partId = TaskInstanceId.ToPartId(entry.id);
                if (string.IsNullOrWhiteSpace(partId)) continue;
                results.Add(partId);
                any = true;
            }
            return any;
        }

        private static bool TryInvokeCameraMethod(string methodName, object[] args, params Type[] signature)
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null)
                return false;

            Transform current = cam.transform;
            while (current != null)
            {
                Component[] components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    MethodInfo method = component.GetType().GetMethod(
                        methodName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: signature,
                        modifiers: null);

                    if (method == null)
                        continue;

                    try
                    {
                        method.Invoke(component, args);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        string message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        OseLog.Warn($"[PartInteraction] Camera method '{methodName}' failed on '{component.GetType().Name}': {message}");
                    }
                }

                current = current.parent;
            }

            return false;
        }

        private Transform GetPreviewRoot()
        {
            var setup = _ctx.Setup;
            return setup != null ? setup.PreviewRoot : null;
        }

        // Unauthored placements deserialise as all-zero SceneFloat3 — treat
        // that as "no position" rather than "at world origin." A real target
        // at literal (0,0,0) is vanishingly rare in practice and not worth
        // the camera jumping to the floor when the placement is missing.
        private static bool IsZeroPosition(SceneFloat3 pos)
            => pos.x == 0f && pos.y == 0f && pos.z == 0f;
    }
}
