using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OSE.Content;
using OSE.Content.Loading;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Determinism / isolation harness. Catches regressions where the runtime's
    /// in-memory model diverges from what's authored on disk.
    ///
    /// Background: Unity's <c>JsonUtility</c> creates a default instance for
    /// any [Serializable] reference field absent from the JSON, instead of
    /// leaving it null. Without intervention, every step ends up with non-null
    /// workingOrientation / animationCues / particleEffects, even when the
    /// author never specified them — which is the root-cause class of the
    /// step-263 phantom-orientation bug (instruction text appended a
    /// "rotated to expose the work area" paragraph for every step).
    ///
    /// The fix lives in <see cref="MachinePackageNormalizer.DropEmptyStepPayloads"/>,
    /// which nulls those phantoms after deserialization. These tests verify the
    /// normalizer pass actually drops them — if someone removes or breaks that
    /// pass, the runtime regresses to the phantom-payload behavior and these
    /// tests fail.
    /// </summary>
    [TestFixture]
    public class EditorRuntimeIsolationTests
    {
        // The canonical regression target. Any package with at least one step
        // that lacks workingOrientation works; d3d_v18_10 is the package the
        // step-263 bug was reported against, so it's the most direct guard.
        private const string FixturePackageId = "d3d_v18_10";

        private static string PackageRootAuthoring =>
            Path.Combine(Application.dataPath, "_Project", "Data", "Packages", FixturePackageId);

        // ── Test 1: Parser must not inject empty StepWorkingOrientationPayload ──

        /// <summary>
        /// For every step in the on-disk assembly JSON files, assert: if the
        /// raw JSON does NOT contain the "workingOrientation" key, the parsed
        /// <see cref="StepDefinition.workingOrientation"/> must be null.
        ///
        /// This is the precise pattern that broke at step 263: TTAW (and any
        /// other code reading the parsed model) infers from a non-null payload
        /// that the step has been authored with a working orientation, then
        /// runs orientation-related code paths (instruction text, animation,
        /// rotation gizmo) that the author never asked for.
        /// </summary>
        [Test]
        public void Loaded_Steps_Have_Null_WorkingOrientation_When_Json_Has_None()
        {
            string assembliesDir = Path.Combine(PackageRootAuthoring, "assemblies");
            if (!Directory.Exists(assembliesDir))
            {
                Assert.Ignore($"Fixture package '{FixturePackageId}' has no assemblies/ directory at '{assembliesDir}'. Run on a machine where the package is checked in.");
                return;
            }

            string[] assemblyFiles = Directory.GetFiles(assembliesDir, "*.json");
            Assert.That(assemblyFiles, Is.Not.Empty, "Fixture package has no assembly JSON files.");

            int totalStepsChecked = 0;
            int stepsWithoutOrientationInJson = 0;

            foreach (string file in assemblyFiles)
            {
                string raw = File.ReadAllText(file);

                // Parse via the same serializer the runtime uses.
                AssemblyFileShape parsed = JsonUtility.FromJson<AssemblyFileShape>(raw);
                if (parsed?.steps == null) continue;

                // Wrap parsed steps in a package so we can run the normalizer's
                // phantom-payload purge — the same pass the runtime invokes
                // immediately after load. Tests the *post-normalization* model,
                // which is what the runtime actually consumes.
                var package = new MachinePackageDefinition { steps = parsed.steps };
                MachinePackageNormalizer.DropEmptyStepPayloads(package);

                foreach (StepDefinition step in parsed.steps)
                {
                    if (step == null || string.IsNullOrEmpty(step.id)) continue;
                    totalStepsChecked++;

                    if (StepRawJsonContainsWorkingOrientation(raw, step.id)) continue;

                    stepsWithoutOrientationInJson++;
                    Assert.IsNull(
                        step.workingOrientation,
                        $"Step '{step.id}' in '{Path.GetFileName(file)}' has NO workingOrientation in JSON, " +
                        $"but after MachinePackageNormalizer.DropEmptyStepPayloads the field is non-null. " +
                        $"Either the normalizer pass was removed/broken, or " +
                        $"StepWorkingOrientationPayload.IsEmpty() no longer recognizes the default-instance shape " +
                        $"created by JsonUtility. Re-check both.");
                }
            }

            Assert.Greater(totalStepsChecked, 0, "Test verified zero steps — fixture package is empty?");
            Assert.Greater(stepsWithoutOrientationInJson, 0,
                "Every step in the fixture has workingOrientation defined — test gives no signal. " +
                "Either pick a different fixture or add a step without workingOrientation.");
        }

        // ── Test 2: Parser must not inject empty StepAnimationCuePayload ────────

        /// <summary>
        /// Same contract as <see cref="Loaded_Steps_Have_Null_WorkingOrientation_When_Json_Has_None"/>
        /// but for <see cref="StepDefinition.animationCues"/>. This payload also
        /// has lazy-init temptation in TTAW edit panels and triggers preview
        /// animations when present.
        /// </summary>
        [Test]
        public void Loaded_Steps_Have_Null_AnimationCues_When_Json_Has_None()
        {
            string assembliesDir = Path.Combine(PackageRootAuthoring, "assemblies");
            if (!Directory.Exists(assembliesDir))
            {
                Assert.Ignore($"Fixture package '{FixturePackageId}' missing.");
                return;
            }

            int totalChecked = 0;
            int stepsWithoutCuesInJson = 0;

            foreach (string file in Directory.GetFiles(assembliesDir, "*.json"))
            {
                string raw = File.ReadAllText(file);
                AssemblyFileShape parsed = JsonUtility.FromJson<AssemblyFileShape>(raw);
                if (parsed?.steps == null) continue;

                var package = new MachinePackageDefinition { steps = parsed.steps };
                MachinePackageNormalizer.DropEmptyStepPayloads(package);

                foreach (StepDefinition step in parsed.steps)
                {
                    if (step == null || string.IsNullOrEmpty(step.id)) continue;
                    totalChecked++;
                    if (StepRawJsonContainsKey(raw, step.id, "animationCues")) continue;

                    stepsWithoutCuesInJson++;
                    Assert.IsNull(
                        step.animationCues,
                        $"Step '{step.id}' in '{Path.GetFileName(file)}' has NO animationCues in JSON, " +
                        $"but after DropEmptyStepPayloads the field is non-null. Check the normalizer pass " +
                        $"and StepAnimationCuePayload.IsEmpty().");
                }
            }

            Assert.Greater(totalChecked, 0);
            Assert.Greater(stepsWithoutCuesInJson, 0);
        }

        // ── Test 3: Parser must not inject empty StepParticleEffectPayload ─────

        [Test]
        public void Loaded_Steps_Have_Null_ParticleEffects_When_Json_Has_None()
        {
            string assembliesDir = Path.Combine(PackageRootAuthoring, "assemblies");
            if (!Directory.Exists(assembliesDir))
            {
                Assert.Ignore($"Fixture package '{FixturePackageId}' missing.");
                return;
            }

            int totalChecked = 0;
            int stepsWithoutEffectsInJson = 0;

            foreach (string file in Directory.GetFiles(assembliesDir, "*.json"))
            {
                string raw = File.ReadAllText(file);
                AssemblyFileShape parsed = JsonUtility.FromJson<AssemblyFileShape>(raw);
                if (parsed?.steps == null) continue;

                var package = new MachinePackageDefinition { steps = parsed.steps };
                MachinePackageNormalizer.DropEmptyStepPayloads(package);

                foreach (StepDefinition step in parsed.steps)
                {
                    if (step == null || string.IsNullOrEmpty(step.id)) continue;
                    totalChecked++;
                    if (StepRawJsonContainsKey(raw, step.id, "particleEffects")) continue;

                    stepsWithoutEffectsInJson++;
                    Assert.IsNull(
                        step.particleEffects,
                        $"Step '{step.id}' in '{Path.GetFileName(file)}' has NO particleEffects in JSON, " +
                        $"but after DropEmptyStepPayloads the field is non-null. Check the normalizer pass " +
                        $"and StepParticleEffectPayload.IsEmpty().");
                }
            }

            Assert.Greater(totalChecked, 0);
            Assert.Greater(stepsWithoutEffectsInJson, 0);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Heuristic: does the raw JSON of an assembly file contain a
        /// "workingOrientation" key inside the step block whose id matches
        /// <paramref name="stepId"/>? Uses a regex that finds the step's
        /// id in a "id":"<id>" field, then looks within ~2KB after for the
        /// "workingOrientation" key. Good enough for round-trip detection;
        /// the false-negative direction (says "absent" when present) would
        /// just make the test more conservative, never less.
        /// </summary>
        private static bool StepRawJsonContainsWorkingOrientation(string raw, string stepId)
            => StepRawJsonContainsKey(raw, stepId, "workingOrientation");

        private static bool StepRawJsonContainsKey(string raw, string stepId, string keyName)
        {
            // Find the position of "id": "<stepId>"
            string escaped = Regex.Escape(stepId);
            Match idMatch = Regex.Match(raw, $"\"id\"\\s*:\\s*\"{escaped}\"");
            if (!idMatch.Success) return false;

            // Scan forward to find the next "id": (start of next step block)
            // or end of string. The key we care about must appear before that.
            int blockStart = idMatch.Index;
            Match nextIdMatch = Regex.Match(raw.Substring(blockStart + idMatch.Length),
                "\"id\"\\s*:\\s*\"step_");
            int blockEnd = nextIdMatch.Success
                ? blockStart + idMatch.Length + nextIdMatch.Index
                : raw.Length;

            string block = raw.Substring(blockStart, blockEnd - blockStart);
            return Regex.IsMatch(block, $"\"{Regex.Escape(keyName)}\"\\s*:");
        }

        /// <summary>
        /// Minimal shape for parsing assembly JSON files. The full
        /// <see cref="MachinePackageDefinition"/> isn't needed since each
        /// assembly file is a self-contained subset — assemblies, parts,
        /// partGroups, steps, targets, hints. Only `steps` is exercised
        /// by these tests; declaring the rest would just bloat the fixture.
        /// </summary>
        [System.Serializable]
        private sealed class AssemblyFileShape
        {
            public StepDefinition[] steps;
        }
    }
}
