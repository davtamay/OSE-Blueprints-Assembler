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

        // Fresh-load grace flag. Set true at the end of LoadPkg; the first
        // SpawnerPartsReady that fires after a load runs the clean-state
        // assertion a second time (covers writes from ApplySpawnerStepPositions
        // / SyncAllPartMeshesToActivePose). Cleared after that one tick so
        // subsequent spawn cycles (which legitimately follow author edits) do
        // not swallow real dirty bits.
        private bool _expectCleanAfterFirstSpawn;

        // Deadline for the post-load OnGUI clean-state sweep. Set to
        // (timeSinceStartup + LoadCleanWatchSeconds) at the end of LoadPkg;
        // every editor tick before the deadline runs AssertCleanAfterLoad.
        // The window covers first-paint EndChangeCheck misfires that no other
        // hook is positioned to catch. Short on purpose: only the very first
        // paints after a load can be phantom-dirty (the author hasn't had time
        // to interact yet), so a 0.75s sweep is plenty without risking
        // swallowing genuine edits.
        private double _loadCleanWatchUntil;
        private const double LoadCleanWatchSeconds = 0.75;

        /// <summary>
        /// Single chokepoint for marking a partGroup dirty. Kept as a method
        /// (rather than direct <c>_dirtyPartGroupIds.Add</c>) so future
        /// diagnostics or invariant checks have one place to hook. The
        /// per-paint corruption bug found via stack-trace logging on
        /// 2026-05-05 was driving every group dirty 60×/sec — keeping the
        /// wrapper means any regression of that pattern can be re-instrumented
        /// in seconds.
        /// </summary>
        private bool MarkPartGroupDirty(string id) => _dirtyPartGroupIds.Add(id);

        /// <summary>
        /// Returns true iff <paramref name="stepId"/> has unsaved changes in
        /// either the step body (<see cref="_dirtyStepIds"/>) or its task
        /// sequence (<see cref="_dirtyTaskOrderStepIds"/>). Single source of
        /// truth so the navigator dot, the toolbar pill, and the right-click
        /// "Revert this step" gate all agree.
        /// </summary>
        internal bool IsStepDirty(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return false;
            return _dirtyStepIds.Contains(stepId)
                || _dirtyTaskOrderStepIds.Contains(stepId);
        }

        /// <summary>
        /// Captures the current <c>JsonUtility.ToJson</c> form of every step
        /// into <see cref="_stepDiskSnapshots"/>. Called at the end of
        /// <c>LoadPkg</c> and after every successful <c>WriteJson</c> — those
        /// are the two moments where in-memory state is known to match disk.
        /// "Revert this step" deserializes from this snapshot instead of
        /// re-parsing the assembly file, which keeps the operation cheap and
        /// avoids re-reading split-layout files for one row revert.
        /// </summary>
        private void SnapshotAllStepsForRevert()
        {
            _stepDiskSnapshots.Clear();
            if (_pkg?.steps == null) return;
            foreach (var s in _pkg.steps)
            {
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                _stepDiskSnapshots[s.id] = JsonUtility.ToJson(s);
            }
        }

        /// <summary>
        /// Discards in-memory edits to a single step, restoring its body to
        /// the snapshot taken at LoadPkg / last WriteJson. Pose authoring
        /// (stepPose entries on parts) and target placement edits live on
        /// their own per-row isDirty flags and are not reverted here — those
        /// have their own row-level affordances. Confirmation dialog gates
        /// the destructive action; cancelling is a no-op.
        /// </summary>
        private void RevertStepOnly(string stepId)
        {
            if (string.IsNullOrEmpty(stepId) || _pkg?.steps == null) return;
            if (!IsStepDirty(stepId))
            {
                ShowNotification(new GUIContent($"'{stepId}' has no unsaved changes"));
                return;
            }
            if (!_stepDiskSnapshots.TryGetValue(stepId, out var json) || string.IsNullOrEmpty(json))
            {
                OseLog.Warn($"[TTAW.RevertStepOnly] No snapshot for '{stepId}' — was the step added since the last save? Use 'Revert all changes' instead.");
                EditorUtility.DisplayDialog(
                    "Cannot revert this step",
                    $"No saved snapshot exists for '{stepId}' — it was likely added since the last save.\n\nUse File → Revert all changes to discard the new step entirely.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Revert step?",
                    $"Discard unsaved changes on step '{stepId}' and restore its body to the last saved state?\n\nNote: pose and target placement edits on this step are tracked separately and will not be reverted here.",
                    "Revert step",
                    "Cancel"))
                return;

            int idx = -1;
            for (int i = 0; i < _pkg.steps.Length; i++)
                if (_pkg.steps[i] != null && string.Equals(_pkg.steps[i].id, stepId, StringComparison.Ordinal))
                { idx = i; break; }
            if (idx < 0) return;

            try
            {
                var fresh = JsonUtility.FromJson<StepDefinition>(json);
                if (fresh == null) throw new Exception("FromJson returned null");
                _pkg.steps[idx] = fresh;
            }
            catch (Exception e)
            {
                OseLog.Error($"[TTAW.RevertStepOnly] Failed to deserialize snapshot for '{stepId}': {e.Message}");
                return;
            }

            _dirtyStepIds.Remove(stepId);
            _dirtyTaskOrderStepIds.Remove(stepId);
            InvalidateTaskOrderCache();
            BuildStepOptions();
            BuildPartList();
            BuildTargetList();
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
            SceneView.RepaintAll();
            ShowNotification(new GUIContent($"Reverted '{stepId}'"));
        }

        /// <summary>
        /// Fires when ANYTHING in the Unity Hierarchy panel changes — including
        /// our own spawn/parent/destroy operations. Gated against the
        /// load-clean watch window so spawner-driven reparenting (which moves
        /// parts under Group_* roots after LoadPkg) doesn't trip the author-
        /// rearranged-parts detector. Also gated against in-flight reparenting
        /// inside our own code via <see cref="_suppressHierarchyPoll"/>.
        /// </summary>
        private bool _suppressHierarchyPoll;

        private void OnHierarchyChanged()
        {
            if (_pkg == null) return;
            if (_suppressHierarchyPoll) return;
            // During the load-clean window the spawner is still parenting
            // parts under group roots — this isn't an author rearrangement.
            if (_loadCleanWatchUntil > 0) return;
            PollHierarchyGroupChanges();
        }

        private void TickLoadCleanWatch()
        {
            // The watch flag is now ONLY a gate for OnHierarchyChanged so
            // spawner-driven reparenting doesn't trip the author-rearranged
            // detector. The per-tick AssertCleanAfterLoad call was removed —
            // it was scrubbing legitimate author edits that landed within
            // 0.75s of a RespawnScene (every field commit triggers a respawn).
            if (_loadCleanWatchUntil > 0
                && EditorApplication.timeSinceStartup >= _loadCleanWatchUntil)
                _loadCleanWatchUntil = 0;
        }

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
            EditorApplication.update += TickLoadCleanWatch;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            RuntimeEventBus.Subscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
            // Play → TTAW step sync. The runtime publishes StepActivated when
            // the trainee navigates (toolbar, in-app input field, auto-advance
            // on completion). Mirroring it into _stepFilterIdx keeps the
            // authoring window's hierarchy/list in step with whatever the
            // runtime is showing — same UX the EditModePreviewDriver sync gives
            // in edit mode, but for Play.
            RuntimeEventBus.Subscribe<StepActivated>(OnRuntimeStepActivated);

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
            EditorApplication.update -= TickLoadCleanWatch;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            RuntimeEventBus.Unsubscribe<SpawnerPartsReady>(OnSpawnerPartsReady);
            RuntimeEventBus.Unsubscribe<StepActivated>(OnRuntimeStepActivated);
            // Destroy scene objects but do NOT reset serialized state (_selectedIdx,
            // _selectedTargetId, etc.) — OnDisable runs BEFORE Unity serializes
            // [SerializeField] fields during domain reload, so resetting here
            // would erase the values we need to restore in OnEnable.
            _partPreview?.Dispose();
            _partPreview   = null;
            _partPreviewId = null;
            _groupPreview?.Dispose();
            _groupPreview = null;
            _groupPreviewId = null;
            _groupPreviewPoseMode = -1;
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

                // Phase A pose-pill: clear cached active-task identity so the
                // ghost + gizmo stop rendering at Play start. Live mesh is not
                // touched (the ghost is purely overlay rendering).
                RestoreLiveMeshToInheritedPose();

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

            // Second half of the fresh-load invariant. ApplySpawnerStepPositions
            // / SyncAllPartMeshesToActivePose / RefreshToolPreview run above —
            // any of them stamping dirty bits would be a load-time bug. Only
            // checked on the first spawn after LoadPkg so genuine post-edit
            // spawn cycles aren't swallowed.
            if (_expectCleanAfterFirstSpawn)
            {
                _expectCleanAfterFirstSpawn = false;
                AssertCleanAfterLoad("OnSpawnerPartsReady");
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

        /// <summary>
        /// Play-mode step sync. Runtime publishes StepActivated whenever the
        /// trainee advances, navigates back, or jumps via the in-app input.
        /// We mirror it into _stepFilterIdx so the authoring panel always
        /// shows the step the trainee is on. _suppressStepSync prevents an
        /// echo loop with the TTAW→Play push in ApplyStepFilter.
        /// </summary>
        private void OnRuntimeStepActivated(StepActivated evt)
        {
            if (!Application.isPlaying) return;
            if (_suppressStepSync) return;
            if (_stepIds == null || _stepIds.Length == 0) return;
            if (string.IsNullOrEmpty(evt.StepId)) return;

            int newFilterIdx = -1;
            for (int i = 1; i < _stepIds.Length; i++)
            {
                if (string.Equals(_stepIds[i], evt.StepId, StringComparison.Ordinal))
                {
                    newFilterIdx = i;
                    break;
                }
            }
            if (newFilterIdx < 0 || newFilterIdx == _stepFilterIdx) return;

            _suppressStepSync = true;
            try { ApplyStepFilter(newFilterIdx); }
            finally { _suppressStepSync = false; }
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
            _groupPreview?.Dispose();
            _groupPreview = null;
            _groupPreviewId = null;
            _groupPreviewPoseMode = -1;
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
            _multiSelectedStepIds.Clear();
            // Destroy the partGroup root GO so parts unparent back to PreviewRoot.
            DestroyAllPartGroupRoots();
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
            _dirtyPartGroupIds.Clear();
            _dirtyPartIds.Clear();
            _dirtyHintIds.Clear();
            _dirtyPrefabInstanceIds.Clear();
            _newHintDefs.Clear();
        }

        /// <summary>
        /// Fresh-load invariant: after LoadPkg returns and after the first
        /// SpawnerPartsReady refresh, every dirty set must be empty. The author
        /// hasn't touched anything yet, so any bit in there came from a load-
        /// time mutator that forgot its <c>markDirty:false</c> contract — that
        /// would silently overwrite disk on the next save. We log the leak
        /// (set name + ids, capped) so the offending call site can be tracked
        /// down, then clear so the toolbar doesn't lie to the author.
        /// </summary>
        private void AssertCleanAfterLoad(string phase)
        {
            int leaked = _dirtyStepIds.Count + _dirtyToolIds.Count
                       + _dirtyTaskOrderStepIds.Count + _dirtyPartAssetRefIds.Count
                       + _dirtyPartGroupIds.Count + _dirtyPartIds.Count
                       + _dirtyHintIds.Count + _dirtyPrefabInstanceIds.Count;
            if (leaked == 0) return;

            string Sample(HashSet<string> s) =>
                s.Count == 0 ? "" : "[" + string.Join(",", s.Take(8)) + (s.Count > 8 ? ",…" : "") + "]";

            OseLog.Warn(
                $"[TTAW.{phase}] {leaked} dirty entries leaked during load — clearing. " +
                $"steps={_dirtyStepIds.Count}{Sample(_dirtyStepIds)} " +
                $"tools={_dirtyToolIds.Count}{Sample(_dirtyToolIds)} " +
                $"taskOrder={_dirtyTaskOrderStepIds.Count}{Sample(_dirtyTaskOrderStepIds)} " +
                $"partAssetRefs={_dirtyPartAssetRefIds.Count}{Sample(_dirtyPartAssetRefIds)} " +
                $"partGroups={_dirtyPartGroupIds.Count}{Sample(_dirtyPartGroupIds)} " +
                $"parts={_dirtyPartIds.Count}{Sample(_dirtyPartIds)} " +
                $"hints={_dirtyHintIds.Count}{Sample(_dirtyHintIds)} " +
                $"prefabInstances={_dirtyPrefabInstanceIds.Count}{Sample(_dirtyPrefabInstanceIds)}");

            _dirtyStepIds.Clear();
            _dirtyToolIds.Clear();
            _dirtyTaskOrderStepIds.Clear();
            _dirtyPartAssetRefIds.Clear();
            _dirtyPartGroupIds.Clear();
            _dirtyPartIds.Clear();
            _dirtyHintIds.Clear();
            _dirtyPrefabInstanceIds.Clear();
        }
    }
}
