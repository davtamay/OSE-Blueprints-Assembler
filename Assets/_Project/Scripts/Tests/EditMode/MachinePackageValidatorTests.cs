using NUnit.Framework;
using OSE.Content;
using OSE.Content.Validation;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class MachinePackageValidatorTests
    {
        [Test]
        public void Null_Package_Returns_Error()
        {
            var result = MachinePackageValidator.Validate(null);

            Assert.IsTrue(result.HasErrors);
            Assert.AreEqual(1, result.Issues.Length);
            StringAssert.Contains("null", result.Issues[0].Message);
        }

        [Test]
        public void Empty_Package_Reports_Missing_Required_Fields()
        {
            var package = new MachinePackageDefinition();

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            Assert.IsTrue(result.Issues.Length > 0);
        }

        [Test]
        public void Minimal_Valid_Package_Has_No_Errors()
        {
            var package = CreateMinimalValidPackage();

            var result = MachinePackageValidator.Validate(package);

            Assert.IsFalse(result.HasErrors,
                $"Expected no errors but got:\n{result.FormatSummary(20)}");
        }

        [Test]
        public void Missing_SchemaVersion_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.schemaVersion = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "schemaVersion");
        }

        [Test]
        public void Missing_Machine_Definition_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "Machine definition");
        }

        [Test]
        public void Duplicate_Assembly_Ids_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.assemblies = new[]
            {
                new AssemblyDefinition { id = "asm_1", name = "Assembly 1", stepIds = new[] { "step_1" } },
                new AssemblyDefinition { id = "asm_1", name = "Assembly Dup", stepIds = new[] { "step_1" } }
            };
            package.machine.entryAssemblyIds = new[] { "asm_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "Duplicate");
        }

        [Test]
        public void Invalid_Difficulty_Reported_As_Warning()
        {
            var package = CreateMinimalValidPackage();
            package.machine.difficulty = "impossible";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
        }

        [Test]
        public void Step_With_Invalid_Family_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].family = "InvalidFamily";

            var result = MachinePackageValidator.Validate(package);

            bool hasRelevantIssue = false;
            foreach (var issue in result.Issues)
            {
                if (issue.Message.Contains("family") || issue.Message.Contains("Family"))
                {
                    hasRelevantIssue = true;
                    break;
                }
            }
            Assert.IsTrue(hasRelevantIssue, "Expected a warning/error about invalid family value.");
        }

        [Test]
        public void Step_Referencing_Missing_Part_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].requiredPartIds = new[] { "nonexistent_part" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors || result.HasWarnings);
        }

        [Test]
        public void FormatSummary_With_No_Issues_Returns_Clean_Message()
        {
            var result = MachinePackageValidationResult.Valid;
            Assert.AreEqual("No validation issues.", result.FormatSummary());
        }

        [Test]
        public void FormatSummary_Truncates_When_Many_Issues()
        {
            var issues = new MachinePackageValidationIssue[10];
            for (int i = 0; i < 10; i++)
                issues[i] = new MachinePackageValidationIssue(
                    MachinePackageIssueSeverity.Error, $"path_{i}", $"Error {i}");

            var result = new MachinePackageValidationResult(issues);
            string summary = result.FormatSummary(3);

            StringAssert.Contains("more issue(s)", summary);
        }

        // ── Helpers ──

        private static MachinePackageDefinition CreateMinimalValidPackage()
        {
            return new MachinePackageDefinition
            {
                schemaVersion = "1.0.0",
                packageVersion = "0.1.0",
                machine = new MachineDefinition
                {
                    id = "test_machine",
                    name = "Test Machine",
                    description = "A test machine for unit tests.",
                    difficulty = "beginner",
                    entryAssemblyIds = new[] { "asm_1" }
                },
                assemblies = new[]
                {
                    new AssemblyDefinition
                    {
                        id = "asm_1",
                        name = "Assembly 1",
                        stepIds = new[] { "step_1" }
                    }
                },
                parts = new[]
                {
                    new PartDefinition { id = "part_1", name = "Part 1", assetRef = "models/part1.glb" }
                },
                tools = new[]
                {
                    new ToolDefinition { id = "tool_1", name = "Wrench", assetRef = "models/wrench.glb" }
                },
                steps = new[]
                {
                    new StepDefinition
                    {
                        id = "step_1",
                        name = "Place Part 1",
                        family = "Place",
                        completionType = "placement",
                        requiredPartIds = new[] { "part_1" },
                        targetIds = new[] { "target_1" }
                    }
                },
                targets = new[]
                {
                    new TargetDefinition { id = "target_1", associatedPartId = "part_1" }
                },
                validationRules = System.Array.Empty<ValidationRuleDefinition>(),
                hints = System.Array.Empty<HintDefinition>(),
                effects = System.Array.Empty<EffectDefinition>()
            };
        }

        private static void AssertHasIssueContaining(MachinePackageValidationResult result, string substring)
        {
            foreach (var issue in result.Issues)
            {
                if (issue.Message.Contains(substring) || issue.Path.Contains(substring))
                    return;
            }
            Assert.Fail($"Expected an issue containing '{substring}'. Issues:\n{result.FormatSummary(20)}");
        }
    }
}
