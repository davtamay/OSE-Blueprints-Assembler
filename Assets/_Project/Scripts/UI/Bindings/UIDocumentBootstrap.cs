using OSE.Core;
using OSE.UI.Root;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace OSE.UI.Bindings
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class UIDocumentBootstrap : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;
        [SerializeField] private UISceneMode _sceneMode = UISceneMode.Runtime;
        [SerializeField] private UIPresentationSurface _presentationSurface = UIPresentationSurface.ScreenSpace;
        [SerializeField, Min(0)] private int _sortingOrder = 10;
        [SerializeField] private bool _ensureEventSystemInEditMode = true;
        [SerializeField] private InputActionAsset _uiInputActions;

        private PanelSettings _runtimePanelSettings;
        private bool _xrUiInputModuleConfigured;
        private readonly IXRInputConfigurator _xrInputConfigurator = new XRUIInputConfigurator();

        public UISceneMode SceneMode => _sceneMode;
        public UIPresentationSurface PresentationSurface => _presentationSurface;

        private void Awake()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            EnsurePanelSettings();
            EnsureEventSystem();
            TryConfigureXrUiInputModule();
        }

        public VisualElement PrepareDocumentRoot()
        {
            EnsurePanelSettings();

            if (_document == null)
            {
                OseLog.Error("[UIDocumentBootstrap] Missing UIDocument component.");
                return null;
            }

            VisualElement root = _document.rootVisualElement;
            if (root == null)
            {
                OseLog.Warn("[UIDocumentBootstrap] UIDocument root is not yet available.");
                return null;
            }

            root.Clear();
            root.name = "ose-ui-root";
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1f;
            root.EnableInClassList("ose-ui-root", true);
            root.EnableInClassList("ose-ui-root--frontend", _sceneMode == UISceneMode.Frontend);
            root.EnableInClassList("ose-ui-root--runtime", _sceneMode == UISceneMode.Runtime);
            root.EnableInClassList("ose-ui-root--screen", _presentationSurface == UIPresentationSurface.ScreenSpace);
            root.EnableInClassList("ose-ui-root--spatial-ready", _presentationSurface == UIPresentationSurface.SpatialReady);

            return root;
        }

        private void Reset()
        {
            _document = GetComponent<UIDocument>();
        }

        private void EnsurePanelSettings()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            if (_document == null || _document.panelSettings != null)
            {
                return;
            }

            if (_runtimePanelSettings == null)
            {
                _runtimePanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _runtimePanelSettings.name = "OSERuntimePanelSettings";
                _runtimePanelSettings.sortingOrder = _sortingOrder;
            }

            _document.panelSettings = _runtimePanelSettings;
            OseLog.VerboseInfo("[UI] Created runtime PanelSettings for UIDocument bootstrap.");
        }

        private void Update()
        {
            // Once both systems are fully configured in play mode, skip per-frame
            // scene queries (FindObjectsByType calls inside EnsureEventSystem).
            if (Application.isPlaying && _xrUiInputModuleConfigured)
                return;

            EnsureEventSystem();
            TryConfigureXrUiInputModule();
        }

        private void EnsureEventSystem()
        {
            if (!Application.isPlaying && !_ensureEventSystemInEditMode)
                return;

            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
                return;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.gameObject.scene != gameObject.scene)
            {
                EventSystem[] eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < eventSystems.Length; i++)
                {
                    EventSystem candidate = eventSystems[i];
                    if (candidate != null && candidate.gameObject.scene == gameObject.scene)
                    {
                        eventSystem = candidate;
                        break;
                    }
                }
            }

            if (eventSystem == null)
            {
                GameObject eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
                eventSystemObject.AddComponent<XRUIInputModule>();
                OseLog.Info("[UI] Created EventSystem with XR UI Input Module.");
                return;
            }

            if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
                OseLog.Info("[UI] Added XR UI Input Module to existing EventSystem.");
            }
        }

        private void TryConfigureXrUiInputModule()
        {
            if (!Application.isPlaying || _xrUiInputModuleConfigured)
                return;

            if (_uiInputActions == null)
                _uiInputActions = ResolveUiInputActionAsset();

            _xrUiInputModuleConfigured = _xrInputConfigurator.TryConfigure(_uiInputActions);

            if (_xrUiInputModuleConfigured)
                OseLog.Info("[UI] Bound XR UI Input Module actions from XRI UI action map.");
        }

        private static InputActionAsset ResolveUiInputActionAsset()
        {
            InputActionManager[] actionManagers = FindObjectsByType<InputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int managerIndex = 0; managerIndex < actionManagers.Length; managerIndex++)
            {
                InputActionManager manager = actionManagers[managerIndex];
                if (manager == null || manager.actionAssets == null)
                    continue;

                for (int assetIndex = 0; assetIndex < manager.actionAssets.Count; assetIndex++)
                {
                    InputActionAsset candidate = manager.actionAssets[assetIndex];
                    if (candidate == null)
                        continue;

                    if (candidate.FindActionMap("XRI UI", throwIfNotFound: false) != null)
                        return candidate;
                }
            }

            InputActionAsset[] assets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (int i = 0; i < assets.Length; i++)
            {
                InputActionAsset candidate = assets[i];
                if (candidate == null)
                    continue;

                if (candidate.FindActionMap("XRI UI", throwIfNotFound: false) != null)
                    return candidate;
            }

            return null;
        }

        private void OnDestroy()
        {
            if (_runtimePanelSettings != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_runtimePanelSettings);
                }
                else
                {
                    DestroyImmediate(_runtimePanelSettings);
                }

                _runtimePanelSettings = null;
            }

            (_xrInputConfigurator as XRUIInputConfigurator)?.DestroyActionReferences();
        }
    }
}
