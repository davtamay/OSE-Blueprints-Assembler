using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.PartBrowser.cs  —  Unified part-picker browser.
//
// Single source of truth for "the user is picking a part for X." Replaces the
// flat EditorGUILayout.Popup / PartPickerDropdown affordances scattered across
// TTAW with a richer panel that keeps three input surfaces in sync:
//
//   • Browser list (search + view modes) — keyboard / type-ahead path
//   • SceneView click                    — spatial / "I see it" path
//   • Unity Hierarchy selection           — when the author already has the GO
//
// All three funnel through SelectPartFromBrowser(id, source) which broadcasts
// out to the others, with a SelectionSource gate to prevent feedback loops.
//
// Slice 1 only wires the Add Part to Step site (TTAW.UnifiedList). Slice 2
// will roll the same BeginPick API to InteractionPanel / Visibility /
// PartGroup, add the ghost-render escape hatch, and delete the legacy
// PartPickerDropdown.
//
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Types ─────────────────────────────────────────────────────────────

        internal enum PartBrowseMode
        {
            Alphabetical = 0,
            ByCategory   = 1,
            UsedInThisStep = 2,
            NotYetPlaced = 3,
        }

        internal enum PartSelectionSource
        {
            Scene = 0,
            List = 1,
            Hierarchy = 2,
            Programmatic = 3,
        }

        /// <summary>
        /// How the Browser commits the selected partIds back to the step.
        /// NoTask = visualPartIds (visible-only). Task = requiredPartIds
        /// + per-part taskOrder entries. Group = a single new partGroup +
        /// one Group taskOrder entry (only available when AllowGroup is set
        /// AND 2+ parts are selected).
        /// </summary>
        public enum PartCommitMode
        {
            NoTask = 0,
            Task   = 1,
            Group  = 2,
        }

        /// <summary>
        /// One open invocation of the browser. Stays alive between
        /// BeginPick and either Confirm or Cancel.
        ///
        /// Multi-select API: callers that opt into <c>AllowMulti</c> receive
        /// the full ordered list of picked partIds via <c>OnPickedMany</c>;
        /// when <c>AllowGroup</c> is also true and the author flipped the
        /// "Add as group" toggle, <c>asGroup</c> is true and <c>groupId</c>
        /// carries the author-supplied (or auto-generated) group id.
        /// Single-select callers use the simpler <c>OnPicked</c> path.
        /// </summary>
        internal sealed class PartPickRequest
        {
            public string                                                       Label;
            public HashSet<string>                                              ExcludeIds;
            public Action<string>                                               OnPicked;     // single-pick legacy
            public Action<IReadOnlyList<string>, PartCommitMode, string /*groupId*/> OnPickedMany; // multi-pick
            public bool                                                          AllowMulti;
            public bool                                                          AllowGroup;
        }

        // ── State (NonSerialized — pick mode is always transient) ─────────────

        [NonSerialized] private PartPickRequest _pickRequest;
        [NonSerialized] private string          _browserHoverPartId;
        // Primary selection — drives the 3D preview, scene outline, scroll-to.
        // For multi-pick, this is the most-recently-clicked id (the anchor for
        // shift-range extension). Always a member of _browserSelectedPartIds
        // when set.
        [NonSerialized] private string          _browserSelectedPartId;
        // Full multi-select set. For single-pick callers this only ever
        // contains 0 or 1 entries.
        [NonSerialized] private HashSet<string> _browserSelectedPartIds = new HashSet<string>(StringComparer.Ordinal);
        // Commit mode for the multi-select footer (only meaningful when
        // _pickRequest.AllowMulti / AllowGroup are set). NoTask = the part(s)
        // land in step.visualPartIds — visible at this step but no task.
        // Task = each part becomes its own taskOrder entry. Group = a new
        // partGroup is created and added as a single Group task. Default to
        // NoTask: most authors stage visuals first and promote later, and
        // staying off the task path means we don't trip the auto-bake until
        // we explicitly opt in.
        [NonSerialized] private PartCommitMode  _browserCommitMode = PartCommitMode.NoTask;
        [NonSerialized] private string          _browserGroupId    = "";
        [NonSerialized] private string          _browserSearch = "";
        [NonSerialized] private PartBrowseMode  _browserViewMode = PartBrowseMode.NotYetPlaced;
        [NonSerialized] private Vector2         _browserScroll;
        [NonSerialized] private bool            _browserPanelFocusSearch;
        // Set when a non-List selection source (Scene click, Hierarchy ping)
        // updates the active partId. DrawBrowserList watches this and scrolls
        // the list to the matching row on the next Repaint pass.
        [NonSerialized] private bool            _browserScrollToSelected;
        [NonSerialized] private PartModelPreviewRenderer _browserPreview;
        [NonSerialized] private string          _browserPreviewId;
        // Group preview — separate renderer instance because PartGroup has
        // a multi-mesh layout (one MemberPose per partIds[]) that the
        // single-part renderer can't represent. Both renderers share the
        // same Draw/Orbit/Zoom API; the active branch in DrawBrowserPreviewPane
        // picks whichever matches the selected row's kind.
        [NonSerialized] private PartGroupModelPreviewRenderer _browserGroupPreview;
        [NonSerialized] private string          _browserGroupPreviewId;

        // Temporary live-GO activation tracking while parts are selected in
        // the Browser. Every member of _browserSelectedPartIds whose live GO
        // is currently inactive gets activated so the author can see the real
        // mesh — and we hide the matching ghost so the assembled-pose
        // duplicate doesn't double-render. Both maps remember the prior state
        // per-GO so we can restore granularly when the selection set changes
        // or when the Browser closes. We do NOT touch GOs that were already
        // active before the Browser opened (we don't want to flip them off).
        [NonSerialized] private Dictionary<GameObject, bool> _browserActivatedLiveGOs = new Dictionary<GameObject, bool>();
        [NonSerialized] private Dictionary<GameObject, bool> _browserHiddenGhostGOs   = new Dictionary<GameObject, bool>();

        // ── Public-ish API (called from picker call sites) ────────────────────

        public bool IsPartBrowserOpen => _pickRequest != null;

        public void BeginPartPick(string label, IReadOnlyCollection<string> excludeIds, Action<string> onPicked)
        {
            if (onPicked == null) return;
            _pickRequest = new PartPickRequest
            {
                Label       = string.IsNullOrEmpty(label) ? "Pick a part" : label,
                ExcludeIds  = excludeIds != null
                                  ? new HashSet<string>(excludeIds, StringComparer.Ordinal)
                                  : new HashSet<string>(StringComparer.Ordinal),
                OnPicked    = onPicked,
                AllowMulti  = false,
                AllowGroup  = false,
            };
            ResetBrowserPickState();
            CancelCreatePart();              // any leftover create-form state from a previous open

            // Defer ghost spawn + window Show out of the current IMGUI pass.
            // The trigger for BeginPartPick is a button click inside TTAW's
            // own OnGUI, so opening another EditorWindow + instantiating
            // dozens of prefabs synchronously fights Unity's renderer and
            // produces "GUI Window tried to begin rendering while something
            // else had not finished" assertions. delayCall hops to the next
            // editor tick, after the click handler has returned.
            EditorApplication.delayCall += () =>
            {
                if (_pickRequest == null) return;   // user already cancelled
                EnsureBrowserGhosts();
                PartBrowserWindow.Show(this);
                SceneView.RepaintAll();
                Repaint();
            };
        }

        /// <summary>
        /// Multi-select variant of <see cref="BeginPartPick"/>. Caller receives
        /// the full ordered list of picked ids plus an optional "as group"
        /// flag and an author-supplied group id.
        /// </summary>
        public void BeginPartPickMulti(
            string label,
            IReadOnlyCollection<string> excludeIds,
            bool allowGroup,
            Action<IReadOnlyList<string>, PartCommitMode, string> onPicked)
        {
            if (onPicked == null) return;
            _pickRequest = new PartPickRequest
            {
                Label        = string.IsNullOrEmpty(label) ? "Pick parts" : label,
                ExcludeIds   = excludeIds != null
                                   ? new HashSet<string>(excludeIds, StringComparer.Ordinal)
                                   : new HashSet<string>(StringComparer.Ordinal),
                OnPickedMany = onPicked,
                AllowMulti   = true,
                AllowGroup   = allowGroup,
            };
            ResetBrowserPickState();
            CancelCreatePart();

            // Defer ghost spawn + window Show out of the current IMGUI pass.
            // The trigger for BeginPartPick is a button click inside TTAW's
            // own OnGUI, so opening another EditorWindow + instantiating
            // dozens of prefabs synchronously fights Unity's renderer and
            // produces "GUI Window tried to begin rendering while something
            // else had not finished" assertions. delayCall hops to the next
            // editor tick, after the click handler has returned.
            EditorApplication.delayCall += () =>
            {
                if (_pickRequest == null) return;   // user already cancelled
                EnsureBrowserGhosts();
                PartBrowserWindow.Show(this);
                SceneView.RepaintAll();
                Repaint();
            };
        }

        public void CancelPartPick()
        {
            if (_pickRequest == null) return;
            _pickRequest = null;
            ResetBrowserPickState();
            CancelCreatePart();
            RestoreBrowserPreviewActivation();
            DisposeBrowserPreview();
            TearDownBrowserGhosts();
            PartBrowserWindow.CloseIfOpen();
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Clears all per-pick selection state. Called from BeginPick paths to
        /// start fresh and from CancelPartPick to drop everything before the
        /// window closes. Does not touch the pick request itself.
        /// </summary>
        private void ResetBrowserPickState()
        {
            _browserSelectedPartId   = null;
            _browserSelectedPartIds.Clear();
            _browserHoverPartId      = null;
            _browserSearch           = "";
            _browserPanelFocusSearch = true;
            _browserCommitMode       = PartCommitMode.NoTask;
            _browserGroupId          = "";
        }

        private void DisposeBrowserPreview()
        {
            _browserPreview?.Dispose();
            _browserPreview   = null;
            _browserPreviewId = null;
            _browserGroupPreview?.Dispose();
            _browserGroupPreview   = null;
            _browserGroupPreviewId = null;
        }

        /// <summary>
        /// Reconciles temporary preview activation against the current
        /// <see cref="_browserSelectedPartIds"/> set. For each selected
        /// partId: activate the live GO if it's inactive, and hide the
        /// matching ghost so we don't double-render. For each previously-
        /// activated GO that's no longer in the set: restore to its prior
        /// active state. Idempotent — call after any selection mutation.
        /// </summary>
        private void ApplyBrowserPreviewActivation()
        {
            // Build the set of GOs that SHOULD be activated / ghosts hidden
            // based on current selection. Group ids expand to their member
            // partIds — selecting a group row activates every member's live
            // GO + hides every member's ghost. Mirrors how the inspector
            // already treats groups (one logical handle, many GOs).
            var desiredLive  = new HashSet<GameObject>();
            var desiredGhost = new HashSet<GameObject>();
            foreach (var sid in _browserSelectedPartIds)
            {
                if (string.IsNullOrEmpty(sid)) continue;
                AddDesiredFor(sid, desiredLive, desiredGhost);
                if (_pkg != null && _pkg.TryGetPartGroup(sid, out var groupDef) && groupDef?.partIds != null)
                {
                    foreach (var mid in groupDef.partIds)
                        if (!string.IsNullOrEmpty(mid))
                            AddDesiredFor(mid, desiredLive, desiredGhost);
                }
            }

            void AddDesiredFor(string pid, HashSet<GameObject> live, HashSet<GameObject> ghost)
            {
                var liveGO = FindLivePartGO(pid);
                if (liveGO != null) live.Add(liveGO);
                if (_browserGhostRoot != null)
                {
                    var ghostT = _browserGhostRoot.transform.Find(pid);
                    if (ghostT != null) ghost.Add(ghostT.gameObject);
                }
            }

            // Restore live GOs that fell out of the desired set.
            var staleLive = new List<GameObject>();
            foreach (var kv in _browserActivatedLiveGOs)
                if (!desiredLive.Contains(kv.Key)) staleLive.Add(kv.Key);
            foreach (var go in staleLive)
            {
                if (go != null) go.SetActive(_browserActivatedLiveGOs[go]);
                _browserActivatedLiveGOs.Remove(go);
            }

            // Activate desired GOs that we haven't already activated.
            // Skip GOs that were already active when we encountered them —
            // we never want to flip an already-visible part to inactive on
            // restore.
            foreach (var go in desiredLive)
            {
                if (_browserActivatedLiveGOs.ContainsKey(go)) continue;
                if (!go.activeSelf)
                {
                    _browserActivatedLiveGOs[go] = false;
                    go.SetActive(true);
                }
            }

            // Symmetric ghost hiding.
            var staleGhost = new List<GameObject>();
            foreach (var kv in _browserHiddenGhostGOs)
                if (!desiredGhost.Contains(kv.Key)) staleGhost.Add(kv.Key);
            foreach (var go in staleGhost)
            {
                if (go != null) go.SetActive(_browserHiddenGhostGOs[go]);
                _browserHiddenGhostGOs.Remove(go);
            }
            foreach (var go in desiredGhost)
            {
                if (_browserHiddenGhostGOs.ContainsKey(go)) continue;
                if (go.activeSelf)
                {
                    _browserHiddenGhostGOs[go] = true;
                    go.SetActive(false);
                }
            }

            SceneView.RepaintAll();
        }

        private void RestoreBrowserPreviewActivation()
        {
            // Restore every tracked live GO to its prior state. Unity's null
            // override catches destroyed GameObjects (e.g. ghost root torn
            // down before this call). Used on Cancel — the user discarded,
            // so we revert. On Confirm we use ClearBrowserPreviewActivationTracking
            // instead, since the activated GOs are now legitimate scene
            // members via the new step.visualPartIds / requiredPartIds.
            foreach (var kv in _browserActivatedLiveGOs)
                if (kv.Key != null) kv.Key.SetActive(kv.Value);
            _browserActivatedLiveGOs.Clear();

            foreach (var kv in _browserHiddenGhostGOs)
                if (kv.Key != null) kv.Key.SetActive(kv.Value);
            _browserHiddenGhostGOs.Clear();
        }

        /// <summary>
        /// Confirm-path counterpart of <see cref="RestoreBrowserPreviewActivation"/>.
        /// Drops the tracking dictionaries WITHOUT toggling any GameObjects.
        /// On Confirm the activated parts are about to be committed to
        /// step.visualPartIds / requiredPartIds, so their currently-active
        /// state is the desired final state — flipping them back to inactive
        /// would leave the user with empty rows (the bug this fixes). The
        /// ghost root gets destroyed by TearDownBrowserGhosts; we don't need
        /// to individually restore ghost children.
        /// </summary>
        private void ClearBrowserPreviewActivationTracking()
        {
            _browserActivatedLiveGOs.Clear();
            _browserHiddenGhostGOs.Clear();
        }

        /// <summary>
        /// Ctrl-click toggle: adds <paramref name="partId"/> to the multi-set
        /// if absent, removes it if present. Updates the primary selection
        /// (which drives preview / scene outline) to the toggled id when
        /// adding; falls back to any remaining set member when removing.
        /// </summary>
        private void ToggleBrowserSelection(string partId, PartSelectionSource source)
        {
            if (string.IsNullOrEmpty(partId)) return;
            if (_pickRequest != null && !_pickRequest.AllowMulti)
            {
                // Caller didn't opt into multi — fall back to single-select.
                SelectPartFromBrowser(partId, source);
                return;
            }

            if (_browserSelectedPartIds.Contains(partId))
            {
                _browserSelectedPartIds.Remove(partId);
                if (string.Equals(_browserSelectedPartId, partId, StringComparison.Ordinal))
                {
                    // Primary just got removed — pick any remaining member as
                    // the new primary, or clear if none.
                    string newPrimary = null;
                    foreach (var id in _browserSelectedPartIds) { newPrimary = id; break; }
                    _browserSelectedPartId = newPrimary;
                    ApplyBrowserPreviewActivation();
                }
            }
            else
            {
                _browserSelectedPartIds.Add(partId);
                _browserSelectedPartId = partId;
                if (source != PartSelectionSource.List) _browserScrollToSelected = true;
                ApplyBrowserPreviewActivation();
            }

            SceneView.RepaintAll();
            Repaint();
            PartBrowserWindow.RepaintIfOpen();
        }

        /// <summary>
        /// Shift-click range select within the currently-rendered filtered
        /// list. Anchors at the current primary; selects every row from the
        /// anchor's index to <paramref name="targetId"/>'s index inclusive,
        /// replacing the prior multi-set.
        /// </summary>
        private void RangeSelectInList(string targetId, IReadOnlyList<BrowserRow> visibleRows)
        {
            if (string.IsNullOrEmpty(targetId) || visibleRows == null || visibleRows.Count == 0) return;
            if (_pickRequest != null && !_pickRequest.AllowMulti)
            {
                SelectPartFromBrowser(targetId, PartSelectionSource.List);
                return;
            }

            int anchorIdx = -1;
            int targetIdx = -1;
            for (int i = 0; i < visibleRows.Count; i++)
            {
                if (anchorIdx < 0 && string.Equals(visibleRows[i].id, _browserSelectedPartId, StringComparison.Ordinal)) anchorIdx = i;
                if (targetIdx < 0 && string.Equals(visibleRows[i].id, targetId,                StringComparison.Ordinal)) targetIdx = i;
                if (anchorIdx >= 0 && targetIdx >= 0) break;
            }
            if (targetIdx < 0) return;
            if (anchorIdx < 0) anchorIdx = targetIdx;   // no anchor → just select target

            int lo = Mathf.Min(anchorIdx, targetIdx);
            int hi = Mathf.Max(anchorIdx, targetIdx);

            _browserSelectedPartIds.Clear();
            for (int i = lo; i <= hi; i++)
            {
                var r = visibleRows[i];
                if (r.excluded) continue;
                _browserSelectedPartIds.Add(r.id);
            }
            _browserSelectedPartId = targetId;
            ApplyBrowserPreviewActivation();
            SceneView.RepaintAll();
            PartBrowserWindow.RepaintIfOpen();
        }

        private void ConfirmPartPick()
        {
            if (_pickRequest == null) return;

            // Filter selection through ExcludeIds (defensive — UI already
            // greys them out and rejects clicks).
            var picked = new List<string>(_browserSelectedPartIds.Count);
            foreach (var id in _browserSelectedPartIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (_pickRequest.ExcludeIds != null && _pickRequest.ExcludeIds.Contains(id)) continue;
                picked.Add(id);
            }
            if (picked.Count == 0) return;

            // Capture callbacks + state before tearing down so the user-
            // supplied callback runs against a clean Browser (e.g. they may
            // immediately re-open it, or call BuildPartList).
            var cbSingle = _pickRequest.OnPicked;
            var cbMany   = _pickRequest.OnPickedMany;
            var mode     = _browserCommitMode;
            // Group mode requires AllowGroup + 2+ parts. Demote to Task if
            // the count fell to 1 between toggle-and-confirm.
            if (mode == PartCommitMode.Group && (!_pickRequest.AllowGroup || picked.Count < 2))
                mode = PartCommitMode.Task;
            string groupId = mode == PartCommitMode.Group ? (_browserGroupId ?? "").Trim() : null;

            _pickRequest = null;
            ResetBrowserPickState();
            // Drop activation tracking WITHOUT restoring — the parts we
            // activated are about to be committed; flipping them back to
            // inactive would leave them invisible until the user navigates
            // away and back. The runtime spawner / visibility system takes
            // ownership from here.
            ClearBrowserPreviewActivationTracking();
            DisposeBrowserPreview();
            TearDownBrowserGhosts();
            PartBrowserWindow.CloseIfOpen();

            try
            {
                if (cbMany != null) cbMany.Invoke(picked, mode, groupId);
                else if (cbSingle != null && picked.Count > 0) cbSingle.Invoke(picked[0]);
            }
            catch (Exception e) { OseLog.Warn($"[TTAW.PartBrowser] Pick callback threw: {e}"); }

            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Single entry point for selection updates. Routes the new id to the
        /// list, scene, and Hierarchy ping while suppressing the source that
        /// originated the change so the three handlers don't loop on each other.
        /// Replaces any prior multi-selection with a single-item set rooted at
        /// <paramref name="partId"/>.
        /// </summary>
        private void SelectPartFromBrowser(string partId, PartSelectionSource source)
        {
            if (string.IsNullOrEmpty(partId)) return;
            if (string.Equals(_browserSelectedPartId, partId, StringComparison.Ordinal)
                && _browserSelectedPartIds.Count == 1)
                return;

            _browserSelectedPartId = partId;
            _browserSelectedPartIds.Clear();
            _browserSelectedPartIds.Add(partId);

            // Mark for auto-scroll if the change originated outside the list
            // itself — clicking a row already produces a visible row, but a
            // Scene click or Hierarchy ping on a part scrolled out of view
            // should snap the list back to it.
            if (source != PartSelectionSource.List) _browserScrollToSelected = true;

            // Editor-only preview: temporarily activate every member of the
            // current selection set so the author sees live geometry (not
            // just ghosts) for everything they have selected. Mirror-hide the
            // matching ghosts so we don't double-render at the same pose.
            // Restored when selection changes or Browser closes.
            ApplyBrowserPreviewActivation();

            // Hierarchy ping + scene reframe — but skip ping when the user
            // *just* clicked in Hierarchy (Selection.activeGameObject is
            // already the right GO).
            var liveGO = FindLivePartGO(partId);
            if (liveGO != null)
            {
                if (source != PartSelectionSource.Hierarchy)
                    EditorGUIUtility.PingObject(liveGO);

                if (source == PartSelectionSource.List || source == PartSelectionSource.Hierarchy)
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv != null)
                    {
                        var renderers = liveGO.GetComponentsInChildren<Renderer>();
                        if (renderers != null && renderers.Length > 0)
                        {
                            var bounds = renderers[0].bounds;
                            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                            sv.Frame(bounds, false);
                        }
                    }
                }
            }

            SceneView.RepaintAll();
            Repaint();
            PartBrowserWindow.RepaintIfOpen();
        }

        /// <summary>
        /// Hook called from <see cref="OnUnitySelectionChanged"/> when the
        /// browser is open. Mirrors the entire Unity Hierarchy selection into
        /// the Browser — single-pick callers only consume the primary
        /// (activeGameObject) match, multi-pick callers receive the full set.
        /// Walks up each selected GO's parents looking for a match in
        /// <c>_pkg.GetParts()</c> — same resolution used by every
        /// "From Selection" affordance in TTAW.
        /// </summary>
        private void NotifyBrowserOfHierarchySelection()
        {
            if (_pickRequest == null || _pkg == null) return;
            var parts = _pkg.GetParts();
            if (parts == null || parts.Length == 0) return;

            // Build a lookup of all known partIds for O(1) walk-up matching.
            var pidSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
                if (p != null && !string.IsNullOrEmpty(p.id))
                    pidSet.Add(p.id);

            // Primary first — that's the GO Unity highlights as "active".
            var activeGO = Selection.activeGameObject;
            string primaryId = activeGO != null ? ResolvePartIdFromGOWalkUp(activeGO, pidSet) : null;

            // Single-pick callers don't get the multi-set treatment — just
            // mirror the active GO and bail.
            if (!_pickRequest.AllowMulti)
            {
                if (!string.IsNullOrEmpty(primaryId))
                    SelectPartFromBrowser(primaryId, PartSelectionSource.Hierarchy);
                return;
            }

            // Multi-pick: walk every selected GO and collect partIds.
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return;

            var newSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var go in gos)
            {
                if (go == null) continue;
                string pid = ResolvePartIdFromGOWalkUp(go, pidSet);
                if (string.IsNullOrEmpty(pid)) continue;
                if (_pickRequest.ExcludeIds != null && _pickRequest.ExcludeIds.Contains(pid)) continue;
                newSet.Add(pid);
            }
            if (newSet.Count == 0) return;

            // Skip the loopback case: Hierarchy selection didn't change in
            // a way that affects us.
            if (newSet.SetEquals(_browserSelectedPartIds)
                && string.Equals(primaryId, _browserSelectedPartId, StringComparison.Ordinal))
                return;

            _browserSelectedPartIds = newSet;
            // Primary = active GO's partId if it's in the set; else first.
            if (!string.IsNullOrEmpty(primaryId) && newSet.Contains(primaryId))
                _browserSelectedPartId = primaryId;
            else
            {
                string first = null;
                foreach (var id in newSet) { first = id; break; }
                _browserSelectedPartId = first;
            }
            _browserScrollToSelected = true;
            ApplyBrowserPreviewActivation();
            SceneView.RepaintAll();
            Repaint();
            PartBrowserWindow.RepaintIfOpen();
        }

        private static string ResolvePartIdFromGOWalkUp(GameObject go, HashSet<string> pidSet)
        {
            if (go == null || pidSet == null) return null;
            var t = go.transform;
            int safety = 12;
            while (t != null && safety-- > 0)
            {
                if (pidSet.Contains(t.name)) return t.name;
                t = t.parent;
            }
            return null;
        }

        // ── Scene handler (called from TTAW.SceneView.OnSceneGUI when open) ───

        /// <summary>
        /// Scene-side counterpart of the Browser. Hovers paint a thin outline,
        /// click commits the hovered id as the new selection, Esc cancels.
        /// Selection (driven by Browser list, Scene click, or Hierarchy) is
        /// always painted as a thicker outline so list-driven picks are
        /// visible without leaving the SceneView.
        /// </summary>
        private void HandleBrowserSceneGUI(SceneView sv)
        {
            if (_pickRequest == null) return;
            var e = Event.current;

            // Canonical Unity event-type switch. Calling HandleUtility methods
            // (PickGameObject, AddDefaultControl) outside their valid event
            // type produces "GUI Window tried to begin rendering" assertions
            // and a grey SceneView. PickGameObject is only safe in Layout /
            // MouseMove / MouseDrag / MouseUp / MouseDown. AddDefaultControl
            // is only safe in Layout. Outline + HUD draws are only safe in
            // Repaint.
            switch (e.type)
            {
                case EventType.Layout:
                    // No HandleUtility.AddDefaultControl — we don't want to
                    // grab focus, we want clicks on parts to work normally.
                    break;

                case EventType.MouseMove:
                {
                    string newHover = ResolvePartIdUnderMouse(e.mousePosition);
                    if (!string.Equals(newHover, _browserHoverPartId, StringComparison.Ordinal))
                    {
                        _browserHoverPartId = newHover;
                        sv?.Repaint();
                    }
                    break;
                }

                case EventType.MouseDown:
                {
                    if (e.button != 0 || e.alt) break;
                    string clicked = ResolvePartIdUnderMouse(e.mousePosition);
                    if (string.IsNullOrEmpty(clicked)) break;
                    bool excluded = _pickRequest.ExcludeIds != null
                                    && _pickRequest.ExcludeIds.Contains(clicked);
                    if (!excluded)
                    {
                        bool ctrl = e.control || e.command;
                        if (ctrl && _pickRequest.AllowMulti)
                            ToggleBrowserSelection(clicked, PartSelectionSource.Scene);
                        else
                            SelectPartFromBrowser(clicked, PartSelectionSource.Scene);
                        e.Use();
                    }
                    break;
                }

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape)
                    {
                        CancelPartPick();
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                {
                    // Hover outline + HUD label.
                    if (!string.IsNullOrEmpty(_browserHoverPartId))
                    {
                        var hoverGO = FindLivePartGO(_browserHoverPartId);
                        if (hoverGO != null)
                        {
                            bool excluded = _pickRequest.ExcludeIds != null
                                            && _pickRequest.ExcludeIds.Contains(_browserHoverPartId);
                            DrawPartOutline(hoverGO,
                                excluded ? new Color(0.55f, 0.55f, 0.55f, 0.85f)
                                         : new Color(1f, 0.85f, 0.20f, 0.95f),
                                thickness: 2f);

                            var hStyle = new GUIStyle(EditorStyles.boldLabel)
                            {
                                normal    = { textColor = excluded ? new Color(0.85f, 0.55f, 0.55f) : Color.white },
                                fontSize  = 11,
                            };
                            string lbl = excluded
                                ? $"⏵ {_browserHoverPartId}  (already in step)"
                                : $"⏵ {_browserHoverPartId}";
                            Handles.BeginGUI();
                            var labelRect = new Rect(e.mousePosition.x + 14f, e.mousePosition.y + 14f, 380f, 18f);
                            EditorGUI.DrawRect(labelRect, new Color(0f, 0f, 0f, 0.55f));
                            GUI.Label(new Rect(labelRect.x + 4f, labelRect.y, labelRect.width - 4f, labelRect.height), lbl, hStyle);
                            Handles.EndGUI();
                        }
                    }

                    // Selection outlines (thicker, persistent). Every member
                    // of the multi-set gets a cyan outline; the primary id
                    // gets a slightly hotter cyan to stand out. Group ids
                    // expand to all their members — outline every member
                    // GO so the author sees the full extent of the group.
                    foreach (var selId in _browserSelectedPartIds)
                    {
                        if (string.IsNullOrEmpty(selId)) continue;
                        if (string.Equals(selId, _browserHoverPartId, StringComparison.Ordinal)) continue;
                        bool isPrimary = string.Equals(selId, _browserSelectedPartId, StringComparison.Ordinal);
                        var primaryColor = new Color(0.20f, 0.95f, 1f, 0.95f);
                        var memberColor  = new Color(0.30f, 0.65f, 0.95f, 0.85f);

                        if (_pkg != null && _pkg.TryGetPartGroup(selId, out var groupDef) && groupDef?.partIds != null)
                        {
                            foreach (var mid in groupDef.partIds)
                            {
                                if (string.IsNullOrEmpty(mid)) continue;
                                var memberGO = FindLivePartGO(mid);
                                if (memberGO == null) continue;
                                DrawPartOutline(memberGO,
                                    isPrimary ? primaryColor : memberColor,
                                    thickness: isPrimary ? 3f : 2f);
                            }
                            continue;
                        }

                        var selGO = FindLivePartGO(selId);
                        if (selGO == null) continue;
                        DrawPartOutline(selGO,
                            isPrimary ? primaryColor : memberColor,
                            thickness: isPrimary ? 3f : 2f);
                    }
                    break;
                }
            }
        }

        private string ResolvePartIdUnderMouse(Vector2 mousePos)
        {
            if (_pkg == null) return null;
            var picked = HandleUtility.PickGameObject(mousePos, false);
            if (picked == null) return null;

            var parts = _pkg.GetParts();
            if (parts == null || parts.Length == 0) return null;

            var pidSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
                if (p != null && !string.IsNullOrEmpty(p.id))
                    pidSet.Add(p.id);

            var t = picked.transform;
            int safety = 12;
            while (t != null && safety-- > 0)
            {
                if (pidSet.Contains(t.name)) return t.name;
                t = t.parent;
            }
            return null;
        }

        private static void DrawPartOutline(GameObject go, Color color, float thickness)
        {
            if (go == null) return;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return;

            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

            var prev = Handles.color;
            Handles.color = color;
            Handles.DrawWireCube(b.center, b.size);
            // A second slightly-larger pass approximates a thicker line.
            if (thickness > 1.5f)
            {
                Handles.DrawWireCube(b.center, b.size * 1.005f);
                Handles.DrawWireCube(b.center, b.size * 0.995f);
            }
            Handles.color = prev;
        }

        // ── Browser panel (rendered by PartBrowserWindow.OnGUI) ───────────────

        /// <summary>
        /// Renders the full Browser panel — header, search, view-mode tabs,
        /// scrollable filtered list, Confirm/Cancel buttons. Called from
        /// <see cref="PartBrowserWindow"/> so the panel can live in its own
        /// floating EditorWindow without colliding with TTAW's three-pane
        /// shell. State lives on TTAW; the window is just a presentation
        /// surface.
        /// </summary>
        internal void DrawPartBrowserPanel(EditorWindow host)
        {
            if (_pickRequest == null)
            {
                EditorGUILayout.LabelField("(no active pick request)", EditorStyles.centeredGreyMiniLabel);
                // Don't call host.Close() inside OnGUI — host's own OnGUI
                // path defers the close via delayCall when it sees the same
                // null-state on the next tick.
                return;
            }

            // ── Header ────────────────────────────────────────────────────────
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            EditorGUILayout.LabelField(_pickRequest.Label, titleStyle);
            string hint = _pickRequest.AllowMulti
                ? "Click a part in the SceneView, the Hierarchy, or this list. Ctrl-click to add to selection, Shift-click for range. Esc cancels."
                : "Click a part in the SceneView, the Hierarchy, or this list. Esc cancels.";
            EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);
            int hdrCount = _browserSelectedPartIds.Count;
            if (_pickRequest.AllowMulti && hdrCount > 0)
            {
                var countStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    normal = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                };
                EditorGUILayout.LabelField($"{hdrCount} selected", countStyle);
            }
            EditorGUILayout.EndVertical();

            // ── Mode toolbar ──────────────────────────────────────────────────
            // Browse Parts / Browse from GLB Assets — switches the entire
            // body. Browse Parts shows the existing list + preview pane.
            // Browse from GLB Assets shows the folder-driven create flow.
            // Clone-Selected was retired in Slice 1.11 — same authoring
            // outcome reachable by selecting an existing GLB and editing
            // the auto-suggested id.
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            DrawCreateModeTab(host, "📂  Browse Parts",          PartCreateMode.None);
            DrawCreateModeTab(host, "📁  Browse from GLB Assets", PartCreateMode.FromAsset);
            EditorGUILayout.EndHorizontal();

            // ── Body — branches on toolbar mode ───────────────────────────────
            // Browse Parts: existing 2-column layout (search/tabs/list +
            // preview pane). From-Asset: full-width create form (folder
            // list + preview + form fields). Mutually exclusive — pick and
            // create are different intents and don't share screen real
            // estate well.
            if (IsCreatePartActive)
            {
                EditorGUILayout.Space(2);
                DrawCreatePartForm(host);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();

                // ── Left column — search + tabs + list ───────────────────────
                EditorGUILayout.BeginVertical(GUILayout.MinWidth(280f));
                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("🔍", GUILayout.Width(18));
                GUI.SetNextControlName("ttaw-partbrowser-search");
                string newSearch = EditorGUILayout.TextField(_browserSearch ?? "");
                if (!string.Equals(newSearch, _browserSearch, StringComparison.Ordinal))
                {
                    _browserSearch = newSearch;
                    host?.Repaint();
                }
                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    _browserSearch = "";
                    host?.Repaint();
                }
                EditorGUILayout.EndHorizontal();

                if (_browserPanelFocusSearch && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.FocusTextInControl("ttaw-partbrowser-search");
                    _browserPanelFocusSearch = false;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                DrawBrowseModeTab(host, "Not Yet Placed", PartBrowseMode.NotYetPlaced);
                DrawBrowseModeTab(host, "Used In Step",   PartBrowseMode.UsedInThisStep);
                DrawBrowseModeTab(host, "By Category",    PartBrowseMode.ByCategory);
                DrawBrowseModeTab(host, "A → Z",          PartBrowseMode.Alphabetical);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
                DrawBrowserList(host);

                EditorGUILayout.EndVertical();

                // ── Right column — 3D preview ────────────────────────────────
                DrawBrowserPreviewPane(host);

                EditorGUILayout.EndHorizontal();
            }

            // ── Commit-mode selector + Footer (skipped in Create mode) ───────
            // The pick-flow UI (commit-mode radios + Confirm/Cancel) only
            // makes sense when Browse-Parts is active. In From-Asset mode
            // the create form has its own Create/Cancel buttons.
            if (!IsCreatePartActive)
            {
                int selCount = _browserSelectedPartIds.Count;
                bool multiAllowed = _pickRequest != null && _pickRequest.AllowMulti;
                bool groupAvailable = multiAllowed && _pickRequest.AllowGroup && selCount >= 2;

                if (_browserCommitMode == PartCommitMode.Group && !groupAvailable)
                    _browserCommitMode = PartCommitMode.Task;

                if (multiAllowed && selCount > 0)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    DrawCommitModeRadio(host, "No task",  PartCommitMode.NoTask,
                        "Add the selected part(s) to the step's visualPartIds — visible at this step but not part of the task sequence. Default.");
                    DrawCommitModeRadio(host, "Task",     PartCommitMode.Task,
                        "Add each selected part as its own task in the step. Writes to requiredPartIds + taskOrder.");
                    EditorGUI.BeginDisabledGroup(!groupAvailable);
                    DrawCommitModeRadio(host, "Group task", PartCommitMode.Group,
                        "Bundle all selected parts into a new partGroup and add a single Group task to the step. Requires 2+ parts.");
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();

                    if (_browserCommitMode == PartCommitMode.Group)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("group id", GUILayout.Width(60));
                        if (string.IsNullOrEmpty(_browserGroupId)) _browserGroupId = SuggestGroupId();
                        _browserGroupId = EditorGUILayout.TextField(_browserGroupId ?? "");
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                bool canConfirm = selCount > 0;
                EditorGUI.BeginDisabledGroup(!canConfirm);
                var confirmStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                };

                string confirmLabel = BuildConfirmLabel(selCount, multiAllowed);
                if (GUILayout.Button(confirmLabel, confirmStyle, GUILayout.Height(26)))
                {
                    ConfirmPartPick();
                }
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(26)))
                {
                    CancelPartPick();
                }
                EditorGUILayout.EndHorizontal();
            }

            // Enter inside the search field also confirms — but only in
            // Browse-Parts mode. In From-Asset mode the create form has its
            // own Create button so Enter shouldn't fire the pick path.
            if (!IsCreatePartActive
                && Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && !string.IsNullOrEmpty(_browserSelectedPartId))
            {
                ConfirmPartPick();
                Event.current.Use();
            }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                CancelPartPick();   // also cancels create via CancelCreatePart in CancelPartPick
                Event.current.Use();
            }
        }

        private void DrawCommitModeRadio(EditorWindow host, string label, PartCommitMode mode, string tooltip)
        {
            bool selected = _browserCommitMode == mode;
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = selected
                            ? new Color(0.20f, 0.62f, 0.95f)
                            : EditorStyles.miniButton.normal.textColor },
            };
            if (GUILayout.Toggle(selected, new GUIContent(label, tooltip), style, GUILayout.Height(20)) && !selected)
            {
                _browserCommitMode = mode;
                host?.Repaint();
            }
        }

        private string BuildConfirmLabel(int selCount, bool multiAllowed)
        {
            if (selCount == 0) return "Confirm";

            // Single-pick (or single-selected in multi mode): always shows the
            // exact partId so the author can verify what they're about to add.
            // Multi-pick respects the chosen commit mode for the verb.
            if (!multiAllowed || selCount == 1)
            {
                string verb = _browserCommitMode switch
                {
                    PartCommitMode.Task   => "Add as task",
                    PartCommitMode.Group  => "Add",
                    _                      => "Add (no task)",
                };
                return $"✓  {verb} '{_browserSelectedPartId}'";
            }

            return _browserCommitMode switch
            {
                PartCommitMode.Group => $"✓  Add as group '{_browserGroupId}' ({selCount} parts)",
                PartCommitMode.Task  => $"✓  Add {selCount} parts as tasks",
                _                     => $"✓  Add {selCount} parts (no task)",
            };
        }

        private void DrawCreateModeTab(EditorWindow host, string label, PartCreateMode mode)
        {
            bool selected = _createMode == mode;
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = selected
                            ? new Color(0.20f, 0.62f, 0.95f)
                            : EditorStyles.miniButton.normal.textColor },
            };
            if (GUILayout.Toggle(selected, label, style, GUILayout.Height(22)) && !selected)
            {
                switch (mode)
                {
                    case PartCreateMode.None:      CancelCreatePart();        break;
                    case PartCreateMode.FromAsset: BeginCreateFromAsset();    break;
                }
                host?.Repaint();
            }
        }

        private void DrawBrowseModeTab(EditorWindow host, string label, PartBrowseMode mode)
        {
            bool selected = _browserViewMode == mode;
            var style = new GUIStyle(EditorStyles.miniButton)
            {
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                normal = { textColor = selected ? new Color(0.20f, 0.62f, 0.95f) : EditorStyles.miniButton.normal.textColor },
            };
            if (GUILayout.Toggle(selected, label, style, GUILayout.Height(20)) && !selected)
            {
                _browserViewMode = mode;
                host?.Repaint();
            }
        }

        private void DrawBrowserList(EditorWindow host)
        {
            var rows = BuildBrowserRows();
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("(no parts match the current filter)", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            _browserScroll = EditorGUILayout.BeginScrollView(_browserScroll,
                GUILayout.MinHeight(240f), GUILayout.ExpandHeight(true));

            // Track the rect of the row matching _browserSelectedPartId so
            // we can scroll-into-view after the loop. Only valid during
            // Repaint — GetRect returns zeros during Layout.
            float matchedRowY = -1f;

            string lastCategory = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (_browserViewMode == PartBrowseMode.ByCategory
                    && !string.Equals(r.category, lastCategory, StringComparison.Ordinal))
                {
                    EditorGUILayout.Space(4);
                    var catStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = new Color(0.55f, 0.78f, 1f, 0.85f) },
                    };
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(r.category) ? "uncategorized" : r.category, catStyle);
                    lastCategory = r.category;
                }

                bool selected      = _browserSelectedPartIds.Contains(r.id);
                bool isPrimary     = string.Equals(_browserSelectedPartId, r.id, StringComparison.Ordinal);
                bool excluded      = r.excluded;

                var rowStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = isPrimary ? FontStyle.Bold : (selected ? FontStyle.Bold : FontStyle.Normal),
                    normal    = { textColor = excluded
                                    ? new Color(0.50f, 0.50f, 0.50f)
                                    : selected
                                        ? new Color(0.20f, 0.62f, 0.95f)
                                        : EditorStyles.label.normal.textColor },
                    padding   = new RectOffset(8, 4, 2, 2),
                };

                var rowRect = GUILayoutUtility.GetRect(new GUIContent(r.label), rowStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true));
                if (selected)
                {
                    // Primary row: full-saturation tint. Other multi-selected
                    // rows: subtle tint to disambiguate the anchor from the set.
                    EditorGUI.DrawRect(rowRect, isPrimary
                        ? new Color(0.20f, 0.62f, 0.95f, 0.18f)
                        : new Color(0.20f, 0.62f, 0.95f, 0.08f));
                    if (isPrimary && Event.current.type == EventType.Repaint) matchedRowY = rowRect.y;
                }

                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && rowRect.Contains(ev.mousePosition))
                {
                    if (!excluded)
                    {
                        bool ctrl  = ev.control || ev.command;
                        bool shift = ev.shift;
                        if (shift && _pickRequest != null && _pickRequest.AllowMulti)
                        {
                            RangeSelectInList(r.id, rows);
                        }
                        else if (ctrl && _pickRequest != null && _pickRequest.AllowMulti)
                        {
                            ToggleBrowserSelection(r.id, PartSelectionSource.List);
                        }
                        else
                        {
                            SelectPartFromBrowser(r.id, PartSelectionSource.List);
                            if (ev.clickCount >= 2) ConfirmPartPick();
                        }
                    }
                    ev.Use();
                    host?.Repaint();
                }
                GUI.Label(rowRect, r.label, rowStyle);
                if (excluded)
                {
                    var tagStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = new Color(0.85f, 0.55f, 0.55f) },
                    };
                    GUI.Label(rowRect, "already in step  ", tagStyle);
                }
            }

            EditorGUILayout.EndScrollView();

            // Auto-scroll the matched row into view. We use rowRect.y (content
            // coords) and place it ~30px below the top of the viewport so the
            // row isn't flush against the scrollview's top edge. Only fires
            // when an external selection source (Scene / Hierarchy) requested
            // the scroll, and only after we captured a valid y during Repaint.
            if (_browserScrollToSelected && matchedRowY >= 0f
                && Event.current.type == EventType.Repaint)
            {
                _browserScroll.y         = Mathf.Max(0f, matchedRowY - 30f);
                _browserScrollToSelected = false;
                host?.Repaint();
            }
        }

        // ── Row model + filters ───────────────────────────────────────────────

        internal enum BrowserRowKind
        {
            Part  = 0,
            Group = 1,
        }

        private struct BrowserRow
        {
            public string         id;
            public string         label;
            public string         category;
            public bool           excluded;
            public BrowserRowKind kind;
            public int            groupMemberCount;   // 0 for parts; populated for Group rows
        }

        private List<BrowserRow> BuildBrowserRows()
        {
            var rows = new List<BrowserRow>();
            if (_pkg == null) return rows;
            var parts = _pkg.GetParts();
            if (parts == null) return rows;

            var search = (_browserSearch ?? "").Trim();

            // Step-aware filters — populated once, consulted by both the
            // parts and groups passes below.
            HashSet<string> usedInStepParts   = null;
            HashSet<string> usedInStepGroups  = null;
            HashSet<string> notYetPlacedParts = null;
            HashSet<string> notYetPlacedGroups = null;
            int curSeq = -1;
            StepDefinition curStep = null;
            if (_browserViewMode == PartBrowseMode.UsedInThisStep
                || _browserViewMode == PartBrowseMode.NotYetPlaced)
            {
                curStep = (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                    ? FindStep(_stepIds[_stepFilterIdx])
                    : null;
                if (curStep != null) curSeq = curStep.sequenceIndex;

                if (curStep != null)
                {
                    usedInStepParts = new HashSet<string>(StringComparer.Ordinal);
                    if (curStep.requiredPartIds != null) foreach (var s in curStep.requiredPartIds) if (!string.IsNullOrEmpty(s)) usedInStepParts.Add(s);
                    if (curStep.optionalPartIds != null) foreach (var s in curStep.optionalPartIds) if (!string.IsNullOrEmpty(s)) usedInStepParts.Add(s);
                    if (curStep.visualPartIds   != null) foreach (var s in curStep.visualPartIds)   if (!string.IsNullOrEmpty(s)) usedInStepParts.Add(s);

                    usedInStepGroups = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrEmpty(curStep.requiredPartGroupId))
                        usedInStepGroups.Add(curStep.requiredPartGroupId);
                    // A group is also "used in step" when any of its members
                    // is in the step's role arrays — author commonly added
                    // members ad-hoc rather than referencing the group as a
                    // unit.
                    var allGroups = _pkg.GetPartGroups();
                    if (allGroups != null && usedInStepParts.Count > 0)
                    {
                        foreach (var g in allGroups)
                        {
                            if (g == null || string.IsNullOrEmpty(g.id) || g.partIds == null) continue;
                            foreach (var mid in g.partIds)
                                if (!string.IsNullOrEmpty(mid) && usedInStepParts.Contains(mid))
                                {
                                    usedInStepGroups.Add(g.id);
                                    break;
                                }
                        }
                    }
                }

                if (_browserViewMode == PartBrowseMode.NotYetPlaced)
                {
                    // Parts: first-appearance resolver — same source of truth
                    // as the existing single-mode rule. firstSeq < 0 means
                    // the part has never been placed.
                    notYetPlacedParts = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var p in parts)
                    {
                        if (p == null || string.IsNullOrEmpty(p.id)) continue;
                        int firstSeq = ResolvePartFirstAppearance(p.id);
                        if (firstSeq < 0 || firstSeq >= curSeq) notYetPlacedParts.Add(p.id);
                    }
                    // Groups: "not yet placed" when no required step references
                    // the group AND every member is not-yet-placed too. The
                    // member roll-up keeps groups out of the list once any
                    // member has been placed — the group is "in progress".
                    notYetPlacedGroups = new HashSet<string>(StringComparer.Ordinal);
                    var allGroups = _pkg.GetPartGroups();
                    if (allGroups != null)
                    {
                        foreach (var g in allGroups)
                        {
                            if (g == null || string.IsNullOrEmpty(g.id)) continue;
                            // Has any step's requiredPartGroupId pointed here at curSeq?
                            bool placedAsGroup = false;
                            if (_pkg.steps != null)
                                foreach (var st in _pkg.steps)
                                    if (st != null
                                        && string.Equals(st.requiredPartGroupId, g.id, StringComparison.Ordinal)
                                        && st.sequenceIndex < curSeq)
                                    { placedAsGroup = true; break; }
                            if (placedAsGroup) continue;
                            // Any member already placed?
                            bool memberPlaced = false;
                            if (g.partIds != null)
                                foreach (var mid in g.partIds)
                                {
                                    if (string.IsNullOrEmpty(mid)) continue;
                                    int firstSeq = ResolvePartFirstAppearance(mid);
                                    if (firstSeq >= 0 && firstSeq < curSeq) { memberPlaced = true; break; }
                                }
                            if (!memberPlaced) notYetPlacedGroups.Add(g.id);
                        }
                    }
                }
            }

            // Parts pass.
            foreach (var p in parts)
            {
                if (p == null || string.IsNullOrEmpty(p.id)) continue;

                if (_browserViewMode == PartBrowseMode.UsedInThisStep && (usedInStepParts == null || !usedInStepParts.Contains(p.id))) continue;
                if (_browserViewMode == PartBrowseMode.NotYetPlaced   && (notYetPlacedParts == null || !notYetPlacedParts.Contains(p.id))) continue;

                if (!string.IsNullOrEmpty(search) && !MatchesSearch(p, search)) continue;

                bool excluded = _pickRequest?.ExcludeIds != null && _pickRequest.ExcludeIds.Contains(p.id);
                string label = string.IsNullOrEmpty(p.name) || p.name == p.id
                    ? p.id
                    : $"{p.id}  —  {p.name}";

                rows.Add(new BrowserRow
                {
                    id       = p.id,
                    label    = label,
                    category = p.category ?? "",
                    excluded = excluded,
                    kind     = BrowserRowKind.Part,
                    groupMemberCount = 0,
                });
            }

            // Groups pass — partGroups appear inline as first-class rows. The
            // 📦 prefix + member count + assemblyId-as-category lets the
            // existing row renderer + sort logic treat them uniformly.
            var groups = _pkg.GetPartGroups();
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    if (g == null || string.IsNullOrEmpty(g.id)) continue;
                    if (g.isAggregate) continue;   // aggregate groups are roll-ups, not author-pickable units

                    if (_browserViewMode == PartBrowseMode.UsedInThisStep && (usedInStepGroups == null || !usedInStepGroups.Contains(g.id))) continue;
                    if (_browserViewMode == PartBrowseMode.NotYetPlaced   && (notYetPlacedGroups == null || !notYetPlacedGroups.Contains(g.id))) continue;

                    if (!string.IsNullOrEmpty(search) && !MatchesSearchGroup(g, search)) continue;

                    bool excluded = _pickRequest?.ExcludeIds != null && _pickRequest.ExcludeIds.Contains(g.id);
                    int memberCount = g.partIds?.Length ?? 0;
                    string displayName = g.GetDisplayName();
                    string label = $"📦  {(string.IsNullOrEmpty(displayName) ? g.id : displayName)}  ({memberCount} parts)";
                    string category = string.IsNullOrEmpty(g.assemblyId) ? "groups" : g.assemblyId;

                    rows.Add(new BrowserRow
                    {
                        id       = g.id,
                        label    = label,
                        category = category,
                        excluded = excluded,
                        kind     = BrowserRowKind.Group,
                        groupMemberCount = memberCount,
                    });
                }
            }

            // Sort
            if (_browserViewMode == PartBrowseMode.ByCategory)
            {
                rows.Sort((a, b) =>
                {
                    int c = string.Compare(a.category ?? "", b.category ?? "", StringComparison.Ordinal);
                    return c != 0 ? c : string.Compare(a.id, b.id, StringComparison.Ordinal);
                });
            }
            else
            {
                rows.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
            }

            return rows;
        }

        private static bool MatchesSearch(PartDefinition p, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            if (Contains(p.id, search))                 return true;
            if (Contains(p.name, search))               return true;
            if (Contains(p.category, search))           return true;
            return false;
        }

        private static bool MatchesSearchGroup(PartGroupDefinition g, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            if (Contains(g.id, search))            return true;
            if (Contains(g.name, search))          return true;
            if (Contains(g.assemblyId, search))    return true;
            return false;
        }

        private static bool Contains(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Auto-suggests a unique group id for the "Add as group" toggle.
        /// Format: <c>group_step_&lt;currentStepId&gt;</c>, with auto-increment
        /// suffix on collision. The author can edit it before confirming.
        /// </summary>
        private string SuggestGroupId()
        {
            string stepId = (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                ? _stepIds[_stepFilterIdx]
                : "new";
            string baseId = $"group_{stepId}";
            if (_pkg == null || !_pkg.TryGetPartGroup(baseId, out _)) return baseId;
            for (int n = 2; n < 100; n++)
            {
                string candidate = baseId + "_" + n;
                if (!_pkg.TryGetPartGroup(candidate, out _)) return candidate;
            }
            return baseId + "_" + System.Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        // ── Right-column 3D preview pane ──────────────────────────────────────

        private void DrawBrowserPreviewPane(EditorWindow host)
        {
            const float PaneWidth     = 240f;
            const float PreviewHeight = 200f;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(PaneWidth));

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_browserSelectedPartId))
            {
                EditorGUILayout.LabelField("(no selection)", EditorStyles.centeredGreyMiniLabel);
                GUILayout.Space(PreviewHeight - 18f);
                EditorGUILayout.EndVertical();
                return;
            }

            // Branch on selection kind: parts use the single-mesh renderer,
            // groups use the multi-mesh group renderer. Disposal of the
            // OTHER renderer happens inside the Ensure* methods so we don't
            // hold both alive simultaneously.
            bool isGroup = _pkg != null && _pkg.TryGetPartGroup(_browserSelectedPartId, out _);
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(PreviewHeight), GUILayout.Width(PaneWidth - 12f));

            if (isGroup)
            {
                EnsureBrowserGroupPreview(_browserSelectedPartId);
                if (_browserGroupPreview != null)
                {
                    var ev = Event.current;
                    if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                    { _browserGroupPreview.Orbit(ev.delta); ev.Use(); host?.Repaint(); }
                    else if (ev.type == EventType.ScrollWheel && rect.Contains(ev.mousePosition))
                    { _browserGroupPreview.Zoom(ev.delta.y); ev.Use(); host?.Repaint(); }
                    if (_browserGroupPreview.Draw(rect, new PartGroupModelPreviewRenderer.DrawOptions { useMm = true })) host?.Repaint();
                }
                else
                {
                    EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.20f));
                    GUI.Label(rect, "(group has no renderable members)", EditorStyles.centeredGreyMiniLabel);
                }
            }
            else
            {
                EnsureBrowserPreview(_browserSelectedPartId);
                if (_browserPreview != null)
                {
                    var ev = Event.current;
                    if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                    { _browserPreview.Orbit(ev.delta); ev.Use(); host?.Repaint(); }
                    else if (ev.type == EventType.ScrollWheel && rect.Contains(ev.mousePosition))
                    { _browserPreview.Zoom(ev.delta.y); ev.Use(); host?.Repaint(); }
                    if (_browserPreview.Draw(rect, useMm: true)) host?.Repaint();
                }
                else
                {
                    EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.20f));
                    GUI.Label(rect, "(asset not found)", EditorStyles.centeredGreyMiniLabel);
                }
            }

            // Selected metadata
            EditorGUILayout.Space(4);
            if (isGroup && _pkg.TryGetPartGroup(_browserSelectedPartId, out var groupDef))
            {
                var idStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    normal   = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                    wordWrap = true,
                };
                EditorGUILayout.LabelField($"📦 {groupDef.id}", idStyle);
                if (!string.IsNullOrEmpty(groupDef.name) && groupDef.name != groupDef.id)
                    EditorGUILayout.LabelField(groupDef.name, new GUIStyle(EditorStyles.miniLabel) { wordWrap = true });
                EditorGUILayout.LabelField($"members: {(groupDef.partIds?.Length ?? 0)}", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(groupDef.assemblyId))
                    EditorGUILayout.LabelField($"assembly: {groupDef.assemblyId}", EditorStyles.miniLabel);
            }
            else
            {
                var partDef = FindPartById(_browserSelectedPartId);
                if (partDef != null)
                {
                    var idStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal   = { textColor = new Color(0.20f, 0.62f, 0.95f) },
                        wordWrap = true,
                    };
                    EditorGUILayout.LabelField(partDef.id, idStyle);
                    if (!string.IsNullOrEmpty(partDef.name) && partDef.name != partDef.id)
                        EditorGUILayout.LabelField(partDef.name, new GUIStyle(EditorStyles.miniLabel) { wordWrap = true });
                    if (!string.IsNullOrEmpty(partDef.category))
                        EditorGUILayout.LabelField($"category: {partDef.category}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void EnsureBrowserPreview(string partId)
        {
            // Tear down the group renderer if we're switching to a part.
            if (_browserGroupPreview != null)
            {
                _browserGroupPreview.Dispose();
                _browserGroupPreview   = null;
                _browserGroupPreviewId = null;
            }
            if (string.Equals(_browserPreviewId, partId, StringComparison.Ordinal) && _browserPreview != null)
                return;

            _browserPreview?.Dispose();
            _browserPreview   = null;
            _browserPreviewId = null;
            if (string.IsNullOrEmpty(_pkgId)) return;

            var def = FindPartById(partId);
            if (def == null) return;
            string glbFile = ResolvePartAssetRef(def);
            if (string.IsNullOrEmpty(glbFile)) return;

            string assetPath = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/" + glbFile;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null) return;

            _browserPreview   = new PartModelPreviewRenderer(assetPath, Vector3.zero);
            _browserPreviewId = partId;
        }

        private void EnsureBrowserGroupPreview(string groupId)
        {
            // Tear down the part renderer if we're switching to a group.
            if (_browserPreview != null)
            {
                _browserPreview.Dispose();
                _browserPreview   = null;
                _browserPreviewId = null;
            }
            if (string.Equals(_browserGroupPreviewId, groupId, StringComparison.Ordinal) && _browserGroupPreview != null)
                return;

            _browserGroupPreview?.Dispose();
            _browserGroupPreview   = null;
            _browserGroupPreviewId = null;
            if (_pkg == null || !_pkg.TryGetPartGroup(groupId, out var groupDef)) return;
            if (groupDef.partIds == null || groupDef.partIds.Length == 0) return;

            string partsFolder = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/";
            var members = new List<PartGroupModelPreviewRenderer.MemberPose>();
            foreach (var pid in groupDef.partIds)
            {
                if (string.IsNullOrEmpty(pid)) continue;
                var partDef = FindPartById(pid);
                if (partDef == null) continue;
                string glbFile = ResolvePartAssetRef(partDef);
                if (string.IsNullOrEmpty(glbFile)) continue;
                string assetPath = partsFolder + glbFile;
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null) continue;

                // Use each member's startPosition as the preview pose.
                // Matches the "what does this group look like before it's
                // assembled" mental model — the same pose the existing TTAW
                // group inspector defaults to.
                Vector3 pos = Vector3.zero, scale = Vector3.one;
                Quaternion rot = Quaternion.identity;
                var pp = FindPartPlacement(pid);
                if (pp != null)
                {
                    pos   = PackageJsonUtils.ToVector3(pp.startPosition);
                    rot   = PackageJsonUtils.ToUnityQuaternion(pp.startRotation);
                    scale = PackageJsonUtils.ToVector3(pp.startScale);
                    if (scale.sqrMagnitude < 1e-8f) scale = Vector3.one;
                }
                members.Add(new PartGroupModelPreviewRenderer.MemberPose
                {
                    assetPath = assetPath,
                    position  = pos,
                    rotation  = rot,
                    scale     = scale,
                });
            }
            if (members.Count == 0) return;
            _browserGroupPreview   = new PartGroupModelPreviewRenderer(members);
            _browserGroupPreviewId = groupId;
        }

        private PartDefinition FindPartById(string partId)
        {
            if (_pkg == null || string.IsNullOrEmpty(partId)) return null;
            var parts = _pkg.GetParts();
            if (parts == null) return null;
            foreach (var p in parts)
                if (p != null && string.Equals(p.id, partId, StringComparison.Ordinal))
                    return p;
            return null;
        }
    }
}
