using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="PackagePreviewConfig"/> coverage — checks that every
    /// part, target, and subassembly has a placement entry, and verifies that
    /// authored target preview positions agree with part assembledPositions.
    /// </summary>
    internal sealed class PreviewConfigPass : IPackageValidationPass
    {
        private const float PositionTolerance = 0.001f;

        public void Execute(ValidationPassContext ctx)
        {
            MachinePackageDefinition package = ctx.Package;
            PackagePreviewConfig previewConfig = package.previewConfig;
            var issues = ctx.Issues;

            if (previewConfig == null)
            {
                if (ctx.PartIds.Count > 0)
                    issues.Add(ValidationPassHelpers.Warning("previewConfig",
                        "No previewConfig defined but package has parts. Parts will use fallback positioning."));
                return;
            }

            HashSet<string> coveredParts    = CoveredSet(previewConfig.partPlacements,   p => p?.partId);
            HashSet<string> coveredTargets  = CoveredSet(previewConfig.targetPlacements, p => p?.targetId);
            HashSet<string> wireOwnedIds    = BuildWireOwnedTargetIds(package);

            CheckPartCoverage  (ctx.PartIds,   coveredParts,   issues);
            CheckTargetCoverage(ctx.TargetIds, coveredTargets, wireOwnedIds, issues);

            HashSet<string> coveredSubassemblies = ValidateSubassemblyPlacements(previewConfig, ctx.SubassemblyIds, issues);
            ValidateParkingPlacements    (previewConfig, ctx.SubassemblyIds, issues);
            ValidateIntegratedPlacements (package, previewConfig, ctx.SubassemblyIds, ctx.TargetIds, ctx.PartIds, issues);
            ValidateConstrainedFitPlacements(package, previewConfig, ctx.SubassemblyIds, ctx.PartIds, issues);
            CheckAxisFitCoverage         (package, previewConfig, coveredSubassemblies, issues);
            ValidatePreviewPlayPositionConsistency(package, previewConfig, issues);
        }

        // ── Set builders ─────────────────────────────────────────────────────

        private static HashSet<string> CoveredSet<T>(T[] placements, Func<T, string> idSelector)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (placements == null) return set;
            for (int i = 0; i < placements.Length; i++)
            {
                string id = idSelector(placements[i]);
                if (!string.IsNullOrWhiteSpace(id)) set.Add(id);
            }
            return set;
        }

        private static HashSet<string> BuildWireOwnedTargetIds(MachinePackageDefinition package)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (package.steps == null) return set;
            foreach (var step in package.steps)
                if (step?.wireConnect?.wires != null)
                    foreach (var we in step.wireConnect.wires)
                        if (!string.IsNullOrEmpty(we?.targetId)) set.Add(we.targetId);
            return set;
        }

        // ── Coverage checks ───────────────────────────────────────────────────

        private static void CheckPartCoverage(
            HashSet<string> partIds, HashSet<string> covered, List<MachinePackageValidationIssue> issues)
        {
            foreach (string id in partIds)
                if (!covered.Contains(id))
                    issues.Add(ValidationPassHelpers.Warning("previewConfig.partPlacements",
                        $"Part '{id}' has no placement entry. It will use fallback positioning."));
        }

        private static void CheckTargetCoverage(
            HashSet<string> targetIds, HashSet<string> covered,
            HashSet<string> wireOwned, List<MachinePackageValidationIssue> issues)
        {
            foreach (string id in targetIds)
                if (!covered.Contains(id) && !wireOwned.Contains(id))
                    issues.Add(ValidationPassHelpers.Warning("previewConfig.targetPlacements",
                        $"Target '{id}' has no placement entry. Preview will use fallback positioning."));
        }

        // ── Subassembly placements ────────────────────────────────────────────

        private static HashSet<string> ValidateSubassemblyPlacements(
            PackagePreviewConfig previewConfig, HashSet<string> subassemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previewConfig.subassemblyPlacements == null) return covered;

            for (int i = 0; i < previewConfig.subassemblyPlacements.Length; i++)
            {
                SubassemblyPreviewPlacement p = previewConfig.subassemblyPlacements[i];
                if (p == null || string.IsNullOrWhiteSpace(p.subassemblyId)) continue;
                covered.Add(p.subassemblyId);
                if (!subassemblyIds.Contains(p.subassemblyId))
                    issues.Add(ValidationPassHelpers.Error(
                        $"previewConfig.subassemblyPlacements[{i}].subassemblyId",
                        $"Reference '{p.subassemblyId}' does not resolve."));
            }
            return covered;
        }

        private static void ValidateParkingPlacements(
            PackagePreviewConfig previewConfig, HashSet<string> subassemblyIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.completedSubassemblyParkingPlacements == null) return;

            for (int i = 0; i < previewConfig.completedSubassemblyParkingPlacements.Length; i++)
            {
                SubassemblyPreviewPlacement p = previewConfig.completedSubassemblyParkingPlacements[i];
                if (p == null || string.IsNullOrWhiteSpace(p.subassemblyId)) continue;
                if (!subassemblyIds.Contains(p.subassemblyId))
                    issues.Add(ValidationPassHelpers.Error(
                        $"previewConfig.completedSubassemblyParkingPlacements[{i}].subassemblyId",
                        $"Reference '{p.subassemblyId}' does not resolve."));
            }
        }

        private static void ValidateIntegratedPlacements(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> subassemblyIds, HashSet<string> targetIds, HashSet<string> partIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.integratedSubassemblyPlacements == null) return;

            for (int i = 0; i < previewConfig.integratedSubassemblyPlacements.Length; i++)
            {
                IntegratedSubassemblyPreviewPlacement p = previewConfig.integratedSubassemblyPlacements[i];
                string path = $"previewConfig.integratedSubassemblyPlacements[{i}]";
                if (p == null) { issues.Add(ValidationPassHelpers.Error(path, "Integrated subassembly placement entry is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(p.subassemblyId, $"{path}.subassemblyId", issues);
                ValidationPassHelpers.ValidateRequiredText(p.targetId,      $"{path}.targetId",      issues);

                if (!string.IsNullOrWhiteSpace(p.subassemblyId) && !subassemblyIds.Contains(p.subassemblyId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.subassemblyId", $"Reference '{p.subassemblyId}' does not resolve."));

                if (!string.IsNullOrWhiteSpace(p.targetId) && !targetIds.Contains(p.targetId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.targetId", $"Reference '{p.targetId}' does not resolve."));

                if (p.memberPlacements == null || p.memberPlacements.Length == 0)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.memberPlacements",
                        "Integrated subassembly placement has no member placements."));
                    continue;
                }

                HashSet<string> subassemblyPartIds = BuildSubassemblyPartSet(package, p.subassemblyId);

                if (subassemblyPartIds != null && p.memberPlacements.Length != subassemblyPartIds.Count)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.memberPlacements",
                        $"Integrated placement has {p.memberPlacements.Length} member(s) but subassembly " +
                        $"'{p.subassemblyId}' defines {subassemblyPartIds.Count} part(s). Some members may be missing or extraneous."));
                }

                for (int j = 0; j < p.memberPlacements.Length; j++)
                {
                    IntegratedMemberPreviewPlacement member = p.memberPlacements[j];
                    string memberPath = $"{path}.memberPlacements[{j}]";
                    if (member == null) { issues.Add(ValidationPassHelpers.Error(memberPath, "Integrated member placement entry is null.")); continue; }

                    ValidationPassHelpers.ValidateRequiredText(member.partId, $"{memberPath}.partId", issues);
                    if (!string.IsNullOrWhiteSpace(member.partId))
                    {
                        if (!partIds.Contains(member.partId))
                            issues.Add(ValidationPassHelpers.Error($"{memberPath}.partId", $"Reference '{member.partId}' does not resolve."));
                        else if (subassemblyPartIds != null && !subassemblyPartIds.Contains(member.partId))
                            issues.Add(ValidationPassHelpers.Error($"{memberPath}.partId",
                                $"Part '{member.partId}' is not a member of subassembly '{p.subassemblyId}'."));
                    }
                }
            }
        }

        private static void ValidateConstrainedFitPlacements(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> subassemblyIds, HashSet<string> partIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.constrainedSubassemblyFitPlacements == null) return;

            for (int i = 0; i < previewConfig.constrainedSubassemblyFitPlacements.Length; i++)
            {
                ConstrainedSubassemblyFitPreviewPlacement p = previewConfig.constrainedSubassemblyFitPlacements[i];
                string path = $"previewConfig.constrainedSubassemblyFitPlacements[{i}]";
                if (p == null) { issues.Add(ValidationPassHelpers.Error(path, "Constrained subassembly fit entry is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(p.subassemblyId, $"{path}.subassemblyId", issues);
                ValidationPassHelpers.ValidateRequiredText(p.targetId,      $"{path}.targetId",      issues);

                if (!string.IsNullOrWhiteSpace(p.subassemblyId) && !subassemblyIds.Contains(p.subassemblyId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.subassemblyId", $"Reference '{p.subassemblyId}' does not resolve."));

                if (p.drivenPartIds == null || p.drivenPartIds.Length == 0)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.drivenPartIds",
                        "Constrained subassembly fit has no drivenPartIds. The fit will behave like a rigid placement."));
                }

                HashSet<string> subassemblyPartIds = BuildSubassemblyPartSet(package, p.subassemblyId);
                string[] driven = p.drivenPartIds ?? Array.Empty<string>();

                for (int j = 0; j < driven.Length; j++)
                {
                    string drivenId = driven[j];
                    string drivenPath = $"{path}.drivenPartIds[{j}]";
                    ValidationPassHelpers.ValidateRequiredText(drivenId, drivenPath, issues);
                    if (!string.IsNullOrWhiteSpace(drivenId))
                    {
                        if (!partIds.Contains(drivenId))
                            issues.Add(ValidationPassHelpers.Error(drivenPath, $"Reference '{drivenId}' does not resolve."));
                        else if (subassemblyPartIds != null && !subassemblyPartIds.Contains(drivenId))
                            issues.Add(ValidationPassHelpers.Error(drivenPath,
                                $"Part '{drivenId}' is not a member of subassembly '{p.subassemblyId}'."));
                    }
                }
            }
        }

        private static void CheckAxisFitCoverage(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> coveredSubassemblies, List<MachinePackageValidationIssue> issues)
        {
            foreach (StepDefinition step in package.GetSteps())
            {
                if (step == null || string.IsNullOrWhiteSpace(step.requiredSubassemblyId)) continue;

                if (!coveredSubassemblies.Contains(step.requiredSubassemblyId))
                    issues.Add(ValidationPassHelpers.Warning("previewConfig.subassemblyPlacements",
                        $"Subassembly '{step.requiredSubassemblyId}' is used by a placement step but has no authored subassembly placement frame."));

                if (step.IsAxisFitPlacement)
                {
                    string targetId = step.targetIds != null && step.targetIds.Length == 1 ? step.targetIds[0] : null;
                    if (string.IsNullOrWhiteSpace(targetId) ||
                        previewConfig.constrainedSubassemblyFitPlacements == null ||
                        !package.TryGetConstrainedSubassemblyFitPreviewPlacement(step.requiredSubassemblyId, targetId, out _))
                    {
                        issues.Add(ValidationPassHelpers.Warning("previewConfig.constrainedSubassemblyFitPlacements",
                            $"AxisFit step '{step.id}' has no matching constrained fit preview payload for " +
                            $"subassembly '{step.requiredSubassemblyId}' and target '{targetId ?? "<missing>"}'."));
                    }
                }
            }
        }

        // ── Play-position consistency ─────────────────────────────────────────

        private static void ValidatePreviewPlayPositionConsistency(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.targetPlacements == null || previewConfig.partPlacements == null) return;

            var placementTargetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StepDefinition step in package.GetSteps())
            {
                if (step == null || !step.IsPlacement) continue;
                string[] ids = step.targetIds ?? Array.Empty<string>();
                for (int i = 0; i < ids.Length; i++)
                    if (!string.IsNullOrWhiteSpace(ids[i])) placementTargetIds.Add(ids[i]);
            }

            var partLookup = new Dictionary<string, PartPreviewPlacement>(StringComparer.OrdinalIgnoreCase);
            foreach (var pp in previewConfig.partPlacements)
                if (pp != null && !string.IsNullOrEmpty(pp.partId)) partLookup[pp.partId] = pp;

            var targetPartLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in package.GetTargets())
                if (t != null && !string.IsNullOrEmpty(t.id) &&
                    !string.IsNullOrEmpty(t.associatedPartId) &&
                    string.IsNullOrEmpty(t.associatedSubassemblyId))
                    targetPartLookup[t.id] = t.associatedPartId;

            foreach (var tp in previewConfig.targetPlacements)
            {
                if (tp == null || string.IsNullOrEmpty(tp.targetId)) continue;
                if (!placementTargetIds.Contains(tp.targetId)) continue;
                if (!targetPartLookup.TryGetValue(tp.targetId, out string partId)) continue;
                if (!partLookup.TryGetValue(partId, out var pp)) continue;

                float dx = tp.position.x - pp.assembledPosition.x;
                float dy = tp.position.y - pp.assembledPosition.y;
                float dz = tp.position.z - pp.assembledPosition.z;
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq > PositionTolerance * PositionTolerance)
                {
                    float dist = (float)Math.Sqrt(distSq);
                    issues.Add(ValidationPassHelpers.Warning(
                        $"previewConfig.targetPlacements[{tp.targetId}]",
                        $"Preview position ({tp.position.x:F3}, {tp.position.y:F3}, {tp.position.z:F3}) differs from " +
                        $"part '{partId}' assembledPosition ({pp.assembledPosition.x:F3}, {pp.assembledPosition.y:F3}, {pp.assembledPosition.z:F3}) " +
                        $"by {dist:F4}m. Preview will appear at the wrong location. " +
                        $"Update targetPlacement to match assembledPosition or the preview code will override it."));
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static HashSet<string> BuildSubassemblyPartSet(MachinePackageDefinition package, string subassemblyId)
        {
            if (string.IsNullOrWhiteSpace(subassemblyId)) return null;
            if (!package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition sub) || sub == null) return null;
            return new HashSet<string>(sub.partIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
