using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Runtime.Preview;
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
        private static readonly Color HoveredAffordanceColor = new Color(0.40f, 0.85f, 1.0f, 1.0f);
        private static readonly Color SelectedAffordanceColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);

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
        /// Loads a GLB asset from AssetDatabase and returns a new scene instance
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

            if (prefab == null)
            {
                OseLog.Warn($"[PackagePartSpawner] GLB not in AssetDatabase: {assetPath}");
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
                    if (Application.isPlaying)
                        EnsureColliders(existing.gameObject);
                    if (enableRuntimeGrab)
                        TryEnableXRGrabInteractable(existing.gameObject);
                    _spawnedParts.Add(existing.gameObject);
                    continue;
                }

                GameObject go = TryLoadPackageAsset(part.assetRef)
                             ?? GetOrCreatePrimitive(part.id, PrimitiveType.Cube);
                go.name = part.id;
                if (enableRuntimeGrab)
                    TryEnableXRGrabInteractable(go);
                _spawnedParts.Add(go);
            }

            bool showGeometry = _setup.ActiveProfile.ShowGeometryPreview;
            foreach (var p in _spawnedParts)
                SetObjectActive(p, showGeometry);
        }

        private void PositionParts()
        {
            if (!_setup.ActiveProfile.ShowGeometryPreview)
                return;

            for (int i = 0; i < _spawnedParts.Count; i++)
            {
                var partGo = _spawnedParts[i];
                if (partGo == null) continue;

                PartPreviewPlacement pp = FindPartPlacement(partGo.name);

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
    }
}
