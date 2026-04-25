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

        // Consolidated lifecycle contract:
        //   • OnEnable wires events and calls EnsureLoaded() — the single
        //     idempotent guard that resolves _pkg / _targets whenever they're
        //     missing. Earlier versions of this file deferred loading to the
        //     first OnGUI out of an AssetDatabase-readiness concern, but
        //     PackageJsonUtils.LoadPackage uses File.IO and Normalize is pure
        //     data — neither requires AssetDatabase to be ready. The deferral
        //     left a window where SpawnerPartsReady fired before _targets was
        //     built, dropping the tool-preview refresh that the event was
        //     supposed to trigger. EnsureLoaded() closes that window.
        //   • OnGUI and OnSpawnerPartsReady ALSO call EnsureLoaded() as a
        //     safety net. It returns immediately when already loaded, so the
        //     redundant calls are cheap and defensive against any path that
        //     could lose state (e.g. Revert All Changes).
        //   • _pendingLoadRetry flags a transient failure (should not happen
        //     in normal operation) so the next OnGUI reattempts rather than
        //     leaving the window in a silently-broken state.

        private bool _pendingLoadRetry;

        private void OnEnable()
        {
            OseLog.VerboseInfo($"[TTAW.Lifecycle] OnEnable — _pkgId='{_pkgId ?? "<null>"}' _selectedIdx={_selectedIdx} _showToolPreview={_showToolPreview}");

            // After a domain reload the runtime ServiceRegistry is wiped, so
            // FindLivePartGO returns null until the spawner re-registers. Unity's
            // serialized Selection however still points at a previously-spawned
            // part GameObject — the result is Unity's NATIVE Move/Rotate tool gizmo
            // floating on an unregistered orphan that TTAW can't write back to.
            // Clear the stale selection so only our gizmo (drawn once the spawner
            // reports parts via OnSpawnerPartsReady) is visible.
            Selection.activeGameObject = null;

            RefreshPackageList();

            // Fresh-open fallback: no saved _pkgId → pick the first available
            // package so the window isn't empty on first launch.
            if (string.IsNullOrEmpty(_pkgId)
                && _packageIds != null && _packageIds.Length > 0
                && _pkgIdx >= 0 && _pkgIdx < _packageIds.Length)
            {
                _pkgId = _packageIds[_pkgIdx];
            }

            SceneView.duringSceneGui += OnSceneGUI;
            SessionDriver.EditModeStepChanged += OnSessionDriverStepChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RuntimeEventBus.Subscribe<SpawnerPartsReady>(OnSpawnerPartsReady);

            // Resolve _pkg / _targets here rather than deferring to the first
            // OnGUI. Any exception (e.g. corrupt JSON) sets _pendingLoadRetry
            // and OnGUI / OnSpawnerPartsReady will retry.
            EnsureLoaded();
        }

        /// <summary>
        /// Single idempotent "ensure the package is loaded" guard. All three
        /// former defensive blocks (OnEnable fresh-open fallback, OnGUI lazy
        /// retry, OnSpawnerPartsReady inline LoadPkg) now funnel through here.
        /// Returns immediately when <c>_pkg</c> and <c>_targets</c> are already
        /// populated. On exception flags <see cref="_pendingLoadRetry"/> so the
        /// next lifecycle tick retries rather than silently leaving the window
        /// broken.
        /// </summary>
        private void EnsureLoaded()
        {
            if (_pkg != null && _targets != null)
            {
                _pendingLoadRetry = false;
                return;
            }
            if (string.IsNullOrEmpty(_pkgId))
            {
                _pendingLoadRetry = false;
                return;
            }
            try
            {
                LoadPkg(_pkgId, restoring: true);
                _pendingLoadRetry = _pkg == null || _targets == null;
                if (_pendingLoadRetry)
                    OseLog.Warn($"[TTAW.Lifecycle] EnsureLoaded: LoadPkg('{_pkgId}') completed but state still incomplete (pkg={(_pkg == null ? "null" : "ok")}, targets={(_targets == null ? "null" : _targets.Length.ToString())}). Will retry.");
            }
            catch (Exception e)
            {
                OseLog.Warn($"[TTAW.Lifecycle] EnsureLoaded: LoadPkg('{_pkgId}') threw '{e.Message}'. Will retry on next OnGUI.");
                _pendingLoadRetry = true;
            }
        }

        private void OnDisable()
        {
            OseLog.VerboseInfo($"[TTAW.Lifecycle] OnDisable — _pkgId='{_pkgId ?? "<null>"}' _selectedIdx={_selectedIdx} toolPreviewGO={(_toolPreviewGO != null ? "live" : "null")}");

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
                // Force-save any dirty TTAW edits before Play loads
                // machine.json from disk. Without this, in-memory
                // authoring state (freshly-toggled awaitCues clocks,
                // new step poses, unsaved field edits) is silently
                // discarded at Play — the runtime reads the old on-disk
                // JSON and behaves as if the edit never happened. The
                // "ambiguous green checkbox" bug. Flushing here makes
                // Play-button behaviour deterministic: what you see in
                // TTAW is always what Play runs.
                FlushDirtyEditsBeforePlay();

                StopAllPreviews();
                StopParticlePreview();
                // Destroy the tool/wire preview meshes too — without this
                // the editor-spawned tool GO (HideAndDontSave) carries
                // into the Play scene and the user sees the authored
                // tool floating in the workspace alongside the runtime
                // cursor tool. OnDisable also clears these but isn't
                // guaranteed to fire before Play starts (it fires when
                // the window closes / domain reloads, not on Play entry).
                ClearToolPreview();
                ClearWirePreview();
                return;
            }

            if (state != PlayModeStateChange.EnteredEditMode) return;
            // Reload the package so the window reflects any runtime changes.
            if (!string.IsNullOrEmpty(_pkgId))
                LoadPkg(_pkgId);
        }

        /// <summary>
        /// Invoked on <see cref="PlayModeStateChange.ExitingEditMode"/>.
        /// Saves any pending TTAW edits so the Play session reads
        /// current authoring state from disk rather than stale JSON.
        /// Safe to call when nothing is dirty — <see cref="AnyDirty"/>
        /// short-circuits. Catches and logs so a single broken package
        /// can't block Play from starting.
        /// </summary>
        private void FlushDirtyEditsBeforePlay()
        {
            try
            {
                if (_pkg == null) return;
                if (!AnyDirty()) return;

                OseLog.Info("[TTAW.Lifecycle] Flushing dirty edits to JSON before entering Play.");
                // reloadAfter:false — we're transitioning out of edit
                // mode; Play's own MachinePackageLoader will read fresh
                // JSON. Full reload-and-respawn during ExitingEditMode
                // collides with Unity's own scene teardown.
                WriteJson(reloadAfter: false);
            }
            catch (Exception e)
            {
                OseLog.Warn($"[TTAW.Lifecycle] FlushDirtyEditsBeforePlay threw '{e.Message}'. Play will start with stale JSON for this window.");
            }
        }

        /// <summary>
        /// Fired each time <see cref="PackagePartSpawner"/> finishes a spawn cycle.
        /// Re-sync live part positions and add mesh colliders so click-to-snap still works.
        /// </summary>
        private void OnSpawnerPartsReady(SpawnerPartsReady _)
        {
            OseLog.VerboseInfo($"[TTAW.Lifecycle] OnSpawnerPartsReady — _selectedIdx={_selectedIdx} _targets={(_targets == null ? "null" : _targets.Length.ToString())} toolPreviewGO={(_toolPreviewGO != null ? "live" : "null")}");

            // The spawner may fire this event before TTAW's first OnGUI (post
            // domain-reload race). EnsureLoaded is idempotent and closes the
            // window where _targets was null when the refresh block below
            // needed it — that race was the root cause of "tool preview
            // disappears after compile."
            EnsureLoaded();

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
