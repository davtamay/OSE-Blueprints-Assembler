using OSE.Core;
using OSE.Runtime.Preview;
using OSE.UI.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns the scene scaffold: Preview Scaffold parent, floor plane, target marker,
    /// camera positioning, and the UI Root host (UIDocument + UIDocumentBootstrap +
    /// UIRootCoordinator). Reads MechanicsSceneVisualProfile for environment settings.
    ///
    /// This component knows nothing about packages, parts, or runtime events.
    /// It just ensures the scene skeleton exists and looks right.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PreviewSceneSetup : MonoBehaviour
    {
        private const string PreviewRootName = "Preview Scaffold";
        private const string LegacyPreviewRootName = "Generated Preview";
        private const string FloorName = "Preview Floor";
        private const string TargetMarkerName = "Placement Target";
        private const string UiHostName = "UI Root";

        [SerializeField] private MechanicsSceneVisualProfile _visualProfile;

        private MechanicsSceneVisualProfile _builtInVisualProfile;
        private bool _applied;

        // ── Public accessors for sibling components ──

        public Transform PreviewRoot { get; private set; }
        public GameObject Floor { get; private set; }
        public GameObject TargetMarker { get; private set; }
        public GameObject UiHost { get; private set; }
        public UIRootCoordinator UiRootCoordinator { get; private set; }
        public MechanicsSceneVisualProfile ActiveProfile => _visualProfile != null
            ? _visualProfile
            : (_builtInVisualProfile ??= MechanicsSceneVisualProfile.CreateBuiltInDefault());

        // ── Lifecycle ──

        private void OnEnable()
        {
            MechanicsSceneVisualProfile.Changed += HandleProfileChanged;
            _applied = false;
            Apply();
        }

        private void OnDisable()
        {
            MechanicsSceneVisualProfile.Changed -= HandleProfileChanged;
        }

        private void Update()
        {
            if (Application.isPlaying)
                return;

            if (!ActiveProfile.PreviewInEditMode)
            {
                CacheScaffold();
                SetScaffoldActive(false);
                return;
            }

            if (!_applied)
                Apply();
        }

        private void OnValidate()
        {
            _applied = false;
            if (isActiveAndEnabled)
                Apply();
        }

        private void OnDestroy()
        {
            MechanicsSceneVisualProfile.Changed -= HandleProfileChanged;
            if (_builtInVisualProfile != null)
            {
                if (Application.isPlaying) Destroy(_builtInVisualProfile);
                else DestroyImmediate(_builtInVisualProfile);
                _builtInVisualProfile = null;
            }
        }

        // ── Public API ──

        /// <summary>Call this after any external change that requires a re-apply.</summary>
        public void Refresh()
        {
            _applied = false;
            Apply();
        }

        // ── Core ──

        private void Apply()
        {
            if (!Application.isPlaying && !ActiveProfile.PreviewInEditMode)
            {
                CacheScaffold();
                SetScaffoldActive(false);
                return;
            }

            EnsureScaffold();
            SetScaffoldActive(true);
            ApplyCamera();
            ApplyFloor();
            ApplyTargetMarker();
            _applied = true;
        }

        private void CacheScaffold()
        {
            if (PreviewRoot == null)
            {
                PreviewRoot = transform.Find(PreviewRootName);
                if (PreviewRoot == null)
                {
                    PreviewRoot = transform.Find(LegacyPreviewRootName);
                    if (PreviewRoot != null)
                        PreviewRoot.name = PreviewRootName;
                }
            }

            if (PreviewRoot == null) return;

            Floor = FindChild(FloorName);
            TargetMarker = FindChild(TargetMarkerName);
            UiHost = FindChild(UiHostName);

            if (UiHost != null)
                UiRootCoordinator = UiHost.GetComponent<UIRootCoordinator>();
        }

        private void EnsureScaffold()
        {
            CacheScaffold();
            PreviewRoot = GetOrCreateChildTransform(PreviewRootName);
            Floor = GetOrCreatePrimitive(FloorName, PrimitiveType.Plane);
            TargetMarker = GetOrCreatePrimitive(TargetMarkerName, PrimitiveType.Cylinder);
            EnsureUiHost();

            SetObjectActive(Floor, ActiveProfile.ShowGeometryPreview);
            SetObjectActive(TargetMarker, ActiveProfile.ShowGeometryPreview);
            SetObjectActive(UiHost, ActiveProfile.ShowUiPreview);
        }

        private void EnsureUiHost()
        {
            UiHost = GetOrCreateChildObject(UiHostName);
            GetOrAddComponent<UIDocument>(UiHost);
            GetOrAddComponent<UIDocumentBootstrap>(UiHost);
            UiRootCoordinator = GetOrAddComponent<UIRootCoordinator>(UiHost);
        }

        private void ApplyCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            if (!Application.isPlaying) return;
            PreviewCameraSettings cam = ActiveProfile.Camera;
            mainCamera.backgroundColor = cam.backgroundColor;
        }

        private void ApplyFloor()
        {
            if (Floor == null || !ActiveProfile.ShowGeometryPreview) return;
            PreviewObjectAppearance floor = ActiveProfile.Floor;
            Floor.transform.SetLocalPositionAndRotation(floor.position, Quaternion.identity);
            Floor.transform.localScale = floor.scale;
            MaterialHelper.Apply(Floor, "Preview Floor Material", floor.color);
        }

        private void ApplyTargetMarker()
        {
            if (TargetMarker == null || !ActiveProfile.ShowGeometryPreview) return;
            // Default placement — PackagePartSpawner overrides from previewConfig
            Collider col = TargetMarker.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // The cylinder only exists as an authoring anchor for scene-capture tools.
            // Hide its renderer at runtime so it doesn't appear in the game view;
            // it remains active so SessionDriverEditor can still find and read its transform.
            if (Application.isPlaying)
            {
                var mr = TargetMarker.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        // ── Events ──

        private void HandleProfileChanged(MechanicsSceneVisualProfile profile)
        {
            if (profile != null && profile == _visualProfile)
                Refresh();
        }

        // ── Helpers ──

        private GameObject FindChild(string name)
        {
            if (PreviewRoot == null) return null;
            Transform child = PreviewRoot.Find(name);
            return child != null ? child.gameObject : null;
        }

        private Transform GetOrCreateChildTransform(string name)
        {
            Transform existing = transform.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private GameObject GetOrCreateChildObject(string name)
        {
            Transform existing = PreviewRoot != null ? PreviewRoot.Find(name) : null;
            if (existing != null) return existing.gameObject;
            var go = new GameObject(name);
            go.transform.SetParent(PreviewRoot, false);
            return go;
        }

        private GameObject GetOrCreatePrimitive(string name, PrimitiveType type)
        {
            GameObject go = GetOrCreateChildObject(name);
            if (go.GetComponent<MeshFilter>() == null || go.GetComponent<MeshRenderer>() == null)
            {
                SafeDestroy(go);
                go = GameObject.CreatePrimitive(type);
                go.name = name;
                go.transform.SetParent(PreviewRoot, false);
            }
            return go;
        }

        private void SetScaffoldActive(bool active)
        {
            if (PreviewRoot != null && PreviewRoot.gameObject.activeSelf != active)
                PreviewRoot.gameObject.SetActive(active);
        }

        private static void SetObjectActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
