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
            NormalizeTaskOrderToolActionKinds(package);
            EnsureTaskOrderCoversRequirements(package);
            ValidateUseFamilyPartsArePrePlaced(package);
            ValidateUnorderedSets(package);
            NormalizeToolActions(package);
            ResolveToolActionPartIds(package);
            ResolveDirectTargetPartIds(package);
            IndexPartOwnership(package);
            DeriveSubassemblyPartIds(package);
            BakeGroupRigidBody(package);

            // Cue passes run BEFORE BakePoseTable so synthesized stepPoses
            // (below) are picked up by the regular bake. Trigger rewrite and
            // host migration don't depend on poseTable.
            NormalizeAnimationCueTriggers(package);
            MigrateStepAnimationCuesToHosts(package);
            ValidateAnimationCueInvariants(package);
            BakeHoldAtEndEndPoses(package);

            BakePoseTable(package);
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
                    Debug.LogWarning($"[TaskOrder.Normalize] step '{step.id}': rewrote {rewritten} kind='target' entr{(rewritten == 1 ? "y" : "ies")} to kind='toolAction' (cursor drives on action ids, not target ids). Update the authoring source to emit kind='toolAction' directly.");
                }
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

                Debug.LogWarning($"[TaskOrder.Normalize] step '{step.id}': appended {missing.Count} missing taskOrder entr{(missing.Count == 1 ? "y" : "ies")} to cover declared requirements — the cursor would otherwise deadlock. Update the authoring source so taskOrder reflects every requiredPart/requiredToolAction explicitly.");
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
                            Debug.LogError($"[Validate.UseParts] step '{step.id}' (seq {step.sequenceIndex}, family=Use) declares requiredPartIds that no prior family=Place step placed: {string.Join(", ", unplaced)}. Trainee cannot complete this step — Use-family routes interactions through UseStepHandler, which does not place parts. Either change the family to Place, or add a prior Place step that introduces these parts.");
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
                                Debug.LogError($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{label}' mixes kinds ('{currentKind}' and '{entry.kind}'). Sets must be kind-pure.");
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
                            Debug.LogWarning($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{currentLabel}' has only 1 member — drop the label or add siblings.");
                        }
                    }

                    if (label != null)
                    {
                        if (closedLabels.Contains(label))
                        {
                            Debug.LogError($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{label}' reappears as a non-contiguous span. Entries with the same label must be adjacent.");
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
                    Debug.LogWarning($"[UnorderedSet.Validate] step '{step.id}' unorderedSet '{currentLabel}' has only 1 member — drop the label or add siblings.");
                }
            }
        }

        private const string SynthesizedStepPoseLabelPrefix = "synthesized:holdAtEnd";

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

            // Strip prior synthesized entries so the pass is idempotent.
            foreach (var pp in placementByPart.Values)
                pp.stepPoses = StripSynthesizedStepPoses(pp.stepPoses);

            // Resolver index for effective-pose lookups. Cycle-free — only
            // reads package, placements, and subassembly membership; does
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

            // ── Subassembly-hosted cues ──
            var subs = package.GetSubassemblies();
            if (subs != null)
            {
                for (int si = 0; si < subs.Length; si++)
                {
                    var sub = subs[si];
                    if (sub?.animationCues == null || sub.animationCues.Length == 0) continue;
                    if (sub.partIds == null || sub.partIds.Length == 0) continue;
                    synthesizedOnSubs += SynthesizeGroupHoldAtEnd(
                        package, sub, idx, orderedSteps, seqByStepId, placementByPart);
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
                Debug.Log($"[CueRuntime.BakeHoldAtEnd] synthesized {total} stepPose(s) in '{package.packageId}' ({synthesizedOnSubs} from group cues, {synthesizedOnParts} from part cues).");
        }

        private static int SynthesizeGroupHoldAtEnd(
            MachinePackageDefinition package,
            SubassemblyDefinition sub,
            PoseResolverIndex idx,
            StepDefinition[] orderedSteps,
            Dictionary<string, int> seqByStepId,
            Dictionary<string, PartPreviewPlacement> placementByPart)
        {
            int count = 0;

            for (int ci = 0; ci < sub.animationCues.Length; ci++)
            {
                var cue = sub.animationCues[ci];
                if (cue == null) continue;
                if (!string.Equals(cue.type, "poseTransition", StringComparison.Ordinal)) continue;
                if (!cue.holdAtEnd) continue;
                if (cue.stepIds == null || cue.stepIds.Length == 0)
                {
                    Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] subassembly '{sub.id}' cue[{ci}]: holdAtEnd=true but empty stepIds — skipping.");
                    continue;
                }
                if (cue.toPose == null || IsZeroQuaternion(cue.toPose.rotation))
                {
                    Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] subassembly '{sub.id}' cue[{ci}]: toPose.rotation is zero quaternion — skipping.");
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
                        Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] subassembly '{sub.id}' cue[{ci}] anchors to final step '{cueStepId}' — no next step to persist into.");
                        continue;
                    }
                    int anchorSeq = nextStep.sequenceIndex;

                    // Eligible members: resolve each member's effective pose
                    // at the cue's step via PoseResolver. Hidden / at-origin
                    // members match the runtime centroid filter and are
                    // excluded.
                    var eligible = new List<(string id, Vector3 pos, Quaternion rot, Vector3 scl)>();
                    for (int mi = 0; mi < sub.partIds.Length; mi++)
                    {
                        string memberId = sub.partIds[mi];
                        if (string.IsNullOrEmpty(memberId)) continue;
                        if (!placementByPart.ContainsKey(memberId)) continue;
                        var res = PoseResolver.Resolve(
                            memberId, cueStep.sequenceIndex, package, idx, PoseMode.Committed);
                        if (res.source == PoseSource.Hidden) continue;
                        if (res.pos.sqrMagnitude < 0.0001f) continue;
                        eligible.Add((memberId, res.pos, res.rot, res.scl));
                    }
                    if (eligible.Count == 0)
                    {
                        Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] subassembly '{sub.id}' cue[{ci}] at '{cueStepId}': no eligible members visible at cue step — skipping.");
                        continue;
                    }

                    Vector3 centroid = Vector3.zero;
                    for (int k = 0; k < eligible.Count; k++) centroid += eligible[k].pos;
                    centroid /= eligible.Count;

                    // Fan out per member.
                    for (int k = 0; k < eligible.Count; k++)
                    {
                        var m = eligible[k];
                        var placement = placementByPart[m.id];

                        if (AnyAuthoredSpanCovers(placement.stepPoses, anchorSeq, seqByStepId))
                        {
                            Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] member '{m.id}' has authored stepPose covering step '{nextStep.id}' (seq {anchorSeq}) — skipping synthesis for this member (authored wins).");
                            continue;
                        }

                        Vector3    finalPos = centroid + deltaRot * (m.pos - centroid);
                        Quaternion finalRot = deltaRot * m.rot;

                        var synth = new StepPoseEntry
                        {
                            stepId = nextStep.id,
                            label  = $"{SynthesizedStepPoseLabelPrefix} (sub={sub.id} cue={cue.type}[{ci}])",
                            position = new SceneFloat3 { x = finalPos.x, y = finalPos.y, z = finalPos.z },
                            rotation = new SceneQuaternion { x = finalRot.x, y = finalRot.y, z = finalRot.z, w = finalRot.w },
                            scale    = m.scl.sqrMagnitude > 0.0001f
                                         ? new SceneFloat3 { x = m.scl.x, y = m.scl.y, z = m.scl.z }
                                         : new SceneFloat3 { x = 1f, y = 1f, z = 1f },
                            propagateFromStep    = "",
                            propagateThroughStep = "",
                        };
                        placement.stepPoses = AppendStepPose(placement.stepPoses, synth);
                        count++;
                    }
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
                    Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}]: holdAtEnd=true but empty stepIds — skipping.");
                    continue;
                }
                if (cue.toPose == null || IsZeroQuaternion(cue.toPose.rotation))
                {
                    Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}]: toPose.rotation is zero quaternion — skipping.");
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
                        Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' cue[{ci}] anchors to final step '{cueStepId}' — no next step to persist into.");
                        continue;
                    }
                    int anchorSeq = nextStep.sequenceIndex;

                    if (AnyAuthoredSpanCovers(placement.stepPoses, anchorSeq, seqByStepId))
                    {
                        Debug.LogWarning($"[CueRuntime.BakeHoldAtEnd] part '{part.id}' has authored stepPose covering step '{nextStep.id}' (seq {anchorSeq}) — skipping synthesis (authored wins).");
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
