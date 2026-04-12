using System;
using System.Collections.Generic;

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

            InflatePartTemplates(package);
            BakeStagingPoses(package);
            InferStepParentIds(package);
            NormalizeToolActions(package);
            ResolveToolActionPartIds(package);
            ResolveDirectTargetPartIds(package);
            IndexPartOwnership(package);
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
        /// to the subassembly id. For every Place-family step, sets
        /// <c>part.owningPlaceStepId</c> to the step id. First-writer-wins —
        /// <c>PartOwnershipExclusivityPass</c> guarantees no collisions at
        /// validation time, so first-wins is equivalent to only-wins for
        /// well-formed packages. Aggregate subassemblies are intentionally
        /// skipped (they may contain child parts).
        /// </summary>
        private static void IndexPartOwnership(MachinePackageDefinition package)
        {
            PartDefinition[] parts = package.parts;
            if (parts == null || parts.Length == 0) return;

            var partById = new Dictionary<string, PartDefinition>(
                parts.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                PartDefinition p = parts[i];
                if (p == null || string.IsNullOrWhiteSpace(p.id)) continue;

                // Clear any stale state from a prior Normalize call on the
                // same in-memory package (editor reload path).
                p.owningSubassemblyId = null;
                p.owningPlaceStepId = null;
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
                for (int s = 0; s < steps.Length; s++)
                {
                    StepDefinition step = steps[s];
                    if (step == null || string.IsNullOrWhiteSpace(step.id)) continue;
                    if (step.ResolvedFamily != StepFamily.Place) continue;
                    if (step.requiredPartIds == null) continue;

                    for (int i = 0; i < step.requiredPartIds.Length; i++)
                    {
                        string pid = step.requiredPartIds[i];
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partById.TryGetValue(pid, out PartDefinition part)) continue;
                        if (string.IsNullOrEmpty(part.owningPlaceStepId))
                            part.owningPlaceStepId = step.id;
                    }
                }
            }
        }
    }
}
