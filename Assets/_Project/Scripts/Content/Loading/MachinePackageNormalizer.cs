using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Post-deserialization pass that inflates compact JSON conventions into
    /// the fully-populated definition objects the runtime expects.
    ///
    /// Handles:
    /// - Part templates: fills empty PartDefinition fields from the referenced template.
    /// - Step parent refs: infers assemblyId / subassemblyId from assembly and subassembly stepIds.
    /// - Tool action defaults: auto-generates missing id, defaults requiredCount to 1.
    /// - Null arrays: replaces null arrays with empty arrays so callers never need null checks.
    /// </summary>
    public static class MachinePackageNormalizer
    {
        public static void Normalize(MachinePackageDefinition package)
        {
            if (package == null) return;

            InferAggregateFlag(package);
            InflatePartTemplates(package);
            BakeStagingPoses(package);
            InferStepParentIds(package);
            NormalizeToolActions(package);
            ResolveToolActionPartIds(package);
            ResolveDirectTargetPartIds(package);
            IndexPartOwnership(package);
            DeriveSubassemblyPartIds(package);
            BakeGroupRigidBody(package);
            // Animation-cue cleanup runs BEFORE BakePoseTable so any
            // stepPoses synthesized from legacy cue toPoses are visible to
            // the resolver. Trigger normalization and step→host migration
            // are prerequisites — the cue-to-stepPose migration needs
            // canonical data to reason about.
            NormalizeAnimationCueTriggers(package);
            MigrateStepAnimationCuesToHosts(package);
            MigrateAnimationCueEndPoses(package);
            BakePoseTable(package);
            ValidateAnimationCueInvariants(package);
        }

        // Canonical trigger names for AnimationCueEntry.trigger. Every alias
        // encountered in authored JSON is rewritten to one of these at load.
        private const string TriggerOnActivate         = "onActivate";
        private const string TriggerAfterDelay         = "afterDelay";
        private const string TriggerAfterPartsShown    = "afterPartsShown";
        private const string TriggerOnStepComplete     = "onStepComplete";
        private const string TriggerOnFirstInteraction = "onFirstInteraction";
        private const string TriggerOnTaskComplete     = "onTaskComplete";

        /// <summary>
        /// Rewrites legacy / typo trigger aliases to their canonical names so
        /// cues land in the same scheduling bucket regardless of how they
        /// were authored. Runs on every cue across steps, parts, and
        /// subassemblies. Protective: prevents the "onStepActivate vs
        /// onActivate" divergence that caused step 55's double-fire.
        /// </summary>
        private static void NormalizeAnimationCueTriggers(MachinePackageDefinition package)
        {
            int rewrites = 0;

            if (package.steps != null)
                for (int i = 0; i < package.steps.Length; i++)
                {
                    var s = package.steps[i];
                    rewrites += RewriteArray(s?.animationCues?.cues);
                }

            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                    rewrites += RewriteArray(package.parts[i]?.animationCues);

            var subs = package.GetSubassemblies();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    rewrites += RewriteArray(subs[i]?.animationCues);

            if (rewrites > 0)
                Debug.Log($"[CueRuntime.Normalize] rewrote {rewrites} legacy trigger alias(es) to canonical names in '{package.packageId}'.");

            static int RewriteArray(AnimationCueEntry[] cues)
            {
                if (cues == null) return 0;
                int n = 0;
                for (int i = 0; i < cues.Length; i++)
                {
                    if (cues[i] == null) continue;
                    string canonical = Canonicalize(cues[i].trigger);
                    if (!string.Equals(canonical, cues[i].trigger, StringComparison.Ordinal))
                    {
                        cues[i].trigger = canonical;
                        n++;
                    }
                }
                return n;
            }

            static string Canonicalize(string trigger)
            {
                if (string.IsNullOrEmpty(trigger)) return TriggerOnActivate;
                // Case-insensitive match against canonicals, plus known legacy aliases.
                if (string.Equals(trigger, TriggerOnActivate,         StringComparison.OrdinalIgnoreCase)) return TriggerOnActivate;
                if (string.Equals(trigger, TriggerAfterDelay,         StringComparison.OrdinalIgnoreCase)) return TriggerAfterDelay;
                if (string.Equals(trigger, TriggerAfterPartsShown,    StringComparison.OrdinalIgnoreCase)) return TriggerAfterPartsShown;
                if (string.Equals(trigger, TriggerOnStepComplete,     StringComparison.OrdinalIgnoreCase)) return TriggerOnStepComplete;
                if (string.Equals(trigger, TriggerOnFirstInteraction, StringComparison.OrdinalIgnoreCase)) return TriggerOnFirstInteraction;
                if (string.Equals(trigger, TriggerOnTaskComplete,     StringComparison.OrdinalIgnoreCase)) return TriggerOnTaskComplete;
                // Legacy aliases — map to canonical.
                if (string.Equals(trigger, "onStepActivate",   StringComparison.OrdinalIgnoreCase)) return TriggerOnActivate;
                if (string.Equals(trigger, "onStepActivated",  StringComparison.OrdinalIgnoreCase)) return TriggerOnActivate;
                if (string.Equals(trigger, "onStepStart",      StringComparison.OrdinalIgnoreCase)) return TriggerOnActivate;
                if (string.Equals(trigger, "afterParts",       StringComparison.OrdinalIgnoreCase)) return TriggerAfterPartsShown;
                // Unknown — leave as-is and let ValidateAnimationCueInvariants flag it.
                return trigger;
            }
        }

        /// <summary>
        /// Host-owned cues (part / subassembly) are the authoritative home.
        /// Any cues still living on <c>step.animationCues.cues</c> are
        /// migrated to their target host at load time, so the runtime only
        /// ever sees host-owned cues. Legacy content keeps working without
        /// manual JSON editing; new authoring tools write directly to hosts.
        ///
        /// Migration rules:
        /// - If the entry has a non-empty <c>targetSubassemblyId</c>, move to
        ///   that subassembly's <c>animationCues</c>.
        /// - Else if the entry has exactly one <c>targetPartIds[]</c> entry,
        ///   move to that part's <c>animationCues</c>.
        /// - Else: leave on the step and let the validator flag it — the
        ///   author needs to pick a host.
        /// </summary>
        private static void MigrateStepAnimationCuesToHosts(MachinePackageDefinition package)
        {
            if (package.steps == null || package.steps.Length == 0) return;

            int moved = 0;
            int left = 0;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                var payload = step?.animationCues;
                var cues = payload?.cues;
                if (cues == null || cues.Length == 0) continue;

                var kept = new List<AnimationCueEntry>();
                for (int ci = 0; ci < cues.Length; ci++)
                {
                    var entry = cues[ci];
                    if (entry == null) continue;

                    if (!string.IsNullOrEmpty(entry.targetSubassemblyId)
                        && TryAppendToSubassembly(package, entry.targetSubassemblyId, entry))
                    { moved++; continue; }

                    if (entry.targetPartIds != null && entry.targetPartIds.Length == 1
                        && !string.IsNullOrEmpty(entry.targetPartIds[0])
                        && TryAppendToPart(package, entry.targetPartIds[0], entry))
                    { moved++; continue; }

                    kept.Add(entry);
                    left++;
                }

                step.animationCues.cues = kept.ToArray();
            }

            if (moved > 0)
                Debug.Log($"[CueRuntime.Migrate] moved {moved} step-level cue(s) onto their target host in '{package.packageId}'.");
            if (left > 0)
                Debug.LogWarning($"[CueRuntime.Migrate] {left} step-level cue(s) in '{package.packageId}' have no clear host target and remain on the step. Edit them in TTAW to assign a host.");

            static bool TryAppendToSubassembly(MachinePackageDefinition pkg, string subId, AnimationCueEntry entry)
            {
                var subs = pkg.GetSubassemblies();
                if (subs == null) return false;
                for (int i = 0; i < subs.Length; i++)
                {
                    if (subs[i] == null) continue;
                    if (!string.Equals(subs[i].id, subId, StringComparison.Ordinal)) continue;
                    if (HasEquivalentCue(subs[i].animationCues, entry))
                    {
                        Debug.LogError($"[CueRuntime.Migrate] subassembly '{subId}' already has a (type='{entry.type}', trigger='{entry.trigger}') cue. Refusing to migrate a duplicate from the step level — delete one in TTAW.");
                        return false;
                    }
                    subs[i].animationCues = Append(subs[i].animationCues, entry);
                    return true;
                }
                return false;
            }

            static bool TryAppendToPart(MachinePackageDefinition pkg, string partId, AnimationCueEntry entry)
            {
                if (pkg.parts == null) return false;
                for (int i = 0; i < pkg.parts.Length; i++)
                {
                    if (pkg.parts[i] == null) continue;
                    if (!string.Equals(pkg.parts[i].id, partId, StringComparison.Ordinal)) continue;
                    if (HasEquivalentCue(pkg.parts[i].animationCues, entry))
                    {
                        Debug.LogError($"[CueRuntime.Migrate] part '{partId}' already has a (type='{entry.type}', trigger='{entry.trigger}') cue. Refusing to migrate a duplicate from the step level — delete one in TTAW.");
                        return false;
                    }
                    pkg.parts[i].animationCues = Append(pkg.parts[i].animationCues, entry);
                    return true;
                }
                return false;
            }

            static bool HasEquivalentCue(AnimationCueEntry[] arr, AnimationCueEntry entry)
            {
                if (arr == null || entry == null) return false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    if (string.Equals(arr[i].type, entry.type, StringComparison.Ordinal)
                        && string.Equals(arr[i].trigger ?? "", entry.trigger ?? "", StringComparison.Ordinal))
                        return true;
                }
                return false;
            }

            static AnimationCueEntry[] Append(AnimationCueEntry[] arr, AnimationCueEntry entry)
            {
                if (arr == null || arr.Length == 0) return new[] { entry };
                var next = new AnimationCueEntry[arr.Length + 1];
                Array.Copy(arr, next, arr.Length);
                next[arr.Length] = entry;
                return next;
            }
        }

        /// <summary>
        /// Protective guard: after normalize + migrate, the scheduling
        /// invariants should be uniform across the package. Any remaining
        /// step-level cues (that the migrator could not re-host), unknown
        /// triggers, or same-(host, trigger) duplicates with identical type
        /// are logged as errors so content authors see the problem before
        /// Play. Does not throw — load succeeds but the console flags the
        /// issue.
        /// </summary>
        private static void ValidateAnimationCueInvariants(MachinePackageDefinition package)
        {
            // 1. No step-level cues should remain after migration.
            if (package.steps != null)
            {
                for (int i = 0; i < package.steps.Length; i++)
                {
                    var s = package.steps[i];
                    var cues = s?.animationCues?.cues;
                    if (cues != null && cues.Length > 0)
                        Debug.LogError($"[CueRuntime.Validate] step '{s.id}' still has {cues.Length} step-level cue(s) after migration. Assign a target host (part/subassembly) in TTAW.");
                }
            }

            // 2. No unknown triggers on host-owned cues.
            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                    CheckTriggers(package.parts[i]?.animationCues, $"part '{package.parts[i]?.id}'");

            var subs = package.GetSubassemblies();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    CheckTriggers(subs[i]?.animationCues, $"subassembly '{subs[i]?.id}'");

            static void CheckTriggers(AnimationCueEntry[] arr, string hostLabel)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    string t = arr[i].trigger;
                    if (string.IsNullOrEmpty(t)) continue;
                    bool canonical =
                        t == TriggerOnActivate || t == TriggerAfterDelay ||
                        t == TriggerAfterPartsShown || t == TriggerOnStepComplete ||
                        t == TriggerOnFirstInteraction || t == TriggerOnTaskComplete;
                    if (!canonical)
                        Debug.LogError($"[CueRuntime.Validate] {hostLabel} cue[{i}] has unknown trigger '{t}'. Canonical values: onActivate, afterDelay, afterPartsShown, onStepComplete, onFirstInteraction, onTaskComplete.");
                }
            }
        }

        /// <summary>
        /// Retires the obsolete per-cue pose fields (<c>fromPose</c>,
        /// <c>toPose</c>, <c>holdAtEnd</c>). Runs before <c>BakePoseTable</c>
        /// so any stepPoses we synthesize land in the table on the next
        /// resolve.
        ///
        /// Rules (per the approved plan):
        /// - <c>shake</c> / <c>pulse</c> / <c>particle</c>: these animate in
        ///   place. Clear <c>toPose</c> — never read, never needed.
        /// - <c>poseTransition</c> / <c>transform</c>:
        ///   - If the cue names exactly one <c>targetPartIds</c> (or is a
        ///     part-hosted cue in a future pass), synthesize a stepPose on
        ///     that <c>PartPreviewPlacement</c> at the cue's step using the
        ///     cue's <c>toPose</c>. The part-hosted coordinator then reads
        ///     the destination from the stepPose via PoseTable — no dual
        ///     storage.
        ///   - If the cue is subassembly-hosted with no partIds, leave
        ///     <c>toPose</c> alone. Subassemblies do not yet carry stepPoses;
        ///     the runtime still reads the obsolete field for now
        ///     (<see cref="AnimationCueCoordinator.ResolveHostedSubassemblyContext"/>
        ///     with a documented pragma).
        /// - <c>fromPose</c> and <c>holdAtEnd</c>: always cleared — both
        ///   semantics are now implicit (fromPose = live transform,
        ///   holdAtEnd = always hold, PoseTable drives next step).
        /// </summary>
        private static void MigrateAnimationCueEndPoses(MachinePackageDefinition package)
        {
            int clearedFrom = 0;
            int clearedToInPlace = 0;
            int synthesizedStepPoses = 0;
            int clearedHold = 0;
            int skippedSubassembly = 0;

            void MigrateArray(AnimationCueEntry[] arr, string hostLabel)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var e = arr[i];
                    if (e == null) continue;

#pragma warning disable CS0618
                    // fromPose: never read at runtime. Always clear.
                    if (e.fromPose != null)
                    {
                        e.fromPose = null;
                        clearedFrom++;
                    }

                    bool isInPlaceType =
                        string.Equals(e.type, "shake",    StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.type, "pulse",    StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(e.type, "particle", StringComparison.OrdinalIgnoreCase);

                    if (isInPlaceType)
                    {
                        if (e.toPose != null)
                        {
                            e.toPose = null;
                            clearedToInPlace++;
                        }
                    }
                    else if (e.toPose != null
                             && !string.IsNullOrEmpty(e.type)
                             && (string.Equals(e.type, "poseTransition", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(e.type, "transform",      StringComparison.OrdinalIgnoreCase)))
                    {
                        // For part-hosted poseTransitions with exactly one
                        // target part, convert toPose into a stepPose on
                        // that part for the cue's step. Requires a single
                        // stepId so we know where the pose anchors.
                        string targetPartId = null;
                        if (e.targetPartIds != null && e.targetPartIds.Length == 1
                            && !string.IsNullOrEmpty(e.targetPartIds[0]))
                            targetPartId = e.targetPartIds[0];

                        string anchorStepId = null;
                        if (e.stepIds != null && e.stepIds.Length == 1
                            && !string.IsNullOrEmpty(e.stepIds[0]))
                            anchorStepId = e.stepIds[0];

                        if (targetPartId != null && anchorStepId != null
                            && TrySynthesizeStepPose(package, targetPartId, anchorStepId, e.toPose))
                        {
                            e.toPose = null;
                            synthesizedStepPoses++;
                        }
                        else if (!string.IsNullOrEmpty(e.targetSubassemblyId)
                                 && (e.targetPartIds == null || e.targetPartIds.Length == 0))
                        {
                            // Subassembly-hosted root transform — keep toPose
                            // for now. The runtime reads it via
                            // AnimationCueCoordinator.ResolveHostedSubassemblyContext
                            // (documented pragma). Logged so authors can
                            // eventually migrate to subassembly stepPoses.
                            skippedSubassembly++;
                        }
                    }

                    // holdAtEnd: never read at runtime (Stop always holds).
                    if (e.holdAtEnd)
                    {
                        e.holdAtEnd = false;
                        clearedHold++;
                    }
#pragma warning restore CS0618
                }
            }

            if (package.steps != null)
                for (int i = 0; i < package.steps.Length; i++)
                    MigrateArray(package.steps[i]?.animationCues?.cues, $"step '{package.steps[i]?.id}'");

            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                    MigrateArray(package.parts[i]?.animationCues, $"part '{package.parts[i]?.id}'");

            var subs = package.GetSubassemblies();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    MigrateArray(subs[i]?.animationCues, $"subassembly '{subs[i]?.id}'");

            int total = clearedFrom + clearedToInPlace + synthesizedStepPoses + clearedHold;
            if (total > 0)
                Debug.Log($"[CueRuntime.Migrate] end-poses in '{package.packageId}': cleared {clearedFrom} fromPose, {clearedToInPlace} in-place toPose, synthesized {synthesizedStepPoses} stepPose(s), cleared {clearedHold} holdAtEnd. Kept {skippedSubassembly} subassembly-root toPose(s) (no stepPose equivalent yet).");
        }

        /// <summary>
        /// Writes an <c>AnimationPose</c> onto the target part's
        /// <see cref="PartPreviewPlacement.stepPoses"/> for the given step,
        /// unless an entry for that step already exists (author-authored
        /// data wins).
        /// </summary>
        private static bool TrySynthesizeStepPose(MachinePackageDefinition package, string partId, string stepId, AnimationPose pose)
        {
            if (package?.previewConfig?.partPlacements == null || pose == null) return false;

            PartPreviewPlacement placement = null;
            for (int i = 0; i < package.previewConfig.partPlacements.Length; i++)
            {
                var pp = package.previewConfig.partPlacements[i];
                if (pp == null) continue;
                if (string.Equals(pp.partId, partId, StringComparison.Ordinal))
                {
                    placement = pp;
                    break;
                }
            }
            if (placement == null) return false;

            if (placement.stepPoses != null)
            {
                for (int i = 0; i < placement.stepPoses.Length; i++)
                {
                    var sp = placement.stepPoses[i];
                    if (sp != null && string.Equals(sp.stepId, stepId, StringComparison.Ordinal))
                        return false; // author-authored stepPose already present
                }
            }

            var entry = new StepPoseEntry
            {
                stepId = stepId,
                position = new SceneFloat3   { x = pose.position.x, y = pose.position.y, z = pose.position.z },
                rotation = new SceneQuaternion { x = pose.rotation.x, y = pose.rotation.y, z = pose.rotation.z, w = pose.rotation.w },
                scale    = new SceneFloat3   { x = pose.scale.x,    y = pose.scale.y,    z = pose.scale.z },
            };

            if (placement.stepPoses == null || placement.stepPoses.Length == 0)
            {
                placement.stepPoses = new[] { entry };
            }
            else
            {
                var next = new StepPoseEntry[placement.stepPoses.Length + 1];
                Array.Copy(placement.stepPoses, next, placement.stepPoses.Length);
                next[placement.stepPoses.Length] = entry;
                placement.stepPoses = next;
            }
            return true;
        }

        /// <summary>
        /// Derives each non-aggregate subassembly's <c>partIds</c> list from
        /// the canonical <see cref="PartDefinition.subassemblyIds"/> claims on
        /// each part. Parts are the single source of truth for group
        /// membership — authors set membership per part, and every subassembly
        /// recomputes its roster at load time. If a subassembly's legacy
        /// authored <c>partIds</c> array is present (older packages), it is
        /// merged in as a fallback so the migration is non-breaking; new
        /// authoring tools should stop writing <c>subassembly.partIds</c>.
        /// Aggregates are left alone here — their <c>partIds</c>/
        /// <c>memberSubassemblyIds</c> composition is curated, not derived.
        /// Order: runs AFTER <see cref="IndexPartOwnership"/> (so parts are
        /// known) and BEFORE <see cref="BakeGroupRigidBody"/> /
        /// <see cref="BakePoseTable"/> (which query <c>sub.partIds</c>).
        /// </summary>
        private static void DeriveSubassemblyPartIds(MachinePackageDefinition package)
        {
            var subs = package.GetSubassemblies();
            if (subs == null || subs.Length == 0) return;
            var parts = package.parts;
            if (parts == null || parts.Length == 0) return;

            var rosterBySub = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var seenBySub   = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == null || string.IsNullOrEmpty(p.id) || p.subassemblyIds == null) continue;
                for (int k = 0; k < p.subassemblyIds.Length; k++)
                {
                    string subId = p.subassemblyIds[k];
                    if (string.IsNullOrEmpty(subId)) continue;
                    if (!rosterBySub.TryGetValue(subId, out var list))
                    {
                        rosterBySub[subId] = list = new List<string>();
                        seenBySub[subId]   = new HashSet<string>(StringComparer.Ordinal);
                    }
                    if (seenBySub[subId].Add(p.id)) list.Add(p.id);
                }
            }

            for (int i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];
                if (sub == null || string.IsNullOrEmpty(sub.id) || sub.isAggregate) continue;

                // Legacy fallback: merge any authored partIds that don't already
                // appear via part.subassemblyIds. Lets old packages load until
                // migration populates the canonical claims.
                if (sub.partIds != null && sub.partIds.Length > 0)
                {
                    if (!rosterBySub.TryGetValue(sub.id, out var list))
                    {
                        rosterBySub[sub.id] = list = new List<string>();
                        seenBySub[sub.id]   = new HashSet<string>(StringComparer.Ordinal);
                    }
                    for (int k = 0; k < sub.partIds.Length; k++)
                    {
                        string pid = sub.partIds[k];
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (seenBySub[sub.id].Add(pid)) list.Add(pid);
                    }
                }

                sub.partIds = rosterBySub.TryGetValue(sub.id, out var roster)
                    ? roster.ToArray()
                    : System.Array.Empty<string>();
            }
        }

        /// <summary>
        /// Final Normalize pass: runs <see cref="PoseResolver.Resolve"/> once
        /// for every visible (partId, seqIndex) pair and stores the answers in
        /// <see cref="MachinePackageDefinition.poseTable"/>. Editor and runtime
        /// read from this table instead of re-running resolution logic — the
        /// single-source-of-truth that eliminates the editor/runtime
        /// divergence bugs that prompted the rewrite.
        ///
        /// Complexity: O(parts × steps) — ~10k entries for a typical 200-step
        /// 50-part package. Runs once per load.
        /// </summary>
        private static void BakePoseTable(MachinePackageDefinition package)
        {
            var idx = new PoseResolverIndex(package);
            var map = new Dictionary<PoseKey, PoseResolution>(capacity: idx.firstVisibleSeqByPart.Count * 8);

            foreach (var kvp in idx.firstVisibleSeqByPart)
            {
                string partId = kvp.Key;
                int firstSeq = kvp.Value;

                // Populate from firstVisible through the end of the step list.
                // Past-task parts stay at assembledPosition in steady state,
                // so every forward seq is a valid (non-hidden) entry — that
                // guarantees the table covers any seq the editor or runtime
                // might look up.
                foreach (var s in idx.orderedSteps)
                {
                    int seq = s.sequenceIndex;
                    if (seq < firstSeq) continue;
                    var resolution = PoseResolver.Resolve(partId, seq, package, idx, PoseMode.Committed);
                    if (resolution.IsHidden) continue;
                    map[new PoseKey(partId, seq)] = resolution;
                }
            }

            package.poseTable = new PoseTable(map, idx.firstVisibleSeqByPart, idx.lastVisibleSeqByPart, package, idx);

            // Structural checks — WARN-only in this phase. Any violation is a
            // bug in either the authored data or the resolver/index; Step 6
            // of the rewrite flips these to throw. See PoseTableInvariants.
            PoseTableInvariants.Validate(package, idx, package.poseTable);
        }

        /// <summary>
        /// Label marker previously attached to synthetic NO-TASK stepPose
        /// entries baked into memory by <c>BakeNoTaskWaypoints</c>. The
        /// synthetic bake is gone (NO-TASK is now a first-class source
        /// resolved by <see cref="PoseResolver"/>), but the constant remains
        /// so <see cref="PoseResolverIndex"/> and the save-path filter can
        /// still recognise and skip legacy entries that may exist in old
        /// preview_config.json files.
        /// </summary>
        public const string AutoNoTaskLabel = "__notask_auto";

        /// <summary>
        /// Auto-derives <see cref="SubassemblyDefinition.isAggregate"/> from the
        /// presence of <c>memberSubassemblyIds</c>. The flag is redundant with
        /// the data — if a subassembly's members are other subassemblies, it IS
        /// an aggregate by definition. Authors no longer need to set the flag
        /// manually; existing JSON with explicit <c>isAggregate: true</c> still
        /// works unchanged. Must run BEFORE any pass that branches on the flag
        /// (template inflation, ownership indexing, rigid-body bake).
        /// </summary>
        private static void InferAggregateFlag(MachinePackageDefinition package)
        {
            var subs = package.GetSubassemblies();
            if (subs == null) return;
            for (int i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];
                if (sub == null) continue;
                if (!sub.isAggregate && sub.memberSubassemblyIds != null && sub.memberSubassemblyIds.Length > 0)
                    sub.isAggregate = true;
            }
        }

        /// <summary>
        /// Derives per-(subassembly, target) rigid-body representations from
        /// <see cref="PackagePreviewConfig.integratedSubassemblyPlacements"/>.
        /// For each placement, computes the centroid of member positions and
        /// each member's offset from that centroid. The editor consumes this
        /// so a group pose is ONE transform (center + fixed offsets), parallel
        /// to how individual parts work. JSON stays in per-member format;
        /// this derived data is never persisted.
        /// </summary>
        private static void BakeGroupRigidBody(MachinePackageDefinition package)
        {
            var subs = package.GetSubassemblies();
            if (subs == null || subs.Length == 0) return;

            // ── Start pose: fabrication centroid from partPlacements[].assembledPosition ──
            var partPlacements = package.previewConfig?.partPlacements;
            if (partPlacements != null && partPlacements.Length > 0)
            {
                var posByPart   = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                var rotByPart   = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
                var scaleByPart = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                for (int i = 0; i < partPlacements.Length; i++)
                {
                    var pp = partPlacements[i];
                    if (pp == null || string.IsNullOrEmpty(pp.partId)) continue;
                    posByPart[pp.partId]   = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
                    rotByPart[pp.partId]   = pp.assembledRotation.IsIdentity
                        ? Quaternion.identity
                        : new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w);
                    Vector3 s              = new Vector3(pp.assembledScale.x, pp.assembledScale.y, pp.assembledScale.z);
                    scaleByPart[pp.partId] = s.sqrMagnitude < 0.00001f ? Vector3.one : s;
                }

                for (int i = 0; i < subs.Length; i++)
                {
                    var sub = subs[i];
                    if (sub == null || sub.isAggregate || sub.partIds == null || sub.partIds.Length == 0) continue;

                    Vector3 sum = Vector3.zero;
                    int n = 0;
                    for (int k = 0; k < sub.partIds.Length; k++)
                    {
                        if (!string.IsNullOrEmpty(sub.partIds[k]) && posByPart.TryGetValue(sub.partIds[k], out var mpos))
                        { sum += mpos; n++; }
                    }
                    if (n == 0) continue;
                    Vector3 center = sum / n;

                    var rb = new GroupRigidBody
                    {
                        targetId              = null,
                        groupCenter           = center,
                        groupRotation         = Quaternion.identity,
                        memberPositionOffsets = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                        memberRotationOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal),
                        memberScales          = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                    };
                    for (int k = 0; k < sub.partIds.Length; k++)
                    {
                        string pid = sub.partIds[k];
                        if (string.IsNullOrEmpty(pid) || !posByPart.TryGetValue(pid, out var mpos)) continue;
                        rb.memberPositionOffsets[pid] = mpos - center;
                        rb.memberRotationOffsets[pid] = rotByPart.TryGetValue(pid, out var mr) ? mr : Quaternion.identity;
                        rb.memberScales[pid]          = scaleByPart.TryGetValue(pid, out var ms) ? ms : Vector3.one;
                    }
                    sub.startRigidBody = rb;
                }

                // ── Aggregate start pose: centroid of member leaves' centers ──
                // An aggregate's "start pose" is the geometric center of the
                // child-subassembly group centers, with each child treated as
                // a rigid offset. Enables moving the whole phase (e.g. the
                // Frame Cube) as a single rigid unit for integration into
                // larger assemblies.
                for (int i = 0; i < subs.Length; i++)
                {
                    var agg = subs[i];
                    if (agg == null || !agg.isAggregate || agg.memberSubassemblyIds == null || agg.memberSubassemblyIds.Length == 0) continue;

                    Vector3 aggSum = Vector3.zero;
                    int aggN = 0;
                    var childCenters = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                    for (int k = 0; k < agg.memberSubassemblyIds.Length; k++)
                    {
                        string cid = agg.memberSubassemblyIds[k];
                        if (string.IsNullOrEmpty(cid)) continue;
                        SubassemblyDefinition child = null;
                        for (int j = 0; j < subs.Length; j++)
                            if (subs[j] != null && string.Equals(subs[j].id, cid, StringComparison.Ordinal))
                            { child = subs[j]; break; }
                        if (child?.startRigidBody == null) continue;
                        childCenters[cid] = child.startRigidBody.groupCenter;
                        aggSum += child.startRigidBody.groupCenter;
                        aggN++;
                    }
                    if (aggN == 0) continue;
                    Vector3 aggCenter = aggSum / aggN;

                    var aggRb = new GroupRigidBody
                    {
                        targetId              = null,
                        groupCenter           = aggCenter,
                        groupRotation         = Quaternion.identity,
                        memberPositionOffsets = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                        memberRotationOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal),
                        memberScales          = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                    };
                    // Member offsets are per child-subassembly-id (not partId),
                    // marking where each child's root sits relative to the aggregate.
                    foreach (var kvp in childCenters)
                        aggRb.memberPositionOffsets[kvp.Key] = kvp.Value - aggCenter;
                    agg.startRigidBody = aggRb;
                }
            }

            // ── Assembled pose: integrated-target centroid per (subId, targetId) ──
            var placements = package.previewConfig?.integratedSubassemblyPlacements;
            if (placements == null || placements.Length == 0) return;

            for (int p = 0; p < placements.Length; p++)
            {
                var pl = placements[p];
                if (pl == null || pl.memberPlacements == null || pl.memberPlacements.Length == 0) continue;
                if (string.IsNullOrEmpty(pl.subassemblyId) || string.IsNullOrEmpty(pl.targetId)) continue;

                SubassemblyDefinition sub = null;
                for (int i = 0; i < subs.Length; i++)
                    if (subs[i] != null && string.Equals(subs[i].id, pl.subassemblyId, StringComparison.Ordinal))
                    { sub = subs[i]; break; }
                if (sub == null) continue;

                // Centroid of member positions in PreviewRoot space.
                Vector3 sum = Vector3.zero;
                int n = 0;
                for (int m = 0; m < pl.memberPlacements.Length; m++)
                {
                    var mp = pl.memberPlacements[m];
                    if (mp == null || string.IsNullOrEmpty(mp.partId)) continue;
                    sum += new Vector3(mp.position.x, mp.position.y, mp.position.z);
                    n++;
                }
                if (n == 0) continue;
                Vector3 center = sum / n;

                var rb = new GroupRigidBody
                {
                    targetId             = pl.targetId,
                    groupCenter          = center,
                    groupRotation        = Quaternion.identity,
                    memberPositionOffsets = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                    memberRotationOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal),
                    memberScales          = new Dictionary<string, Vector3>(StringComparer.Ordinal),
                };

                for (int m = 0; m < pl.memberPlacements.Length; m++)
                {
                    var mp = pl.memberPlacements[m];
                    if (mp == null || string.IsNullOrEmpty(mp.partId)) continue;
                    Vector3 mPos = new Vector3(mp.position.x, mp.position.y, mp.position.z);
                    Quaternion mRot = mp.rotation.IsIdentity
                        ? Quaternion.identity
                        : new Quaternion(mp.rotation.x, mp.rotation.y, mp.rotation.z, mp.rotation.w);
                    Vector3 mScl = new Vector3(mp.scale.x, mp.scale.y, mp.scale.z);
                    if (mScl.sqrMagnitude < 0.00001f) mScl = Vector3.one;

                    rb.memberPositionOffsets[mp.partId] = mPos - center;
                    rb.memberRotationOffsets[mp.partId] = mRot;
                    rb.memberScales[mp.partId]          = mScl;
                }

                if (sub.rigidBodyByTargetId == null)
                    sub.rigidBodyByTargetId = new Dictionary<string, GroupRigidBody>(StringComparer.Ordinal);
                sub.rigidBodyByTargetId[pl.targetId] = rb;
            }
        }

        // ── Staging Pose Bake ──

        /// <summary>
        /// Copies <see cref="StagingPose"/> data from each <see cref="PartDefinition"/>
        /// into the matching <see cref="PartPreviewPlacement"/> start fields so that all
        /// runtime code that reads <c>previewConfig.partPlacements[].startPosition</c>
        /// automatically gets the agent-authored values without modification.
        ///
        /// This is a one-way bake: <c>part.stagingPose</c> is the source of truth;
        /// <c>partPlacements.startPosition</c> is derived. Agents should only write
        /// <c>stagingPose</c> in <c>parts[]</c> — never edit <c>startPosition</c> directly.
        ///
        /// Parts without a <see cref="StagingPose"/> are left untouched so the legacy
        /// <c>previewConfig.partPlacements.startPosition</c> values (if present in an
        /// un-migrated package) continue to work as the fallback.
        /// </summary>
        private static void BakeStagingPoses(MachinePackageDefinition package)
        {
            if (package.parts == null) return;
            if (package.previewConfig == null)
                package.previewConfig = new PackagePreviewConfig();
            if (package.previewConfig.partPlacements == null)
                package.previewConfig.partPlacements = System.Array.Empty<PartPreviewPlacement>();

            var placementById = new Dictionary<string, PartPreviewPlacement>(
                package.previewConfig.partPlacements.Length, StringComparer.OrdinalIgnoreCase);
            foreach (PartPreviewPlacement pp in package.previewConfig.partPlacements)
            {
                if (pp != null && !string.IsNullOrWhiteSpace(pp.partId))
                    placementById[pp.partId] = pp;
            }

            bool addedNew = false;
            foreach (PartDefinition part in package.parts)
            {
                if (part?.stagingPose == null) continue;
                if (string.IsNullOrWhiteSpace(part.id)) continue;

                // If no placement entry exists, create one so the part has a
                // real position in the system (prevents fallback to 0,0,0).
                if (!placementById.TryGetValue(part.id, out PartPreviewPlacement placement))
                {
                    placement = new PartPreviewPlacement { partId = part.id };
                    placementById[part.id] = placement;
                    addedNew = true;
                }

                placement.startPosition = part.stagingPose.position;
                placement.startRotation = part.stagingPose.rotation;

                // Default assembledPosition to startPosition if not set —
                // part stays in place until explicitly moved by authoring.
                if (placement.assembledPosition.x == 0f
                    && placement.assembledPosition.y == 0f
                    && placement.assembledPosition.z == 0f)
                {
                    placement.assembledPosition = part.stagingPose.position;
                    placement.assembledRotation = part.stagingPose.rotation;
                }

                StagingPose sp = part.stagingPose;
                if (sp.scale.x != 0f || sp.scale.y != 0f || sp.scale.z != 0f)
                {
                    placement.startScale = sp.scale;
                    if (placement.assembledScale.x == 0f
                        && placement.assembledScale.y == 0f
                        && placement.assembledScale.z == 0f)
                        placement.assembledScale = sp.scale;
                }

                if (sp.color.a > 0f)
                    placement.color = sp.color;
            }

            // Merge any newly created placements back into the array
            if (addedNew)
                package.previewConfig.partPlacements = new System.Collections.Generic.List<PartPreviewPlacement>(
                    placementById.Values).ToArray();
        }

        // ── Part Templates ──

        private static void InflatePartTemplates(MachinePackageDefinition package)
        {
            PartTemplateDefinition[] templates = package.partTemplates;
            PartDefinition[] parts = package.parts;
            if (templates == null || templates.Length == 0 || parts == null)
                return;

            // Build lookup
            var lookup = new Dictionary<string, PartTemplateDefinition>(templates.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < templates.Length; i++)
            {
                if (templates[i] != null && !string.IsNullOrWhiteSpace(templates[i].id))
                    lookup[templates[i].id.Trim()] = templates[i];
            }

            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition part = parts[i];
                if (part == null || string.IsNullOrWhiteSpace(part.templateId))
                    continue;

                if (!lookup.TryGetValue(part.templateId.Trim(), out PartTemplateDefinition template))
                    continue;

                // Fill empty fields from template
                if (string.IsNullOrEmpty(part.name)) part.name = template.name;
                if (string.IsNullOrEmpty(part.displayName)) part.displayName = template.displayName;
                if (string.IsNullOrEmpty(part.category)) part.category = template.category;
                if (string.IsNullOrEmpty(part.material)) part.material = template.material;
                if (string.IsNullOrEmpty(part.function)) part.function = template.function;
                if (string.IsNullOrEmpty(part.structuralRole)) part.structuralRole = template.structuralRole;
                if (part.quantity == 0) part.quantity = template.quantity;
                if (part.toolIds == null || part.toolIds.Length == 0) part.toolIds = template.toolIds;
                if (string.IsNullOrEmpty(part.assetRef)) part.assetRef = template.assetRef;
                if (part.searchTerms == null || part.searchTerms.Length == 0) part.searchTerms = template.searchTerms;
                if (!part.allowPhysicalSubstitution) part.allowPhysicalSubstitution = template.allowPhysicalSubstitution;
                if (string.IsNullOrEmpty(part.defaultOrientationHint)) part.defaultOrientationHint = template.defaultOrientationHint;
                if (part.tags == null || part.tags.Length == 0) part.tags = template.tags;
            }
        }

        // ── Step Parent IDs ──

        private static void InferStepParentIds(MachinePackageDefinition package)
        {
            StepDefinition[] steps = package.steps;
            if (steps == null || steps.Length == 0) return;

            // Build step ID → step index lookup
            var stepIndex = new Dictionary<string, int>(steps.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] != null && !string.IsNullOrWhiteSpace(steps[i].id))
                    stepIndex[steps[i].id.Trim()] = i;
            }

            // Fill assemblyId from assembly.stepIds
            AssemblyDefinition[] assemblies = package.assemblies;
            if (assemblies != null)
            {
                for (int a = 0; a < assemblies.Length; a++)
                {
                    AssemblyDefinition assembly = assemblies[a];
                    if (assembly?.stepIds == null) continue;
                    for (int s = 0; s < assembly.stepIds.Length; s++)
                    {
                        if (stepIndex.TryGetValue(assembly.stepIds[s], out int idx))
                        {
                            if (string.IsNullOrEmpty(steps[idx].assemblyId))
                                steps[idx].assemblyId = assembly.id;
                        }
                    }
                }
            }

            // Fill subassemblyId from subassembly.stepIds
            SubassemblyDefinition[] subs = package.subassemblies;
            if (subs != null)
            {
                for (int sa = 0; sa < subs.Length; sa++)
                {
                    SubassemblyDefinition sub = subs[sa];
                    if (sub?.stepIds == null) continue;
                    for (int s = 0; s < sub.stepIds.Length; s++)
                    {
                        if (stepIndex.TryGetValue(sub.stepIds[s], out int idx))
                        {
                            if (string.IsNullOrEmpty(steps[idx].subassemblyId))
                                steps[idx].subassemblyId = sub.id;
                        }
                    }
                }
            }
        }

        // ── Tool Action Defaults ──

        // ── Tool Action → Part ID Resolution ──

        /// <summary>
        /// For every step with <c>requiredToolActions</c>, derives the set of part IDs
        /// those actions operate on (<c>targetId → target.associatedPartId</c>) and stores
        /// them in <c>step.derivedToolActionPartIds</c>.
        ///
        /// Kept in a SEPARATE field (not merged into <c>requiredPartIds</c>) so that:
        /// - <c>GetEffectiveRequiredPartIds()</c> / <c>requiredPartIds</c> keep their
        ///   "authored, owning-step" semantics — used by RevealStepParts,
        ///   RevertFutureStepParts, etc. to decide which parts belong to each step.
        /// - Callers that need the full set of parts a step touches (completion
        ///   repositioning, restore-on-navigation) call <c>GetAllTouchedPartIds()</c>.
        ///
        /// This prevents Use-family steps (e.g. drill-tighten) from being treated as
        /// the owning step of parts that were actually placed in a prior Place-family
        /// step.
        /// </summary>
        private static void ResolveToolActionPartIds(MachinePackageDefinition package)
        {
            StepDefinition[] steps = package.steps;
            TargetDefinition[] targets = package.targets;
            if (steps == null || targets == null || targets.Length == 0) return;

            // Build target lookup once
            var targetLookup = new Dictionary<string, TargetDefinition>(
                targets.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null && !string.IsNullOrWhiteSpace(targets[i].id))
                    targetLookup[targets[i].id] = targets[i];
            }

            for (int s = 0; s < steps.Length; s++)
            {
                StepDefinition step = steps[s];
                if (step == null) continue;

                ToolActionDefinition[] actions = step.requiredToolActions;
                if (actions == null || actions.Length == 0) continue;

                // Build set of already-authored requiredPartIds so we skip duplicates.
                var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (step.requiredPartIds != null)
                {
                    for (int i = 0; i < step.requiredPartIds.Length; i++)
                    {
                        if (!string.IsNullOrEmpty(step.requiredPartIds[i]))
                            authored.Add(step.requiredPartIds[i]);
                    }
                }

                var derived = new List<string>();
                for (int a = 0; a < actions.Length; a++)
                {
                    string tid = actions[a]?.targetId;
                    if (string.IsNullOrEmpty(tid)) continue;
                    if (!targetLookup.TryGetValue(tid, out var target)) continue;
                    if (string.IsNullOrEmpty(target.associatedPartId)) continue;
                    if (authored.Contains(target.associatedPartId)) continue;
                    if (derived.Contains(target.associatedPartId)) continue;
                    derived.Add(target.associatedPartId);
                }

                if (derived.Count > 0)
                    step.derivedToolActionPartIds = derived.ToArray();
            }
        }

        // ── Direct Target → Part ID Resolution ──

        /// <summary>
        /// For every step with direct <c>targetIds</c>, derives the set of part IDs
        /// those targets reference (<c>targetId → target.associatedPartId</c>) minus
        /// anything already in <c>requiredPartIds</c> or <c>derivedToolActionPartIds</c>,
        /// and stores the remainder in <c>step.derivedTargetPartIds</c>.
        ///
        /// This captures the "touch but don't own" case: a Place step that
        /// repositions a previously-placed part via its targets (anchor/stage/mount
        /// steps that move pre-built bench units into their final printer position)
        /// without claiming first-placement ownership. These parts then show up in
        /// <c>GetAllTouchedPartIds()</c> so Rule 3 (target.associatedPartId must be
        /// in owning step's touched set) passes, while Rule 2 (partId in &gt;1
        /// Place-family <c>requiredPartIds</c>) doesn't trigger.
        ///
        /// Must run after <see cref="ResolveToolActionPartIds"/> so we can dedupe
        /// against tool-action-derived parts.
        /// </summary>
        private static void ResolveDirectTargetPartIds(MachinePackageDefinition package)
        {
            StepDefinition[] steps = package.steps;
            TargetDefinition[] targets = package.targets;
            if (steps == null || targets == null || targets.Length == 0) return;

            var targetLookup = new Dictionary<string, TargetDefinition>(
                targets.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null && !string.IsNullOrWhiteSpace(targets[i].id))
                    targetLookup[targets[i].id] = targets[i];
            }

            for (int s = 0; s < steps.Length; s++)
            {
                StepDefinition step = steps[s];
                if (step == null) continue;
                if (step.targetIds == null || step.targetIds.Length == 0) continue;

                // Skip ids already covered by requiredPartIds or derivedToolActionPartIds
                // so the 'derived' field stays a strict "extra parts" set.
                var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (step.requiredPartIds != null)
                    for (int i = 0; i < step.requiredPartIds.Length; i++)
                        if (!string.IsNullOrEmpty(step.requiredPartIds[i]))
                            covered.Add(step.requiredPartIds[i]);
                if (step.derivedToolActionPartIds != null)
                    for (int i = 0; i < step.derivedToolActionPartIds.Length; i++)
                        if (!string.IsNullOrEmpty(step.derivedToolActionPartIds[i]))
                            covered.Add(step.derivedToolActionPartIds[i]);

                var derived = new List<string>();
                for (int i = 0; i < step.targetIds.Length; i++)
                {
                    string tid = step.targetIds[i];
                    if (string.IsNullOrEmpty(tid)) continue;
                    if (!targetLookup.TryGetValue(tid, out var target)) continue;
                    if (string.IsNullOrEmpty(target.associatedPartId)) continue;
                    if (covered.Contains(target.associatedPartId)) continue;
                    covered.Add(target.associatedPartId);
                    derived.Add(target.associatedPartId);
                }

                if (derived.Count > 0)
                    step.derivedTargetPartIds = derived.ToArray();
            }
        }

        // ── Tool Action Defaults ──

        private static void NormalizeToolActions(MachinePackageDefinition package)
        {
            StepDefinition[] steps = package.steps;
            if (steps == null) return;

            for (int s = 0; s < steps.Length; s++)
            {
                ToolActionDefinition[] actions = steps[s]?.requiredToolActions;
                if (actions == null) continue;

                for (int a = 0; a < actions.Length; a++)
                {
                    ToolActionDefinition action = actions[a];
                    if (action == null) continue;

                    if (action.requiredCount < 1)
                        action.requiredCount = 1;

                    if (string.IsNullOrWhiteSpace(action.id))
                        action.id = $"{steps[s].id}_action_{a}";
                }
            }
        }

        // ── Part Ownership Index ──

        /// <summary>
        /// Bakes the authoritative "who owns this part" answers onto each
        /// <see cref="PartDefinition"/> so runtime callers don't re-scan
        /// subassemblies/steps every time.
        ///
        /// For every non-aggregate subassembly, sets <c>part.owningSubassemblyId</c>
        /// to the subassembly id. For every Place-family step, appends the
        /// step id to <see cref="PartDefinition.owningPlaceStepIds"/> and (on
        /// first write) also sets the scalar <see cref="PartDefinition.owningPlaceStepId"/>
        /// as the canonical "first placement" for legacy callers. Multi-Place
        /// is now supported: a part can be Required by several Place steps
        /// representing distinct physical placements (e.g. loose alignment
        /// followed by final placement). Aggregate subassemblies are
        /// intentionally skipped (they may contain child parts).
        /// </summary>
        private static void IndexPartOwnership(MachinePackageDefinition package)
        {
            PartDefinition[] parts = package.parts;
            if (parts == null || parts.Length == 0) return;

            var partById = new Dictionary<string, PartDefinition>(
                parts.Length, StringComparer.OrdinalIgnoreCase);
            var ownerListsByPart = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition p = parts[i];
                if (p == null || string.IsNullOrWhiteSpace(p.id)) continue;

                // Clear any stale state from a prior Normalize call on the
                // same in-memory package (editor reload path).
                p.owningSubassemblyId = null;
                p.owningPlaceStepId   = null;
                p.owningPlaceStepIds  = null;
                partById[p.id] = p;
            }

            SubassemblyDefinition[] subs = package.subassemblies;
            if (subs != null)
            {
                for (int sa = 0; sa < subs.Length; sa++)
                {
                    SubassemblyDefinition sub = subs[sa];
                    if (sub == null || sub.isAggregate) continue;
                    if (sub.partIds == null || string.IsNullOrWhiteSpace(sub.id)) continue;

                    for (int i = 0; i < sub.partIds.Length; i++)
                    {
                        string pid = sub.partIds[i];
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partById.TryGetValue(pid, out PartDefinition part)) continue;
                        if (string.IsNullOrEmpty(part.owningSubassemblyId))
                            part.owningSubassemblyId = sub.id;
                    }
                }
            }

            StepDefinition[] steps = package.steps;
            if (steps != null)
            {
                // Walk steps in ascending sequenceIndex so the first-append
                // also becomes owningPlaceStepId (canonical "first placement")
                // and owningPlaceStepIds is naturally sorted.
                var ordered = new List<StepDefinition>(steps.Length);
                for (int s = 0; s < steps.Length; s++) if (steps[s] != null) ordered.Add(steps[s]);
                ordered.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                for (int s = 0; s < ordered.Count; s++)
                {
                    StepDefinition step = ordered[s];
                    if (step == null || string.IsNullOrWhiteSpace(step.id)) continue;
                    if (step.ResolvedFamily != StepFamily.Place) continue;
                    if (step.requiredPartIds == null) continue;

                    for (int i = 0; i < step.requiredPartIds.Length; i++)
                    {
                        string pid = step.requiredPartIds[i];
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partById.TryGetValue(pid, out PartDefinition part)) continue;

                        if (!ownerListsByPart.TryGetValue(part.id, out var list))
                            ownerListsByPart[part.id] = list = new List<string>();
                        if (list.Contains(step.id)) continue; // dedupe if a step lists the part twice
                        list.Add(step.id);
                        if (string.IsNullOrEmpty(part.owningPlaceStepId))
                            part.owningPlaceStepId = step.id;
                    }
                }

                foreach (var kvp in ownerListsByPart)
                {
                    if (!partById.TryGetValue(kvp.Key, out var part)) continue;
                    part.owningPlaceStepIds = kvp.Value.ToArray();
                }
            }
        }
    }
}
