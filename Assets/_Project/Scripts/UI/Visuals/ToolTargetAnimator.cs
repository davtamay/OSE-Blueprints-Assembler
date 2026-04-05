using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Drives pulse animation, hover/ready colour, distance fade,
    /// and click/fail effects for tool-action target markers.
    /// </summary>
    internal sealed class ToolTargetAnimator
    {
        // ── Colours ──
        internal static readonly Color ToolTargetIdleColor  = new Color(0.25f, 0.9f, 1.0f, 0.62f);
        internal static readonly Color ToolTargetHoverColor = new Color(0.55f, 1.0f, 1.0f, 0.9f);
        internal static readonly Color ToolTargetFailColor  = new Color(1.0f, 0.35f, 0.25f, 0.9f);

        // ── Tuning ──
        private const float ToolTargetPulseSpeed       = 3.6f;
        private const float ToolTargetScalePulse       = 0.12f;
        private const float ToolTargetHeightPulse      = 0.05f;
        private const float ToolTargetFadeStartDistance = 3.0f;
        private const float ToolTargetFadeEndDistance   = 0.8f;

        private readonly ISiblingAccessContext _siblings;
        private readonly ToolTargetDetector _detector;
        private readonly List<GameObject> _spawnedTargets;

        private GameObject _hoveredTarget;
        private ToolActionTargetInfo _readyTarget;

        public ToolTargetAnimator(
            ISiblingAccessContext siblings,
            ToolTargetDetector detector,
            List<GameObject> spawnedTargets)
        {
            _siblings = siblings;
            _detector = detector;
            _spawnedTargets = spawnedTargets;
        }

        // ====================================================================
        //  Per-frame updates
        // ====================================================================

        /// <summary>Animates pulse, hover colour, and distance fade on all spawned targets.</summary>
        public void UpdateVisuals()
        {
            if (_spawnedTargets.Count == 0)
            {
                _hoveredTarget = null;
                _readyTarget = null;
                return;
            }

            _hoveredTarget = _detector.TryGetHoveredToolActionTarget(out ToolActionTargetInfo hoveredInfo)
                ? hoveredInfo.gameObject
                : null;

            Color idlePulseColor = ToolTargetIdleColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * ToolTargetPulseSpeed);
            float intensity = Mathf.Lerp(0.75f, 1.25f, pulse);
            idlePulseColor = new Color(
                Mathf.Clamp01(idlePulseColor.r * intensity),
                Mathf.Clamp01(idlePulseColor.g * intensity),
                Mathf.Clamp01(idlePulseColor.b * intensity),
                Mathf.Clamp01(0.55f + 0.35f * pulse));

            GameObject readyGo = _readyTarget != null ? _readyTarget.gameObject : null;
            Camera cam = CameraUtil.GetMain();

            for (int i = _spawnedTargets.Count - 1; i >= 0; i--)
            {
                GameObject target = _spawnedTargets[i];
                if (target == null)
                {
                    _spawnedTargets.RemoveAt(i);
                    continue;
                }

                Color targetColor = (target == _hoveredTarget || target == readyGo)
                    ? ToolTargetHoverColor
                    : idlePulseColor;

                if (cam != null)
                {
                    float dist = Vector3.Distance(cam.transform.position, target.transform.position);
                    if (dist < ToolTargetFadeStartDistance)
                    {
                        float t = Mathf.InverseLerp(ToolTargetFadeEndDistance, ToolTargetFadeStartDistance, dist);
                        targetColor.a *= t;
                    }
                }

                MaterialHelper.SetMaterialColor(target, targetColor);

                ToolActionTargetInfo info = target.GetComponent<ToolActionTargetInfo>();
                Vector3 baseScale = info != null && info.BaseScale.sqrMagnitude > 0f
                    ? info.BaseScale
                    : target.transform.localScale;
                float scaleFactor = 1f + (ToolTargetScalePulse * pulse);
                target.transform.localScale = baseScale * scaleFactor;

                Vector3 baseLocalPosition = info != null
                    ? info.BaseLocalPosition
                    : target.transform.localPosition;
                target.transform.localPosition = baseLocalPosition + (Vector3.up * (ToolTargetHeightPulse * (pulse - 0.5f)));
            }
        }

        /// <summary>Updates cursor ready-state based on tool-preview overlap with targets.</summary>
        public void UpdateCursorProximity()
        {
            var cursorManager = _siblings.CursorManager;
            if (cursorManager == null)
                return;

            if (_spawnedTargets.Count == 0)
            {
                _readyTarget = null;
                if (cursorManager.CursorInReadyState)
                    cursorManager.RestoreColor();
                return;
            }

            if (!_detector.TryGetReadyToolActionTarget(out ToolActionTargetInfo readyTarget))
            {
                _readyTarget = null;
                if (cursorManager.CursorInReadyState)
                    cursorManager.RestoreColor();
                return;
            }

            _readyTarget = readyTarget;
            cursorManager.SetReadyState(true);
        }

        // ====================================================================
        //  Effects
        // ====================================================================

        /// <summary>Applies fail-flash colour to all spawned tool targets.</summary>
        public void FlashOnFailure()
        {
            for (int i = 0; i < _spawnedTargets.Count; i++)
            {
                if (_spawnedTargets[i] == null) continue;
                MaterialHelper.ApplyToolTargetMarker(_spawnedTargets[i], ToolTargetFailColor);
            }
        }

        /// <summary>
        /// Spawns a click completion effect (ring + optional particle) on the marker
        /// matching <paramref name="targetId"/>.
        /// </summary>
        public void SpawnClickEffect(
            string targetId,
            string activeProfile,
            StepProfile activeProfileEnum,
            Color completionEffectColor,
            float completionPulseScale,
            string completionParticleId,
            out Vector3? anchorWorldPos,
            string measureStartAnchorTargetId)
        {
            anchorWorldPos = null;

            if (string.IsNullOrEmpty(activeProfile))
                return;

            if (!ToolProfileRegistry.Get(activeProfile).SpawnClickEffect)
                return;

            bool isMeasure = activeProfileEnum == StepProfile.Measure;

            for (int i = 0; i < _spawnedTargets.Count; i++)
            {
                GameObject marker = _spawnedTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Vector3 markerWorldPos = marker.transform.position;

                ToolActionClickEffect.Spawn(markerWorldPos, marker.transform.localScale,
                    completionEffectColor, completionPulseScale);
                CompletionParticleEffect.TrySpawn(completionParticleId,
                    markerWorldPos, marker.transform.localScale);

                if (isMeasure && !string.IsNullOrEmpty(measureStartAnchorTargetId) &&
                    string.Equals(targetId, measureStartAnchorTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    anchorWorldPos = markerWorldPos;
                }

                return;
            }
        }

        /// <summary>Sets the material colour on the marker matching the given target id.</summary>
        public void SetTargetColor(string targetId, Color color)
        {
            for (int i = 0; i < _spawnedTargets.Count; i++)
            {
                var marker = _spawnedTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    MaterialHelper.SetMaterialColor(marker, color);
                    return;
                }
            }
        }
    }
}
