using System.Collections.Generic;
using OSE.App;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns all per-frame animation concerns for <see cref="PlaceStepHandler"/>:
    /// snap-to-target lerp, invalid flash, preview selection pulse, and
    /// required-part emission pulse.  Extracted so <c>PlaceStepHandler</c> can
    /// focus on placement logic only.
    /// </summary>
    internal sealed class PlaceStepAnimator
    {
        // ── Dependency ──
        private readonly IBridgeContext _ctx;

        // ── Snap animation ──
        private struct SnapEntry
        {
            public GameObject Part;
            public Vector3    TargetPos;
            public Quaternion TargetRot;
            public Vector3    TargetScale;
        }
        private readonly List<SnapEntry> _activeSnaps = new();

        // ── Invalid flash ──
        private struct FlashEntry
        {
            public GameObject Part;
            public Color      OriginalColor;
            public float      Timer;
        }
        private readonly List<FlashEntry> _activeFlashes = new();

        // ── Preview selection pulse ──
        private string _previewPulsePartId;
        private const float PreviewSelectedPulseSpeed = 3.0f;
        private static readonly Color PreviewSelectedPulseA = new Color(0.35f, 0.85f, 1.0f, 0.35f);
        private static readonly Color PreviewSelectedPulseB = new Color(0.55f, 1.0f, 0.70f, 0.70f);

        // ── Required part emission pulse ──
        private string[] _requiredPartIdsForStep;
        private const float RequiredPartPulseSpeed = 0.8f;
        private static readonly Color RequiredPartEmissionA = new Color(0.15f, 0.08f, 0.00f, 1f);
        private static readonly Color RequiredPartEmissionB = new Color(0.45f, 0.25f, 0.02f, 1f);

        // ──────────────────────────────────────────────────────────────────────

        public PlaceStepAnimator(IBridgeContext ctx) => _ctx = ctx;

        // ── Lifecycle ──

        /// <summary>Drive all active animations; call once per frame from <see cref="PlaceStepHandler.Update"/>.</summary>
        public void Update(float deltaTime)
        {
            UpdateSnapAnimation(deltaTime);
            UpdateInvalidFlash(deltaTime);
            UpdatePreviewSelectionPulse();
            UpdateRequiredPartPulse();
        }

        public void OnStepCompleted()
        {
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
        }

        public void Cleanup()
        {
            ClearRequiredPartEmission();
            _requiredPartIdsForStep = null;
        }

        // ── Snap ──

        public void BeginSnapToTarget(GameObject partGo, string selectionId, string targetId, Transform fallback)
        {
            if (partGo == null) return;

            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                if (_activeSnaps[i].Part == partGo)
                    _activeSnaps.RemoveAt(i);
            }

            if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                subassemblyController.IsProxy(partGo) &&
                subassemblyController.TryResolveTargetPose(targetId, out Vector3 proxyPos, out Quaternion proxyRot, out Vector3 proxyScale))
            {
                _activeSnaps.Add(new SnapEntry { Part = partGo, TargetPos = proxyPos, TargetRot = proxyRot, TargetScale = proxyScale });
                return;
            }

            if (TryResolveSnapPose(selectionId, targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale))
            {
                _activeSnaps.Add(new SnapEntry { Part = partGo, TargetPos = pos, TargetRot = rot, TargetScale = scale });
                return;
            }

            if (fallback != null)
            {
                _activeSnaps.Add(new SnapEntry
                {
                    Part       = partGo,
                    TargetPos  = fallback.localPosition,
                    TargetRot  = fallback.localRotation,
                    TargetScale = fallback.localScale
                });
            }
        }

        public bool TryResolveSnapPose(string partId, string targetId, out Vector3 pos, out Quaternion rot, out Vector3 scale)
        {
            if (TryGetPreviewTargetPose(targetId, out pos, out rot, out scale))
                return true;

            TargetPreviewPlacement tp = _ctx.Spawner.FindTargetPlacement(targetId);
            PartPreviewPlacement   pp = _ctx.Spawner.FindPartPlacement(partId);

            if (tp != null)
            {
                pos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                rot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;
            }
            else if (pp != null)
            {
                pos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                rot = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;
            }
            else
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
            }

            if (pp != null)
                scale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
            else if (tp != null)
                scale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
            else
                scale = Vector3.one;

            return tp != null || pp != null;
        }

        // ── Flash ──

        public void FlashInvalidSelection(GameObject targetGo, string selectionId)
        {
            if (targetGo == null) return;

            if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var subassemblyController) &&
                subassemblyController != null &&
                subassemblyController.IsProxy(targetGo))
            {
                foreach (GameObject member in subassemblyController.EnumerateMemberParts(targetGo))
                    FlashInvalid(member, member != null ? member.name : selectionId);
                return;
            }

            FlashInvalid(targetGo, selectionId);
        }

        // ── Preview selection pulse ──

        public void StartPreviewSelectionPulse(string partId)
        {
            StopPreviewSelectionPulse();
            _previewPulsePartId = partId;
        }

        public void StopPreviewSelectionPulse()
        {
            if (_previewPulsePartId == null) return;

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && info.MatchesSelectionId(_previewPulsePartId))
                    MaterialHelper.ApplyPreviewMaterial(preview);
            }
            _previewPulsePartId = null;
        }

        // ── Required part pulse ──

        public void RefreshRequiredPartIds(string stepId)
        {
            _requiredPartIdsForStep = null;

            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            if (!step.IsPlacement)
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null || partIds.Length == 0)
                return;

            var pending = new List<string>();
            for (int i = 0; i < partIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(partIds[i])) continue;
                PartPlacementState state = _ctx.GetPartState(partIds[i]);
                if (state != PartPlacementState.PlacedVirtually && state != PartPlacementState.Completed)
                    pending.Add(partIds[i]);
            }

            _requiredPartIdsForStep = pending.Count > 0 ? pending.ToArray() : null;

            if (_requiredPartIdsForStep != null)
                OseLog.Info($"[PlaceHandler] Required parts for step '{stepId}': {string.Join(", ", _requiredPartIdsForStep)}");
        }

        public void RemoveFromRequiredPartIds(string partId)
        {
            if (_requiredPartIdsForStep == null) return;

            int idx = -1;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                if (string.Equals(_requiredPartIdsForStep[i], partId, System.StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return;

            GameObject partGo = _ctx.FindSpawnedPart(partId);
            if (partGo != null)
                MaterialHelper.SetEmission(partGo, Color.black);

            if (_requiredPartIdsForStep.Length == 1)
            {
                _requiredPartIdsForStep = null;
                return;
            }

            string[] updated = new string[_requiredPartIdsForStep.Length - 1];
            int j = 0;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                if (i != idx) updated[j++] = _requiredPartIdsForStep[i];
            }
            _requiredPartIdsForStep = updated;
        }

        public void ClearRequiredPartEmission()
        {
            if (_requiredPartIdsForStep == null) return;
            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                GameObject partGo = _ctx.FindSpawnedPart(_requiredPartIdsForStep[i]);
                if (partGo != null)
                    MaterialHelper.SetEmission(partGo, Color.black);
            }
        }

        // ── Private helpers ──

        private void UpdateSnapAnimation(float deltaTime)
        {
            if (_activeSnaps.Count == 0) return;

            float t = StepHandlerConstants.Animation.SnapLerpSpeed * deltaTime;

            for (int i = _activeSnaps.Count - 1; i >= 0; i--)
            {
                var snap = _activeSnaps[i];
                if (snap.Part == null) { _activeSnaps.RemoveAt(i); continue; }

                snap.Part.transform.localPosition = Vector3.Lerp(snap.Part.transform.localPosition, snap.TargetPos, t);
                snap.Part.transform.localRotation = Quaternion.Slerp(snap.Part.transform.localRotation, snap.TargetRot, t);
                snap.Part.transform.localScale    = Vector3.Lerp(snap.Part.transform.localScale, snap.TargetScale, t);

                if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var sub) &&
                    sub != null && sub.IsProxy(snap.Part))
                {
                    sub.ApplyProxyTransform(snap.Part);
                }

                if (Vector3.Distance(snap.Part.transform.localPosition, snap.TargetPos) < 0.001f)
                {
                    snap.Part.transform.SetLocalPositionAndRotation(snap.TargetPos, snap.TargetRot);
                    snap.Part.transform.localScale = snap.TargetScale;
                    if (ServiceRegistry.TryGet<ISubassemblyPlacementService>(out var finalSub) &&
                        finalSub != null && finalSub.IsProxy(snap.Part))
                    {
                        finalSub.ApplyProxyTransform(snap.Part);
                    }
                    _activeSnaps.RemoveAt(i);
                }
            }
        }

        private void UpdateInvalidFlash(float deltaTime)
        {
            if (_activeFlashes.Count == 0) return;

            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                var flash = _activeFlashes[i];
                if (flash.Part == null) { _activeFlashes.RemoveAt(i); continue; }

                flash.Timer -= deltaTime;
                if (flash.Timer <= 0f)
                {
                    _ctx.RestorePartVisual(flash.Part);
                    _activeFlashes.RemoveAt(i);
                }
                else
                {
                    _activeFlashes[i] = flash;
                }
            }
        }

        private void UpdatePreviewSelectionPulse()
        {
            if (_previewPulsePartId == null || _ctx.SpawnedPreviews.Count == 0)
                return;

            Color pulseColor = ColorPulseHelper.Lerp(PreviewSelectedPulseA, PreviewSelectedPulseB, PreviewSelectedPulseSpeed);

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null) continue;
                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !info.MatchesSelectionId(_previewPulsePartId)) continue;
                MaterialHelper.SetMaterialColor(preview, pulseColor);
            }
        }

        private void UpdateRequiredPartPulse()
        {
            if (_requiredPartIdsForStep == null || _requiredPartIdsForStep.Length == 0)
                return;

            Color emissionColor = ColorPulseHelper.Lerp(RequiredPartEmissionA, RequiredPartEmissionB, RequiredPartPulseSpeed * Mathf.PI * 2f);

            for (int i = 0; i < _requiredPartIdsForStep.Length; i++)
            {
                string partId = _requiredPartIdsForStep[i];
                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo == null) continue;

                PartPlacementState state = _ctx.GetPartState(partId);
                if (state == PartPlacementState.Selected || state == PartPlacementState.Grabbed
                    || state == PartPlacementState.Inspected)
                {
                    MaterialHelper.SetEmission(partGo, Color.black);
                    continue;
                }

                if (state == PartPlacementState.PlacedVirtually || state == PartPlacementState.Completed)
                    continue;

                MaterialHelper.SetEmission(partGo, emissionColor);
            }
        }

        private void FlashInvalid(GameObject partGo, string partId)
        {
            for (int i = _activeFlashes.Count - 1; i >= 0; i--)
            {
                if (_activeFlashes[i].Part == partGo)
                    _activeFlashes.RemoveAt(i);
            }

            PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(partId);
            Color originalColor = pp != null
                ? new Color(pp.color.r, pp.color.g, pp.color.b, pp.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, StepHandlerConstants.Colors.InvalidFlash);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", StepHandlerConstants.Colors.InvalidFlash);

            _activeFlashes.Add(new FlashEntry
            {
                Part          = partGo,
                OriginalColor = originalColor,
                Timer         = StepHandlerConstants.Animation.InvalidFlashDuration
            });
        }

        private bool TryGetPreviewTargetPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale    = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _ctx.SpawnedPreviews.Count == 0)
                return false;

            PreviewSceneSetup setup       = _ctx.Setup;
            Transform         previewRoot = setup != null ? setup.PreviewRoot : null;

            for (int i = _ctx.SpawnedPreviews.Count - 1; i >= 0; i--)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null) continue;

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = preview.transform;
                if (previewRoot != null && tx.parent == previewRoot)
                {
                    position = tx.localPosition;
                    rotation = tx.localRotation;
                }
                else if (previewRoot != null)
                {
                    position = previewRoot.InverseTransformPoint(tx.position);
                    rotation = Quaternion.Inverse(previewRoot.rotation) * tx.rotation;
                }
                else
                {
                    position = tx.position;
                    rotation = tx.rotation;
                }

                scale = tx.localScale;
                return true;
            }
            return false;
        }
    }
}
