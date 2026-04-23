using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Central orchestrator for data-driven animation cues.
    /// Reads <see cref="StepDefinition.animationCues"/> on step activation,
    /// resolves target GameObjects, and drives per-frame ticking of
    /// <see cref="IAnimationCuePlayer"/> instances.
    /// </summary>
    internal sealed class AnimationCueCoordinator
    {
        private readonly IBridgeContext _ctx;
        private readonly Dictionary<string, Func<IAnimationCuePlayer>> _factories;
        private readonly List<ActiveCue> _activeCues = new List<ActiveCue>();
        private readonly List<DelayedCue> _delayedCues = new List<DelayedCue>();
        private readonly List<GameObject> _ghostObjects = new List<GameObject>();

        // Phase 2: progress-range onDuringAction cues. Populated by
        // FireDuringActionStart, advanced by OnToolActionProgress,
        // cleared by StopDuringAction / Cleanup.
        private readonly List<RangedCue> _rangedCues = new List<RangedCue>();
        private float _lastToolProgress;

        // Tracks the currently-active step so ToolActionProgressTick events
        // can be routed without the controller needing to know the stepId.
        private string _currentStepId;

        // Fabrication grouping: temp parent for ungrouped subassembly members
        private GameObject _fabricationGroupRoot;
        private readonly List<FabricationGroupEntry> _fabricationGroupEntries = new List<FabricationGroupEntry>();

        // Deferred preview spawning
        private Action _deferredPreviewSpawn;
        private float _previewDelayRemaining;

        // ── awaitCues state ──────────────────────────────────────────────────
        //
        // When a TaskOrderEntry is flagged awaitCues=true, this coordinator:
        //   1) Deferrs its host's onActivate cues at step activation — they
        //      don't fire immediately. Instead they're kept in
        //      _pendingAwaitCues keyed by the entry's (kind, id).
        //   2) Subscribes to the active TaskCursor's TaskSpanOpened event.
        //   3) When a span opens containing an awaitCues entry, starts that
        //      entry's deferred cues, tagging each active cue with
        //      AwaitingEntry = (kind, id). Tracks how many non-loop cues
        //      are still running per entry in _awaitCueCount.
        //   4) Tick's cue-removal path decrements _awaitCueCount when a
        //      non-loop AwaitingEntry cue finishes. When the count for an
        //      entry hits 0, the coordinator calls NotifyTaskCompleted so
        //      the cursor advances past the entry.
        //
        // Loop cues (entry.loop == true) are fire-and-forget — they never
        // block advancement. Covers the "ambient particle that keeps
        // playing through the whole step" case.
        private readonly Dictionary<(string kind, string id), List<PendingAwaitCue>> _pendingAwaitCues
            = new Dictionary<(string kind, string id), List<PendingAwaitCue>>();
        private readonly Dictionary<(string kind, string id), int> _awaitCueCount
            = new Dictionary<(string kind, string id), int>();
        private TaskCursor _awaitAttachedCursor;
        private StepDefinition _awaitStep;

        private struct PendingAwaitCue
        {
            public AnimationCueEntry Entry;
            public AnimationCueContext Context;
            public Func<IAnimationCuePlayer> Factory;
        }

        private struct ActiveCue
        {
            public IAnimationCuePlayer Player;
            public AnimationCueContext Context;
            /// <summary>
            /// When this cue corresponds to a TaskOrderEntry that has
            /// <c>awaitCues=true</c>, these are the (kind, id) of that
            /// entry — so when Tick removes the cue on completion, the
            /// coordinator decrements the entry's await-count and,
            /// when the count hits 0, tells the cursor to advance.
            /// Null when the cue isn't being awaited by any entry.
            /// </summary>
            public (string kind, string id)? AwaitingEntry;
        }

        private struct DelayedCue
        {
            public AnimationCueEntry Entry;
            public AnimationCueContext Context;
            public float RemainingDelay;
        }

        /// <summary>
        /// A progress-ranged onDuringAction cue — gathered at action start,
        /// promoted to active (via Player) when tool-action progress crosses
        /// <see cref="StartProgress"/>, ticked via <c>TickProgress</c> until
        /// progress crosses <see cref="EndProgress"/>.
        /// </summary>
        private struct RangedCue
        {
            public AnimationCueEntry Entry;
            public AnimationCueContext Context;
            public Func<IAnimationCuePlayer> Factory;
            public IAnimationCuePlayer Player;
            public float StartProgress;
            public float EndProgress;
            public bool IsStarted;
            public bool IsStopped;
            public bool IsBurst;
        }

        private struct FabricationGroupEntry
        {
            public Transform Child;
            public Transform OriginalParent;
            public Vector3 OriginalLocalPosition;
            public Quaternion OriginalLocalRotation;
            public Vector3 OriginalLocalScale;
        }

        public AnimationCueCoordinator(IBridgeContext ctx)
        {
            _ctx = ctx;
            _factories = new Dictionary<string, Func<IAnimationCuePlayer>>(StringComparer.OrdinalIgnoreCase)
            {
                { "demonstratePlacement", () => new DemonstratePlacementPlayer() },
                { "poseTransition",       () => new PoseTransitionPlayer() },
                { "pulse",                () => new PulsePlayer() },
                { "orientSubassembly",    () => new OrientSubassemblyPlayer() },
                { "shake",                () => new ShakePlayer() },
                { "particle",             () => new ParticlePlayer() },
                { "transform",            () => new PoseTransitionPlayer() },
                // Phase 2 effect cues
                { "emissionPulse",        () => new EmissionPulsePlayer() },
                { "colorTween",           () => new ColorTweenPlayer() },
                { "materialFade",         () => new MaterialFadePlayer() },
                { "clickPop",             () => new ClickPopPlayer() },
                { "poseWobble",           () => new PoseWobblePlayer() },
                { "toolVibration",        () => new ToolVibrationPlayer() },
                { "lineBetweenAnchors",   () => new LineBetweenAnchorsPlayer() },
                { "moveBetweenAnchors",   () => new MoveBetweenAnchorsPlayer() },
                { "drawSpline",           () => new DrawSplinePlayer() },
                { "measureLine",          () => new MeasureLinePlayer() },
                { "screwSpin",            () => new ScrewSpinPlayer() },
            };

            // Subscribe to tool-action progress ticks emitted by
            // ToolActionPreviewController so progress-ranged onDuringAction
            // cues advance in lockstep with the action.
            OSE.Core.RuntimeEventBus.Subscribe<OSE.Core.ToolActionProgressTick>(OnToolProgressEvent);

            // Register onDuringAction cues when the Action phase opens —
            // guaranteed to run BEFORE the first progress tick of the
            // same action. Replaces the off-by-one registration that
            // used to fire from UseStepHandler's onComplete path.
            OSE.Core.RuntimeEventBus.Subscribe<OSE.Core.ToolActionStarted>(OnToolActionStartedEvent);
        }

        private void OnToolProgressEvent(OSE.Core.ToolActionProgressTick evt)
        {
            if (string.IsNullOrEmpty(_currentStepId)) return;
            OnToolActionProgress(_currentStepId, evt.Progress);
        }

        private void OnToolActionStartedEvent(OSE.Core.ToolActionStarted evt)
        {
            if (string.IsNullOrEmpty(_currentStepId)) return;
            FireDuringActionStart(_currentStepId);
        }

        /// <summary>
        /// Activate cues for a step. When <paramref name="deferredPreviewSpawn"/> is
        /// non-null the coordinator will invoke it after <c>previewDelaySeconds</c>
        /// elapses; this lets the caller defer ghost/preview spawning until
        /// orientation cues have finished.
        /// </summary>
        public void OnStepActivated(string stepId, Action deferredPreviewSpawn = null)
        {
            Cleanup();
            _currentStepId = stepId;

            var package = _ctx.Spawner?.CurrentPackage;

            // Play-mode step-pose re-apply. Edit-mode preview drives this via
            // EditModePreviewDriver; play mode had no equivalent, so any
            // cue-animated state from the previous step lingered on some
            // children (those the cue touched) while others still sat at
            // their original spawn pose — the "mixed pre/post pose" symptom
            // on step 56 after step 55's group transition. Running the
            // spawner's step-aware positioning here guarantees every part
            // is at its authored canonical step pose before the new step's
            // cues start. The baker that pre-computes hold-at-end cue
            // results into the next step's poseTable makes this the
            // right source of truth.
            if (Application.isPlaying && package != null && package.TryGetStep(stepId, out var s))
                _ctx.Spawner?.ApplyStepAwarePositions(s.sequenceIndex, package);

            if (package == null || !package.TryGetStep(stepId, out var step))
            {
                deferredPreviewSpawn?.Invoke();
                return;
            }

            // Diagnostic: dump taskOrder state as the runtime sees it, so we
            // can tell from logs alone whether awaitCues/isOptional survived
            // load + normalization.
            if (step.taskOrder != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[CueRuntime.TaskOrderState] step='{step.id}' entries=");
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var te = step.taskOrder[ti];
                    if (te == null) continue;
                    if (ti > 0) sb.Append(", ");
                    sb.Append($"[{ti}]({te.kind}:{te.id} opt={te.isOptional} awaitCues={te.awaitCues})");
                }
                OseLog.Info(sb.ToString());
            }

            // Host-owned cues are the authoritative source. Step-level cues
            // are migrated onto their target host at load time by
            // MachinePackageNormalizer.MigrateStepAnimationCuesToHosts, so
            // the runtime only reads from parts and subassemblies here.
            // Any step-level leftovers are flagged as errors by the
            // normalizer's validator and intentionally do not fire.
            var gathered = new List<GatheredCue>();
            GatherHostCues(package, step, gathered);

            // Compute per-cue panel delays so the ∥ / ⇣ toggles authored in
            // TTAW are honoured at runtime — same scheduling the editor's
            // ▶▶ panel-play uses. Group by (host, trigger) to form panels,
            // sort by panelOrder, then walk the sequenceAfterPrevious chain
            // to compute an accumulated start-offset per cue.
            ApplyPanelTimingDelays(gathered);

            if (gathered.Count == 0)
            {
                deferredPreviewSpawn?.Invoke();
                return;
            }

            // previewDelaySeconds still lives on the step payload wrapper
            // (migration has not moved it onto the step directly yet). The
            // cues array itself is empty after host migration — only this
            // timing knob still lives here.
            float previewDelay = step.animationCues != null ? step.animationCues.previewDelaySeconds : 0f;
            if (previewDelay > 0f && deferredPreviewSpawn != null)
            {
                _deferredPreviewSpawn = deferredPreviewSpawn;
                _previewDelayRemaining = previewDelay;
            }
            else
            {
                deferredPreviewSpawn?.Invoke();
            }

            for (int i = 0; i < gathered.Count; i++)
            {
                var g = gathered[i];
                var entry = g.Entry;
                if (entry == null || string.IsNullOrEmpty(entry.type))
                    continue;

                if (!_factories.TryGetValue(entry.type, out var factory))
                {
                    OseLog.VerboseInfo($"[AnimCue] Unknown cue type '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                AnimationCueContext context = g.HostKind switch
                {
                    HostKind.Part        => ResolveHostedPartContext(g.HostId, entry, step),
                    HostKind.Subassembly => ResolveHostedSubassemblyContext(g.HostId, entry, step),
                    HostKind.Tool        => ResolveHostedToolContext(g.HostId, entry, step),
                    _                    => ResolveContext(entry, step),
                };
                if (context.Targets == null || context.Targets.Count == 0)
                {
                    OseLog.VerboseInfo($"[AnimCue] No targets resolved for cue '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                // awaitCues deferral — FIRST priority. If this cue's host
                // matches a taskOrder entry flagged awaitCues=true, the
                // author has explicitly said "this cue gates its span."
                // That intent overrides every other timing path (panel
                // delay, afterDelay, afterPartsShown) — we always stash the
                // cue in _pendingAwaitCues and wait for the span-open
                // handler to fire it when the cursor lands on that entry.
                //
                // Only onActivate-style triggers qualify. Triggers like
                // onStepComplete / onTaskComplete / onDuringAction fire via
                // dedicated public methods and must not be captured here
                // (they'd block forever since no span-open would fire
                // their host's cue).
                bool triggerIsActivationLike =
                    string.IsNullOrEmpty(entry.trigger) ||
                    string.Equals(entry.trigger, "onActivate",      StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.trigger, "afterDelay",      StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.trigger, "afterPartsShown", StringComparison.OrdinalIgnoreCase);
                if (triggerIsActivationLike)
                {
                    var awaitingEntry = TryResolveAwaitingEntry(step, entry, g.HostKind, g.HostId);
                    if (awaitingEntry.HasValue)
                    {
                        if (!_pendingAwaitCues.TryGetValue(awaitingEntry.Value, out var list))
                            _pendingAwaitCues[awaitingEntry.Value] = list = new List<PendingAwaitCue>();
                        list.Add(new PendingAwaitCue
                        {
                            Entry   = entry,
                            Context = context,
                            Factory = factory,
                        });
                        OseLog.Info($"[CueRuntime.AwaitDefer] type={entry.type} hostId={g.HostId} → deferred for task entry ({awaitingEntry.Value.kind}, {awaitingEntry.Value.id})");
                        continue;
                    }
                    else
                    {
                        OseLog.Info($"[CueRuntime.AwaitNoMatch] type={entry.type} hostKind={g.HostKind} hostId={g.HostId} — no awaitCues taskOrder entry matched; will fire normally");
                    }
                }

                // Effective delay = authored afterDelay seconds + the
                // panel-chain offset (parallel rows share an offset,
                // sequenced rows wait for the previous row to finish).
                float effectiveDelay = g.PanelDelay
                    + (string.Equals(entry.trigger, "afterDelay", StringComparison.OrdinalIgnoreCase)
                        ? entry.delaySeconds : 0f);
                bool isDelayed         = effectiveDelay > 0f
                                         && !string.Equals(entry.trigger, "onStepComplete",     StringComparison.OrdinalIgnoreCase)
                                         && !string.Equals(entry.trigger, "onFirstInteraction", StringComparison.OrdinalIgnoreCase)
                                         && !string.Equals(entry.trigger, "onTaskComplete",     StringComparison.OrdinalIgnoreCase)
                                         && !string.Equals(entry.trigger, "onDuringAction",     StringComparison.OrdinalIgnoreCase)
                                         && !string.Equals(entry.trigger, "afterPartsShown",    StringComparison.OrdinalIgnoreCase);
                bool isAfterPartsShown = string.Equals(entry.trigger, "afterPartsShown", StringComparison.OrdinalIgnoreCase);
                // Deferred-trigger cues (onStepComplete, onFirstInteraction, onTaskComplete,
                // onDuringAction) are fired by their dedicated public methods and must NOT
                // start on step activation.
                bool isDeferredTrigger = string.Equals(entry.trigger, "onStepComplete",      StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(entry.trigger, "onFirstInteraction",  StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(entry.trigger, "onTaskComplete",       StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(entry.trigger, "onDuringAction",       StringComparison.OrdinalIgnoreCase);

                if (isDeferredTrigger)
                {
                    // Nothing to do now — the orchestrator calls OnStepCompleted /
                    // OnFirstInteraction / OnTaskCompleted at the right moment.
                }
                else if (isDelayed)
                {
                    OseLog.Info($"[CueRuntime.Schedule] type={entry.type} hostId={g.HostId} → delayed by {effectiveDelay:0.00}s (panel={g.PanelDelay:0.00}s + authored={(string.Equals(entry.trigger, "afterDelay", StringComparison.OrdinalIgnoreCase) ? entry.delaySeconds : 0f):0.00}s)");
                    _delayedCues.Add(new DelayedCue
                    {
                        Entry = entry,
                        Context = context,
                        RemainingDelay = effectiveDelay,
                    });
                }
                else if (isAfterPartsShown)
                {
                    // Queue to fire when the deferred preview spawn callback fires.
                    // We wrap the player start into a local closure captured per-entry.
                    var capturedContext = context;
                    var capturedFactory = factory;
                    var previousDeferred = _deferredPreviewSpawn;
                    _deferredPreviewSpawn = () =>
                    {
                        previousDeferred?.Invoke();
                        var pl = capturedFactory();
                        pl.Start(capturedContext);
                        _activeCues.Add(new ActiveCue { Player = pl, Context = capturedContext });
                    };
                    // Ensure the delay countdown is running (use 0 if not already set by previewDelaySeconds)
                    if (_previewDelayRemaining <= 0f) _previewDelayRemaining = 0.001f;
                }
                else
                {
                    OseLog.Info($"[CueRuntime.Schedule] type={entry.type} hostId={g.HostId} → fire immediately (effectiveDelay={effectiveDelay:0.00}s)");
                    var player = factory();
                    player.Start(context);
                    _activeCues.Add(new ActiveCue { Player = player, Context = context });
                }
            }

            // Attach to the active cursor so span-opens can start deferred
            // awaitCues cues at the right moment.
            AttachAwaitCursor(step);
        }

        /// <summary>
        /// Looks up the taskOrder entry whose (kind, host-id) matches this
        /// cue's host AND has <c>awaitCues=true</c>. Returns null when no
        /// such entry exists — the cue will fire normally. Part-entry id
        /// can be either a partId or a subassemblyId (group task); both
        /// resolve via HostKind matching.
        ///
        /// <para><b>Required entries are deliberately excluded.</b>
        /// awaitCues is a visual-pacing affordance for optional / NO-TASK
        /// entries — the cursor needs a way to "wait on the animation"
        /// for something the user doesn't otherwise interact with.
        /// Required entries drive their own advancement through user
        /// completion (placement, tool action), and their cue timing
        /// lives in the per-cue timing panels (First to Show / Show
        /// During / During Tool Action / …). Applying awaitCues on a
        /// required entry would either auto-advance past the required
        /// task (wrong) or create an ambiguity between cue-done and
        /// user-done completion sources. Ignore the flag in that case.
        /// </para>
        /// </summary>
        private static (string kind, string id)? TryResolveAwaitingEntry(
            StepDefinition step, AnimationCueEntry cue, HostKind cueHostKind, string cueHostId)
        {
            if (step?.taskOrder == null || step.taskOrder.Length == 0) return null;

            // Dump the taskOrder state as TryResolveAwaitingEntry sees it, so
            // one log line proves whether awaitCues survived load/normalize.
            // This duplicates some of the TaskOrderState dump but fires at
            // the exact moment we're deciding to defer, so timing is precise.
            var trDesc = new System.Text.StringBuilder();
            for (int li = 0; li < step.taskOrder.Length; li++)
            {
                var le = step.taskOrder[li];
                if (le == null) continue;
                if (trDesc.Length > 0) trDesc.Append(", ");
                trDesc.Append($"({le.kind}:{le.id} awaitCues={le.awaitCues} opt={le.isOptional})");
            }
            OseLog.Info($"[CueRuntime.TryResolve] cueHost=({cueHostKind}:{cueHostId}) taskOrder=[{trDesc}]");

            for (int i = 0; i < step.taskOrder.Length; i++)
            {
                var e = step.taskOrder[i];
                if (e == null || !e.awaitCues || string.IsNullOrEmpty(e.id)) continue;
                // NOTE: we intentionally do NOT gate on e.isOptional here.
                // Required vs optional differentiation is handled by
                // OnAwaitSpanOpened (autoAdvanceOnCueDone = e.isOptional).
                // Gating here would cause required+awaitCues entries — and
                // entries where MarkVisualOnlyTaskOrderEntriesOptional
                // failed to run for any reason — to bypass await-defer and
                // fire their cues in parallel with adjacent awaitCues
                // entries, breaking the sequential-NO-TASK invariant.

                // Part-kind entry: host is a Part OR Subassembly with that id.
                if (string.Equals(e.kind, "part", StringComparison.Ordinal))
                {
                    string partId = TaskInstanceId.ToPartId(e.id);
                    if ((cueHostKind == HostKind.Part         && string.Equals(cueHostId, partId, StringComparison.Ordinal))
                     || (cueHostKind == HostKind.Subassembly  && string.Equals(cueHostId, e.id,   StringComparison.Ordinal)))
                    {
                        return (e.kind, e.id);
                    }
                }
                // toolAction-kind entry: host is a Tool whose id matches the
                // action's toolId in step.requiredToolActions.
                else if (string.Equals(e.kind, "toolAction", StringComparison.Ordinal)
                         && cueHostKind == HostKind.Tool
                         && step.requiredToolActions != null)
                {
                    for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                    {
                        var a = step.requiredToolActions[ai];
                        if (a == null) continue;
                        if (!string.Equals(a.id, e.id, StringComparison.Ordinal)) continue;
                        if (string.Equals(cueHostId, a.toolId, StringComparison.Ordinal))
                            return (e.kind, e.id);
                    }
                }
            }
            return null;
        }

        private void AttachAwaitCursor(StepDefinition step)
        {
            _awaitStep = step;
            var cursor = TryGetCursorForStep();
            if (cursor == null) return;
            if (ReferenceEquals(_awaitAttachedCursor, cursor)) return;

            DetachAwaitCursor();
            _awaitAttachedCursor = cursor;
            _awaitAttachedCursor.TaskSpanOpened += OnAwaitSpanOpened;

            // Manual replay ONLY when cursor.Start() has already fired.
            //
            // During the normal initial step-activation flow, OnStepActivated
            // (this path) runs synchronously from StepStateChanged(Active),
            // which fires BEFORE StepController.ActivateStep calls
            // cursor.Start(). The TaskCursor exposes OpenTasks for span 0
            // even pre-Start (spans are built in the constructor), so the
            // Count > 0 heuristic alone produced a double-fire bug: manual
            // call here fires cue A, list is cleared; then cursor.Start()
            // fires TaskSpanOpened naturally → OnAwaitSpanOpened runs again
            // with empty pending list → auto-notify advances the cursor →
            // span 1 opens → cue B fires → both cues now running in parallel.
            //
            // Gating on HasStarted eliminates the double-fire. For
            // late-attach cases (e.g. RebuildVisualStateForActiveStep
            // calling OnStepActivated after a step has already been
            // playing), HasStarted is true → replay fires as intended.
            if (cursor.HasStarted && cursor.OpenTasks != null && cursor.OpenTasks.Count > 0)
            {
                OnAwaitSpanOpened(new TaskSpanOpenedInfo(
                    cursor.OpenTasks, cursor.CurrentSetLabel, cursor.SpanIndex, cursor.TotalSpans));
            }
        }

        private void DetachAwaitCursor()
        {
            if (_awaitAttachedCursor != null)
                _awaitAttachedCursor.TaskSpanOpened -= OnAwaitSpanOpened;
            _awaitAttachedCursor = null;
        }

        private static TaskCursor TryGetCursorForStep()
        {
            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return null;
            return session?.AssemblyController?.StepController?.CurrentTaskCursor;
        }

        /// <summary>
        /// Fires every time the cursor opens a new span. For any span entry
        /// flagged awaitCues, starts the cues deferred at OnStepActivated for
        /// that entry, tagging each active cue with AwaitingEntry so the Tick
        /// loop can notify the cursor when the entry's cues finish. Entries
        /// with no deferred cues immediately fire NotifyTaskCompleted since
        /// there's nothing to wait on.
        /// </summary>
        private void OnAwaitSpanOpened(TaskSpanOpenedInfo info)
        {
            if (info.Entries == null) return;

            var entriesDesc = new System.Text.StringBuilder();
            for (int li = 0; li < info.Entries.Count; li++)
            {
                var le = info.Entries[li];
                if (le == null) continue;
                if (entriesDesc.Length > 0) entriesDesc.Append(", ");
                entriesDesc.Append($"({le.kind}:{le.id} awaitCues={le.awaitCues} opt={le.isOptional})");
            }
            int pendingKeys = _pendingAwaitCues.Count;
            OseLog.Info($"[CueRuntime.SpanOpen] spanIdx={info.SpanIndex}/{info.TotalSpans} entries=[{entriesDesc}] pendingKeys={pendingKeys}");

            for (int i = 0; i < info.Entries.Count; i++)
            {
                var e = info.Entries[i];
                if (e == null || !e.awaitCues || string.IsNullOrEmpty(e.id)) continue;
                var key = (e.kind ?? string.Empty, e.id);

                // Required entries: fire cues at span-open (visual effect
                // lands when the cursor visits this entry, which is the
                // whole point of awaitCues), but DO NOT auto-notify. The
                // cursor advances when the user completes the required
                // task through the normal runtime path (placement, tool
                // action, etc.). Auto-notifying here would bypass the
                // user's actual interaction and complete the task on
                // cue-end alone — wrong for Required + awaitCues.
                //
                // Only optional entries (NO TASK markers primarily) get
                // the auto-notify-on-cue-complete behaviour.
                bool autoAdvanceOnCueDone = e.isOptional;

                if (!_pendingAwaitCues.TryGetValue(key, out var list) || list.Count == 0)
                {
                    if (autoAdvanceOnCueDone)
                    {
                        _awaitAttachedCursor?.NotifyTaskCompleted(key.Item1, key.id);
                        OseLog.Info($"[CueRuntime.AwaitFire] ({key.Item1}, {key.id}) → no cues deferred; auto-notify complete.");
                    }
                    continue;
                }

                int nonLoopCount = 0;
                for (int ci = 0; ci < list.Count; ci++)
                {
                    var pac = list[ci];
                    if (!_factories.TryGetValue(pac.Entry.type ?? string.Empty, out var _)) continue;
                    var player = pac.Factory();
                    player.Start(pac.Context);
                    var active = new ActiveCue
                    {
                        Player         = player,
                        Context        = pac.Context,
                        // Only tag with AwaitingEntry when we WANT the
                        // Tick loop to decrement the count and auto-notify
                        // the cursor on cue finish. For required entries
                        // we skip the tag so Tick doesn't auto-advance.
                        AwaitingEntry  = autoAdvanceOnCueDone ? key : (ValueTuple<string, string>?)null,
                    };
                    _activeCues.Add(active);
                    if (autoAdvanceOnCueDone && !pac.Entry.loop) nonLoopCount++;
                }
                list.Clear();
                _pendingAwaitCues.Remove(key);

                if (!autoAdvanceOnCueDone)
                {
                    OseLog.Info($"[CueRuntime.AwaitFire] ({key.Item1}, {key.id}) → required entry: cues started without auto-advance; user completion drives advancement.");
                    continue;
                }

                if (nonLoopCount == 0)
                {
                    _awaitAttachedCursor?.NotifyTaskCompleted(key.Item1, key.id);
                    OseLog.Info($"[CueRuntime.AwaitFire] ({key.Item1}, {key.id}) → all cues looped; auto-notify complete.");
                }
                else
                {
                    _awaitCueCount[key] = nonLoopCount;
                    OseLog.Info($"[CueRuntime.AwaitFire] ({key.Item1}, {key.id}) → started, waiting on {nonLoopCount} non-loop cue(s).");
                }
            }
        }

        public void Update(float deltaTime)
        {
            // Tick deferred preview spawn timer
            if (_deferredPreviewSpawn != null)
            {
                _previewDelayRemaining -= deltaTime;
                if (_previewDelayRemaining <= 0f)
                {
                    var action = _deferredPreviewSpawn;
                    _deferredPreviewSpawn = null;
                    action.Invoke();
                }
            }

            // Tick delayed cues
            for (int i = _delayedCues.Count - 1; i >= 0; i--)
            {
                var delayed = _delayedCues[i];
                delayed.RemainingDelay -= deltaTime;
                if (delayed.RemainingDelay <= 0f)
                {
                    _delayedCues.RemoveAt(i);
                    if (_factories.TryGetValue(delayed.Entry.type, out var factory))
                    {
                        var player = factory();
                        player.Start(delayed.Context);
                        _activeCues.Add(new ActiveCue { Player = player, Context = delayed.Context });
                    }
                }
                else
                {
                    _delayedCues[i] = delayed;
                }
            }

            // Tick active cues. Skip players that are being driven by a
            // progress-ranged onDuringAction cue — those advance via
            // TickProgress() from OnToolActionProgress, not wall-clock
            // Tick(). Ticking both would double-advance tween players.
            for (int i = _activeCues.Count - 1; i >= 0; i--)
            {
                var active = _activeCues[i];
                if (IsProgressDriven(active.Player)) continue;

                bool stillPlaying = active.Player.Tick(deltaTime);
                if (!stillPlaying)
                {
                    if (active.Context.Entry.loop)
                    {
                        active.Player.Stop();
                        active.Player.Start(active.Context);
                    }
                    else
                    {
                        active.Player.Stop();
                        _activeCues.RemoveAt(i);
                        NotifyAwaitCueFinished(active);
                    }
                }
            }
        }

        /// <summary>
        /// Decrements the await-count for the task entry this finished cue
        /// was contributing to, if any. When the count hits zero, fires
        /// NotifyTaskCompleted on the attached cursor so the span advances.
        /// </summary>
        private void NotifyAwaitCueFinished(ActiveCue finished)
        {
            if (!finished.AwaitingEntry.HasValue) return;
            var key = finished.AwaitingEntry.Value;
            if (!_awaitCueCount.TryGetValue(key, out int remaining)) return;
            remaining--;
            if (remaining > 0)
            {
                _awaitCueCount[key] = remaining;
                return;
            }
            _awaitCueCount.Remove(key);
            _awaitAttachedCursor?.NotifyTaskCompleted(key.kind, key.id);
            OseLog.VerboseInfo($"[CueRuntime.AwaitDone] ({key.kind}, {key.id}) → all non-loop cues finished; cursor notified.");
        }

        private bool IsProgressDriven(IAnimationCuePlayer player)
        {
            if (player == null) return false;
            for (int i = 0; i < _rangedCues.Count; i++)
            {
                var rc = _rangedCues[i];
                if (rc.IsStarted && !rc.IsStopped && ReferenceEquals(rc.Player, player))
                    return true;
            }
            return false;
        }

        public void Cleanup()
        {
            // Fire deferred preview if still pending (navigated away before delay expired)
            if (_deferredPreviewSpawn != null)
            {
                _deferredPreviewSpawn = null;
                _previewDelayRemaining = 0f;
            }

            for (int i = 0; i < _activeCues.Count; i++)
                _activeCues[i].Player.Stop();
            _activeCues.Clear();
            _delayedCues.Clear();

            // awaitCues: drop every deferred cue and unsubscribe from the
            // cursor. Any entry still in _awaitCueCount had its cues
            // cancelled — we do NOT fire NotifyTaskCompleted here because
            // the step is tearing down, not completing.
            _pendingAwaitCues.Clear();
            _awaitCueCount.Clear();
            DetachAwaitCursor();
            _awaitStep = null;

            // Phase 2: drop any progress-ranged cues still pending / active.
            for (int i = 0; i < _rangedCues.Count; i++)
            {
                var rc = _rangedCues[i];
                if (rc.IsStarted && !rc.IsStopped && rc.Player != null)
                    rc.Player.Stop();
            }
            _rangedCues.Clear();
            _lastToolProgress = 0f;

            // Ungroup fabrication members back to their original parents
            UngroupFabricationMembers();

            for (int i = 0; i < _ghostObjects.Count; i++)
            {
                if (_ghostObjects[i] != null)
                    UnityEngine.Object.Destroy(_ghostObjects[i]);
            }
            _ghostObjects.Clear();
        }

        /// <summary>
        /// Fire all cues whose trigger is <c>"onStepComplete"</c>.
        /// Call this when the player completes all tasks in the step.
        /// </summary>
        public void OnStepCompleted(string stepId)
            => FireTriggerCues(stepId, "onStepComplete");

        /// <summary>
        /// Fire all cues whose trigger is <c>"onFirstInteraction"</c>.
        /// Call this on the first validated tool-target interaction within the step.
        /// </summary>
        public void OnFirstInteraction(string stepId)
            => FireTriggerCues(stepId, "onFirstInteraction");

        /// <summary>
        /// Fire all cues whose trigger is <c>"onTaskComplete"</c> and whose
        /// <c>targetPartIds</c> or authored target ID matches <paramref name="taskId"/>.
        /// </summary>
        public void OnTaskCompleted(string stepId, string taskId)
            => FireTriggerCues(stepId, "onTaskComplete", taskId);

        /// <summary>
        /// Fire <c>onDuringAction</c> cues when a tool action starts. Cues
        /// with no authored progress range fire immediately (Phase 1 legacy
        /// behaviour); cues with a range are stashed and promoted by
        /// <see cref="OnToolActionProgress"/> as progress crosses
        /// <c>startProgress</c>. Pair with <see cref="StopDuringAction"/> on
        /// action end so looping cues fade out cleanly.
        /// </summary>
        public void FireDuringActionStart(string stepId)
        {
            _lastToolProgress = 0f;

            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step)) return;

            var gathered = new List<GatheredCue>();
            GatherHostCues(package, step, gathered);

            for (int i = 0; i < gathered.Count; i++)
            {
                var g = gathered[i];
                var entry = g.Entry;
                if (entry == null) continue;
                if (!string.Equals(entry.trigger, "onDuringAction", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.type)) continue;
                if (!_factories.TryGetValue(entry.type, out var factory)) continue;

                AnimationCueContext context = g.HostKind switch
                {
                    HostKind.Part        => ResolveHostedPartContext(g.HostId, entry, step),
                    HostKind.Subassembly => ResolveHostedSubassemblyContext(g.HostId, entry, step),
                    HostKind.Tool        => ResolveHostedToolContext(g.HostId, entry, step),
                    _                    => ResolveContext(entry, step),
                };
                if (context.Targets == null || context.Targets.Count == 0) continue;

                if (HasProgressRange(entry))
                {
                    // Stash — promoted by OnToolActionProgress.
                    _rangedCues.Add(new RangedCue
                    {
                        Entry          = entry,
                        Context        = context,
                        Factory        = factory,
                        Player         = null,
                        StartProgress  = Mathf.Clamp01(entry.startProgress),
                        EndProgress    = ResolveEndProgress(entry),
                        IsStarted      = false,
                        IsStopped      = false,
                        IsBurst        = IsBurstType(entry.type),
                    });
                }
                else
                {
                    // Phase 1 path: fire immediately, stop on action end.
                    var player = factory();
                    player.Start(context);
                    _activeCues.Add(new ActiveCue { Player = player, Context = context });
                }
            }
        }

        /// <summary>
        /// Advance progress-ranged <c>onDuringAction</c> cues. Called each
        /// frame by <c>ToolActionPreviewController</c> with the action's
        /// normalised 0..1 progress. Safe to call when no ranged cues are
        /// pending — a no-op in that case.
        /// </summary>
        public void OnToolActionProgress(string stepId, float progress01)
        {
            progress01 = Mathf.Clamp01(progress01);
            _lastToolProgress = progress01;

            if (_rangedCues.Count == 0) return;

            for (int i = 0; i < _rangedCues.Count; i++)
            {
                var rc = _rangedCues[i];
                if (rc.IsStopped) continue;
                if (progress01 < rc.StartProgress) continue;

                if (!rc.IsStarted)
                {
                    rc.Player = rc.Factory();
                    rc.Player.Start(rc.Context);
                    _activeCues.Add(new ActiveCue { Player = rc.Player, Context = rc.Context });
                    rc.IsStarted = true;
                }

                float range = Mathf.Max(0.0001f, rc.EndProgress - rc.StartProgress);
                float t = Mathf.Clamp01((progress01 - rc.StartProgress) / range);

                if (!rc.IsBurst)
                    rc.Player.TickProgress(t);

                if (progress01 >= rc.EndProgress)
                {
                    rc.Player.Stop();
                    for (int a = _activeCues.Count - 1; a >= 0; a--)
                        if (ReferenceEquals(_activeCues[a].Player, rc.Player)) { _activeCues.RemoveAt(a); break; }
                    rc.IsStopped = true;
                }

                _rangedCues[i] = rc;
            }
        }

        /// <summary>
        /// Gracefully stops any active cue with trigger
        /// <c>"onDuringAction"</c> (Phase 1 legacy path) and any pending or
        /// active progress-ranged cue (Phase 2). Safe to call when no
        /// during-action cues are running.
        /// </summary>
        public void StopDuringAction(string stepId)
        {
            for (int i = _activeCues.Count - 1; i >= 0; i--)
            {
                var entry = _activeCues[i].Context.Entry;
                if (entry == null) continue;
                if (!string.Equals(entry.trigger, "onDuringAction", StringComparison.OrdinalIgnoreCase))
                    continue;

                _activeCues[i].Player.Stop();
                _activeCues.RemoveAt(i);
            }

            for (int i = 0; i < _rangedCues.Count; i++)
            {
                var rc = _rangedCues[i];
                if (rc.IsStarted && !rc.IsStopped && rc.Player != null)
                    rc.Player.Stop();
            }
            _rangedCues.Clear();
            _lastToolProgress = 0f;
        }

        private static bool HasProgressRange(AnimationCueEntry e)
        {
            // Both zero = unset = legacy full-range onDuringAction.
            if (e.startProgress == 0f && e.endProgress == 0f) return false;
            return true;
        }

        private static float ResolveEndProgress(AnimationCueEntry e)
        {
            if (e.endProgress <= 0f) return 1f;
            return Mathf.Clamp01(e.endProgress);
        }

        private static bool IsBurstType(string type)
        {
            // Burst types fire once at startProgress and don't receive
            // TickProgress updates. "particle" used to be here but is now
            // a tween — ParticlePlayer.TickProgress lerps the spawned
            // instance along anchorA → anchorB when both anchors resolve
            // (the weld_arc seam-travel path). Non-travel particle cues
            // get a no-op TickProgress, which is cheap.
            return string.Equals(type, "clickPop",   StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "drawSpline", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Common helper: instantiate and start all cues that match <paramref name="trigger"/>
        /// (and optionally filter by <paramref name="matchId"/> when non-null).
        /// Skips cues whose targets cannot be resolved.
        /// </summary>
        private void FireTriggerCues(string stepId, string trigger, string matchId = null)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step)) return;

            // Deferred-trigger dispatch reads only host-owned cues. Legacy
            // step-level cues are migrated onto hosts at load time
            // (MachinePackageNormalizer.MigrateStepAnimationCuesToHosts),
            // so this path does not need a legacy fallback.
            var gathered = new List<GatheredCue>();
            GatherHostCues(package, step, gathered);

            for (int i = 0; i < gathered.Count; i++)
            {
                var g = gathered[i];
                var entry = g.Entry;
                if (entry == null) continue;
                if (!string.Equals(entry.trigger, trigger, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.type)) continue;

                // Optional ID filter for onTaskComplete — match against the
                // host (for hosted cues) OR the legacy authored target fields.
                if (matchId != null)
                {
                    bool idMatch;
                    if (g.HostKind == HostKind.Part || g.HostKind == HostKind.Subassembly || g.HostKind == HostKind.Tool)
                        idMatch = string.Equals(g.HostId, matchId, StringComparison.Ordinal);
                    else
                        idMatch = (entry.targetPartIds != null && System.Array.IndexOf(entry.targetPartIds, matchId) >= 0)
                               || string.Equals(entry.targetSubassemblyId, matchId, StringComparison.Ordinal);
                    if (!idMatch) continue;
                }

                if (!_factories.TryGetValue(entry.type, out var factory))
                {
                    OseLog.VerboseInfo($"[AnimCue] Unknown cue type '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                AnimationCueContext context = g.HostKind switch
                {
                    HostKind.Part        => ResolveHostedPartContext(g.HostId, entry, step),
                    HostKind.Subassembly => ResolveHostedSubassemblyContext(g.HostId, entry, step),
                    HostKind.Tool        => ResolveHostedToolContext(g.HostId, entry, step),
                    _                    => ResolveContext(entry, step),
                };
                if (context.Targets == null || context.Targets.Count == 0)
                {
                    OseLog.VerboseInfo($"[AnimCue] No targets for '{entry.type}' trigger '{trigger}' on step '{stepId}'.");
                    continue;
                }

                var player = factory();
                player.Start(context);
                _activeCues.Add(new ActiveCue { Player = player, Context = context });
            }
        }

        /// <summary>
        /// After deferred previews are spawned, transforms their positions and rotations
        /// to match the fabrication group's current orientation. Call immediately after
        /// <c>SpawnPreviewsForStep</c> in the deferred callback.
        /// </summary>
        public void TransformDeferredPreviews()
        {
            if (_fabricationGroupRoot == null) return;

            Transform groupT = _fabricationGroupRoot.transform;
            Quaternion groupRot = groupT.localRotation;
            Vector3 groupPos = groupT.localPosition;

            // Identity means no rotation was applied — nothing to transform
            if (groupRot == Quaternion.identity) return;

            var previews = _ctx.SpawnedPreviews;
            if (previews == null) return;

            for (int i = 0; i < previews.Count; i++)
            {
                if (previews[i] == null) continue;
                Transform pt = previews[i].transform;

                // Rotate the preview's position around the fabrication group center
                Vector3 offset = pt.localPosition - groupPos;
                pt.localPosition = groupPos + groupRot * offset;
                pt.localRotation = groupRot * pt.localRotation;
            }
        }

        // ── Host-owned cue gather / fire ────────────────────────────────

        /// <summary>
        /// Mirrors the editor's ▶▶ panel-play scheduling: cues that share a
        /// (host, trigger) "panel" are sorted by <c>panelOrder</c>, then
        /// each cue's <c>PanelDelay</c> is computed from the
        /// <c>sequenceAfterPrevious</c> chain. Parallel (∥) rows inherit
        /// the previous row's offset; sequenced (⇣) rows add the previous
        /// row's duration. Honours runtime-authored toggles 1:1 with the
        /// editor preview.
        /// </summary>
        private static void ApplyPanelTimingDelays(List<GatheredCue> gathered)
        {
            if (gathered == null || gathered.Count == 0) return;

            // Bucket by (HostKind, HostId, trigger). Step-scoped cues use
            // an empty HostId so they all land in the same per-trigger bucket
            // — matching how step.animationCues was authored historically.
            var groups = new Dictionary<(HostKind, string, string), List<int>>();
            for (int i = 0; i < gathered.Count; i++)
            {
                var e = gathered[i].Entry;
                if (e == null) continue;
                string trig = string.IsNullOrEmpty(e.trigger) ? "onActivate" : e.trigger;
                var key = (gathered[i].HostKind, gathered[i].HostId ?? "", trig);
                if (!groups.TryGetValue(key, out var list))
                    groups[key] = list = new List<int>();
                list.Add(i);
            }

            foreach (var kv in groups)
            {
                var indices = kv.Value;
                indices.Sort((a, b) =>
                    gathered[a].Entry.panelOrder.CompareTo(gathered[b].Entry.panelOrder));

                float runningOffset = 0f;
                float prevDuration  = 0f;
                for (int row = 0; row < indices.Count; row++)
                {
                    int gi = indices[row];
                    var cue = gathered[gi].Entry;

                    if (row > 0 && cue.sequenceAfterPrevious)
                        runningOffset += prevDuration;

                    var entry = gathered[gi];
                    entry.PanelDelay = runningOffset;
                    gathered[gi] = entry;

                    prevDuration = cue.durationSeconds > 0f
                        ? cue.durationSeconds
                        : AnimationCueDefaults.GetDefaultDuration(cue.type);

                    OseLog.VerboseInfo($"[CueRuntime.Panel] bucket={kv.Key} row={row} type={cue.type} seqAfterPrev={cue.sequenceAfterPrevious} panelOrder={cue.panelOrder} duration={prevDuration:0.00}s panelDelay={entry.PanelDelay:0.00}s");
                }
            }
        }

        private enum HostKind { Step, Part, Subassembly, Tool }

        private struct GatheredCue
        {
            public AnimationCueEntry Entry;
            public HostKind HostKind;
            public string HostId;

            /// <summary>
            /// Per-fire delay computed from the timing-panel grouping +
            /// <see cref="AnimationCueEntry.sequenceAfterPrevious"/> chain.
            /// Added on top of the cue's authored <c>delaySeconds</c>.
            /// Mirrors the editor's ▶▶ panel-play scheduling so runtime
            /// playback matches preview 1:1.
            /// </summary>
            public float PanelDelay;
        }

        /// <summary>
        /// Walks every host that is visible at <paramref name="step"/> and
        /// appends its <c>animationCues</c> entries to <paramref name="out_"/>
        /// when the entry's <see cref="AnimationCueEntry.stepIds"/> is empty
        /// (fire everywhere) or contains <c>step.id</c> (scoped match).
        /// Part hosts: every part active at this seq. Subassembly hosts:
        /// every subassembly with at least one visible member. Aggregates
        /// inherit naturally — same check, descendants' visibility bubbles
        /// up through shared partIds.
        /// </summary>
        private void GatherHostCues(MachinePackageDefinition package, StepDefinition step, List<GatheredCue> out_)
        {
            int seq = step.sequenceIndex;
            var poseTable = package.poseTable;
            var visible = new HashSet<string>(StringComparer.Ordinal);
            if (poseTable != null)
            {
                foreach (var pid in poseTable.EnumerateVisiblePartsAt(seq))
                    if (!string.IsNullOrEmpty(pid)) visible.Add(pid);
            }

            // Part-hosted cues
            if (package.parts != null)
            {
                for (int i = 0; i < package.parts.Length; i++)
                {
                    var p = package.parts[i];
                    if (p == null || string.IsNullOrEmpty(p.id) || p.animationCues == null || p.animationCues.Length == 0)
                        continue;
                    if (!visible.Contains(p.id)) continue;
                    for (int k = 0; k < p.animationCues.Length; k++)
                    {
                        var e = p.animationCues[k];
                        if (!MatchesStepScope(e, step.id)) continue;
                        out_.Add(new GatheredCue { Entry = e, HostKind = HostKind.Part, HostId = p.id });
                    }
                }
            }

            // Subassembly / aggregate-hosted cues
            var subs = package.GetSubassemblies();
            if (subs != null)
            {
                for (int i = 0; i < subs.Length; i++)
                {
                    var sub = subs[i];
                    if (sub == null || string.IsNullOrEmpty(sub.id) || sub.animationCues == null || sub.animationCues.Length == 0)
                        continue;
                    bool anyMemberVisible = false;
                    if (sub.partIds != null)
                    {
                        for (int m = 0; m < sub.partIds.Length; m++)
                        {
                            if (visible.Contains(sub.partIds[m])) { anyMemberVisible = true; break; }
                        }
                    }
                    if (!anyMemberVisible) continue;
                    for (int k = 0; k < sub.animationCues.Length; k++)
                    {
                        var e = sub.animationCues[k];
                        if (!MatchesStepScope(e, step.id)) continue;
                        out_.Add(new GatheredCue { Entry = e, HostKind = HostKind.Subassembly, HostId = sub.id });
                    }
                }
            }

            // Tool-hosted cues — gated by the step's requiredToolActions so a
            // tool's cues only fire when that tool is actually in use this
            // step. Tools don't have a "visibility" concept like parts; the
            // step's tool-action list is the authoritative signal.
            var reqActions = step.requiredToolActions;
            if (reqActions != null && reqActions.Length > 0 && package.tools != null)
            {
                for (int i = 0; i < package.tools.Length; i++)
                {
                    var tool = package.tools[i];
                    if (tool == null || string.IsNullOrEmpty(tool.id) || tool.animationCues == null || tool.animationCues.Length == 0)
                        continue;

                    bool toolInUse = false;
                    for (int a = 0; a < reqActions.Length; a++)
                    {
                        if (reqActions[a] != null && string.Equals(reqActions[a].toolId, tool.id, StringComparison.Ordinal))
                        { toolInUse = true; break; }
                    }
                    if (!toolInUse) continue;

                    for (int k = 0; k < tool.animationCues.Length; k++)
                    {
                        var e = tool.animationCues[k];
                        if (!MatchesStepScope(e, step.id)) continue;
                        out_.Add(new GatheredCue { Entry = e, HostKind = HostKind.Tool, HostId = tool.id });
                    }
                }
            }
        }

        private static bool MatchesStepScope(AnimationCueEntry entry, string stepId)
        {
            if (entry == null) return false;
            if (entry.stepIds == null || entry.stepIds.Length == 0) return true; // always-on
            for (int i = 0; i < entry.stepIds.Length; i++)
                if (string.Equals(entry.stepIds[i], stepId, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Build an <see cref="AnimationCueContext"/> where the sole target
        /// is the spawned GameObject for <paramref name="partId"/>. Pose
        /// snapshots come from the part's placement (start / step-scoped /
        /// assembled). Used by part-hosted cues.
        /// </summary>
        private AnimationCueContext ResolveHostedPartContext(string partId, AnimationCueEntry entry, StepDefinition step)
        {
            var targets = new List<GameObject>();
            var startPoses = new List<AnimationCueResolvedPose>();
            var assembledPoses = new List<AnimationCueResolvedPose>();

            GameObject go = _ctx.FindSpawnedPart(partId);
            if (go == null)
                return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);

            targets.Add(go);
            var placement = _ctx.Spawner?.FindPartPlacement(partId);
            if (placement != null)
            {
                startPoses.Add(new AnimationCueResolvedPose
                {
                    Position = new Vector3(placement.startPosition.x, placement.startPosition.y, placement.startPosition.z),
                    Rotation = new Quaternion(placement.startRotation.x, placement.startRotation.y, placement.startRotation.z, placement.startRotation.w),
                    Scale    = new Vector3(placement.startScale.x, placement.startScale.y, placement.startScale.z),
                });
                StepPoseEntry stepPose = step != null ? _ctx.Spawner?.FindPartStepPose(partId, step.id) : null;
                if (stepPose != null)
                {
                    assembledPoses.Add(new AnimationCueResolvedPose
                    {
                        Position = new Vector3(stepPose.position.x, stepPose.position.y, stepPose.position.z),
                        Rotation = new Quaternion(stepPose.rotation.x, stepPose.rotation.y, stepPose.rotation.z, stepPose.rotation.w),
                        Scale    = new Vector3(stepPose.scale.x, stepPose.scale.y, stepPose.scale.z),
                    });
                }
                else
                {
                    assembledPoses.Add(new AnimationCueResolvedPose
                    {
                        Position = new Vector3(placement.assembledPosition.x, placement.assembledPosition.y, placement.assembledPosition.z),
                        Rotation = new Quaternion(placement.assembledRotation.x, placement.assembledRotation.y, placement.assembledRotation.z, placement.assembledRotation.w),
                        Scale    = new Vector3(placement.assembledScale.x, placement.assembledScale.y, placement.assembledScale.z),
                    });
                }
            }
            else
            {
                var t = go.transform;
                var p = new AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
                startPoses.Add(p); assembledPoses.Add(p);
            }

            return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);
        }

        /// <summary>
        /// Subassembly-hosted cue target: always the persistent
        /// <c>Group_*</c> root — single target, no transient anim group,
        /// no scene-graph duplication. Players that need to rotate around
        /// the members' centroid (rotate / orientSubassembly /
        /// poseTransition) compute a counter-translation themselves so
        /// the visible pivot is the centroid even though Group_ sits at
        /// PreviewRoot origin (see OrientSubassemblyPlayer for the math).
        /// Players that translate (shake) just move Group_ — children
        /// inherit. Particle cues parent the prefab to Group_, which
        /// keeps it under one persistent parent. Particles handle their
        /// own positioning via the prefab.
        /// </summary>
        private AnimationCueContext ResolveHostedSubassemblyContext(string subId, AnimationCueEntry entry, StepDefinition step)
        {
            var targets = new List<GameObject>();
            var startPoses = new List<AnimationCueResolvedPose>();
            var assembledPoses = new List<AnimationCueResolvedPose>();

            var pkg = _ctx.Spawner?.CurrentPackage;
            if (pkg == null || !pkg.TryGetSubassembly(subId, out var sub))
                return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);

            GameObject root = _ctx.Spawner?.GetSubassemblyRoot(subId);
            if (root == null)
                return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);

            targets.Add(root);
            var t = root.transform;
            var pose = new AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
            startPoses.Add(pose);
            assembledPoses.Add(pose);

            Vector3? pivotHint = PivotCentroidResolver.ComputeBodyCentroidLocal(root.transform, pkg, step);

            return new AnimationCueContext(
                entry, targets, startPoses, assembledPoses,
                DurationOrDefault(entry),
                ghosts: null,
                pivotHintLocal: pivotHint,
                package: pkg,
                stepSeq: step != null ? step.sequenceIndex : -1);
        }

        /// <summary>
        /// Tool-hosted cue target: the active tool's cursor preview GO
        /// resolved via <see cref="ToolCursorManager.ToolPreview"/>. When
        /// no preview is live (e.g. step hasn't activated the tool yet),
        /// returns an empty context so the scheduler skips the cue.
        /// </summary>
        private AnimationCueContext ResolveHostedToolContext(string toolId, AnimationCueEntry entry, StepDefinition step)
        {
            var targets = new List<GameObject>();
            var startPoses = new List<AnimationCueResolvedPose>();
            var assembledPoses = new List<AnimationCueResolvedPose>();

            GameObject toolGo = _ctx.CursorManager?.ToolPreview;
            if (toolGo == null)
                return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);

            targets.Add(toolGo);
            var t = toolGo.transform;
            var pose = new AnimationCueResolvedPose { Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale };
            startPoses.Add(pose);
            assembledPoses.Add(pose);

            return new AnimationCueContext(entry, targets, startPoses, assembledPoses, DurationOrDefault(entry), null);
        }

        private float DurationOrDefault(AnimationCueEntry entry)
            => entry.durationSeconds > 0f ? entry.durationSeconds : GetDefaultDuration(entry.type);

        // ── Private ──────────────────────────────────────────────────────

        private AnimationCueContext ResolveContext(AnimationCueEntry entry, StepDefinition step)
        {
            var targets = new List<GameObject>();
            var startPoses = new List<AnimationCueResolvedPose>();
            var assembledPoses = new List<AnimationCueResolvedPose>();
            List<GameObject> ghosts = null;

            bool isGhostMode = string.Equals(entry.target, "ghost", StringComparison.OrdinalIgnoreCase);

            // ── Resolve subassembly target FIRST ──────────────────────────────────
            // This may create the fabrication group (_fabricationGroupRoot). When both
            // targetSubassemblyId and targetPartIds are authored on the same cue, the
            // extra parts are absorbed into the fabrication group so everything moves
            // as one unit — no separate per-part targets are registered.
            bool partsAbsorbedIntoGroup = false;
            if (!string.IsNullOrEmpty(entry.targetSubassemblyId))
            {
                GameObject subassemblyRoot = ResolveSubassemblyTarget(entry.targetSubassemblyId, step);
                if (subassemblyRoot != null)
                {
                    targets.Add(subassemblyRoot);
                    var t = subassemblyRoot.transform;
                    var pose = new AnimationCueResolvedPose
                    {
                        Position = t.localPosition,
                        Rotation = t.localRotation,
                        Scale = t.localScale,
                    };
                    startPoses.Add(pose);
                    assembledPoses.Add(pose);

                    // If a fabrication group was built and the cue also names extra parts,
                    // absorb those parts into the group so the entire set animates as one.
                    if (_fabricationGroupRoot != null && entry.targetPartIds != null && entry.targetPartIds.Length > 0)
                    {
                        AbsorbPartsIntoFabricationGroup(entry.targetPartIds);
                        partsAbsorbedIntoGroup = true;
                    }
                }
            }

            // ── Step-scoped promotion (transient animation root) ─────────────────
            // When a cue targets multiple partIds that all belong to the
            // step's scoped subassembly, animate them as one rigid unit by
            // wrapping them in a transient root at their world centroid —
            // WITHOUT touching the persistent Group_* root (that one drives
            // interactions/selection/drag/ghost and must stay at identity).
            // The transient root lives only for the duration of the step's
            // cues; Cleanup() / next OnStepActivated() restores children to
            // their original parents via UngroupFabricationMembers().
            //
            // Conditions mirror the design doc: ≥2 partIds, no explicit
            // targetSubassemblyId, not ghost mode, step has group scope,
            // every partId is a member. Single-part cues and cross-group
            // cues fall through to the per-part branch unchanged.
            if (!partsAbsorbedIntoGroup
                && !isGhostMode
                && _fabricationGroupRoot == null
                && string.IsNullOrEmpty(entry.targetSubassemblyId)
                && entry.targetPartIds != null
                && entry.targetPartIds.Length >= 2
                && step != null)
            {
                string stepSubId = !string.IsNullOrEmpty(step.requiredSubassemblyId)
                    ? step.requiredSubassemblyId
                    : step.subassemblyId;
                var pkg = _ctx.Spawner?.CurrentPackage;
                if (!string.IsNullOrEmpty(stepSubId)
                    && pkg != null
                    && pkg.TryGetSubassembly(stepSubId, out var stepSub)
                    && stepSub?.partIds != null
                    && stepSub.partIds.Length > 0)
                {
                    bool allMembers = true;
                    for (int i = 0; i < entry.targetPartIds.Length; i++)
                    {
                        string pid = entry.targetPartIds[i];
                        if (string.IsNullOrEmpty(pid)) continue;
                        bool found = false;
                        for (int k = 0; k < stepSub.partIds.Length; k++)
                        {
                            if (string.Equals(stepSub.partIds[k], pid, StringComparison.Ordinal))
                            { found = true; break; }
                        }
                        if (!found) { allMembers = false; break; }
                    }

                    if (allMembers)
                    {
                        // Use the FULL subassembly member list, not just the
                        // partIds named in the cue. The cue's partIds act as
                        // the "all parts belong to this group" trigger; the
                        // animation should move the whole group (every
                        // member, including bearings and other non-cue parts)
                        // as a rigid unit. Matches the author's mental model
                        // of "shake the carriage" = shake everything in the
                        // carriage, not just the halves named in the cue.
                        GameObject transientRoot = BuildTransientAnimGroupForParts(stepSub.partIds, stepSubId);
                        if (transientRoot != null)
                        {
                            targets.Add(transientRoot);
                            var t = transientRoot.transform;
                            var pose = new AnimationCueResolvedPose
                            {
                                Position = t.localPosition,
                                Rotation = t.localRotation,
                                Scale = t.localScale,
                            };
                            startPoses.Add(pose);
                            assembledPoses.Add(pose);
                            partsAbsorbedIntoGroup = true;
                        }
                    }
                }
            }

            // ── Resolve part targets (independent path) ───────────────────────────
            // Skipped when parts were already absorbed into the fabrication group above.
            if (!partsAbsorbedIntoGroup && entry.targetPartIds != null)
            {
                for (int i = 0; i < entry.targetPartIds.Length; i++)
                {
                    string partId = entry.targetPartIds[i];
                    GameObject go = _ctx.FindSpawnedPart(partId);
                    if (go == null) continue;

                    var placement = _ctx.Spawner?.FindPartPlacement(partId);
                    GameObject animTarget = go;

                    if (isGhostMode)
                    {
                        animTarget = CreateGhost(go);
                        if (animTarget == null) continue;
                        ghosts ??= new List<GameObject>();
                        ghosts.Add(animTarget);
                    }

                    targets.Add(animTarget);

                    if (placement != null)
                    {
                        startPoses.Add(new AnimationCueResolvedPose
                        {
                            Position = new Vector3(placement.startPosition.x, placement.startPosition.y, placement.startPosition.z),
                            Rotation = new Quaternion(placement.startRotation.x, placement.startRotation.y, placement.startRotation.z, placement.startRotation.w),
                            Scale = new Vector3(placement.startScale.x, placement.startScale.y, placement.startScale.z),
                        });

                        // Use step-scoped pose as target when available
                        StepPoseEntry stepPose = step != null
                            ? _ctx.Spawner?.FindPartStepPose(partId, step.id)
                            : null;
                        if (stepPose != null)
                        {
                            assembledPoses.Add(new AnimationCueResolvedPose
                            {
                                Position = new Vector3(stepPose.position.x, stepPose.position.y, stepPose.position.z),
                                Rotation = new Quaternion(stepPose.rotation.x, stepPose.rotation.y, stepPose.rotation.z, stepPose.rotation.w),
                                Scale = new Vector3(stepPose.scale.x, stepPose.scale.y, stepPose.scale.z),
                            });
                        }
                        else
                        {
                            assembledPoses.Add(new AnimationCueResolvedPose
                            {
                                Position = new Vector3(placement.assembledPosition.x, placement.assembledPosition.y, placement.assembledPosition.z),
                                Rotation = new Quaternion(placement.assembledRotation.x, placement.assembledRotation.y, placement.assembledRotation.z, placement.assembledRotation.w),
                                Scale = new Vector3(placement.assembledScale.x, placement.assembledScale.y, placement.assembledScale.z),
                            });
                        }
                    }
                    else
                    {
                        var t = go.transform;
                        var pose = new AnimationCueResolvedPose
                        {
                            Position = t.localPosition,
                            Rotation = t.localRotation,
                            Scale = t.localScale,
                        };
                        startPoses.Add(pose);
                        assembledPoses.Add(pose);
                    }
                }
            }

            // Resolve tool targets
            if (entry.targetToolIds != null)
            {
                for (int i = 0; i < entry.targetToolIds.Length; i++)
                {
                    GameObject toolGo = _ctx.CursorManager?.ToolPreview;
                    if (toolGo == null) continue;

                    GameObject animTarget = toolGo;
                    if (isGhostMode)
                    {
                        animTarget = CreateGhost(toolGo);
                        if (animTarget == null) continue;
                        ghosts ??= new List<GameObject>();
                        ghosts.Add(animTarget);
                    }

                    targets.Add(animTarget);
                    var t = toolGo.transform;
                    var pose = new AnimationCueResolvedPose
                    {
                        Position = t.localPosition,
                        Rotation = t.localRotation,
                        Scale = t.localScale,
                    };
                    startPoses.Add(pose);
                    assembledPoses.Add(pose);
                }
            }

            float duration = entry.durationSeconds > 0f ? entry.durationSeconds : GetDefaultDuration(entry.type);

            return new AnimationCueContext(entry, targets, startPoses, assembledPoses, duration, ghosts);
        }

        /// <summary>
        /// Computes the centroid of a subassembly's member parts from their
        /// authored <c>assembledPosition</c> values (stored in the package).
        /// Returns the centroid in the target's local frame. Null when no
        /// member has an authored position.
        /// </summary>
        private Vector3? ComputeAuthoredCentroidLocal(string subassemblyId, Transform target)
        {
            var pkg = _ctx.Spawner?.CurrentPackage;
            if (pkg == null || !pkg.TryGetSubassembly(subassemblyId, out var sub) || sub?.partIds == null)
                return null;

            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < sub.partIds.Length; i++)
            {
                var pid = sub.partIds[i];
                if (string.IsNullOrEmpty(pid)) continue;
                var pp = _ctx.Spawner?.FindPartPlacement(pid);
                if (pp == null) continue;
                // assembledPosition is in PreviewRoot-local space — convert
                // to the target's local frame so the player applies it in
                // the same coordinate system ComputeChildrenCentroidLocal
                // would produce.
                Vector3 world = target.parent != null
                    ? target.parent.TransformPoint(new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z))
                    : new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                sum += target.InverseTransformPoint(world);
                n++;
            }
            return n > 0 ? sum / n : (Vector3?)null;
        }

        /// <summary>
        /// Resolves a subassembly target. For stacking steps that have a proxy, returns the proxy root.
        /// For fabrication steps with no proxy, groups the completed member parts under a temp parent.
        /// </summary>
        private GameObject ResolveSubassemblyTarget(string subassemblyId, StepDefinition step)
        {
            // Try proxy first (stacking steps)
            if (_ctx.SubassemblyController != null &&
                _ctx.SubassemblyController.TryGetProxy(subassemblyId, out GameObject proxyRoot))
            {
                return proxyRoot;
            }

            // Prefer the persistent Group_* root created by PackagePartSpawner —
            // that's the scene-graph parent the trainee sees and grabs, so
            // animations (rotate, shake) should play on it too. The root is
            // at origin+identity so rotations pivot around origin, matching
            // how the author authored poses in TTAW.
            var groupRoot = _ctx.Spawner?.GetSubassemblyRoot(subassemblyId);
            if (groupRoot != null)
                return groupRoot;

            // Fabrication fallback: group completed member parts under a temp parent
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetSubassembly(subassemblyId, out var subassemblyDef))
                return null;

            string[] memberPartIds = subassemblyDef.partIds;
            if (memberPartIds == null || memberPartIds.Length == 0)
                return null;

            // Collect current step's required parts — these are NOT yet placed
            var currentStepPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (step.requiredPartIds != null)
            {
                for (int i = 0; i < step.requiredPartIds.Length; i++)
                    currentStepPartIds.Add(step.requiredPartIds[i]);
            }

            // Find spawned member parts that are NOT current-step parts (= completed from prior steps).
            // Also compute pivot from authored assembled positions for stability.
            var completedPartIds = new List<string>();
            var completedMembers = new List<GameObject>();
            Vector3 pivotSum = Vector3.zero;
            int pivotCount = 0;
            for (int i = 0; i < memberPartIds.Length; i++)
            {
                if (currentStepPartIds.Contains(memberPartIds[i]))
                    continue;

                GameObject go = _ctx.FindSpawnedPart(memberPartIds[i]);
                if (go != null && go.activeInHierarchy)
                {
                    completedPartIds.Add(memberPartIds[i]);
                    completedMembers.Add(go);

                    // Use authored assembled position for pivot (stable regardless of runtime state)
                    var pp = _ctx.Spawner?.FindPartPlacement(memberPartIds[i]);
                    if (pp != null)
                        pivotSum += new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                    else
                        pivotSum += go.transform.localPosition;
                    pivotCount++;
                }
            }

            if (completedMembers.Count == 0)
                return null;

            Vector3 pivot = pivotSum / pivotCount;

            // Create temporary parent at the pivot (in PreviewRoot local space)
            _fabricationGroupRoot = new GameObject($"_AnimCue_FabGroup_{subassemblyId}");
            var setup = _ctx.Setup;
            if (setup != null && setup.PreviewRoot != null)
                _fabricationGroupRoot.transform.SetParent(setup.PreviewRoot, false);
            _fabricationGroupRoot.transform.localPosition = pivot;
            _fabricationGroupRoot.transform.localRotation = Quaternion.identity;

            // Reparent completed members into the fabrication group.
            // Snap each member to its authored assembled position first so any unplaced
            // parts (still at startPosition) appear correctly assembled during animation.
            // Store original local transforms so we can restore them exactly on cleanup.
            _fabricationGroupEntries.Clear();
            for (int i = 0; i < completedMembers.Count; i++)
            {
                var ct = completedMembers[i].transform;
                _fabricationGroupEntries.Add(new FabricationGroupEntry
                {
                    Child = ct,
                    OriginalParent = ct.parent,
                    OriginalLocalPosition = ct.localPosition,
                    OriginalLocalRotation = ct.localRotation,
                    OriginalLocalScale = ct.localScale,
                });

                // Snap to authored assembled position before reparenting
                var pp = _ctx.Spawner?.FindPartPlacement(completedPartIds[i]);
                if (pp != null)
                {
                    ct.localPosition = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                    ct.localRotation = new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w);
                }

                ct.SetParent(_fabricationGroupRoot.transform, true);
            }

            OseLog.VerboseInfo($"[AnimCue] Grouped {completedMembers.Count} fabrication members for '{subassemblyId}' under temp parent.");
            return _fabricationGroupRoot;
        }

        /// <summary>
        /// Builds a transient root at the world-space centroid of the named
        /// parts and reparents them under it with <c>worldPositionStays=true</c>
        /// so each child's visible pose is preserved — only the pivot changes.
        /// Used by the step-scoped promotion path so multi-part cues pivot on
        /// the group's geometric center without mutating the persistent
        /// <c>Group_*</c> hierarchy (which drives interactions). The root is
        /// registered in <see cref="_fabricationGroupRoot"/> and
        /// <see cref="_fabricationGroupEntries"/> so the existing
        /// <see cref="UngroupFabricationMembers"/> path releases it on Cleanup.
        /// Returns null if fewer than two parts could be resolved — the caller
        /// falls through to per-part animation.
        /// </summary>
        private GameObject BuildTransientAnimGroupForParts(string[] partIds, string stepSubId)
        {
            var members = new List<Transform>(partIds.Length);
            Vector3 centroidSum = Vector3.zero;
            for (int i = 0; i < partIds.Length; i++)
            {
                string pid = partIds[i];
                if (string.IsNullOrEmpty(pid)) continue;
                GameObject go = _ctx.FindSpawnedPart(pid);
                if (go == null || !go.activeInHierarchy) continue;
                members.Add(go.transform);
                centroidSum += go.transform.position;
            }
            if (members.Count < 2) return null;

            Vector3 centroid = centroidSum / members.Count;

            _fabricationGroupRoot = new GameObject($"_AnimCue_AnimGroup_{stepSubId}");
            var setup = _ctx.Setup;
            if (setup != null && setup.PreviewRoot != null)
                _fabricationGroupRoot.transform.SetParent(setup.PreviewRoot, false);
            _fabricationGroupRoot.transform.position = centroid;
            _fabricationGroupRoot.transform.rotation = Quaternion.identity;

            _fabricationGroupEntries.Clear();
            var rootT = _fabricationGroupRoot.transform;
            for (int i = 0; i < members.Count; i++)
            {
                var ct = members[i];
                _fabricationGroupEntries.Add(new FabricationGroupEntry
                {
                    Child = ct,
                    OriginalParent = ct.parent,
                    OriginalLocalPosition = ct.localPosition,
                    OriginalLocalRotation = ct.localRotation,
                    OriginalLocalScale = ct.localScale,
                });
                // worldPositionStays:true preserves each member's live world
                // pose — no snap to assembled, no offset. Only the pivot
                // moves to the centroid.
                ct.SetParent(rootT, worldPositionStays: true);
            }

            OseLog.VerboseInfo($"[AnimCue] Built transient anim group at centroid for {members.Count} parts of '{stepSubId}'.");
            return _fabricationGroupRoot;
        }

        private void UngroupFabricationMembers()
        {
            for (int i = 0; i < _fabricationGroupEntries.Count; i++)
            {
                var entry = _fabricationGroupEntries[i];
                if (entry.Child != null)
                {
                    // Reparent then restore exact original local transform
                    // (worldPositionStays=false avoids double-transformation artifacts)
                    entry.Child.SetParent(entry.OriginalParent, false);
                    entry.Child.localPosition = entry.OriginalLocalPosition;
                    entry.Child.localRotation = entry.OriginalLocalRotation;
                    entry.Child.localScale = entry.OriginalLocalScale;
                }
            }
            _fabricationGroupEntries.Clear();

            if (_fabricationGroupRoot != null)
            {
                UnityEngine.Object.Destroy(_fabricationGroupRoot);
                _fabricationGroupRoot = null;
            }
        }

        /// <summary>
        /// Reparents extra parts named in <paramref name="partIds"/> into the active
        /// fabrication group so they animate as one unit with the subassembly.
        /// Called when a cue authors both <c>targetSubassemblyId</c> AND <c>targetPartIds</c>.
        /// Parts already in the group (subassembly members) are skipped.
        /// </summary>
        private void AbsorbPartsIntoFabricationGroup(string[] partIds)
        {
            if (_fabricationGroupRoot == null || partIds == null) return;

            Transform groupTransform = _fabricationGroupRoot.transform;
            for (int i = 0; i < partIds.Length; i++)
            {
                string partId = partIds[i];
                if (string.IsNullOrEmpty(partId)) continue;

                GameObject go = _ctx.FindSpawnedPart(partId);
                if (go == null) continue;

                Transform ct = go.transform;
                // Skip if already a child of the fabrication group (subassembly member)
                if (ct.parent == groupTransform) continue;

                // Save original transform for cleanup restoration
                _fabricationGroupEntries.Add(new FabricationGroupEntry
                {
                    Child = ct,
                    OriginalParent = ct.parent,
                    OriginalLocalPosition = ct.localPosition,
                    OriginalLocalRotation = ct.localRotation,
                    OriginalLocalScale = ct.localScale,
                });

                // Snap to authored assembled position so the part appears correctly placed
                var pp = _ctx.Spawner?.FindPartPlacement(partId);
                if (pp != null)
                {
                    ct.localPosition = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                    ct.localRotation = new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w);
                }

                ct.SetParent(groupTransform, true);
            }
        }

        private GameObject CreateGhost(GameObject source)
        {
            var ghost = UnityEngine.Object.Instantiate(source);
            ghost.name = source.name + "_AnimCueGhost";

            var setup = _ctx.Setup;
            if (setup != null && setup.PreviewRoot != null)
                ghost.transform.SetParent(setup.PreviewRoot, true);

            MaterialHelper.ApplyPreviewMaterial(ghost);
            _ghostObjects.Add(ghost);
            return ghost;
        }

        private static float GetDefaultDuration(string type)
            => AnimationCueDefaults.GetDefaultDuration(type);
    }
}
