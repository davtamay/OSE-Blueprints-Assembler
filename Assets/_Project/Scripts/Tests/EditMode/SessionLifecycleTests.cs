using System;
using System.Collections.Generic;
using NUnit.Framework;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Integration tests exercising the full session lifecycle:
    /// step activation → step completion → progression advancement → assembly completion.
    /// Uses the real runtime controllers (StepController, ProgressionController,
    /// AssemblyRuntimeController) with no MonoBehaviour dependencies.
    /// </summary>
    [TestFixture]
    public sealed class SessionLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.Clear();
            RuntimeEventBus.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceRegistry.Clear();
            RuntimeEventBus.Clear();
        }

        // ── StepController + ProgressionController integration ──

        [Test]
        public void ActivateStep_Then_Complete_Publishes_Correct_Event_Sequence()
        {
            var events = new List<StepStateChanged>();
            RuntimeEventBus.Subscribe<StepStateChanged>(evt => events.Add(evt));

            var step = MakeStep("step_1");
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(step, 0f);
            stepCtrl.CompleteStep(1.5f);

            // Expected sequence: Available, Active, Completed
            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(StepState.Available, events[0].Current);
            Assert.AreEqual(StepState.Active, events[1].Current);
            Assert.AreEqual(StepState.Completed, events[2].Current);
            Assert.AreEqual("step_1", events[2].StepId);
        }

        [Test]
        public void ProgressionController_AdvancesToNextStep_After_Completion()
        {
            var steps = new[] { MakeStep("s1", 1), MakeStep("s2", 2) };
            var progression = new ProgressionController();
            progression.Initialize(steps);

            Assert.AreEqual(0, progression.CurrentStepIndex);
            Assert.AreEqual("s1", progression.GetCurrentStep().id);

            progression.AdvanceToNextStep();

            Assert.AreEqual(1, progression.CurrentStepIndex);
            Assert.AreEqual("s2", progression.GetCurrentStep().id);
        }

        [Test]
        public void ProgressionController_IsComplete_After_AllSteps()
        {
            var steps = new[] { MakeStep("s1", 1) };
            var progression = new ProgressionController();
            progression.Initialize(steps);

            progression.AdvanceToNextStep();

            Assert.IsTrue(progression.IsComplete);
            Assert.IsNull(progression.GetCurrentStep());
        }

        [Test]
        public void ProgressionController_LastAdvanceWasFirstTime_True_On_Initial_Advance()
        {
            var steps = new[] { MakeStep("s1", 1), MakeStep("s2", 2) };
            var progression = new ProgressionController();
            progression.Initialize(steps);

            progression.AdvanceToNextStep();

            Assert.IsTrue(progression.LastAdvanceWasFirstTime);
        }

        // ── AssemblyRuntimeController integration ──

        [Test]
        public void AssemblyController_Activates_First_Step_On_Begin()
        {
            var package = CreateTwoStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);

            var activatedEvents = new List<StepActivated>();
            RuntimeEventBus.Subscribe<StepActivated>(evt => activatedEvents.Add(evt));

            controller.BeginAssembly("asm_1", () => 0f);

            Assert.IsTrue(controller.StepController.HasActiveStep);
            Assert.AreEqual("step_1", controller.StepController.CurrentStepDefinition.id);
            Assert.AreEqual(1, activatedEvents.Count);
            Assert.AreEqual("step_1", activatedEvents[0].StepId);
        }

        [Test]
        public void AssemblyController_Advances_To_Next_Step_After_Completion()
        {
            var package = CreateTwoStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);
            controller.BeginAssembly("asm_1", () => 0f);

            // Complete step 1
            controller.StepController.CompleteStep(1f);

            // Controller should have advanced to step 2
            Assert.IsTrue(controller.StepController.HasActiveStep);
            Assert.AreEqual("step_2", controller.StepController.CurrentStepDefinition.id);
        }

        [Test]
        public void AssemblyController_Fires_OnAssemblyCompleted_After_Last_Step()
        {
            var package = CreateOneStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);

            string completedAssemblyId = null;
            controller.OnAssemblyCompleted += id => completedAssemblyId = id;

            controller.BeginAssembly("asm_1", () => 0f);

            // Complete the only step
            controller.StepController.CompleteStep(1f);

            Assert.AreEqual("asm_1", completedAssemblyId);
        }

        [Test]
        public void AssemblyController_Publishes_AssemblyStarted_Event()
        {
            var package = CreateOneStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);

            var startedEvents = new List<AssemblyStarted>();
            RuntimeEventBus.Subscribe<AssemblyStarted>(evt => startedEvents.Add(evt));

            controller.BeginAssembly("asm_1", () => 0f);

            Assert.AreEqual(1, startedEvents.Count);
            Assert.AreEqual("asm_1", startedEvents[0].AssemblyId);
        }

        [Test]
        public void AssemblyController_Publishes_AssemblyCompleted_Event()
        {
            var package = CreateOneStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);

            var completedEvents = new List<AssemblyCompleted>();
            RuntimeEventBus.Subscribe<AssemblyCompleted>(evt => completedEvents.Add(evt));

            controller.BeginAssembly("asm_1", () => 0f);
            controller.StepController.CompleteStep(1f);

            Assert.AreEqual(1, completedEvents.Count);
            Assert.AreEqual("asm_1", completedEvents[0].AssemblyId);
        }

        // ── Full session event sequence ──

        [Test]
        public void Full_Two_Step_Session_Produces_Expected_Events()
        {
            var package = CreateTwoStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package);

            var stepStates = new List<StepStateChanged>();
            var stepActivations = new List<StepActivated>();
            RuntimeEventBus.Subscribe<StepStateChanged>(evt => stepStates.Add(evt));
            RuntimeEventBus.Subscribe<StepActivated>(evt => stepActivations.Add(evt));

            controller.BeginAssembly("asm_1", () => 0f);

            // After begin: step_1 is active
            Assert.AreEqual(2, stepStates.Count); // Available, Active for step_1
            Assert.AreEqual(1, stepActivations.Count);

            // Complete step 1
            controller.StepController.CompleteStep(1f);

            // After completing step 1: step_2 is now active
            // Events: step_1 Completed, step_2 Available, step_2 Active
            Assert.AreEqual(5, stepStates.Count);
            Assert.AreEqual(StepState.Completed, stepStates[2].Current);
            Assert.AreEqual("step_1", stepStates[2].StepId);
            Assert.AreEqual(StepState.Active, stepStates[4].Current);
            Assert.AreEqual("step_2", stepStates[4].StepId);

            // Complete step 2
            string completedAssemblyId = null;
            controller.OnAssemblyCompleted += id => completedAssemblyId = id;
            controller.StepController.CompleteStep(2f);

            Assert.AreEqual("asm_1", completedAssemblyId);
            Assert.AreEqual(6, stepStates.Count); // +1 for step_2 Completed
        }

        // ── StepController fail + retry ──

        [Test]
        public void FailAttempt_Increments_Count_And_Returns_To_Active()
        {
            var step = MakeStep("step_1");
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(step, 0f);

            stepCtrl.FailAttempt(0.5f);

            Assert.AreEqual(1, stepCtrl.CurrentStepState.AttemptCount);
            Assert.AreEqual(StepState.Active, stepCtrl.CurrentStepState.State);
            Assert.IsTrue(stepCtrl.HasActiveStep);
        }

        [Test]
        public void Multiple_FailAttempts_Accumulate()
        {
            var step = MakeStep("step_1");
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(step, 0f);

            stepCtrl.FailAttempt(0.5f);
            stepCtrl.FailAttempt(1.0f);
            stepCtrl.FailAttempt(1.5f);

            Assert.AreEqual(3, stepCtrl.CurrentStepState.AttemptCount);
        }

        // ── Suspend / Resume ──

        [Test]
        public void SuspendStep_Then_ResumeStep_Returns_To_Active()
        {
            var step = MakeStep("step_1");
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(step, 0f);

            stepCtrl.SuspendStep();
            Assert.AreEqual(StepState.Suspended, stepCtrl.CurrentStepState.State);

            stepCtrl.ResumeStep(1f);
            Assert.AreEqual(StepState.Active, stepCtrl.CurrentStepState.State);
            Assert.IsTrue(stepCtrl.HasActiveStep);
        }

        // ── ProgressionController restore ──

        [Test]
        public void SkipToIndex_Advances_Cursor_And_Returns_Skipped_Steps()
        {
            var steps = new[] { MakeStep("s1", 1), MakeStep("s2", 2), MakeStep("s3", 3) };
            var progression = new ProgressionController();
            progression.Initialize(steps);

            var skipped = progression.SkipToIndex(2);

            Assert.AreEqual(2, skipped.Length);
            Assert.AreEqual("s1", skipped[0].id);
            Assert.AreEqual("s2", skipped[1].id);
            Assert.AreEqual("s3", progression.GetCurrentStep().id);
        }

        // ── Helpers ──

        private static StepDefinition MakeStep(string id, int sequence = 1)
        {
            return new StepDefinition
            {
                id = id,
                name = id,
                assemblyId = "asm_1",
                family = "Place",
                completionType = "placement",
                instructionText = $"Do {id}.",
                sequenceIndex = sequence
            };
        }

        private static MachinePackageDefinition CreateOneStepPackage()
        {
            return new MachinePackageDefinition
            {
                schemaVersion = "1.0.0",
                packageVersion = "0.1.0",
                machine = new MachineDefinition
                {
                    id = "test_machine",
                    name = "Test Machine",
                    description = "Test",
                    difficulty = "beginner",
                    entryAssemblyIds = new[] { "asm_1" }
                },
                assemblies = new[]
                {
                    new AssemblyDefinition { id = "asm_1", name = "Assembly 1", machineId = "test_machine", stepIds = new[] { "step_1" } }
                },
                steps = new[]
                {
                    MakeStep("step_1", 1)
                },
                parts = Array.Empty<PartDefinition>(),
                tools = Array.Empty<ToolDefinition>(),
                targets = Array.Empty<TargetDefinition>(),
                validationRules = Array.Empty<ValidationRuleDefinition>(),
                hints = Array.Empty<HintDefinition>(),
                effects = Array.Empty<EffectDefinition>()
            };
        }

        private static MachinePackageDefinition CreateTwoStepPackage()
        {
            return new MachinePackageDefinition
            {
                schemaVersion = "1.0.0",
                packageVersion = "0.1.0",
                machine = new MachineDefinition
                {
                    id = "test_machine",
                    name = "Test Machine",
                    description = "Test",
                    difficulty = "beginner",
                    entryAssemblyIds = new[] { "asm_1" }
                },
                assemblies = new[]
                {
                    new AssemblyDefinition { id = "asm_1", name = "Assembly 1", machineId = "test_machine", stepIds = new[] { "step_1", "step_2" } }
                },
                steps = new[]
                {
                    MakeStep("step_1", 1),
                    MakeStep("step_2", 2)
                },
                parts = Array.Empty<PartDefinition>(),
                tools = Array.Empty<ToolDefinition>(),
                targets = Array.Empty<TargetDefinition>(),
                validationRules = Array.Empty<ValidationRuleDefinition>(),
                hints = Array.Empty<HintDefinition>(),
                effects = Array.Empty<EffectDefinition>()
            };
        }
    }
}
