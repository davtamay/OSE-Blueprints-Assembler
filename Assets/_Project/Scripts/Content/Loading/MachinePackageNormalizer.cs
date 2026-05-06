using System;
using System.Collections.Generic;
using OSE.Core;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Post-deserialization pass that inflates compact JSON conventions into
    /// the fully-populated definition objects the runtime expects.
    ///
    /// Handles:
    /// - Part templates: fills empty PartDefinition fields from the referenced template.
    /// - Step parent refs: infers assemblyId / partGroupId from assembly and partGroup stepIds.
    /// - Tool action defaults: auto-generates missing id, defaults requiredCount to 1.
    /// - Null arrays: replaces null arrays with empty arrays so callers never need null checks.
    /// </summary>
    public static class MachinePackageNormalizer
    {
        /// <summary>
        /// Re-runs the cue-synthesis bake and pose-table rebuild
        /// idempotently. Use from editor paths that mutate cues in place
        /// (add / delete / retarget / change holdAtEnd) so synthesized
        /// stepPoses tied to the old cue graph are stripped and rebuilt
        /// from the current state in the same tick. Without this, the
        /// in-memory poseTable still reflects the pre-mutation cue set,
        /// and future steps show stale hold-at-end poses until the next
        /// full package reload.
        ///
        /// <para>Stripping happens inside <see cref="BakeHoldAtEndEndPoses"/>
        /// (per its idempotency contract). Callers don't need to strip
        /// manually; just invoke this after the cue mutation.</para>
        ///
        /// <para>Safe to call repeatedly — every pass here is idempotent.
        /// Do NOT call from the hot path; this does an O(subs × cues) +
        /// O(parts × steps) pass.</para>
        /// </summary>
        public static void RebakeCueSynthesisAndPoseTable(MachinePackageDefinition package)
        {
            if (package == null) return;
            BakeHoldAtEndEndPoses(package);
            BakePoseTable(package);
            BakePartGroupLifecycle(package);
        }

        public static void Normalize(MachinePackageDefinition package)
        {
            if (package == null) return;

            // Phantom-payload purge MUST run first. Unity's JsonUtility creates
            // a default instance for any [Serializable] reference field absent
            // from the JSON — so `step.workingOrientation`, `step.animationCues`,
            // `step.particleEffects` come back non-null on every step that
            // didn't author them. Downstream code checks `!= null` to decide
            // whether to apply a rotation, append "rotated to expose the work
            // area" to the instruction text, schedule cues, or spawn particles.
            // Without this pass the runtime treats every step as if it had
            // authored intent for all three subsystems (the step-263
            // phantom-orientation bug). Caught by `EditorRuntimeIsolationTests`.
            DropEmptyStepPayloads(package);
            DropEmptyTaskOrderTransformPayloads(package);

            // Expand Step Configuration Prefab instances into virtual steps
            // BEFORE every downstream pass so template inheritance, parent-id
            // inference, validation, and pose baking treat them identically
            // to authored steps. Edits to a prefab YAML propagate to every
            // instance on next load — see Slice 1 of the prefab plan.
            ExpandPrefabInstances(package);
            // Re-run the phantom-payload purge so the freshly-expanded
            // virtual steps don't inherit JsonUtility's default-instance
            // noise on workingOrientation / animationCues / particleEffects.
            // Idempotent for the authored steps already cleaned above.
            DropEmptyStepPayloads(package);
            DropEmptyTaskOrderTransformPayloads(package);

            InferAggregateFlag(package);
            InflatePartTemplates(package);
            BakeStagingPoses(package);
            InferStepParentIds(package);
            NormalizeConfirmActionTaskOrder(package);
            EnsureConfirmActionForConfirmSteps(package);
            EnsureProfileForUseTightenSteps(package);
            NormalizeTaskOrderToolActionKinds(package);
            EnsureTaskOrderCoversRequirements(package);
            MarkVisualOnlyTaskOrderEntriesOptional(package);
            ValidateUseFamilyPartsArePrePlaced(package);
            ValidateUnorderedSets(package);
            NormalizeToolActions(package);
            ResolveToolActionPartIds(package);
            ResolveDirectTargetPartIds(package);
            IndexPartOwnership(package);
            DerivePartGroupPartIds(package);
            BakeGroupRigidBody(package);

            // Cue passes run BEFORE BakePoseTable so synthesized stepPoses
            // (below) are picked up by the regular bake. Trigger rewrite and
            // host migration don't depend on poseTable.
            NormalizeAnimationCueTriggers(package);
            NormalizeParticleCueSourceMode(package);
            MigrateStepAnimationCuesToHosts(package);
            ValidateAnimationCueInvariants(package);
            BakeHoldAtEndEndPoses(package);

            BakePoseTable(package);

            // Per-partGroup lifecycle (firstBuiltSeq, lastTouchedSeq,
            // touchedSeqs[]). Both TTAW and the runtime parts/groups
            // overlay query this via PartGroupLifecycleResolver to
            // present groups as accumulating tiers (Active/Recent/Built/
            // Hidden) instead of strict per-step filtering. Runs LAST so
            // every derived part-id field (derivedToolActionPartIds,
            // derivedTargetPartIds, partGroups[].partIds) is populated.
            BakePartGroupLifecycle(package);
        }

        /// <summary>
        /// Sets <see cref="StepDefinition.workingOrientation"/>,
        /// <see cref="StepDefinition.animationCues"/>, and
        /// <see cref="StepDefinition.particleEffects"/> back to null when their
        /// in-memory instance carries no authored content. JsonUtility creates
        /// these as default instances when the JSON field is absent — see the
        /// comment in <see cref="Normalize"/> for the full rationale.
        /// Idempotent. Logs the count when any phantom is dropped so future
        /// regressions surface in build logs. Public so EditMode tests can
        /// invoke it directly without building a fully-populated package.
        /// </summary>
        public static void DropEmptyStepPayloads(MachinePackageDefinition package)
        {
            if (package == null) return;

            int droppedOrient = 0, droppedCues = 0, droppedParticles = 0, droppedPrefabRef = 0;

            if (package.steps != null)
            {
                for (int i = 0; i < package.steps.Length; i++)
                {
                    var s = package.steps[i];
                    if (s == null) continue;

                    if (s.workingOrientation != null && s.workingOrientation.IsEmpty())
                    {
                        s.workingOrientation = null;
                        droppedOrient++;
                    }
                    if (s.animationCues != null && s.animationCues.IsEmpty())
                    {
                        s.animationCues = null;
                        droppedCues++;
                    }
                    if (s.particleEffects != null && s.particleEffects.IsEmpty())
                    {
                        s.particleEffects = null;
                        droppedParticles++;
                    }
                    if (s.prefabRef != null && s.prefabRef.IsEmpty())
                    {
                        s.prefabRef = null;
                        droppedPrefabRef++;
                    }
                }
            }

            if (package.parts != null)
            {
                foreach (var p in package.parts)
                {
                    if (p?.prefabRef != null && p.prefabRef.IsEmpty())
                    {
                        p.prefabRef = null;
                        droppedPrefabRef++;
                    }
                }
            }

            if (package.partGroups != null)
            {
                foreach (var g in package.partGroups)
                {
                    if (g?.prefabRef != null && g.prefabRef.IsEmpty())
                    {
                        g.prefabRef = null;
                        droppedPrefabRef++;
                    }
                }
            }

            if (droppedOrient + droppedCues + droppedParticles + droppedPrefabRef > 0)
                OseLog.Info($"[Normalizer.DropEmptyStepPayloads] '{package.packageId}': dropped {droppedOrient} workingOrientation, {droppedCues} animationCues, {droppedParticles} particleEffects, {droppedPrefabRef} prefabRef phantom payloads (JsonUtility default-instance noise).");
        }

        /// <summary>
        /// Companion to <see cref="DropEmptyStepPayloads"/> for the
        /// <see cref="TaskOrderEntry.startTransform"/> /
        /// <see cref="TaskOrderEntry.endTransform"/> reference fields. JsonUtility
        /// inflates absent reference fields to default instances on every load,
        /// which the inspector + runtime both read as "this task has an authored
        /// inline pose" — flipping read-only Start fields to editable, and
        /// causing the runtime to snap the part to (0,0,0,0,0,0,0,0). The tell
        /// is rotation w == 0, an invalid quaternion no real authored value
        /// produces (the identity quaternion has w == 1). Strip back to null
        /// so the opt-in invariant holds: null ≡ inherited / no authored end.
        /// </summary>
        public static void DropEmptyTaskOrderTransformPayloads(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;
            int droppedStart = 0;
            int droppedEnd   = 0;
            foreach (var step in package.steps)
            {
                if (step?.taskOrder == null) continue;
                foreach (var entry in step.taskOrder)
                {
                    if (entry == null) continue;
                    if (entry.startTransform != null
                        && IsDefaultInflatedTaskEndTransform(entry.startTransform))
                    {
                        entry.startTransform = null;
                        droppedStart++;
                    }
                    if (entry.endTransform != null
                        && IsDefaultInflatedTaskEndTransform(entry.endTransform))
                    {
                        entry.endTransform = null;
                        droppedEnd++;
                    }
                }
            }
            if (droppedStart + droppedEnd > 0)
                OseLog.Info($"[Normalizer.DropEmptyTaskOrderTransformPayloads] '{package.packageId}': dropped {droppedStart} startTransform, {droppedEnd} endTransform phantom payloads.");
        }

        private static bool IsDefaultInflatedTaskEndTransform(TaskEndTransform t)
        {
            if (t == null) return true;
            return t.position.x == 0f && t.position.y == 0f && t.position.z == 0f
                && t.rotation.x == 0f && t.rotation.y == 0f && t.rotation.z == 0f && t.rotation.w == 0f
                && t.scale.x == 0f && t.scale.y == 0f && t.scale.z == 0f;
        }

        /// <summary>
        /// Walks <see cref="MachinePackageDefinition.prefabInstances"/> and
        /// appends one expanded <see cref="StepDefinition"/> per
        /// step template per instance, each tagged with a matching
        /// <see cref="PrefabRef"/>. The instance entry stays in the package
        /// so editors can show provenance + offer Bake / Discard, while the
        /// virtual steps are merged into <see cref="MachinePackageDefinition.steps"/>
        /// so every downstream consumer treats them as ordinary steps.
        /// Idempotent: removes previously-expanded virtual steps for an
        /// instance before re-expanding (so editor mutations to bindings or
        /// the source YAML take effect on the next call without duplicates).
        /// </summary>
        public static void ExpandPrefabInstances(MachinePackageDefinition package)
        {
            if (package == null) return;
            var instances = package.prefabInstances;
            if (instances == null || instances.Length == 0) return;

            string prefabsDir = PrefabExpander.GetPrefabsDir();

            // Drop every previously-expanded virtual entity. Re-emitted
            // below from the current instance set; this also clears
            // orphans whose source instance was Discarded between calls.
            // Authored entities (no prefabRef, or prefabRef cleared by
            // DropEmptyPrefabRef above) pass through untouched.
            package.steps      = StripVirtual(package.steps,      s => s?.prefabRef);
            package.parts      = StripVirtual(package.parts,      p => p?.prefabRef);
            package.partGroups = StripVirtual(package.partGroups, g => g?.prefabRef);
            // Placements are stripped indirectly: any placement whose
            // partId no longer exists in `package.parts` after the strip
            // above is treated as virtual and discarded.
            if (package.previewConfig?.partPlacements != null && package.previewConfig.partPlacements.Length > 0)
            {
                var liveParts = new HashSet<string>(StringComparer.Ordinal);
                if (package.parts != null)
                    foreach (var p in package.parts)
                        if (p != null && !string.IsNullOrEmpty(p.id)) liveParts.Add(p.id);
                var keepP = new List<PartPreviewPlacement>(package.previewConfig.partPlacements.Length);
                foreach (var pp in package.previewConfig.partPlacements)
                {
                    if (pp == null) continue;
                    if (string.IsNullOrEmpty(pp.partId) || liveParts.Contains(pp.partId)) keepP.Add(pp);
                }
                if (keepP.Count != package.previewConfig.partPlacements.Length)
                    package.previewConfig.partPlacements = keepP.ToArray();
            }

            int totalErrors   = 0;
            int totalSteps = 0, totalParts = 0, totalGroups = 0, totalPlacements = 0;
            var emittedSteps      = new List<StepDefinition>();
            var emittedParts      = new List<PartDefinition>();
            var emittedGroups     = new List<PartGroupDefinition>();
            var emittedPlacements = new List<PartPreviewPlacement>();
            foreach (var instance in instances)
            {
                if (instance == null || instance.IsEmpty()) continue;
                var result = PrefabExpander.Expand(instance, prefabsDir);
                if (result.Errors != null && result.Errors.Count > 0)
                {
                    totalErrors += result.Errors.Count;
                    foreach (var err in result.Errors)
                        OseLog.Warn($"[Normalizer.ExpandPrefabInstances] {instance.instanceId} ({instance.prefabId}): {err}");
                }
                if (result.Steps      != null && result.Steps.Length      > 0) { emittedSteps.AddRange(result.Steps);           totalSteps      += result.Steps.Length; }
                if (result.Parts      != null && result.Parts.Length      > 0) { emittedParts.AddRange(result.Parts);           totalParts      += result.Parts.Length; }
                if (result.PartGroups != null && result.PartGroups.Length > 0) { emittedGroups.AddRange(result.PartGroups);     totalGroups     += result.PartGroups.Length; }
                if (result.Placements != null && result.Placements.Length > 0) { emittedPlacements.AddRange(result.Placements); totalPlacements += result.Placements.Length; }
            }

            package.steps      = AppendArray(package.steps,      emittedSteps);
            package.parts      = AppendArray(package.parts,      emittedParts);
            package.partGroups = AppendArray(package.partGroups, emittedGroups);
            if (emittedPlacements.Count > 0)
            {
                package.previewConfig ??= new PackagePreviewConfig();
                package.previewConfig.partPlacements = AppendArray(package.previewConfig.partPlacements, emittedPlacements);
            }

            OseLog.Info(
                $"[Normalizer.ExpandPrefabInstances] '{package.packageId}': " +
                $"expanded {instances.Length} instance(s) → {totalSteps} step(s), {totalParts} part(s), " +
                $"{totalGroups} partGroup(s), {totalPlacements} placement(s), {totalErrors} error(s).");
        }

        private static T[] StripVirtual<T>(T[] source, Func<T, PrefabRef> refSelector) where T : class
        {
            if (source == null || source.Length == 0) return source;
            var keep = new List<T>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                T item = source[i];
                if (item == null) continue;
                PrefabRef pr = refSelector(item);
                if (pr != null && !string.IsNullOrEmpty(pr.instanceId)) continue;
                keep.Add(item);
            }
            return keep.Count == source.Length ? source : keep.ToArray();
        }

        private static T[] AppendArray<T>(T[] source, List<T> additions)
        {
            if (additions == null || additions.Count == 0) return source;
            int existing = source?.Length ?? 0;
            var merged = new T[existing + additions.Count];
            if (existing > 0) Array.Copy(source, 0, merged, 0, existing);
            additions.CopyTo(merged, existing);
            return merged;
        }

        // Canonical trigger names for AnimationCueEntry.trigger. Every alias
        // encountered in authored JSON is rewritten to one of these at load.
        private const string TriggerOnActivate         = "onActivate";
        private const string TriggerAfterDelay         = "afterDelay";
        private const string TriggerAfterPartsShown    = "afterPartsShown";
        private const string TriggerOnStepComplete     = "onStepComplete";
        private const string TriggerOnFirstInteraction = "onFirstInteraction";
        private const string TriggerOnTaskComplete     = "onTaskComplete";
        private const string TriggerOnDuringAction     = "onDuringAction";

        /// <summary>
        /// Rewrites legacy / typo trigger aliases to their canonical names so
        /// cues land in the same scheduling bucket regardless of how they
        /// were authored. Runs on every cue across steps, parts, and
        /// partGroups. Protective: prevents the "onStepActivate vs
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

            var subs = package.GetPartGroups();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    rewrites += RewriteArray(subs[i]?.animationCues);

            if (package.tools != null)
                for (int i = 0; i < package.tools.Length; i++)
                    rewrites += RewriteArray(package.tools[i]?.animationCues);

            if (rewrites > 0)
                OseLog.Info($"[CueRuntime.Normalize] rewrote {rewrites} legacy trigger alias(es) to canonical names in '{package.packageId}'.");

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
                if (string.Equals(trigger, TriggerOnDuringAction,     StringComparison.OrdinalIgnoreCase)) return TriggerOnDuringAction;
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
        /// For <c>type == "particle"</c> cues, fills in <c>particleSourceMode</c>
        /// when the author left it empty. Back-compat for content written
        /// before the field existed: a non-empty <c>particlePresetId</c> maps
        /// to <c>"preset"</c>; a non-empty <c>particlePrefabRef</c> maps to
        /// <c>"prefab"</c>. Entries with neither are left alone so the
        /// validator can flag them.
        /// </summary>
        private static void NormalizeParticleCueSourceMode(MachinePackageDefinition package)
        {
            if (package.steps != null)
                for (int i = 0; i < package.steps.Length; i++)
                    InferModes(package.steps[i]?.animationCues?.cues);

            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                    InferModes(package.parts[i]?.animationCues);

            var subs = package.GetPartGroups();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    InferModes(subs[i]?.animationCues);

            if (package.tools != null)
                for (int i = 0; i < package.tools.Length; i++)
                    InferModes(package.tools[i]?.animationCues);

            static void InferModes(AnimationCueEntry[] arr)
            {
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    var e = arr[i];
                    if (e == null) continue;
                    if (!string.Equals(e.type, "particle", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(e.particleSourceMode)) continue;

                    if (!string.IsNullOrEmpty(e.particlePresetId))
                        e.particleSourceMode = "preset";
                    else if (!string.IsNullOrEmpty(e.particlePrefabRef))
                        e.particleSourceMode = "prefab";
                }
            }
        }

        /// <summary>
        /// Host-owned cues (part / partGroup) are the authoritative home.
        /// Any cues still living on <c>step.animationCues.cues</c> are
        /// migrated to their target host at load time, so the runtime only
        /// ever sees host-owned cues. Legacy content keeps working without
        /// manual JSON editing; new authoring tools write directly to hosts.
        ///
        /// Migration rules:
        /// - If the entry has a non-empty <c>targetPartGroupId</c>, move to
        ///   that partGroup's <c>animationCues</c>.
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

                    if (!string.IsNullOrEmpty(entry.targetPartGroupId)
                        && TryAppendToPartGroup(package, entry.targetPartGroupId, entry))
                    { moved++; continue; }

                    if (entry.targetPartIds != null && entry.targetPartIds.Length == 1
                        && !string.IsNullOrEmpty(entry.targetPartIds[0])
                        && TryAppendToPart(package, entry.targetPartIds[0], entry))
                    { moved++; continue; }

                    if (entry.targetToolIds != null && entry.targetToolIds.Length == 1
                        && !string.IsNullOrEmpty(entry.targetToolIds[0])
                        && TryAppendToTool(package, entry.targetToolIds[0], entry, step?.id))
                    { moved++; continue; }

                    kept.Add(entry);
                    left++;
                }

                step.animationCues.cues = kept.ToArray();
            }

            if (moved > 0)
                OseLog.Info($"[CueRuntime.Migrate] moved {moved} step-level cue(s) onto their target host in '{package.packageId}'.");
            if (left > 0)
                OseLog.Warn($"[CueRuntime.Migrate] {left} step-level cue(s) in '{package.packageId}' have no clear host target and remain on the step. Edit them in TTAW to assign a host.");

            static bool TryAppendToPartGroup(MachinePackageDefinition pkg, string subId, AnimationCueEntry entry)
            {
                var subs = pkg.GetPartGroups();
                if (subs == null) return false;
                for (int i = 0; i < subs.Length; i++)
                {
                    if (subs[i] == null) continue;
                    if (!string.Equals(subs[i].id, subId, StringComparison.Ordinal)) continue;
                    if (HasEquivalentCue(subs[i].animationCues, entry))
                    {
                        OseLog.Error($"[CueRuntime.Migrate] partGroup '{subId}' already has a (type='{entry.type}', trigger='{entry.trigger}') cue. Refusing to migrate a duplicate from the step level — delete one in TTAW.");
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
                        OseLog.Error($"[CueRuntime.Migrate] part '{partId}' already has a (type='{entry.type}', trigger='{entry.trigger}') cue. Refusing to migrate a duplicate from the step level — delete one in TTAW.");
                        return false;
                    }
                    pkg.parts[i].animationCues = Append(pkg.parts[i].animationCues, entry);
                    return true;
                }
                return false;
            }

            static bool TryAppendToTool(MachinePackageDefinition pkg, string toolId, AnimationCueEntry entry, string stepId)
            {
                if (pkg.tools == null) return false;
                for (int i = 0; i < pkg.tools.Length; i++)
                {
                    if (pkg.tools[i] == null) continue;
                    if (!string.Equals(pkg.tools[i].id, toolId, StringComparison.Ordinal)) continue;
                    if (HasEquivalentCue(pkg.tools[i].animationCues, entry))
                    {
                        OseLog.Error($"[CueRuntime.Migrate] tool '{toolId}' already has a (type='{entry.type}', trigger='{entry.trigger}') cue. Refusing to migrate a duplicate from the step level — delete one in TTAW.");
                        return false;
                    }
                    // Step-level tool cues implicitly scoped to one step; keep
                    // that scope by stamping stepIds so the migrated host-owned
                    // entry only fires on that step.
                    if ((entry.stepIds == null || entry.stepIds.Length == 0) && !string.IsNullOrEmpty(stepId))
                        entry.stepIds = new[] { stepId };
                    pkg.tools[i].animationCues = Append(pkg.tools[i].animationCues, entry);
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
                        OseLog.Error($"[CueRuntime.Validate] step '{s.id}' still has {cues.Length} step-level cue(s) after migration. Assign a target host (part/partGroup) in TTAW.");
                }
            }

            // 2. No unknown triggers on host-owned cues.
            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                    CheckTriggers(package.parts[i]?.animationCues, $"part '{package.parts[i]?.id}'");

            var subs = package.GetPartGroups();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                    CheckTriggers(subs[i]?.animationCues, $"partGroup '{subs[i]?.id}'");

            if (package.tools != null)
                for (int i = 0; i < package.tools.Length; i++)
                    CheckTriggers(package.tools[i]?.animationCues, $"tool '{package.tools[i]?.id}'");

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
                        t == TriggerOnFirstInteraction || t == TriggerOnTaskComplete ||
                        t == TriggerOnDuringAction;
                    if (!canonical)
                        OseLog.Error($"[CueRuntime.Validate] {hostLabel} cue[{i}] has unknown trigger '{t}'. Canonical values: onActivate, afterDelay, afterPartsShown, onStepComplete, onFirstInteraction, onTaskComplete, onDuringAction.");
                }
            }
        }

        /// <summary>
        /// Reconciles <c>kind="confirm_action"</c> task entries with step family
        /// to prevent cursor deadlock at runtime.
        ///
        /// <para>Background: <c>confirm_action</c> is an editor-only convention
        /// that only <see cref="Runtime.ConfirmStepHandler"/> recognizes — and
        /// only when <c>step.IsConfirmation</c> is true. Runtime has no other
        /// subsystem that notifies this task kind as complete. So a non-Confirm
        /// step carrying a <c>confirm_action</c> in its taskOrder will block
        /// the cursor forever: the Confirm handler short-circuits on the family
        /// mismatch, and no other handler knows what to do with the entry.</para>
        ///
        /// <para>Root incident (2026-04-21, step_batch_carriage_clean_holes
        /// seq 51): family=Use with a lone <c>confirm_action</c> task, no
        /// requiredToolActions, no requiredPartIds. Nothing completed the
        /// task — step 52 appeared stuck because the cursor was actually
        /// still parked on 51.</para>
        ///
        /// <para>Repair strategy (idempotent):
        /// <list type="bullet">
        ///   <item>Step has zero real gating tasks (no requiredPartIds, no
        ///         requiredToolActions with ids): semantically a
        ///         confirmation — promote <c>family</c> to <c>"Confirm"</c>.
        ///         The existing <c>confirm_action</c> entry is now valid.</item>
        ///   <item>Step HAS real gating tasks: <c>confirm_action</c> was
        ///         spurious (likely residue from a later-changed family) —
        ///         strip those entries so real tasks alone gate completion.</item>
        /// </list>
        /// Runs BEFORE <see cref="NormalizeTaskOrderToolActionKinds"/> and
        /// <see cref="EnsureTaskOrderCoversRequirements"/> so downstream passes
        /// operate on the post-repair data.</para>
        /// </summary>
        private static void NormalizeConfirmActionTaskOrder(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            int promoted = 0;
            int stripped = 0;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step?.taskOrder == null || step.taskOrder.Length == 0) continue;

                // Look for any confirm_action entry.
                bool hasConfirmAction = false;
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var e = step.taskOrder[ti];
                    if (e != null && string.Equals(e.kind, "confirm_action", StringComparison.Ordinal))
                    { hasConfirmAction = true; break; }
                }
                if (!hasConfirmAction) continue;

                // Confirm family is the only place confirm_action is valid.
                if (step.ResolvedFamily == StepFamily.Confirm) continue;

                // Count real gating tasks (requiredPartIds + non-null required
                // tool action ids). These are the entries the runtime actually
                // notifies to advance the cursor.
                int realTasks = 0;
                if (step.requiredPartIds != null) realTasks += step.requiredPartIds.Length;
                if (step.requiredToolActions != null)
                {
                    for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                    {
                        var a = step.requiredToolActions[ai];
                        if (a != null && !string.IsNullOrEmpty(a.id)) realTasks++;
                    }
                }

                if (realTasks == 0)
                {
                    // The confirm_action is the ONLY gating task. That's Confirm
                    // semantics — fix the family so the Confirm handler can
                    // complete the step on button press.
                    step.family = "Confirm";
                    promoted++;
                    OseLog.VerboseInfo($"[TaskOrder.Normalize] step '{step.id}': promoted family → 'Confirm' (had confirm_action with no real tasks).");
                    continue;
                }

                // Real tasks exist — confirm_action is spurious. Strip it.
                var filtered = new List<TaskOrderEntry>(step.taskOrder.Length);
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var e = step.taskOrder[ti];
                    if (e != null && string.Equals(e.kind, "confirm_action", StringComparison.Ordinal)) continue;
                    filtered.Add(e);
                }
                step.taskOrder = filtered.ToArray();
                stripped++;
                OseLog.VerboseInfo($"[TaskOrder.Normalize] step '{step.id}': stripped confirm_action entries (family={step.ResolvedFamily}, {realTasks} real task(s) gate completion).");
            }

            if (promoted > 0 || stripped > 0)
            {
                OseLog.Warn($"[TaskOrder.Normalize] Repaired confirm_action/family mismatch: promoted {promoted} step(s) to family=Confirm, stripped confirm_action from {stripped} step(s). Set OseLog.Verbose=true for per-step detail, or author family=Confirm when the step ends on a button press (confirm_action is valid only on Confirm family).");
            }
        }

        /// <summary>
        /// Appends a <c>kind="confirm_action"</c> task to any Confirm-family
        /// step whose taskOrder lacks one. Confirm steps complete only when
        /// the user fires the Continue button — that button maps to a
        /// confirm_action entry in the cursor. Without it the cursor has no
        /// gating task and the Continue button is either disabled (when other
        /// non-optional task entries hold the cursor) or enabled-but-inert
        /// (no confirm_action to fire). Observed 2026-04-25 — 74 Confirm
        /// steps in d3d_v18_10 missing the entry, blocking session progression
        /// past step 57.
        ///
        /// <para>Pairs with <see cref="NormalizeConfirmActionTaskOrder"/>
        /// which handles the inverse case (confirm_action present on non-
        /// Confirm family).</para>
        /// </summary>
        private static void EnsureConfirmActionForConfirmSteps(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            int added = 0;
            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step == null) continue;
                if (step.ResolvedFamily != StepFamily.Confirm) continue;

                bool hasConfirm = false;
                if (step.taskOrder != null)
                {
                    for (int ti = 0; ti < step.taskOrder.Length; ti++)
                    {
                        var e = step.taskOrder[ti];
                        if (e != null && string.Equals(e.kind, "confirm_action", StringComparison.Ordinal))
                        { hasConfirm = true; break; }
                    }
                }
                if (hasConfirm) continue;

                var entry = new TaskOrderEntry { kind = "confirm_action", id = "confirm" };
                if (step.taskOrder == null || step.taskOrder.Length == 0)
                {
                    step.taskOrder = new[] { entry };
                }
                else
                {
                    var augmented = new TaskOrderEntry[step.taskOrder.Length + 1];
                    Array.Copy(step.taskOrder, augmented, step.taskOrder.Length);
                    augmented[step.taskOrder.Length] = entry;
                    step.taskOrder = augmented;
                }
                added++;
                OseLog.VerboseInfo($"[TaskOrder.Normalize] step '{step.id}': appended missing confirm_action (Confirm-family step needs a Continue button task to complete).");
            }

            if (added > 0)
            {
                OseLog.Warn($"[TaskOrder.Normalize] Added missing confirm_action to {added} Confirm-family step(s). Author should add `kind: confirm_action` to taskOrder on every Confirm step that ends with a Continue button press.");
            }
        }

        /// <summary>
        /// Use-family steps that drive a "tighten" tool action need
        /// <c>profile = "Torque"</c> so the runtime descriptor sets
        /// <c>PartFollowsTool = true</c> — without it,
        /// <see cref="UI.Coordination.ToolActionExecutor"/>'s BuildPartEffect
        /// gate at "fail-closed: only build a PartEffect when the profile
        /// opts in" returns null, the LerpPosePartEffect is never
        /// instantiated, and the trainee sees the bolt jump from start to
        /// completed with no animation.
        ///
        /// CLAUDE.md documents the convention ("Tighten steps include
        /// `profile: 'Torque'`") but it's an authoring rule that's easy to
        /// forget. Auto-fill it once at load so every existing and future
        /// tighten step gets the lerp visual without manual JSON edits.
        /// </summary>
        private static void EnsureProfileForUseTightenSteps(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            int filled = 0;
            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step == null) continue;
                if (step.ResolvedFamily != StepFamily.Use) continue;
                if (!string.IsNullOrEmpty(step.profile)) continue;
                if (step.requiredToolActions == null || step.requiredToolActions.Length == 0) continue;

                bool anyTighten = false;
                for (int ai = 0; ai < step.requiredToolActions.Length; ai++)
                {
                    var a = step.requiredToolActions[ai];
                    if (a == null) continue;
                    if (string.Equals(a.actionType, "tighten", StringComparison.OrdinalIgnoreCase))
                    { anyTighten = true; break; }
                }
                if (!anyTighten) continue;

                step.profile = "Torque";
                filled++;
                OseLog.VerboseInfo($"[Profile.Normalize] step '{step.id}' (seq {step.sequenceIndex}): inferred profile='Torque' (family=Use + actionType=tighten). Author should set this explicitly in JSON.");
            }

            if (filled > 0)
            {
                OseLog.Warn($"[Profile.Normalize] Auto-inferred profile='Torque' on {filled} Use-family tighten step(s). Without it, ToolActionExecutor skips the LerpPosePartEffect and the bolt visibly jumps to its end pose without animation.");
            }
        }

        /// <summary>
        /// Rewrites <see cref="TaskOrderEntry.kind"/> = <c>"target"</c> entries
        /// to <c>"toolAction"</c> when the step has a
        /// <see cref="StepDefinition.requiredToolActions"/> entry whose
        /// <c>targetId</c> matches the task entry's <c>id</c>. The entry's
        /// <c>id</c> is replaced with the matching action's id so the runtime
        /// <see cref="TaskCursor"/> and <c>ToolRuntimeController</c> see the
        /// same identity the completion-notify path uses.
        ///
        /// <para>Why: TTAW's <c>GetOrDeriveTaskOrder</c> writes <c>kind="target"</c>
        /// for every entry in <c>step.targetIds</c> on Use/Connect/Weld steps,
        /// then skips a paired <c>kind="toolAction"</c> entry because the
        /// target is already covered. That choice was fine before Phase I.d,
        /// but the cursor now drives tool-action availability and advancement,
        /// and it only notifies on <c>kind="toolAction"</c>. A step authored
        /// with <c>kind="target"</c> stalls: the user fires the action, the
        /// controller calls <c>cursor.NotifyTaskCompleted("toolAction", …)</c>,
        /// no match, cursor never advances, trainee is locked to the first
        /// target forever.</para>
        ///
        /// <para>Structural prevention: normalize at load time, keep
        /// <c>unorderedSet</c> / <c>isOptional</c> / <c>endTransform</c>
        /// intact. Also emit a warning so authors can clean up the source
        /// eventually — but content continues to play correctly in the
        /// meantime. Entries with no matching action are left alone (they
        /// may belong to a Confirm-family step with its own semantics).</para>
        /// </summary>
        private static void NormalizeTaskOrderToolActionKinds(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            int totalStepsTouched = 0;
            int totalEntriesRewritten = 0;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step?.taskOrder == null || step.taskOrder.Length == 0) continue;
                var actions = step.requiredToolActions;
                if (actions == null || actions.Length == 0) continue;

                int rewritten = 0;
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var entry = step.taskOrder[ti];
                    if (entry == null) continue;
                    if (!string.Equals(entry.kind, "target", StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(entry.id)) continue;

                    ToolActionDefinition match = null;
                    for (int ai = 0; ai < actions.Length; ai++)
                    {
                        var a = actions[ai];
                        if (a == null || string.IsNullOrEmpty(a.id)) continue;
                        if (!string.Equals(a.targetId, entry.id, StringComparison.Ordinal)) continue;
                        match = a;
                        break;
                    }
                    if (match == null) continue;

                    entry.kind = "toolAction";
                    entry.id   = match.id;
                    rewritten++;
                }

                if (rewritten > 0)
                {
                    totalStepsTouched++;
                    totalEntriesRewritten += rewritten;
                    // Per-step detail is Verbose (gated) to avoid console spam
                    // on every load. One summary Warn is emitted below.
                    OseLog.VerboseInfo($"[TaskOrder.Normalize] step '{step.id}': rewrote {rewritten} kind='target' entr{(rewritten == 1 ? "y" : "ies")} to kind='toolAction'.");
                }
            }

            if (totalStepsTouched > 0)
            {
                OseLog.Warn($"[TaskOrder.Normalize] Auto-rewrote kind='target'→'toolAction' on {totalEntriesRewritten} entr{(totalEntriesRewritten == 1 ? "y" : "ies")} across {totalStepsTouched} step{(totalStepsTouched == 1 ? "" : "s")}. Set OseLog.Verbose=true for per-step detail, or update authoring source to emit kind='toolAction' directly.");
            }
        }

        /// <summary>
        /// Guarantees that <see cref="StepDefinition.taskOrder"/> covers every
        /// runtime-completion-gated requirement declared on the step. Missing
        /// entries are appended in the order they appear in the requirement
        /// arrays; existing entries are left alone (preserves author-specified
        /// sequence + <c>unorderedSet</c> labels + <c>endTransform</c>).
        ///
        /// <para>Why: the <see cref="TaskCursor"/> completion gate is the only
        /// runtime path that advances a step past its first span. It only
        /// notifies on <c>(kind, id)</c> tuples that appear in <c>taskOrder</c>.
        /// When authored content declares <c>requiredToolActions</c> or
        /// <c>requiredPartIds</c> but forgets to add matching <c>taskOrder</c>
        /// entries, the step deadlocks: placement handlers refuse to complete
        /// the step (tool actions still pending), and the tool controller
        /// refuses to dispatch the actions (cursor never opens them).</para>
        ///
        /// <para>Seen 2026-04-19 on <c>step_place_upper_corner_brackets</c>
        /// (seq 43): taskOrder had the 4 Part entries but zero toolAction
        /// entries. User got stuck clicking a tool target that never completed.
        /// Prior instances of the same deadlock shape on steps 4/27 had
        /// different triggers (wrong kind, stale PartEffect) — the common
        /// failure is "cursor doesn't know about something the step requires."
        /// This pass is a fail-closed guarantee: every required task is
        /// visible to the cursor, regardless of how the taskOrder was
        /// authored.</para>
        ///
        /// <para>Warnings are logged so authors can clean up content upstream,
        /// but the runtime doesn't wait — the step plays correctly on load.</para>
        /// </summary>
        private static void EnsureTaskOrderCoversRequirements(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            int totalStepsTouched = 0;
            int totalEntriesAppended = 0;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step == null) continue;

                // Build the set of (kind, id) tuples already represented.
                var existing = new HashSet<string>(StringComparer.Ordinal);
                if (step.taskOrder != null)
                {
                    for (int ti = 0; ti < step.taskOrder.Length; ti++)
                    {
                        var e = step.taskOrder[ti];
                        if (e == null || string.IsNullOrEmpty(e.kind) || string.IsNullOrEmpty(e.id)) continue;
                        existing.Add(e.kind + ":" + e.id);
                    }
                }

                var missing = new List<TaskOrderEntry>();

                // Required part tasks — cursor gates placement completion on these.
                var requiredParts = step.requiredPartIds;
                if (requiredParts != null)
                {
                    for (int pi = 0; pi < requiredParts.Length; pi++)
                    {
                        string pid = requiredParts[pi];
                        if (string.IsNullOrEmpty(pid)) continue;
                        // Match is on bare partId OR an instance id (partId#N). Any
                        // existing "part:" entry whose ToPartId == pid satisfies
                        // the requirement — don't add a duplicate.
                        bool covered = false;
                        if (step.taskOrder != null)
                        {
                            for (int ti = 0; ti < step.taskOrder.Length && !covered; ti++)
                            {
                                var e = step.taskOrder[ti];
                                if (e == null || !string.Equals(e.kind, "part", StringComparison.Ordinal)) continue;
                                if (string.IsNullOrEmpty(e.id)) continue;
                                if (string.Equals(TaskInstanceId.ToPartId(e.id), pid, StringComparison.Ordinal))
                                    covered = true;
                            }
                        }
                        if (!covered)
                            missing.Add(new TaskOrderEntry { kind = "part", id = pid });
                    }
                }

                // Required tool actions — cursor gates tool-action execution on these.
                var requiredActions = step.requiredToolActions;
                if (requiredActions != null)
                {
                    for (int ai = 0; ai < requiredActions.Length; ai++)
                    {
                        var a = requiredActions[ai];
                        if (a == null || string.IsNullOrEmpty(a.id)) continue;
                        if (existing.Contains("toolAction:" + a.id)) continue;
                        missing.Add(new TaskOrderEntry { kind = "toolAction", id = a.id });
                    }
                }

                if (missing.Count == 0) continue;

                // Append in declaration order. Authors can re-order in source
                // if they want a different sequence; we just guarantee presence.
                var combined = new List<TaskOrderEntry>(step.taskOrder?.Length + missing.Count ?? missing.Count);
                if (step.taskOrder != null) combined.AddRange(step.taskOrder);
                combined.AddRange(missing);
                step.taskOrder = combined.ToArray();

                totalStepsTouched++;
                totalEntriesAppended += missing.Count;
                // Per-step Warn so authors immediately see WHICH step had its
                // taskOrder auto-fixed. The console gets one Warn per affected
                // step plus the rollup Warn below — slightly noisy when many
                // steps need fixing, but that's the point: silent auto-fix
                // masked the step 83 deadlock for a long time. Update source
                // so taskOrder explicitly covers requirements and these Warns
                // disappear.
                OseLog.Warn($"[TaskOrder.Normalize] step '{step.id}': appended {missing.Count} missing taskOrder entr{(missing.Count == 1 ? "y" : "ies")} — update authoring source to remove this auto-fix.");
            }

            if (totalStepsTouched > 0)
            {
                OseLog.Warn($"[TaskOrder.Normalize] Auto-appended {totalEntriesAppended} missing taskOrder entr{(totalEntriesAppended == 1 ? "y" : "ies")} across {totalStepsTouched} step{(totalStepsTouched == 1 ? "" : "s")} to prevent cursor deadlock. Set OseLog.Verbose=true for per-step detail, or update authoring source so taskOrder reflects every requiredPart/requiredToolAction explicitly.");
            }
        }

        /// <summary>
        /// Marks <c>kind:"part"</c> <see cref="TaskOrderEntry"/> entries as
        /// <c>isOptional=true</c> when their id appears only in
        /// <c>step.visualPartIds</c> — TTAW calls these "NO TASK" rows. The
        /// runtime <see cref="TaskCursor"/> has no visualPartIds awareness;
        /// without this pass it opens the first span on the visual-only
        /// entry, waits forever for a "completion" the user cannot deliver,
        /// and never advances to the spans that carry the actual required
        /// tasks. Marking them optional lets the cursor auto-skip (required
        /// count = 0 → span closes immediately) so the real tasks become
        /// active. No-op on entries already optional or on entries whose id
        /// is also in requiredPartIds / optionalPartIds (membership win
        /// order: required &gt; optional &gt; visual-only).
        /// </summary>
        private static void MarkVisualOnlyTaskOrderEntriesOptional(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step?.taskOrder == null || step.taskOrder.Length == 0) continue;
                if (step.visualPartIds == null || step.visualPartIds.Length == 0) continue;

                var visual = new HashSet<string>(step.visualPartIds, StringComparer.Ordinal);
                var required = step.requiredPartIds != null
                    ? new HashSet<string>(step.requiredPartIds, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
                var optional = step.optionalPartIds != null
                    ? new HashSet<string>(step.optionalPartIds, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);

                int marked = 0;
                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var e = step.taskOrder[ti];
                    if (e == null || !string.Equals(e.kind, "part", StringComparison.Ordinal)) continue;
                    if (e.isOptional) continue;
                    if (string.IsNullOrEmpty(e.id)) continue;

                    string partId = TaskInstanceId.ToPartId(e.id);
                    if (string.IsNullOrEmpty(partId)) continue;
                    if (required.Contains(partId)) continue;
                    if (optional.Contains(partId)) continue;
                    if (!visual.Contains(partId)) continue;

                    e.isOptional = true;
                    marked++;
                }

                if (marked > 0)
                    OseLog.VerboseInfo($"[TaskOrder.Normalize] step '{step.id}': marked {marked} visual-only taskOrder entr{(marked == 1 ? "y" : "ies")} isOptional=true so the cursor auto-advances past them (they're NO TASK visual markers, not actionable).");
            }
        }

        /// <summary>
        /// Catches the "family=Use with unplaced requiredPartIds" authoring
        /// bug at load time. A Use-family step routes interactions through
        /// <c>UseStepHandler</c> — there's no placement handler active, so
        /// any Part task in <c>taskOrder</c> will never transition to
        /// <c>PlacedVirtually</c>, the cursor stalls on its first Part span,
        /// and subsequent toolAction spans never open. Users experience
        /// this as "tool target does nothing when clicked."
        ///
        /// <para>A part is considered placed-prior if any step with a smaller
        /// <c>sequenceIndex</c> listed it in <c>requiredPartIds</c> under
        /// family=Place. Use-family steps that depend on parts never placed
        /// by a prior Place step are reported as errors — the authoring fix
        /// is either (a) change the step's family to Place (if the intent is
        /// to place these parts here), or (b) add a prior Place step that
        /// introduces them.</para>
        ///
        /// <para>Seen 2026-04-19 on step 43 (step_place_upper_corner_brackets)
        /// — should have been family=Place like its sibling step 41
        /// (step_place_lower_corner_brackets) but was mis-authored as
        /// family=Use+profile=Torque. Deadlocked on first tool target click.
        /// Validator log would have caught this at load before Play.</para>
        /// </summary>
        private static void ValidateUseFamilyPartsArePrePlaced(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            // Steps are not guaranteed sorted; clone + sort by sequenceIndex.
            var sorted = new List<StepDefinition>(package.steps.Length);
            for (int i = 0; i < package.steps.Length; i++)
                if (package.steps[i] != null) sorted.Add(package.steps[i]);
            sorted.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

            var placedBefore = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < sorted.Count; i++)
            {
                var step = sorted[i];
                string family = (step.family ?? string.Empty).Trim();

                if (string.Equals(family, "Use", StringComparison.OrdinalIgnoreCase))
                {
                    var required = step.requiredPartIds;
                    if (required != null && required.Length > 0)
                    {
                        List<string> unplaced = null;
                        for (int r = 0; r < required.Length; r++)
                        {
                            string pid = required[r];
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!placedBefore.Contains(pid))
                                (unplaced ??= new List<string>()).Add(pid);
                        }
                        if (unplaced != null)
                        {
                            OseLog.Error($"[Validate.UseParts] step '{step.id}' (seq {step.sequenceIndex}, family=Use) declares requiredPartIds that no prior family=Place step placed: {string.Join(", ", unplaced)}. Trainee cannot complete this step — Use-family routes interactions through UseStepHandler, which does not place parts. Either change the family to Place, or add a prior Place step that introduces these parts.");
                        }
                    }
                }

                // Accumulate: Place-family steps contribute their requiredPartIds
                // to the placed-before set for subsequent step checks.
                if (string.Equals(family, "Place", StringComparison.OrdinalIgnoreCase))
                {
                    var placed = step.requiredPartIds;
                    if (placed != null)
                    {
                        for (int r = 0; r < placed.Length; r++)
                        {
                            if (!string.IsNullOrEmpty(placed[r]))
                                placedBefore.Add(placed[r]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates Phase I.a's <c>unorderedSet</c> label semantics on
        /// <see cref="StepDefinition.taskOrder"/>:
        /// <list type="number">
        ///   <item><b>Contiguity</b> — entries sharing a non-empty label must
        ///   be adjacent. A label re-appearing after a different label (or
        ///   null) constitutes a second span, which is forbidden.</item>
        ///   <item><b>Kind purity</b> — all entries in a set share the same
        ///   <see cref="TaskOrderEntry.kind"/> (e.g. all "part", or all
        ///   "toolAction"). Mixed kinds break the runtime controller contract
        ///   Phase I.c / I.d depend on.</item>
        ///   <item><b>Single-span per label</b> — subsumed by contiguity; a
        ///   label that tries to start a second span after closing is flagged
        ///   as the contiguity error.</item>
        ///   <item><b>Single-member warning</b> — an unordered set with just
        ///   one member is an authoring smell; warn but do not block.</item>
        /// </list>
        /// Errors use <see cref="Debug.LogError"/>; single-member warnings
        /// use <see cref="Debug.LogWarning"/>. Does not throw — load succeeds
        /// with console feedback so authors see the issue before Play.
        /// Phase I.a is spec + validation only; no runtime path consumes the
        /// field yet. Future wiring (I.c / I.d) assumes these invariants hold.
        /// </summary>
        private static void ValidateUnorderedSets(MachinePackageDefinition package)
        {
            if (package?.steps == null) return;

            for (int si = 0; si < package.steps.Length; si++)
            {
                var step = package.steps[si];
                if (step?.taskOrder == null || step.taskOrder.Length == 0) continue;

                var closedLabels = new HashSet<string>(StringComparer.Ordinal);
                string currentLabel = null;
                string currentKind  = null;
                int    currentSize  = 0;

                for (int ti = 0; ti < step.taskOrder.Length; ti++)
                {
                    var entry = step.taskOrder[ti];
                    string label = string.IsNullOrEmpty(entry?.unorderedSet) ? null : entry.unorderedSet;

                    if (string.Equals(label, currentLabel, StringComparison.Ordinal))
                    {
                        if (label != null)
                        {
                            if (!string.Equals(entry.kind, currentKind, StringComparison.Ordinal))
                            {
                                OseLog.Error($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{label}' mixes kinds ('{currentKind}' and '{entry.kind}'). Sets must be kind-pure.");
                            }
                            currentSize++;
                        }
                        continue;
                    }

                    // Span transition: close the previous span, open a new one.
                    if (currentLabel != null)
                    {
                        closedLabels.Add(currentLabel);
                        if (currentSize == 1)
                        {
                            OseLog.Warn($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{currentLabel}' has only 1 member — drop the label or add siblings.");
                        }
                    }

                    if (label != null)
                    {
                        if (closedLabels.Contains(label))
                        {
                            OseLog.Error($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{label}' reappears as a non-contiguous span. Entries with the same label must be adjacent.");
                        }
                        currentLabel = label;
                        currentKind  = entry?.kind;
                        currentSize  = 1;
                    }
                    else
                    {
                        currentLabel = null;
                        currentKind  = null;
                        currentSize  = 0;
                    }
                }

                // Trailing span check for single-member warning.
                if (currentLabel != null && currentSize == 1)
                {
                    OseLog.Warn($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{currentLabel}' has only 1 member — drop the label or add siblings.");
                }
            }
        }

        private const string SynthesizedStepPoseLabelPrefix = "synthesized:holdAtEnd";

        /// <summary>
        /// Dedicated label prefix for group-level synthesized stepPoses emitted
        /// onto <see cref="PartGroupPreviewPlacement.stepPoses"/> by the
        /// Phase-B refactor. Carefully chosen to NOT share a common prefix
        /// with the legacy per-member prefix — strips key off StartsWith, and
        /// the legacy strip (<c>SynthesizedStepPoseLabelPrefix</c>) must not
        /// accidentally catch the new group entries, nor vice versa.
        /// </summary>
        public const string SynthesizedGroupStepPoseLabelPrefix = "synthGroup:holdAtEnd";

        /// <summary>
        /// Bakes the end-state of every <c>poseTransition</c> cue with
        /// <c>holdAtEnd=true</c> into per-member <see cref="StepPoseEntry"/>
        /// records on each member's <see cref="PartPreviewPlacement.stepPoses"/>.
        /// The synthesized entry is anchored to the step immediately AFTER
        /// the cue's step so the cue still animates from the authored
        /// baseline at its own step; forward-propagation carries the pose
        /// to every subsequent step until an authored stepPose or later
        /// cue supersedes.
        ///
        /// Crucially, member baseline positions and the rotation pivot
        /// (centroid) are resolved via <see cref="PoseResolver.Resolve"/>
        /// at the cue's step — the exact same source the runtime player's
        /// <c>ComputeChildrenCentroidLocal</c> sees — so synthesized poses
        /// match what the player would produce at <c>easedT=1</c>
        /// (<see cref="PoseTransitionPlayer.Tick"/> multi-child branch,
        /// formula: <c>final = C + deltaRot * (baseline - C)</c>).
        ///
        /// Idempotent: strips prior synthesized entries (label prefix
        /// <see cref="SynthesizedStepPoseLabelPrefix"/>) before writing.
        /// Authored stepPoses whose propagation span covers the synthesized
        /// anchor are preserved and cause synthesis to skip that member
        /// with a warning.
        /// </summary>
        private static void BakeHoldAtEndEndPoses(MachinePackageDefinition package)
        {
            if (package?.previewConfig == null) return;

            // Lookup for part placements (write target).
            var placementByPart = new Dictionary<string, PartPreviewPlacement>(StringComparer.Ordinal);
            if (package.previewConfig.partPlacements != null)
                foreach (var pp in package.previewConfig.partPlacements)
                    if (pp != null && !string.IsNullOrEmpty(pp.partId))
                        placementByPart[pp.partId] = pp;

            // Strip prior synthesized entries so the pass is idempotent. This
            // now removes BOTH the legacy per-member entries (Phase-B
            // migration: the group-centric model replaces them) and any prior
            // group synthesized entries from earlier runs in this load.
            foreach (var pp in placementByPart.Values)
                pp.stepPoses = StripSynthesizedStepPoses(pp.stepPoses);

            // Lookup / ensure PartGroupPreviewPlacement entries so the
            // baker can write group stepPoses onto them. Absent placements
            // are materialised on the fly — cue-bearing partGroups may
            // not have authored a placement entry yet.
            var placementBySub = EnsurePartGroupPlacements(package);
            foreach (var sp in placementBySub.Values)
                sp.stepPoses = StripSynthesizedGroupStepPoses(sp.stepPoses);

            // Resolver index for effective-pose lookups. Cycle-free — only
            // reads package, placements, and partGroup membership; does
            // not touch poseTable.
            var idx = new PoseResolverIndex(package);

            // seqByStepId mirror for overlap-check against existing spans.
            var seqByStepId = new Dictionary<string, int>(StringComparer.Ordinal);
            if (package.steps != null)
                foreach (var s in package.steps)
                    if (s != null && !string.IsNullOrEmpty(s.id))
                        seqByStepId[s.id] = s.sequenceIndex;

            var orderedSteps = package.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0) return;

            int synthesizedOnSubs  = 0;
            int synthesizedOnParts = 0;

            // ── PartGroup-hosted cues ──
            var subs = package.GetPartGroups();
            if (subs != null)
            {
                for (int si = 0; si < subs.Length; si++)
                {
                    var sub = subs[si];
                    if (sub?.animationCues == null || sub.animationCues.Length == 0) continue;
                    if (sub.partIds == null || sub.partIds.Length == 0) continue;
                    synthesizedOnSubs += SynthesizeGroupHoldAtEnd(
                        package, sub, idx, orderedSteps, seqByStepId, placementByPart, placementBySub);
                }
            }

            // ── Part-hosted cues ──
            if (package.parts != null)
            {
                for (int pi = 0; pi < package.parts.Length; pi++)
                {
                    var part = package.parts[pi];
                    if (part?.animationCues == null || part.animationCues.Length == 0) continue;
                    if (!placementByPart.TryGetValue(part.id, out var placement) || placement == null) continue;
                    synthesizedOnParts += SynthesizePartHoldAtEnd(
                        package, part, placement, idx, orderedSteps, seqByStepId);
                }
            }

            int total = synthesizedOnSubs + synthesizedOnParts;
            if (total > 0)
                OseLog.Info($"[CueRuntime.BakeHoldAtEnd] synthesized {total} stepPose(s) in '{package.packageId}' ({synthesizedOnSubs} from group cues, {synthesizedOnParts} from part cues).");
        }

        /// <summary>
        /// Phase-B group-centric bake. Instead of fanning the cue's rotation
        /// into N per-member <see cref="StepPoseEntry"/> rows, emit a single
        /// <see cref="StepPoseEntry"/> onto the partGroup's
        /// <see cref="PartGroupPreviewPlacement.stepPoses"/>. At resolve
        /// time <c>PoseResolver.ApplyGroupStepPose</c> composes the group's
        /// transform onto each member's per-part pose.
        ///
        /// <para>Encoding: a pivot-based rotation <c>final = C + R*(p - C)</c>
        /// is equivalent to a free transform <c>final = R*p + (C - R*C)</c>.
        /// So we encode <c>groupRot = R</c> and <c>groupPos = C - R*C</c>.
        /// Composition reproduces the same world pose for every member,
        /// regardless of which member (pure math — no per-member data needed).</para>
        /// </summary>
        private static int SynthesizeGroupHoldAtEnd(
            MachinePackageDefinition package,
            PartGroupDefinition sub,
            PoseResolverIndex idx,
            StepDefinition[] orderedSteps,
            Dictionary<string, int> seqByStepId,
            Dictionary<string, PartPreviewPlacement> placementByPart,
            Dictionary<string, PartGroupPreviewPlacement> placementBySub)
        {
            int count = 0;

            if (!placementBySub.TryGetValue(sub.id, out var subPlacement) || subPlacement == null)
                return 0;

            for (int ci = 0; ci < sub.animationCues.Length; ci++)
            {
                var cue = sub.animationCues[ci];
                if (cue == null) continue;
                if (!string.Equals(cue.type, "poseTransition", StringComparison.Ordinal)) continue;
                if (!cue.holdAtEnd) continue;
                if (cue.stepIds == null || cue.stepIds.Length == 0)
                {
                    OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] partGroup '{sub.id}' cue[{ci}]: holdAtEnd=true but empty stepIds — skipping.");
                    continue;
                }
                if (cue.toPose == null || IsZeroQuaternion(cue.toPose.rotation))
                {
                    OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] partGroup '{sub.id}' cue[{ci}]: toPose.rotation is zero quaternion — skipping.");
                    continue;
                }

                Quaternion toRot   = QuatFrom(cue.toPose.rotation);
                Quaternion fromRot = cue.fromPose != null && !IsZeroQuaternion(cue.fromPose.rotation)
                    ? QuatFrom(cue.fromPose.rotation)
                    : Quaternion.identity;
                Quaternion deltaRot = toRot * Quaternion.Inverse(fromRot);

                for (int si = 0; si < cue.stepIds.Length; si++)
                {
                    string cueStepId = cue.stepIds[si];
                    if (string.IsNullOrEmpty(cueStepId)) continue;
                    if (!package.TryGetStep(cueStepId, out var cueStep) || cueStep == null) continue;

                    StepDefinition nextStep = null;
                    for (int k = 0; k < orderedSteps.Length; k++)
                    {
                        if (orderedSteps[k].sequenceIndex > cueStep.sequenceIndex)
                        { nextStep = orderedSteps[k]; break; }
                    }
                    if (nextStep == null)
                    {
                        OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] partGroup '{sub.id}' cue[{ci}] anchors to final step '{cueStepId}' — no next step to persist into.");
                        continue;
                    }

                    // Centroid computation must match PivotCentroidResolver
                    // — the runtime's pivot source. Both filter:
                    //   (1) hidden members (not visible at cueStep),
                    //   (2) at-origin members (not yet positioned),
                    //   (3) members introduced THIS step — their
                    //       firstVisibleSeq == cueStep.sequenceIndex means
                    //       they sit at startPosition (tray/staging), not
                    //       on the established body. Including them drags
                    //       the pivot toward the tray, producing the
                    //       "group jumps on next step" bug: animation uses
                    //       body-only centroid, synthesized stepPose uses
                    //       centroid-polluted-by-staging → same rotation
                    //       around different pivots, visible jump on the
                    //       step the hold-at-end persists into.
                    //
                    // Body-only centroid matches PivotCentroidResolver's
                    // filter, which is what the runtime's
                    // PoseTransitionPlayer uses as the rotation pivot.
                    // When no body members exist (every member is being
                    // introduced THIS step), runtime's PivotHint is null
                    // and PoseTransitionPlayer falls back to Vector3.zero
                    // (group-root-local origin) — so the baker must do
                    // the same to keep the runtime animation's end frame
                    // identical to the synthesized step-N+1 pose.
                    Vector3 bodyCentroid = Vector3.zero;
                    int     bodyCount    = 0;
                    bool    anyVisible   = false;
                    for (int mi = 0; mi < sub.partIds.Length; mi++)
                    {
                        string memberId = sub.partIds[mi];
                        if (string.IsNullOrEmpty(memberId)) continue;
                        if (!placementByPart.ContainsKey(memberId)) continue;
                        var res = PoseResolver.Resolve(
                            memberId, cueStep.sequenceIndex, package, idx, PoseMode.Committed);
                        if (res.IsHidden) continue;
                        if (res.pos.sqrMagnitude < 0.0001f) continue;
                        anyVisible = true;

                        bool isEstablishedBody =
                            !idx.firstVisibleSeqByPart.TryGetValue(memberId, out int memberFirstSeq)
                            || memberFirstSeq < cueStep.sequenceIndex;
                        if (isEstablishedBody)
                        {
                            bodyCentroid += res.pos;
                            bodyCount++;
                        }
                    }
                    if (!anyVisible)
                    {
                        OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] partGroup '{sub.id}' cue[{ci}] at '{cueStepId}': no non-origin members visible — skipping group synthesis.");
                        continue;
                    }
                    Vector3 centroid;
                    if (bodyCount > 0)
                        centroid = bodyCentroid / bodyCount;
                    else
                    {
                        // No established body members at this step —
                        // runtime uses Vector3.zero as pivot; match it.
                        centroid = Vector3.zero;
                        OseLog.VerboseInfo($"[CueRuntime.BakeHoldAtEnd] partGroup '{sub.id}' cue[{ci}] at '{cueStepId}': no established body members (all introduced this step); using Vector3.zero pivot to match runtime fallback.");
                    }

                    // Compose with any prior group stepPose covering cueStep —
                    // handles stacked hold-at-end cues. Runtime applies each
                    // cue's deltaRot to the current baselines (which include
                    // prior cues' accumulated state). ApplyGroupStepPose at
                    // resolve-time picks the closest-anchor span, so if we
                    // encoded only this cue's deltaRot, step N+1 would show
                    // just deltaRot applied to authored base — NOT the runtime
                    // end state which is deltaRot composed with the prior
                    // state. Composition formula:
                    //   runtime_end = C + dRot*(prior_composed - C)
                    //   prior_composed = priorPos + priorRot*base
                    //   → net = (C - dRot*C + dRot*priorPos) + (dRot*priorRot)*base
                    // So newRot = dRot*priorRot, newPos = (C - dRot*C) + dRot*priorPos.
                    Quaternion priorRot = Quaternion.identity;
                    Vector3    priorPos = Vector3.zero;
                    if (subPlacement.stepPoses != null)
                    {
                        int bestDist = int.MaxValue;
                        StepPoseEntry bestEntry = null;
                        for (int spi = 0; spi < subPlacement.stepPoses.Length; spi++)
                        {
                            var sp = subPlacement.stepPoses[spi];
                            if (sp == null) continue;
                            int anchorSeq = seqByStepId.TryGetValue(sp.stepId ?? "", out int a) ? a : int.MinValue;
                            int fromSeq = string.IsNullOrEmpty(sp.propagateFromStep)
                                ? (anchorSeq >= 0 ? anchorSeq : int.MinValue)
                                : (seqByStepId.TryGetValue(sp.propagateFromStep, out int f) ? f : int.MinValue);
                            int throughSeq = string.IsNullOrEmpty(sp.propagateThroughStep)
                                ? int.MaxValue
                                : (seqByStepId.TryGetValue(sp.propagateThroughStep, out int t) ? t : int.MaxValue);
                            if (cueStep.sequenceIndex < fromSeq || cueStep.sequenceIndex > throughSeq) continue;
                            int dist = anchorSeq >= 0 ? Mathf.Abs(cueStep.sequenceIndex - anchorSeq) : int.MaxValue / 2;
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestEntry = sp;
                            }
                        }
                        if (bestEntry != null)
                        {
                            priorRot = QuatFrom(bestEntry.rotation);
                            priorPos = new Vector3(bestEntry.position.x, bestEntry.position.y, bestEntry.position.z);
                        }
                    }

                    Quaternion groupRot = deltaRot * priorRot;
                    Vector3    groupPos = (centroid - deltaRot * centroid) + deltaRot * priorPos;

                    var synth = new StepPoseEntry
                    {
                        stepId = nextStep.id,
                        label  = $"{SynthesizedGroupStepPoseLabelPrefix} (sub={sub.id} cue={cue.type}[{ci}])",
                        position = new SceneFloat3 { x = groupPos.x, y = groupPos.y, z = groupPos.z },
                        rotation = new SceneQuaternion { x = groupRot.x, y = groupRot.y, z = groupRot.z, w = groupRot.w },
                        scale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                        propagateFromStep    = "",
                        propagateThroughStep = "",
                    };
                    subPlacement.stepPoses = AppendStepPose(subPlacement.stepPoses, synth);
                    count++;
                }
            }
            return count;
        }

        private static int SynthesizePartHoldAtEnd(
            MachinePackageDefinition package,
            PartDefinition part,
            PartPreviewPlacement placement,
            PoseResolverIndex idx,
            StepDefinition[] orderedSteps,
            Dictionary<string, int> seqByStepId)
        {
            int count = 0;

            for (int ci = 0; ci < part.animationCues.Length; ci++)
            {
                var cue = part.animationCues[ci];
                if (cue == null) continue;
                if (!string.Equals(cue.type, "poseTransition", StringComparison.Ordinal)) continue;
                if (!cue.holdAtEnd) continue;
                if (cue.stepIds == null || cue.stepIds.Length == 0)
                {
                    OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}]: holdAtEnd=true but empty stepIds — skipping.");
                    continue;
                }
                if (cue.toPose == null || IsZeroQuaternion(cue.toPose.rotation))
                {
                    OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}]: toPose.rotation is zero quaternion — skipping.");
                    continue;
                }

                // Part-hosted cue → single target (the part itself). Runtime
                // single-part branch lerps position/rotation directly to
                // toPose, so the end state is literally toPose composed
                // with the part's current pose for the from side.
                // With default fromPose = current (PoseResolver at cueStep),
                // end state = toPose literal for position/rotation/scale.
                for (int si = 0; si < cue.stepIds.Length; si++)
                {
                    string cueStepId = cue.stepIds[si];
                    if (string.IsNullOrEmpty(cueStepId)) continue;
                    if (!package.TryGetStep(cueStepId, out var cueStep) || cueStep == null) continue;

                    StepDefinition nextStep = null;
                    for (int k = 0; k < orderedSteps.Length; k++)
                    {
                        if (orderedSteps[k].sequenceIndex > cueStep.sequenceIndex)
                        { nextStep = orderedSteps[k]; break; }
                    }
                    if (nextStep == null)
                    {
                        OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}] anchors to final step '{cueStepId}' — no next step to persist into.");
                        continue;
                    }
                    int anchorSeq = nextStep.sequenceIndex;

                    if (AnyAuthoredSpanCovers(placement.stepPoses, anchorSeq, seqByStepId))
                    {
                        OseLog.Warn($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' has authored stepPose covering step '{nextStep.id}' (seq {anchorSeq}) — skipping synthesis (authored wins).");
                        continue;
                    }

                    var synth = new StepPoseEntry
                    {
                        stepId = nextStep.id,
                        label  = $"{SynthesizedStepPoseLabelPrefix} (part={part.id} cue={cue.type}[{ci}])",
                        position = cue.toPose.position,
                        rotation = cue.toPose.rotation,
                        scale    = IsZeroOrNearZeroScale(cue.toPose.scale)
                                     ? new SceneFloat3 { x = 1f, y = 1f, z = 1f }
                                     : cue.toPose.scale,
                        propagateFromStep    = "",
                        propagateThroughStep = "",
                    };
                    placement.stepPoses = AppendStepPose(placement.stepPoses, synth);
                    count++;
                }
            }
            return count;
        }

        private static StepPoseEntry[] StripSynthesizedStepPoses(StepPoseEntry[] arr)
        {
            if (arr == null || arr.Length == 0) return arr;
            int kept = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) continue;
                if (arr[i].label != null && arr[i].label.StartsWith(SynthesizedStepPoseLabelPrefix, StringComparison.Ordinal)) continue;
                kept++;
            }
            if (kept == arr.Length) return arr;
            var next = new StepPoseEntry[kept];
            int w = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) continue;
                if (arr[i].label != null && arr[i].label.StartsWith(SynthesizedStepPoseLabelPrefix, StringComparison.Ordinal)) continue;
                next[w++] = arr[i];
            }
            return next;
        }

        /// <summary>
        /// Strips previously-synthesized GROUP stepPoses
        /// (<see cref="SynthesizedGroupStepPoseLabelPrefix"/>) from a
        /// <see cref="PartGroupPreviewPlacement.stepPoses"/> array so the
        /// Phase-B bake is idempotent. Author-written group stepPoses are
        /// preserved (they use neither prefix).
        /// </summary>
        private static StepPoseEntry[] StripSynthesizedGroupStepPoses(StepPoseEntry[] arr)
        {
            if (arr == null || arr.Length == 0) return arr;
            int kept = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) continue;
                if (arr[i].label != null && arr[i].label.StartsWith(SynthesizedGroupStepPoseLabelPrefix, StringComparison.Ordinal)) continue;
                kept++;
            }
            if (kept == arr.Length) return arr;
            var next = new StepPoseEntry[kept];
            int w = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) continue;
                if (arr[i].label != null && arr[i].label.StartsWith(SynthesizedGroupStepPoseLabelPrefix, StringComparison.Ordinal)) continue;
                next[w++] = arr[i];
            }
            return next;
        }

        /// <summary>
        /// Ensures every non-aggregate partGroup with animationCues has a
        /// <see cref="PartGroupPreviewPlacement"/> in
        /// <see cref="PackagePreviewConfig.partGroupPlacements"/>. Existing
        /// placements are reused; missing ones are created with identity
        /// transforms so the baker has a write target. Returns a lookup
        /// keyed by partGroupId.
        ///
        /// <para>Rationale: authors may define a partGroup (partIds +
        /// animationCues) without authoring a placement — before Phase B
        /// nothing read <c>partGroupPlacements[].stepPoses</c>, so there
        /// was no need. The baker now writes there, so the placement must
        /// exist.</para>
        /// </summary>
        private static Dictionary<string, PartGroupPreviewPlacement> EnsurePartGroupPlacements(
            MachinePackageDefinition package)
        {
            var byId = new Dictionary<string, PartGroupPreviewPlacement>(StringComparer.Ordinal);
            if (package?.previewConfig == null) return byId;

            var existing = package.previewConfig.partGroupPlacements;
            if (existing != null)
            {
                foreach (var sp in existing)
                {
                    if (sp == null || string.IsNullOrEmpty(sp.partGroupId)) continue;
                    byId[sp.partGroupId] = sp;
                }
            }

            // Materialize missing placements for partGroups with cues. The
            // baker only writes to partGroups that actually have
            // holdAtEnd cues, but we populate eagerly here to give the index
            // a consistent view regardless of whether the cue fires this run.
            var subs = package.GetPartGroups();
            if (subs == null) return byId;

            var toAppend = new List<PartGroupPreviewPlacement>();
            for (int i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];
                if (sub == null || string.IsNullOrEmpty(sub.id)) continue;
                if (sub.isAggregate) continue;
                if (byId.ContainsKey(sub.id)) continue;

                var sp = new PartGroupPreviewPlacement
                {
                    partGroupId     = sub.id,
                    position          = default,
                    rotation          = new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                    scale             = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                    startPosition     = default,
                    startRotation     = new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                    startScale        = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                    assembledPosition = default,
                    assembledRotation = new SceneQuaternion { x = 0f, y = 0f, z = 0f, w = 1f },
                    assembledScale    = new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                };
                byId[sub.id] = sp;
                toAppend.Add(sp);
            }

            if (toAppend.Count > 0)
            {
                var combined = new List<PartGroupPreviewPlacement>();
                if (existing != null) combined.AddRange(existing);
                combined.AddRange(toAppend);
                package.previewConfig.partGroupPlacements = combined.ToArray();
            }

            return byId;
        }

        private static StepPoseEntry[] AppendStepPose(StepPoseEntry[] arr, StepPoseEntry entry)
        {
            if (arr == null || arr.Length == 0) return new[] { entry };
            var next = new StepPoseEntry[arr.Length + 1];
            Array.Copy(arr, next, arr.Length);
            next[arr.Length] = entry;
            return next;
        }

        /// <summary>
        /// True when any non-synthesized <see cref="StepPoseEntry"/> on
        /// <paramref name="arr"/> has a resolved propagation span that
        /// covers <paramref name="targetSeq"/>. Mirrors
        /// <see cref="PoseResolverIndex"/>'s span resolution (closed
        /// interval [fromSeq..throughSeq]) so the check aligns 1:1 with
        /// what PoseTableInvariants flags. Prior-pass synthesized entries
        /// are stripped before this runs, so anything present is authored.
        /// </summary>
        private static bool AnyAuthoredSpanCovers(
            StepPoseEntry[] arr, int targetSeq, Dictionary<string, int> seqByStepId)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i];
                if (e == null) continue;
                if (e.label != null && e.label.StartsWith(SynthesizedStepPoseLabelPrefix, StringComparison.Ordinal))
                    continue;
                int anchorSeq  = seqByStepId.TryGetValue(e.stepId ?? "", out int a) ? a : int.MinValue;
                int fromSeq    = string.IsNullOrEmpty(e.propagateFromStep)
                    ? (anchorSeq >= 0 ? anchorSeq : int.MinValue)
                    : (seqByStepId.TryGetValue(e.propagateFromStep, out int f) ? f : int.MinValue);
                int throughSeq = string.IsNullOrEmpty(e.propagateThroughStep)
                    ? int.MaxValue
                    : (seqByStepId.TryGetValue(e.propagateThroughStep, out int t) ? t : int.MaxValue);
                if (fromSeq <= targetSeq && throughSeq >= targetSeq) return true;
            }
            return false;
        }

        private static bool IsZeroQuaternion(SceneQuaternion q)
            => q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f;

        private static bool IsZeroOrNearZeroScale(SceneFloat3 s)
            => (s.x * s.x + s.y * s.y + s.z * s.z) < 0.0001f;

        private static Quaternion QuatFrom(SceneQuaternion q)
            => new Quaternion(q.x, q.y, q.z, q.w);

        /// <summary>
        /// Derives each non-aggregate partGroup's <c>partIds</c> list from
        /// the canonical <see cref="PartDefinition.partGroupIds"/> claims on
        /// each part. Parts are the single source of truth for group
        /// membership — authors set membership per part, and every partGroup
        /// recomputes its roster at load time. If a partGroup's legacy
        /// authored <c>partIds</c> array is present (older packages), it is
        /// merged in as a fallback so the migration is non-breaking; new
        /// authoring tools should stop writing <c>partGroup.partIds</c>.
        /// Aggregates are left alone here — their <c>partIds</c>/
        /// <c>memberPartGroupIds</c> composition is curated, not derived.
        /// Order: runs AFTER <see cref="IndexPartOwnership"/> (so parts are
        /// known) and BEFORE <see cref="BakeGroupRigidBody"/> /
        /// <see cref="BakePoseTable"/> (which query <c>sub.partIds</c>).
        /// </summary>
        private static void DerivePartGroupPartIds(MachinePackageDefinition package)
        {
            var subs = package.GetPartGroups();
            if (subs == null || subs.Length == 0) return;
            var parts = package.parts;
            if (parts == null || parts.Length == 0) return;

            var rosterBySub = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var seenBySub   = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == null || string.IsNullOrEmpty(p.id) || p.partGroupIds == null) continue;
                for (int k = 0; k < p.partGroupIds.Length; k++)
                {
                    string subId = p.partGroupIds[k];
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
                // appear via part.partGroupIds. Lets old packages load until
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
        /// Bakes <see cref="MachinePackageDefinition.partGroupLifecycleByGroupId"/>:
        /// for every partGroup, the set of step seqIndices where it's
        /// "touched" (group id or any member partId referenced through the
        /// step's structural fields, animation cues, or targets), plus
        /// FirstBuiltSeq / LastTouchedSeq summaries.
        ///
        /// Both TTAW and the runtime overlay use this via
        /// <see cref="PartGroupLifecycleResolver"/> so the per-step group
        /// list is consistent and the per-UI filter logic stops drifting.
        /// Idempotent — overwrites the dictionary on every call.
        /// </summary>
        private static void BakePartGroupLifecycle(MachinePackageDefinition package)
        {
            var groups = package.GetPartGroups();
            if (groups == null || groups.Length == 0)
            {
                package.partGroupLifecycleByGroupId =
                    new Dictionary<string, PartGroupLifecycle>(0);
                return;
            }

            // Membership: partId -> set of groupIds that own it. A part can
            // belong to multiple groups (e.g., aggregate's memberPartGroups
            // share parts with the leaf group). Hash here is cheap and
            // shared by every step's intersection check below.
            var groupIdsByPartId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int g = 0; g < groups.Length; g++)
            {
                var grp = groups[g];
                if (grp == null || string.IsNullOrEmpty(grp.id) || grp.partIds == null) continue;
                for (int p = 0; p < grp.partIds.Length; p++)
                {
                    var pid = grp.partIds[p];
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!groupIdsByPartId.TryGetValue(pid, out var list))
                    {
                        list = new List<string>(2);
                        groupIdsByPartId[pid] = list;
                    }
                    if (!list.Contains(grp.id)) list.Add(grp.id);
                }
            }

            // Per-group touched-seq sets. SortedSet keeps inserts O(log n)
            // and produces ascending order for free.
            var touchedByGroup = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
            for (int g = 0; g < groups.Length; g++)
            {
                if (groups[g] != null && !string.IsNullOrEmpty(groups[g].id))
                    touchedByGroup[groups[g].id] = new SortedSet<int>();
            }

            // Resolve target -> partGroupId once; used for step.targetIds
            // touch detection. associatedPartId is captured by the partId
            // intersection path below so we only need the explicit
            // associatedPartGroupId here.
            var partGroupIdByTargetId = new Dictionary<string, string>(StringComparer.Ordinal);
            var allTargets = package.GetTargets();
            for (int t = 0; t < allTargets.Length; t++)
            {
                var tgt = allTargets[t];
                if (tgt == null || string.IsNullOrEmpty(tgt.id)) continue;
                if (!string.IsNullOrEmpty(tgt.associatedPartGroupId))
                    partGroupIdByTargetId[tgt.id] = tgt.associatedPartGroupId;
            }

            void Touch(string groupId, int seq)
            {
                if (string.IsNullOrEmpty(groupId)) return;
                if (touchedByGroup.TryGetValue(groupId, out var set))
                    set.Add(seq);
            }

            void TouchByPartIds(string[] partIds, int seq)
            {
                if (partIds == null || partIds.Length == 0) return;
                for (int i = 0; i < partIds.Length; i++)
                {
                    if (string.IsNullOrEmpty(partIds[i])) continue;
                    if (groupIdsByPartId.TryGetValue(partIds[i], out var owners))
                    {
                        for (int o = 0; o < owners.Count; o++)
                            Touch(owners[o], seq);
                    }
                }
            }

            var steps = package.GetOrderedSteps();
            for (int s = 0; s < steps.Length; s++)
            {
                var step = steps[s];
                if (step == null) continue;
                int seq = step.sequenceIndex;

                // Direct group references on the step itself.
                Touch(step.partGroupId, seq);
                Touch(step.requiredPartGroupId, seq);

                // Member-part intersections across every part-id field the
                // runtime treats as "this step touches part X".
                TouchByPartIds(step.requiredPartIds, seq);
                TouchByPartIds(step.optionalPartIds, seq);
                TouchByPartIds(step.visualPartIds, seq);
                TouchByPartIds(step.derivedToolActionPartIds, seq);
                TouchByPartIds(step.derivedTargetPartIds, seq);

                // Targets associated with a partGroup directly.
                if (step.targetIds != null)
                {
                    for (int t = 0; t < step.targetIds.Length; t++)
                    {
                        if (string.IsNullOrEmpty(step.targetIds[t])) continue;
                        if (partGroupIdByTargetId.TryGetValue(step.targetIds[t], out var grpId))
                            Touch(grpId, seq);
                    }
                }

                // Animation cues — both group-targeted and part-targeted.
                if (step.animationCues?.cues != null)
                {
                    var cues = step.animationCues.cues;
                    for (int c = 0; c < cues.Length; c++)
                    {
                        var cue = cues[c];
                        if (cue == null) continue;
                        Touch(cue.targetPartGroupId, seq);
                        TouchByPartIds(cue.targetPartIds, seq);
                    }
                }
            }

            // Group-authored cues with stepIds also count as touches: a cue
            // hosted on the partGroup that fires on specific steps means
            // those steps are operating on the group even if no part-id
            // intersection exists. (No-stepIds cues are "always on while
            // visible" — covered by the FirstBuiltSeq forward-fill.)
            for (int g = 0; g < groups.Length; g++)
            {
                var grp = groups[g];
                if (grp == null || string.IsNullOrEmpty(grp.id)) continue;
                if (grp.animationCues == null) continue;
                for (int c = 0; c < grp.animationCues.Length; c++)
                {
                    var cue = grp.animationCues[c];
                    if (cue?.stepIds == null) continue;
                    for (int si = 0; si < cue.stepIds.Length; si++)
                    {
                        if (package.TryGetStep(cue.stepIds[si], out var stepRef))
                            Touch(grp.id, stepRef.sequenceIndex);
                    }
                }
            }

            var lifecycleByGroupId = new Dictionary<string, PartGroupLifecycle>(
                touchedByGroup.Count, StringComparer.Ordinal);

            foreach (var kvp in touchedByGroup)
            {
                var set = kvp.Value;
                if (set.Count == 0) continue;
                int[] arr = new int[set.Count];
                set.CopyTo(arr);
                lifecycleByGroupId[kvp.Key] = new PartGroupLifecycle
                {
                    GroupId = kvp.Key,
                    FirstBuiltSeq = arr[0],
                    LastTouchedSeq = arr[arr.Length - 1],
                    TouchedSeqs = arr,
                };
            }

            package.partGroupLifecycleByGroupId = lifecycleByGroupId;
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
        /// Auto-derives <see cref="PartGroupDefinition.isAggregate"/> from the
        /// presence of <c>memberPartGroupIds</c>. The flag is redundant with
        /// the data — if a partGroup's members are other partGroups, it IS
        /// an aggregate by definition. Authors no longer need to set the flag
        /// manually; existing JSON with explicit <c>isAggregate: true</c> still
        /// works unchanged. Must run BEFORE any pass that branches on the flag
        /// (template inflation, ownership indexing, rigid-body bake).
        /// </summary>
        private static void InferAggregateFlag(MachinePackageDefinition package)
        {
            var subs = package.GetPartGroups();
            if (subs == null) return;
            for (int i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];
                if (sub == null) continue;
                if (!sub.isAggregate && sub.memberPartGroupIds != null && sub.memberPartGroupIds.Length > 0)
                    sub.isAggregate = true;
            }
        }

        /// <summary>
        /// Derives per-(partGroup, target) rigid-body representations from
        /// <see cref="PackagePreviewConfig.integratedPartGroupPlacements"/>.
        /// For each placement, computes the centroid of member positions and
        /// each member's offset from that centroid. The editor consumes this
        /// so a group pose is ONE transform (center + fixed offsets), parallel
        /// to how individual parts work. JSON stays in per-member format;
        /// this derived data is never persisted.
        /// </summary>
        private static void BakeGroupRigidBody(MachinePackageDefinition package)
        {
            var subs = package.GetPartGroups();
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
                // child-partGroup group centers, with each child treated as
                // a rigid offset. Enables moving the whole phase (e.g. the
                // Frame Cube) as a single rigid unit for integration into
                // larger assemblies.
                for (int i = 0; i < subs.Length; i++)
                {
                    var agg = subs[i];
                    if (agg == null || !agg.isAggregate || agg.memberPartGroupIds == null || agg.memberPartGroupIds.Length == 0) continue;

                    Vector3 aggSum = Vector3.zero;
                    int aggN = 0;
                    var childCenters = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                    for (int k = 0; k < agg.memberPartGroupIds.Length; k++)
                    {
                        string cid = agg.memberPartGroupIds[k];
                        if (string.IsNullOrEmpty(cid)) continue;
                        PartGroupDefinition child = null;
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
                    // Member offsets are per child-partGroup-id (not partId),
                    // marking where each child's root sits relative to the aggregate.
                    foreach (var kvp in childCenters)
                        aggRb.memberPositionOffsets[kvp.Key] = kvp.Value - aggCenter;
                    agg.startRigidBody = aggRb;
                }
            }

            // ── Assembled pose: integrated-target centroid per (subId, targetId) ──
            var placements = package.previewConfig?.integratedPartGroupPlacements;
            if (placements == null || placements.Length == 0) return;

            for (int p = 0; p < placements.Length; p++)
            {
                var pl = placements[p];
                if (pl == null || pl.memberPlacements == null || pl.memberPlacements.Length == 0) continue;
                if (string.IsNullOrEmpty(pl.partGroupId) || string.IsNullOrEmpty(pl.targetId)) continue;

                PartGroupDefinition sub = null;
                for (int i = 0; i < subs.Length; i++)
                    if (subs[i] != null && string.Equals(subs[i].id, pl.partGroupId, StringComparison.Ordinal))
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
        /// <summary>
        /// Public so TTAW's WriteJson can call it before persisting
        /// preview_config.json — keeps the on-disk partPlacements in sync
        /// with whatever the runtime's bake would produce, eliminating the
        /// "preview_config.json stale vs bake" bug class.
        /// </summary>
        public static void BakeStagingPoses(MachinePackageDefinition package)
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

            // Diagnostic: surface the bake's headcount so silent
            // "preview_config.json is incomplete + bake didn't fix it"
            // scenarios are visible in the console at load time. Counts
            // how many parts have stagingPose vs. how many partPlacement
            // entries exist after the bake — should match.
            int partsWithStaging = 0;
            for (int i = 0; i < package.parts.Length; i++)
                if (package.parts[i]?.stagingPose != null) partsWithStaging++;
            int placementsAfter = package.previewConfig.partPlacements?.Length ?? 0;
            // Tripwire: if placementsAfter < partsWithStaging, the bake didn't
            // synthesize entries for some parts and the runtime will hide them.
            // Promoted to Warn for that case so it surfaces without verbose
            // logging on. Otherwise stays VerboseInfo (silent in normal play).
            if (placementsAfter < partsWithStaging)
                OseLog.Warn($"[Normalizer.BakeStagingPoses] parts={package.parts.Length} partsWithStagingPose={partsWithStaging} placementsAfterBake={placementsAfter} addedNew={addedNew} — placement count below stagingPose count, parts will fail to render. Run OSE → Package → Persist Bake to Disk.");
            else
                OseLog.VerboseInfo($"[Normalizer.BakeStagingPoses] parts={package.parts.Length} partsWithStagingPose={partsWithStaging} placementsAfterBake={placementsAfter} addedNew={addedNew}");
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

            // Fill partGroupId from partGroup.stepIds
            PartGroupDefinition[] subs = package.partGroups;
            if (subs != null)
            {
                for (int sa = 0; sa < subs.Length; sa++)
                {
                    PartGroupDefinition sub = subs[sa];
                    if (sub?.stepIds == null) continue;
                    for (int s = 0; s < sub.stepIds.Length; s++)
                    {
                        if (stepIndex.TryGetValue(sub.stepIds[s], out int idx))
                        {
                            if (string.IsNullOrEmpty(steps[idx].partGroupId))
                                steps[idx].partGroupId = sub.id;
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
        /// partGroups/steps every time.
        ///
        /// For every non-aggregate partGroup, sets <c>part.owningPartGroupId</c>
        /// to the partGroup id. For every Place-family step, appends the
        /// step id to <see cref="PartDefinition.owningPlaceStepIds"/> and (on
        /// first write) also sets the scalar <see cref="PartDefinition.owningPlaceStepId"/>
        /// as the canonical "first placement" for legacy callers. Multi-Place
        /// is now supported: a part can be Required by several Place steps
        /// representing distinct physical placements (e.g. loose alignment
        /// followed by final placement). Aggregate partGroups are
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
                p.owningPartGroupId = null;
                p.owningPlaceStepId   = null;
                p.owningPlaceStepIds  = null;
                partById[p.id] = p;
            }

            PartGroupDefinition[] subs = package.partGroups;
            if (subs != null)
            {
                for (int sa = 0; sa < subs.Length; sa++)
                {
                    PartGroupDefinition sub = subs[sa];
                    if (sub == null || sub.isAggregate) continue;
                    if (sub.partIds == null || string.IsNullOrWhiteSpace(sub.id)) continue;

                    for (int i = 0; i < sub.partIds.Length; i++)
                    {
                        string pid = sub.partIds[i];
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partById.TryGetValue(pid, out PartDefinition part)) continue;
                        if (string.IsNullOrEmpty(part.owningPartGroupId))
                            part.owningPartGroupId = sub.id;
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
