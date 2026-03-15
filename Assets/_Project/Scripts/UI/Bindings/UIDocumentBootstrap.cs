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

        private InputActionReference _pointActionReference;
        private InputActionReference _leftClickActionReference;
        private InputActionReference _middleClickActionReference;
        private InputActionReference _rightClickActionReference;
        private InputActionReference _scrollWheelActionReference;
        private InputActionReference _navigateActionReference;
        private InputActionReference _submitActionReference;
        private InputActionReference _cancelActionReference;

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

            EventSystem eventSystem = EventSystem.current != null
                ? EventSystem.current
                : FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
                return;

            XRUIInputModule uiInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (uiInputModule == null)
                return;

            if (_uiInputActions == null)
                _uiInputActions = ResolveUiInputActionAsset();

            if (_uiInputActions == null)
                return;

            InputActionMap uiActionMap = _uiInputActions.FindActionMap("XRI UI", throwIfNotFound: false);
            if (uiActionMap == null)
                return;

            // Force explicit XRI UI bindings. XRUIInputModule can lazily create internal fallback
            // actions which appear configured via properties but do not use this project's action map.
            uiInputModule.pointAction = GetOrCreateActionReference(uiActionMap, "Point", ref _pointActionReference);
            uiInputModule.leftClickAction = GetOrCreateActionReference(uiActionMap, "Click", ref _leftClickActionReference);
            uiInputModule.middleClickAction = GetOrCreateActionReference(uiActionMap, "MiddleClick", ref _middleClickActionReference);
            uiInputModule.rightClickAction = GetOrCreateActionReference(uiActionMap, "RightClick", ref _rightClickActionReference);
            uiInputModule.scrollWheelAction = GetOrCreateActionReference(uiActionMap, "ScrollWheel", ref _scrollWheelActionReference);
            uiInputModule.navigateAction = GetOrCreateActionReference(uiActionMap, "Navigate", ref _navigateActionReference);
            uiInputModule.submitAction = GetOrCreateActionReference(uiActionMap, "Submit", ref _submitActionReference);
            uiInputModule.cancelAction = GetOrCreateActionReference(uiActionMap, "Cancel", ref _cancelActionReference);

            uiInputModule.enableBuiltinActionsAsFallback = false;
            _xrUiInputModuleConfigured = uiInputModule.pointAction != null &&
                                         uiInputModule.leftClickAction != null &&
                                         uiInputModule.scrollWheelAction != null &&
                                         uiInputModule.navigateAction != null &&
                                         uiInputModule.submitAction != null &&
                                         uiInputModule.cancelAction != null;

            if (_xrUiInputModuleConfigured)
                OseLog.Info("[UI] Bound XR UI Input Module actions from XRI UI action map.");
        }

        private static InputActionReference GetOrCreateActionReference(
            InputActionMap actionMap,
            string actionName,
            ref InputActionReference cachedReference)
        {
            if (cachedReference != null)
                return cachedReference;

            InputAction action = actionMap.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
                return null;

            cachedReference = InputActionReference.Create(action);
            return cachedReference;
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

            DestroyActionReference(ref _pointActionReference);
            DestroyActionReference(ref _leftClickActionReference);
            DestroyActionReference(ref _middleClickActionReference);
            DestroyActionReference(ref _rightClickActionReference);
            DestroyActionReference(ref _scrollWheelActionReference);
            DestroyActionReference(ref _navigateActionReference);
            DestroyActionReference(ref _submitActionReference);
            DestroyActionReference(ref _cancelActionReference);
        }

        private static void DestroyActionReference(ref InputActionReference actionReference)
        {
            if (actionReference == null)
                return;

            if (Application.isPlaying)
                Destroy(actionReference);
            else
                DestroyImmediate(actionReference);

            actionReference = null;
        }
    }
}
