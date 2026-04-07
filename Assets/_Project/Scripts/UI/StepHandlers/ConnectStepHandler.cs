using System.Collections.Generic;
using OSE.App;
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
        private readonly IBridgeContext _ctx;

        private readonly List<GameObject> _spawnedPortSpheres = new();
        private readonly HashSet<GameObject> _confirmedSpheres = new();
        public bool HasActivePortSpheres => _spawnedPortSpheres.Count > 0;
        private readonly List<GameObject> _cablePreviews = new();
        private readonly List<GameObject> _renderedPipeSplines = new();
        private bool _pipePortAConfirmed;
        private AnchorToAnchorInteraction _anchorInteraction;

        // Per-sphere port lookup — keyed by GameObject so multi-wire steps (multiple
        // targetIds) each carry their own portA/portB pair instead of one shared field.
        private readonly Dictionary<GameObject, Vector3> _portAByGo = new();
        private readonly Dictionary<GameObject, Vector3> _portBByGo = new();

        // Per-sphere polarity type — populated for WireConnect profile steps.
        // Value is a polarity token: "+12V", "GND", "signal", etc. Null for Cable profile.
        private readonly Dictionary<GameObject, string> _polarityByGo = new();

        // Per-sphere wire entry — used to read color/width when building the cable visual.
        private readonly Dictionary<GameObject, WireConnectEntry> _wireEntryByGo = new();

        // Material pool — reused across step activations to avoid per-sphere new Material().
        // Populated lazily; all instances destroyed in Cleanup().
        private readonly List<Material> _sphereMatPool = new();
        private Shader _portSphereShader;

        // World positions for the active in-progress anchor interaction.
        private Vector3 _portAWorldPos;
        private Vector3 _portBWorldPos;


        public ConnectStepHandler(IBridgeContext context)
        {
            _ctx = context;
        }

        public void OnStepActivated(in StepHandlerContext context)
        {
            ClearPortSpheres();
            ClearCablePreviews();
            CleanupAnchorInteraction();

            var package = _ctx.Spawner?.CurrentPackage;
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
                // WireConnect polarity-order enforcement: when enforcePortOrder is true,
                // the learner must click portA (the positive/hot side) first.
                // Clicking portB first triggers a warning and rejects the click.
                if (step.ResolvedProfile == StepProfile.WireConnect &&
                    step.wireConnect?.IsConfigured == true &&
                    step.wireConnect.enforcePortOrder &&
                    hitGo.name.Contains("_B"))
                {
                    OseLog.Info("[ConnectStepHandler] WireConnect order violation — click the positive/hot port (red) first.");
                    FlashPortSphereRejected(hitGo);
                    return true; // consumed — don't fall through
                }

                _pipePortAConfirmed = true;
                SetPortSphereConfirmed(hitGo);

                // Resolve port world positions from the per-sphere lookup so multi-wire
                // steps each use the correct pair rather than the last-written shared field.
                bool isPortA = hitGo.name.Contains("_A");
                if (_portAByGo.TryGetValue(hitGo, out Vector3 pairA) &&
                    _portBByGo.TryGetValue(hitGo, out Vector3 pairB))
                {
                    _portAWorldPos = pairA;
                    _portBWorldPos = pairB;
                }

                Vector3 anchorA = isPortA ? _portAWorldPos : _portBWorldPos;
                Vector3 anchorB = isPortA ? _portBWorldPos : _portAWorldPos;

                _wireEntryByGo.TryGetValue(hitGo, out WireConnectEntry hitEntry);
                Color cableColor = ResolveWireColor(hitEntry);
                float cableRadius = ResolveWireRadius(hitEntry);

                _anchorInteraction = new AnchorToAnchorInteraction(new AnchorToAnchorInteraction.Config
                {
                    AnchorA = anchorA,
                    AnchorB = anchorB,
                    NearBScreenThreshold = Application.isMobilePlatform ? StepHandlerConstants.Proximity.MobilePixels : StepHandlerConstants.Proximity.DesktopPixels,
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

        /// <summary>
        /// Re-renders pipe splines for all Connect-family steps in <paramref name="completedSteps"/>.
        /// Call this after jumping forward in the step sequence so that wires from past
        /// Connect steps remain visible in the scene.
        /// </summary>
        public void RenderCompletedWires(StepDefinition[] completedSteps)
        {
            if (completedSteps == null) return;
            foreach (var step in completedSteps)
            {
                if (step == null || !step.IsPipeConnection) continue;
                TryRenderPipeSpline(step.id);
            }
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
            foreach (var mat in _sphereMatPool)
                if (mat != null) UnityEngine.Object.Destroy(mat);
            _sphereMatPool.Clear();
        }

        // ── Port-sphere spawning ──

        private void SpawnPortSpheresForStep(MachinePackageDefinition package, StepDefinition step)
        {
            PreviewSceneSetup setup = _ctx.Setup;
            if (setup == null) return;
            Transform previewRoot = setup.PreviewRoot;
            if (previewRoot == null) return;

            string[] targetIds = step.targetIds;
            if (targetIds == null) return;

            for (int ti = 0; ti < targetIds.Length; ti++)
            {
                string targetId = targetIds[ti];

                // Resolve the wire entry for this target — contains portA/portB, color, radius, and polarity.
                WireConnectEntry wireEntry = ResolveWireEntry(step, targetId, ti);

                // Port positions: wire entry is authoritative; fall back to target placement.
                Vector3 portAPos, portBPos;
                if (wireEntry != null && (wireEntry.portA.x != 0f || wireEntry.portA.y != 0f || wireEntry.portA.z != 0f ||
                                          wireEntry.portB.x != 0f || wireEntry.portB.y != 0f || wireEntry.portB.z != 0f))
                {
                    portAPos = new Vector3(wireEntry.portA.x, wireEntry.portA.y, wireEntry.portA.z);
                    portBPos = new Vector3(wireEntry.portB.x, wireEntry.portB.y, wireEntry.portB.z);
                }
                else
                {
                    TargetPreviewPlacement tp = _ctx.Spawner.FindTargetPlacement(targetId);
                    if (tp == null)
                    {
                        OseLog.Warn($"[ConnectStepHandler] No port positions for '{targetId}' — skipping.");
                        continue;
                    }
                    portAPos = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                    portBPos = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);
                    if (portAPos == Vector3.zero && portBPos == Vector3.zero)
                    {
                        OseLog.Warn($"[ConnectStepHandler] Target '{targetId}' has no portA/portB. Using fallback offset.");
                        Vector3 c = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                        portAPos = c + new Vector3(-0.12f, 0.06f, 0f);
                        portBPos = c + new Vector3( 0.12f, 0.06f, 0f);
                    }
                }
                Color wireColor = ResolveWireColor(wireEntry);
                float wireRadius = ResolveWireRadius(wireEntry);
                string polarityA = wireEntry?.portAPolarityType;
                string polarityB = wireEntry?.portBPolarityType;

                // Per-sphere world-space port pair — stored so multi-wire steps each
                // carry the correct pair regardless of iteration order.
                Vector3 worldA = previewRoot.TransformPoint(portAPos);
                Vector3 worldB = previewRoot.TransformPoint(portBPos);

                GameObject sphereA = SpawnPortSphere(portAPos, isPortA: true,  previewRoot, polarityA);
                GameObject sphereB = SpawnPortSphere(portBPos, isPortA: false, previewRoot, polarityB);

                _portAByGo[sphereA] = worldA;
                _portBByGo[sphereA] = worldB;
                _portAByGo[sphereB] = worldA;
                _portBByGo[sphereB] = worldB;

                if (wireEntry != null) { _wireEntryByGo[sphereA] = wireEntry; _wireEntryByGo[sphereB] = wireEntry; }
                if (polarityA != null) _polarityByGo[sphereA] = polarityA;
                if (polarityB != null) _polarityByGo[sphereB] = polarityB;

                // Cable preview — shows the connection path while the user taps the ports.
                var previewPath = new SplinePathDefinition
                {
                    radius     = wireRadius,
                    segments   = 8,
                    metallic   = 0f,
                    smoothness = 0.25f,
                    knots = BuildSagKnots(portAPos, portBPos, wireEntry?.subdivisions ?? 1, wireEntry?.sag ?? 0f),
                };
                GameObject cablePreview = SplinePartFactory.CreatePreview(targetId, previewPath, previewRoot);
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

        /// <param name="polarityType">
        /// Optional polarity token (+12V, GND, signal, etc.) for WireConnect steps.
        /// When non-null, the sphere color is resolved from <see cref="PolarityToColor"/>
        /// instead of the default red/blue A/B pair.
        /// </param>
        private GameObject SpawnPortSphere(Vector3 localPos, bool isPortA, Transform parent, string polarityType = null)
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
                // WireConnect: color by polarity type so learner reads the wire intent visually.
                // Cable/default: generic red (A) or blue (B) positional coding.
                Color c = polarityType != null
                    ? PolarityToColor(polarityType)
                    : isPortA
                        ? new Color(1.00f, 0.18f, 0.18f, 1f)
                        : new Color(0.18f, 0.50f, 1.00f, 1f);

                var mat = AcquirePortMaterial(mr);
                mat.name = go.name + "_Mat";

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     c);

                mr.sharedMaterial = mat;
            }

            AddPortShapeMarker(go, isPortA);

            _spawnedPortSpheres.Add(go);
            return go;
        }

        // ── Screen-proximity hit detection ──

        private GameObject FindNearestPortSphereByScreenProximity(Vector2 screenPos)
        {
            return StepHandlerConstants.FindNearestByScreenProximity(
                _spawnedPortSpheres, screenPos, StepHandlerConstants.Proximity.GetThreshold());
        }

        // ── Port-sphere visual state ──

        private bool IsPortSphereConfirmed(GameObject sphere)
        {
            return sphere != null && _confirmedSpheres.Contains(sphere);
        }

        private void SetPortSphereConfirmed(GameObject sphere)
        {
            _confirmedSpheres.Add(sphere);

            var mr = sphere.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                // Use sharedMaterial — each sphere already owns its own Material instance
                // (created in SpawnPortSphere), so mutating sharedMaterial is safe and
                // avoids the implicit new-instance allocation that mr.material triggers.
                var mat = mr.sharedMaterial;
                if (mat != null)
                {
                    Color green = new Color(0.25f, 1.00f, 0.35f, 1f);
                    mat.SetColor("_BaseColor", green);
                    mat.SetColor("_EmissionColor", green * 0.6f);
                }
            }

            // Audio + haptic feedback — fires if an IEffectPlayer is registered; no-op otherwise.
            if (ServiceRegistry.TryGet<IEffectPlayer>(out var fx))
            {
                fx.Play(EffectRole.PlacementFeedback, sphere.transform.position);
                fx.PlayHaptic(EffectRole.HapticFeedback);
            }
        }

        // ── Pipe spline rendering ──

        private void TryRenderPipeSpline(string stepId)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null) return;

            PreviewSceneSetup setup = _ctx.Setup;
            if (setup == null) return;

            if (!package.TryGetStep(stepId, out var step)) return;
            if (!step.IsPipeConnection) return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0) return;

            Transform previewRoot = setup.PreviewRoot;
            if (previewRoot == null) return;

            for (int ti = 0; ti < targetIds.Length; ti++)
            {
                string targetId = targetIds[ti];
                WireConnectEntry entry = ResolveWireEntry(step, targetId, ti);
                Color hoseColor  = ResolveWireColor(entry);
                float hoseRadius = ResolveWireRadius(entry);

                Vector3 portAPos, portBPos;
                if (entry != null && (entry.portA.x != 0f || entry.portA.y != 0f || entry.portA.z != 0f ||
                                      entry.portB.x != 0f || entry.portB.y != 0f || entry.portB.z != 0f))
                {
                    portAPos = new Vector3(entry.portA.x, entry.portA.y, entry.portA.z);
                    portBPos = new Vector3(entry.portB.x, entry.portB.y, entry.portB.z);
                }
                else
                {
                    TargetPreviewPlacement tp = _ctx.Spawner.FindTargetPlacement(targetId);
                    if (tp == null) continue;
                    portAPos = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                    portBPos = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);
                    if (portAPos == Vector3.zero && portBPos == Vector3.zero) continue;
                }

                var path = new SplinePathDefinition
                {
                    radius     = hoseRadius,
                    segments   = 8,
                    metallic   = 0f,
                    smoothness = 0.25f,
                    knots = BuildSagKnots(portAPos, portBPos, entry?.subdivisions ?? 1, entry?.sag ?? 0f),
                };

                GameObject splineGo = SplinePartFactory.CreateWire(targetId, stepId, path, hoseColor, previewRoot);
                if (splineGo != null)
                {
                    MaterialHelper.MarkAsImported(splineGo);
                    _renderedPipeSplines.Add(splineGo);
                    OseLog.Info($"[ConnectStepHandler] Rendered pipe spline for '{targetId}'.");
                }
            }
        }

        // ── Cleanup ──

        private void ClearPortSpheres()
        {
            foreach (var s in _spawnedPortSpheres)
            {
                if (s == null) continue;
                // The sphere's own material goes back to the pool; child shape-marker
                // materials (AccessibilityMarker_H / _V cubes) are destroyed outright.
                foreach (var mr in s.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr == null || mr.sharedMaterial == null) continue;
                    if (mr.gameObject == s)
                        ReturnPortMaterialToPool(mr.sharedMaterial);
                    else
                        UnityEngine.Object.Destroy(mr.sharedMaterial);
                }
                UnityEngine.Object.Destroy(s);
            }
            _spawnedPortSpheres.Clear();
            _confirmedSpheres.Clear();
            _portAByGo.Clear();
            _portBByGo.Clear();
            _polarityByGo.Clear();
            _wireEntryByGo.Clear();
            _pipePortAConfirmed = false;

            var cursorManager = _ctx.CursorManager;
            cursorManager?.ClearPipeCursorPreview();
        }

        private void ClearCablePreviews()
        {
            foreach (var g in _cablePreviews)
            {
                if (g == null) continue;
                var mr = g.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                    UnityEngine.Object.Destroy(mr.sharedMaterial);
                UnityEngine.Object.Destroy(g);
            }
            _cablePreviews.Clear();
        }

        private void ClearRenderedPipeSplines()
        {
            foreach (var p in _renderedPipeSplines)
            {
                if (p == null) continue;
                var mr = p.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                    UnityEngine.Object.Destroy(mr.sharedMaterial);
                UnityEngine.Object.Destroy(p);
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
            var cursorManager = _ctx.CursorManager;
            if (cursorManager != null)
                _ = cursorManager.SpawnPipeCursorPreviewAsync(package, step, _ctx.FindSpawnedPart, _ctx.Spawner);
        }

        // ── Helpers ──

        /// <summary>
        /// Adds a shape symbol to the sphere so port identity is not conveyed by color alone.
        /// portA gets a "+" (two perpendicular flat cubes); portB gets a "−" (one horizontal cube).
        /// Markers are white, slightly larger than the sphere surface, and have no colliders.
        /// </summary>
        private static void AddPortShapeMarker(GameObject sphere, bool isPortA)
        {
            // Scale relative to the sphere's local scale (sphere is 0.12 world units).
            // Marker cubes are scaled in the sphere's local space, so they appear the same
            // regardless of the sphere's world scale.
            const float barLong  = 1.10f;  // relative: spans slightly wider than sphere diameter
            const float barShort = 0.18f;  // relative: thin bar
            const float barDepth = 0.18f;  // relative: thin in Z

            GameObject hBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hBar.name = "ShapeMarker_H";
            hBar.transform.SetParent(sphere.transform, false);
            hBar.transform.localPosition = Vector3.zero;
            hBar.transform.localScale    = new Vector3(barLong, barShort, barDepth);
            Object.Destroy(hBar.GetComponent<BoxCollider>());
            ApplyMarkerMaterial(hBar);

            if (isPortA)
            {
                // Vertical bar of the "+" — only portA
                GameObject vBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vBar.name = "ShapeMarker_V";
                vBar.transform.SetParent(sphere.transform, false);
                vBar.transform.localPosition = Vector3.zero;
                vBar.transform.localScale    = new Vector3(barShort, barLong, barDepth);
                Object.Destroy(vBar.GetComponent<BoxCollider>());
                ApplyMarkerMaterial(vBar);
            }
        }

        private static void ApplyMarkerMaterial(GameObject marker)
        {
            var mr = marker.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            var mat = new Material(shader) { name = "PortMarker_Mat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
            mr.sharedMaterial = mat;
        }

        private Material AcquirePortMaterial(MeshRenderer fallbackMr)
        {
            if (_sphereMatPool.Count > 0)
            {
                int last = _sphereMatPool.Count - 1;
                var pooled = _sphereMatPool[last];
                _sphereMatPool.RemoveAt(last);
                return pooled;
            }

            if (_portSphereShader == null)
                _portSphereShader = Shader.Find("OSE/PortSphereOnTop")
                                 ?? Shader.Find("Universal Render Pipeline/Unlit")
                                 ?? Shader.Find("Universal Render Pipeline/Lit");

            return _portSphereShader != null
                ? new Material(_portSphereShader)
                : new Material(fallbackMr.sharedMaterial);
        }

        private void ReturnPortMaterialToPool(Material mat)
        {
            // Reset to a neutral state so stale colors don't bleed into the next step.
            if (mat.HasProperty("_BaseColor"))     mat.SetColor("_BaseColor",     Color.gray);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            _sphereMatPool.Add(mat);
        }

        /// <summary>
        /// Builds sag knots for a wire spline between portA and portB.
        /// <paramref name="subdivisions"/> controls how many intermediate knots are inserted
        /// along the parabolic sag curve (1 = one midpoint, 2+ = smoother drape).
        /// </summary>
        private static SceneFloat3[] BuildSagKnots(Vector3 a, Vector3 b, int subdivisions, float sag = 1.0f)
        {
            int midCount = Mathf.Max(1, subdivisions);
            var knots = new SceneFloat3[midCount + 2];
            knots[0] = new SceneFloat3 { x = a.x, y = a.y, z = a.z };
            knots[knots.Length - 1] = new SceneFloat3 { x = b.x, y = b.y, z = b.z };

            float sagFactor = sag > 0f ? sag : 1.0f;
            float wireLength = Vector3.Distance(a, b);
            float sagDepth   = sagFactor * (wireLength * 0.12f + 0.04f);
            for (int i = 0; i < midCount; i++)
            {
                float t = (i + 1f) / (midCount + 1f);
                float sagY = -sagDepth * Mathf.Sin(Mathf.PI * t);
                knots[i + 1] = new SceneFloat3
                {
                    x = Mathf.Lerp(a.x, b.x, t),
                    y = Mathf.Lerp(a.y, b.y, t) + sagY,
                    z = Mathf.Lerp(a.z, b.z, t),
                };
            }
            return knots;
        }

        /// <summary>
        /// Finds the <see cref="WireConnectEntry"/> for the given targetId/index
        /// from the step's wireConnect payload. Returns null for non-WireConnect steps
        /// or when no matching entry is found.
        /// </summary>
        private static WireConnectEntry ResolveWireEntry(StepDefinition step, string targetId, int targetIndex)
        {
            if (step.ResolvedProfile != StepProfile.WireConnect) return null;
            var payload = step.wireConnect;
            if (payload == null || !payload.IsConfigured) return null;

            // Prefer explicit targetId match, fall back to index.
            foreach (var entry in payload.wires)
            {
                if (entry.targetId == targetId) return entry;
            }
            return targetIndex < payload.wires.Length ? payload.wires[targetIndex] : null;
        }

        /// <summary>
        /// Maps a polarity token to its conventional wire color.
        /// <list type="table">
        ///   <item>+12V / +5V / + → red</item>
        ///   <item>GND / - / -12V → near-black</item>
        ///   <item>signal / pwm / enable / endstop → yellow</item>
        ///   <item>thermistor / fan → blue</item>
        ///   <item>unknown → blue (generic default)</item>
        /// </list>
        /// </summary>
        private static Color PolarityToColor(string polarityType) => polarityType switch
        {
            "+12V" or "+5V" or "+"          => new Color(1.00f, 0.18f, 0.18f, 1f),  // red — positive power
            "GND"  or "-"   or "-12V"       => new Color(0.08f, 0.08f, 0.08f, 1f),  // near-black — ground/negative
            "signal" or "pwm" or "enable"
                or "endstop"                => new Color(1.00f, 0.85f, 0.00f, 1f),  // yellow — logic/signal
            "thermistor" or "fan"           => new Color(0.20f, 0.60f, 1.00f, 1f),  // blue — sensor/fan
            _                               => new Color(0.18f, 0.50f, 1.00f, 1f),  // blue — unrecognized
        };

        /// <summary>
        /// Briefly flashes a port sphere orange to signal a rejected click
        /// (e.g. wrong polarity order). Does not permanently change the sphere's color.
        /// </summary>
        private static void FlashPortSphereRejected(GameObject sphere)
        {
            var mr = sphere?.GetComponent<MeshRenderer>();
            if (mr == null) return;
            // Immediate orange flash — the sphere will restore on the next step activation.
            // A coroutine-based fade-back is intentionally omitted to avoid MonoBehaviour
            // coupling; the visual is transient enough for tap interactions.
            // Use sharedMaterial to avoid allocating a new instance (each sphere owns its own).
            var mat = mr.sharedMaterial;
            if (mat == null) return;
            Color orange = new Color(1.00f, 0.55f, 0.00f, 1f);
            mat.SetColor("_BaseColor", orange);
            mat.SetColor("_EmissionColor", orange * 0.4f);
        }

        /// <summary>
        /// Returns the wire color from the entry's authored color field when alpha &gt; 0,
        /// otherwise returns the default near-black cable color.
        /// </summary>
        private static Color ResolveWireColor(WireConnectEntry entry)
        {
            if (entry != null && entry.color.a > 0f)
                return new Color(entry.color.r, entry.color.g, entry.color.b, entry.color.a);
            return new Color(0.15f, 0.15f, 0.15f, 1f);
        }

        /// <summary>
        /// Returns the tube radius from the entry's width field, defaulting to 0.003 m when unset.
        /// </summary>
        private static float ResolveWireRadius(WireConnectEntry entry)
            => entry != null && entry.radius > 0f ? entry.radius : 0.003f;
    }
}
