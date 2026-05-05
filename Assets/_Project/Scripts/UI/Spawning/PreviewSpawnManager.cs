using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages preview spawning for placement steps.
    /// Owns the <c>SpawnedPreviews</c> list, sequential-target state,
    /// and all Preview* static math helpers.
    /// Extracted from PartInteractionBridge (Phase 3).
    /// </summary>
    internal sealed class PreviewSpawnManager
    {
        private readonly IBridgeContext _ctx;

        private string _activeStepId;

        // Attached to the StepController's active TaskCursor. The cursor
        // is the SINGLE SOURCE OF TRUTH for span progression: its
        // TaskSpanOpened event is the sole trigger for preview spawning
        // (via OnSpanOpened → SpawnPreviewsForCurrentSpan). Its
        // StepTasksComplete event is the sole trigger for step completion
        // (via StepController.HandleCursorStepComplete). Any handler that
        // tracks "current target index" or calls CompleteStep directly
        // for a cursor-driven step will race the cursor and produce
        // phantom ghosts / double-completion (see 2026-04-24 step-41
        // regression — parallel legacy-sequential path spawned an extra
        // ghost at the wrong corner).
        private TaskCursor _attachedCursor;

        /// <summary>The shared preview list. Passed by reference to PlaceStepHandler / UseStepHandler.</summary>
        public List<GameObject> SpawnedPreviews => _ctx.SpawnedPreviews;

        public PreviewSpawnManager(IBridgeContext context)
        {
            _ctx = context;
        }

        // ── Public API ──

        public void SpawnPreviewsForStep(string stepId)
        {
            ClearPreviews();
            DetachFromStepCursor();
            _activeStepId = stepId;
            var package = _ctx.Spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Attach to the cursor FIRST, before any early returns. Even
            // steps that don't need ghost previews (Use, Confirm, Pipe)
            // still need the cursor attached so GetActionableToolActionTargetIds
            // can answer "which tool-target is actionable right now" — the
            // ToolTargetSpawner relies on it to filter sequential Use-step
            // markers to one-at-a-time. Without this, tool-action steps
            // fall back to "show every target" because their actionable
            // set reads empty.
            if (TryGetActiveCursor(out var cursor) && cursor.TotalSpans > 0)
                AttachToStepCursor(cursor);

            // Pipe connection steps are handled entirely by ConnectStepHandler via the router.
            if (step.IsPipeConnection)
                return;

            // Use-family steps (tool actions) operate on parts already in the scene.
            // No ghost preview needed — the real parts are visible at their stepPose.
            if (step.IsToolAction)
                return;

            // Confirm-family steps don't require part placement — they're
            // "acknowledge these parts are present / condition is met" steps.
            // When a Confirm step authors "part" entries in taskOrder (e.g.
            // step_batch_carriage_layout's "gather and lay out all 8 halves"),
            // those are task markers for cursor gating, NOT targets for
            // drag-to-place ghost rendering. Spawning ghosts here surfaces
            // placement UX for parts that don't need placing.
            if (step.IsConfirmation)
                return;

            string[] targetIds = step.targetIds;
            bool hasTargets = targetIds != null && targetIds.Length > 0;

            // Cursor-driven Place family: already attached above, OnSpanOpened
            // handles the spawn path. Defer — UNLESS the cursor has already
            // started (and thus already fired the initial TaskSpanOpened
            // before we subscribed). That happens whenever SpawnPreviewsForStep
            // runs from a *late* path: AnimationCueCoordinator's deferred
            // OnStepActivated callback (rebuildPreviewDelay > 0), navigation,
            // session restore. In those flows StepController.ActivateStep
            // (which calls cursor.Start) has already executed by the time we
            // get here, so the natural TaskSpanOpened delivery has already
            // happened to whoever was subscribed at that moment — not us.
            // Replay it manually so the ghost spawns. Mirrors the same
            // late-attach pattern in AnimationCueCoordinator.OnStepActivated.
            if (_attachedCursor != null)
            {
                if (_attachedCursor.HasStarted
                    && _attachedCursor.OpenTasks != null
                    && _attachedCursor.OpenTasks.Count > 0)
                {
                    OnSpanOpened(new TaskSpanOpenedInfo(
                        _attachedCursor.OpenTasks,
                        _attachedCursor.CurrentSetLabel,
                        _attachedCursor.SpanIndex,
                        _attachedCursor.TotalSpans));
                }
                return;
            }

            // Legacy fallback — content without taskOrder. Steps shipped
            // through MachinePackageNormalizer.EnsureTaskOrderCoversRequirements
            // always get a taskOrder that covers requiredPartIds, so this
            // branch only fires for pre-normalization code paths, tests,
            // or degenerate content. Always spawns all target ghosts at
            // once — no ordering (no cursor = no notion of "current task").
            if (hasTargets)
            {
                foreach (string targetId in targetIds)
                    SpawnPreviewForTarget(package, targetId);
                return;
            }

            // Target-less ghost fallback: when a step declares
            // requiredPartIds or requiredPartGroupId but no explicit
            // targetIds, spawn a ghost per part at its assembledPose.
            // Matches author intent "put this thing here" without
            // requiring a manually wired target — the assembled placement
            // IS the target. Ghost still uses the standard preview
            // material / click-to-place logic via a virtual target key.
            SpawnTargetlessGhostsFromRequiredParts(package, step);
        }

        // ── Phase I.g — TaskCursor integration ─────────────────────────────

        private static bool TryGetActiveCursor(out TaskCursor cursor)
        {
            cursor = null;
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session)) return false;
            cursor = session?.AssemblyController?.StepController?.CurrentTaskCursor;
            return cursor != null;
        }

        private void AttachToStepCursor(TaskCursor cursor)
        {
            if (_attachedCursor == cursor) return;
            DetachFromStepCursor();
            _attachedCursor = cursor;
            _attachedCursor.TaskSpanOpened += OnSpanOpened;
        }

        private void DetachFromStepCursor()
        {
            if (_attachedCursor == null) return;
            _attachedCursor.TaskSpanOpened -= OnSpanOpened;
            _attachedCursor = null;
        }

        private void OnSpanOpened(TaskSpanOpenedInfo info)
        {
            // Span advanced — replace the prior span's previews with the
            // new span's. The initial TaskSpanOpened fired at step
            // activation is already handled by SpawnPreviewsForStep's
            // initial SpawnPreviewsForCurrentSpan call, but re-running is
            // safe because ClearPreviews precedes SpawnPreviewsForCurrentSpan.
            if (_ctx?.Spawner?.CurrentPackage == null || string.IsNullOrEmpty(_activeStepId)) return;
            if (!_ctx.Spawner.CurrentPackage.TryGetStep(_activeStepId, out var step)) return;

            // Mirror SpawnPreviewsForStep's family gating. The cursor is
            // attached for Confirm / Use / Pipe steps too (so other systems
            // can read OpenTasks for filtering), but those families must
            // NEVER spawn drag-to-place ghosts. Without this gate, a Confirm
            // step that authors a kind="part" taskOrder entry (e.g.
            // step_batch_c1_shake_test referencing the carriage partGroup
            // for cursor gating) would surface a placement ghost over a
            // part the trainee has nothing to place.
            if (step.IsPipeConnection || step.IsToolAction || step.IsConfirmation)
                return;

            ClearPreviews();
            SpawnPreviewsForCurrentSpan(_ctx.Spawner.CurrentPackage, step);
        }

        /// <summary>
        /// Spawns a ghost preview for every Part-kind task in the currently-
        /// open cursor span. Each part's ghost uses the same
        /// <see cref="SpawnPreviewForTarget"/> path (for parts with explicit
        /// targets) or <see cref="SpawnPreviewForAssembledPart"/> fallback
        /// (for parts without targets). Unordered set members all get their
        /// ghosts simultaneously — that's the authoring intent of an
        /// unordered set.
        /// </summary>
        private void SpawnPreviewsForCurrentSpan(MachinePackageDefinition package, StepDefinition step)
        {
            if (_attachedCursor == null) return;

            var openPartIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in _attachedCursor.OpenTasks)
            {
                if (entry?.kind == "part" && !string.IsNullOrEmpty(entry.id))
                    openPartIds.Add(TaskInstanceId.ToPartId(entry.id));
            }
            if (openPartIds.Count == 0) return;

            // Prefer the explicit targets path — matches the step's authored
            // targetIds/partId association and uses the full SpawnPreview
            // pipeline (spline, step-pose overrides, etc.).
            var matchedPartIds = new HashSet<string>(StringComparer.Ordinal);
            if (step.targetIds != null)
            {
                foreach (var targetId in step.targetIds)
                {
                    if (string.IsNullOrEmpty(targetId)) continue;
                    if (!package.TryGetTarget(targetId, out var t)) continue;
                    if (string.IsNullOrEmpty(t.associatedPartId)) continue;
                    if (!openPartIds.Contains(t.associatedPartId)) continue;
                    SpawnPreviewForTarget(package, targetId);
                    matchedPartIds.Add(t.associatedPartId);
                }
            }

            // Targetless fallback — open parts whose partId didn't match any
            // step target (either the step has no targets at all, or the
            // target's associatedPartId points elsewhere). Spawn a ghost at
            // the part's assembledPose so the author's intent "place this
            // here" still shows up visually even without a target wired.
            foreach (var pid in openPartIds)
            {
                if (!matchedPartIds.Contains(pid))
                    SpawnPreviewForAssembledPart(package, pid);
            }

        }

        /// <summary>
        /// Ghost fallback when a step has no <c>targetIds</c>. Enumerates
        /// the parts the step requires (<c>requiredPartIds</c> plus every
        /// member of <c>requiredPartGroupId</c>) and spawns a ghost at
        /// each part's <c>assembledPose</c>. Idempotent re-use of
        /// <see cref="SpawnPreviewForAssembledPart"/> so visual + material
        /// + click-to-place behaviour match the targeted path.
        /// </summary>
        private void SpawnTargetlessGhostsFromRequiredParts(MachinePackageDefinition package, StepDefinition step)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (step.requiredPartIds != null)
            {
                foreach (var pid in step.requiredPartIds)
                    if (!string.IsNullOrEmpty(pid) && seen.Add(pid))
                        SpawnPreviewForAssembledPart(package, pid);
            }
            if (!string.IsNullOrEmpty(step.requiredPartGroupId)
                && package.TryGetPartGroup(step.requiredPartGroupId, out var sub)
                && sub?.partIds != null)
            {
                foreach (var pid in sub.partIds)
                    if (!string.IsNullOrEmpty(pid) && seen.Add(pid))
                        SpawnPreviewForAssembledPart(package, pid);
            }
        }

        /// <summary>
        /// Returns the target IDs that are currently actionable, sourced
        /// from the attached <see cref="TaskCursor"/>'s current span. For
        /// each open toolAction entry in the span, resolves the backing
        /// <see cref="ToolActionDefinition.targetId"/> from the step's
        /// <c>requiredToolActions</c>. Empty array when no cursor, no
        /// open spans, or no toolAction entries. Callers use this to
        /// decide "which tool-target marker(s) should be visible right
        /// now" — always ask the cursor, never track a separate index.
        /// </summary>
        public string[] GetActionableToolActionTargetIds()
        {
            if (_attachedCursor == null || _attachedCursor.IsComplete)
                return System.Array.Empty<string>();

            var open = _attachedCursor.OpenTasks;
            if (open == null || open.Count == 0)
                return System.Array.Empty<string>();

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return System.Array.Empty<string>();
            StepDefinition step = session?.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return System.Array.Empty<string>();

            var result = new List<string>(open.Count);
            for (int i = 0; i < open.Count; i++)
            {
                var entry = open[i];
                if (entry == null || !string.Equals(entry.kind, "toolAction", StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(entry.id)) continue;
                for (int a = 0; a < step.requiredToolActions.Length; a++)
                {
                    var action = step.requiredToolActions[a];
                    if (action == null) continue;
                    if (!string.Equals(action.id, entry.id, StringComparison.Ordinal)) continue;
                    if (!string.IsNullOrWhiteSpace(action.targetId))
                        result.Add(action.targetId.Trim());
                    break;
                }
            }
            return result.ToArray();
        }

        public GameObject FindPreviewForTarget(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return null;

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null)
                    continue;

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    return preview;
            }

            return null;
        }

        public void ClearPreviews()
        {
            foreach (var preview in _ctx.SpawnedPreviews)
            {
                if (preview == null) continue;
                // SetActive(false) + reparent-out-of-PreviewRoot first.
                // Unity's Destroy() is end-of-frame deferred, so without
                // this, the doomed preview renders once more in the frame
                // that spawned its replacement and looks like "2 ghosts"
                // to the user. Deactivating instantly hides it; reparenting
                // to null guarantees PreviewRoot doesn't drag it along even
                // if active state somehow doesn't propagate; Destroy() then
                // reclaims the memory at frame end.
                preview.SetActive(false);
                preview.transform.SetParent(null, worldPositionStays: false);
                _ctx.DestroyObject(preview);
            }
            _ctx.SpawnedPreviews.Clear();

            // Also nuke any edit-mode ghost silhouettes (EditGhost_*) that may
            // have leaked into play mode. EditModeGhostManager spawns these in
            // the editor; if "Reload Scene" is disabled in Enter Play Mode
            // settings, the dynamically-instantiated ghosts survive into play
            // mode and never get cleared because EditModeGhostManager.SpawnGhosts
            // early-returns when isPlaying. Step navigation is the right hook
            // because it's the cleanup point for everything else.
            _ctx.Spawner?.ClearGhosts();
        }

        // ── Preview spawning ──

        private void SpawnPreviewForTarget(MachinePackageDefinition package, string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) { OseLog.Warn("[PartInteraction] SpawnPreviewForTarget: targetId is null/empty."); return; }
            if (!package.TryGetTarget(targetId, out var target)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: target '{targetId}' not found in package."); return; }

            if (!string.IsNullOrWhiteSpace(target.associatedPartGroupId))
            {
                SpawnPreviewForPartGroupTarget(package, targetId, target);
                return;
            }

            string associatedPartId = target.associatedPartId;
            if (string.IsNullOrEmpty(associatedPartId)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: target '{targetId}' has no associatedPartId."); return; }
            if (!package.TryGetPart(associatedPartId, out var part)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' not found in package."); return; }

            // Skip preview for parts already placed or completed from a prior step.
            if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
            {
                var partState = partController.GetPartState(associatedPartId);
                if (partState == PartPlacementState.Completed ||
                    partState == PartPlacementState.PlacedVirtually)
                {
                    OseLog.VerboseInfo($"[PartInteraction] SpawnPreviewForTarget: skipping preview for '{associatedPartId}' — already {partState}.");
                    return;
                }
                OseLog.VerboseInfo($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' state={partState}, proceeding with preview.");
            }

            Transform previewRoot = GetPreviewRoot();

            TargetPreviewPlacement tp = _ctx.Spawner.FindTargetPlacement(targetId);
            PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(associatedPartId);

            // --- Spline parts: create a procedural preview tube instead of loading a GLB ---
            if (SplinePartFactory.HasSplineData(pp))
            {
                GameObject splinePreview = SplinePartFactory.CreatePreview(associatedPartId, pp.splinePath, previewRoot);
                if (splinePreview == null)
                {
                    OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: failed to create spline preview for '{associatedPartId}'.");
                    return;
                }

                PlacementPreviewInfo splineInfo = splinePreview.AddComponent<PlacementPreviewInfo>();
                splineInfo.TargetId = targetId;
                splineInfo.PartId = associatedPartId;

                // Spline preview sits at play position (0,0,0) / scale (1,1,1) — knots define the routing
                splinePreview.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                splinePreview.transform.localScale = Vector3.one;

                // Convert existing colliders to triggers for click-to-place
                foreach (var col in splinePreview.GetComponentsInChildren<Collider>(true))
                    col.isTrigger = true;

                MaterialHelper.ApplyPreviewMaterial(splinePreview);
                _ctx.SpawnedPreviews.Add(splinePreview);
                OseLog.Info($"[PartInteraction] Spline preview spawned for '{associatedPartId}' at target '{targetId}'. Total previews: {_ctx.SpawnedPreviews.Count}");
                return;
            }

            // --- Standard GLB-based preview ---
            string previewRef = part.assetRef;
            if (string.IsNullOrEmpty(previewRef)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' has no assetRef."); return; }

            Vector3 previewPos;
            Quaternion previewRot;
            Vector3 previewScale;

            // Check for step-scoped pose override first, then fall back to assembledPosition.
            // TargetPreviewPlacement is only used as fallback for targets without
            // an associated part placement (tool-action targets, checkpoints, etc.).
            StepPoseEntry stepPose = _ctx.Spawner.FindPartStepPose(associatedPartId, _activeStepId);
            if (stepPose != null)
            {
                previewPos = new Vector3(stepPose.position.x, stepPose.position.y, stepPose.position.z);
                previewRot = !stepPose.rotation.IsIdentity
                    ? new Quaternion(stepPose.rotation.x, stepPose.rotation.y, stepPose.rotation.z, stepPose.rotation.w)
                    : Quaternion.identity;
            }
            else if (pp != null)
            {
                previewPos = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                previewRot = !pp.assembledRotation.IsIdentity
                    ? new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w)
                    : Quaternion.identity;
            }
            else if (tp != null)
            {
                previewPos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                previewRot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;
            }
            else
            {
                previewPos = Vector3.zero;
                previewRot = Quaternion.identity;
            }

            // Preview should mirror the live source part dimensions exactly.
            // In editor, the placeholder is the actual GLB so its localScale
            // is the right scale to mirror. In builds the placeholder is a
            // 1m³ primitive Cube — copying the part's localScale (often
            // (1,1,1) for unit-scale parts whose size lives in the mesh)
            // produces a 1m cube ghost that looks like a giant block. Use
            // the part's renderer bounds for builds so the cube is sized to
            // match the actual mesh silhouette.
            GameObject sourcePart = _ctx.FindSpawnedPart(associatedPartId);
            if (sourcePart != null)
            {
#if UNITY_EDITOR
                previewScale = sourcePart.transform.localScale;
#else
                if (TryGetMeshLocalSize(sourcePart, out Vector3 boundsSize) && boundsSize.sqrMagnitude > 0.0001f)
                    previewScale = boundsSize;
                else
                    previewScale = sourcePart.transform.localScale;
#endif
            }
            else if (pp != null)
            {
                Vector3 authoredStartScale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                Vector3 authoredPlayScale = new Vector3(pp.assembledScale.x, pp.assembledScale.y, pp.assembledScale.z);
                previewScale = authoredStartScale.sqrMagnitude > 0f
                    ? authoredStartScale
                    : (authoredPlayScale.sqrMagnitude > 0f ? authoredPlayScale : Vector3.one);
            }
            else if (tp != null)
            {
                previewScale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
            }
            else
            {
                previewScale = Vector3.one * 0.5f;
            }

            // Editor EDIT mode (TTAW preview) uses sync AssetDatabase to skip
            // the placeholder dance — the authoring workflow renders real GLBs
            // immediately. Editor PLAY mode AND builds both go through the
            // async path: a primitive Cube placeholder holds the click collider
            // and serves as a visual fallback for parts without a loadable GLB
            // (frames). The cube's renderer is HIDDEN during the async load
            // window so the user doesn't see a giant 1m block flash before
            // the real ghost arrives. SwapInRealGhostAsync re-enables the
            // renderer if the swap fails (frames stay as cubes); on success
            // the cube is destroyed and the real GLB takes over.
            GameObject preview = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                preview = _ctx.Spawner.TryLoadPackageAsset(previewRef);
#endif
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (previewRoot != null)
                    preview.transform.SetParent(previewRoot, false);

                // Hide the placeholder's renderer immediately so the cube
                // doesn't flash before swap-in. Re-enabled by
                // SwapInRealGhostAsync when the load fails (cube fallback).
                if (Application.isPlaying)
                {
                    foreach (var r in preview.GetComponentsInChildren<Renderer>(true))
                        r.enabled = false;
                }
            }

            preview.name = $"Preview_{associatedPartId}";
            PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
            if (info == null)
                info = preview.AddComponent<PlacementPreviewInfo>();
            info.TargetId = targetId;
            info.PartId = associatedPartId;
            preview.transform.SetLocalPositionAndRotation(previewPos, previewRot);
            preview.transform.localScale = previewScale;

            foreach (var col in preview.GetComponentsInChildren<Collider>(true))
                _ctx.DestroyObject(col);

            // Fit a BoxCollider to the combined renderer bounds so the trigger
            // is accurate to the mesh shape rather than an oversized sphere.
            var previewRenderers = MaterialHelper.GetRenderers(preview);
            var clickCollider = preview.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            if (previewRenderers.Length > 0)
            {
                Bounds combined = previewRenderers[0].bounds;
                for (int ri = 1; ri < previewRenderers.Length; ri++)
                    combined.Encapsulate(previewRenderers[ri].bounds);
                Vector3 lossyScale = preview.transform.lossyScale;
                clickCollider.center = preview.transform.InverseTransformPoint(combined.center);
                clickCollider.size = new Vector3(
                    lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                    lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                    lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            }

            MaterialHelper.ApplyPreviewMaterial(preview);
            _ctx.SpawnedPreviews.Add(preview);
            OseLog.Info($"[PartInteraction] Preview spawned for '{associatedPartId}' at target '{targetId}' pos={previewPos} scale={previewScale}. Total previews: {_ctx.SpawnedPreviews.Count}");

            // Mesh swap-in. Mirrors the pattern PackagePartSpawner.SpawnGlbPartsAsync
            // uses for actual parts: keep the placeholder for clicks + bounds,
            // async-load the real GLB, swap it in preserving transform + click
            // info. Runs in editor PLAY mode AND builds — both pay the same
            // load latency, so behavior is identical and build-only async-timing
            // bugs surface during editor iteration. Editor EDIT mode (TTAW
            // preview) skips this — TryLoadPackageAsset above already produced
            // a real GLB synchronously, so the cube is only used when no asset
            // resolves and there's nothing to swap to.
            if (Application.isPlaying)
                _ = SwapInRealGhostAsync(preview, associatedPartId, targetId, previewRef, previewPos, previewRot, previewScale);
        }

        /// <summary>
        /// Build-side: replace a cube placeholder ghost with the real GLB
        /// loaded asynchronously, preserving the placeholder's transform,
        /// PlacementPreviewInfo, and click collider so click handling
        /// continues to work through the swap. Mirrors the pattern
        /// <see cref="PackagePartSpawner.SpawnGlbPartsAsync"/> uses to
        /// upgrade primitive part placeholders into real meshes.
        /// Fire-and-forget: if the placeholder was destroyed before the
        /// load returns (preview cleared, step changed, package reloaded),
        /// the freshly-loaded GLB is destroyed too so it doesn't leak.
        /// </summary>
        private async System.Threading.Tasks.Task SwapInRealGhostAsync(
            GameObject placeholder,
            string partId,
            string targetId,
            string assetRef,
            Vector3 localPos,
            Quaternion localRot,
            Vector3 localScale)
        {
            if (placeholder == null) return;
            var spawner = _ctx?.Spawner;
            if (spawner == null) return;

            // Announce the load so a loading affordance can render at the
            // ghost's anchor. World pos derived from the placeholder we just
            // spawned — that's where the user's attention is. Subscribers
            // (GhostLoadingIndicator) apply their own perceptual threshold
            // before showing anything; we just signal start/finish.
            Vector3 anchorWorld = placeholder.transform.position;
            RuntimeEventBus.Publish(new GhostLoadStarted(
                partId, targetId, anchorWorld.x, anchorWorld.y, anchorWorld.z));

            // Use the resolver-aware ghost loader so combined-GLB node parts
            // route through LoadCombinedNodeAsync and bare filenames get the
            // assets/parts/ prefix — same logic SpawnGlbPartsAsync uses for
            // actual parts. Passing the raw assetRef directly (as the prior
            // version did) silently failed in builds for combined-node parts
            // (frames, multi-part GLBs) because LoadPackageAssetAsync alone
            // can't open a node inside a combined file.
            GameObject loaded = null;
            try { loaded = await spawner.LoadGhostAssetAsync(partId); }
            catch (System.Exception ex)
            {
                OseLog.Warn($"[PartInteraction] Ghost swap-in: LoadGhostAssetAsync threw for '{partId}' (ref='{assetRef}'): {ex.Message}");
                ShowPlaceholderAsCubeFallback(placeholder);
                RuntimeEventBus.Publish(new GhostLoadCompleted(partId, targetId, success: false));
                return;
            }
            if (loaded == null)
            {
                OseLog.VerboseInfo($"[PartInteraction] Ghost swap-in: no asset loaded for '{partId}' (ref='{assetRef}'). Re-showing cube placeholder as fallback.");
                ShowPlaceholderAsCubeFallback(placeholder);
                RuntimeEventBus.Publish(new GhostLoadCompleted(partId, targetId, success: false));
                return;
            }

            // Placeholder may have been destroyed during the load (preview
            // cleared on step change, package reloaded, etc.). Discard the
            // freshly-loaded GLB rather than parenting it to a stale tree.
            if (placeholder == null)
            {
                UnityEngine.Object.Destroy(loaded);
                return;
            }
            // Spawned-previews list mutates during ClearPreviews etc. Confirm
            // our placeholder is still tracked before swapping; if not, the
            // owner has already torn it down and we should not resurrect it.
            int idx = _ctx.SpawnedPreviews.IndexOf(placeholder);
            if (idx < 0)
            {
                UnityEngine.Object.Destroy(loaded);
                return;
            }

            // Hide the loaded GLB instantly (mirrors ToolCursorManager's
            // post-await hide). Between this point and the parent/scale
            // writes below, the GLB is at native scale + world origin.
            loaded.SetActive(false);

            Transform previewRoot = _ctx?.Setup?.PreviewRoot;
            loaded.name = $"Preview_{partId}";
            if (previewRoot != null)
                loaded.transform.SetParent(previewRoot, worldPositionStays: false);
            loaded.transform.SetLocalPositionAndRotation(localPos, localRot);

            // Use the live source part's localScale for the GLB, not the
            // placeholder's bounds-sized scale. The cube placeholder needed
            // bounds-size because its mesh is 1m³ (so scale must encode the
            // part's actual size). The GLB carries its mesh dimensions
            // internally, so it needs the same authored localScale that
            // SpawnGlbPartsAsync applies to the real part — anything else
            // double-counts the GLB's native size and produces an invisible
            // (too small) or off-screen-huge ghost.
            GameObject sourcePart = _ctx.FindSpawnedPart(partId);
            Vector3 glbScale = sourcePart != null && sourcePart.transform.localScale.sqrMagnitude > 0.000001f
                ? sourcePart.transform.localScale
                : Vector3.one;
            loaded.transform.localScale = glbScale;

            // Strip incoming colliders so they don't fight the click trigger.
            foreach (var col in loaded.GetComponentsInChildren<Collider>(true))
                _ctx.DestroyObject(col);

            // Re-fit the click trigger to the real mesh bounds.
            var loadedRenderers = MaterialHelper.GetRenderers(loaded);
            var clickCollider = loaded.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            if (loadedRenderers.Length > 0)
            {
                Bounds combined = loadedRenderers[0].bounds;
                for (int ri = 1; ri < loadedRenderers.Length; ri++)
                    combined.Encapsulate(loadedRenderers[ri].bounds);
                Vector3 lossyScale = loaded.transform.lossyScale;
                clickCollider.center = loaded.transform.InverseTransformPoint(combined.center);
                clickCollider.size = new Vector3(
                    lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                    lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                    lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            }

            // Carry over the PlacementPreviewInfo identity so click routing
            // resolves the same target/part as the placeholder did.
            var loadedInfo = loaded.GetComponent<PlacementPreviewInfo>();
            if (loadedInfo == null)
                loadedInfo = loaded.AddComponent<PlacementPreviewInfo>();
            loadedInfo.TargetId = targetId;
            loadedInfo.PartId = partId;

            MaterialHelper.ApplyPreviewMaterial(loaded);

            // Atomic swap: replace the placeholder in the tracked list,
            // activate the GLB, destroy the cube. Order matters — destroy
            // last so any lingering frame doesn't see two ghosts.
            _ctx.SpawnedPreviews[idx] = loaded;
            loaded.SetActive(true);
            UnityEngine.Object.Destroy(placeholder);

            RuntimeEventBus.Publish(new GhostLoadCompleted(partId, targetId, success: true));
            OseLog.Info($"[PartInteraction] Ghost swapped to real GLB for '{partId}' at target '{targetId}'.");
        }

        /// <summary>
        /// Re-enables the placeholder cube's renderer when the async swap-in
        /// can't produce a real GLB (asset not resolvable, load threw, etc.).
        /// Frames + parts that are intentionally rendered as primitives end up
        /// here — the cube IS their visual, hidden during the load window
        /// only as a courtesy to parts that DO have a GLB. Once we know the
        /// swap won't happen, the cube needs to be visible.
        /// </summary>
        private static void ShowPlaceholderAsCubeFallback(GameObject placeholder)
        {
            if (placeholder == null) return;
            foreach (var r in placeholder.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }

        /// <summary>
        /// Returns the source part's combined renderer bounds size, expressed
        /// in the units the placeholder cube needs as <c>localScale</c> when
        /// parented under PreviewRoot. The primitive Cube's mesh is 1m³ in
        /// mesh-local space, so its world size = localScale × parent.lossyScale.
        /// We invert by previewRoot.lossyScale so the rendered ghost matches
        /// the part's actual world dimensions instead of being a 1m placeholder.
        /// Used only by the build-side ghost path (the editor uses the real
        /// GLB, which carries its own mesh dimensions).
        /// </summary>
        private bool TryGetMeshLocalSize(GameObject sourcePart, out Vector3 size)
        {
            size = Vector3.zero;
            if (sourcePart == null) return false;
            var renderers = sourcePart.GetComponentsInChildren<Renderer>(false);
            if (renderers == null || renderers.Length == 0) return false;
            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);
            if (combined.size.sqrMagnitude < 0.0001f) return false;

            Transform pr = _ctx?.Spawner?.PreviewRoot;
            Vector3 prScale = pr != null ? pr.lossyScale : Vector3.one;
            size = new Vector3(
                prScale.x != 0f ? combined.size.x / prScale.x : combined.size.x,
                prScale.y != 0f ? combined.size.y / prScale.y : combined.size.y,
                prScale.z != 0f ? combined.size.z / prScale.z : combined.size.z);
            return true;
        }

        /// <summary>
        /// Target-less ghost: spawns a preview at <paramref name="partId"/>'s
        /// <c>assembledPose</c> without needing a <see cref="TargetDefinition"/>.
        /// Used when a step declares <c>requiredPartIds</c> or
        /// <c>requiredPartGroupId</c> but no <c>targetIds</c> — the
        /// assembled placement IS the target visually. Click-to-place
        /// against this ghost uses the synthetic target key
        /// <c>__auto_{partId}</c> so existing placement handlers can key off
        /// the PlacementPreviewInfo.PartId field directly.
        /// </summary>
        private void SpawnPreviewForAssembledPart(MachinePackageDefinition package, string partId)
        {
            if (string.IsNullOrEmpty(partId)) return;
            if (!package.TryGetPart(partId, out var part) || part == null) return;

            // Skip if already placed / completed.
            if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
            {
                var state = partController.GetPartState(partId);
                if (state == PartPlacementState.Completed ||
                    state == PartPlacementState.PlacedVirtually)
                    return;
            }

            PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(partId);
            if (pp == null) return;

            Transform previewRoot = GetPreviewRoot();

            // Spline parts: reuse the procedural preview path if applicable.
            if (SplinePartFactory.HasSplineData(pp))
            {
                GameObject splinePreview = SplinePartFactory.CreatePreview(partId, pp.splinePath, previewRoot);
                if (splinePreview == null) return;
                var splineInfo = splinePreview.AddComponent<PlacementPreviewInfo>();
                splineInfo.TargetId = $"__auto_{partId}";
                splineInfo.PartId = partId;
                splinePreview.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                splinePreview.transform.localScale = Vector3.one;
                foreach (var col in splinePreview.GetComponentsInChildren<Collider>(true))
                    col.isTrigger = true;
                MaterialHelper.ApplyPreviewMaterial(splinePreview);
                _ctx.SpawnedPreviews.Add(splinePreview);
                return;
            }

            if (string.IsNullOrEmpty(part.assetRef)) return;

            // Prefer step-scoped pose, then assembledPose.
            Vector3 previewPos;
            Quaternion previewRot;
            StepPoseEntry stepPose = _ctx.Spawner.FindPartStepPose(partId, _activeStepId);
            if (stepPose != null)
            {
                previewPos = new Vector3(stepPose.position.x, stepPose.position.y, stepPose.position.z);
                previewRot = !stepPose.rotation.IsIdentity
                    ? new Quaternion(stepPose.rotation.x, stepPose.rotation.y, stepPose.rotation.z, stepPose.rotation.w)
                    : Quaternion.identity;
            }
            else
            {
                previewPos = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                previewRot = !pp.assembledRotation.IsIdentity
                    ? new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w)
                    : Quaternion.identity;
            }

            GameObject sourcePart = _ctx.FindSpawnedPart(partId);
            Vector3 previewScale;
            if (sourcePart != null)
            {
#if UNITY_EDITOR
                previewScale = sourcePart.transform.localScale;
#else
                if (TryGetMeshLocalSize(sourcePart, out Vector3 boundsSize) && boundsSize.sqrMagnitude > 0.0001f)
                    previewScale = boundsSize;
                else
                    previewScale = sourcePart.transform.localScale;
#endif
            }
            else
            {
                previewScale = new Vector3(pp.assembledScale.x, pp.assembledScale.y, pp.assembledScale.z);
            }
            if (previewScale.sqrMagnitude < 0.00001f) previewScale = Vector3.one;

            // Editor EDIT mode: sync AssetDatabase. Editor PLAY + build:
            // cube placeholder with renderer hidden during async load,
            // re-enabled if the swap fails (frame-as-cube fallback).
            // See SpawnPreviewForTarget for full rationale.
            GameObject preview = null;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                preview = _ctx.Spawner.TryLoadPackageAsset(part.assetRef);
#endif
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (previewRoot != null) preview.transform.SetParent(previewRoot, false);
                if (Application.isPlaying)
                {
                    foreach (var r in preview.GetComponentsInChildren<Renderer>(true))
                        r.enabled = false;
                }
            }

            preview.name = $"Preview_{partId}";
            PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>() ?? preview.AddComponent<PlacementPreviewInfo>();
            info.TargetId = $"__auto_{partId}";
            info.PartId = partId;
            preview.transform.SetLocalPositionAndRotation(previewPos, previewRot);
            preview.transform.localScale = previewScale;

            foreach (var col in preview.GetComponentsInChildren<Collider>(true))
                _ctx.DestroyObject(col);

            var previewRenderers = MaterialHelper.GetRenderers(preview);
            var clickCollider = preview.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            if (previewRenderers.Length > 0)
            {
                Bounds combined = previewRenderers[0].bounds;
                for (int ri = 1; ri < previewRenderers.Length; ri++)
                    combined.Encapsulate(previewRenderers[ri].bounds);
                Vector3 lossyScale = preview.transform.lossyScale;
                clickCollider.center = preview.transform.InverseTransformPoint(combined.center);
                clickCollider.size = new Vector3(
                    lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                    lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                    lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            }

            MaterialHelper.ApplyPreviewMaterial(preview);
            _ctx.SpawnedPreviews.Add(preview);
            OseLog.Info($"[PartInteraction] Target-less ghost spawned for '{partId}' at assembledPose. Total previews: {_ctx.SpawnedPreviews.Count}");

            if (Application.isPlaying)
                _ = SwapInRealGhostAsync(preview, partId, $"__auto_{partId}", part.assetRef, previewPos, previewRot, previewScale);
        }

        private void SpawnPreviewForPartGroupTarget(MachinePackageDefinition package, string targetId, TargetDefinition target)
        {
            string partGroupId = target.associatedPartGroupId;
            if (string.IsNullOrWhiteSpace(partGroupId))
                return;

            var subCtrl = _ctx.PartGroupController;
            if (subCtrl == null || !subCtrl.IsPartGroupReady(partGroupId))
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: partGroup '{partGroupId}' is not ready for placement.");
                return;
            }

            if (!package.TryGetPartGroup(partGroupId, out PartGroupDefinition partGroup) || partGroup == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: partGroup '{partGroupId}' not found in package.");
                return;
            }

            PartGroupPreviewPlacement frame = _ctx.Spawner.FindPartGroupPlacement(partGroupId);
            if (frame == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: partGroup '{partGroupId}' has no authored preview frame.");
                return;
            }

            Transform previewRoot = GetPreviewRoot();
            GameObject subPreviewRoot = new GameObject($"Preview_{partGroupId}");
            if (previewRoot != null)
                subPreviewRoot.transform.SetParent(previewRoot, false);

            if (subCtrl.TryResolveTargetPose(targetId, out Vector3 previewPos, out Quaternion previewRot, out Vector3 previewScale))
            {
                subPreviewRoot.transform.SetLocalPositionAndRotation(previewPos, previewRot);
                subPreviewRoot.transform.localScale = previewScale;
            }
            else
            {
                subPreviewRoot.transform.SetLocalPositionAndRotation(
                    ToVector3(frame.position),
                    ToQuaternion(frame.rotation));
                subPreviewRoot.transform.localScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);
            }

            Vector3 framePos = ToVector3(frame.position);
            Quaternion frameRot = ToQuaternion(frame.rotation);
            Vector3 frameScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);
            IntegratedPartGroupPreviewPlacement integratedPlacement = _ctx.Spawner.FindIntegratedPartGroupPlacement(partGroupId, targetId);
            ConstrainedPartGroupFitPreviewPlacement fitPlacement = _ctx.Spawner.FindConstrainedPartGroupFitPlacement(partGroupId, targetId);
            Vector3 fitAxisLocal = fitPlacement != null ? ToVector3(fitPlacement.fitAxisLocal) : Vector3.zero;
            if (fitAxisLocal.sqrMagnitude > 0.000001f)
                fitAxisLocal.Normalize();
            float fitTravel = fitPlacement != null
                ? Mathf.Clamp(
                    fitPlacement.completionTravel,
                    Mathf.Min(fitPlacement.minTravel, fitPlacement.maxTravel),
                    Mathf.Max(fitPlacement.minTravel, fitPlacement.maxTravel))
                : 0f;
            bool isAxisFitPreview = fitPlacement?.drivenPartIds != null && fitPlacement.drivenPartIds.Length > 0;
            HashSet<string> fitDrivenPartIds = isAxisFitPreview
                ? new HashSet<string>(fitPlacement.drivenPartIds, StringComparer.OrdinalIgnoreCase)
                : null;
            var fitPreviewChildren = isAxisFitPreview ? new List<Transform>() : null;

            string[] memberIds = partGroup.partIds ?? Array.Empty<string>();
            for (int i = 0; i < memberIds.Length; i++)
            {
                string memberId = memberIds[i];
                if (string.IsNullOrWhiteSpace(memberId) || !package.TryGetPart(memberId, out PartDefinition part))
                    continue;
                if (isAxisFitPreview && !fitDrivenPartIds.Contains(memberId))
                    continue;

                PartPreviewPlacement placement = _ctx.Spawner.FindPartPlacement(memberId);
                if (placement == null)
                    continue;

                // Editor EDIT mode: sync AssetDatabase real GLB.
                // Editor PLAY mode + build: primitive placeholder (matches
                // build behavior so editor iteration catches build-only
                // bugs). PartGroup-child swap-in is a follow-up — the
                // SpawnerPartsReady flow drives material updates either way.
                GameObject childPreview = null;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    childPreview = _ctx.Spawner.TryLoadPackageAsset(part.assetRef);
#endif
                if (childPreview == null)
                {
                    childPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    childPreview.name = $"PreviewFallback_{memberId}";
                    if (previewRoot != null)
                        childPreview.transform.SetParent(previewRoot, false);
                }

                childPreview.transform.SetParent(subPreviewRoot.transform, false);

                Vector3 memberLocalPos;
                Quaternion memberLocalRot;
                Vector3 memberLocalScale;

                if (TryGetIntegratedMemberPlacement(integratedPlacement, memberId, out Vector3 integratedPos, out Quaternion integratedRot, out Vector3 integratedScale))
                {
                    memberLocalPos = InverseTransformPoint(
                        subPreviewRoot.transform.localPosition,
                        subPreviewRoot.transform.localRotation,
                        SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one),
                        integratedPos);
                    memberLocalRot = Quaternion.Inverse(subPreviewRoot.transform.localRotation) * integratedRot;
                    memberLocalScale = DivideScale(integratedScale, SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one));
                }
                else
                {
                    Vector3 memberPlayPos = new Vector3(placement.assembledPosition.x, placement.assembledPosition.y, placement.assembledPosition.z);
                    Quaternion memberPlayRot = !placement.assembledRotation.IsIdentity
                        ? new Quaternion(placement.assembledRotation.x, placement.assembledRotation.y, placement.assembledRotation.z, placement.assembledRotation.w)
                        : Quaternion.identity;
                    Vector3 memberPlayScale = SanitizeScale(new Vector3(placement.assembledScale.x, placement.assembledScale.y, placement.assembledScale.z), Vector3.one);

                    memberLocalPos = InverseTransformPoint(framePos, frameRot, frameScale, memberPlayPos);
                    memberLocalRot = Quaternion.Inverse(frameRot) * memberPlayRot;
                    memberLocalScale = DivideScale(memberPlayScale, frameScale);
                }

                if (fitPlacement?.drivenPartIds != null &&
                    Array.IndexOf(fitPlacement.drivenPartIds, memberId) >= 0)
                {
                    memberLocalPos += fitAxisLocal * (fitTravel - fitPlacement.minTravel);
                }

                childPreview.transform.SetLocalPositionAndRotation(memberLocalPos, memberLocalRot);
                childPreview.transform.localScale = memberLocalScale;
                if (fitPreviewChildren != null)
                    fitPreviewChildren.Add(childPreview.transform);

                foreach (Collider collider in childPreview.GetComponentsInChildren<Collider>(true))
                    _ctx.DestroyObject(collider);
            }

            if (isAxisFitPreview && fitPreviewChildren != null && fitPreviewChildren.Count > 0)
            {
                Vector3 anchorLocal = Vector3.zero;
                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    anchorLocal += fitPreviewChildren[i].localPosition;
                anchorLocal /= fitPreviewChildren.Count;

                subPreviewRoot.transform.localPosition = TransformPoint(
                    subPreviewRoot.transform.localPosition,
                    subPreviewRoot.transform.localRotation,
                    SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one),
                    anchorLocal);

                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    fitPreviewChildren[i].localPosition -= anchorLocal;
            }

            PlacementPreviewInfo info = subPreviewRoot.AddComponent<PlacementPreviewInfo>();
            info.TargetId = targetId;
            info.PartGroupId = partGroupId;

            ApplyPreviewMaterialClickCollider(subPreviewRoot, minAxisSize: isAxisFitPreview ? 0.09f : 0.18f, paddingWorld: isAxisFitPreview ? 0.03f : 0.08f);
            MaterialHelper.ApplyPreviewMaterial(subPreviewRoot);
            _ctx.SpawnedPreviews.Add(subPreviewRoot);
            OseLog.Info($"[PartInteraction] Composite preview spawned for partGroup '{partGroupId}' at target '{targetId}'. Total previews: {_ctx.SpawnedPreviews.Count}");
        }

        // ── Static helpers ──

        private static bool TryGetIntegratedMemberPlacement(
            IntegratedPartGroupPreviewPlacement integratedPlacement,
            string partId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (integratedPlacement?.memberPlacements == null || string.IsNullOrWhiteSpace(partId))
                return false;

            for (int i = 0; i < integratedPlacement.memberPlacements.Length; i++)
            {
                IntegratedMemberPreviewPlacement member = integratedPlacement.memberPlacements[i];
                if (member == null || !string.Equals(member.partId, partId, StringComparison.OrdinalIgnoreCase))
                    continue;

                position = ToVector3(member.position);
                rotation = ToQuaternion(member.rotation);
                scale = SanitizeScale(ToVector3(member.scale), Vector3.one);
                return true;
            }

            return false;
        }

        internal static void ApplyPreviewMaterialClickCollider(GameObject preview, float minAxisSize = 0.06f, float paddingWorld = 0f)
        {
            if (preview == null)
                return;

            Renderer[] previewRenderers = MaterialHelper.GetRenderers(preview);
            BoxCollider clickCollider = preview.GetComponent<BoxCollider>();
            if (clickCollider == null)
                clickCollider = preview.AddComponent<BoxCollider>();

            clickCollider.isTrigger = true;
            if (previewRenderers.Length <= 0)
                return;

            Bounds combined = previewRenderers[0].bounds;
            for (int ri = 1; ri < previewRenderers.Length; ri++)
                combined.Encapsulate(previewRenderers[ri].bounds);

            if (paddingWorld > 0f)
                combined.Expand(paddingWorld);

            Vector3 lossyScale = preview.transform.lossyScale;
            clickCollider.center = preview.transform.InverseTransformPoint(combined.center);
            Vector3 localSize = new Vector3(
                lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            clickCollider.size = new Vector3(
                Mathf.Max(localSize.x, minAxisSize),
                Mathf.Max(localSize.y, minAxisSize),
                Mathf.Max(localSize.z, minAxisSize));
        }

        internal static Vector3 ToVector3(SceneFloat3 value) => new Vector3(value.x, value.y, value.z);

        internal static Quaternion ToQuaternion(SceneQuaternion value)
        {
            return !value.IsIdentity
                ? new Quaternion(value.x, value.y, value.z, value.w)
                : Quaternion.identity;
        }

        internal static Vector3 SanitizeScale(Vector3 value, Vector3 fallback)
        {
            return new Vector3(
                Mathf.Approximately(value.x, 0f) ? fallback.x : value.x,
                Mathf.Approximately(value.y, 0f) ? fallback.y : value.y,
                Mathf.Approximately(value.z, 0f) ? fallback.z : value.z);
        }

        internal static Vector3 DivideScale(Vector3 value, Vector3 divisor)
        {
            Vector3 safe = SanitizeScale(divisor, Vector3.one);
            return new Vector3(value.x / safe.x, value.y / safe.y, value.z / safe.z);
        }

        internal static Vector3 MultiplyScale(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        internal static Vector3 TransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 localPoint)
        {
            return origin + rotation * MultiplyScale(scale, localPoint);
        }

        internal static Vector3 InverseTransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 point)
        {
            Vector3 translated = Quaternion.Inverse(rotation) * (point - origin);
            return DivideScale(translated, scale);
        }

        internal static bool TryGetRenderableBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = MaterialHelper.GetRenderers(target);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                bounds = renderer.bounds;
                for (int j = i + 1; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        bounds.Encapsulate(renderers[j].bounds);
                }

                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(includeInactive: false);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                bounds = collider.bounds;
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    if (colliders[j] != null)
                        bounds.Encapsulate(colliders[j].bounds);
                }

                return true;
            }

            return false;
        }

        // ── Private helpers ──

        private Transform GetPreviewRoot()
        {
            var setup = _ctx.Setup;
            return setup != null ? setup.PreviewRoot : null;
        }
    }
}
