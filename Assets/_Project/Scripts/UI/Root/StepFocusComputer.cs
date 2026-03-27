using System;
using System.Collections.Generic;
using System.Reflection;
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

            // Always include target positions from previewConfig — not just as a fallback.
            if (step.targetIds != null && step.targetIds.Length > 0)
            {
                Transform previewRoot = GetPreviewRoot();
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(step.targetIds[i]);
                    if (targetPlacement == null)
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            // Also include requiredToolActions target positions.
            if (step.requiredToolActions != null)
            {
                Transform previewRoot = GetPreviewRoot();
                for (int i = 0; i < step.requiredToolActions.Length; i++)
                {
                    ToolActionDefinition action = step.requiredToolActions[i];
                    if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                        continue;

                    TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(action.targetId);
                    if (targetPlacement == null)
                        continue;

                    fallbackTargetCount++;
                    Vector3 localPos = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                    Vector3 worldPos = previewRoot != null ? previewRoot.TransformPoint(localPos) : localPos;
                    Encapsulate(new Bounds(worldPos, Vector3.one * 0.08f));
                }
            }

            // Enforce a minimum bounds size so the camera gives a "third person"
            // overview with enough surrounding context visible.
            if (hasBounds)
            {
                const float minSize = 1.0f;
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

            if (!string.IsNullOrWhiteSpace(step.subassemblyId))
            {
                StepDefinition[] allSteps = package.GetOrderedSteps();
                for (int i = 0; i < allSteps.Length; i++)
                {
                    StepDefinition candidate = allSteps[i];
                    if (candidate == null ||
                        !string.Equals(candidate.subassemblyId, step.subassemblyId, StringComparison.OrdinalIgnoreCase) ||
                        candidate.requiredPartIds == null)
                    {
                        continue;
                    }

                    for (int p = 0; p < candidate.requiredPartIds.Length; p++)
                    {
                        string partId = candidate.requiredPartIds[p];
                        if (!string.IsNullOrWhiteSpace(partId))
                            results.Add(partId);
                    }
                }

                return;
            }

            if (step.requiredPartIds == null)
                return;

            for (int i = 0; i < step.requiredPartIds.Length; i++)
            {
                string partId = step.requiredPartIds[i];
                if (!string.IsNullOrWhiteSpace(partId))
                    results.Add(partId);
            }
        }

        private static bool TryInvokeCameraMethod(string methodName, object[] args, params Type[] signature)
        {
            Camera cam = Camera.main;
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
    }
}
