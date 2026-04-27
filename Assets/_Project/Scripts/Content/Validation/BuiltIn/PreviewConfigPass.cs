using System;
using System.Collections.Generic;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="PackagePreviewConfig"/> coverage — checks that every
    /// part, target, and partGroup has a placement entry, and verifies that
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

            HashSet<string> coveredPartGroups = ValidatePartGroupPlacements(previewConfig, ctx.PartGroupIds, issues);
            ValidateParkingPlacements    (previewConfig, ctx.PartGroupIds, issues);
            ValidateIntegratedPlacements (package, previewConfig, ctx.PartGroupIds, ctx.TargetIds, ctx.PartIds, issues);
            ValidateConstrainedFitPlacements(package, previewConfig, ctx.PartGroupIds, ctx.PartIds, issues);
            CheckAxisFitCoverage         (package, previewConfig, coveredPartGroups, issues);
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

        // ── PartGroup placements ────────────────────────────────────────────

        private static HashSet<string> ValidatePartGroupPlacements(
            PackagePreviewConfig previewConfig, HashSet<string> partGroupIds,
            List<MachinePackageValidationIssue> issues)
        {
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previewConfig.partGroupPlacements == null) return covered;

            for (int i = 0; i < previewConfig.partGroupPlacements.Length; i++)
            {
                PartGroupPreviewPlacement p = previewConfig.partGroupPlacements[i];
                if (p == null || string.IsNullOrWhiteSpace(p.partGroupId)) continue;
                covered.Add(p.partGroupId);
                if (!partGroupIds.Contains(p.partGroupId))
                    issues.Add(ValidationPassHelpers.Error(
                        $"previewConfig.partGroupPlacements[{i}].partGroupId",
                        $"Reference '{p.partGroupId}' does not resolve."));
            }
            return covered;
        }

        private static void ValidateParkingPlacements(
            PackagePreviewConfig previewConfig, HashSet<string> partGroupIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.completedPartGroupParkingPlacements == null) return;

            for (int i = 0; i < previewConfig.completedPartGroupParkingPlacements.Length; i++)
            {
                PartGroupPreviewPlacement p = previewConfig.completedPartGroupParkingPlacements[i];
                if (p == null || string.IsNullOrWhiteSpace(p.partGroupId)) continue;
                if (!partGroupIds.Contains(p.partGroupId))
                    issues.Add(ValidationPassHelpers.Error(
                        $"previewConfig.completedPartGroupParkingPlacements[{i}].partGroupId",
                        $"Reference '{p.partGroupId}' does not resolve."));
            }
        }

        private static void ValidateIntegratedPlacements(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> partGroupIds, HashSet<string> targetIds, HashSet<string> partIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.integratedPartGroupPlacements == null) return;

            for (int i = 0; i < previewConfig.integratedPartGroupPlacements.Length; i++)
            {
                IntegratedPartGroupPreviewPlacement p = previewConfig.integratedPartGroupPlacements[i];
                string path = $"previewConfig.integratedPartGroupPlacements[{i}]";
                if (p == null) { issues.Add(ValidationPassHelpers.Error(path, "Integrated partGroup placement entry is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(p.partGroupId, $"{path}.partGroupId", issues);
                ValidationPassHelpers.ValidateRequiredText(p.targetId,      $"{path}.targetId",      issues);

                if (!string.IsNullOrWhiteSpace(p.partGroupId) && !partGroupIds.Contains(p.partGroupId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.partGroupId", $"Reference '{p.partGroupId}' does not resolve."));

                if (!string.IsNullOrWhiteSpace(p.targetId) && !targetIds.Contains(p.targetId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.targetId", $"Reference '{p.targetId}' does not resolve."));

                if (p.memberPlacements == null || p.memberPlacements.Length == 0)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.memberPlacements",
                        "Integrated partGroup placement has no member placements."));
                    continue;
                }

                HashSet<string> partGroupPartIds = BuildPartGroupPartSet(package, p.partGroupId);

                if (partGroupPartIds != null && p.memberPlacements.Length != partGroupPartIds.Count)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.memberPlacements",
                        $"Integrated placement has {p.memberPlacements.Length} member(s) but partGroup " +
                        $"'{p.partGroupId}' defines {partGroupPartIds.Count} part(s). Some members may be missing or extraneous."));
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
                        else if (partGroupPartIds != null && !partGroupPartIds.Contains(member.partId))
                            issues.Add(ValidationPassHelpers.Error($"{memberPath}.partId",
                                $"Part '{member.partId}' is not a member of partGroup '{p.partGroupId}'."));
                    }
                }
            }
        }

        private static void ValidateConstrainedFitPlacements(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> partGroupIds, HashSet<string> partIds,
            List<MachinePackageValidationIssue> issues)
        {
            if (previewConfig.constrainedPartGroupFitPlacements == null) return;

            for (int i = 0; i < previewConfig.constrainedPartGroupFitPlacements.Length; i++)
            {
                ConstrainedPartGroupFitPreviewPlacement p = previewConfig.constrainedPartGroupFitPlacements[i];
                string path = $"previewConfig.constrainedPartGroupFitPlacements[{i}]";
                if (p == null) { issues.Add(ValidationPassHelpers.Error(path, "Constrained partGroup fit entry is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredText(p.partGroupId, $"{path}.partGroupId", issues);
                ValidationPassHelpers.ValidateRequiredText(p.targetId,      $"{path}.targetId",      issues);

                if (!string.IsNullOrWhiteSpace(p.partGroupId) && !partGroupIds.Contains(p.partGroupId))
                    issues.Add(ValidationPassHelpers.Error($"{path}.partGroupId", $"Reference '{p.partGroupId}' does not resolve."));

                if (p.drivenPartIds == null || p.drivenPartIds.Length == 0)
                {
                    issues.Add(ValidationPassHelpers.Warning($"{path}.drivenPartIds",
                        "Constrained partGroup fit has no drivenPartIds. The fit will behave like a rigid placement."));
                }

                HashSet<string> partGroupPartIds = BuildPartGroupPartSet(package, p.partGroupId);
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
                        else if (partGroupPartIds != null && !partGroupPartIds.Contains(drivenId))
                            issues.Add(ValidationPassHelpers.Error(drivenPath,
                                $"Part '{drivenId}' is not a member of partGroup '{p.partGroupId}'."));
                    }
                }
            }
        }

        private static void CheckAxisFitCoverage(
            MachinePackageDefinition package, PackagePreviewConfig previewConfig,
            HashSet<string> coveredPartGroups, List<MachinePackageValidationIssue> issues)
        {
            foreach (StepDefinition step in package.GetSteps())
            {
                if (step == null || string.IsNullOrWhiteSpace(step.requiredPartGroupId)) continue;

                if (!coveredPartGroups.Contains(step.requiredPartGroupId))
                    issues.Add(ValidationPassHelpers.Warning("previewConfig.partGroupPlacements",
                        $"PartGroup '{step.requiredPartGroupId}' is used by a placement step but has no authored partGroup placement frame."));

                if (step.IsAxisFitPlacement)
                {
                    string targetId = step.targetIds != null && step.targetIds.Length == 1 ? step.targetIds[0] : null;
                    if (string.IsNullOrWhiteSpace(targetId) ||
                        previewConfig.constrainedPartGroupFitPlacements == null ||
                        !package.TryGetConstrainedPartGroupFitPreviewPlacement(step.requiredPartGroupId, targetId, out _))
                    {
                        issues.Add(ValidationPassHelpers.Warning("previewConfig.constrainedPartGroupFitPlacements",
                            $"AxisFit step '{step.id}' has no matching constrained fit preview payload for " +
                            $"partGroup '{step.requiredPartGroupId}' and target '{targetId ?? "<missing>"}'."));
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
                    string.IsNullOrEmpty(t.associatedPartGroupId))
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

        private static HashSet<string> BuildPartGroupPartSet(MachinePackageDefinition package, string partGroupId)
        {
            if (string.IsNullOrWhiteSpace(partGroupId)) return null;
            if (!package.TryGetPartGroup(partGroupId, out PartGroupDefinition sub) || sub == null) return null;
            return new HashSet<string>(sub.partIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
