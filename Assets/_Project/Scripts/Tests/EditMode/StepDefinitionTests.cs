using NUnit.Framework;
using OSE.Content;
using OSE.UI.Root;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Contract tests for <see cref="StepDefinition"/> resolved accessors and
    /// <see cref="WireConnectEntry"/> polarity helpers.
    ///
    /// These are pure C# — no Unity scene, no MonoBehaviours, no setup overhead.
    /// They guard the payload-first resolution rules documented in STEP_SCHEMA.md.
    /// </summary>
    [TestFixture]
    public class StepDefinitionTests
    {
        // ── ResolvedFamily ────────────────────────────────────────────────────

        [Test]
        public void ResolvedFamily_FamilyFieldSet_ReturnsEnumDirectly()
        {
            var step = new StepDefinition { family = "Use", completionType = "placement" };
            Assert.AreEqual(StepFamily.Use, step.ResolvedFamily,
                "family field must win over completionType.");
        }

        [Test]
        public void ResolvedFamily_FamilyAbsent_FallsBackToCompletionType()
        {
            var step = new StepDefinition { completionType = "tool_action" };
            Assert.AreEqual(StepFamily.Use, step.ResolvedFamily);
        }

        [Test]
        public void ResolvedFamily_BothAbsent_DefaultsToPlace()
        {
            var step = new StepDefinition();
            Assert.AreEqual(StepFamily.Place, step.ResolvedFamily);
        }

        [TestCase("Place",   StepFamily.Place)]
        [TestCase("Use",     StepFamily.Use)]
        [TestCase("Connect", StepFamily.Connect)]
        [TestCase("Confirm", StepFamily.Confirm)]
        public void ResolvedFamily_AllFamilyValues_Roundtrip(string familyStr, StepFamily expected)
        {
            var step = new StepDefinition { family = familyStr };
            Assert.AreEqual(expected, step.ResolvedFamily);
        }

        [TestCase("placement",       StepFamily.Place)]
        [TestCase("tool_action",     StepFamily.Use)]
        [TestCase("pipe_connection", StepFamily.Connect)]
        [TestCase("confirmation",    StepFamily.Confirm)]
        public void ResolvedFamily_LegacyCompletionTypes_MapCorrectly(string completionType, StepFamily expected)
        {
            var step = new StepDefinition { completionType = completionType };
            Assert.AreEqual(expected, step.ResolvedFamily);
        }

        [Test]
        public void ResolvedFamily_UnrecognizedFamily_DefaultsToPlace()
        {
            var step = new StepDefinition { family = "BogusFamily" };
            Assert.AreEqual(StepFamily.Place, step.ResolvedFamily);
        }

        // ── ResolvedProfile ───────────────────────────────────────────────────

        [TestCase("Clamp",       StepProfile.Clamp)]
        [TestCase("AxisFit",     StepProfile.AxisFit)]
        [TestCase("Torque",      StepProfile.Torque)]
        [TestCase("Weld",        StepProfile.Weld)]
        [TestCase("Cut",         StepProfile.Cut)]
        [TestCase("Strike",      StepProfile.Strike)]
        [TestCase("Measure",     StepProfile.Measure)]
        [TestCase("SquareCheck", StepProfile.SquareCheck)]
        [TestCase("Cable",       StepProfile.Cable)]
        [TestCase("WireConnect", StepProfile.WireConnect)]
        public void ResolvedProfile_AllProfileValues_Roundtrip(string profileStr, StepProfile expected)
        {
            var step = new StepDefinition { profile = profileStr };
            Assert.AreEqual(expected, step.ResolvedProfile);
        }

        [Test]
        public void ResolvedProfile_NullProfile_ReturnsNone()
        {
            var step = new StepDefinition();
            Assert.AreEqual(StepProfile.None, step.ResolvedProfile);
        }

        [Test]
        public void ResolvedProfile_UnrecognizedProfile_ReturnsNone()
        {
            var step = new StepDefinition { profile = "NotAProfile" };
            Assert.AreEqual(StepProfile.None, step.ResolvedProfile);
        }

        // ── Payload-first resolution ──────────────────────────────────────────

        [Test]
        public void ResolvedInstructionText_PayloadWins_WhenBothPresent()
        {
            var step = new StepDefinition
            {
                instructionText = "flat",
                guidance = new StepGuidancePayload { instructionText = "payload" }
            };
            Assert.AreEqual("payload", step.ResolvedInstructionText);
        }

        [Test]
        public void ResolvedInstructionText_FlatFallback_WhenPayloadAbsent()
        {
            var step = new StepDefinition { instructionText = "flat" };
            Assert.AreEqual("flat", step.ResolvedInstructionText);
        }

        [Test]
        public void ResolvedInstructionText_FlatFallback_WhenPayloadFieldEmpty()
        {
            var step = new StepDefinition
            {
                instructionText = "flat",
                guidance = new StepGuidancePayload { instructionText = "" }
            };
            Assert.AreEqual("flat", step.ResolvedInstructionText);
        }

        [Test]
        public void ResolvedHintIds_PayloadWins_WhenPresent()
        {
            var step = new StepDefinition
            {
                hintIds  = new[] { "flat_hint" },
                guidance = new StepGuidancePayload { hintIds = new[] { "payload_hint" } }
            };
            CollectionAssert.AreEqual(new[] { "payload_hint" }, step.ResolvedHintIds);
        }

        [Test]
        public void ResolvedHintIds_FlatFallback_WhenPayloadAbsent()
        {
            var step = new StepDefinition { hintIds = new[] { "flat_hint" } };
            CollectionAssert.AreEqual(new[] { "flat_hint" }, step.ResolvedHintIds);
        }

        [Test]
        public void ResolvedAllowSkip_DifficultyPayloadWins_WhenPresent()
        {
            var step = new StepDefinition
            {
                allowSkip  = false,
                difficulty = new StepDifficultyPayload { allowSkip = true }
            };
            Assert.IsTrue(step.ResolvedAllowSkip);
        }

        [Test]
        public void ResolvedAllowSkip_FlatFallback_WhenPayloadAbsent()
        {
            var step = new StepDefinition { allowSkip = true };
            Assert.IsTrue(step.ResolvedAllowSkip);
        }

        [Test]
        public void ResolvedValidationRuleIds_PayloadWins_WhenPresent()
        {
            var step = new StepDefinition
            {
                validationRuleIds = new[] { "flat_rule" },
                validation        = new StepValidationPayload { validationRuleIds = new[] { "payload_rule" } }
            };
            CollectionAssert.AreEqual(new[] { "payload_rule" }, step.ResolvedValidationRuleIds);
        }

        // ── IsPlacement / IsToolAction / IsPipeConnection / IsConfirmation ────

        [Test]
        public void IsPlacement_TrueForPlaceFamily()
        {
            Assert.IsTrue(new StepDefinition { family = "Place" }.IsPlacement);
            Assert.IsFalse(new StepDefinition { family = "Use" }.IsPlacement);
        }

        [Test]
        public void IsToolAction_TrueForUseFamily()
        {
            Assert.IsTrue(new StepDefinition { family = "Use" }.IsToolAction);
            Assert.IsFalse(new StepDefinition { family = "Place" }.IsToolAction);
        }

        [Test]
        public void IsPipeConnection_TrueForConnectFamily()
        {
            Assert.IsTrue(new StepDefinition { family = "Connect" }.IsPipeConnection);
        }

        [Test]
        public void IsConfirmation_TrueForConfirmFamily()
        {
            Assert.IsTrue(new StepDefinition { family = "Confirm" }.IsConfirmation);
        }

        // ── IsSequential ──────────────────────────────────────────────────────

        [Test]
        public void IsSequential_TrueWhen_TargetOrderIsSequential()
        {
            var step = new StepDefinition { targetOrder = "sequential" };
            Assert.IsTrue(step.IsSequential);
        }

        [Test]
        public void IsSequential_CaseInsensitive()
        {
            var step = new StepDefinition { targetOrder = "SEQUENTIAL" };
            Assert.IsTrue(step.IsSequential);
        }

        [Test]
        public void IsSequential_FalseWhen_Null()
        {
            Assert.IsFalse(new StepDefinition().IsSequential);
        }

        [Test]
        public void IsSequential_FalseWhen_Parallel()
        {
            Assert.IsFalse(new StepDefinition { targetOrder = "parallel" }.IsSequential);
        }

        // ── RequiresSubassemblyPlacement ──────────────────────────────────────

        [Test]
        public void RequiresSubassemblyPlacement_TrueWhen_IdPresent()
        {
            var step = new StepDefinition { requiredSubassemblyId = "extruder" };
            Assert.IsTrue(step.RequiresSubassemblyPlacement);
        }

        [Test]
        public void RequiresSubassemblyPlacement_FalseWhen_IdAbsent()
        {
            Assert.IsFalse(new StepDefinition().RequiresSubassemblyPlacement);
        }

        // ── GetDisplayName ────────────────────────────────────────────────────

        [Test]
        public void GetDisplayName_ReturnsName_WhenSet()
        {
            var step = new StepDefinition { name = "Attach Rail", id = "step_01" };
            Assert.AreEqual("Attach Rail", step.GetDisplayName());
        }

        [Test]
        public void GetDisplayName_FallsBackToId_WhenNameBlank()
        {
            var step = new StepDefinition { id = "step_01" };
            Assert.AreEqual("step_01", step.GetDisplayName());
        }

        [Test]
        public void GetDisplayName_ReturnsUnnamed_WhenBothBlank()
        {
            Assert.AreEqual("Unnamed Step", new StepDefinition().GetDisplayName());
        }

        // ── WireConnectEntry polarity helpers ─────────────────────────────────

        [TestCase("+12V", true)]
        [TestCase("+5V",  true)]
        [TestCase("+",    true)]
        [TestCase("GND",  false)]
        [TestCase("signal", false)]
        public void WireConnectEntry_IsPortAPositive(string polarity, bool expected)
        {
            var entry = new WireConnectEntry { portAPolarityType = polarity };
            Assert.AreEqual(expected, entry.IsPortAPositive);
        }

        [TestCase("GND",  true)]
        [TestCase("-",    true)]
        [TestCase("-12V", true)]
        [TestCase("+12V", false)]
        public void WireConnectEntry_IsPortAGround(string polarity, bool expected)
        {
            var entry = new WireConnectEntry { portAPolarityType = polarity };
            Assert.AreEqual(expected, entry.IsPortAGround);
        }

        [TestCase("signal",     true)]
        [TestCase("pwm",        true)]
        [TestCase("enable",     true)]
        [TestCase("thermistor", true)]
        [TestCase("fan",        true)]
        [TestCase("endstop",    true)]
        [TestCase("+12V",       false)]
        [TestCase("GND",        false)]
        public void WireConnectEntry_IsPortASignal(string polarity, bool expected)
        {
            var entry = new WireConnectEntry { portAPolarityType = polarity };
            Assert.AreEqual(expected, entry.IsPortASignal);
        }

        // ── StepWireConnectPayload.IsConfigured ───────────────────────────────

        [Test]
        public void StepWireConnectPayload_IsConfigured_FalseWhen_NullWires()
        {
            Assert.IsFalse(new StepWireConnectPayload().IsConfigured);
        }

        [Test]
        public void StepWireConnectPayload_IsConfigured_FalseWhen_EmptyWires()
        {
            Assert.IsFalse(new StepWireConnectPayload { wires = new WireConnectEntry[0] }.IsConfigured);
        }

        [Test]
        public void StepWireConnectPayload_IsConfigured_TrueWhen_HasEntries()
        {
            var payload = new StepWireConnectPayload
            {
                wires = new[] { new WireConnectEntry { portAPolarityType = "+12V" } }
            };
            Assert.IsTrue(payload.IsConfigured);
        }

        // ── SplinePartFactory.HasSplineData ───────────────────────────────────

        [Test]
        public void HasSplineData_NullPlacement_ReturnsFalse()
        {
            Assert.IsFalse(SplinePartFactory.HasSplineData(null));
        }

        [Test]
        public void HasSplineData_NoSplinePath_ReturnsFalse()
        {
            Assert.IsFalse(SplinePartFactory.HasSplineData(new PartPreviewPlacement()));
        }

        [Test]
        public void HasSplineData_OneKnot_ReturnsFalse()
        {
            var placement = new PartPreviewPlacement
            {
                splinePath = new SplinePathDefinition
                {
                    knots = new[] { new SceneFloat3 { x = 0, y = 0, z = 0 } }
                }
            };
            Assert.IsFalse(SplinePartFactory.HasSplineData(placement));
        }

        [Test]
        public void HasSplineData_TwoKnots_ReturnsTrue()
        {
            var placement = new PartPreviewPlacement
            {
                splinePath = new SplinePathDefinition
                {
                    knots = new[]
                    {
                        new SceneFloat3 { x = 0, y = 0, z = 0 },
                        new SceneFloat3 { x = 1, y = 0, z = 0 }
                    }
                }
            };
            Assert.IsTrue(SplinePartFactory.HasSplineData(placement));
        }
    }
}
