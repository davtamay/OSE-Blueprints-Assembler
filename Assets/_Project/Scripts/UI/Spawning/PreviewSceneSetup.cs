using OSE.Core;
using OSE.Runtime.Preview;
using OSE.UI.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns the scene scaffold: Preview Scaffold parent, camera positioning,
    /// and the UI Root host (UIDocument + UIDocumentBootstrap + UIRootCoordinator).
    /// Reads MechanicsSceneVisualProfile for environment settings.
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
        private const string UiHostName = "UI Root";

        [SerializeField] private MechanicsSceneVisualProfile _visualProfile;

        private MechanicsSceneVisualProfile _builtInVisualProfile;
        private bool _applied;

        // ── Public accessors for sibling components ──

        public Transform PreviewRoot { get; private set; }
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
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            // DestroyImmediate is forbidden inside OnValidate (called during physics,
            // animation, and play-mode-transition callbacks). Defer Apply() so it runs
            // on the next editor tick when destruction is safe.
            UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Apply(); };
#else
            Apply();
#endif
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

            UiHost = FindChild(UiHostName);

            if (UiHost != null)
                UiRootCoordinator = UiHost.GetComponent<UIRootCoordinator>();
        }

        private void EnsureScaffold()
        {
            CacheScaffold();
            PreviewRoot = GetOrCreateChildTransform(PreviewRootName);
            EnsureUiHost();

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
            Camera mainCamera = CameraUtil.GetMain();
            if (mainCamera == null) return;
            if (!Application.isPlaying) return;
            PreviewCameraSettings cam = ActiveProfile.Camera;
            mainCamera.backgroundColor = cam.backgroundColor;
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
