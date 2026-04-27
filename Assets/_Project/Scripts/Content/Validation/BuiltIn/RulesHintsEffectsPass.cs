using System;
using System.Linq;
using OSE.Core;

namespace OSE.Content.Validation
{
    /// <summary>
    /// Validates <see cref="ValidationRuleDefinition"/>, <see cref="HintDefinition"/>,
    /// and <see cref="EffectDefinition"/> entries, and host-owned particle cues.
    /// </summary>
    internal sealed class RulesHintsEffectsPass : IPackageValidationPass
    {
        public void Execute(ValidationPassContext ctx)
        {
            ValidateValidationRules(ctx);
            ValidateHints(ctx);
            ValidateEffects(ctx);
            ValidateParticleCues(ctx);
        }

        private static void ValidateValidationRules(ValidationPassContext ctx)
        {
            ValidationRuleDefinition[] rules = ctx.Package.GetValidationRules();
            var issues = ctx.Issues;

            for (int i = 0; i < rules.Length; i++)
            {
                ValidationRuleDefinition r = rules[i];
                string path = $"validationRules[{i}]";
                if (r == null) { issues.Add(ValidationPassHelpers.Error(path, "Validation rule definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(r.type, ValidationPassHelpers.ValidationTypeValues, $"{path}.type", issues);
                ValidationPassHelpers.ValidateOptionalReference(r.targetId,       ctx.TargetIds, $"{path}.targetId",       issues);
                ValidationPassHelpers.ValidateOptionalReference(r.expectedPartId, ctx.PartIds,   $"{path}.expectedPartId", issues);
                ValidationPassHelpers.ValidateOptionalReferences(r.requiredStepIds, ctx.StepIds, $"{path}.requiredStepIds", issues);
                ValidationPassHelpers.ValidateOptionalReferences(r.requiredPartIds, ctx.PartIds, $"{path}.requiredPartIds", issues);
                ValidationPassHelpers.ValidateOptionalReference(r.correctionHintId, ctx.HintIds, $"{path}.correctionHintId", issues);
            }
        }

        private static void ValidateHints(ValidationPassContext ctx)
        {
            HintDefinition[] hints = ctx.Package.GetHints();
            var issues = ctx.Issues;

            for (int i = 0; i < hints.Length; i++)
            {
                HintDefinition h = hints[i];
                string path = $"hints[{i}]";
                if (h == null) { issues.Add(ValidationPassHelpers.Error(path, "Hint definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(h.type,     ValidationPassHelpers.HintTypeValues,     $"{path}.type",     issues);
                ValidationPassHelpers.ValidateRequiredText(h.message,                                            $"{path}.message",  issues);
                ValidationPassHelpers.ValidateOptionalEnum(h.priority,  ValidationPassHelpers.HintPriorityValues, $"{path}.priority", issues);
                ValidationPassHelpers.ValidateOptionalReference(h.targetId, ctx.TargetIds, $"{path}.targetId", issues);
                ValidationPassHelpers.ValidateOptionalReference(h.partId,   ctx.PartIds,   $"{path}.partId",   issues);
                ValidationPassHelpers.ValidateOptionalReference(h.toolId,   ctx.ToolIds,   $"{path}.toolId",   issues);
            }
        }

        private static void ValidateEffects(ValidationPassContext ctx)
        {
            EffectDefinition[] effects = ctx.Package.GetEffects();
            var issues = ctx.Issues;

            for (int i = 0; i < effects.Length; i++)
            {
                EffectDefinition e = effects[i];
                string path = $"effects[{i}]";
                if (e == null) { issues.Add(ValidationPassHelpers.Error(path, "Effect definition is null.")); continue; }

                ValidationPassHelpers.ValidateRequiredEnum(e.type,          ValidationPassHelpers.EffectTypeValues,    $"{path}.type",          issues);
                ValidationPassHelpers.ValidateOptionalEnum(e.triggerPolicy, ValidationPassHelpers.EffectTriggerValues, $"{path}.triggerPolicy", issues);
            }
        }

        private static void ValidateParticleCues(ValidationPassContext ctx)
        {
            var package = ctx.Package;

            if (package.parts != null)
                for (int i = 0; i < package.parts.Length; i++)
                {
                    var part = package.parts[i];
                    CheckHostCues(ctx, part?.animationCues, $"parts[{i}].animationCues");
                }

            var subs = package.GetPartGroups();
            if (subs != null)
                for (int i = 0; i < subs.Length; i++)
                {
                    var sub = subs[i];
                    CheckHostCues(ctx, sub?.animationCues, $"partGroups[{i}].animationCues");
                }

            var tools = package.GetTools();
            if (tools != null)
                for (int i = 0; i < tools.Length; i++)
                {
                    var tool = tools[i];
                    CheckHostCues(ctx, tool?.animationCues, $"tools[{i}].animationCues");
                }
        }

        private static void CheckHostCues(ValidationPassContext ctx, AnimationCueEntry[] cues, string basePath)
        {
            if (cues == null) return;
            var issues = ctx.Issues;
            var presetIds = CompletionParticleEffect.PresetIds;

            for (int i = 0; i < cues.Length; i++)
            {
                var e = cues[i];
                if (e == null) continue;
                string path = $"{basePath}[{i}]";

                // Phase 1: particle schema
                if (string.Equals(e.type, "particle", StringComparison.OrdinalIgnoreCase))
                {
                    string mode = e.particleSourceMode ?? "";
                    if (!string.Equals(mode, "preset", StringComparison.Ordinal) &&
                        !string.Equals(mode, "prefab", StringComparison.Ordinal))
                    {
                        issues.Add(ValidationPassHelpers.Error(
                            $"{path}.particleSourceMode",
                            $"must be 'preset' or 'prefab' (got '{mode}')."));
                    }
                    else if (mode == "preset")
                    {
                        if (string.IsNullOrEmpty(e.particlePresetId))
                            issues.Add(ValidationPassHelpers.Error($"{path}.particlePresetId", "is required when particleSourceMode = 'preset'."));
                        else if (!presetIds.Contains(e.particlePresetId))
                            issues.Add(ValidationPassHelpers.Error(
                                $"{path}.particlePresetId",
                                $"'{e.particlePresetId}' is not a known preset. Known: {string.Join(", ", presetIds)}."));
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(e.particlePrefabRef))
                            issues.Add(ValidationPassHelpers.Error($"{path}.particlePrefabRef", "is required when particleSourceMode = 'prefab'."));
                    }

                    if (e.particleScale != 0f && (e.particleScale < 0.05f || e.particleScale > 20f))
                        issues.Add(ValidationPassHelpers.Error(
                            $"{path}.particleScale",
                            $"must be within [0.05, 20] (got {e.particleScale})."));
                }

                // Phase 2: progress-range + numeric-range checks across any cue type.
                CheckPhase2Fields(e, path, issues);
            }
        }

        private static void CheckPhase2Fields(AnimationCueEntry e, string path, System.Collections.Generic.List<MachinePackageValidationIssue> issues)
        {
            // Progress range: both in [0, 1] and start <= end (treating
            // end==0 as "unset → full range", same as the runtime).
            if (e.startProgress < 0f || e.startProgress > 1f)
                issues.Add(ValidationPassHelpers.Error($"{path}.startProgress",
                    $"must be within [0, 1] (got {e.startProgress})."));
            if (e.endProgress < 0f || e.endProgress > 1f)
                issues.Add(ValidationPassHelpers.Error($"{path}.endProgress",
                    $"must be within [0, 1] (got {e.endProgress})."));
            if (e.endProgress > 0f && e.endProgress < e.startProgress)
                issues.Add(ValidationPassHelpers.Error($"{path}.endProgress",
                    $"must be ≥ startProgress ({e.startProgress}) (got {e.endProgress})."));

            // Intensity fields — generous upper bound catches typos like 1500.
            if (e.fromIntensity < 0f || e.fromIntensity > 20f)
                issues.Add(ValidationPassHelpers.Error($"{path}.fromIntensity",
                    $"must be within [0, 20] (got {e.fromIntensity})."));
            if (e.toIntensity < 0f || e.toIntensity > 20f)
                issues.Add(ValidationPassHelpers.Error($"{path}.toIntensity",
                    $"must be within [0, 20] (got {e.toIntensity})."));
            if (e.lineEmissionIntensity < 0f || e.lineEmissionIntensity > 20f)
                issues.Add(ValidationPassHelpers.Error($"{path}.lineEmissionIntensity",
                    $"must be within [0, 20] (got {e.lineEmissionIntensity})."));

            // Wobble / vibration sanity.
            if (e.wobbleAmplitude < 0f || e.wobbleAmplitude > 1f)
                issues.Add(ValidationPassHelpers.Error($"{path}.wobbleAmplitude",
                    $"must be within [0, 1] rad (got {e.wobbleAmplitude})."));
            if (e.wobbleFrequency < 0f || e.wobbleFrequency > 200f)
                issues.Add(ValidationPassHelpers.Error($"{path}.wobbleFrequency",
                    $"must be within [0, 200] rad/s (got {e.wobbleFrequency})."));
            if (e.vibrationFrequency < 0f || e.vibrationFrequency > 500f)
                issues.Add(ValidationPassHelpers.Error($"{path}.vibrationFrequency",
                    $"must be within [0, 500] Hz (got {e.vibrationFrequency})."));

            // Anchor refs — warn when the value is non-empty and not recognised
            // (still allow because "literal:x,y,z" is valid).
            CheckAnchorRef(e.anchorARef, $"{path}.anchorARef", issues);
            CheckAnchorRef(e.anchorBRef, $"{path}.anchorBRef", issues);
            if (e.splineAnchorRefs != null)
                for (int k = 0; k < e.splineAnchorRefs.Length; k++)
                    CheckAnchorRef(e.splineAnchorRefs[k], $"{path}.splineAnchorRefs[{k}]", issues);

            // Measure unit enum.
            if (!string.IsNullOrEmpty(e.measureUnit))
            {
                switch (e.measureUnit)
                {
                    case "mm": case "cm": case "m": case "inch": case "ft": break;
                    default:
                        issues.Add(ValidationPassHelpers.Error($"{path}.measureUnit",
                            $"must be one of mm/cm/m/inch/ft (got '{e.measureUnit}')."));
                        break;
                }
            }
        }

        private static void CheckAnchorRef(string anchorRef, string path, System.Collections.Generic.List<MachinePackageValidationIssue> issues)
        {
            if (string.IsNullOrEmpty(anchorRef)) return;
            if (anchorRef.StartsWith("literal:")) return;
            switch (anchorRef)
            {
                case "toolTip": case "toolGrip":
                case "targetSurface":
                case "weldStart": case "weldEnd": case "weldMid":
                case "measureAnchorA": case "measureAnchorB":
                case "partAssembledCenter":
                    return;
                default:
                    issues.Add(ValidationPassHelpers.Error(path,
                        $"'{anchorRef}' is not a recognised anchor ref. Valid: toolTip, toolGrip, targetSurface, weldStart, weldEnd, weldMid, measureAnchorA, measureAnchorB, partAssembledCenter, or literal:x,y,z."));
                    break;
            }
        }
    }
}
