using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Runtime.Preview;
using System.IO;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Theme;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Theme.Primitives;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace OSE.UI.Root
{
    /// <summary>
    /// Subscribes to <see cref="SessionDriver.PackageChanged"/> and spawns / positions
    /// GLB part GameObjects under the <see cref="PreviewSceneSetup.PreviewRoot"/> transform.
    /// Exposes the spawned part list so sibling components (PartInteractionBridge) can
    /// interact with them.
    ///
    /// This component knows nothing about runtime events, click interaction, or ghosts.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PreviewSceneSetup))]
    public sealed class PackagePartSpawner : MonoBehaviour
    {
        private const string SamplePartName = "Sample Beam";
        private static readonly Color HoveredAffordanceColor = new Color(0.60f, 0.82f, 1.0f, 1.0f);
        private static readonly Color SelectedAffordanceColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly HashSet<string> MissingAssetWarnings = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        private PreviewSceneSetup _setup;
        private MachinePackageDefinition _currentPackage;
        private PackagePreviewConfig _currentPreviewConfig;
        private readonly List<GameObject> _spawnedParts = new List<GameObject>();

        // ── Public accessors ──

        public IReadOnlyList<GameObject> SpawnedParts => _spawnedParts;
        public MachinePackageDefinition CurrentPackage => _currentPackage;
        public PackagePreviewConfig CurrentPreviewConfig => _currentPreviewConfig;

        // ── Lifecycle ──

        private void OnEnable()
        {
            _setup = GetComponent<PreviewSceneSetup>();
            SessionDriver.PackageChanged += HandlePackageChanged;

            // Catch up if this component enabled after the latest package event.
            if (SessionDriver.CurrentPackage != null)
            {
                HandlePackageChanged(SessionDriver.CurrentPackage);
            }
        }

        private void OnDisable()
        {
            SessionDriver.PackageChanged -= HandlePackageChanged;
        }

        // ── Public API ──

        /// <summary>
        /// Finds the <see cref="PartPreviewPlacement"/> for a given part id from
        /// the current preview config. Returns null if not found.
        /// </summary>
        public PartPreviewPlacement FindPartPlacement(string partId)
        {
            if (_currentPreviewConfig?.partPlacements == null) return null;
            foreach (var p in _currentPreviewConfig.partPlacements)
                if (p.partId == partId) return p;
            return null;
        }

        /// <summary>
        /// Finds the <see cref="TargetPreviewPlacement"/> for a given target id.
        /// </summary>
        public TargetPreviewPlacement FindTargetPlacement(string targetId)
        {
            if (_currentPreviewConfig?.targetPlacements == null) return null;
            foreach (var t in _currentPreviewConfig.targetPlacements)
                if (t.targetId == targetId) return t;
            return null;
        }

        /// <summary>
        /// Applies a package snapshot immediately. Used by late-initializing listeners
        /// that need deterministic startup behavior in play mode.
        /// </summary>
        public void ApplyPackageSnapshot(MachinePackageDefinition package)
        {
            if (package == null)
                return;

            HandlePackageChanged(package);
        }

        /// <summary>
        /// Loads a package model asset from AssetDatabase and returns a new scene instance
        /// parented under PreviewRoot, or null if the asset isn't imported yet.
        /// Play-mode instances get MeshColliders; edit-mode colliders are stripped.
        /// </summary>
        public GameObject TryLoadPackageAsset(string assetRef)
        {
            if (string.IsNullOrWhiteSpace(assetRef) ||
                string.IsNullOrWhiteSpace(_currentPackage?.packageId))
                return null;

#if UNITY_EDITOR
            string assetPath =
                $"Assets/_Project/Data/Packages/{_currentPackage.packageId}/{assetRef.Replace('\\', '/')}";

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    if (asset is GameObject go) { prefab = go; break; }
            }

            if (prefab == null && assetPath.EndsWith(".glb"))
            {
                string gltfPath = Path.ChangeExtension(assetPath, ".gltf");
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(gltfPath);
                if (prefab == null)
                {
                    foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(gltfPath))
                        if (asset is GameObject go) { prefab = go; break; }
                }

                if (prefab != null)
                    OseLog.Warn($"[PackagePartSpawner] Fallback loaded GLTF for missing GLB ref: {assetPath} -> {gltfPath}");
            }

            if (prefab == null)
            {
                if (MissingAssetWarnings.Add(assetPath))
                {
                    if (File.Exists(assetPath))
                        OseLog.Warn($"[PackagePartSpawner] Model exists but could not be imported as a GameObject: {assetPath}. Falling back to primitive preview.");
                    else
                        OseLog.Warn($"[PackagePartSpawner] Asset prefab not in AssetDatabase: {assetPath}");
                }
                return null;
            }

            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(_setup.PreviewRoot, false);
            if (Application.isPlaying)
            {
                EnsureColliders(instance);
            }
            else
            {
                foreach (var col in instance.GetComponentsInChildren<Collider>(true))
                    DestroyImmediate(col);
            }
            return instance;
#else
            return null;
#endif
        }

        // ── Events ──

        private void HandlePackageChanged(MachinePackageDefinition package)
        {
            _currentPackage = package;
            _currentPreviewConfig = package?.previewConfig;
            ClearSpawnedParts();
            RespawnAndPosition();
        }

        // ── Spawning ──

        private void RespawnAndPosition()
        {
            if (_setup.PreviewRoot == null)
                return;

            SpawnPackageParts();
            PositionParts();
            PositionTargetMarker();
        }

        private void SpawnPackageParts()
        {
            _spawnedParts.Clear();
            bool enableRuntimeGrab = Application.isPlaying;

            if (_currentPackage?.parts == null || _currentPackage.parts.Length == 0)
            {
                _spawnedParts.Add(GetOrCreatePrimitive(SamplePartName, PrimitiveType.Cube));
                return;
            }

            foreach (var part in _currentPackage.parts)
            {
                if (string.IsNullOrWhiteSpace(part.id) || string.IsNullOrWhiteSpace(part.assetRef))
                    continue;

                Transform existing = _setup.PreviewRoot.Find(part.id);
                if (existing != null)
                {
                    // Mark imported models that were already in the scene (e.g. from a prior spawn)
                    if (!MaterialHelper.IsImportedModel(existing.gameObject)
                        && existing.GetComponentInChildren<MeshFilter>() != null
                        && !IsPrimitive(existing.gameObject))
                    {
                        MaterialHelper.MarkAsImported(existing.gameObject);
                    }

                    if (Application.isPlaying)
                    {
                        // Spline parts use SplineMeshColliderBinder for deferred MeshCollider — skip EnsureColliders
                        if (existing.GetComponent<SplineMeshColliderBinder>() == null)
                            EnsureColliders(existing.gameObject);
                    }
                    if (enableRuntimeGrab)
                        TryEnableXRGrabInteractable(existing.gameObject);
                    _spawnedParts.Add(existing.gameObject);
                    continue;
                }

                // Spline-based parts (hoses, cables) — create procedural tube mesh
                PartPreviewPlacement splinePP = FindPartPlacement(part.id);
                if (SplinePartFactory.HasSplineData(splinePP))
                {
                    Color sc = new Color(splinePP.color.r, splinePP.color.g, splinePP.color.b, splinePP.color.a);
                    GameObject splineGo = SplinePartFactory.Create(part.id, splinePP.splinePath, sc, _setup.PreviewRoot);
                    MaterialHelper.MarkAsImported(splineGo);
                    if (enableRuntimeGrab)
                        TryEnableXRGrabInteractable(splineGo);
                    _spawnedParts.Add(splineGo);
                    continue;
                }

                GameObject go = TryLoadPackageAsset(part.assetRef);
                if (go != null)
                    MaterialHelper.MarkAsImported(go);
                else
                    go = GetOrCreatePrimitive(part.id, PrimitiveType.Cube);
                go.name = part.id;
                if (enableRuntimeGrab)
                    TryEnableXRGrabInteractable(go);
                _spawnedParts.Add(go);
            }

            bool showGeometry = _setup.ActiveProfile.ShowGeometryPreview;
            foreach (var p in _spawnedParts)
                SetObjectActive(p, showGeometry);
        }

        // Layout constants for auto-positioning parts around the floor perimeter
        private const float LayoutRadius = 3.8f;           // distance from center
        private const float LayoutArcDegrees = 220f;       // total arc spread
        private const float LayoutArcStartDeg = -110f;     // centered on negative Z (camera side)
        private const float LayoutY = 0.55f;               // height above floor
        private const float LayoutPadding = 0.15f;         // gap between parts within a group
        private const float LayoutGroupGap = 0.3f;         // extra gap between different groups on the arc

        private void PositionParts()
        {
            if (!_setup.ActiveProfile.ShowGeometryPreview)
                return;

            if (!Application.isPlaying || _currentPackage?.parts == null)
            {
                PositionPartsFallback();
                return;
            }

            // Group parts by assetRef so identical parts cluster together
            var groups = new List<List<int>>();
            var assetToGroup = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _spawnedParts.Count; i++)
            {
                var partGo = _spawnedParts[i];
                if (partGo == null) continue;

                string assetRef = null;
                foreach (var part in _currentPackage.parts)
                {
                    if (string.Equals(part.id, partGo.name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        assetRef = part.assetRef;
                        break;
                    }
                }

                string groupKey = assetRef ?? partGo.name;
                if (assetToGroup.TryGetValue(groupKey, out int groupIdx))
                {
                    groups[groupIdx].Add(i);
                }
                else
                {
                    assetToGroup[groupKey] = groups.Count;
                    groups.Add(new List<int> { i });
                }
            }

            int groupCount = groups.Count;
            if (groupCount == 0) return;

            // Pre-resolve scale for every part (needed for bounds-aware spacing)
            var partScales = new Vector3[_spawnedParts.Count];
            for (int i = 0; i < _spawnedParts.Count; i++)
            {
                var go = _spawnedParts[i];
                if (go == null) { partScales[i] = Vector3.one; continue; }
                PartPreviewPlacement pp = FindPartPlacement(go.name);
                partScales[i] = pp != null
                    ? new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z)
                    : Vector3.one;
            }

            // Compute the tangent-direction width of each part at a given angle.
            // The tangent is perpendicular to the radius on the XZ plane.
            // Part footprint on tangent = |scale.x * cos(angle)| + |scale.z * sin(angle)|
            // This gives the maximum extent along the tangent direction.

            // For each group, compute its total tangent span (sum of member widths + padding).
            // We'll use a representative angle (will refine after layout) — start with even spacing.
            float[] groupSpans = new float[groupCount];
            float totalSpan = 0f;

            // First pass: estimate spans using evenly-spaced angles
            float roughArcStep = groupCount > 1 ? LayoutArcDegrees / (groupCount - 1) : 0f;
            for (int g = 0; g < groupCount; g++)
            {
                float angle = (LayoutArcStartDeg + roughArcStep * g) * Mathf.Deg2Rad;
                float tanX = Mathf.Abs(Mathf.Cos(angle));
                float tanZ = Mathf.Abs(Mathf.Sin(angle));

                var members = groups[g];
                float span = 0f;
                for (int m = 0; m < members.Count; m++)
                {
                    Vector3 s = partScales[members[m]];
                    float memberWidth = s.x * tanX + s.z * tanZ;
                    span += memberWidth;
                    if (m < members.Count - 1)
                        span += LayoutPadding;
                }
                groupSpans[g] = span;
                totalSpan += span;
            }

            // Add inter-group gaps
            totalSpan += (groupCount - 1) * LayoutGroupGap;

            // Convert total linear span to arc degrees at LayoutRadius
            float totalArcNeeded = (totalSpan / (LayoutRadius * Mathf.Deg2Rad)) * (180f / Mathf.PI);
            // If the needed arc exceeds available, scale radius up to fit
            float effectiveRadius = LayoutRadius;
            if (totalArcNeeded > LayoutArcDegrees && totalSpan > 0f)
            {
                // Increase radius so everything fits within the arc
                float arcLengthAvailable = LayoutArcDegrees * Mathf.Deg2Rad * LayoutRadius;
                if (totalSpan > arcLengthAvailable)
                    effectiveRadius = totalSpan / (LayoutArcDegrees * Mathf.Deg2Rad);
            }

            // Distribute groups proportionally along the arc based on their span
            float arcLength = LayoutArcDegrees * Mathf.Deg2Rad * effectiveRadius;
            float cursor = 0f; // linear position along the arc

            for (int g = 0; g < groupCount; g++)
            {
                float groupCenter = cursor + groupSpans[g] * 0.5f;
                float groupAngleRad = (LayoutArcStartDeg * Mathf.Deg2Rad) + (groupCenter / effectiveRadius);

                float cx = Mathf.Sin(groupAngleRad) * effectiveRadius;
                float cz = -Mathf.Cos(groupAngleRad) * effectiveRadius;

                var members = groups[g];

                // Tangent direction at this angle
                float tangentX = Mathf.Cos(groupAngleRad);
                float tangentZ = Mathf.Sin(groupAngleRad);
                float absTanX = Mathf.Abs(tangentX);
                float absTanZ = Mathf.Abs(tangentZ);

                // Compute individual member widths along tangent
                float[] memberWidths = new float[members.Count];
                float groupTotalWidth = 0f;
                for (int m = 0; m < members.Count; m++)
                {
                    Vector3 s = partScales[members[m]];
                    memberWidths[m] = s.x * absTanX + s.z * absTanZ;
                    groupTotalWidth += memberWidths[m];
                    if (m < members.Count - 1)
                        groupTotalWidth += LayoutPadding;
                }

                // Place members centered on group center
                float memberCursor = -groupTotalWidth * 0.5f;
                for (int m = 0; m < members.Count; m++)
                {
                    int partIdx = members[m];
                    var partGo = _spawnedParts[partIdx];
                    if (partGo == null) continue;

                    // Skip spline parts — their geometry is defined by spline knots
                    PartPreviewPlacement spCheck = FindPartPlacement(partGo.name);
                    if (SplinePartFactory.HasSplineData(spCheck)) continue;

                    float offset = memberCursor + memberWidths[m] * 0.5f;
                    float px = cx + tangentX * offset;
                    float pz = cz + tangentZ * offset;

                    PartPreviewPlacement pp = FindPartPlacement(partGo.name);
                    Vector3 scale = partScales[partIdx];
                    Color col = pp != null
                        ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                        : new Color(0.94f, 0.55f, 0.18f, 1f);
                    Quaternion rot = pp != null && !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;

                    partGo.transform.SetLocalPositionAndRotation(new Vector3(px, LayoutY, pz), rot);
                    partGo.transform.localScale = scale;

                    // Preserve original GLB materials; only apply solid color to primitives
                    if (!MaterialHelper.IsImportedModel(partGo))
                        MaterialHelper.Apply(partGo, "Preview Part Material", col);

                    TryApplyAffordanceState(partGo, AffordanceStateShortcuts.idle);

                    memberCursor += memberWidths[m] + LayoutPadding;
                }

                cursor += groupSpans[g] + LayoutGroupGap;
            }
        }

        /// <summary>
        /// Edit-mode fallback: use previewConfig positions or linear grid.
        /// </summary>
        private void PositionPartsFallback()
        {
            for (int i = 0; i < _spawnedParts.Count; i++)
            {
                var partGo = _spawnedParts[i];
                if (partGo == null) continue;

                PartPreviewPlacement pp = FindPartPlacement(partGo.name);

                // Skip spline parts — their geometry is defined by spline knots
                if (SplinePartFactory.HasSplineData(pp)) continue;

                Vector3 pos;
                Vector3 scale;
                Color col;
                Quaternion rot;

                if (pp != null)
                {
                    pos   = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    scale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    col   = new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a);
                    rot   = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
                }
                else
                {
                    pos   = new Vector3(-2f + i * 1.5f, 0.55f, 0f);
                    scale = Vector3.one * 0.5f;
                    col   = new Color(0.94f, 0.55f, 0.18f, 1f);
                    rot   = Quaternion.identity;
                }

                partGo.transform.SetLocalPositionAndRotation(pos, rot);
                partGo.transform.localScale = scale;

                // Preserve original GLB materials; only apply solid color to primitives
                if (!MaterialHelper.IsImportedModel(partGo))
                    MaterialHelper.Apply(partGo, "Preview Part Material", col);

                TryApplyAffordanceState(partGo, AffordanceStateShortcuts.idle);
            }
        }

        private void PositionTargetMarker()
        {
            if (_setup.TargetMarker == null || !_setup.ActiveProfile.ShowGeometryPreview)
                return;

            TargetPreviewPlacement tp = _currentPreviewConfig?.targetPlacements?.Length > 0
                ? _currentPreviewConfig.targetPlacements[0] : null;

            Vector3    tPos   = tp != null ? new Vector3(tp.position.x, tp.position.y, tp.position.z) : new Vector3(0f, 0.04f, 0f);
            Vector3    tScale = tp != null ? new Vector3(tp.scale.x, tp.scale.y, tp.scale.z) : new Vector3(0.9f, 0.04f, 0.9f);
            Color      tColor = tp != null ? new Color(tp.color.r, tp.color.g, tp.color.b, tp.color.a) : new Color(0.20f, 0.84f, 0.58f, 1f);
            Quaternion tRot   = tp != null && !tp.rotation.IsIdentity
                ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                : Quaternion.identity;

            _setup.TargetMarker.transform.SetLocalPositionAndRotation(tPos, tRot);
            _setup.TargetMarker.transform.localScale = tScale;
            MaterialHelper.Apply(_setup.TargetMarker, "Preview Target Material", tColor);
        }

        // ── Cleanup ──

        private void ClearSpawnedParts()
        {
            foreach (var go in _spawnedParts)
            {
                if (go == null) continue;
                go.transform.SetParent(null);
                SafeDestroy(go);
            }
            _spawnedParts.Clear();
        }

        // ── Helpers ──

        private GameObject GetOrCreatePrimitive(string name, PrimitiveType type)
        {
            Transform existing = _setup.PreviewRoot != null ? _setup.PreviewRoot.Find(name) : null;
            if (existing != null)
            {
                var go = existing.gameObject;
                if (go.GetComponent<MeshFilter>() != null && go.GetComponent<MeshRenderer>() != null)
                    return go;
                SafeDestroy(go);
            }

            var prim = GameObject.CreatePrimitive(type);
            prim.name = name;
            prim.transform.SetParent(_setup.PreviewRoot, false);
            return prim;
        }

        private static void TryEnableXRGrabInteractable(GameObject target)
        {
            if (target == null)
                return;

            XRGrabInteractable grabInteractable = target.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
            {
                var rb = target.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = target.AddComponent<Rigidbody>();

                rb.isKinematic = true;
                rb.useGravity = false;

                grabInteractable = target.AddComponent<XRGrabInteractable>();
            }

            // Skip affordance color system for imported models — their original materials
            // should not be overridden by MaterialPropertyBlock. State colors are handled
            // by ApplyTint/ClearTint in PartInteractionBridge instead.
            if (!MaterialHelper.IsImportedModel(target))
                EnsurePartColorAffordance(target, grabInteractable);
            TryApplyAffordanceState(target, AffordanceStateShortcuts.idle);
        }

        private static void EnsurePartColorAffordance(GameObject target, XRGrabInteractable grabInteractable)
        {
            if (target == null || grabInteractable == null)
                return;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                stateProvider = target.AddComponent<XRInteractableAffordanceStateProvider>();

            stateProvider.interactableSource = grabInteractable;
            stateProvider.transitionDuration = 0.08f;
            stateProvider.ignoreHoverEvents = true;
            stateProvider.ignoreHoverPriorityEvents = true;
            stateProvider.ignoreFocusEvents = true;
            stateProvider.ignoreSelectEvents = true;
            stateProvider.ignoreActivateEvents = true;
            stateProvider.selectClickAnimationMode = XRInteractableAffordanceStateProvider.SelectClickAnimationMode.None;
            stateProvider.activateClickAnimationMode = XRInteractableAffordanceStateProvider.ActivateClickAnimationMode.None;

            ColorAffordanceTheme theme = CreatePartColorAffordanceTheme();
            var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;

                var blockHelper = renderer.GetComponent<MaterialPropertyBlockHelper>();
                if (blockHelper == null)
                    blockHelper = renderer.gameObject.AddComponent<MaterialPropertyBlockHelper>();
                blockHelper.rendererTarget = renderer;
                blockHelper.materialIndex = 0;

                var colorReceiver = renderer.GetComponent<ColorMaterialPropertyAffordanceReceiver>();
                if (colorReceiver == null)
                    colorReceiver = renderer.gameObject.AddComponent<ColorMaterialPropertyAffordanceReceiver>();

                colorReceiver.affordanceStateProvider = stateProvider;
                colorReceiver.replaceIdleStateValueWithInitialValue = true;
                colorReceiver.materialPropertyBlockHelper = blockHelper;
                colorReceiver.colorPropertyName = ResolveColorPropertyName(renderer.sharedMaterial);

                colorReceiver.affordanceTheme = theme;
            }
        }

        private static string ResolveColorPropertyName(Material material)
        {
            if (material != null)
            {
                if (material.HasProperty("_BaseColor"))
                    return "_BaseColor";

                if (material.HasProperty("_Color"))
                    return "_Color";
            }

            return "_BaseColor";
        }

        private static ColorAffordanceTheme CreatePartColorAffordanceTheme()
        {
            var theme = new ColorAffordanceTheme
            {
                colorBlendMode = ColorBlendMode.Solid,
                blendAmount = 1f
            };
            theme.SetAnimationCurve(AnimationCurve.Linear(0f, 0f, 1f, 1f));
            theme.SetAffordanceThemeDataList(new List<AffordanceThemeData<Color>>
            {
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.disabled),
                    animationStateStartValue = Color.clear,
                    animationStateEndValue = Color.clear
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.idle),
                    animationStateStartValue = Color.clear,
                    animationStateEndValue = Color.clear
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.hovered),
                    animationStateStartValue = HoveredAffordanceColor,
                    animationStateEndValue = HoveredAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.hoveredPriority),
                    animationStateStartValue = HoveredAffordanceColor,
                    animationStateEndValue = HoveredAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.selected),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.activated),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.focused),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                }
            });

            return theme;
        }

        private static bool TryApplyAffordanceState(GameObject target, byte stateIndex, float transitionAmount = 1f)
        {
            if (target == null)
                return false;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                return false;

            stateProvider.UpdateAffordanceState(new AffordanceStateData(stateIndex, transitionAmount));
            return true;
        }

        /// <summary>
        /// Adds MeshColliders to every child with a MeshFilter for accurate raycasting.
        /// Falls back to a fitted BoxCollider if no MeshFilters exist.
        /// </summary>
        public static void EnsureColliders(GameObject target)
        {
            if (target.GetComponentInChildren<Collider>(true) != null)
                return;

            // Spline parts have a deferred binder that adds a MeshCollider once
            // the SplineExtrude mesh is generated — don't add a fallback BoxCollider.
            if (target.GetComponent<SplineMeshColliderBinder>() != null)
                return;

            var meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters.Length > 0)
            {
                foreach (var mf in meshFilters)
                {
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                        mf.gameObject.AddComponent<MeshCollider>();
                }
            }
            else
            {
                var renderers = target.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);
                    var box = target.AddComponent<BoxCollider>();
                    box.center = target.transform.InverseTransformPoint(bounds.center);
                    box.size = target.transform.InverseTransformVector(bounds.size);
                }
                else
                {
                    target.AddComponent<BoxCollider>();
                }
            }
        }

        private static void SetObjectActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static bool IsPrimitive(GameObject go)
        {
            // Unity primitives created by GetOrCreatePrimitive have a MeshFilter
            // with a shared mesh named after the primitive type
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            string meshName = mf.sharedMesh.name;
            return meshName == "Cube" || meshName == "Sphere" || meshName == "Cylinder"
                || meshName == "Capsule" || meshName == "Plane" || meshName == "Quad";
        }
    }
}
