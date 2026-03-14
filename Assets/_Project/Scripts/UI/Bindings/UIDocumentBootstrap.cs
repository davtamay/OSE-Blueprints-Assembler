using OSE.Core;
using OSE.UI.Root;
using UnityEngine;
using UnityEngine.UIElements;

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

        private PanelSettings _runtimePanelSettings;

        public UISceneMode SceneMode => _sceneMode;
        public UIPresentationSurface PresentationSurface => _presentationSurface;

        private void Awake()
        {
            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            EnsurePanelSettings();
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

        private void OnDestroy()
        {
            if (_runtimePanelSettings == null)
            {
                return;
            }

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
    }
}
