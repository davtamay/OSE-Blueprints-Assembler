using OSE.App;
using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages assembly repositioning mode. When active, users can translate the
    /// assembly on the XZ ground plane (left-drag) and rotate around Y (right-drag).
    /// Moving PreviewRoot preserves all local-space part positions and placement validation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssemblyRepositionController : MonoBehaviour
    {
        private const float RotationSpeed = 0.4f;
        private const string HandleName = "Reposition Handle";

        private Transform _previewRoot;
        private GameObject _floor;
        private Camera _camera;

        // Original transform for Reset
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private Color _originalFloorColor;
        private bool _originalsCaptured;

        // State
        private bool _isRepositioning;
        private GameObject _handleDisc;

        // Desktop drag tracking
        private bool _leftDragActive;
        private bool _rightDragActive;
        private Vector3 _lastGroundHit;

        public bool IsRepositioning => _isRepositioning;

        private void OnEnable()
        {
            RuntimeEventBus.Subscribe<RepositionModeChanged>(HandleRepositionModeChanged);
            ServiceRegistry.Register(this);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<RepositionModeChanged>(HandleRepositionModeChanged);

            if (ServiceRegistry.TryGet<AssemblyRepositionController>(out var existing) &&
                ReferenceEquals(existing, this))
            {
                ServiceRegistry.Unregister<AssemblyRepositionController>();
            }

            if (_isRepositioning)
                ExitRepositionMode();
        }

        private void Update()
        {
            if (!_isRepositioning || _previewRoot == null)
                return;

            ProcessDesktopInput();
        }

        // ── Public API ──

        public void Toggle()
        {
            RuntimeEventBus.Publish(new RepositionModeChanged(!_isRepositioning));
        }

        public void ResetPosition()
        {
            if (_previewRoot == null || !_originalsCaptured)
                return;

            _previewRoot.SetPositionAndRotation(_originalPosition, _originalRotation);
            OseLog.Info("[Reposition] Reset to original position.");
        }

        // ── Mode transitions ──

        private void HandleRepositionModeChanged(RepositionModeChanged evt)
        {
            if (evt.IsActive && !_isRepositioning)
                EnterRepositionMode();
            else if (!evt.IsActive && _isRepositioning)
                ExitRepositionMode();
        }

        private void EnterRepositionMode()
        {
            if (!ResolveReferences())
                return;

            CaptureOriginals();

            _isRepositioning = true;
            _leftDragActive = false;
            _rightDragActive = false;

            ApplyFloorTint(new Color(0.85f, 0.65f, 0.2f, 0.6f));
            ShowHandle(true);

            OseLog.Info("[Reposition] Entered reposition mode.");
        }

        private void ExitRepositionMode()
        {
            _isRepositioning = false;
            _leftDragActive = false;
            _rightDragActive = false;

            RestoreFloorColor();
            ShowHandle(false);

            OseLog.Info("[Reposition] Exited reposition mode.");
        }

        // ── Desktop Input ──

        private void ProcessDesktopInput()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null)
                return;

            bool leftDown = mouse.leftButton.isPressed;
            bool rightDown = mouse.rightButton.isPressed;
            Vector2 mousePos = mouse.position.ReadValue();

            // Skip if over UI
            if (IsPointerOverUI(mousePos))
            {
                _leftDragActive = false;
                _rightDragActive = false;
                return;
            }

            // Left-click drag → translate on XZ ground plane
            if (leftDown)
            {
                if (!_leftDragActive)
                {
                    _leftDragActive = true;
                    TryGetGroundHit(mousePos, out _lastGroundHit);
                }
                else
                {
                    if (TryGetGroundHit(mousePos, out Vector3 currentHit))
                    {
                        Vector3 delta = currentHit - _lastGroundHit;
                        delta.y = 0f;
                        _previewRoot.position += delta;
                        // Re-project after moving to avoid drift
                        TryGetGroundHit(mousePos, out _lastGroundHit);
                    }
                }
            }
            else
            {
                _leftDragActive = false;
            }

            // Right-click drag → rotate around Y
            if (rightDown)
            {
                if (!_rightDragActive)
                {
                    _rightDragActive = true;
                }
                else
                {
                    float yawDelta = mouse.delta.ReadValue().x * RotationSpeed;
                    _previewRoot.Rotate(0f, yawDelta, 0f, Space.World);
                }
            }
            else
            {
                _rightDragActive = false;
            }
        }

        private bool TryGetGroundHit(Vector2 screenPos, out Vector3 hit)
        {
            hit = Vector3.zero;
            if (_camera == null)
                return false;

            Ray ray = _camera.ScreenPointToRay(screenPos);
            float groundY = _originalsCaptured ? _originalPosition.y : _previewRoot.position.y;
            var groundPlane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

            if (groundPlane.Raycast(ray, out float enter))
            {
                hit = ray.GetPoint(enter);
                return true;
            }

            return false;
        }

        private static bool IsPointerOverUI(Vector2 screenPos)
        {
            // Use EventSystem if available for robust UI hit testing
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.IsPointerOverGameObject())
                return true;

            return false;
        }

        // ── Visual Feedback ──

        private void ApplyFloorTint(Color tint)
        {
            if (_floor == null) return;
            MaterialHelper.SetMaterialColor(_floor, tint);
        }

        private void RestoreFloorColor()
        {
            if (_floor == null) return;

            var sceneSetup = GetComponent<PreviewSceneSetup>();
            if (sceneSetup != null)
            {
                Color original = sceneSetup.ActiveProfile.Floor.color;
                MaterialHelper.Apply(_floor, "Preview Floor Material", original);
            }
            else
            {
                MaterialHelper.SetMaterialColor(_floor, _originalFloorColor);
            }
        }

        private void ShowHandle(bool visible)
        {
            if (visible)
            {
                if (_handleDisc == null)
                    CreateHandleDisc();

                _handleDisc.SetActive(true);
            }
            else
            {
                if (_handleDisc != null)
                    _handleDisc.SetActive(false);
            }
        }

        private void CreateHandleDisc()
        {
            _handleDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _handleDisc.name = HandleName;
            _handleDisc.transform.SetParent(_previewRoot, false);
            _handleDisc.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            _handleDisc.transform.localScale = new Vector3(1.2f, 0.005f, 1.2f);

            // Disable collider — handle is visual only for desktop
            var col = _handleDisc.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            // Amber translucent material
            MaterialHelper.ApplyGhost(_handleDisc, new Color(1f, 0.75f, 0.2f, 0.35f));
        }

        // ── Reference Resolution ──

        private bool ResolveReferences()
        {
            if (_previewRoot != null && _floor != null && _camera != null)
                return true;

            var sceneSetup = GetComponent<PreviewSceneSetup>();
            if (sceneSetup == null)
            {
                OseLog.Warn("[Reposition] PreviewSceneSetup not found on this GameObject.");
                return false;
            }

            _previewRoot = sceneSetup.PreviewRoot;
            _floor = sceneSetup.Floor;
            _camera = Camera.main;

            if (_previewRoot == null)
            {
                OseLog.Warn("[Reposition] PreviewRoot not found.");
                return false;
            }

            return true;
        }

        private void CaptureOriginals()
        {
            if (_originalsCaptured || _previewRoot == null)
                return;

            _originalPosition = _previewRoot.position;
            _originalRotation = _previewRoot.rotation;

            if (_floor != null)
            {
                var renderer = _floor.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    _originalFloorColor = renderer.sharedMaterial.HasProperty("_BaseColor")
                        ? renderer.sharedMaterial.GetColor("_BaseColor")
                        : Color.gray;
                }
            }

            _originalsCaptured = true;
        }
    }
}
