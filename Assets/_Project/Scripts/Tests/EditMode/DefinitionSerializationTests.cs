using NUnit.Framework;
using OSE.Content;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class DefinitionSerializationTests
    {
        [Test]
        public void MachineDefinition_RoundTrips_Through_Json()
        {
            var original = new MachineDefinition
            {
                id = "test_machine",
                name = "Test Machine",
                displayName = "Test Display",
                description = "A machine for testing.",
                difficulty = "beginner",
                estimatedBuildTimeMinutes = 30,
                learningObjectives = new[] { "Learn bolt placement", "Learn torque" },
                recommendedMode = "tutorial",
                entryAssemblyIds = new[] { "asm_1" }
            };

            string json = JsonUtility.ToJson(original);
            var deserialized = JsonUtility.FromJson<MachineDefinition>(json);

            Assert.AreEqual(original.id, deserialized.id);
            Assert.AreEqual(original.name, deserialized.name);
            Assert.AreEqual(original.displayName, deserialized.displayName);
            Assert.AreEqual(original.description, deserialized.description);
            Assert.AreEqual(original.difficulty, deserialized.difficulty);
            Assert.AreEqual(original.estimatedBuildTimeMinutes, deserialized.estimatedBuildTimeMinutes);
            Assert.AreEqual(original.learningObjectives, deserialized.learningObjectives);
            Assert.AreEqual(original.recommendedMode, deserialized.recommendedMode);
            Assert.AreEqual(original.entryAssemblyIds, deserialized.entryAssemblyIds);
        }

        [Test]
        public void StepDefinition_RoundTrips_Through_Json()
        {
            var original = new StepDefinition
            {
                id = "step_1",
                name = "Place Bolt",
                family = "Place",
                profile = "Clamp",
                completionType = "placement",
                sequenceIndex = 0,
                instructionText = "Place the bolt on the frame.",
                requiredPartIds = new[] { "part_bolt" },
                targetIds = new[] { "target_1" }
            };

            string json = JsonUtility.ToJson(original);
            var deserialized = JsonUtility.FromJson<StepDefinition>(json);

            Assert.AreEqual(original.id, deserialized.id);
            Assert.AreEqual(original.name, deserialized.name);
            Assert.AreEqual(original.family, deserialized.family);
            Assert.AreEqual(original.profile, deserialized.profile);
            Assert.AreEqual(original.completionType, deserialized.completionType);
            Assert.AreEqual(original.sequenceIndex, deserialized.sequenceIndex);
            Assert.AreEqual(original.instructionText, deserialized.instructionText);
            Assert.AreEqual(original.requiredPartIds, deserialized.requiredPartIds);
            Assert.AreEqual(original.targetIds, deserialized.targetIds);
        }

        [Test]
        public void PartDefinition_RoundTrips_Through_Json()
        {
            var original = new PartDefinition
            {
                id = "part_bolt",
                name = "Hex Bolt",
                displayName = "M10 Hex Bolt",
                category = "fastener",
                material = "steel",
                assetRef = "models/bolt.glb",
                quantity = 4,
                toolIds = new[] { "tool_wrench" },
                templateId = "template_bolt"
            };

            string json = JsonUtility.ToJson(original);
            var deserialized = JsonUtility.FromJson<PartDefinition>(json);

            Assert.AreEqual(original.id, deserialized.id);
            Assert.AreEqual(original.name, deserialized.name);
            Assert.AreEqual(original.displayName, deserialized.displayName);
            Assert.AreEqual(original.category, deserialized.category);
            Assert.AreEqual(original.material, deserialized.material);
            Assert.AreEqual(original.assetRef, deserialized.assetRef);
            Assert.AreEqual(original.quantity, deserialized.quantity);
            Assert.AreEqual(original.toolIds, deserialized.toolIds);
            Assert.AreEqual(original.templateId, deserialized.templateId);
        }

        [Test]
        public void MachinePackageDefinition_RoundTrips_Through_Json()
        {
            var original = new MachinePackageDefinition
            {
                schemaVersion = "1.0.0",
                packageVersion = "0.5.0",
                machine = new MachineDefinition
                {
                    id = "test",
                    name = "Test",
                    description = "Desc"
                },
                assemblies = new[]
                {
                    new AssemblyDefinition { id = "asm_1", name = "Asm 1" }
                },
                parts = new[]
                {
                    new PartDefinition { id = "p1", name = "Part 1" }
                },
                steps = new[]
                {
                    new StepDefinition { id = "s1", name = "Step 1", family = "Place" }
                }
            };

            string json = JsonUtility.ToJson(original);
            var deserialized = JsonUtility.FromJson<MachinePackageDefinition>(json);

            Assert.AreEqual("1.0.0", deserialized.schemaVersion);
            Assert.AreEqual("0.5.0", deserialized.packageVersion);
            Assert.AreEqual("test", deserialized.machine.id);
            Assert.AreEqual(1, deserialized.assemblies.Length);
            Assert.AreEqual("asm_1", deserialized.assemblies[0].id);
            Assert.AreEqual(1, deserialized.parts.Length);
            Assert.AreEqual(1, deserialized.steps.Length);
        }

        [Test]
        public void MachineDefinition_GetDisplayName_Prefers_DisplayName()
        {
            var def = new MachineDefinition { name = "Internal", displayName = "Pretty Name" };
            Assert.AreEqual("Pretty Name", def.GetDisplayName());
        }

        [Test]
        public void MachineDefinition_GetDisplayName_Falls_Back_To_Name()
        {
            var def = new MachineDefinition { name = "Internal Name" };
            Assert.AreEqual("Internal Name", def.GetDisplayName());
        }

        [Test]
        public void MachineDefinition_GetDisplayName_Returns_Default_When_Empty()
        {
            var def = new MachineDefinition();
            Assert.AreEqual("Unnamed Machine", def.GetDisplayName());
        }

        [Test]
        public void MachinePackageDefinition_GetParts_Returns_Empty_When_Null()
        {
            var package = new MachinePackageDefinition();
            Assert.IsNotNull(package.GetParts());
            Assert.AreEqual(0, package.GetParts().Length);
        }

        [Test]
        public void MachinePackageDefinition_GetSteps_Returns_Empty_When_Null()
        {
            var package = new MachinePackageDefinition();
            Assert.IsNotNull(package.GetSteps());
            Assert.AreEqual(0, package.GetSteps().Length);
        }
    }
}
