using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
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

        // Fabrication grouping: temp parent for ungrouped subassembly members
        private GameObject _fabricationGroupRoot;
        private readonly List<FabricationGroupEntry> _fabricationGroupEntries = new List<FabricationGroupEntry>();

        // Deferred preview spawning
        private Action _deferredPreviewSpawn;
        private float _previewDelayRemaining;

        private struct ActiveCue
        {
            public IAnimationCuePlayer Player;
            public AnimationCueContext Context;
        }

        private struct DelayedCue
        {
            public AnimationCueEntry Entry;
            public AnimationCueContext Context;
            public float RemainingDelay;
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
            };
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

            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
            {
                deferredPreviewSpawn?.Invoke();
                return;
            }

            // Gather cues from every host (part / subassembly / aggregate)
            // that is visible at this step, plus legacy step.animationCues
            // as a fallback for unmigrated JSON. Each gathered entry carries
            // its pre-resolved host so the coordinator doesn't re-walk the
            // fields on every fire.
            var gathered = new List<GatheredCue>();
            GatherHostCues(package, step, gathered);

            var legacyPayload = step.animationCues;
            var legacyCues = legacyPayload?.cues;
            if (legacyCues != null)
            {
                for (int i = 0; i < legacyCues.Length; i++)
                    gathered.Add(new GatheredCue { Entry = legacyCues[i], HostKind = HostKind.Step });
            }

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
            // for now (migration will move it onto the step directly).
            float previewDelay = legacyPayload != null ? legacyPayload.previewDelaySeconds : 0f;
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
                    _                    => ResolveContext(entry, step),
                };
                if (context.Targets == null || context.Targets.Count == 0)
                {
                    OseLog.VerboseInfo($"[AnimCue] No targets resolved for cue '{entry.type}' on step '{stepId}', skipping.");
                    continue;
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
                                         && !string.Equals(entry.trigger, "afterPartsShown",    StringComparison.OrdinalIgnoreCase);
                bool isAfterPartsShown = string.Equals(entry.trigger, "afterPartsShown", StringComparison.OrdinalIgnoreCase);
                // Deferred-trigger cues (onStepComplete, onFirstInteraction, onTaskComplete) are
                // fired by their dedicated public methods and must NOT start on step activation.
                bool isDeferredTrigger = string.Equals(entry.trigger, "onStepComplete",      StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(entry.trigger, "onFirstInteraction",  StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(entry.trigger, "onTaskComplete",       StringComparison.OrdinalIgnoreCase);

                if (isDeferredTrigger)
                {
                    // Nothing to do now — the orchestrator calls OnStepCompleted /
                    // OnFirstInteraction / OnTaskCompleted at the right moment.
                }
                else if (isDelayed)
                {
                    Debug.Log($"[CueRuntime.Schedule] type={entry.type} hostId={g.HostId} → delayed by {effectiveDelay:0.00}s (panel={g.PanelDelay:0.00}s + authored={(string.Equals(entry.trigger, "afterDelay", StringComparison.OrdinalIgnoreCase) ? entry.delaySeconds : 0f):0.00}s)");
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
                    Debug.Log($"[CueRuntime.Schedule] type={entry.type} hostId={g.HostId} → fire immediately (effectiveDelay={effectiveDelay:0.00}s)");
                    var player = factory();
                    player.Start(context);
                    _activeCues.Add(new ActiveCue { Player = player, Context = context });
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

            // Tick active cues
            for (int i = _activeCues.Count - 1; i >= 0; i--)
            {
                var active = _activeCues[i];
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
                    }
                }
            }
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
        /// Common helper: instantiate and start all cues that match <paramref name="trigger"/>
        /// (and optionally filter by <paramref name="matchId"/> when non-null).
        /// Skips cues whose targets cannot be resolved.
        /// </summary>
        private void FireTriggerCues(string stepId, string trigger, string matchId = null)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step)) return;

            // Merge host-hosted cues with legacy step-owned cues so deferred
            // triggers (onStepComplete / onFirstInteraction / onTaskComplete)
            // work for relocated cues the same way they work for legacy ones.
            var gathered = new List<GatheredCue>();
            GatherHostCues(package, step, gathered);
            var legacyCues = step.animationCues?.cues;
            if (legacyCues != null)
            {
                for (int i = 0; i < legacyCues.Length; i++)
                    gathered.Add(new GatheredCue { Entry = legacyCues[i], HostKind = HostKind.Step });
            }

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
                    if (g.HostKind == HostKind.Part)
                        idMatch = string.Equals(g.HostId, matchId, StringComparison.Ordinal);
                    else if (g.HostKind == HostKind.Subassembly)
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

                    Debug.Log($"[CueRuntime.Panel] bucket={kv.Key} row={row} type={cue.type} seqAfterPrev={cue.sequenceAfterPrevious} panelOrder={cue.panelOrder} duration={prevDuration:0.00}s panelDelay={entry.PanelDelay:0.00}s");
                }
            }
        }

        private enum HostKind { Step, Part, Subassembly }

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
