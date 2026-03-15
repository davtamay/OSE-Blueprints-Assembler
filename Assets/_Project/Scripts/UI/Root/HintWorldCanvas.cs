using OSE.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    [DisallowMultipleComponent]
    public sealed class HintWorldCanvas : MonoBehaviour
    {
        private const float DefaultVisibleSeconds = 6f;
        private const float DefaultWorldScale = 0.0015f;
        private static readonly Vector2 DefaultPanelSize = new Vector2(360f, 180f);

        [Header("Layout")]
        [SerializeField] private Vector2 _panelSize = DefaultPanelSize;
        [SerializeField] private float _worldScale = DefaultWorldScale;
        [SerializeField] private Vector3 _worldOffset = new Vector3(0f, 0.22f, 0f);
        [SerializeField] private float _visibleSeconds = DefaultVisibleSeconds;

        private UIDocument _document;
        private PanelSettings _panelSettings;
        private VisualElement _root;
        private Label _eyebrowLabel;
        private Label _titleLabel;
        private Label _messageLabel;
        private Transform _followTarget;
        private float _hideAtSeconds;
        private bool _isVisible;

        private void Awake()
        {
            EnsureDocument();
            BuildUi();
            SetVisible(false);
        }

        private void Update()
        {
            if (!_isVisible)
                return;

            if (Time.time >= _hideAtSeconds)
            {
                SetVisible(false);
                return;
            }

            UpdateTransform();
        }

        private void OnDestroy()
        {
            if (_panelSettings == null)
                return;

            if (Application.isPlaying)
                Destroy(_panelSettings);
            else
                DestroyImmediate(_panelSettings);

            _panelSettings = null;
        }

        public void ShowHint(string hintType, string title, string message, Transform followTarget)
        {
            EnsureDocument();
            if (!BuildUi())
                return;

            _followTarget = followTarget;
            _eyebrowLabel.text = string.IsNullOrWhiteSpace(hintType) ? "Hint" : $"Hint - {hintType}";
            _titleLabel.text = string.IsNullOrWhiteSpace(title) ? "Guidance" : title;
            _messageLabel.text = string.IsNullOrWhiteSpace(message) ? "Follow the guidance to continue." : message;

            _hideAtSeconds = Time.time + Mathf.Max(0.1f, _visibleSeconds);
            SetVisible(true);
            UpdateTransform();
        }

        private void EnsureDocument()
        {
            if (_document == null)
                _document = GetComponent<UIDocument>();

            if (_document == null)
                _document = gameObject.AddComponent<UIDocument>();

            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.name = "OSEHintWorldPanelSettings";
                _panelSettings.sortingOrder = 20;
                _panelSettings.renderMode = PanelRenderMode.WorldSpace;
                _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                _panelSettings.referenceResolution = new Vector2Int(
                    Mathf.RoundToInt(_panelSize.x),
                    Mathf.RoundToInt(_panelSize.y));
            }

            _document.panelSettings = _panelSettings;
            _document.sortingOrder = 20;
            _document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
        }

        private bool BuildUi()
        {
            if (_document == null)
                return false;

            _root = _document.rootVisualElement;
            if (_root == null)
                return false;

            _root.Clear();
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.pickingMode = PickingMode.Ignore;

            var card = new VisualElement();
            UIToolkitStyleUtility.ApplyPanelSurface(card);
            card.style.width = _panelSize.x;
            card.style.maxWidth = _panelSize.x;
            card.style.backgroundColor = new Color(0.08f, 0.12f, 0.2f, 0.94f);
            card.pickingMode = PickingMode.Ignore;

            var accent = new VisualElement();
            accent.style.height = 3f;
            accent.style.backgroundColor = new Color(0.42f, 0.82f, 1f, 1f);
            accent.style.marginBottom = 10f;
            card.Add(accent);

            _eyebrowLabel = UIToolkitStyleUtility.CreateEyebrowLabel("Hint");
            _titleLabel = UIToolkitStyleUtility.CreateTitleLabel("Guidance");
            _titleLabel.style.fontSize = 18f;
            _messageLabel = UIToolkitStyleUtility.CreateBodyLabel("Follow the guidance to continue.");

            card.Add(_eyebrowLabel);
            card.Add(_titleLabel);
            card.Add(_messageLabel);
            _root.Add(card);

            return true;
        }

        private void UpdateTransform()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 anchor = _followTarget != null ? _followTarget.position : transform.position;
            transform.position = anchor + _worldOffset;
            transform.rotation = Quaternion.LookRotation(cam.transform.position - transform.position);
            transform.localScale = Vector3.one * _worldScale;
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_root != null)
                _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
