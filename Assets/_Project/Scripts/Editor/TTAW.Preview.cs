// TTAW.Preview.cs — Tool/wire preview lifecycle, target undo/redo, animation cue preview,
//                   and particle effect preview drawing.
// ──────────────────────────────────────────────────────────────────────────────────────────
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.
// ──────────────────────────────────────────────────────────────────────────────────────────

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
        // ── Tool preview ──────────────────────────────────────────────────────

        private void ClearToolPreview()
        {
            if (_toolPreviewGO != null)
            {
                string stackSnippet = System.Environment.StackTrace;
                if (stackSnippet.Length > 500) stackSnippet = stackSnippet.Substring(0, 500);
                Debug.Log($"[TTAW.ToolPreview] Clear — destroying '{_toolPreviewGO.name}' (stack: {stackSnippet})");
                DeselectIfSelected(_toolPreviewGO);
                DestroyImmediate(_toolPreviewGO);
                _toolPreviewGO = null;
            }
            _toolPreviewDef = null;
        }

        // Rate-limited diagnostic so the OnGUI retry loop doesn't spam. Logs
        // the same (target, reason) tuple at most once — use when investigating
        // "tool preview disappeared" reports. Cleared each time we actually
        // spawn a preview so the next issue re-logs fresh.
        private string _lastRefreshDiagKey;
        private void LogRefreshDiag(string msg)
        {
            if (string.Equals(_lastRefreshDiagKey, msg, System.StringComparison.Ordinal)) return;
            _lastRefreshDiagKey = msg;
            Debug.Log($"[TTAW.ToolPreview] {msg}");
        }

        private void ClearWirePreview()
        {
            if (_wirePreviewRoot != null)
            {
                // Destroy procedural meshes before the GO — they are unmanaged assets
                // and won't be GC'd automatically when the MeshFilter is destroyed.
                foreach (var mf in _wirePreviewRoot.GetComponentsInChildren<MeshFilter>())
                    if (mf != null && mf.sharedMesh != null)
                        DestroyImmediate(mf.sharedMesh);
                DestroyImmediate(_wirePreviewRoot);
                _wirePreviewRoot = null;
            }
        }

        private void RefreshWirePreview(StepDefinition step)
        {
            ClearWirePreview();

            Transform root = GetPreviewRoot();
            if (root == null) return;

            // Collect all Connect-family steps up to and including the current step
            // so wires from previously completed steps remain visible.
            int currentSeq = step?.sequenceIndex ?? -1;
            var stepsToShow = new List<StepDefinition>();
            if (_pkg?.steps != null)
                foreach (var s in _pkg.steps)
                    if (s?.wireConnect?.IsConfigured == true && s.sequenceIndex <= currentSeq)
                        stepsToShow.Add(s);

            if (stepsToShow.Count == 0) return;

            _wirePreviewRoot = new GameObject("[TTAW] WirePreview");
            _wirePreviewRoot.hideFlags = HideFlags.HideAndDontSave;
            _wirePreviewRoot.transform.SetParent(root, false);

            foreach (var showStep in stepsToShow)
            foreach (var wire in showStep.wireConnect.wires)
            {
                if (wire == null) continue;

                Vector3 pA = new Vector3(wire.portA.x, wire.portA.y, wire.portA.z);
                Vector3 pB = new Vector3(wire.portB.x, wire.portB.y, wire.portB.z);
                if (pA == Vector3.zero && pB == Vector3.zero) continue;

                float radius = wire.radius > 0f ? wire.radius : 0.003f;
                Color col = wire.color.a > 0f
                    ? new Color(wire.color.r, wire.color.g, wire.color.b, 1f)
                    : new Color(0.1f, 0.1f, 0.1f, 1f);
                int subdivs = Mathf.Max(1, wire.subdivisions);

                // Build sag knots in local space.
                // sag=0 (unset) uses natural default (1.0). 0.01=rigid, 1=natural, 2+=heavy droop.
                float wireLength  = Vector3.Distance(pA, pB);
                float sagFactor   = wire.sag > 0f ? wire.sag : 1.0f;
                float sagDepth    = sagFactor * (wireLength * 0.12f + 0.04f);
                var knotPositions = new SceneFloat3[subdivs + 2];
                knotPositions[0]           = PackageJsonUtils.ToFloat3(pA);
                knotPositions[subdivs + 1] = PackageJsonUtils.ToFloat3(pB);
                for (int k = 0; k < subdivs; k++)
                {
                    float t = (k + 1f) / (subdivs + 1f);
                    knotPositions[k + 1] = PackageJsonUtils.ToFloat3(new Vector3(
                        Mathf.Lerp(pA.x, pB.x, t),
                        Mathf.Lerp(pA.y, pB.y, t) - sagDepth * Mathf.Sin(Mathf.PI * t),
                        Mathf.Lerp(pA.z, pB.z, t)));
                }

                // Delegate to SplinePartFactory — same path as play mode.
                var tangentMode = string.Equals(wire.interpolation, "linear",
                    System.StringComparison.OrdinalIgnoreCase)
                    ? UnityEngine.Splines.TangentMode.Linear
                    : UnityEngine.Splines.TangentMode.AutoSmooth;

                var splineDef = new SplinePathDefinition
                {
                    radius     = radius,
                    segments   = 16,
                    metallic   = 0f,
                    smoothness = 0.4f,
                    knots      = knotPositions
                };

                var wireGo = SplinePartFactory.Create(
                    $"Wire_{wire.targetId}", splineDef, col, _wirePreviewRoot.transform, tangentMode);
                wireGo.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void RefreshToolPreview(ref TargetEditState t)
        {
            string diagTargetId = t.def != null ? t.def.id : "<null def>";
            LogRefreshDiag($"enter target='{diagTargetId}' showPreview={_showToolPreview} pkgId='{_pkgId ?? "<null>"}' toolPreviewGO={(_toolPreviewGO != null ? "live" : "null")} selectedIdx={_selectedIdx}");

            // Split guards into two buckets:
            //
            //  • SEMANTIC ("this target has no tool / preview is off") → clear
            //    the old preview. The user's intent is "no tool shown here."
            //
            //  • TRANSIENT ("assets / services not ready yet") → keep the old
            //    preview. A frame later the retry will find them and replace.
            //    The worst offender is `previewRoot == null` immediately after
            //    a domain reload, before the spawner re-registers its service.
            //    Clearing in that window destroys the preview and leaves no
            //    replacement — the "tool visual disappears after compile" bug.

            // ── Semantic guards (clear and exit) ─────────────────────────────
            if (!_showToolPreview || string.IsNullOrEmpty(_pkgId))
            {
                LogRefreshDiag($"SEMANTIC bail: showPreview={_showToolPreview} pkgId='{_pkgId ?? "<null>"}' — Clear+return");
                ClearToolPreview();
                return;
            }

            // Target without a tool (pure observe/measure/confirm target) —
            // the old preview is no longer relevant.
            if (_targetToolIdMap == null || !_targetToolIdMap.TryGetValue(t.def.id, out string toolId))
            {
                LogRefreshDiag($"SEMANTIC bail: _targetToolIdMap is {(_targetToolIdMap == null ? "null" : $"set ({_targetToolIdMap.Count} entries)")} — no tool for '{diagTargetId}' — Clear+return");
                ClearToolPreview();
                return;
            }

            ToolDefinition toolDef = null;
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) { toolDef = td; break; }

            if (toolDef == null || string.IsNullOrEmpty(toolDef.assetRef))
            {
                LogRefreshDiag($"SEMANTIC bail: toolDef={(toolDef == null ? "null" : $"'{toolId}' assetRef='{toolDef?.assetRef ?? "<null>"}'")} — Clear+return");
                ClearToolPreview();
                return;
            }

            // ── Transient guards (silently bail, keep old preview) ───────────
            Transform previewRoot = GetPreviewRoot();
            if (previewRoot == null)
            {
                LogRefreshDiag($"TRANSIENT bail: previewRoot null (ISpawnerQueryService not registered yet)");
                return;
            }

            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{toolDef.assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            // Fallback: try assets/tools/ prefix when assetRef is a bare filename
            if (pfb == null && !toolDef.assetRef.Contains("/"))
            {
                string prefixed = $"Assets/_Project/Data/Packages/{_pkgId}/assets/tools/{toolDef.assetRef}";
                pfb = AssetDatabase.LoadAssetAtPath<GameObject>(prefixed);
                if (pfb == null)
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(prefixed))
                        if (asset is GameObject go2) { pfb = go2; break; }
                if (pfb != null) path = prefixed;
            }

            if (pfb == null)
            {
                LogRefreshDiag($"TRANSIENT bail: asset not found at '{path}'");
                Debug.LogWarning($"[ToolTargetAuthoring] Tool asset not found: {path}");
                return;
            }

            // Idempotent: if the active preview is already for this exact tool,
            // skip re-instantiation. Keeps the preview stable on repeated task
            // clicks and post-reload lazy-retries.
            if (_toolPreviewGO != null && _toolPreviewDef == toolDef)
            {
                LogRefreshDiag($"idempotent: existing preview matches '{toolId}', skip");
                return;
            }

            LogRefreshDiag($"SPAWN new preview for '{toolId}' at '{path}'");

            // All resources resolved — now we can safely swap. Destroy the
            // previous preview (if any) and spawn the replacement.
            ClearToolPreview();

            // Spawn as a child of previewRoot so it lives in the same coordinate space
            _toolPreviewGO           = Instantiate(pfb, previewRoot);
            _toolPreviewGO.name      = "[ToolTargetAuthoring] ToolPreview";
            _toolPreviewGO.hideFlags = HideFlags.HideAndDontSave;

            // Match the runtime cursor scale (ToolCursorManager.CursorUniformScale = 0.16).
            // The previewRoot may have a non-unit lossyScale (e.g. if parts use a scaled root),
            // so we divide by it to get the correct world-space size.
            const float RuntimeCursorScale = 0.16f;
            float toolCursorScale = (toolDef.scaleOverride > 0f)
                ? RuntimeCursorScale * toolDef.scaleOverride
                : RuntimeCursorScale;
            float rootS = previewRoot.lossyScale.x;
            float localToolScale = Mathf.Approximately(rootS, 0f) ? toolCursorScale : toolCursorScale / rootS;
            _toolPreviewGO.transform.localScale = Vector3.one * localToolScale;
            _lastRefreshDiagKey = null; // reset so next bail re-logs fresh

            // Remove colliders — preview only, must not interfere with click-to-snap raycasts
            foreach (var c in _toolPreviewGO.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(c);

            // Cyan tint via MaterialPropertyBlock to distinguish from real parts
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0.35f, 0.85f, 1f, 1f));
            block.SetColor("_Color",     new Color(0.35f, 0.85f, 1f, 1f)); // Standard fallback
            foreach (var r in _toolPreviewGO.GetComponentsInChildren<Renderer>(true))
                r.SetPropertyBlock(block);

            _toolPreviewDef = toolDef;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Computes the tool's local position and rotation under previewRoot so that
        /// the tool's tipPoint sits at the target position.
        /// After migration, t.rotation is already the mesh rotation — no grip correction needed.
        /// </summary>
        private void ComputeToolLocalTransform(ref TargetEditState t,
            out Vector3 localPos, out Quaternion localRot)
        {
            // t.rotation is the single source of truth (gizmo + Euler field).
            // After migration it IS the mesh rotation (independent of gripRotation).
            localRot = t.rotation;

            // Offset so tipPoint lands exactly on the target position.
            // tipPoint is in the tool's local space; multiply by the GO's localScale
            // so the offset is correct regardless of cursor scale.
            localPos = t.position;
            if (_toolPreviewDef?.toolPose?.HasTipPoint == true)
            {
                float s = _toolPreviewGO != null ? _toolPreviewGO.transform.localScale.x : 1f;
                localPos = t.position - localRot * (_toolPreviewDef.toolPose.GetTipPoint() * s);
            }
        }

        private void UpdateToolPreview(ref TargetEditState sel)
        {
            if (!_showToolPreview || _toolPreviewGO == null || GetPreviewRoot() == null) return;

            ComputeToolLocalTransform(ref sel, out Vector3 localPos, out Quaternion localRot);
            _toolPreviewGO.transform.localPosition = localPos;
            _toolPreviewGO.transform.localRotation = localRot;

            // Yellow dot at the tip contact point
            if (_toolPreviewDef?.toolPose?.HasTipPoint == true)
            {
                Vector3 tipWorld = _toolPreviewGO.transform.TransformPoint(
                    _toolPreviewDef.toolPose.GetTipPoint());
                float tipSize = HandleUtility.GetHandleSize(tipWorld) * 0.06f;
                Handles.color = new Color(1f, 0.85f, 0.1f, 1f);
                Handles.SphereHandleCap(0, tipWorld, Quaternion.identity, tipSize, EventType.Repaint);
            }
        }

        private void FrameInScene()
        {
            Transform previewRoot = GetPreviewRoot();
            if (previewRoot == null) return;
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;

            // Frame on selected target position if available
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
            {
                Vector3 worldPos = previewRoot.TransformPoint(_targets[_selectedIdx].position);
                float frameSize = HandleUtility.GetHandleSize(worldPos) * 0.5f;
                sv.Frame(new Bounds(worldPos, Vector3.one * frameSize), false);
            }
            else
            {
                Selection.activeGameObject = previewRoot.gameObject;
                sv.FrameSelected();
            }

            sv.Repaint();
        }

        // ── Undo / Redo ───────────────────────────────────────────────────────

        private TargetSnapshot CaptureSnapshot(ref TargetEditState t) => new()
        {
            position               = t.position,
            rotation               = t.rotation,
            scale                  = t.scale,
            weldAxis               = t.weldAxis,
            weldLength             = t.weldLength,
            useToolActionRotation  = t.useToolActionRotation,
            toolActionRotationEuler= t.toolActionRotationEuler,
            portA                  = t.portA,
            portB                  = t.portB,
        };

        private void BeginEdit()
        {
            if (_snapshotPending || _selectedIdx < 0 || _targets == null) return;
            _undoStack.Add((_selectedIdx, CaptureSnapshot(ref _targets[_selectedIdx])));
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _snapshotPending = true;
        }

        private void EndEdit() => _snapshotPending = false;

        private void UndoPose()
        {
            if (_undoStack.Count == 0 || _targets == null) return;
            var (idx, prev) = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            if (idx < _targets.Length)
            {
                _redoStack.Add((idx, CaptureSnapshot(ref _targets[idx])));
                ApplySnapshot(idx, prev);
            }
        }

        private void RedoPose()
        {
            if (_redoStack.Count == 0 || _targets == null) return;
            var (idx, next) = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            if (idx < _targets.Length)
            {
                _undoStack.Add((idx, CaptureSnapshot(ref _targets[idx])));
                ApplySnapshot(idx, next);
            }
        }

        private void ApplySnapshot(int idx, TargetSnapshot s)
        {
            ref TargetEditState t   = ref _targets[idx];
            t.position               = s.position;
            t.rotation               = s.rotation;
            t.scale                  = s.scale;
            t.weldAxis               = s.weldAxis;
            t.weldLength             = s.weldLength;
            t.useToolActionRotation  = s.useToolActionRotation;
            t.toolActionRotationEuler= s.toolActionRotationEuler;
            t.portA                  = s.portA;
            t.portB                  = s.portB;
            t.isDirty        = true;
            _snapshotPending = false;
            Repaint();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Centroid of a subassembly's member parts from their authored
        /// <c>assembledPosition</c>. Mirrors the runtime's equivalent so
        /// editor preview pivots on the same spot as Play mode. Returns
        /// null when no members have authored positions.
        /// </summary>
        private Vector3? ComputeAuthoredCentroidLocalEditor(string subassemblyId, Transform target)
        {
            if (_pkg == null || !_pkg.TryGetSubassembly(subassemblyId, out var sub) || sub?.partIds == null)
                return null;
            var previewCfg = _pkg.previewConfig?.partPlacements;
            if (previewCfg == null) return null;

            Vector3 sum = Vector3.zero;
            int n = 0;
            var members = new System.Collections.Generic.HashSet<string>(sub.partIds, StringComparer.Ordinal);
            for (int i = 0; i < previewCfg.Length; i++)
            {
                var pp = previewCfg[i];
                if (pp == null || !members.Contains(pp.partId)) continue;
                Vector3 world = target.parent != null
                    ? target.parent.TransformPoint(new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z))
                    : new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                sum += target.InverseTransformPoint(world);
                n++;
            }
            return n > 0 ? sum / n : (Vector3?)null;
        }

        // ── Animation Cue preview lifecycle ──────────────────────────────────

        // Temporary reparent state — members of a group that were NOT already
        // children of its Group_ root get attached for the duration of the
        // preview, then restored to their original parent on Stop. Prevents
        // "only a few parts animate" when some members live elsewhere in the
        // hierarchy (aggregate sub-roots, previewRoot siblings, etc).
        private readonly List<(Transform child, Transform originalParent)> _previewReparents = new();

        // Transient wrapper GOs created when no persistent Group_ root exists
        // for the subassembly being previewed. Destroyed on Stop.
        private readonly List<GameObject> _previewTransientWrappers = new();

        // Panel-playback queue: cues scheduled to start at a given time
        // offset from when "Play Panel" was pressed. Honors the ∥ / ⇣
        // (parallel / sequenced) toggle on each row.
        private struct PanelCueEntry
        {
            public int    cueIdx;
            public float  startOffset;   // seconds from panel-play start
            public float  duration;      // for sequencing math; the player tracks its own timing
            public bool   started;
            public OSE.UI.Root.IAnimationCuePlayer player;
        }
        private readonly List<PanelCueEntry> _panelQueue = new();
        private double _panelPlayStartTime;
        private StepDefinition _panelPlayStep;
        // Scope of the currently-playing panel. Needed by OnPanelPlayUpdate /
        // StartSinglePanelCue to look up the host's cue array (host-owned
        // storage) instead of the empty step.animationCues.cues.
        private CueScope _panelPlayScope;
        private string _panelPlayScopeKey;
        // Re-entrancy guard: when starting a single cue inside the panel
        // loop, StartCuePreview calls StopAllPreviews — which would normally
        // clear _panelQueue mid-iteration. This flag suppresses that clear.
        private bool _suppressPanelQueueClear;

        /// <summary>
        /// Plays every cue in a timing panel back-to-back, honoring each
        /// row's ∥ / ⇣ toggle: parallel rows start with the previous,
        /// sequenced rows start when the previous finishes. Reuses the
        /// per-cue player infrastructure — same pivot, same target
        /// resolution, same stop semantics. Visual sequence-vs-parallel
        /// authoring can be tested without entering Play mode.
        /// </summary>
        private void StartPanelPreview(StepDefinition step, CueScope scope, string scopeKey, List<int> cueIndices)
        {
            StopAllPreviews();
            if (step == null || cueIndices == null || cueIndices.Count == 0) return;

            var storage = GetHostCueStorage(step, scope, scopeKey);
            var cues = storage.cues;
            if (cues == null || cues.Length == 0) return;

            float runningOffset = 0f;
            float prevDuration  = 0f;

            for (int rowIdx = 0; rowIdx < cueIndices.Count; rowIdx++)
            {
                int ci = cueIndices[rowIdx];
                if (ci < 0 || ci >= cues.Length) continue;
                var cue = cues[ci];
                if (cue == null) continue;

                // First row always starts at offset 0. Sequenced rows wait
                // for the previous to finish; parallel rows start with the
                // previous (same offset).
                if (rowIdx > 0 && cue.sequenceAfterPrevious)
                    runningOffset += prevDuration;

                float duration = cue.durationSeconds > 0f
                    ? cue.durationSeconds
                    : AnimationCueDefaults.GetDefaultDuration(cue.type);

                _panelQueue.Add(new PanelCueEntry
                {
                    cueIdx      = ci,
                    startOffset = runningOffset,
                    duration    = duration,
                    started     = false,
                });
                Debug.Log($"[PanelPlay] row {rowIdx}: cueIdx={ci} type={cue.type} seqAfterPrev={cue.sequenceAfterPrevious} duration={duration:0.00}s startOffset={runningOffset:0.00}s");
                prevDuration = duration;
            }

            if (_panelQueue.Count == 0) return;

            _panelPlayStartTime = EditorApplication.timeSinceStartup;
            _panelPlayStep      = step;
            _panelPlayScope     = scope;
            _panelPlayScopeKey  = scopeKey;
            _previewLastTickTime = _panelPlayStartTime;

            EditorApplication.update -= OnPanelPlayUpdate;
            EditorApplication.update += OnPanelPlayUpdate;
            _previewUpdateRegistered  = true;

            Debug.Log($"[PanelPlay] Scheduled {_panelQueue.Count} cue(s) on step '{step.id}'.");
        }

        private void OnPanelPlayUpdate()
        {
            try
            {
                if (_panelQueue.Count == 0) { StopPanelPreview(); return; }

                double now = EditorApplication.timeSinceStartup;
                float elapsed = (float)(now - _panelPlayStartTime);
                float deltaTime = Mathf.Min((float)(now - _previewLastTickTime), 0.05f);
                _previewLastTickTime = now;

                bool anyAlive = false;
                for (int i = 0; i < _panelQueue.Count; i++)
                {
                    var e = _panelQueue[i];

                    // Start cues whose offset has been reached.
                    if (!e.started && elapsed >= e.startOffset)
                    {
                        e.player = StartSinglePanelCue(_panelPlayStep, _panelPlayScope, _panelPlayScopeKey, e.cueIdx);
                        e.started = true;
                        _panelQueue[i] = e;
                    }

                    if (e.started && e.player != null && e.player.IsPlaying)
                    {
                        bool still = e.player.Tick(deltaTime);
                        if (!still) { try { e.player.Stop(); } catch { } e.player = null; _panelQueue[i] = e; }
                        else        anyAlive = true;
                    }
                    else if (!e.started)
                    {
                        anyAlive = true; // not-yet-started cues keep loop alive
                    }
                }

                SceneView.RepaintAll();
                Repaint();

                if (!anyAlive) StopPanelPreview();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PanelPlay] update failed: {ex.Message}");
                StopPanelPreview();
            }
        }

        private void StopPanelPreview()
        {
            for (int i = 0; i < _panelQueue.Count; i++)
            {
                var e = _panelQueue[i];
                if (e.player != null) { try { e.player.Stop(); } catch { } }
            }
            _panelQueue.Clear();
            _panelPlayStep = null;
            EditorApplication.update -= OnPanelPlayUpdate;
            _previewUpdateRegistered = false;
            // Clean any reparents/wrappers the per-cue Start created.
            CleanupPreviewSideEffects();
            SceneView.RepaintAll();
            Repaint();
        }

        private void CleanupPreviewSideEffects()
        {
            if (_previewReparents.Count > 0)
            {
                for (int i = _previewReparents.Count - 1; i >= 0; i--)
                {
                    var (ch, orig) = _previewReparents[i];
                    if (ch != null) ch.SetParent(orig, worldPositionStays: true);
                }
                _previewReparents.Clear();
            }
            if (_previewTransientWrappers.Count > 0)
            {
                for (int i = _previewTransientWrappers.Count - 1; i >= 0; i--)
                {
                    var w = _previewTransientWrappers[i];
                    if (w == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(w);
                    else                        UnityEngine.Object.DestroyImmediate(w);
                }
                _previewTransientWrappers.Clear();
            }
        }

        /// <summary>
        /// Builds a context for a single cue and starts it without registering
        /// the per-cue update loop (the panel-play loop drives Tick instead).
        /// Returns the player instance, or null on failure.
        /// </summary>
        private OSE.UI.Root.IAnimationCuePlayer StartSinglePanelCue(StepDefinition step, CueScope scope, string scopeKey, int cueIdx)
        {
            var storage = GetHostCueStorage(step, scope, scopeKey);
            var cues = storage.cues;
            if (cues == null || cueIdx < 0 || cueIdx >= cues.Length) return null;
            var entry = cues[cueIdx];
            if (entry == null) return null;

            // All three scopes are host-owned — route through StartHostCuePreview
            // with the right hostKind. Previously Tool scope fell through to the
            // legacy StartCuePreview which reads step.animationCues; since Phase 1
            // moved tool cues onto tool.animationCues, that path found nothing and
            // the play-every-cue button silently no-oped on tool panels.
            string hostKindStr = scope switch
            {
                CueScope.Part        => "part",
                CueScope.Tool        => "tool",
                CueScope.Subassembly => "subassembly",
                _                    => "part",
            };
            _suppressPanelQueueClear = true;
            try
            {
                StartHostCuePreview(entry, cueIdx, step, hostKindStr, scopeKey);
            }
            finally { _suppressPanelQueueClear = false; }

            EditorApplication.update -= OnPreviewUpdate;
            var player = _previewPlayer;
            _previewPlayer       = null;     // panel queue owns it now
            _previewingCueIdx    = -1;
            return player;
        }

        private void StopAllPreviews()
        {
            // Cancel any running Tool × Part Interaction preview and restore pose.
            InteractionPreviewRunner.Stop();

            // Tear down panel-play queue if active — unless we're being
            // called re-entrantly from inside StartSinglePanelCue (in which
            // case the panel loop owns the queue and is about to install
            // the freshly-created player into it).
            if (!_suppressPanelQueueClear && _panelQueue.Count > 0)
            {
                for (int i = 0; i < _panelQueue.Count; i++)
                {
                    var e = _panelQueue[i];
                    if (e.player != null) { try { e.player.Stop(); } catch { } }
                }
                _panelQueue.Clear();
                _panelPlayStep = null;
                EditorApplication.update -= OnPanelPlayUpdate;
            }

            if (_previewPlayer != null)
            {
                try { _previewPlayer.Stop(); }
                catch (System.Exception e) { Debug.LogWarning($"[AnimCuePreview] Stop failed: {e.Message}"); }
                _previewPlayer = null;
            }

            // When re-entered from inside the panel-queue (suppress flag),
            // skip these cleanups too — the side-effect lists belong to a
            // still-running cue in the queue. Final cleanup runs when the
            // panel orchestrator stops via StopPanelPreview.
            if (!_suppressPanelQueueClear)
            {
                if (_previewReparents.Count > 0)
                {
                    for (int i = _previewReparents.Count - 1; i >= 0; i--)
                    {
                        var (ch, orig) = _previewReparents[i];
                        if (ch != null) ch.SetParent(orig, worldPositionStays: true);
                    }
                    _previewReparents.Clear();
                }
                if (_previewTransientWrappers.Count > 0)
                {
                    for (int i = _previewTransientWrappers.Count - 1; i >= 0; i--)
                    {
                        var w = _previewTransientWrappers[i];
                        if (w == null) continue;
                        if (Application.isPlaying) UnityEngine.Object.Destroy(w);
                        else                        UnityEngine.Object.DestroyImmediate(w);
                    }
                    _previewTransientWrappers.Clear();
                }
            }

            _previewingCueIdx   = -1;
            _previewingForStepId = null;
            if (_previewUpdateRegistered)
            {
                EditorApplication.update -= OnPreviewUpdate;
                _previewUpdateRegistered  = false;
            }
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Preview a host-owned cue (selection-scoped inspector). The
        /// currently-selected part or group is the host: for a part,
        /// targets = [that part's live GO]; for a group, targets = its
        /// Group_* root (single target — shake/pulse/particle just move
        /// the root, OrientSubassembly computes centroid-pivot math).
        /// Reuses the existing preview update loop.
        /// </summary>
        private void StartHostCuePreview(AnimationCueEntry entry, int cueIdx, StepDefinition step, string hostKind, string hostId)
        {
            StopAllPreviews();
            if (entry == null || string.IsNullOrEmpty(entry.type) || string.IsNullOrEmpty(hostId)) return;

            var targets    = new List<GameObject>();
            var startPoses = new List<OSE.UI.Root.AnimationCueResolvedPose>();
            var asmPoses   = new List<OSE.UI.Root.AnimationCueResolvedPose>();

            if (string.Equals(hostKind, "subassembly", StringComparison.Ordinal))
            {
                if (_subassemblyRootGOs != null
                    && _subassemblyRootGOs.TryGetValue(hostId, out var root)
                    && root != null)
                {
                    targets.Add(root);
                    var t = root.transform;
                    var pose = new OSE.UI.Root.AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
                    startPoses.Add(pose);
                    asmPoses.Add(pose);
                }
            }
            else if (string.Equals(hostKind, "tool", StringComparison.Ordinal))
            {
                // TTAW spawns its own tool preview GO ("[ToolTargetAuthoring]
                // ToolPreview") — prefer that over scene-search since the
                // runtime cursor preview isn't live in edit mode. Falls back
                // to any scene GO named after the tool id (runtime name
                // convention) then to anchor-only resolution.
                GameObject go = _toolPreviewGO != null ? _toolPreviewGO : FindLivePartGO(hostId);
                if (go != null)
                {
                    targets.Add(go);
                    var t = go.transform;
                    var pose = new OSE.UI.Root.AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
                    startPoses.Add(pose);
                    asmPoses.Add(pose);
                }
            }
            else if (string.Equals(hostKind, "part", StringComparison.Ordinal))
            {
                GameObject go = FindLivePartGO(hostId);
                if (go != null)
                {
                    targets.Add(go);
                    // Find matching PartEditState for pose context (best effort).
                    if (_parts != null)
                    {
                        for (int pi = 0; pi < _parts.Length; pi++)
                        {
                            if (!string.Equals(_parts[pi].def?.id, hostId, StringComparison.Ordinal)) continue;
                            ref PartEditState p = ref _parts[pi];
                            startPoses.Add(new OSE.UI.Root.AnimationCueResolvedPose { Position = p.startPosition,     Rotation = p.startRotation,     Scale = p.startScale });
                            asmPoses.Add(new OSE.UI.Root.AnimationCueResolvedPose   { Position = p.assembledPosition, Rotation = p.assembledRotation, Scale = p.assembledScale });
                            break;
                        }
                    }
                    if (startPoses.Count == 0)
                    {
                        var t = go.transform;
                        var pose = new OSE.UI.Root.AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
                        startPoses.Add(pose);
                        asmPoses.Add(pose);
                    }
                }
            }

            if (targets.Count == 0)
            {
                Debug.LogWarning($"[AnimCuePreview] No host target resolved. selectedGroupIdx={_selectedGroupIdx}, selectedPartIdx={_selectedPartIdx}, groups.Length={_groups?.Length ?? 0}, parts.Length={_parts?.Length ?? 0}, subassemblyRootGOs.Count={_subassemblyRootGOs?.Count ?? 0}");
                return;
            }

            Debug.Log($"[AnimCuePreview] Starting '{entry.type}' on {targets.Count} target(s): {string.Join(", ", targets.ConvertAll(t => t ? t.name : "<null>"))}");

            OSE.UI.Root.IAnimationCuePlayer player = CreatePreviewPlayer(entry.type);
            if (player == null)
            {
                Debug.LogWarning($"[AnimCuePreview] Unknown cue type '{entry.type}'.");
                return;
            }

            float duration = entry.durationSeconds > 0f
                ? entry.durationSeconds
                : AnimationCueDefaults.GetDefaultDuration(entry.type);

            var ctx = new OSE.UI.Root.AnimationCueContext(entry, targets, startPoses, asmPoses, duration);
            try { player.Start(ctx); }
            catch (System.Exception e)
            {
                Debug.LogError($"[AnimCuePreview] player.Start failed: {e}");
                return;
            }

            _previewPlayer       = player;
            _previewingCueIdx    = cueIdx;
            _previewingForStepId = step?.id;
            _previewStartTime    = EditorApplication.timeSinceStartup;
            _previewLastTickTime = EditorApplication.timeSinceStartup;

            // Always force-clean the delegate before registering so a
            // prior crash / exception can't leave us in a state where the
            // flag says "registered" but the delegate is actually gone.
            EditorApplication.update -= OnPreviewUpdate;
            EditorApplication.update += OnPreviewUpdate;
            _previewUpdateRegistered  = true;

            SceneView.RepaintAll();
            Repaint();
        }

        private void StartCuePreview(StepDefinition step, int cueIdx)
        {
            StopAllPreviews();

            var payload = step.animationCues;
            if (payload?.cues == null || cueIdx >= payload.cues.Length) return;
            var entry = payload.cues[cueIdx];

            // Resolve target GOs from part IDs.
            // If the cue has no explicit targetPartIds, fall back to all of the step's
            // requiredPartIds so authors can press ▶ Play without having to tick every
            // part first.
            var targets    = new List<GameObject>();
            var startPoses = new List<OSE.UI.Root.AnimationCueResolvedPose>();
            var asmPoses   = new List<OSE.UI.Root.AnimationCueResolvedPose>();

            // Subassembly-scoped cues resolve to the Group_* root when one is
            // spawned. If no root exists for this step, fall back to the
            // subassembly's member part GOs so Play still animates something
            // visible instead of silently no-op'ing.
            bool groupRootResolved = false;
            if (!string.IsNullOrEmpty(entry.targetSubassemblyId))
            {
                Debug.Log($"[AnimCuePreview] Cue scoped to subassembly '{entry.targetSubassemblyId}'.");
                // After editor restart, Group_ GOs (HideFlags.DontSave)
                // are destroyed but the dictionary may hold stale keys.
                // Trigger a package reload to recreate them if needed.
                GameObject groupRoot = null;
                bool hasKey = _subassemblyRootGOs != null
                    && _subassemblyRootGOs.TryGetValue(entry.targetSubassemblyId, out groupRoot);

                // If the key is missing or the GO was destroyed (editor
                // restart clears DontSave GOs), rebuild all group roots
                // for the current step before giving up.
                if (!hasKey || groupRoot == null)
                {
                    if (hasKey) _subassemblyRootGOs.Remove(entry.targetSubassemblyId);
                    if (_stepIds != null && _stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length)
                    {
                        var refreshStep = FindStep(_stepIds[_stepFilterIdx]);
                        if (refreshStep != null)
                        {
                            Debug.Log("[AnimCuePreview] Group root missing — rebuilding subassembly roots.");
                            EnsureAllSubassemblyRoots(refreshStep);
                        }
                    }
                    if (_subassemblyRootGOs != null)
                        _subassemblyRootGOs.TryGetValue(entry.targetSubassemblyId, out groupRoot);
                }
                // Always rebuild the group hierarchy before preview.
                // Parts may be partially reparented, inactive, or live in
                // another group after navigation — rebuilding guarantees a
                // correct centroid every time. Cheap (just reparenting).
                if (_stepIds != null && _stepFilterIdx > 0 && _stepFilterIdx < _stepIds.Length)
                {
                    var refreshStep = FindStep(_stepIds[_stepFilterIdx]);
                    if (refreshStep != null) EnsureAllSubassemblyRoots(refreshStep);
                }
                // Re-fetch in case roots were recreated during the rebuild.
                if (_subassemblyRootGOs != null)
                    _subassemblyRootGOs.TryGetValue(entry.targetSubassemblyId, out groupRoot);
                if (groupRoot != null)
                {
                    targets.Add(groupRoot);
                    var gt = groupRoot.transform;
                    var gpose = new OSE.UI.Root.AnimationCueResolvedPose
                    {
                        Position = gt.localPosition,
                        Rotation = gt.localRotation,
                        Scale    = gt.localScale,
                    };
                    startPoses.Add(gpose);
                    asmPoses.Add(gpose);
                    groupRootResolved = true;

                    Debug.Log($"[AnimCuePreview] Resolved group root '{groupRoot.name}' at localPos={gt.localPosition}");
                }
                else if (_pkg != null
                    && _pkg.TryGetSubassembly(entry.targetSubassemblyId, out var subDef)
                    && subDef?.partIds != null)
                {
                    // Fallback: no Group_ root is registered, so spawn a
                    // transient wrapper root at PreviewRoot, scoop all member
                    // parts under it, and animate the wrapper as a single
                    // group target. Cleanup on Stop restores every member's
                    // original parent.
                    Debug.LogWarning($"[AnimCuePreview] No Group_ root for '{entry.targetSubassemblyId}'. Creating transient wrapper for the preview.");
                    var previewRoot = GetPreviewRoot();
                    var wrapperGO   = new GameObject($"PreviewGroup_{entry.targetSubassemblyId}")
                    {
                        hideFlags = HideFlags.DontSave,
                    };
                    if (previewRoot != null) wrapperGO.transform.SetParent(previewRoot, false);

                    int scooped = 0;
                    foreach (string pid in subDef.partIds)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        var pgo = FindLivePartGO(pid);
                        if (pgo == null) continue;
                        var pt2 = pgo.transform;
                        _previewReparents.Add((pt2, pt2.parent));
                        pt2.SetParent(wrapperGO.transform, worldPositionStays: true);
                        scooped++;
                    }

                    if (scooped > 0)
                    {
                        // Also track the wrapper itself so Stop can destroy it.
                        _previewTransientWrappers.Add(wrapperGO);
                        targets.Add(wrapperGO);
                        var wt = wrapperGO.transform;
                        var wpose = new OSE.UI.Root.AnimationCueResolvedPose
                        {
                            Position = wt.localPosition,
                            Rotation = wt.localRotation,
                            Scale    = wt.localScale,
                        };
                        startPoses.Add(wpose);
                        asmPoses.Add(wpose);
                        groupRootResolved = true;
                        Debug.Log($"[AnimCuePreview] Transient wrapper contains {scooped} member(s).");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(wrapperGO);
                    }
                }
            }

            // When the cue is group-scoped, the group root (or its member
            // parts) is the target set — don't also spam all the step's
            // required parts.
            bool hasSubTarget = groupRootResolved;
            bool hasExplicitTargets = entry.targetPartIds != null && entry.targetPartIds.Length > 0;
            IEnumerable<string> resolveIds = hasSubTarget
                ? Array.Empty<string>()
                : (hasExplicitTargets
                    ? (IEnumerable<string>)entry.targetPartIds
                    : (step.requiredPartIds ?? Array.Empty<string>()));

            foreach (string pid in resolveIds)
            {
                if (string.IsNullOrEmpty(pid)) continue;
                var go = FindLivePartGO(pid);
                if (go == null) continue;
                targets.Add(go);

                // Match to PartEditState for pose data
                bool foundPart = false;
                if (_parts != null)
                {
                    for (int pi = 0; pi < _parts.Length; pi++)
                    {
                        if (!string.Equals(_parts[pi].def?.id, pid, StringComparison.Ordinal)) continue;
                        ref PartEditState p = ref _parts[pi];
                        startPoses.Add(new OSE.UI.Root.AnimationCueResolvedPose
                        {
                            Position = p.startPosition,
                            Rotation = p.startRotation,
                            Scale    = p.startScale,
                        });
                        asmPoses.Add(new OSE.UI.Root.AnimationCueResolvedPose
                        {
                            Position = p.assembledPosition,
                            Rotation = p.assembledRotation,
                            Scale    = p.assembledScale,
                        });
                        foundPart = true;
                        break;
                    }
                }
                if (!foundPart)
                {
                    // Fallback: use GO's current transform as both poses
                    var t2 = go.transform;
                    var pose = new OSE.UI.Root.AnimationCueResolvedPose
                    {
                        Position = t2.localPosition,
                        Rotation = t2.localRotation,
                        Scale    = t2.localScale,
                    };
                    startPoses.Add(pose);
                    asmPoses.Add(pose);
                }
            }

            // Resolve tool targets — the cursor tool preview in the scene
            if (entry.targetToolIds != null && entry.targetToolIds.Length > 0)
            {
                foreach (string toolId in entry.targetToolIds)
                {
                    if (string.IsNullOrEmpty(toolId)) continue;
                    // In editor preview we look for any scene GO whose name matches the tool ID.
                    // Runtime uses ToolCursorManager.ToolPreview; in edit mode we scan the hierarchy.
                    var toolGO = FindLivePartGO(toolId); // scene-search fallback handles this too
                    if (toolGO == null) continue;
                    targets.Add(toolGO);
                    var tt = toolGO.transform;
                    var tpose = new OSE.UI.Root.AnimationCueResolvedPose
                    {
                        Position = tt.localPosition,
                        Rotation = tt.localRotation,
                        Scale    = tt.localScale,
                    };
                    startPoses.Add(tpose);
                    asmPoses.Add(tpose);
                }
            }

            if (targets.Count == 0)
            {
                string fallbackNote = hasExplicitTargets || (entry.targetToolIds?.Length > 0)
                    ? "Check that the listed part/tool IDs are spawned in the scene."
                    : $"No requiredPartIds on step '{step.id}' and no explicit targets — add at least one.";
                Debug.LogWarning($"[AnimCuePreview] No live GOs found for cue '{entry.type}' on step '{step.id}'. {fallbackNote}");
                return;
            }

            OSE.UI.Root.IAnimationCuePlayer player = CreatePreviewPlayer(entry.type);
            if (player == null)
            {
                Debug.LogWarning($"[AnimCuePreview] Unknown cue type '{entry.type}'.");
                return;
            }

            float duration = entry.durationSeconds > 0f
                ? entry.durationSeconds
                : AnimationCueDefaults.GetDefaultDuration(entry.type);
            var ctx = new OSE.UI.Root.AnimationCueContext(entry, targets, startPoses, asmPoses, duration);
            try { player.Start(ctx); }
            catch (System.Exception e)
            {
                Debug.LogError($"[AnimCuePreview] player.Start failed: {e}");
                return;
            }

            _previewPlayer         = player;
            _previewingCueIdx      = cueIdx;
            _previewingForStepId   = step.id;
            _previewStartTime      = EditorApplication.timeSinceStartup;
            _previewLastTickTime   = EditorApplication.timeSinceStartup;

            // Always force-clean the delegate before registering so a
            // prior crash / exception can't leave us in a state where the
            // flag says "registered" but the delegate is actually gone.
            EditorApplication.update -= OnPreviewUpdate;
            EditorApplication.update += OnPreviewUpdate;
            _previewUpdateRegistered  = true;

            SceneView.RepaintAll();
            Repaint();
        }

        private void OnPreviewUpdate()
        {
            try
            {
                if (_previewPlayer == null || !_previewPlayer.IsPlaying)
                {
                    StopAllPreviews();
                    return;
                }

                double now      = EditorApplication.timeSinceStartup;
                float deltaTime = Mathf.Min((float)(now - _previewLastTickTime), 0.05f);
                _previewLastTickTime = now;

                bool still = _previewPlayer.Tick(deltaTime);
                if (!still) StopAllPreviews();

                SceneView.RepaintAll();
                Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AnimCuePreview] OnPreviewUpdate failed: {e.Message}");
                StopAllPreviews();
            }
        }

        // ── Host-section authoring removed ───────────────────────────────────
        //
        // The "Animations & Effects" section that used to render on the
        // selected part / subassembly inspector was a duplicate authoring
        // surface for the same host-owned cue data. Authoring now happens in
        // the task-sequence timing panels (TTAW.TimingPanels.cs +
        // TTAW.CueContext.cs DrawCuesForPart / DrawCuesForSubassembly /
        // DrawCuesForTool), which read/write the same host storage and drive
        // the same scene preview. One surface, one storage — no drift.
        //
        // The block below was intentionally deleted:
        //   DrawSubassemblyAnimationCuesSection
        //   DrawPartAnimationCuesSection
        //   DrawHostCueList
        //   HostCueTypes / HostCueTriggers dropdown constants
        // StartHostCuePreview is still invoked by the timing-panels play
        // button (host-aware preview) and lives in its original location.

        // ── Legacy step-level cue editor (kept for fallback rendering only) ──

        private void DrawAnimationCuesSection(StepDefinition step)
        {
            // Auto-stop preview if user changed step
            if (_previewingForStepId != null && _previewingForStepId != step.id)
                StopAllPreviews();

            var payload  = step.animationCues;
            int cueCount = payload?.cues?.Length ?? 0;

            DrawUnifiedSectionHeader($"ANIMATION & EFFECT CUES ({cueCount})", cueCount, () =>
            {
                if (payload == null)
                {
                    payload = new StepAnimationCuePayload { cues = Array.Empty<AnimationCueEntry>() };
                    step.animationCues = payload;
                }
                var list = new List<AnimationCueEntry>(payload.cues ?? Array.Empty<AnimationCueEntry>());
                list.Add(new AnimationCueEntry { type = "shake" });
                payload.cues = list.ToArray();
                while (_cueFoldouts.Count < payload.cues.Length) _cueFoldouts.Add(false);
                _cueFoldouts[payload.cues.Length - 1] = true;
                _dirtyStepIds.Add(step.id);
                Repaint();
            });

            if (payload == null || payload.cues == null || payload.cues.Length == 0)
            {
                EditorGUILayout.LabelField("  No animation cues. Press + to add one.", EditorStyles.miniLabel);
                return;
            }

            // previewDelaySeconds
            EditorGUI.BeginChangeCheck();
            float newDelay = FloatFieldClip("  Preview Delay (s)", payload.previewDelaySeconds);
            if (EditorGUI.EndChangeCheck())
            {
                payload.previewDelaySeconds = Mathf.Max(0f, newDelay);
                _dirtyStepIds.Add(step.id);
            }

            // Ensure foldout list is sized
            while (_cueFoldouts.Count < payload.cues.Length) _cueFoldouts.Add(true);

            // Draw each cue — collect mutations to apply after loop
            int removeIdx   = -1;
            int moveUpIdx   = -1;
            int moveDownIdx = -1;

            for (int i = 0; i < payload.cues.Length; i++)
            {
                DrawCueEntry(step, payload.cues, i, out bool remove, out bool moveUp, out bool moveDown);
                if (remove)   removeIdx   = i;
                if (moveUp)   moveUpIdx   = i;
                if (moveDown) moveDownIdx = i;
            }

            // Apply mutations after the loop
            if (removeIdx >= 0)
            {
                if (_previewingCueIdx == removeIdx) StopAllPreviews();
                var list = new List<AnimationCueEntry>(payload.cues);
                list.RemoveAt(removeIdx);
                payload.cues = list.ToArray();
                if (removeIdx < _cueFoldouts.Count) _cueFoldouts.RemoveAt(removeIdx);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
            else if (moveUpIdx > 0)
            {
                var arr = payload.cues;
                (arr[moveUpIdx], arr[moveUpIdx - 1]) = (arr[moveUpIdx - 1], arr[moveUpIdx]);
                if (moveUpIdx < _cueFoldouts.Count && moveUpIdx - 1 < _cueFoldouts.Count)
                    (_cueFoldouts[moveUpIdx], _cueFoldouts[moveUpIdx - 1]) = (_cueFoldouts[moveUpIdx - 1], _cueFoldouts[moveUpIdx]);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
            else if (moveDownIdx >= 0 && moveDownIdx < payload.cues.Length - 1)
            {
                var arr = payload.cues;
                (arr[moveDownIdx], arr[moveDownIdx + 1]) = (arr[moveDownIdx + 1], arr[moveDownIdx]);
                if (moveDownIdx < _cueFoldouts.Count && moveDownIdx + 1 < _cueFoldouts.Count)
                    (_cueFoldouts[moveDownIdx], _cueFoldouts[moveDownIdx + 1]) = (_cueFoldouts[moveDownIdx + 1], _cueFoldouts[moveDownIdx]);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
        }

        private void DrawCueEntry(StepDefinition step, AnimationCueEntry[] cues, int idx,
                                  out bool remove, out bool moveUp, out bool moveDown)
        {
            remove   = false;
            moveUp   = false;
            moveDown = false;

            var cue   = cues[idx];
            int total = cues.Length;

            // ── Foldout header row ──────────────────────────────────────────────
            var rowBg = new GUIStyle(EditorStyles.label)
            {
                normal  = { background = Texture2D.grayTexture },
                padding = new RectOffset(4, 4, 2, 2),
            };
            EditorGUILayout.BeginHorizontal();
            _cueFoldouts[idx] = EditorGUILayout.Foldout(_cueFoldouts[idx],
                $"  Cue {idx + 1}:  {cue.type ?? "(unset)"}", true, EditorStyles.foldout);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(idx == 0);
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22))) moveUp = true;
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(idx >= total - 1);
            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(22))) moveDown = true;
            EditorGUI.EndDisabledGroup();
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(22))) remove = true;
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            if (!_cueFoldouts[idx]) return;

            EditorGUI.indentLevel++;

            // ── Type ────────────────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            int typeIdx    = Mathf.Max(0, Array.IndexOf(_cueTypes, cue.type));
            int newTypeIdx = EditorGUILayout.Popup("Type", typeIdx, _cueTypes);
            if (EditorGUI.EndChangeCheck())
            {
                cues[idx] = cue = new AnimationCueEntry { type = _cueTypes[newTypeIdx] };
                _dirtyStepIds.Add(step.id);
            }

            // Target selection is derived from the cue's host (the selected
            // part or group in the task sequence). The old Target Part IDs
            // toggle block has been removed — authoring is host-scoped now.

            // ── Target Subassembly (orientSubassembly only) ─────────────────────
            if (string.Equals(cue.type, "orientSubassembly", StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(cue.targetSubassemblyId))
                    EditorGUILayout.HelpBox("Set a Target Subassembly ID.", MessageType.Warning);

                if (_pkg?.subassemblies != null && _pkg.subassemblies.Length > 0)
                {
                    string[] subIds   = Array.ConvertAll(_pkg.subassemblies, s => s?.id ?? "?");
                    int      subIdx   = Mathf.Max(0, Array.IndexOf(subIds, cue.targetSubassemblyId));
                    EditorGUI.BeginChangeCheck();
                    int newSubIdx = EditorGUILayout.Popup("Target Subassembly", subIdx, subIds);
                    if (EditorGUI.EndChangeCheck())
                    {
                        cue.targetSubassemblyId = subIds[newSubIdx];
                        cues[idx]               = cue;
                        _dirtyStepIds.Add(step.id);
                    }
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string newSubId = EditorGUILayout.TextField("Target Subassembly ID", cue.targetSubassemblyId ?? "");
                    if (EditorGUI.EndChangeCheck()) { cue.targetSubassemblyId = newSubId; cues[idx] = cue; _dirtyStepIds.Add(step.id); }
                }
            }

            // ── Common fields ───────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();

            int trigIdx    = Mathf.Max(0, Array.IndexOf(_cueTriggers, cue.trigger));
            int newTrigIdx = EditorGUILayout.Popup("Trigger", trigIdx, _cueTriggerLabels);
            cue.trigger    = _cueTriggers[newTrigIdx];

            if (string.Equals(cue.trigger, "afterDelay", StringComparison.Ordinal))
                cue.delaySeconds = Mathf.Max(0f, FloatFieldClip("Delay (s)", cue.delaySeconds));

            if (string.Equals(cue.trigger, "onTaskComplete", StringComparison.Ordinal))
                EditorGUILayout.HelpBox(
                    "Fires when any task in this step is validated. To target a specific task, " +
                    "ensure the part ID you checked above matches that task's part ID.",
                    MessageType.Info);

            // Phase 2: progress-range scheduling for onDuringAction cues.
            // Remaps the cue onto the tool action's 0..1 progress timeline
            // so a weld bead can extend 20–90% of progress, sparks fire at
            // 10%, etc. — exactly matching WeldPreview / DrillPreview.
            if (string.Equals(cue.trigger, "onDuringAction", StringComparison.Ordinal))
            {
                EditorGUILayout.LabelField("Progress Range (on tool action 0..1)", EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                cue.startProgress = EditorGUILayout.Slider(
                    new GUIContent("Start", "When in the tool action this cue begins. 0 = action start."),
                    cue.startProgress, 0f, 1f);
                float endDisplay = cue.endProgress <= 0f ? 1f : cue.endProgress;
                endDisplay = EditorGUILayout.Slider(
                    new GUIContent("End", "When in the tool action this cue completes. 1 = action end."),
                    endDisplay, 0f, 1f);
                cue.endProgress = endDisplay >= 1f ? 1f : endDisplay;
                if (cue.endProgress < cue.startProgress) cue.endProgress = cue.startProgress;
                EditorGUILayout.HelpBox(
                    "Leave Start=0 and End=1 for full-action behaviour (legacy). " +
                    "Set a sub-range to scope the cue onto a window of the tool action's progress.",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }

            cue.durationSeconds = Mathf.Max(0f, FloatFieldClip("Duration (s — 0 = indefinite)", cue.durationSeconds));
            cue.loop            = EditorGUILayout.Toggle("Loop", cue.loop);

            int easingIdx    = Mathf.Max(0, Array.IndexOf(_cueEasings, cue.easing));
            int newEasingIdx = EditorGUILayout.Popup("Easing", easingIdx, _cueEasings);
            cue.easing       = _cueEasings[newEasingIdx];

            int modeIdx    = string.Equals(cue.target, "ghost", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            int newModeIdx = EditorGUILayout.Popup("Target Mode", modeIdx, _cueTargetModes);
            cue.target     = _cueTargetModes[newModeIdx];

            // Parallel / Sequenced is authored via the ∥ / ⇣ toggle on the
            // timing-panel row header — redundant toggle removed to avoid
            // two UIs writing the same field.

            if (string.Equals(cue.type, "animationClip", StringComparison.Ordinal))
            {
                cue.animationClipAssetPath = EditorGUILayout.TextField(
                    new GUIContent("Clip Asset Path",
                        "Project-relative path to a custom animation asset (reserved for future playback)."),
                    cue.animationClipAssetPath ?? "");
            }

            if (EditorGUI.EndChangeCheck()) { cues[idx] = cue; _dirtyStepIds.Add(step.id); }

            // ── Type-specific fields ────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            switch (cue.type)
            {
                case "shake":
                {
                    cue.shakeAmplitude = Mathf.Max(0f, FloatFieldClip("Amplitude (m)", cue.shakeAmplitude));
                    cue.shakeFrequency = Mathf.Max(0f, FloatFieldClip("Frequency (Hz)", cue.shakeFrequency > 0f ? cue.shakeFrequency : 3f));
                    var axis = new Vector3(cue.shakeAxis.x, cue.shakeAxis.y, cue.shakeAxis.z);
                    if (axis == Vector3.zero) axis = Vector3.right;
                    axis           = Vector3FieldClip("Shake Axis", axis);
                    cue.shakeAxis  = new SceneFloat3 { x = axis.x, y = axis.y, z = axis.z };
                    break;
                }
                case "pulse":
                {
                    EditorGUILayout.HelpBox(
                        "Pulse preview uses Time.time=0 in edit mode — colour is pinned to Color A. " +
                        "Best tested in Play mode.", MessageType.Info);
                    var ca = cue.pulseColorA.a > 0f || cue.pulseColorA.r > 0f || cue.pulseColorA.g > 0f || cue.pulseColorA.b > 0f
                             ? new Color(cue.pulseColorA.r, cue.pulseColorA.g, cue.pulseColorA.b, cue.pulseColorA.a)
                             : new Color(0.1f, 0.3f, 1f, 1f);
                    var cb = cue.pulseColorB.a > 0f || cue.pulseColorB.r > 0f || cue.pulseColorB.g > 0f || cue.pulseColorB.b > 0f
                             ? new Color(cue.pulseColorB.r, cue.pulseColorB.g, cue.pulseColorB.b, cue.pulseColorB.a)
                             : new Color(1f, 0.85f, 0f, 1f);
                    ca = EditorGUILayout.ColorField("Color A", ca);
                    cb = EditorGUILayout.ColorField("Color B", cb);
                    cue.pulseColorA = new SceneFloat4 { r = ca.r, g = ca.g, b = ca.b, a = ca.a };
                    cue.pulseColorB = new SceneFloat4 { r = cb.r, g = cb.g, b = cb.b, a = cb.a };
                    cue.pulseSpeed  = Mathf.Max(0f, FloatFieldClip("Speed (rad/s)", cue.pulseSpeed > 0f ? cue.pulseSpeed : 3f));
                    break;
                }
                case "demonstratePlacement":
                {
                    cue.spinRevolutions = FloatFieldClip("Spin Revolutions", cue.spinRevolutions);
                    var sa = new Vector3(cue.spinAxis.x, cue.spinAxis.y, cue.spinAxis.z);
                    if (sa == Vector3.zero) sa = Vector3.up;
                    sa          = Vector3FieldClip("Spin Axis", sa);
                    cue.spinAxis = new SceneFloat3 { x = sa.x, y = sa.y, z = sa.z };
                    break;
                }
                case "poseTransition":
                {
                    // Spin fields intentionally omitted — pose transition is
                    // now strictly a from→to interpolation. Use the dedicated
                    // 'demonstratePlacement' cue when spin is needed.
                    cue.spinRevolutions = 0f;
                    if (EditorGUI.EndChangeCheck()) { cues[idx] = cue; _dirtyStepIds.Add(step.id); EditorGUI.BeginChangeCheck(); }
                    DrawAnimationPoseField("From Pose", ref cue.fromPose, step);
                    DrawAnimationPoseField("To Pose",   ref cue.toPose,   step);
                    cues[idx] = cue;
                    break;
                }
                case "orientSubassembly":
                {
                    var rot = new Vector3(cue.subassemblyRotation.x, cue.subassemblyRotation.y, cue.subassemblyRotation.z);
                    rot                     = Vector3FieldClip("Rotation (Euler °)", rot);
                    cue.subassemblyRotation = new SceneFloat3 { x = rot.x, y = rot.y, z = rot.z };
                    break;
                }
                case "particle":
                {
                    DrawParticleCueFields(cue);
                    break;
                }
                case "emissionPulse":
                {
                    DrawEmissionPulseFields(cue);
                    break;
                }
                case "colorTween":
                {
                    DrawColorTweenFields(cue);
                    break;
                }
                case "materialFade":
                {
                    DrawMaterialFadeFields(cue);
                    break;
                }
                case "clickPop":
                {
                    DrawClickPopFields(cue);
                    break;
                }
                case "poseWobble":
                {
                    DrawPoseWobbleFields(cue);
                    break;
                }
                case "toolVibration":
                {
                    DrawToolVibrationFields(cue);
                    break;
                }
                case "lineBetweenAnchors":
                {
                    DrawLineBetweenAnchorsFields(cue);
                    break;
                }
                case "drawSpline":
                {
                    DrawSplinePathFields(cue);
                    break;
                }
                case "measureLine":
                {
                    DrawMeasureLineFields(cue);
                    break;
                }
                case "screwSpin":
                {
                    DrawScrewSpinFields(cue);
                    break;
                }
                case "transform":
                {
                    // Universal From → To transform animation (position, rotation,
                    // scale). Backed by PoseTransitionPlayer. "Capture Current"
                    // snapshots the selected host's live transform into the
                    // chosen pose so authors see real starting values.
                    EditorGUILayout.HelpBox(
                        "Animates position, rotation, and scale from the From pose " +
                        "to the To pose over Duration. Use Capture Current to " +
                        "snapshot the host's live transform.",
                        MessageType.None);
                    if (EditorGUI.EndChangeCheck()) { cues[idx] = cue; _dirtyStepIds.Add(step.id); EditorGUI.BeginChangeCheck(); }
                    DrawAnimationPoseField("From Pose", ref cue.fromPose, step);
                    DrawAnimationPoseField("To Pose",   ref cue.toPose,   step);
                    cues[idx] = cue;
                    break;
                }
            }
            if (EditorGUI.EndChangeCheck()) { cues[idx] = cue; _dirtyStepIds.Add(step.id); }

            // ── Hold-at-end (optional — only meaningful for pose-based cues) ──
            bool holdCapable =
                string.Equals(cue.type, "transform", StringComparison.Ordinal) ||
                string.Equals(cue.type, "poseTransition", StringComparison.Ordinal) ||
                string.Equals(cue.type, "orientSubassembly", StringComparison.Ordinal);
            if (holdCapable)
            {
                bool newHold = EditorGUILayout.Toggle(
                    new GUIContent("Hold At End",
                        "When ON, children stay at their final animated pose when the cue stops. " +
                        "When OFF (default), they snap back to the baseline pose. " +
                        "Use ON for animations whose end-state should persist (e.g. an orientation flip)."),
                    cue.holdAtEnd);
                // Only mark dirty on an actual value change — avoids the
                // "1 unsaved" appearing the moment an author opens the edit
                // panel without changing anything.
                if (newHold != cue.holdAtEnd)
                {
                    cue.holdAtEnd = newHold;
                    cues[idx] = cue;
                    _dirtyStepIds.Add(step.id);
                }
            }

            // ── Pivot override (optional — types that rotate or emit from a point) ──
            // Default pivot for "orientSubassembly" is the member centroid; for
            // "particle" it is the host's position (centroid for groups). The
            // override lets authors nudge the rotation / effect origin with a
            // local-space offset. Only exposed for types where it is meaningful.
            bool pivotCapable =
                string.Equals(cue.type, "orientSubassembly", StringComparison.Ordinal) ||
                string.Equals(cue.type, "particle", StringComparison.Ordinal) ||
                string.Equals(cue.type, "transform", StringComparison.Ordinal) ||
                string.Equals(cue.type, "poseTransition", StringComparison.Ordinal);
            if (pivotCapable)
            {
                EditorGUILayout.Space(2);
                bool dirtyFromPivot = false;

                bool newOverride = EditorGUILayout.Toggle(
                    new GUIContent("Pivot Override",
                        "When off (default), pivot is the host's natural origin " +
                        "(mesh origin for parts, member centroid for groups). " +
                        "When on, the offset below shifts the rotation / effect origin."),
                    cue.pivotOffsetOverride);
                if (newOverride != cue.pivotOffsetOverride)
                {
                    cue.pivotOffsetOverride = newOverride;
                    if (!newOverride)
                        cue.pivotOffset = new SceneFloat3 { x = 0f, y = 0f, z = 0f };
                    dirtyFromPivot = true;
                }
                if (cue.pivotOffsetOverride)
                {
                    EditorGUI.indentLevel++;
                    var oldOff = new Vector3(cue.pivotOffset.x, cue.pivotOffset.y, cue.pivotOffset.z);
                    var newOff = Vector3FieldClip("Pivot Offset (m, local)", oldOff);
                    if (newOff != oldOff)
                    {
                        cue.pivotOffset = new SceneFloat3 { x = newOff.x, y = newOff.y, z = newOff.z };
                        dirtyFromPivot = true;
                    }

                    if (GUILayout.Button(new GUIContent("Reset to Default",
                        "Clear the pivot override. Rotation / effect returns to the host's natural origin."),
                        EditorStyles.miniButton, GUILayout.Width(140)))
                    {
                        cue.pivotOffsetOverride = false;
                        cue.pivotOffset = new SceneFloat3 { x = 0f, y = 0f, z = 0f };
                        dirtyFromPivot = true;
                    }
                    EditorGUI.indentLevel--;
                }
                if (dirtyFromPivot) { cues[idx] = cue; _dirtyStepIds.Add(step.id); }
            }

            // ── Preview strip ───────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            bool isPreviewing = (_previewingCueIdx == idx && _previewPlayer != null && _previewPlayer.IsPlaying);

            // Trigger hint: preview always fires immediately regardless of authored trigger
            if (!string.Equals(cue.trigger, "onActivate", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(cue.trigger))
            {
                int hintTrigIdx = Mathf.Max(0, Array.IndexOf(_cueTriggers, cue.trigger));
                string trigLabel = _cueTriggerLabels[hintTrigIdx].Split('—')[0].Trim();
                EditorGUILayout.LabelField(
                    $"  Trigger: {trigLabel}  (preview fires immediately)",
                    new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic });
            }

            // ── Single toggle button ──────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            var toggleStyle = new GUIStyle(EditorStyles.miniButton);
            if (isPreviewing) toggleStyle.normal.textColor = new Color(0.3f, 0.95f, 0.45f);
            string toggleLabel = isPreviewing ? "■  Stop Preview" : "▶  Preview";
            if (GUILayout.Button(toggleLabel, toggleStyle, GUILayout.Width(110)))
            {
                if (isPreviewing) StopAllPreviews();
                else              StartCuePreview(step, idx);
            }

            if (isPreviewing)
            {
                float elapsed  = (float)(EditorApplication.timeSinceStartup - _previewStartTime);
                string status  = cue.durationSeconds > 0f
                    ? $"  {elapsed:F1}s / {cue.durationSeconds:F1}s"
                    : $"  {elapsed:F1}s";
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            // Thin progress bar (finite-duration previews)
            if (isPreviewing && cue.durationSeconds > 0f)
            {
                float elapsed2 = (float)(EditorApplication.timeSinceStartup - _previewStartTime);
                float t        = Mathf.Clamp01(elapsed2 / cue.durationSeconds);
                Rect  barRect  = EditorGUILayout.GetControlRect(GUILayout.Height(4f));
                EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * t, barRect.height),
                                   new Color(0.3f, 0.85f, 0.4f));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Single source of truth for instantiating a preview player by
        /// cue type. Mirrors <c>AnimationCueCoordinator._factories</c> so
        /// the editor ▶ button and the runtime scheduler produce the same
        /// behaviour. Returns null for unknown types — the caller logs.
        /// </summary>
        private static OSE.UI.Root.IAnimationCuePlayer CreatePreviewPlayer(string type) => type switch
        {
            "shake"                => new OSE.UI.Root.ShakePlayer(),
            "pulse"                => new OSE.UI.Root.PulsePlayer(),
            "particle"             => new OSE.UI.Root.ParticlePlayer(),
            "demonstratePlacement" => new OSE.UI.Root.DemonstratePlacementPlayer(),
            "poseTransition"       => new OSE.UI.Root.PoseTransitionPlayer(),
            "transform"            => new OSE.UI.Root.PoseTransitionPlayer(),
            "orientSubassembly"    => new OSE.UI.Root.OrientSubassemblyPlayer(),
            // Phase 2 effect cues
            "emissionPulse"        => new OSE.UI.Root.EmissionPulsePlayer(),
            "colorTween"           => new OSE.UI.Root.ColorTweenPlayer(),
            "materialFade"         => new OSE.UI.Root.MaterialFadePlayer(),
            "clickPop"             => new OSE.UI.Root.ClickPopPlayer(),
            "poseWobble"           => new OSE.UI.Root.PoseWobblePlayer(),
            "toolVibration"        => new OSE.UI.Root.ToolVibrationPlayer(),
            "lineBetweenAnchors"   => new OSE.UI.Root.LineBetweenAnchorsPlayer(),
            "drawSpline"           => new OSE.UI.Root.DrawSplinePlayer(),
            "measureLine"          => new OSE.UI.Root.MeasureLinePlayer(),
            "screwSpin"            => new OSE.UI.Root.ScrewSpinPlayer(),
            _                      => null,
        };

        // Cached preset labels for the particle-cue dropdown — rebuilt on demand.
        private static string[] _cachedParticlePresetIds;

        private static string[] GetParticlePresetIds()
        {
            if (_cachedParticlePresetIds == null || _cachedParticlePresetIds.Length == 0)
            {
                var ids = CompletionParticleEffect.PresetIds;
                _cachedParticlePresetIds = ids != null ? ids.ToArray() : Array.Empty<string>();
            }
            return _cachedParticlePresetIds;
        }

        // Converts a project-path GameObject asset into its Resources.Load-relative
        // path (no extension) when the asset lives under any Resources/ folder.
        // Returns null when the asset is not under a Resources/ root — the UI uses
        // that signal to warn the author.
        private static string ResolveResourcesRelativePath(GameObject asset)
        {
            if (asset == null) return null;
            string projectPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(projectPath)) return null;
            projectPath = projectPath.Replace('\\', '/');

            const string marker = "/Resources/";
            int idx = projectPath.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return null;

            string rel = projectPath.Substring(idx + marker.Length);
            int dot = rel.LastIndexOf('.');
            if (dot >= 0) rel = rel.Substring(0, dot);
            return rel;
        }

        private void DrawParticleCueFields(AnimationCueEntry cue)
        {
            // Source mode — preset (procedural) vs. prefab (Resources.Load).
            string mode = string.IsNullOrEmpty(cue.particleSourceMode) ? "preset" : cue.particleSourceMode;
            int modeIdx = string.Equals(mode, "prefab", StringComparison.Ordinal) ? 1 : 0;
            int newModeIdx = EditorGUILayout.Popup(
                new GUIContent("Source",
                    "Preset: procedural effect from CompletionParticleEffect (weld_arc, torque_sparks, …). " +
                    "Prefab: Resources.Load a prefab by package-relative path."),
                modeIdx, new[] { "preset", "prefab" });
            cue.particleSourceMode = newModeIdx == 1 ? "prefab" : "preset";

            if (cue.particleSourceMode == "preset")
            {
                string[] ids = GetParticlePresetIds();
                int sel = Mathf.Max(0, Array.IndexOf(ids, cue.particlePresetId ?? ""));
                int newSel = EditorGUILayout.Popup(
                    new GUIContent("Preset",
                        "Named entry from CompletionParticleEffect.Presets. Single source of truth — " +
                        "add new presets there to see them here."),
                    sel, ids);
                if (ids.Length > 0)
                    cue.particlePresetId = ids[Mathf.Clamp(newSel, 0, ids.Length - 1)];
            }
            else
            {
                string currentRef = cue.particlePrefabRef ?? "";
                GameObject current = null;
                if (!string.IsNullOrEmpty(currentRef))
                    current = Resources.Load<GameObject>(currentRef);

                GameObject picked = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Prefab",
                        "Drop a prefab that lives under any Assets/.../Resources/ folder. " +
                        "Stored as the Resources-relative path (no extension)."),
                    current, typeof(GameObject), false);

                if (picked != current)
                {
                    string resolved = ResolveResourcesRelativePath(picked);
                    if (picked != null && resolved == null)
                    {
                        EditorGUILayout.HelpBox(
                            "Prefab is not under a Resources/ folder — Resources.Load cannot find it. " +
                            "Move the prefab under Assets/.../Resources/ and drop it again.",
                            MessageType.Warning);
                    }
                    else
                    {
                        cue.particlePrefabRef = resolved ?? "";
                    }
                }
                else
                {
                    cue.particlePrefabRef = EditorGUILayout.TextField(
                        new GUIContent("Prefab Path",
                            "Resources-relative path (no extension). Set automatically when dropping a prefab."),
                        cue.particlePrefabRef ?? "");
                }
            }

            // Scale multiplier — applies on top of host local scale.
            float scale = cue.particleScale > 0f ? cue.particleScale : 1f;
            cue.particleScale = Mathf.Clamp(
                EditorGUILayout.Slider(
                    new GUIContent("Scale", "Multiplier applied to the emitter base scale. 1.0 = native size."),
                    scale, 0.1f, 5f),
                0.05f, 20f);

            // Colour tint — alpha = 0 means "no tint".
            Color tint = new Color(
                cue.particleColorTint.r, cue.particleColorTint.g,
                cue.particleColorTint.b, cue.particleColorTint.a);
            tint = EditorGUILayout.ColorField(
                new GUIContent("Tint",
                    "Multiplied into each ParticleSystem.main.startColor at spawn. " +
                    "Leave alpha = 0 to skip tinting (use the preset's native colour)."),
                tint);
            cue.particleColorTint = new SceneFloat4 { r = tint.r, g = tint.g, b = tint.b, a = tint.a };
        }

        // ── Phase 2 cue-type editors ─────────────────────────────────────
        //
        // Each helper renders the fields that its cue type reads. Common
        // shared pieces (from/to color pair, anchor ref pair) are extracted
        // into tiny helpers so the per-type helpers stay focused on what is
        // unique.

        private static readonly string[] _anchorRefOptions =
        {
            "",
            "targetSurface",
            "weldStart",
            "weldEnd",
            "weldMid",
            "measureAnchorA",
            "measureAnchorB",
            "toolTip",
            "toolGrip",
            "partAssembledCenter",
        };

        private static string DrawAnchorRefPopup(string label, string tooltip, string current)
        {
            // When current matches a known ref, show the popup with it
            // selected. When it doesn't (e.g. "literal:0,0,0"), show a
            // text field instead so the authored value is preserved.
            bool isKnown = System.Array.IndexOf(_anchorRefOptions, current ?? "") >= 0;
            if (isKnown)
            {
                int idx = System.Array.IndexOf(_anchorRefOptions, current ?? "");
                int newIdx = EditorGUILayout.Popup(new GUIContent(label, tooltip + "\nSwitch to literal: in the field below for custom coordinates."), idx, _anchorRefOptions);
                string picked = _anchorRefOptions[Mathf.Clamp(newIdx, 0, _anchorRefOptions.Length - 1)];
                if (string.Equals(picked, "", System.StringComparison.Ordinal))
                {
                    // Author picked the empty option — offer a text field so
                    // they can type a literal ref next frame.
                    return EditorGUILayout.TextField(new GUIContent(" ", "Type a custom ref, e.g. literal:0.0,0.1,0.0"), "");
                }
                return picked;
            }
            else
            {
                return EditorGUILayout.TextField(new GUIContent(label, tooltip), current ?? "");
            }
        }

        private static (Color fromC, Color toC) DrawColorPair(string fromLabel, string toLabel,
            SceneFloat4 from, SceneFloat4 to, Color fromDefault, Color toDefault)
        {
            Color cf = from.a > 0f || from.r > 0f || from.g > 0f || from.b > 0f
                ? new Color(from.r, from.g, from.b, from.a) : fromDefault;
            Color ct = to.a > 0f || to.r > 0f || to.g > 0f || to.b > 0f
                ? new Color(to.r, to.g, to.b, to.a) : toDefault;
            cf = EditorGUILayout.ColorField(fromLabel, cf);
            ct = EditorGUILayout.ColorField(toLabel, ct);
            return (cf, ct);
        }

        private void DrawEmissionPulseFields(AnimationCueEntry cue)
        {
            EditorGUILayout.HelpBox(
                "Tweens emission colour and intensity on the host's material. " +
                "Pair with trigger=onDuringAction + a progress range for a " +
                "glow that ramps with the tool action.",
                MessageType.None);
            var (fromE, toE) = DrawColorPair("From Emission", "To Emission",
                cue.fromEmission, cue.toEmission, Color.black, Color.white);
            cue.fromEmission = new SceneFloat4 { r = fromE.r, g = fromE.g, b = fromE.b, a = fromE.a };
            cue.toEmission   = new SceneFloat4 { r = toE.r,   g = toE.g,   b = toE.b,   a = toE.a };
            cue.fromIntensity = Mathf.Clamp(EditorGUILayout.FloatField("From Intensity", cue.fromIntensity), 0f, 20f);
            cue.toIntensity   = Mathf.Clamp(EditorGUILayout.FloatField("To Intensity",   cue.toIntensity   > 0f ? cue.toIntensity : 1f), 0f, 20f);
        }

        private void DrawColorTweenFields(AnimationCueEntry cue)
        {
            var (fromC, toC) = DrawColorPair("From Color", "To Color",
                cue.fromColor, cue.toColor, Color.white, Color.gray);
            cue.fromColor = new SceneFloat4 { r = fromC.r, g = fromC.g, b = fromC.b, a = fromC.a };
            cue.toColor   = new SceneFloat4 { r = toC.r,   g = toC.g,   b = toC.b,   a = toC.a };
        }

        private void DrawMaterialFadeFields(AnimationCueEntry cue)
        {
            EditorGUILayout.HelpBox(
                "Combined colour + emission tween — recreates a weld bead " +
                "fading from hot silver + orange emission to cool grey over " +
                "the duration.",
                MessageType.None);
            var (fromC, toC) = DrawColorPair("From Color", "To Color",
                cue.fromColor, cue.toColor,
                new Color(0.85f, 0.82f, 0.72f, 1f),
                new Color(0.55f, 0.55f, 0.52f, 1f));
            cue.fromColor = new SceneFloat4 { r = fromC.r, g = fromC.g, b = fromC.b, a = fromC.a };
            cue.toColor   = new SceneFloat4 { r = toC.r,   g = toC.g,   b = toC.b,   a = toC.a };
            var (fromE, toE) = DrawColorPair("From Emission", "To Emission",
                cue.fromEmission, cue.toEmission,
                new Color(1f, 0.9f, 0.7f, 1f),
                Color.black);
            cue.fromEmission = new SceneFloat4 { r = fromE.r, g = fromE.g, b = fromE.b, a = fromE.a };
            cue.toEmission   = new SceneFloat4 { r = toE.r,   g = toE.g,   b = toE.b,   a = toE.a };
            cue.fromIntensity = Mathf.Clamp(EditorGUILayout.FloatField("From Intensity", cue.fromIntensity > 0f ? cue.fromIntensity : 1.5f), 0f, 20f);
            cue.toIntensity   = Mathf.Clamp(EditorGUILayout.FloatField("To Intensity",   cue.toIntensity), 0f, 20f);
        }

        private void DrawClickPopFields(AnimationCueEntry cue)
        {
            Color col = cue.fromColor.a > 0f
                ? new Color(cue.fromColor.r, cue.fromColor.g, cue.fromColor.b, cue.fromColor.a)
                : new Color(0.2f, 1.0f, 0.4f, 0.9f);
            col = EditorGUILayout.ColorField("Color", col);
            cue.fromColor = new SceneFloat4 { r = col.r, g = col.g, b = col.b, a = col.a };
            cue.pulseScale = EditorGUILayout.Slider("Pulse Scale",
                cue.pulseScale > 0f ? cue.pulseScale : 1.8f, 0.5f, 5f);
        }

        private void DrawPoseWobbleFields(AnimationCueEntry cue)
        {
            var axis = new Vector3(
                cue.wobbleAxis.x != 0f ? cue.wobbleAxis.x : 1f,
                cue.wobbleAxis.y != 0f ? cue.wobbleAxis.y : 1f,
                cue.wobbleAxis.z);
            axis = EditorGUILayout.Vector3Field("Wobble Axis (per-axis amp mask)", axis);
            cue.wobbleAxis = new SceneFloat3 { x = axis.x, y = axis.y, z = axis.z };
            cue.wobbleAmplitude = Mathf.Clamp(
                EditorGUILayout.FloatField("Amplitude (rad)", cue.wobbleAmplitude > 0f ? cue.wobbleAmplitude : 0.12f),
                0f, 1f);
            cue.wobbleFrequency = Mathf.Clamp(
                EditorGUILayout.FloatField("Frequency (rad/s)", cue.wobbleFrequency > 0f ? cue.wobbleFrequency : 40f),
                0f, 200f);
        }

        private void DrawToolVibrationFields(AnimationCueEntry cue)
        {
            var amp = new Vector3(
                cue.vibrationAxes.x != 0f ? cue.vibrationAxes.x : 0.0004f,
                cue.vibrationAxes.y != 0f ? cue.vibrationAxes.y : 0.00028f,
                cue.vibrationAxes.z != 0f ? cue.vibrationAxes.z : 0.0002f);
            amp = EditorGUILayout.Vector3Field("Amplitude (m)", amp);
            cue.vibrationAxes = new SceneFloat3 { x = amp.x, y = amp.y, z = amp.z };
            cue.vibrationFrequency = Mathf.Clamp(
                EditorGUILayout.FloatField("Frequency (Hz)", cue.vibrationFrequency > 0f ? cue.vibrationFrequency : 55f),
                0f, 500f);
            cue.vibrationRampIn = EditorGUILayout.Slider("Ramp In (progress)",
                cue.vibrationRampIn > 0f ? cue.vibrationRampIn : 0.15f, 0f, 1f);
            cue.vibrationRampOut = EditorGUILayout.Slider("Ramp Out (progress)",
                cue.vibrationRampOut > 0f ? cue.vibrationRampOut : 0.85f, 0f, 1f);
        }

        private void DrawLineBetweenAnchorsFields(AnimationCueEntry cue)
        {
            cue.anchorARef = DrawAnchorRefPopup("Anchor A", "Start endpoint of the line.", cue.anchorARef);
            cue.anchorBRef = DrawAnchorRefPopup("Anchor B", "End endpoint; the line extends toward this point as progress ramps 0→1.", cue.anchorBRef);
            cue.lineWidth = EditorGUILayout.Slider("Width (m)",
                cue.lineWidth > 0f ? cue.lineWidth : 0.004f, 0.001f, 0.05f);
            Color col = cue.fromColor.a > 0f
                ? new Color(cue.fromColor.r, cue.fromColor.g, cue.fromColor.b, cue.fromColor.a)
                : new Color(0.85f, 0.82f, 0.72f, 1f);
            col = EditorGUILayout.ColorField("Color", col);
            cue.fromColor = new SceneFloat4 { r = col.r, g = col.g, b = col.b, a = col.a };
            Color emi = cue.fromEmission.a > 0f
                ? new Color(cue.fromEmission.r, cue.fromEmission.g, cue.fromEmission.b, cue.fromEmission.a)
                : new Color(1f, 0.9f, 0.7f, 1f);
            emi = EditorGUILayout.ColorField("Emission", emi);
            cue.fromEmission = new SceneFloat4 { r = emi.r, g = emi.g, b = emi.b, a = emi.a };
            cue.lineEmissionIntensity = Mathf.Clamp(
                EditorGUILayout.FloatField("Emission Intensity", cue.lineEmissionIntensity > 0f ? cue.lineEmissionIntensity : 1.5f),
                0f, 20f);
        }

        private void DrawSplinePathFields(AnimationCueEntry cue)
        {
            EditorGUILayout.HelpBox(
                "One-shot tube-mesh path between ≥2 anchor refs. Uses the " +
                "same SplinePartFactory as wires/hoses.",
                MessageType.None);
            int count = cue.splineAnchorRefs != null ? cue.splineAnchorRefs.Length : 2;
            count = Mathf.Clamp(EditorGUILayout.IntField("Knot Count", Mathf.Max(2, count)), 2, 20);
            if (cue.splineAnchorRefs == null || cue.splineAnchorRefs.Length != count)
            {
                var next = new string[count];
                if (cue.splineAnchorRefs != null)
                    for (int i = 0; i < Mathf.Min(count, cue.splineAnchorRefs.Length); i++) next[i] = cue.splineAnchorRefs[i];
                cue.splineAnchorRefs = next;
            }
            for (int i = 0; i < cue.splineAnchorRefs.Length; i++)
                cue.splineAnchorRefs[i] = DrawAnchorRefPopup($"Knot {i}", "Anchor for this knot.", cue.splineAnchorRefs[i]);

            cue.splineRadius = EditorGUILayout.Slider("Tube Radius (m)",
                cue.splineRadius > 0f ? cue.splineRadius : 0.003f, 0.001f, 0.05f);
            cue.splineMetallic = EditorGUILayout.Slider("Metallic",
                cue.splineMetallic > 0f ? cue.splineMetallic : 0.4f, 0f, 1f);
            cue.splineSmoothness = EditorGUILayout.Slider("Smoothness",
                cue.splineSmoothness > 0f ? cue.splineSmoothness : 0.5f, 0f, 1f);
            Color col = cue.fromColor.a > 0f
                ? new Color(cue.fromColor.r, cue.fromColor.g, cue.fromColor.b, cue.fromColor.a)
                : new Color(0.1f, 0.1f, 0.1f, 1f);
            col = EditorGUILayout.ColorField("Color", col);
            cue.fromColor = new SceneFloat4 { r = col.r, g = col.g, b = col.b, a = col.a };
        }

        private void DrawMeasureLineFields(AnimationCueEntry cue)
        {
            cue.anchorARef = DrawAnchorRefPopup("Anchor A", "Measurement start point.", cue.anchorARef ?? "measureAnchorA");
            cue.anchorBRef = DrawAnchorRefPopup("Anchor B", "Measurement end point.", cue.anchorBRef ?? "measureAnchorB");
            string[] units = { "mm", "cm", "m", "inch", "ft" };
            int uIdx = Mathf.Max(0, System.Array.IndexOf(units, cue.measureUnit ?? "mm"));
            int newU = EditorGUILayout.Popup("Display Unit", uIdx, units);
            cue.measureUnit = units[Mathf.Clamp(newU, 0, units.Length - 1)];
            Color col = cue.fromColor.a > 0f
                ? new Color(cue.fromColor.r, cue.fromColor.g, cue.fromColor.b, cue.fromColor.a)
                : new Color(1f, 0.8f, 0.2f, 1f);
            col = EditorGUILayout.ColorField("Line Color", col);
            cue.fromColor = new SceneFloat4 { r = col.r, g = col.g, b = col.b, a = col.a };
        }

        private void DrawScrewSpinFields(AnimationCueEntry cue)
        {
            cue.spinAngleDegrees = EditorGUILayout.FloatField("Total Angle (°)",
                cue.spinAngleDegrees != 0f ? cue.spinAngleDegrees : 120f);
            var sa = new Vector3(cue.spinAxis.x, cue.spinAxis.y, cue.spinAxis.z);
            if (sa == Vector3.zero) sa = Vector3.up;
            sa = EditorGUILayout.Vector3Field("Spin Axis (local)", sa);
            cue.spinAxis = new SceneFloat3 { x = sa.x, y = sa.y, z = sa.z };
        }

        /// <summary>Draws editable position/rotation/scale fields for an AnimationPose with a "Capture" button.</summary>
        private void DrawAnimationPoseField(string label, ref AnimationPose pose, StepDefinition step)
        {
            if (pose == null)
                pose = new AnimationPose
                {
                    position = new SceneFloat3(),
                    rotation = new SceneQuaternion { w = 1f },
                    scale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                };

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var pos = new Vector3(pose.position.x, pose.position.y, pose.position.z);
            var rot = new Quaternion(pose.rotation.x, pose.rotation.y, pose.rotation.z, pose.rotation.w).eulerAngles;
            var scl = new Vector3(pose.scale.x, pose.scale.y, pose.scale.z);
            if (scl == Vector3.zero) scl = Vector3.one;

            EditorGUI.BeginChangeCheck();
            pos = Vector3FieldClip("Position", pos);
            rot = Vector3FieldClip("Rotation °", rot);
            scl = Vector3FieldClip("Scale", scl);
            if (EditorGUI.EndChangeCheck())
            {
                pose.position = new SceneFloat3 { x = pos.x, y = pos.y, z = pos.z };
                var q = Quaternion.Euler(rot);
                pose.rotation = new SceneQuaternion { x = q.x, y = q.y, z = q.z, w = q.w };
                pose.scale    = new SceneFloat3 { x = scl.x, y = scl.y, z = scl.z };
                _dirtyStepIds.Add(step.id);
            }

            // Capture from the selected host's live GO (part OR group root).
            if (GUILayout.Button("Capture Current", EditorStyles.miniButton))
            {
                Transform captured = null;
                string    captureLabel = null;

                // Prefer a group root when a subassembly is selected.
                if (!string.IsNullOrEmpty(_canvasSelectedSubId)
                    && _subassemblyRootGOs != null
                    && _subassemblyRootGOs.TryGetValue(_canvasSelectedSubId, out var groupGO)
                    && groupGO != null)
                {
                    captured = groupGO.transform;
                    captureLabel = $"group '{_canvasSelectedSubId}'";
                }
                else
                {
                    string captureId = null;
                    if (_selectedPartIdx >= 0 && _parts != null && _selectedPartIdx < _parts.Length)
                        captureId = _parts[_selectedPartIdx].def?.id;
                    else if (_selectedTaskSeqIdx >= 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                    {
                        var ord = GetOrDeriveTaskOrder(step);
                        if (_selectedTaskSeqIdx < ord.Count && ord[_selectedTaskSeqIdx].kind == "part")
                            captureId = TaskInstanceId.ToPartId(ord[_selectedTaskSeqIdx].id);
                    }
                    if (!string.IsNullOrEmpty(captureId))
                    {
                        var pgo = FindLivePartGO(captureId);
                        if (pgo != null) { captured = pgo.transform; captureLabel = $"part '{captureId}'"; }
                    }
                }

                if (captured != null)
                {
                    pose.position = new SceneFloat3 { x = captured.localPosition.x, y = captured.localPosition.y, z = captured.localPosition.z };
                    var q2 = captured.localRotation;
                    pose.rotation = new SceneQuaternion { x = q2.x, y = q2.y, z = q2.z, w = q2.w };
                    pose.scale    = new SceneFloat3 { x = captured.localScale.x, y = captured.localScale.y, z = captured.localScale.z };
                    _dirtyStepIds.Add(step.id);
                    GUI.changed = true;
                    Debug.Log($"[AnimCueCapture] Captured {captureLabel}: pos={captured.localPosition} rot={q2.eulerAngles} scl={captured.localScale}");
                }
                else Debug.LogWarning("[AnimCueCapture] Select a part or group row in the task sequence first.");
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>Popup that operates on string values rather than indices.</summary>
        private static string DrawStringPopup(string label, string current, string[] options)
        {
            int idx    = 0;
            for (int i = 0; i < options.Length; i++)
                if (string.Equals(current, options[i], StringComparison.Ordinal)) { idx = i; break; }
            int newIdx = EditorGUILayout.Popup(label, idx, options);
            return options[newIdx];
        }

        // ── Particle Effect section drawing ───────────────────────────────────

        private void StopParticlePreview()
        {
            if (_particleUpdateRegistered)
            {
                EditorApplication.update -= OnParticleUpdate;
                _particleUpdateRegistered = false;
            }
            if (_previewParticleGO != null)
            {
                DestroyImmediate(_previewParticleGO);
                _previewParticleGO = null;
            }
            _previewingParticleIdx    = -1;
            _previewingParticleStepId = null;
            _particleSimTime          = 0f;
            SceneView.RepaintAll();
            Repaint();
        }

        private void StartParticlePreview(StepDefinition step, int idx)
        {
            StopParticlePreview();

            var payload = step.particleEffects;
            if (payload?.effects == null || idx >= payload.effects.Length) return;
            var entry = payload.effects[idx];
            if (string.IsNullOrEmpty(entry.presetId)) return;

            // Find spawn position from first resolved target part
            Vector3 spawnPos = Vector3.zero;
            bool    found    = false;
            IEnumerable<string> pids = (entry.targetPartIds?.Length > 0)
                ? (IEnumerable<string>)entry.targetPartIds
                : (step.requiredPartIds ?? Array.Empty<string>());
            foreach (string pid in pids)
            {
                if (string.IsNullOrEmpty(pid)) continue;
                var go = FindLivePartGO(pid);
                if (go == null) continue;
                spawnPos = go.transform.position;
                found = true;
                break;
            }
            if (!found)
                Debug.LogWarning("[ParticlePreview] No live GO found — spawning at scene origin.");

            float scale = entry.scale > 0f ? entry.scale : 1f;
            _previewParticleGO = CompletionParticleEffect.TrySpawnContinuous(
                entry.presetId, spawnPos, Vector3.one * scale);

            if (_previewParticleGO == null)
            {
                Debug.LogWarning($"[ParticlePreview] Preset '{entry.presetId}' not found or spawn failed.");
                return;
            }

            // Mark as editor-only: never saved to the scene file and automatically
            // destroyed on domain reload / play mode entry.
            _previewParticleGO.hideFlags = HideFlags.HideAndDontSave;

            // Override the preset's loop/duration settings for a sustained editor preview:
            // force loop=true on the root system so the effect keeps repeating while we
            // advance the simulation time. The authored loop setting is preserved in JSON;
            // we only mutate the in-memory instance here.
            var ps = _previewParticleGO.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.loop = true;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _previewingParticleIdx    = idx;
            _previewingParticleStepId = step.id;
            _particleSimTime          = 0f;
            _particleLastTickTime     = EditorApplication.timeSinceStartup;

            if (!_particleUpdateRegistered)
            {
                EditorApplication.update += OnParticleUpdate;
                _particleUpdateRegistered  = true;
            }

            SceneView.RepaintAll();
            Repaint();
        }

        private void OnParticleUpdate()
        {
            if (_previewParticleGO == null)
            {
                StopParticlePreview();
                return;
            }

            double now       = EditorApplication.timeSinceStartup;
            float  deltaTime = Mathf.Min((float)(now - _particleLastTickTime), 0.05f);
            _particleLastTickTime = now;
            _particleSimTime     += deltaTime;

            // Drive the particle system manually — the only way to animate particles in
            // edit mode. Passing the accumulated time with restart=true gives a deterministic
            // state snapshot at that moment (equivalent to "seek to T seconds").
            var rootPs = _previewParticleGO.GetComponent<ParticleSystem>();
            if (rootPs != null)
                rootPs.Simulate(_particleSimTime, withChildren: true, restart: true, fixedTimeStep: false);

            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawParticleEffectsSection(StepDefinition step)
        {
            // Auto-stop if step changed
            if (_previewingParticleStepId != null && _previewingParticleStepId != step.id)
                StopParticlePreview();

            var payload  = step.particleEffects;
            int count    = payload?.effects?.Length ?? 0;

            DrawUnifiedSectionHeader($"PARTICLE EFFECTS ({count})", count, () =>
            {
                if (payload == null)
                {
                    payload = new StepParticleEffectPayload { effects = Array.Empty<ParticleEffectEntry>() };
                    step.particleEffects = payload;
                }
                var list = new List<ParticleEffectEntry>(payload.effects ?? Array.Empty<ParticleEffectEntry>());
                list.Add(new ParticleEffectEntry { presetId = "torque_sparks", trigger = "onActivate", scale = 1f });
                payload.effects = list.ToArray();
                while (_particleFoldouts.Count < payload.effects.Length) _particleFoldouts.Add(false);
                _particleFoldouts[payload.effects.Length - 1] = true;
                _dirtyStepIds.Add(step.id);
                Repaint();
            });

            if (payload == null || payload.effects == null || payload.effects.Length == 0)
            {
                EditorGUILayout.LabelField("  No particle effects. Press + to add one.", EditorStyles.miniLabel);
                return;
            }

            while (_particleFoldouts.Count < payload.effects.Length) _particleFoldouts.Add(true);

            int removeIdx = -1, moveUpIdx = -1, moveDownIdx = -1;

            for (int i = 0; i < payload.effects.Length; i++)
            {
                DrawParticleEntry(step, payload.effects, i, out bool rem, out bool up, out bool dn);
                if (rem) removeIdx   = i;
                if (up)  moveUpIdx   = i;
                if (dn)  moveDownIdx = i;
            }

            if (removeIdx >= 0)
            {
                if (_previewingParticleIdx == removeIdx) StopParticlePreview();
                var list = new List<ParticleEffectEntry>(payload.effects);
                list.RemoveAt(removeIdx);
                payload.effects = list.ToArray();
                if (removeIdx < _particleFoldouts.Count) _particleFoldouts.RemoveAt(removeIdx);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
            else if (moveUpIdx > 0)
            {
                var arr = payload.effects;
                (arr[moveUpIdx], arr[moveUpIdx - 1]) = (arr[moveUpIdx - 1], arr[moveUpIdx]);
                if (moveUpIdx < _particleFoldouts.Count && moveUpIdx - 1 < _particleFoldouts.Count)
                    (_particleFoldouts[moveUpIdx], _particleFoldouts[moveUpIdx - 1]) =
                    (_particleFoldouts[moveUpIdx - 1], _particleFoldouts[moveUpIdx]);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
            else if (moveDownIdx >= 0 && moveDownIdx < payload.effects.Length - 1)
            {
                var arr = payload.effects;
                (arr[moveDownIdx], arr[moveDownIdx + 1]) = (arr[moveDownIdx + 1], arr[moveDownIdx]);
                if (moveDownIdx < _particleFoldouts.Count && moveDownIdx + 1 < _particleFoldouts.Count)
                    (_particleFoldouts[moveDownIdx], _particleFoldouts[moveDownIdx + 1]) =
                    (_particleFoldouts[moveDownIdx + 1], _particleFoldouts[moveDownIdx]);
                _dirtyStepIds.Add(step.id);
                Repaint();
            }
        }

        private void DrawParticleEntry(StepDefinition step, ParticleEffectEntry[] effects, int idx,
                                       out bool remove, out bool moveUp, out bool moveDown)
        {
            remove = moveUp = moveDown = false;
            var eff = effects[idx];

            // ── Foldout header ────────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(idx == 0);
            if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(20))) moveUp = true;
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(idx == effects.Length - 1);
            if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(20))) moveDown = true;
            EditorGUI.EndDisabledGroup();

            string label = $"Effect {idx + 1}: {(string.IsNullOrEmpty(eff.presetId) ? "(no preset)" : eff.presetId)}";
            _particleFoldouts[idx] = EditorGUILayout.Foldout(_particleFoldouts[idx], label, true);
            if (GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(20))) remove = true;
            EditorGUILayout.EndHorizontal();

            if (!_particleFoldouts[idx]) return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();

            // Preset
            eff.presetId = DrawStringPopup("Preset", eff.presetId, _particlePresets);

            // Target Part IDs
            EditorGUILayout.LabelField("Target Part IDs", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            string[] stepPartIds = step.requiredPartIds ?? Array.Empty<string>();
            if (stepPartIds.Length == 0 && _pkg?.parts != null)
                stepPartIds = System.Array.ConvertAll(_pkg.parts, p => p.id);

            var currentTargets = new HashSet<string>(eff.targetPartIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            bool targetsDirty  = false;
            foreach (string pid in stepPartIds)
            {
                bool was = currentTargets.Contains(pid);
                bool now2 = EditorGUILayout.Toggle(pid, was);
                if (now2 != was) { if (now2) currentTargets.Add(pid); else currentTargets.Remove(pid); targetsDirty = true; }
            }
            if (targetsDirty)
            {
                eff.targetPartIds = currentTargets.ToArray();
                effects[idx] = eff;
                _dirtyStepIds.Add(step.id);
            }
            EditorGUI.indentLevel--;

            // Trigger — show human-readable labels, store raw key
            {
                int trigIdx    = Mathf.Max(0, Array.IndexOf(_particleTriggers, eff.trigger));
                int newTrigIdx = EditorGUILayout.Popup("Trigger", trigIdx, _particleTriggerLabels);
                if (newTrigIdx != trigIdx) eff.trigger = _particleTriggers[newTrigIdx];
            }

            if (string.Equals(eff.trigger, "afterDelay", StringComparison.Ordinal))
                eff.delaySeconds = FloatFieldClip("Delay (s)", eff.delaySeconds);

            // Duration / Loop / Scale
            eff.durationSeconds = FloatFieldClip("Duration (s)  [0=indefinite]", eff.durationSeconds);
            eff.loop            = EditorGUILayout.Toggle("Loop", eff.loop);
            eff.scale           = FloatFieldClip("Scale", eff.scale <= 0f ? 1f : eff.scale);

            if (EditorGUI.EndChangeCheck()) { effects[idx] = eff; _dirtyStepIds.Add(step.id); }

            // ── Preview toggle ────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            bool isPreviewing = (_previewingParticleIdx == idx
                                 && _previewParticleGO != null
                                 && _previewingParticleStepId == step.id);

            EditorGUILayout.BeginHorizontal();
            var toggleStyle = new GUIStyle(EditorStyles.miniButton);
            if (isPreviewing) toggleStyle.normal.textColor = new Color(1f, 0.65f, 0.1f); // orange = fire!
            string toggleLabel = isPreviewing ? "■  Stop Preview" : "▶  Preview";
            if (GUILayout.Button(toggleLabel, toggleStyle, GUILayout.Width(110)))
            {
                if (isPreviewing) StopParticlePreview();
                else              StartParticlePreview(step, idx);
            }
            if (isPreviewing)
                EditorGUILayout.LabelField("  Particle active in scene", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }
    }
}
