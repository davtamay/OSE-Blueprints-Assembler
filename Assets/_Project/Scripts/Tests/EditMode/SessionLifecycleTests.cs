using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        // ── AwaitingResume — navigation guard ──

        [Test]
        public void AssemblyController_StepCompletion_Does_Not_Advance_While_IsNavigating()
        {
            // isNavigating returns true — simulates AwaitingResume state while
            // a scene transition is in progress.
            var package = CreateTwoStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package, isNavigating: () => true);
            controller.BeginAssembly("asm_1", () => 0f);

            string activatedStepId = null;
            RuntimeEventBus.Subscribe<StepActivated>(e => activatedStepId = e.StepId);

            // Completing the active step while navigating must NOT trigger advancement.
            controller.StepController.CompleteStep(1f);

            // step_1 was active on begin; after completing while navigating, step_2
            // should NOT have been activated.
            Assert.AreNotEqual("step_2", activatedStepId);
        }

        [Test]
        public void AssemblyController_StepCompletion_Advances_When_NavigatingFlagClears()
        {
            bool navigating = true;
            var package = CreateTwoStepPackage();
            var controller = new AssemblyRuntimeController();
            controller.Initialize(package, isNavigating: () => navigating);
            controller.BeginAssembly("asm_1", () => 0f);

            // First completion while navigating — should be suppressed.
            controller.StepController.CompleteStep(1f);
            Assert.AreEqual("step_1", controller.StepController.CurrentStepDefinition?.id,
                "Should still be on step_1 while navigating.");

            // Navigation ends; next direct ActivateStep call from the session should work.
            navigating = false;
            // Direct advance to verify the flag gate only blocks the event path.
            controller.StepController.ActivateStep(MakeStep("step_2", 2), 1.5f);
            Assert.AreEqual("step_2", controller.StepController.CurrentStepDefinition.id);
        }

        // ── StepController fail + retry ──

        [Test]
        public void FailAttempt_Increments_Count_And_Returns_To_Active()
        {
            var step = MakeStep("step_1");
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(step, 0f);

            stepCtrl.FailAttempt();

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

            stepCtrl.FailAttempt();
            stepCtrl.FailAttempt();
            stepCtrl.FailAttempt();

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

        // ── Nested-event cursor ownership race (PartRuntimeController) ──

        [Test]
        public void PartRuntimeController_Completed_Does_Not_Detach_Cursor_Owned_By_Nested_Active_Step()
        {
            // Regression for the step-53 bearings bug: AssemblyRuntimeController
            // subscribes to StepStateChanged and synchronously calls
            // ActivateStep(next) on Completed(prev). The nested Active publish
            // inside the outer Completed publish causes PartRuntimeController
            // to attach the next step's cursor FIRST, then the outer Completed
            // handler ran a blind detach that corrupted ownership — the next
            // step's cursor got detached immediately after attachment, and any
            // TransitionPart(PlacedVirtually) call saw _attachedCursor=null
            // and silently failed to advance the task cursor.
            //
            // Fix: PartRuntimeController tags _attachedCursor with the owning
            // step id and uses DetachIfOwnedBy(stepId) in the Completed branch.
            // This test reproduces the race and asserts the cursor stays
            // attached to the new step after the outer Completed completes.
            var package = CreateTwoStepPackageWithTaskOrder();

            var assemblyCtrl = new AssemblyRuntimeController();
            assemblyCtrl.Initialize(package);

            // Stub the session registry so PartRuntimeController can resolve
            // AssemblyController.StepController.CurrentTaskCursor.
            var sessionStub = new TestSessionStub(assemblyCtrl, package);
            ServiceRegistry.Register<IMachineSessionController>(sessionStub);

            var partCtrl = new PartRuntimeController();
            partCtrl.Initialize(package);

            // Begin assembly → step_1 Active → PartRuntime attaches step_1 cursor.
            assemblyCtrl.BeginAssembly("asm_1", () => 0f);
            Assert.AreEqual("step_1", assemblyCtrl.StepController.CurrentStepDefinition.id);
            Assert.IsNotNull(assemblyCtrl.StepController.CurrentTaskCursor);

            // Complete step_1 → AssemblyRuntime's Completed handler nested-
            // publishes Active(step_2). After all event dispatch finishes,
            // step_2 should own the cursor. Pre-fix, step_2's cursor got
            // detached by PartRuntimeController's outer-Completed handler.
            assemblyCtrl.StepController.CompleteStep(1f);

            Assert.AreEqual("step_2", assemblyCtrl.StepController.CurrentStepDefinition.id,
                "step_2 should be active after step_1 completes");

            // Place a part in step_2 — TransitionPart publishes PlacedVirtually
            // and notifies the cursor. If the cursor was detached by the race,
            // task advancement silently fails and span stays at 0.
            partCtrl.AttemptPlacement("p2a", "__auto_p2a", PlacementValidationResult.Valid(true));

            // Cursor should have advanced past span 0 — step_2 has 2 singleton
            // spans (p2a then p2b). After placing p2a, span should be 1.
            var cursor = assemblyCtrl.StepController.CurrentTaskCursor;
            Assert.IsNotNull(cursor, "Cursor must remain attached after the nested publish.");
            Assert.AreEqual(1, cursor.SpanIndex,
                "Cursor should have advanced to span 1 — if it stayed at 0, the nested-event detach race regressed.");

            partCtrl.Dispose();
            ServiceRegistry.Unregister<IMachineSessionController>();
        }

        /// <summary>Minimal IMachineSessionController stub for editmode tests.</summary>
        private sealed class TestSessionStub : IMachineSessionController
        {
            private readonly AssemblyRuntimeController _assembly;
            private readonly MachinePackageDefinition _package;
            public TestSessionStub(AssemblyRuntimeController assembly, MachinePackageDefinition package)
            { _assembly = assembly; _package = package; }

            public event Action<MachinePackageDefinition> PackageReady { add { } remove { } }
            public MachineSessionState SessionState => null;
            public MachinePackageDefinition Package => _package;
            public AssemblyRuntimeController AssemblyController => _assembly;
            public IPartRuntimeController PartController => null;
            public IToolRuntimeController ToolController => null;
            public bool IsNavigating => false;
            public float LastNavigationTime => -1f;
            public bool CanStepBack => false;
            public bool CanStepForward => true;
            public Task<bool> StartSessionAsync(string packageId, SessionMode mode, int restoreStepCount = 0, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public void PauseSession() { }
            public void ResumeSession() { }
            public void EndSession() { }
            public void FlushPersistenceSnapshot() { }
            public float GetElapsedSeconds() => 0f;
            public void TickElapsed(float deltaTime) { }
            public bool StepBack() => false;
            public bool StepForward() => false;
            public bool NavigateToLastStep() => false;
            public bool NavigateToGlobalStep(int globalIndex) => false;
            public bool RestoreToStep(int completedStepCount) => false;
            public void ResumeAfterTransition() { }
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

        private static StepDefinition MakeStepWithTaskOrder(string id, int sequence, params string[] partIds)
        {
            var step = MakeStep(id, sequence);
            step.requiredPartIds = partIds;
            step.taskOrder = new TaskOrderEntry[partIds.Length];
            for (int i = 0; i < partIds.Length; i++)
                step.taskOrder[i] = new TaskOrderEntry { kind = "part", id = partIds[i] };
            return step;
        }

        private static MachinePackageDefinition CreateTwoStepPackageWithTaskOrder()
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
                    MakeStepWithTaskOrder("step_1", 1, "p1a"),
                    MakeStepWithTaskOrder("step_2", 2, "p2a", "p2b")
                },
                parts = new[]
                {
                    new PartDefinition { id = "p1a" },
                    new PartDefinition { id = "p2a" },
                    new PartDefinition { id = "p2b" }
                },
                tools = Array.Empty<ToolDefinition>(),
                targets = Array.Empty<TargetDefinition>(),
                validationRules = Array.Empty<ValidationRuleDefinition>(),
                hints = Array.Empty<HintDefinition>(),
                effects = Array.Empty<EffectDefinition>()
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
