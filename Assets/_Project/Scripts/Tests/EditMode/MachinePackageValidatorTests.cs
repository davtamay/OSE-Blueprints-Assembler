// These tests intentionally set legacy flat fields (completionType, hintIds, etc.) to verify
// the validator's backward-compatibility path. Suppress the obsolete-field warnings file-wide.
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using NUnit.Framework;
using OSE.Content;
using OSE.Content.Validation;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class MachinePackageValidatorTests
    {
        [TearDown]
        public void TearDown()
        {
            MachinePackageValidator.ClearExternalPasses();
        }

        // ── IPackageValidationPass extension point ──

        [Test]
        public void RegisterPass_CustomPass_IsExecutedDuringValidation()
        {
            bool executed = false;
            var pass = new LambdaValidationPass(_ => executed = true);
            MachinePackageValidator.RegisterPass(pass);

            MachinePackageValidator.Validate(CreateMinimalValidPackage());

            Assert.IsTrue(executed, "Custom pass was not executed.");
        }

        [Test]
        public void RegisterPass_CustomPass_CanAddIssue()
        {
            const string marker = "CustomPassMarker";
            var pass = new LambdaValidationPass(ctx =>
                ctx.Issues.Add(new MachinePackageValidationIssue(
                    MachinePackageIssueSeverity.Warning, "custom.path", marker)));
            MachinePackageValidator.RegisterPass(pass);

            var result = MachinePackageValidator.Validate(CreateMinimalValidPackage());

            AssertHasIssueContaining(result, marker);
        }

        [Test]
        public void RegisterPass_SameInstance_NotAddedTwice()
        {
            int callCount = 0;
            var pass = new LambdaValidationPass(_ => callCount++);
            MachinePackageValidator.RegisterPass(pass);
            MachinePackageValidator.RegisterPass(pass); // duplicate

            MachinePackageValidator.Validate(CreateMinimalValidPackage());

            Assert.AreEqual(1, callCount, "Pass was called more than once despite duplicate registration.");
        }

        [Test]
        public void ClearExternalPasses_Removes_All_Registered_Passes()
        {
            bool executed = false;
            MachinePackageValidator.RegisterPass(new LambdaValidationPass(_ => executed = true));
            MachinePackageValidator.ClearExternalPasses();

            MachinePackageValidator.Validate(CreateMinimalValidPackage());

            Assert.IsFalse(executed, "Pass was executed after ClearExternalPasses.");
        }

        // ── Null / Empty ──

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

        // ── Machine Validation ──

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
        public void Missing_PackageVersion_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.packageVersion = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "packageVersion");
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
        public void Missing_Machine_Id_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine.id = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "machine.id");
        }

        [Test]
        public void Missing_Machine_Name_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine.name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "machine.name");
        }

        [Test]
        public void Missing_Machine_Description_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine.description = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "machine.description");
        }

        [Test]
        public void Invalid_Difficulty_Reported_As_Warning()
        {
            var package = CreateMinimalValidPackage();
            package.machine.difficulty = "impossible";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors); // difficulty is required enum so invalid = error
        }

        [Test]
        public void Missing_Learning_Objectives_Reported_As_Warning()
        {
            var package = CreateMinimalValidPackage();
            package.machine.learningObjectives = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "learningObjectives");
        }

        [Test]
        public void Invalid_Recommended_Mode_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine.recommendedMode = "freeplay";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "recommendedMode");
        }

        // ── Assembly Validation ──

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
        public void Assembly_Missing_Name_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.assemblies[0].name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "name");
        }

        [Test]
        public void Assembly_Wrong_MachineId_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.assemblies[0].machineId = "wrong_machine";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "machineId");
        }

        [Test]
        public void Assembly_Referencing_Missing_Subassembly_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.assemblies[0].subassemblyIds = new[] { "nonexistent_sub" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Assembly_Referencing_Missing_DependencyAssembly_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.assemblies[0].dependencyAssemblyIds = new[] { "nonexistent_asm" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Part Validation ──

        [Test]
        public void Part_Missing_Name_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "name");
        }

        [Test]
        public void Part_Invalid_Category_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].category = "widget";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "category");
        }

        [Test]
        public void Part_Missing_Material_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].material = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "material");
        }

        [Test]
        public void Part_Missing_Function_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].function = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "function");
        }

        [Test]
        public void Part_Zero_Quantity_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].quantity = 0;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "quantity");
        }

        [Test]
        public void Part_Referencing_Missing_Tool_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.parts[0].toolIds = new[] { "nonexistent_tool" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Tool Validation ──

        [Test]
        public void Tool_Missing_Name_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.tools[0].name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "name");
        }

        [Test]
        public void Tool_Invalid_Category_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.tools[0].category = "magic_wand";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "category");
        }

        [Test]
        public void Tool_Missing_Purpose_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.tools[0].purpose = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "purpose");
        }

        [Test]
        public void Tool_Missing_AssetRef_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.tools[0].assetRef = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "assetRef");
        }

        // ── Step Validation ──

        [Test]
        public void Step_Missing_Name_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "name");
        }

        [Test]
        public void Step_With_Invalid_Family_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].family = "InvalidFamily";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "family");
        }

        [Test]
        public void Step_With_Invalid_CompletionType_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].completionType = "teleportation";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "completionType");
        }

        [Test]
        public void Step_With_Invalid_ViewMode_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].viewMode = "Panoramic";

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "viewMode");
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
        public void Step_Referencing_Missing_Target_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].targetIds = new[] { "nonexistent_target" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Step_Referencing_Missing_Tool_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].relevantToolIds = new[] { "nonexistent_tool" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Step_Referencing_Missing_Hint_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].hintIds = new[] { "nonexistent_hint" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Step_Referencing_Missing_Effect_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].effectTriggerIds = new[] { "nonexistent_effect" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Step_Referencing_Missing_ValidationRule_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].validationRuleIds = new[] { "nonexistent_rule" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Step_Zero_SequenceIndex_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].sequenceIndex = 0;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "sequenceIndex");
        }

        [Test]
        public void Step_Both_RequiredParts_And_RequiredSubassembly_Reported()
        {
            var package = CreateMinimalValidPackage();
            // Add a subassembly with part
            package.subassemblies = new[]
            {
                new SubassemblyDefinition
                {
                    id = "sub_1", name = "Sub 1", assemblyId = "asm_1",
                    partIds = new[] { "part_1" }, stepIds = new[] { "step_1" }
                }
            };
            package.assemblies[0].subassemblyIds = new[] { "sub_1" };
            package.steps[0].requiredPartIds = new[] { "part_1" };
            package.steps[0].requiredSubassemblyId = "sub_1";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "requiredPartIds or requiredSubassemblyId");
        }

        // ── Tool Action Validation ──

        [Test]
        public void ToolAction_Missing_ToolId_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].family = "Use";
            package.steps[0].completionType = "tool_action";
            package.steps[0].requiredToolActions = new[]
            {
                new ToolActionDefinition { id = "ta_1", toolId = null, actionType = "tighten", requiredCount = 1 }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "toolId");
        }

        [Test]
        public void ToolAction_Invalid_ActionType_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].family = "Use";
            package.steps[0].completionType = "tool_action";
            package.steps[0].requiredToolActions = new[]
            {
                new ToolActionDefinition { id = "ta_1", toolId = "tool_1", actionType = "laser_beam", requiredCount = 1 }
            };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "actionType");
        }

        [Test]
        public void ToolAction_Zero_RequiredCount_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].family = "Use";
            package.steps[0].completionType = "tool_action";
            package.steps[0].requiredToolActions = new[]
            {
                new ToolActionDefinition { id = "ta_1", toolId = "tool_1", actionType = "tighten", requiredCount = 0 }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "requiredCount");
        }

        [Test]
        public void ToolAction_Target_Not_In_Step_TargetIds_Warned()
        {
            var package = CreateMinimalValidPackage();
            // Add a second target that is not in step's targetIds
            package.targets = new[]
            {
                new TargetDefinition { id = "target_1", associatedPartId = "part_1", anchorRef = "anchor_1" },
                new TargetDefinition { id = "target_extra", anchorRef = "anchor_extra" }
            };
            package.steps[0].family = "Use";
            package.steps[0].completionType = "tool_action";
            package.steps[0].requiredPartIds = Array.Empty<string>();
            package.steps[0].requiredToolActions = new[]
            {
                new ToolActionDefinition { id = "ta_1", toolId = "tool_1", actionType = "tighten", targetId = "target_extra", requiredCount = 1 }
            };
            package.steps[0].relevantToolIds = new[] { "tool_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "not listed in step's targetIds");
        }

        // ── Hint Validation ──

        [Test]
        public void Hint_Missing_Type_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = null, message = "Do this" }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "type");
        }

        [Test]
        public void Hint_Invalid_Type_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = "telepathy", message = "Think about it" }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "type");
        }

        [Test]
        public void Hint_Missing_Message_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = "text", message = null }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "message");
        }

        [Test]
        public void Hint_Invalid_Priority_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = "text", message = "msg", priority = "urgent" }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "priority");
        }

        [Test]
        public void Hint_Referencing_Missing_Part_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = "text", message = "msg", partId = "nonexistent" }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Hint_Referencing_Missing_Tool_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.hints = new[]
            {
                new HintDefinition { id = "hint_1", type = "text", message = "msg", toolId = "nonexistent" }
            };
            package.steps[0].hintIds = new[] { "hint_1" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Effect Validation ──

        [Test]
        public void Effect_Missing_Type_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.effects = new[]
            {
                new EffectDefinition { id = "effect_1", type = null }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "type");
        }

        [Test]
        public void Effect_Invalid_Type_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.effects = new[]
            {
                new EffectDefinition { id = "effect_1", type = "explosion" }
            };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "type");
        }

        [Test]
        public void Effect_Invalid_TriggerPolicy_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.effects = new[]
            {
                new EffectDefinition { id = "effect_1", type = "welding", triggerPolicy = "on_hover" }
            };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "triggerPolicy");
        }

        // ── Target Validation ──

        [Test]
        public void Target_Missing_AnchorRef_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.targets[0].anchorRef = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "anchorRef");
        }

        [Test]
        public void Target_Referencing_Missing_Part_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.targets[0].associatedPartId = "nonexistent_part";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Target_Both_Part_And_Subassembly_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.subassemblies = new[]
            {
                new SubassemblyDefinition
                {
                    id = "sub_1", name = "Sub 1", assemblyId = "asm_1",
                    partIds = new[] { "part_1" }, stepIds = new[] { "step_1" }
                }
            };
            package.assemblies[0].subassemblyIds = new[] { "sub_1" };
            package.targets[0].associatedSubassemblyId = "sub_1";
            // associatedPartId is already set to "part_1"

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "associatedPartId or associatedSubassemblyId");
        }

        // ── Validation Rule Validation ──

        [Test]
        public void ValidationRule_Invalid_Type_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.validationRules = new[]
            {
                new ValidationRuleDefinition { id = "rule_1", type = "magic" }
            };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "type");
        }

        [Test]
        public void ValidationRule_Referencing_Missing_Target_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.validationRules = new[]
            {
                new ValidationRuleDefinition { id = "rule_1", type = "placement", targetId = "nonexistent" }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void ValidationRule_Referencing_Missing_Part_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.validationRules = new[]
            {
                new ValidationRuleDefinition { id = "rule_1", type = "placement", expectedPartId = "nonexistent" }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void ValidationRule_Referencing_Missing_CorrectionHint_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.validationRules = new[]
            {
                new ValidationRuleDefinition { id = "rule_1", type = "placement", correctionHintId = "nonexistent" }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Orphan Detection ──

        [Test]
        public void Orphan_Part_Not_Referenced_By_Any_Step_Warned()
        {
            var package = CreateMinimalValidPackage();
            // Add a second part that no step references
            package.parts = new[]
            {
                new PartDefinition { id = "part_1", name = "Part 1", category = "plate", material = "steel", function = "base", quantity = 1, assetRef = "models/part1.glb" },
                new PartDefinition { id = "part_orphan", name = "Orphan Part", category = "plate", material = "steel", function = "unused", quantity = 1, assetRef = "models/orphan.glb" }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "part_orphan");
            AssertHasIssueContaining(result, "never referenced");
        }

        [Test]
        public void Orphan_Target_Not_Referenced_By_Any_Step_Warned()
        {
            var package = CreateMinimalValidPackage();
            package.targets = new[]
            {
                new TargetDefinition { id = "target_1", associatedPartId = "part_1", anchorRef = "anchor_1" },
                new TargetDefinition { id = "target_orphan", anchorRef = "anchor_orphan" }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "target_orphan");
            AssertHasIssueContaining(result, "never referenced");
        }

        [Test]
        public void Orphan_Step_Without_AssemblyId_Warned()
        {
            var package = CreateMinimalValidPackage();
            package.steps = new[]
            {
                new StepDefinition
                {
                    id = "step_1", name = "Place Part 1", family = "Place",
                    completionType = "placement", assemblyId = "asm_1",
                    requiredPartIds = new[] { "part_1" }, targetIds = new[] { "target_1" },
                    sequenceIndex = 1
                },
                new StepDefinition
                {
                    id = "step_orphan", name = "Lost Step", family = "Place",
                    completionType = "placement", assemblyId = null,
                    sequenceIndex = 2
                }
            };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "step_orphan");
            AssertHasIssueContaining(result, "no assemblyId");
        }

        // ── Sequence Index Validation ──

        [Test]
        public void NonContiguous_SequenceIndices_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps = new[]
            {
                new StepDefinition
                {
                    id = "step_1", name = "Step 1", family = "Place",
                    completionType = "placement", assemblyId = "asm_1",
                    requiredPartIds = new[] { "part_1" }, targetIds = new[] { "target_1" },
                    sequenceIndex = 1
                },
                new StepDefinition
                {
                    id = "step_3", name = "Step 3", family = "Place",
                    completionType = "placement", assemblyId = "asm_1",
                    sequenceIndex = 3 // gap — index 2 is missing
                }
            };
            package.assemblies[0].stepIds = new[] { "step_1", "step_3" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "sequenceIndex");
        }

        [Test]
        public void Contiguous_SequenceIndices_Pass()
        {
            var package = CreateMinimalValidPackage();
            // Minimal package already has step with sequenceIndex = 1, which is contiguous for 1 step
            var result = MachinePackageValidator.Validate(package);

            Assert.IsFalse(result.HasErrors,
                $"Expected no errors but got:\n{result.FormatSummary(20)}");
        }

        // ── Subassembly Validation ──

        [Test]
        public void Subassembly_Missing_Name_Reported()
        {
            var package = CreatePackageWithSubassembly();
            package.subassemblies[0].name = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "name");
        }

        [Test]
        public void Subassembly_Referencing_Missing_Assembly_Reported()
        {
            var package = CreatePackageWithSubassembly();
            package.subassemblies[0].assemblyId = "nonexistent_asm";

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        [Test]
        public void Subassembly_Referencing_Missing_Part_Reported()
        {
            var package = CreatePackageWithSubassembly();
            package.subassemblies[0].partIds = new[] { "nonexistent_part" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Preview Config Coverage ──

        [Test]
        public void PreviewConfig_Missing_When_Parts_Exist_Warned()
        {
            var package = CreateMinimalValidPackage();
            package.previewConfig = null;

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasWarnings);
            AssertHasIssueContaining(result, "previewConfig");
        }

        // ── Difficulty Payload ──

        [Test]
        public void Step_Difficulty_Negative_TimeLimit_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].difficulty = new StepDifficultyPayload { timeLimitSeconds = -5f };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "timeLimitSeconds");
        }

        [Test]
        public void Step_Difficulty_Invalid_HintAvailability_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.steps[0].difficulty = new StepDifficultyPayload { hintAvailability = "infinite" };

            var result = MachinePackageValidator.Validate(package);

            AssertHasIssueContaining(result, "hintAvailability");
        }

        // ── Machine Entry Assembly Refs ──

        [Test]
        public void Machine_EntryAssemblyId_Missing_Assembly_Reported()
        {
            var package = CreateMinimalValidPackage();
            package.machine.entryAssemblyIds = new[] { "nonexistent_asm" };

            var result = MachinePackageValidator.Validate(package);

            Assert.IsTrue(result.HasErrors);
            AssertHasIssueContaining(result, "does not resolve");
        }

        // ── Format Summary ──

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
                        machineId = "test_machine",
                        stepIds = new[] { "step_1" }
                    }
                },
                parts = new[]
                {
                    new PartDefinition
                    {
                        id = "part_1", name = "Part 1", category = "plate",
                        material = "steel", function = "structural base",
                        quantity = 1, assetRef = "models/part1.glb"
                    }
                },
                tools = new[]
                {
                    new ToolDefinition
                    {
                        id = "tool_1", name = "Wrench", category = "hand_tool",
                        purpose = "tighten bolts", assetRef = "models/wrench.glb"
                    }
                },
                steps = new[]
                {
                    new StepDefinition
                    {
                        id = "step_1",
                        name = "Place Part 1",
                        assemblyId = "asm_1",
                        family = "Place",
                        completionType = "placement",
                        instructionText = "Place part 1 on the frame.",
                        requiredPartIds = new[] { "part_1" },
                        targetIds = new[] { "target_1" },
                        sequenceIndex = 1
                    }
                },
                targets = new[]
                {
                    new TargetDefinition { id = "target_1", associatedPartId = "part_1", anchorRef = "anchor_1" }
                },
                validationRules = Array.Empty<ValidationRuleDefinition>(),
                hints = Array.Empty<HintDefinition>(),
                effects = Array.Empty<EffectDefinition>()
            };
        }

        private static MachinePackageDefinition CreatePackageWithSubassembly()
        {
            var package = CreateMinimalValidPackage();
            package.subassemblies = new[]
            {
                new SubassemblyDefinition
                {
                    id = "sub_1", name = "Sub 1", assemblyId = "asm_1",
                    partIds = new[] { "part_1" }, stepIds = new[] { "step_1" }
                }
            };
            package.assemblies[0].subassemblyIds = new[] { "sub_1" };
            return package;
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

    /// <summary>
    /// Test helper — wraps a lambda as an <see cref="IPackageValidationPass"/> so
    /// tests don't need to define a full class per scenario.
    /// </summary>
    internal sealed class LambdaValidationPass : IPackageValidationPass
    {
        private readonly System.Action<ValidationPassContext> _action;
        public LambdaValidationPass(System.Action<ValidationPassContext> action) { _action = action; }
        public void Execute(ValidationPassContext ctx) => _action(ctx);
    }
}
