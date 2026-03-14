using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.UI.Root
{
    /// <summary>
    /// Play-mode bridge that connects runtime events to scene objects.
    /// Handles pointer-based interaction (mouse and touch): click-to-select,
    /// drag-to-place with tolerance-based validation and snap-to-target,
    /// ghost part spawning, visual feedback, and step-completion repositioning.
    ///
    /// Requires <see cref="PackagePartSpawner"/> on the same GameObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PackagePartSpawner))]
    public sealed class PartInteractionBridge : MonoBehaviour
    {
        private static readonly Color SelectedPartColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        private static readonly Color GrabbedPartColor = new Color(1.0f, 0.65f, 0.1f, 1.0f);
        private static readonly Color CompletedPartColor = new Color(0.3f, 0.9f, 0.4f, 1.0f);
        private static readonly Color InvalidFlashColor = new Color(1.0f, 0.2f, 0.2f, 1.0f);
        private static readonly Color GhostReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);

        private const float DragThresholdPixels = 5f;
        private const float SnapLerpSpeed = 12f;
        private const float InvalidFlashDuration = 0.3f;
        private const float SnapZoneRadius = 0.8f; // generous radius in Unity units
        private const float ScrollDepthSpeed = 0.5f; // units per scroll tick
        private const float PinchDepthSpeed = 0.02f; // units per pixel of pinch delta

        private PackagePartSpawner _spawner;
        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _spawnedGhosts = new List<GameObject>();

        // Drag state
        private GameObject _draggedPart;
        private string _draggedPartId;
        private Vector3 _dragStartPosition;
        private Vector2 _dragScreenStart;
        private Plane _dragPlane;
        private Camera _dragCamera;
        private bool _isDragging;
        private bool _pointerDown;
        private float _dragDepthOffset;
        private float _lastPinchDistance;
        private float _lastPointerY;
        private GameObject _hoveredGhost;
        private bool _ghostHighlighted;

        // Snap animation
        private GameObject _snappingPart;
        private Vector3 _snapTargetPos;
        private Quaternion _snapTargetRot;
        private Vector3 _snapTargetScale;
        private bool _isSnapping;

        // Invalid flash
        private GameObject _flashPart;
        private Color _flashOriginalColor;
        private float _flashTimer;

        // ── Lifecycle ──

        private void OnEnable()
        {
            _spawner = GetComponent<PackagePartSpawner>();
            _setup = GetComponent<PreviewSceneSetup>();
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<PartStateChanged>(HandlePartStateChanged);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<PartStateChanged>(HandlePartStateChanged);
        }

        private void Update()
        {
            HandlePointerInput();
            UpdateSnapAnimation();
            UpdateInvalidFlash();
        }

        // ── Pointer input (mouse + touch) ──

        private void HandlePointerInput()
        {
            if (TryGetPointerState(out Vector2 screenPos, out bool pressed, out bool released))
            {
                if (pressed)
                    HandlePointerDown(screenPos);
                else if (released)
                    HandlePointerUp(screenPos);
                else if (_pointerDown)
                    HandlePointerDrag(screenPos);
            }
        }

        private static bool TryGetPointerState(out Vector2 screenPos, out bool pressed, out bool released)
        {
            screenPos = Vector2.zero;
            pressed = false;
            released = false;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                pressed = mouse.leftButton.wasPressedThisFrame;
                released = mouse.leftButton.wasReleasedThisFrame;
                if (pressed || released || mouse.leftButton.isPressed)
                    return true;
            }

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                pressed = touch.primaryTouch.press.wasPressedThisFrame;
                released = touch.primaryTouch.press.wasReleasedThisFrame;
                return true;
            }

            return false;
        }

        private void HandlePointerDown(Vector2 screenPos)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
                return;

            GameObject matchedPart = FindPartFromHit(hit.transform);
            if (matchedPart == null)
                return;

            // Select the part
            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;

            bool selected = partController.SelectPart(matchedPart.name);
            if (!selected)
                return;

            OseLog.Info($"[PartInteraction] Selected part '{matchedPart.name}'");

            // Prepare for potential drag
            _pointerDown = true;
            _isDragging = false;
            _draggedPart = matchedPart;
            _draggedPartId = matchedPart.name;
            _dragStartPosition = matchedPart.transform.localPosition;
            _dragScreenStart = screenPos;
            _dragCamera = cam;
            _dragDepthOffset = 0f;
            _lastPinchDistance = -1f;
            _lastPointerY = screenPos.y;
            // Camera-facing plane so dragging follows the pointer naturally
            _dragPlane = new Plane(-cam.transform.forward, matchedPart.transform.position);
        }

        private void HandlePointerDrag(Vector2 screenPos)
        {
            if (_draggedPart == null || _dragCamera == null) return;

            Camera cam = _dragCamera;

            if (!_isDragging)
            {
                float dist = Vector2.Distance(screenPos, _dragScreenStart);
                if (dist < DragThresholdPixels)
                    return;

                // Start dragging
                _isDragging = true;
                if (ServiceRegistry.TryGet<PartRuntimeController>(out var pc))
                    pc.GrabPart(_draggedPartId);

                OseLog.Info($"[PartInteraction] Dragging part '{_draggedPartId}'");
            }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool depthMode = (kb != null && kb.shiftKey.isPressed)
                || (mouse != null && (mouse.rightButton.isPressed || mouse.middleButton.isPressed));

            // Shift/right-click held: vertical pointer movement adjusts depth
            if (depthMode)
            {
                float deltaY = screenPos.y - _lastPointerY;
                _dragDepthOffset += deltaY * PinchDepthSpeed;
            }
            _lastPointerY = screenPos.y;

            // Always position part on camera plane + depth offset
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (_dragPlane.Raycast(ray, out float enter))
            {
                _draggedPart.transform.position = ray.GetPoint(enter) + cam.transform.forward * _dragDepthOffset;
            }

            // Mouse scroll wheel
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    _dragDepthOffset += Mathf.Sign(scroll) * ScrollDepthSpeed;
            }

            // Q/E keys
            if (kb != null)
            {
                if (kb.eKey.isPressed)
                    _dragDepthOffset += ScrollDepthSpeed * Time.deltaTime * 3f;
                if (kb.qKey.isPressed)
                    _dragDepthOffset -= ScrollDepthSpeed * Time.deltaTime * 3f;
            }

            // Touch pinch → push/pull along camera forward (mobile)
            var touch = Touchscreen.current;
            if (touch != null && touch.touches.Count >= 2)
            {
                var t0 = touch.touches[0];
                var t1 = touch.touches[1];
                if (t0.press.isPressed && t1.press.isPressed)
                {
                    float pinchDist = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
                    if (_lastPinchDistance > 0f)
                    {
                        float delta = pinchDist - _lastPinchDistance;
                        _dragDepthOffset += delta * PinchDepthSpeed;
                    }
                    _lastPinchDistance = pinchDist;
                }
            }
            else
            {
                _lastPinchDistance = -1f;
            }

            // Check proximity to ghosts and highlight when in snap zone
            UpdateGhostProximity();
        }

        private void HandlePointerUp(Vector2 screenPos)
        {
            if (!_pointerDown)
                return;

            _pointerDown = false;

            if (!_isDragging || _draggedPart == null)
            {
                // Was just a click, not a drag — selection already handled in HandlePointerDown
                _draggedPart = null;
                _draggedPartId = null;
                return;
            }

            _isDragging = false;

            // Attempt placement
            AttemptDragPlacement();

            _draggedPart = null;
            _draggedPartId = null;
        }

        private void AttemptDragPlacement()
        {
            ClearGhostHighlight();

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            string partId = _draggedPartId;

            // Check if we're in the snap zone of any ghost
            GameObject nearestGhost = FindNearestGhostInRange(_draggedPart.transform.position);

            if (nearestGhost != null)
            {
                // Dropped in snap zone — snap to ghost position and complete
                string[] targetIds = partController.GetActiveStepTargetIds();
                string matchedTargetId = targetIds.Length > 0 ? targetIds[0] : "unknown";

                // Find the matching target for this specific part
                var package = _spawner.CurrentPackage;
                if (package != null)
                {
                    foreach (string tid in targetIds)
                    {
                        if (package.TryGetTarget(tid, out var t) && t.associatedPartId == partId)
                        {
                            matchedTargetId = tid;
                            break;
                        }
                    }
                }

                PlacementValidationResult result = PlacementValidator.ValidateExact();
                partController.AttemptPlacement(partId, matchedTargetId, result);

                OseLog.Info($"[PartInteraction] Dropped '{partId}' in snap zone — snapping to target.");

                // Animate snap to ghost position
                PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
                if (pp != null)
                {
                    _snappingPart = _draggedPart;
                    _snapTargetPos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                    _snapTargetRot = !pp.playRotation.IsIdentity
                        ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                        : Quaternion.identity;
                    _snapTargetScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                    _isSnapping = true;
                }
                else
                {
                    // Fall back to ghost transform directly
                    _draggedPart.transform.position = nearestGhost.transform.position;
                    _draggedPart.transform.rotation = nearestGhost.transform.rotation;
                }

                session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
            }
            else
            {
                // Dropped outside snap zone — flash red, stay where dropped
                OseLog.Info($"[PartInteraction] Dropped '{partId}' outside snap zone — stays at drop position.");
                FlashInvalid(_draggedPart, partId);
                partController.DeselectPart();
            }
        }

        // ── Ghost proximity detection ──

        private void UpdateGhostProximity()
        {
            if (_draggedPart == null || _spawnedGhosts.Count == 0)
                return;

            GameObject nearest = FindNearestGhostInRange(_draggedPart.transform.position);

            if (nearest != null && nearest != _hoveredGhost)
            {
                // Entered a new ghost's snap zone
                ClearGhostHighlight();
                _hoveredGhost = nearest;
                _ghostHighlighted = true;
                MaterialHelper.Apply(nearest, "Ghost Ready Material", GhostReadyColor);
            }
            else if (nearest == null && _ghostHighlighted)
            {
                // Left all snap zones
                ClearGhostHighlight();
            }
        }

        private void ClearGhostHighlight()
        {
            if (_ghostHighlighted && _hoveredGhost != null)
            {
                MaterialHelper.ApplyGhost(_hoveredGhost);
            }
            _hoveredGhost = null;
            _ghostHighlighted = false;
        }

        private GameObject FindNearestGhostInRange(Vector3 worldPos)
        {
            GameObject nearest = null;
            float nearestDist = SnapZoneRadius;

            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                float dist = Vector3.Distance(worldPos, ghost.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = ghost;
                }
            }

            return nearest;
        }

        // ── Snap animation ──

        private void UpdateSnapAnimation()
        {
            if (!_isSnapping || _snappingPart == null)
            {
                _isSnapping = false;
                return;
            }

            float t = SnapLerpSpeed * Time.deltaTime;
            _snappingPart.transform.localPosition = Vector3.Lerp(_snappingPart.transform.localPosition, _snapTargetPos, t);
            _snappingPart.transform.localRotation = Quaternion.Slerp(_snappingPart.transform.localRotation, _snapTargetRot, t);
            _snappingPart.transform.localScale = Vector3.Lerp(_snappingPart.transform.localScale, _snapTargetScale, t);

            if (Vector3.Distance(_snappingPart.transform.localPosition, _snapTargetPos) < 0.001f)
            {
                _snappingPart.transform.SetLocalPositionAndRotation(_snapTargetPos, _snapTargetRot);
                _snappingPart.transform.localScale = _snapTargetScale;
                _isSnapping = false;
                _snappingPart = null;
            }
        }

        // ── Invalid placement flash ──

        private void FlashInvalid(GameObject partGo, string partId)
        {
            _flashPart = partGo;
            _flashTimer = InvalidFlashDuration;
            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
            _flashOriginalColor = pp != null
                ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);
            MaterialHelper.Apply(partGo, "Preview Part Material", InvalidFlashColor);
        }

        private void UpdateInvalidFlash()
        {
            if (_flashPart == null || _flashTimer <= 0f)
                return;

            _flashTimer -= Time.deltaTime;
            if (_flashTimer <= 0f)
            {
                MaterialHelper.Apply(_flashPart, "Preview Part Material", _flashOriginalColor);
                _flashPart = null;
            }
        }

        // ── Runtime event handlers ──

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (evt.Current == StepState.Active)
            {
                SpawnGhostsForStep(evt.StepId);
            }
            else if (evt.Current == StepState.Completed)
            {
                MoveStepPartsToPlayPosition(evt.StepId);
                ClearGhosts();
            }
        }

        private void HandlePartStateChanged(PartStateChanged evt)
        {
            GameObject partGo = FindSpawnedPart(evt.PartId);
            if (partGo == null) return;

            switch (evt.Current)
            {
                case PartPlacementState.Selected:
                case PartPlacementState.Inspected:
                    MaterialHelper.Apply(partGo, "Preview Part Material", SelectedPartColor);
                    break;

                case PartPlacementState.Grabbed:
                    MaterialHelper.Apply(partGo, "Preview Part Material", GrabbedPartColor);
                    break;

                case PartPlacementState.Available:
                    PartPreviewPlacement pp = _spawner.FindPartPlacement(evt.PartId);
                    Color col = pp != null
                        ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                        : new Color(0.94f, 0.55f, 0.18f, 1f);
                    MaterialHelper.Apply(partGo, "Preview Part Material", col);
                    break;

                case PartPlacementState.PlacedVirtually:
                case PartPlacementState.Completed:
                    MaterialHelper.Apply(partGo, "Preview Part Material", CompletedPartColor);
                    break;
            }

            if (evt.Current == PartPlacementState.Selected || evt.Current == PartPlacementState.Inspected)
            {
                PushPartInfoToUI(evt.PartId);
            }
        }

        // ── Ghost parts ──

        private void SpawnGhostsForStep(string stepId)
        {
            ClearGhosts();
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0)
                return;

            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;

            foreach (string targetId in targetIds)
            {
                if (string.IsNullOrEmpty(targetId)) continue;
                if (!package.TryGetTarget(targetId, out var target)) continue;

                string associatedPartId = target.associatedPartId;
                if (string.IsNullOrEmpty(associatedPartId)) continue;
                if (!package.TryGetPart(associatedPartId, out var part)) continue;

                string ghostRef = part.assetRef;
                if (string.IsNullOrEmpty(ghostRef)) continue;

                PartPreviewPlacement pp = _spawner.FindPartPlacement(associatedPartId);
                Vector3 ghostPos;
                Quaternion ghostRot;
                Vector3 ghostScale;

                if (pp != null)
                {
                    ghostPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                    ghostRot   = !pp.playRotation.IsIdentity
                        ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                        : Quaternion.identity;
                    ghostScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                }
                else
                {
                    TargetPreviewPlacement tp = _spawner.FindTargetPlacement(targetId);
                    if (tp != null)
                    {
                        ghostPos   = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                        ghostRot   = !tp.rotation.IsIdentity
                            ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                            : Quaternion.identity;
                        ghostScale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
                    }
                    else
                    {
                        ghostPos   = Vector3.zero;
                        ghostRot   = Quaternion.identity;
                        ghostScale = Vector3.one * 0.5f;
                    }
                }

                GameObject ghost = _spawner.TryLoadPackageAsset(ghostRef);
                if (ghost == null)
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    if (previewRoot != null)
                        ghost.transform.SetParent(previewRoot, false);
                }

                ghost.name = $"Ghost_{associatedPartId}";
                ghost.transform.SetLocalPositionAndRotation(ghostPos, ghostRot);
                ghost.transform.localScale = ghostScale;

                foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                    Destroy(col);

                MaterialHelper.ApplyGhost(ghost);
                _spawnedGhosts.Add(ghost);
            }
        }

        private void ClearGhosts()
        {
            foreach (var ghost in _spawnedGhosts)
            {
                if (ghost == null) continue;
                ghost.transform.SetParent(null);
                Destroy(ghost);
            }
            _spawnedGhosts.Clear();
        }

        // ── Step completion: move parts to assembled position ──

        private void MoveStepPartsToPlayPosition(string stepId)
        {
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null) return;

            foreach (string partId in partIds)
            {
                if (string.IsNullOrEmpty(partId)) continue;

                PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
                if (pp == null) continue;

                GameObject partGo = FindSpawnedPart(partId);
                if (partGo == null) continue;

                Vector3    pPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                Vector3    pScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                Quaternion pRot   = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;

                partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
                partGo.transform.localScale = pScale;
            }
        }

        // ── Context menu: place selected part at target (debug shortcut) ──

        [ContextMenu("Place Selected Part at Target")]
        private void PlaceSelectedPartAtTarget()
        {
            if (!Application.isPlaying)
            {
                OseLog.Warn("[PartInteraction] PlaceSelectedPartAtTarget is only available in play mode.");
                return;
            }

            if (!ServiceRegistry.TryGet<PartRuntimeController>(out var partController))
                return;
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            string selectedId = partController.SelectedPartId;
            if (string.IsNullOrEmpty(selectedId))
            {
                OseLog.Warn("[PartInteraction] No part is selected. Click a part first.");
                return;
            }

            string[] targetIds = partController.GetActiveStepTargetIds();
            if (targetIds.Length == 0)
            {
                OseLog.Warn("[PartInteraction] Active step has no targets.");
                return;
            }

            PlacementValidationResult result = PlacementValidator.ValidateExact();
            partController.AttemptPlacement(selectedId, targetIds[0], result);

            PartPreviewPlacement pp = _spawner.FindPartPlacement(selectedId);
            if (pp != null)
            {
                GameObject partGo = FindSpawnedPart(selectedId);
                if (partGo != null)
                {
                    Vector3 pPos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                    Vector3 pScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                    Quaternion pRot = !pp.playRotation.IsIdentity
                        ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                        : Quaternion.identity;
                    partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
                    partGo.transform.localScale = pScale;
                }
            }

            session.AssemblyController?.StepController?.CompleteStep(session.GetElapsedSeconds());
        }

        // ── UI push ──

        private void PushPartInfoToUI(string partId)
        {
            var package = _spawner.CurrentPackage;
            if (package == null) return;
            if (!package.TryGetPart(partId, out var part)) return;
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui)) return;

            string toolNames = string.Empty;
            if (part.toolIds != null && part.toolIds.Length > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (string toolId in part.toolIds)
                {
                    if (string.IsNullOrEmpty(toolId)) continue;
                    if (package.TryGetTool(toolId, out var tool))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(tool.GetDisplayName());
                    }
                }
                toolNames = sb.ToString();
            }

            ui.ShowPartInfoShell(
                part.GetDisplayName(),
                part.function ?? string.Empty,
                part.material ?? string.Empty,
                toolNames,
                part.searchTerms != null ? string.Join(" ", part.searchTerms) : string.Empty);
        }

        // ── Helpers ──

        private GameObject FindSpawnedPart(string partId)
        {
            var parts = _spawner.SpawnedParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].name == partId)
                    return parts[i];
            }
            return null;
        }

        private GameObject FindPartFromHit(Transform hitTransform)
        {
            Transform previewRoot = _setup != null ? _setup.PreviewRoot : null;
            while (hitTransform != null && hitTransform != previewRoot)
            {
                var parts = _spawner.SpawnedParts;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i] != null && parts[i].transform == hitTransform)
                        return parts[i];
                }
                hitTransform = hitTransform.parent;
            }
            return null;
        }
    }
}
