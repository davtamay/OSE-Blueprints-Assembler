// TTAW.PopulateFromPreview.cs — "Populate from tool preview style" button logic.
// ──────────────────────────────────────────────────────────────────────────────
// Given a Use-step tool host, generates an authored cue stack that mirrors the
// live C# preview for the step's profile (Weld, Drill, Torque, Cut, SquareCheck,
// Measure). Every magic number lifted from the preview class is carried forward
// as a cue field so authors can see — and modify — the decomposition that the
// C# preview encodes as hard-coded behaviour.
//
// Constants are cross-referenced by file + line in comments so it's easy to
// verify parity when the underlying preview changes.

using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEngine;

using OSE.Core;
namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        /// <summary>
        /// Resolves the tool's profile for the given step. Tool profile in
        /// OSE is step-scoped — the same physical tool can run as Weld on
        /// one step and Measure on another. Returns the step's profile
        /// string if the tool is referenced by a requiredToolActions entry,
        /// else null.
        /// </summary>
        private string ResolveToolProfileForStep(StepDefinition step, string toolId)
        {
            if (step == null || string.IsNullOrEmpty(toolId)) return null;
            if (step.requiredToolActions == null) return null;
            foreach (var a in step.requiredToolActions)
            {
                if (a == null) continue;
                if (string.Equals(a.toolId, toolId, StringComparison.Ordinal))
                    return step.profile;
            }
            return null;
        }

        /// <summary>
        /// Returns true when at least one cue on the storage host applies
        /// to this step. Used by the "Seed from &lt;profile&gt;" button to
        /// hide itself once the author has already authored cues for this
        /// step — no autosave, no delayCall, no OnGUI-driven disk I/O.
        /// </summary>
        private bool StepHasApplicableCues(StepDefinition step, CueScope scope, string scopeKey)
        {
            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (!storage.IsValid || storage.cues == null) return false;
            for (int i = 0; i < storage.cues.Length; i++)
            {
                var c = storage.cues[i];
                if (c == null) continue;
                if (CueAppliesHere(c, step, scope, scopeKey)) return true;
            }
            return false;
        }

        /// <summary>
        /// Appends a cue stack reproducing the tool preview for this profile
        /// onto the tool host. Merges with existing cues — panelOrder is
        /// auto-advanced per panel so existing rows stay put. Author can
        /// open each new row, inspect the fields, and see exactly which
        /// numbers drive the effect.
        /// </summary>
        private void PopulateFromToolPreview(StepDefinition step, CueScope scope, string scopeKey, string profile)
        {
            var storage = GetHostCueStorage(step, scope, scopeKey);
            if (!storage.IsValid) return;

            var authored = new List<AnimationCueEntry>(storage.cues ?? Array.Empty<AnimationCueEntry>());
            int countBefore = authored.Count;

            string stepId = step.id;
            switch (profile)
            {
                case "Weld":
                case "Glue":   AppendWeldCues(authored, stepId);       break;
                case "Drill":  AppendDrillCues(authored, stepId);      break;
                case "Torque": AppendTorqueCues(authored, stepId);     break;
                case "Cut":    AppendCutCues(authored, stepId);        break;
                case "Square": AppendSquareCues(authored, stepId);     break;
                case "Measure": AppendMeasureCues(authored, stepId);   break;
                default:
                    OseLog.Warn($"[TTAW] No preview recipe for profile '{profile}'. Use Weld / Drill / Torque / Cut / Square / Measure.");
                    return;
            }

            // Assign per-panel panelOrder so the seeded rows sit after any
            // existing rows in the same (host, trigger) bucket.
            var maxOrderByTrigger = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < countBefore; i++)
            {
                var existing = authored[i];
                if (existing == null) continue;
                string trig = string.IsNullOrEmpty(existing.trigger) ? "onActivate" : existing.trigger;
                if (!maxOrderByTrigger.TryGetValue(trig, out int cur) || existing.panelOrder > cur)
                    maxOrderByTrigger[trig] = existing.panelOrder;
            }
            for (int i = countBefore; i < authored.Count; i++)
            {
                var c = authored[i];
                string trig = string.IsNullOrEmpty(c.trigger) ? "onActivate" : c.trigger;
                int cur = maxOrderByTrigger.TryGetValue(trig, out int v) ? v + 1 : 0;
                c.panelOrder = cur;
                maxOrderByTrigger[trig] = cur;
            }

            storage.setter(authored.ToArray());
            storage.markDirty();
            int added = authored.Count - countBefore;
            OseLog.Info($"[TTAW] Populated {added} cue row(s) from '{profile}' preview onto tool '{scopeKey}'. Open any row to inspect the decomposition.");
            Repaint();
        }

        // ── Weld (mirrors WeldPreview.cs) ─────────────────────────────────

        private static void AppendWeldCues(List<AnimationCueEntry> cues, string stepId)
        {
            // weld_arc particle — WeldPreview.cs:92 (progress ≥ 0.1, continuous)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "weld_arc";
                c.particleScale      = 1f;
                c.startProgress      = 0.1f;
                c.endProgress        = 1f;
                c.loop               = true;
            }));

            // Cyan-white emission glow — WeldPreview.cs:97 (Color(0.9, 0.95, 1.0) × 1.5)
            cues.Add(MakeCue(stepId, "emissionPulse", "onDuringAction", c =>
            {
                c.fromEmission  = new SceneFloat4 { r = 0f,   g = 0f,    b = 0f, a = 1f };
                c.toEmission    = new SceneFloat4 { r = 0.9f, g = 0.95f, b = 1f, a = 1f };
                c.fromIntensity = 0f;
                c.toIntensity   = 1.5f;
                c.startProgress = 0.1f;
                c.endProgress   = 1f;
            }));

            // Torch wobble — WeldPreview.cs:123 (amp 0.12 rad, freq 40 rad/s, pitch+yaw)
            cues.Add(MakeCue(stepId, "poseWobble", "onDuringAction", c =>
            {
                c.wobbleAxis      = new SceneFloat3 { x = 1f, y = 1f, z = 0f };
                c.wobbleAmplitude = 0.12f;
                c.wobbleFrequency = 40f;
                c.startProgress   = 0.05f;
                c.endProgress     = 0.95f;
            }));

            // Bead line — WeldPreview.cs:128, 131-177 (20% spawn, extend 20-90%)
            cues.Add(MakeCue(stepId, "lineBetweenAnchors", "onDuringAction", c =>
            {
                c.anchorARef             = "weldStart";
                c.anchorBRef             = "weldEnd";
                c.lineWidth              = 0.004f;
                c.fromColor              = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                c.fromEmission           = new SceneFloat4 { r = 1f,    g = 0.9f,  b = 0.7f,  a = 1f };
                c.lineEmissionIntensity  = 1.5f;
                c.startProgress          = 0.2f;
                c.endProgress            = 0.9f;
            }));

            // Bead cooling — WeldPreview.cs:197-242 (WeldBeadCooler: hot silver → cool grey, 2s)
            cues.Add(MakeCue(stepId, "materialFade", "onStepComplete", c =>
            {
                c.fromColor       = new SceneFloat4 { r = 0.85f, g = 0.82f, b = 0.72f, a = 1f };
                c.toColor         = new SceneFloat4 { r = 0.55f, g = 0.55f, b = 0.52f, a = 1f };
                c.fromEmission    = new SceneFloat4 { r = 1.00f, g = 0.90f, b = 0.70f, a = 1f };
                c.toEmission      = new SceneFloat4 { r = 0f,    g = 0f,    b = 0f,    a = 1f };
                c.fromIntensity   = 1.5f;
                c.toIntensity     = 0f;
                c.durationSeconds = 2f;
            }));
        }

        // ── Drill (mirrors DrillPreview.cs) ───────────────────────────────

        private static void AppendDrillCues(List<AnimationCueEntry> cues, string stepId)
        {
            // Blue emission glow — DrillPreview.cs:96 (Color(0.3, 0.7, 1.0) × 0.5)
            cues.Add(MakeCue(stepId, "emissionPulse", "onDuringAction", c =>
            {
                c.fromEmission  = new SceneFloat4 { r = 0f,   g = 0f,   b = 0f, a = 1f };
                c.toEmission    = new SceneFloat4 { r = 0.3f, g = 0.7f, b = 1f, a = 1f };
                c.fromIntensity = 0f;
                c.toIntensity   = 0.5f;
                // Envelope ramp mimicking DrillPreview.ComputeIntensity.
                c.startProgress = 0f;
                c.endProgress   = 1f;
            }));

            // Vibration — DrillPreview.cs:27-28 (55 Hz, 0.0004m amp, 0.15/0.85 ramp)
            cues.Add(MakeCue(stepId, "toolVibration", "onDuringAction", c =>
            {
                c.vibrationAxes      = new SceneFloat3 { x = 0.0004f, y = 0.00028f, z = 0.0002f };
                c.vibrationFrequency = 55f;
                c.vibrationRampIn    = 0.15f;
                c.vibrationRampOut   = 0.85f;
                c.startProgress      = 0f;
                c.endProgress        = 1f;
            }));

            // First spark burst — DrillPreview.cs:100-104 (@ 0.4, scale 0.06)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "torque_sparks";
                c.particleScale      = 1f;
                c.startProgress      = 0.4f;
                c.endProgress        = 0.5f;
            }));

            // Second spark burst — DrillPreview.cs:106-110 (@ 0.8, scale 0.04)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "torque_sparks";
                c.particleScale      = 0.67f;
                c.startProgress      = 0.8f;
                c.endProgress        = 0.9f;
            }));
        }

        // ── Torque (mirrors TorquePreview.cs) ─────────────────────────────

        private static void AppendTorqueCues(List<AnimationCueEntry> cues, string stepId)
        {
            // 120° wrench sweep — TorquePreview.cs:31 (TargetAngle)
            cues.Add(MakeCue(stepId, "screwSpin", "onDuringAction", c =>
            {
                c.spinAngleDegrees = 120f;
                c.spinAxis         = new SceneFloat3 { x = 0f, y = 1f, z = 0f };
                c.startProgress    = 0f;
                c.endProgress      = 1f;
            }));

            // Spark burst at halfway — TorquePreview.cs:151-155 (@ 0.5, scale 0.1)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "torque_sparks";
                c.particleScale      = 1.67f;
                c.startProgress      = 0.5f;
                c.endProgress        = 0.6f;
            }));
        }

        // ── Cut (mirrors CutPreview.cs) ───────────────────────────────────

        private static void AppendCutCues(List<AnimationCueEntry> cues, string stepId)
        {
            // Orange emission — CutPreview.cs:54-55 (Color(1.0, 0.5, 0.1))
            cues.Add(MakeCue(stepId, "emissionPulse", "onDuringAction", c =>
            {
                c.fromEmission  = new SceneFloat4 { r = 0f, g = 0f,   b = 0f,   a = 1f };
                c.toEmission    = new SceneFloat4 { r = 1f, g = 0.5f, b = 0.1f, a = 1f };
                c.fromIntensity = 0f;
                c.toIntensity   = 1f;
                c.startProgress = 0.15f;
                c.endProgress   = 1f;
            }));

            // Vibration X-only high-freq — CutPreview.cs:100-101 (80 Hz, 0.15 × 0.001m)
            cues.Add(MakeCue(stepId, "toolVibration", "onDuringAction", c =>
            {
                c.vibrationAxes      = new SceneFloat3 { x = 0.00015f, y = 0f, z = 0f };
                c.vibrationFrequency = 80f;
                c.vibrationRampIn    = 0.15f;
                c.vibrationRampOut   = 0.9f;
                c.startProgress      = 0.15f;
                c.endProgress        = 0.9f;
            }));

            // First spark burst — CutPreview.cs:58-62 (@ 0.2, scale 0.12)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "torque_sparks";
                c.particleScale      = 2f;
                c.startProgress      = 0.2f;
                c.endProgress        = 0.3f;
            }));

            // Second spark burst — CutPreview.cs:66-70 (@ 0.6, scale 0.08)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "torque_sparks";
                c.particleScale      = 1.33f;
                c.startProgress      = 0.6f;
                c.endProgress        = 0.7f;
            }));

            // Scorch line — CutPreview.cs:74, 79-84 (extends 25-90%)
            cues.Add(MakeCue(stepId, "lineBetweenAnchors", "onDuringAction", c =>
            {
                c.anchorARef             = "weldStart";
                c.anchorBRef             = "weldEnd";
                c.lineWidth              = 0.002f;
                c.fromColor              = new SceneFloat4 { r = 0.1f, g = 0.05f, b = 0.02f, a = 1f };
                c.fromEmission           = new SceneFloat4 { r = 1f,   g = 0.3f,  b = 0.05f, a = 1f };
                c.lineEmissionIntensity  = 0.5f;
                c.startProgress          = 0.25f;
                c.endProgress            = 0.9f;
            }));
        }

        // ── Square-check (mirrors SquareCheckPreview.cs) ──────────────────

        private static void AppendSquareCues(List<AnimationCueEntry> cues, string stepId)
        {
            // Settle emission glow — SquareCheckPreview.cs:86-90 (green (0.1, 0.8, 0.2) × 1.5)
            cues.Add(MakeCue(stepId, "emissionPulse", "onDuringAction", c =>
            {
                c.fromEmission  = new SceneFloat4 { r = 0f,   g = 0f,   b = 0f,   a = 1f };
                c.toEmission    = new SceneFloat4 { r = 0.1f, g = 0.8f, b = 0.2f, a = 1f };
                c.fromIntensity = 0f;
                c.toIntensity   = 1.5f;
                c.startProgress = 0.4f;
                c.endProgress   = 0.7f;
            }));

            // Confirm pulse — SquareCheckPreview.cs:95-97 (sine @ 2 Hz, intensity × 2)
            cues.Add(MakeCue(stepId, "emissionPulse", "onDuringAction", c =>
            {
                c.fromEmission  = new SceneFloat4 { r = 0.1f, g = 0.9f, b = 0.2f, a = 1f };
                c.toEmission    = new SceneFloat4 { r = 0.1f, g = 0.9f, b = 0.2f, a = 1f };
                c.fromIntensity = 1.5f;
                c.toIntensity   = 2f;
                c.startProgress = 0.7f;
                c.endProgress   = 1f;
            }));

            // Confirm particle — SquareCheckPreview.cs:100-105 (square_confirm @ 0.7, scale 0.08)
            cues.Add(MakeCue(stepId, "particle", "onDuringAction", c =>
            {
                c.particleSourceMode = "preset";
                c.particlePresetId   = "square_confirm";
                c.particleScale      = 1.33f;
                c.startProgress      = 0.7f;
                c.endProgress        = 0.8f;
            }));
        }

        // ── Measure (mirrors MeasureInteractionHandler.cs) ────────────────

        private static void AppendMeasureCues(List<AnimationCueEntry> cues, string stepId)
        {
            // A→B line with live label — MeasurementLineVisual.cs (width 0.008, yellow (1, 0.8, 0.2))
            cues.Add(MakeCue(stepId, "measureLine", "onActivate", c =>
            {
                c.anchorARef    = "measureAnchorA";
                c.anchorBRef    = "measureAnchorB";
                c.measureUnit   = "mm";
                c.fromColor     = new SceneFloat4 { r = 1f, g = 0.8f, b = 0.2f, a = 1f };
                c.lineWidth     = 0.008f;
            }));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static AnimationCueEntry MakeCue(string stepId, string type, string trigger,
            Action<AnimationCueEntry> configure)
        {
            var cue = new AnimationCueEntry
            {
                type    = type,
                trigger = trigger,
                stepIds = new[] { stepId },
            };
            configure?.Invoke(cue);
            return cue;
        }
    }
}
