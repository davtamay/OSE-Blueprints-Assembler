using NUnit.Framework;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Unit tests for <see cref="SessionNavigationController"/> and the
    /// <see cref="StepController.SetCompletionBlocked"/> guard it manages.
    ///
    /// Coverage priorities from the project audit:
    ///   - SetCompletionBlocked prevents same-frame re-completion during navigation
    ///   - CountCompletedStepsForAssembly edge cases (null, empty, boundary)
    ///   - CanStepBack / CanStepForward boundary conditions
    /// </summary>
    [TestFixture]
    public sealed class SessionNavigationControllerTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimeEventBus.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeEventBus.Clear();
        }

        // ── StepController.SetCompletionBlocked ──────────────────────────────

        [Test]
        public void SetCompletionBlocked_True_Prevents_CompleteStep()
        {
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(MakeStep("s1"), 0f);

            stepCtrl.SetCompletionBlocked(true);
            stepCtrl.CompleteStep(1f);

            // Step should still be Active — completion was blocked
            Assert.AreEqual(StepState.Active, stepCtrl.CurrentStepState.State);
        }

        [Test]
        public void SetCompletionBlocked_False_After_Block_Allows_CompleteStep()
        {
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(MakeStep("s1"), 0f);

            stepCtrl.SetCompletionBlocked(true);
            stepCtrl.CompleteStep(1f);           // blocked — should not complete
            stepCtrl.SetCompletionBlocked(false);
            stepCtrl.CompleteStep(1f);           // unblocked — should complete

            Assert.AreEqual(StepState.Completed, stepCtrl.CurrentStepState.State);
        }

        [Test]
        public void SetCompletionBlocked_WithNoActiveStep_DoesNotThrow()
        {
            var stepCtrl = new StepController();
            Assert.DoesNotThrow(() => stepCtrl.SetCompletionBlocked(true));
            Assert.DoesNotThrow(() => stepCtrl.CompleteStep(1f));
        }

        [Test]
        public void SetCompletionBlocked_ToggledMultipleTimes_RespectsFinalState()
        {
            var stepCtrl = new StepController();
            stepCtrl.ActivateStep(MakeStep("s1"), 0f);

            stepCtrl.SetCompletionBlocked(true);
            stepCtrl.SetCompletionBlocked(false);
            stepCtrl.SetCompletionBlocked(true);
            // Final state is blocked
            stepCtrl.CompleteStep(1f);

            Assert.AreEqual(StepState.Active, stepCtrl.CurrentStepState.State);
        }

        // ── CountCompletedStepsForAssembly ───────────────────────────────────

        [Test]
        public void CountCompletedStepsForAssembly_Null_Steps_Returns_Zero()
        {
            int count = SessionNavigationController.CountCompletedStepsForAssembly(null, "asm_1", 5);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void CountCompletedStepsForAssembly_Zero_Completed_Returns_Zero()
        {
            var steps = new[] { MakeStep("s1", "asm_1"), MakeStep("s2", "asm_1") };
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, "asm_1", 0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void CountCompletedStepsForAssembly_Null_AssemblyId_Returns_Zero()
        {
            var steps = new[] { MakeStep("s1", "asm_1"), MakeStep("s2", "asm_1") };
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, null, 2);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void CountCompletedStepsForAssembly_Counts_Only_MatchingAssembly()
        {
            var steps = new[]
            {
                MakeStep("s1", "asm_1"),
                MakeStep("s2", "asm_2"),
                MakeStep("s3", "asm_1"),
            };
            // Count how many asm_1 steps appear in the first 3 steps (indices 0..2)
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, "asm_1", 3);
            Assert.AreEqual(2, count); // s1 and s3
        }

        [Test]
        public void CountCompletedStepsForAssembly_Limit_Clamps_To_Array_Length()
        {
            var steps = new[] { MakeStep("s1", "asm_1"), MakeStep("s2", "asm_1") };
            // Requesting completedGlobalCount > array length should not throw
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, "asm_1", 100);
            Assert.AreEqual(2, count);
        }

        [Test]
        public void CountCompletedStepsForAssembly_No_Match_Returns_Zero()
        {
            var steps = new[] { MakeStep("s1", "asm_1"), MakeStep("s2", "asm_1") };
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, "asm_99", 2);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void CountCompletedStepsForAssembly_Single_Step_Match()
        {
            var steps = new[] { MakeStep("s1", "asm_1") };
            int count = SessionNavigationController.CountCompletedStepsForAssembly(steps, "asm_1", 1);
            Assert.AreEqual(1, count);
        }

        // ── CanStepBack / CanStepForward via mock host ───────────────────────

        [Test]
        public void CanStepBack_False_When_No_Package()
        {
            var host = new FakeNavigationHost(package: null);
            var nav = new SessionNavigationController(host);

            Assert.IsFalse(nav.CanStepBack);
        }

        [Test]
        public void CanStepBack_False_When_No_Active_Step()
        {
            var package = MakePackage(MakeStep("s1", "asm_1", 1), MakeStep("s2", "asm_1", 2));
            var host = new FakeNavigationHost(package, activeStepId: null);
            var nav = new SessionNavigationController(host);

            Assert.IsFalse(nav.CanStepBack);
        }

        [Test]
        public void CanStepBack_False_When_At_First_Step()
        {
            var steps = new[] { MakeStep("s1", "asm_1", 1), MakeStep("s2", "asm_1", 2) };
            var package = MakePackage(steps);
            var host = new FakeNavigationHost(package, activeStepId: "s1");
            var nav = new SessionNavigationController(host);

            Assert.IsFalse(nav.CanStepBack);
        }

        [Test]
        public void CanStepBack_True_When_At_Second_Step()
        {
            var steps = new[] { MakeStep("s1", "asm_1", 1), MakeStep("s2", "asm_1", 2) };
            var package = MakePackage(steps);
            var host = new FakeNavigationHost(package, activeStepId: "s2");
            var nav = new SessionNavigationController(host);

            Assert.IsTrue(nav.CanStepBack);
        }

        [Test]
        public void CanStepForward_False_When_At_Last_Step()
        {
            var steps = new[] { MakeStep("s1", "asm_1", 1), MakeStep("s2", "asm_1", 2) };
            var package = MakePackage(steps);
            var host = new FakeNavigationHost(package, activeStepId: "s2");
            var nav = new SessionNavigationController(host);

            Assert.IsFalse(nav.CanStepForward);
        }

        [Test]
        public void CanStepForward_True_When_Not_At_Last_Step()
        {
            var steps = new[] { MakeStep("s1", "asm_1", 1), MakeStep("s2", "asm_1", 2) };
            var package = MakePackage(steps);
            var host = new FakeNavigationHost(package, activeStepId: "s1");
            var nav = new SessionNavigationController(host);

            Assert.IsTrue(nav.CanStepForward);
        }

        [Test]
        public void CanStepForward_False_When_Single_Step_Package()
        {
            var steps = new[] { MakeStep("s1", "asm_1", 1) };
            var package = MakePackage(steps);
            var host = new FakeNavigationHost(package, activeStepId: "s1");
            var nav = new SessionNavigationController(host);

            Assert.IsFalse(nav.CanStepForward);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static StepDefinition MakeStep(string id, string assemblyId = "asm_1", int seq = 1) =>
            new StepDefinition
            {
                id            = id,
                name          = id,
                assemblyId    = assemblyId,
                family        = "Place",
                instructionText = $"Do {id}.",
                sequenceIndex = seq
            };

        private static MachinePackageDefinition MakePackage(params StepDefinition[] steps)
        {
            return new MachinePackageDefinition
            {
                schemaVersion  = "1.0",
                packageVersion = "0.1",
                machine = new MachineDefinition
                {
                    id          = "test_machine",
                    name        = "Test",
                    description = "Test",
                    difficulty  = "beginner",
                    entryAssemblyIds = new[] { "asm_1" }
                },
                assemblies = new[]
                {
                    new AssemblyDefinition
                    {
                        id            = "asm_1",
                        name          = "Assembly 1",
                        machineId     = "test_machine",
                        subassemblyIds = new string[0],
                        stepIds       = System.Array.ConvertAll(steps, s => s.id)
                    }
                },
                steps = steps
            };
        }

        /// <summary>
        /// Minimal INavigationHost stub. Only provides the data needed by
        /// CanStepBack / CanStepForward — no assembly controller required.
        /// </summary>
        private sealed class FakeNavigationHost : INavigationHost
        {
            private readonly string _activeStepId;

            public FakeNavigationHost(MachinePackageDefinition package, string activeStepId = null)
            {
                Package       = package;
                SessionState  = new MachineSessionState { CurrentStepId = activeStepId };
                _activeStepId = activeStepId;
            }

            public MachinePackageDefinition    Package              { get; }
            public AssemblyRuntimeController   AssemblyController   => null;
            public IPartRuntimeController      PartController       => null;
            public MachineSessionState         SessionState         { get; }
            public string[]                    AssemblyOrder        => new[] { "asm_1" };
            public int CurrentAssemblyIndex { get; set; }
        }
    }
}
