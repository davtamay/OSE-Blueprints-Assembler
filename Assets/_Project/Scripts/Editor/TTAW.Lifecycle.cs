using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime.Preview;
using OSE.UI.Root;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            Debug.Log($"[TTAW.ToolPreview] ── OnEnable (post-reload or fresh open) — _pkgId='{_pkgId ?? "<null>"}' _selectedTargetId='{_selectedTargetId ?? "<null>"}' _selectedIdx={_selectedIdx} _showToolPreview={_showToolPreview}");

            // Destroy any stale PreviewRoot objects left from a previous session that
            // survived domain reload with HideFlags.HideAndDontSave.
            // (No stale preview roots to destroy — TTAW no longer creates HideAndDontSave objects.)

            // After a domain reload the runtime ServiceRegistry is wiped, so
            // FindLivePartGO returns null until the spawner re-registers. Unity's
            // serialized Selection however still points at a previously-spawned
            // part GameObject — the result is Unity's NATIVE Move/Rotate tool gizmo
            // floating on an unregistered orphan that TTAW can't write back to.
            // Clear the stale selection so only our gizmo (drawn once the spawner
            // reports parts via OnSpawnerPartsReady) is visible.
            Selection.activeGameObject = null;

            RefreshPackageList();
            // Package restore after domain reload is handled by OnGUI (first frame)
            // where the AssetDatabase is guaranteed to be ready.
            // Only handle the fresh-open fallback (no _pkgId yet) here.
            if (_pkg == null && string.IsNullOrEmpty(_pkgId)
                && _packageIds != null && _packageIds.Length > 0
                && _pkgIdx >= 0 && _pkgIdx < _packageIds.Length)
            {
                LoadPkg(_packageIds[_pkgIdx]);
            }
            SceneView.duringSceneGui += OnSceneGUI;
            SessionDriver.EditModeStepChanged += OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RuntimeEventBus.Subscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
        }

        private void OnDisable()
        {
            Debug.Log($"[TTAW.ToolPreview] ── OnDisable (pre-reload or window close) — _pkgId='{_pkgId ?? "<null>"}' _selectedTargetId='{_selectedTargetId ?? "<null>"}' _selectedIdx={_selectedIdx} toolPreviewGO={(_toolPreviewGO != null ? "live" : "null")}");

            StopAllPreviews();
            StopParticlePreview();
            SceneView.duringSceneGui -= OnSceneGUI;
            SessionDriver.EditModeStepChanged -= OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            RuntimeEventBus.Unsubscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
            // Destroy scene objects but do NOT reset serialized state (_selectedIdx,
            // _selectedTargetId, etc.) — OnDisable runs BEFORE Unity serializes
            // [SerializeField] fields during domain reload, so resetting here
            // would erase the values we need to restore in OnEnable.
            _partPreview?.Dispose();
            _partPreview   = null;
            _partPreviewId = null;
            RemoveMeshCollidersFromLiveParts();
            ClearToolPreview();
            ClearWirePreview();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            // Destroy particle/animation previews before play mode starts — the particle GO
            // is an unsaved scene object and must not carry over into the runtime scene.
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                StopAllPreviews();
                StopParticlePreview();
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode) return;
            // Reload the package so the window reflects any runtime changes.
            if (!string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId);
        }

        /// <summary>
        /// Fired each time <see cref="PackagePartSpawner"/> finishes a spawn cycle.
        /// Re-sync live part positions and add mesh colliders so click-to-snap still works.
        /// </summary>
        private void OnSpawnerPartsReady(SpawnerPartsReady _)
        {
            Debug.Log($"[TTAW.ToolPreview] OnSpawnerPartsReady — _selectedIdx={_selectedIdx} _targets={(_targets == null ? "null" : _targets.Length.ToString())} toolPreviewGO={(_toolPreviewGO != null ? "live" : "null")}");

            // Post-reload race fix: the spawner finishes and fires this event
            // before TTAW gets its first OnGUI, so the lazy LoadPkg in
            // DrawTopContent hasn't built _targets yet. Without _targets the
            // refresh block below silently bails — and since this is the one
            // event-driven path that re-spawns the tool preview post-compile,
            // the preview stays gone until the author manually re-selects a
            // task. Restore the package inline so _targets is ready before we
            // run the sibling re-attach work below.
            //
            // Safe in this context because the spawner only publishes
            // SpawnerPartsReady AFTER MachinePackageLoader.LoadFromStreamingAssetsAsync
            // completes — AssetDatabase is guaranteed to be ready.
            if (_pkg == null && !string.IsNullOrEmpty(_pkgId))
            {
                Debug.Log($"[TTAW.ToolPreview] OnSpawnerPartsReady — loading pkg '{_pkgId}' inline to catch post-reload race");
                LoadPkg(_pkgId, restoring: true);
            }

            // Re-apply authoritative _pkg positions after the spawn cycle.
            // The spawn itself calls ApplyStepAwarePositions(_editModePackage) which may
            // override positions using stale StreamingAssets data — overwrite with _pkg.
            ResetAllGroupRootsToOriginPreservingChildren();
            ApplySpawnerStepPositions();
            SyncAllPartMeshesToActivePose();
            AddMeshCollidersToLiveParts();
            SyncAllGroupRootsToActivePose();
            // Suppress native Move-tool polling for a few frames so the position
            // corrections above settle before the change-detection loop runs.
            _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5;

            // Re-bind Unity Selection to the part TTAW currently has selected in
            // the task-sequence panel. After a domain reload Unity restores its
            // serialized Selection (which may point at a now-orphaned GO from the
            // pre-reload spawn cycle) — that's the source of the floating white
            // gizmo. Resolving _selectedPartId against the freshly-spawned GOs
            // hands the gizmo back to the right object. If nothing is selected in
            // TTAW, clear Selection so no orphan remains.
            if (!string.IsNullOrEmpty(_selectedPartId))
            {
                var liveGO = FindLivePartGO(_selectedPartId);
                if (liveGO != null)
                    Selection.activeGameObject = liveGO;
                else
                    Selection.activeGameObject = null;
            }
            else
            {
                Selection.activeGameObject = null;
            }

            // Re-spawn the tool preview for the currently-selected target.
            // Without this, after a script recompile / domain reload the
            // _toolPreviewGO (HideAndDontSave) is destroyed but nothing
            // triggers RefreshToolPreview, so the tool visual is missing
            // until the author re-selects the task. Parts just came back
            // via the spawner event — now is the right moment to bring
            // the tool back with them.
            if (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length
                && _targets[_selectedIdx].def != null)
            {
                RefreshToolPreview(ref _targets[_selectedIdx]);
            }
        }

        private void OnSessionDriverStepChanged(int sequenceIndex)
        {
            if (_suppressStepSync || _stepSequenceIdxs == null) return;
            // Find the filter index that matches this sequence index
            int newFilterIdx = -1;
            for (int i = 1; i < _stepSequenceIdxs.Length; i++)
            {
                if (_stepSequenceIdxs[i] == sequenceIndex) { newFilterIdx = i; break; }
            }
            if (newFilterIdx < 0 || newFilterIdx == _stepFilterIdx) return;
            _suppressStepSync = true;
            ApplyStepFilter(newFilterIdx);
            _suppressStepSync = false;
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        /// <summary>
        /// Returns the spawner's PreviewRoot transform, used as the coordinate space
        /// for all target positions and tool preview placement.
        /// </summary>
        private static Transform GetPreviewRoot()
        {
            return ServiceRegistry.TryGet<ISpawnerQueryService>(out var s) ? s.PreviewRoot : null;
        }

        /// <summary>
        /// Adds a MeshCollider to each face of every live spawned part so the user can
        /// click directly on a mesh surface to snap a target (click-to-snap).
        /// Colliders are tracked in <see cref="_addedMeshColliders"/> and removed by
        /// <see cref="RemoveMeshCollidersFromLiveParts"/>.
        /// </summary>
        private void AddMeshCollidersToLiveParts()
        {
            RemoveMeshCollidersFromLiveParts(); // clear stale ones first
            if (!ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner) || spawner?.SpawnedParts == null)
                return;

            foreach (var go in spawner.SpawnedParts)
            {
                if (go == null) continue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    _addedMeshColliders.Add((mf.gameObject, mc));
                }
            }
        }

        /// <summary>Removes MeshColliders that were added by <see cref="AddMeshCollidersToLiveParts"/>.</summary>
        private void RemoveMeshCollidersFromLiveParts()
        {
            foreach (var (go, col) in _addedMeshColliders)
            {
                if (go != null && col != null)
                    DestroyImmediate(col);
            }
            _addedMeshColliders.Clear();
        }

        private void Cleanup()
        {
            StopAllPreviews();
            StopParticlePreview();
            _cueFoldouts.Clear();
            _particleFoldouts.Clear();
            RemoveMeshCollidersFromLiveParts();
            _partPreview?.Dispose();
            _partPreview   = null;
            _partPreviewId = null;
            RemoveMeshCollidersFromLiveParts();
            ClearToolPreview();
            ClearWirePreview();
            _targets = null;
            _selectedIdx = -1;
            _multiSelected.Clear();
            _parts = null;
            _selectedPartIdx = -1;
            _groups = null;
            _selectedGroupIdx = -1;
            _multiSelectedParts.Clear();
            _multiSelectedTaskSeqIdxs.Clear();
            // Destroy the subassembly root GO so parts unparent back to PreviewRoot.
            DestroyAllSubassemblyRoots();
            // Invalidate the task-sequence cache so stale order entries from
            // in-memory mutations (e.g. drag-drop adds) don't survive a revert.
            _taskSeqReorderList          = null;
            _taskSeqReorderListForStepId = null;
            InvalidateTaskOrderCache();
            // Discard unsaved dirty tracking so stale bits don't bleed into the next package load.
            _dirtyToolIds.Clear();
            _dirtyStepIds.Clear();
            _dirtyTaskOrderStepIds.Clear();
            _dirtyPartAssetRefIds.Clear();
            _dirtySubassemblyIds.Clear();
        }
    }
}
