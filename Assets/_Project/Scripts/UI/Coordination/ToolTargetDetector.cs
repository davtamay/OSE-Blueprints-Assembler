using System;
using System.Collections.Generic;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Detects tool-action targets via raycast, screen-space proximity,
    /// and tool-preview bounding-rect overlap.
    /// </summary>
    internal sealed class ToolTargetDetector
    {
        private const float ToolBoundsReadyPaddingPx = 18f;

        private readonly ISiblingAccessContext _siblings;
        private readonly List<GameObject> _spawnedTargets;

        public ToolTargetDetector(ISiblingAccessContext siblings, List<GameObject> spawnedTargets)
        {
            _siblings = siblings;
            _spawnedTargets = spawnedTargets;
        }

        // ====================================================================
        //  Public detection API
        // ====================================================================

        /// <summary>
        /// Detects the tool action target at the given screen position using
        /// raycast + screen-space proximity fallback.
        /// </summary>
        public bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            if (TryGetToolActionTargetByRaycast(screenPos, out targetInfo))
                return true;

            return TryGetNearestToolTargetByScreenProximity(screenPos, out targetInfo);
        }

        /// <summary>
        /// Resolves the target that can actually execute a Use step.
        /// Prefers the current ready target driven by tool-preview bounds.
        /// </summary>
        public bool TryResolveToolActionTargetForExecution(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            if (TryGetToolActionTargetByRaycast(screenPos, out targetInfo))
                return true;

            if (TryGetReadyToolActionTarget(out targetInfo))
                return true;

            ToolCursorManager cursorManager = _siblings.CursorManager;
            if (cursorManager?.ToolPreview == null || !cursorManager.ToolPreview.activeSelf)
                return TryGetNearestToolTargetByScreenProximity(screenPos, out targetInfo);

            return TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        /// <summary>
        /// Returns world position of the nearest tool action target within screen proximity.
        /// </summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!TryGetToolActionTargetAtScreen(screenPos, out ToolActionTargetInfo info))
                return false;
            worldPos = info.transform.position;
            return true;
        }

        /// <summary>
        /// Focuses camera on a tool target near the pointer. Returns true if a target was found.
        /// </summary>
        public bool TryFocusCameraOnToolTarget(Vector2 screenPos)
        {
            if (_spawnedTargets.Count == 0)
                return false;

            if (!TryGetToolActionTargetAtScreen(screenPos, out ToolActionTargetInfo targetInfo))
                return false;

            if (targetInfo == null)
                return false;

            Camera cam = CameraUtil.GetMain();
            if (cam == null)
                return true;

            cam.SendMessage("FocusOn", targetInfo.transform.position, SendMessageOptions.DontRequireReceiver);
            return true;
        }

        /// <summary>Returns the currently hovered tool action target based on pointer position.</summary>
        public bool TryGetHoveredToolActionTarget(out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            if (!TryGetPointerPosition(out Vector2 screenPos))
                return false;
            return TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        /// <summary>
        /// Returns the tool action target whose tool-preview bounding rect overlaps,
        /// closest to rect center.
        /// </summary>
        public bool TryGetReadyToolActionTarget(out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            ToolCursorManager cursorManager = _siblings.CursorManager;
            Camera cam = CameraUtil.GetMain();
            GameObject toolPreview = cursorManager?.ToolPreview;
            if (cam == null || toolPreview == null || !toolPreview.activeSelf)
                return false;

            if (!TryGetToolPreviewScreenRect(cam, toolPreview, out Rect toolRect))
                return false;

            Rect paddedRect = ExpandRect(toolRect, ToolBoundsReadyPaddingPx);
            Vector2 rectCenter = paddedRect.center;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _spawnedTargets.Count; i++)
            {
                GameObject target = _spawnedTargets[i];
                if (target == null)
                    continue;

                Vector3 targetScreen = cam.WorldToScreenPoint(target.transform.position);
                if (targetScreen.z <= 0f)
                    continue;

                Vector2 targetPoint = new Vector2(targetScreen.x, targetScreen.y);
                if (!paddedRect.Contains(targetPoint))
                    continue;

                ToolActionTargetInfo info = target.GetComponent<ToolActionTargetInfo>();
                if (info == null)
                    continue;

                float score = Vector2.SqrMagnitude(targetPoint - rectCenter);
                if (score < bestScore)
                {
                    bestScore = score;
                    targetInfo = info;
                }
            }

            return targetInfo != null;
        }

        // ====================================================================
        //  Pointer helper
        // ====================================================================

        public static bool TryGetPointerPosition(out Vector2 screenPos)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = Vector2.zero;
            return false;
        }

        // ====================================================================
        //  Private
        // ====================================================================

        private bool TryGetNearestToolTargetByScreenProximity(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            Camera cam = CameraUtil.GetMain();
            if (cam == null) return false;

            float threshold = StepHandlerConstants.Proximity.GetThreshold();
            float closestDist = threshold;

            for (int i = 0; i < _spawnedTargets.Count; i++)
            {
                GameObject target = _spawnedTargets[i];
                if (target == null) continue;

                Vector3 sp = cam.WorldToScreenPoint(target.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    var info = target.GetComponent<ToolActionTargetInfo>();
                    if (info != null)
                        targetInfo = info;
                }
            }
            return targetInfo != null;
        }

        private bool TryGetToolActionTargetByRaycast(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            Camera cam = CameraUtil.GetMain();
            if (cam == null)
                return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                targetInfo = FindToolActionTargetFromHit(hits[i].transform);
                if (targetInfo != null)
                    return true;
            }

            targetInfo = null;
            return false;
        }

        private static ToolActionTargetInfo FindToolActionTargetFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                ToolActionTargetInfo info = hitTransform.GetComponent<ToolActionTargetInfo>();
                if (info != null)
                    return info;
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        private static bool TryGetToolPreviewScreenRect(Camera cam, GameObject toolPreview, out Rect screenRect)
        {
            screenRect = default;

            Renderer[] renderers = MaterialHelper.GetRenderers(toolPreview);
            if (renderers == null || renderers.Length == 0)
                return false;

            bool hasPoint = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                Bounds bounds = renderer.bounds;
                Vector3 extents = bounds.extents;

                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = new Vector3(
                        bounds.center.x + ((cornerIndex & 1) == 0 ? -extents.x : extents.x),
                        bounds.center.y + ((cornerIndex & 2) == 0 ? -extents.y : extents.y),
                        bounds.center.z + ((cornerIndex & 4) == 0 ? -extents.z : extents.z));

                    Vector3 screenPoint = cam.WorldToScreenPoint(corner);
                    if (screenPoint.z <= 0f)
                        continue;

                    hasPoint = true;
                    minX = Mathf.Min(minX, screenPoint.x);
                    minY = Mathf.Min(minY, screenPoint.y);
                    maxX = Mathf.Max(maxX, screenPoint.x);
                    maxY = Mathf.Max(maxY, screenPoint.y);
                }
            }

            if (!hasPoint)
                return false;

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return screenRect.width > 0f && screenRect.height > 0f;
        }

        private static Rect ExpandRect(Rect rect, float padding)
        {
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }
    }
}
