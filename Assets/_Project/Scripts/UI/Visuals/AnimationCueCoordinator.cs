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

            var cuePayload = step.animationCues;
            var cues = cuePayload?.cues;
            if (cues == null || cues.Length == 0)
            {
                deferredPreviewSpawn?.Invoke();
                return;
            }

            // Set up deferred preview spawning
            float previewDelay = cuePayload.previewDelaySeconds;
            if (previewDelay > 0f && deferredPreviewSpawn != null)
            {
                _deferredPreviewSpawn = deferredPreviewSpawn;
                _previewDelayRemaining = previewDelay;
            }
            else
            {
                deferredPreviewSpawn?.Invoke();
            }

            for (int i = 0; i < cues.Length; i++)
            {
                var entry = cues[i];
                if (string.IsNullOrEmpty(entry.type))
                    continue;

                if (!_factories.TryGetValue(entry.type, out var factory))
                {
                    OseLog.VerboseInfo($"[AnimCue] Unknown cue type '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                var context = ResolveContext(entry, step);
                if (context.Targets == null || context.Targets.Count == 0)
                {
                    OseLog.VerboseInfo($"[AnimCue] No targets resolved for cue '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                bool isDelayed         = string.Equals(entry.trigger, "afterDelay", StringComparison.OrdinalIgnoreCase)
                                         && entry.delaySeconds > 0f;
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
                    _delayedCues.Add(new DelayedCue
                    {
                        Entry = entry,
                        Context = context,
                        RemainingDelay = entry.delaySeconds,
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

            var cues = step.animationCues?.cues;
            if (cues == null || cues.Length == 0) return;

            for (int i = 0; i < cues.Length; i++)
            {
                var entry = cues[i];
                if (!string.Equals(entry.trigger, trigger, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.type)) continue;

                // Optional ID filter for onTaskComplete
                if (matchId != null)
                {
                    bool idMatch = (entry.targetPartIds != null && System.Array.IndexOf(entry.targetPartIds, matchId) >= 0)
                                || string.Equals(entry.targetSubassemblyId, matchId, StringComparison.Ordinal);
                    if (!idMatch) continue;
                }

                if (!_factories.TryGetValue(entry.type, out var factory))
                {
                    OseLog.VerboseInfo($"[AnimCue] Unknown cue type '{entry.type}' on step '{stepId}', skipping.");
                    continue;
                }

                var context = ResolveContext(entry, step);
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

        private static float GetDefaultDuration(string type) => type switch
        {
            "demonstratePlacement" => 1.5f,
            "poseTransition"      => 1.0f,
            "pulse"               => 0f,
            "orientSubassembly"   => 0.6f,
            _                     => 1.0f,
        };
    }
}
