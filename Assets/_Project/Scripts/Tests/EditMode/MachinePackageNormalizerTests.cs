using NUnit.Framework;
using OSE.Content;
using OSE.Content.Loading;
using UnityEngine.TestTools;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class MachinePackageNormalizerTests
    {
        [Test]
        public void Normalize_Null_Package_Does_Not_Throw()
        {
            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(null));
        }

        [Test]
        public void InflatePartTemplates_Fills_Empty_Fields_From_Template()
        {
            var package = new MachinePackageDefinition
            {
                partTemplates = new[]
                {
                    new PartTemplateDefinition
                    {
                        id = "template_bolt",
                        name = "Bolt Template",
                        displayName = "Hex Bolt",
                        category = "fastener",
                        material = "steel",
                        function = "Secure two plates",
                        assetRef = "models/bolt.glb",
                        quantity = 4
                    }
                },
                parts = new[]
                {
                    new PartDefinition
                    {
                        id = "bolt_01",
                        templateId = "template_bolt"
                        // All other fields empty — should be filled from template
                    }
                },
                assemblies = System.Array.Empty<AssemblyDefinition>(),
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                steps = System.Array.Empty<StepDefinition>()
            };

            MachinePackageNormalizer.Normalize(package);

            var part = package.parts[0];
            Assert.AreEqual("Bolt Template", part.name);
            Assert.AreEqual("Hex Bolt", part.displayName);
            Assert.AreEqual("fastener", part.category);
            Assert.AreEqual("steel", part.material);
            Assert.AreEqual("Secure two plates", part.function);
            Assert.AreEqual("models/bolt.glb", part.assetRef);
            Assert.AreEqual(4, part.quantity);
        }

        [Test]
        public void InflatePartTemplates_Does_Not_Override_Existing_Fields()
        {
            var package = new MachinePackageDefinition
            {
                partTemplates = new[]
                {
                    new PartTemplateDefinition
                    {
                        id = "template_bolt",
                        name = "Bolt Template",
                        displayName = "Generic Bolt",
                        category = "fastener"
                    }
                },
                parts = new[]
                {
                    new PartDefinition
                    {
                        id = "bolt_custom",
                        templateId = "template_bolt",
                        name = "Custom Bolt",
                        displayName = "My Custom Bolt"
                    }
                },
                assemblies = System.Array.Empty<AssemblyDefinition>(),
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                steps = System.Array.Empty<StepDefinition>()
            };

            MachinePackageNormalizer.Normalize(package);

            var part = package.parts[0];
            Assert.AreEqual("Custom Bolt", part.name, "Existing name should not be overridden");
            Assert.AreEqual("My Custom Bolt", part.displayName, "Existing displayName should not be overridden");
            Assert.AreEqual("fastener", part.category, "Empty field should be filled from template");
        }

        [Test]
        public void InflatePartTemplates_Ignores_Missing_Template()
        {
            var package = new MachinePackageDefinition
            {
                partTemplates = System.Array.Empty<PartTemplateDefinition>(),
                parts = new[]
                {
                    new PartDefinition
                    {
                        id = "bolt_orphan",
                        templateId = "nonexistent_template",
                        name = "Orphan Bolt"
                    }
                },
                assemblies = System.Array.Empty<AssemblyDefinition>(),
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                steps = System.Array.Empty<StepDefinition>()
            };

            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(package));
            Assert.AreEqual("Orphan Bolt", package.parts[0].name);
        }

        [Test]
        public void InferStepParentIds_Sets_AssemblyId_On_Steps()
        {
            var package = new MachinePackageDefinition
            {
                assemblies = new[]
                {
                    new AssemblyDefinition
                    {
                        id = "asm_1",
                        stepIds = new[] { "step_a", "step_b" }
                    }
                },
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                steps = new[]
                {
                    new StepDefinition { id = "step_a" },
                    new StepDefinition { id = "step_b" }
                },
                parts = System.Array.Empty<PartDefinition>(),
                partTemplates = System.Array.Empty<PartTemplateDefinition>()
            };

            MachinePackageNormalizer.Normalize(package);

            Assert.AreEqual("asm_1", package.steps[0].assemblyId);
            Assert.AreEqual("asm_1", package.steps[1].assemblyId);
        }

        [Test]
        public void InferStepParentIds_Sets_SubassemblyId_On_Steps()
        {
            var package = new MachinePackageDefinition
            {
                assemblies = new[]
                {
                    new AssemblyDefinition
                    {
                        id = "asm_1",
                        subassemblyIds = new[] { "sub_1" },
                        stepIds = new[] { "step_a" }
                    }
                },
                subassemblies = new[]
                {
                    new SubassemblyDefinition
                    {
                        id = "sub_1",
                        stepIds = new[] { "step_a" }
                    }
                },
                steps = new[]
                {
                    new StepDefinition { id = "step_a" }
                },
                parts = System.Array.Empty<PartDefinition>(),
                partTemplates = System.Array.Empty<PartTemplateDefinition>()
            };

            MachinePackageNormalizer.Normalize(package);

            Assert.AreEqual("sub_1", package.steps[0].subassemblyId);
        }

        // ──────────────────────────────────────────────────────────────────
        // Phase I.a — unorderedSet validation
        // ──────────────────────────────────────────────────────────────────

        private static MachinePackageDefinition PkgWithTaskOrder(params TaskOrderEntry[] taskOrder)
            => new MachinePackageDefinition
            {
                assemblies    = System.Array.Empty<AssemblyDefinition>(),
                subassemblies = System.Array.Empty<SubassemblyDefinition>(),
                parts         = System.Array.Empty<PartDefinition>(),
                partTemplates = System.Array.Empty<PartTemplateDefinition>(),
                steps = new[]
                {
                    new StepDefinition
                    {
                        id = "s1",
                        sequenceIndex = 1,
                        taskOrder = taskOrder,
                    }
                }
            };

        [Test]
        public void UnorderedSetValidate_StrictSequentialSingletons_NoLogs()
        {
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part", id = "a" },
                new TaskOrderEntry { kind = "part", id = "b" },
                new TaskOrderEntry { kind = "toolAction", id = "c" });

            // No Expect calls — LogAssert fails the test on any unexpected log.
            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(pkg));
        }

        [Test]
        public void UnorderedSetValidate_ValidContiguousSameKindSpan_NoLogs()
        {
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part", id = "a", unorderedSet = "panel" },
                new TaskOrderEntry { kind = "part", id = "b", unorderedSet = "panel" },
                new TaskOrderEntry { kind = "part", id = "c", unorderedSet = "panel" },
                new TaskOrderEntry { kind = "part", id = "c", unorderedSet = "panel" });

            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(pkg));
        }

        [Test]
        public void UnorderedSetValidate_ContiguityViolation_LogsError()
        {
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part", id = "a", unorderedSet = "alpha" },
                new TaskOrderEntry { kind = "part", id = "b", unorderedSet = "alpha" },
                new TaskOrderEntry { kind = "part", id = "c" /* break */ },
                new TaskOrderEntry { kind = "part", id = "d", unorderedSet = "alpha" /* reappears */ });

            // Error message includes "reappears as a non-contiguous span"
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[UnorderedSet\.Validate\].*step 's1'.*unorderedSet 'alpha' reappears"));
            MachinePackageNormalizer.Normalize(pkg);
        }

        [Test]
        public void UnorderedSetValidate_KindPurityViolation_LogsError()
        {
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part",       id = "a", unorderedSet = "mixed" },
                new TaskOrderEntry { kind = "toolAction", id = "b", unorderedSet = "mixed" });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[UnorderedSet\.Validate\].*step 's1'.*unorderedSet 'mixed' mixes kinds"));
            MachinePackageNormalizer.Normalize(pkg);
        }

        [Test]
        public void UnorderedSetValidate_SingleMemberSet_LogsWarning()
        {
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part", id = "a" },
                new TaskOrderEntry { kind = "part", id = "b", unorderedSet = "lonely" });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                @"\[UnorderedSet\.Validate\].*step 's1'.*unorderedSet 'lonely' has only 1 member"));
            MachinePackageNormalizer.Normalize(pkg);
        }

        [Test]
        public void UnorderedSetValidate_TwoDistinctAdjacentSpans_NoLogs()
        {
            // Two sets back-to-back, different labels, both kind-pure, each 2+ members.
            var pkg = PkgWithTaskOrder(
                new TaskOrderEntry { kind = "part", id = "a", unorderedSet = "first" },
                new TaskOrderEntry { kind = "part", id = "b", unorderedSet = "first" },
                new TaskOrderEntry { kind = "toolAction", id = "c", unorderedSet = "second" },
                new TaskOrderEntry { kind = "toolAction", id = "d", unorderedSet = "second" });

            Assert.DoesNotThrow(() => MachinePackageNormalizer.Normalize(pkg));
        }
    }
}
