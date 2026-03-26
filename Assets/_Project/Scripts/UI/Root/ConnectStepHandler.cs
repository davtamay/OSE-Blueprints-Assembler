using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Connect"/> (pipe_connection) steps.
    /// Spawns clickable port spheres at portA/portB positions and a cable preview.
    /// A two-click confirmation (one sphere, then the other) completes
    /// the step and renders the final pipe spline.
    /// </summary>
    internal sealed class ConnectStepHandler : IStepFamilyHandler
    {
        private readonly PackagePartSpawner _spawner;
        private readonly Func<PreviewSceneSetup> _getSetup;
        private readonly Func<ToolCursorManager> _getCursorManager;
        private readonly Func<string, GameObject> _findSpawnedPart;

        private readonly List<GameObject> _spawnedPortSpheres = new();
        public bool HasActivePortSpheres => _spawnedPortSpheres.Count > 0;
        private readonly List<GameObject> _cablePreviews = new();
        private readonly List<GameObject> _renderedPipeSplines = new();
        private bool _pipePortAConfirmed;
        private AnchorToAnchorInteraction _anchorInteraction;
        private Vector3 _portAWorldPos;
        private Vector3 _portBWorldPos;

        private const float ScreenProximityDesktop = 120f;
        private const float ScreenProximityMobile  = 180f;

        public ConnectStepHandler(
            PackagePartSpawner spawner,
            Func<PreviewSceneSetup> getSetup,
            Func<ToolCursorManager> getCursorManager,
            Func<string, GameObject> findSpawnedPart)
        {
            _spawner          = spawner;
            _getSetup         = getSetup;
            _getCursorManager = getCursorManager;
            _findSpawnedPart  = findSpawnedPart;
        }

        public void OnStepActivated(in StepHandlerContext context)
        {
            ClearPortSpheres();
            ClearCablePreviews();
            CleanupAnchorInteraction();

            var package = _spawner?.CurrentPackage;
            if (package == null) return;

            SpawnPortSpheresForStep(package, context.Step);
        }

        public bool TryHandlePointerAction(in StepHandlerContext context)
        {
            // Connect steps use pointer-down (port sphere clicks), not the
            // confirm / tool-primary canonical action.
            return false;
        }

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            _anchorInteraction?.Tick();
        }

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos)
        {
            if (_spawnedPortSpheres.Count == 0)
                return false;

            StepDefinition step = context.Step;
            if (!step.IsPipeConnection)
                return false;

            GameObject hitGo = FindNearestPortSphereByScreenProximity(screenPos);
            if (hitGo == null)
                return false;

            if (!_pipePortAConfirmed)
            {
                _pipePortAConfirmed = true;
                SetPortSphereConfirmed(hitGo);

                // Start anchor-to-anchor interaction with a live cable visual.
                bool isPortA = hitGo.name.Contains("_A");
                Vector3 anchorA = isPortA ? _portAWorldPos : _portBWorldPos;
                Vector3 anchorB = isPortA ? _portBWorldPos : _portAWorldPos;
                Color cableColor = new Color(0.2f, 0.2f, 0.2f, 1f);

                _anchorInteraction = new AnchorToAnchorInteraction(new AnchorToAnchorInteraction.Config
                {
                    AnchorA = anchorA,
                    AnchorB = anchorB,
                    NearBScreenThreshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop,
                    LiveVisualFactory = (a, b) => CableLineVisual.Spawn(a, b, cableColor),
                    ResultVisualFactory = (a, b) => CableLineVisual.Spawn(a, b, cableColor),
                });
                _anchorInteraction.StartFromAnchor();

                OseLog.Info($"[ConnectStepHandler] First port confirmed ('{hitGo.name}'). Cable follows cursor — click the other port.");
                return true;
            }

            // Second port — accept any unconfirmed sphere.
            if (!IsPortSphereConfirmed(hitGo))
            {
                SetPortSphereConfirmed(hitGo);

                // Complete the anchor interaction (snaps cable to exact A→B).
                if (_anchorInteraction != null && _anchorInteraction.IsActive)
                    _anchorInteraction.TryCompleteAtAnchor(hitGo.transform.position, forceComplete: true);

                OseLog.Info("[ConnectStepHandler] Second port confirmed. Completing step.");
                context.StepController.CompleteStep(context.ElapsedSeconds);
                return true;
            }

            return true; // consumed — clicked an already-confirmed sphere
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            TryRenderPipeSpline(context.StepId);
            ClearPortSpheres();
            ClearCablePreviews();
            CleanupAnchorInteraction();
        }

        public void ClearTransientVisuals()
        {
            ClearPortSpheres();
            ClearCablePreviews();
            CleanupAnchorInteraction();
        }

        public void Cleanup()
        {
            ClearTransientVisuals();
            ClearRenderedPipeSplines();
        }

        // ── Port-sphere spawning ──

        private void SpawnPortSpheresForStep(MachinePackageDefinition package, StepDefinition step)
        {
            PreviewSceneSetup setup = _getSetup();
            if (setup == null) return;
            Transform previewRoot = setup.PreviewRoot;
            if (previewRoot == null) return;

            string[] targetIds = step.targetIds;
            if (targetIds == null) return;

            foreach (string targetId in targetIds)
            {
                TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
                if (tp == null)
                {
                    OseLog.Warn($"[ConnectStepHandler] No target placement for '{targetId}'.");
                    continue;
                }

                Vector3 portAPos = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                Vector3 portBPos = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);

                if (portAPos == Vector3.zero && portBPos == Vector3.zero)
                {
                    OseLog.Warn($"[ConnectStepHandler] Target '{targetId}' has no portA/portB. Using fallback offset.");
                    Vector3 c = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                    portAPos = c + new Vector3(-0.12f, 0.06f, 0f);
                    portBPos = c + new Vector3( 0.12f, 0.06f, 0f);
                }

                // Cache world positions for the anchor interaction.
                _portAWorldPos = previewRoot.TransformPoint(portAPos);
                _portBWorldPos = previewRoot.TransformPoint(portBPos);

                SpawnPortSphere(portAPos, isPortA: true, previewRoot);
                SpawnPortSphere(portBPos, isPortA: false, previewRoot);

                // Cable preview — shows the connection path while the user taps the ports.
                string partName = (step.requiredPartIds?.Length > 0) ? step.requiredPartIds[0] : targetId;
                var previewPath = new SplinePathDefinition
                {
                    radius     = 0.018f,
                    segments   = 8,
                    metallic   = 0f,
                    smoothness = 0.25f,
                    knots = new SceneFloat3[]
                    {
                        new SceneFloat3 { x = portAPos.x, y = portAPos.y, z = portAPos.z },
                        new SceneFloat3 { x = (portAPos.x + portBPos.x) * 0.5f,
                                          y = Mathf.Min(portAPos.y, portBPos.y) - 0.04f,
                                          z = (portAPos.z + portBPos.z) * 0.5f },
                        new SceneFloat3 { x = portBPos.x, y = portBPos.y, z = portBPos.z },
                    }
                };
                GameObject cablePreview = SplinePartFactory.CreatePreview(partName, previewPath, previewRoot);
                if (cablePreview != null)
                {
                    MaterialHelper.ApplyPreviewMaterial(cablePreview);
                    _cablePreviews.Add(cablePreview);
                }
            }

            OseLog.Info($"[ConnectStepHandler] Pipe step '{step.id}': spawned {_spawnedPortSpheres.Count} port sphere(s).");

            // Spawn a cable preview on the cursor.
            SpawnPipeCursorPreview(package, step);
        }

        private void SpawnPortSphere(Vector3 localPos, bool isPortA, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = isPortA ? "PortSphere_A" : "PortSphere_B";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.12f;

            // Trigger — doesn't block part raycasts; pipe taps use screen-proximity only.
            var col = go.GetComponent<SphereCollider>();
            if (col != null) col.isTrigger = true;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Color c = isPortA
                    ? new Color(1.00f, 0.18f, 0.18f, 1f)
                    : new Color(0.18f, 0.50f, 1.00f, 1f);

                var shader = Shader.Find("OSE/PortSphereOnTop")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit");

                var mat = shader != null ? new Material(shader) : new Material(mr.sharedMaterial);
                mat.name = go.name + "_Mat";

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     c);

                mr.sharedMaterial = mat;
            }

            _spawnedPortSpheres.Add(go);
        }

        // ── Screen-proximity hit detection ──

        private GameObject FindNearestPortSphereByScreenProximity(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return null;

            float threshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop;
            float closestDist = threshold;
            GameObject closest = null;

            for (int i = 0; i < _spawnedPortSpheres.Count; i++)
            {
                GameObject sphere = _spawnedPortSpheres[i];
                if (sphere == null) continue;

                Vector3 sp = cam.WorldToScreenPoint(sphere.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = sphere;
                }
            }
            return closest;
        }

        // ── Port-sphere visual state ──

        private static bool IsPortSphereConfirmed(GameObject sphere)
        {
            var mr = sphere?.GetComponent<MeshRenderer>();
            if (mr?.sharedMaterial == null) return false;
            Color c = mr.sharedMaterial.GetColor("_BaseColor");
            return c.g > 0.9f && c.r < 0.5f;
        }

        private static void SetPortSphereConfirmed(GameObject sphere)
        {
            var mr = sphere.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mat = mr.material;
            if (mat == null) return;
            Color green = new Color(0.25f, 1.00f, 0.35f, 1f);
            mat.SetColor("_BaseColor", green);
            mat.SetColor("_EmissionColor", green * 0.6f);
        }

        // ── Pipe spline rendering ──

        private void TryRenderPipeSpline(string stepId)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null) return;

            PreviewSceneSetup setup = _getSetup();
            if (setup == null) return;

            if (!package.TryGetStep(stepId, out var step)) return;
            if (!step.IsPipeConnection) return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0) return;

            Transform previewRoot = setup.PreviewRoot;
            if (previewRoot == null) return;

            foreach (string targetId in targetIds)
            {
                TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
                if (tp == null) continue;

                Vector3 portAPos = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                Vector3 portBPos = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);
                if (portAPos == Vector3.zero && portBPos == Vector3.zero) continue;

                float midX = (portAPos.x + portBPos.x) * 0.5f;
                float midY = Mathf.Min(portAPos.y, portBPos.y) - 0.04f;
                float midZ = (portAPos.z + portBPos.z) * 0.5f;

                var path = new SplinePathDefinition
                {
                    radius     = 0.018f,
                    segments   = 8,
                    metallic   = 0f,
                    smoothness = 0.25f,
                    knots = new SceneFloat3[]
                    {
                        new SceneFloat3 { x = portAPos.x, y = portAPos.y, z = portAPos.z },
                        new SceneFloat3 { x = midX,       y = midY,       z = midZ       },
                        new SceneFloat3 { x = portBPos.x, y = portBPos.y, z = portBPos.z },
                    }
                };

                string partName = (step.requiredPartIds?.Length > 0) ? step.requiredPartIds[0] : targetId;
                Color hoseColor = new Color(0.15f, 0.15f, 0.15f, 1f);

                GameObject splineGo = SplinePartFactory.Create(partName, path, hoseColor, previewRoot);
                if (splineGo != null)
                {
                    MaterialHelper.MarkAsImported(splineGo);
                    _renderedPipeSplines.Add(splineGo);
                    OseLog.Info($"[ConnectStepHandler] Rendered pipe spline for '{partName}'.");
                }
            }
        }

        // ── Cleanup ──

        private void ClearPortSpheres()
        {
            foreach (var s in _spawnedPortSpheres)
            {
                if (s != null) UnityEngine.Object.Destroy(s);
            }
            _spawnedPortSpheres.Clear();
            _pipePortAConfirmed = false;

            var cursorManager = _getCursorManager();
            cursorManager?.ClearPipeCursorPreview();
        }

        private void ClearCablePreviews()
        {
            foreach (var g in _cablePreviews)
            {
                if (g != null) UnityEngine.Object.Destroy(g);
            }
            _cablePreviews.Clear();
        }

        private void ClearRenderedPipeSplines()
        {
            foreach (var p in _renderedPipeSplines)
            {
                if (p != null) UnityEngine.Object.Destroy(p);
            }
            _renderedPipeSplines.Clear();
        }

        private void CleanupAnchorInteraction()
        {
            if (_anchorInteraction != null)
            {
                _anchorInteraction.Cleanup();
                _anchorInteraction = null;
            }
        }

        private void SpawnPipeCursorPreview(MachinePackageDefinition package, StepDefinition step)
        {
            var cursorManager = _getCursorManager();
            if (cursorManager != null)
                _ = cursorManager.SpawnPipeCursorPreviewAsync(package, step, _findSpawnedPart, _spawner);
        }
    }
}
