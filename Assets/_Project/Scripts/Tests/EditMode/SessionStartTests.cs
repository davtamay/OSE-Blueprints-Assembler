using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Content.Validation;
using OSE.Core;
using OSE.Runtime;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Integration tests for <see cref="MachineSessionController.StartSessionAsync"/>.
    /// Uses a stub <see cref="IMachinePackageLoader"/> so no file system access is required.
    /// </summary>
    [TestFixture]
    public sealed class SessionStartTests
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

        // ── Helpers ──────────────────────────────────────────────────────────

        private static MachinePackageDefinition BuildMinimalPackage(string machineId = "test_machine")
        {
            return new MachinePackageDefinition
            {
                schemaVersion  = "1.0.0",
                packageVersion = "0.1.0",
                machine = new MachineDefinition
                {
                    id          = machineId,
                    name        = "Test Machine",
                    description = "Minimal package for session start tests.",
                    difficulty  = "beginner",
                    entryAssemblyIds = new[] { "asm_1" }
                },
                assemblies = new[]
                {
                    new AssemblyDefinition
                    {
                        id        = "asm_1",
                        name      = "Assembly 1",
                        machineId = machineId,
                        stepIds   = new[] { "step_1" }
                    }
                },
                steps = new[]
                {
                    new StepDefinition
                    {
                        id           = "step_1",
                        name         = "Step 1",
                        assemblyId   = "asm_1",
                        family       = "Place",
                        sequenceIndex = 1,
                        instructionText = "Place the first part."
                    }
                },
                parts           = Array.Empty<PartDefinition>(),
                tools           = Array.Empty<ToolDefinition>(),
                targets         = Array.Empty<TargetDefinition>(),
                validationRules = Array.Empty<ValidationRuleDefinition>(),
                hints           = Array.Empty<HintDefinition>(),
                effects         = Array.Empty<EffectDefinition>()
            };
        }

        private static MachineSessionController BuildController(MachinePackageDefinition package)
        {
            var stub = new StubPackageLoader(package);
            return new MachineSessionController(stub);
        }

        // ── StartSessionAsync — success path ─────────────────────────────────

        [Test]
        public async Task StartSessionAsync_ValidPackage_ReturnsTrue()
        {
            var controller = BuildController(BuildMinimalPackage());

            bool result = await controller.StartSessionAsync("test_machine", SessionMode.Guided);

            Assert.IsTrue(result);
        }

        [Test]
        public async Task StartSessionAsync_ValidPackage_SessionLifecycle_IsStepActive()
        {
            var controller = BuildController(BuildMinimalPackage());

            await controller.StartSessionAsync("test_machine", SessionMode.Guided);

            Assert.AreEqual(SessionLifecycle.StepActive, controller.SessionState.Lifecycle);
        }

        [Test]
        public async Task StartSessionAsync_ValidPackage_PackageReadyFires()
        {
            var controller = BuildController(BuildMinimalPackage());
            MachinePackageDefinition received = null;
            controller.PackageReady += pkg => received = pkg;

            await controller.StartSessionAsync("test_machine", SessionMode.Guided);

            Assert.IsNotNull(received);
            Assert.AreEqual("test_machine", received.machine.id);
        }

        [Test]
        public async Task StartSessionAsync_ValidPackage_FirstStepIsActive()
        {
            var controller = BuildController(BuildMinimalPackage());

            await controller.StartSessionAsync("test_machine", SessionMode.Guided);

            Assert.IsTrue(controller.AssemblyController.StepController.HasActiveStep);
            Assert.AreEqual("step_1", controller.AssemblyController.StepController.CurrentStepDefinition.id);
        }

        [Test]
        public async Task StartSessionAsync_ValidPackage_PublishesStepStateChanged_ToActive()
        {
            var controller = BuildController(BuildMinimalPackage());
            StepStateChanged? activeEvent = null;
            RuntimeEventBus.Subscribe<StepStateChanged>(e =>
            {
                if (e.Current == StepState.Active)
                    activeEvent = e;
            });

            await controller.StartSessionAsync("test_machine", SessionMode.Guided);

            Assert.IsNotNull(activeEvent);
            Assert.AreEqual("step_1", activeEvent.Value.StepId);
        }

        // ── StartSessionAsync — failure paths ─────────────────────────────────

        [Test]
        public async Task StartSessionAsync_EmptyPackageId_ReturnsFalse()
        {
            var controller = BuildController(BuildMinimalPackage());

            bool result = await controller.StartSessionAsync(string.Empty, SessionMode.Guided);

            Assert.IsFalse(result);
        }

        [Test]
        public async Task StartSessionAsync_LoaderReturnsFailure_ReturnsFalse()
        {
            var stub = new StubPackageLoader(null); // null package → failure result
            var controller = new MachineSessionController(stub);

            bool result = await controller.StartSessionAsync("missing_pkg", SessionMode.Guided);

            Assert.IsFalse(result);
        }

        [Test]
        public async Task StartSessionAsync_LoaderReturnsFailure_LifecycleIsError()
        {
            var stub = new StubPackageLoader(null);
            var controller = new MachineSessionController(stub);

            await controller.StartSessionAsync("missing_pkg", SessionMode.Guided);

            Assert.AreEqual(SessionLifecycle.Error, controller.SessionState.Lifecycle);
        }

        [Test]
        public async Task StartSessionAsync_ConcurrentCall_SecondReturnsFalse()
        {
            // Simulate a slow loader so we can fire two starts in parallel.
            var slow = new SlowStubPackageLoader(BuildMinimalPackage(), delayMs: 50);
            var controller = new MachineSessionController(slow);

            // Start first (don't await yet), then immediately start second.
            Task<bool> first  = controller.StartSessionAsync("test_machine", SessionMode.Guided);
            Task<bool> second = controller.StartSessionAsync("test_machine", SessionMode.Guided);

            bool[] results = await Task.WhenAll(first, second);

            // Exactly one should succeed.
            Assert.IsTrue(results[0] || results[1]);
            Assert.IsFalse(results[0] && results[1],
                "Both concurrent StartSessionAsync calls returned true — re-entrant load guard failed.");
        }

        // ── Stub loader implementations ───────────────────────────────────────

        private sealed class StubPackageLoader : IMachinePackageLoader
        {
            private readonly MachinePackageDefinition _package;

            public StubPackageLoader(MachinePackageDefinition package) => _package = package;

            public Task<MachinePackageLoadResult> LoadFromStreamingAssetsAsync(
                string packageId,
                CancellationToken cancellationToken = default)
            {
                var result = _package != null
                    ? new MachinePackageLoadResult(
                        packageId, "stub://", _package, MachinePackageValidationResult.Valid, string.Empty)
                    : new MachinePackageLoadResult(
                        packageId, "stub://", null, MachinePackageValidationResult.Valid, "Stub: package not found.");

                return Task.FromResult(result);
            }
        }

        private sealed class SlowStubPackageLoader : IMachinePackageLoader
        {
            private readonly MachinePackageDefinition _package;
            private readonly int _delayMs;

            public SlowStubPackageLoader(MachinePackageDefinition package, int delayMs)
            {
                _package = package;
                _delayMs = delayMs;
            }

            public async Task<MachinePackageLoadResult> LoadFromStreamingAssetsAsync(
                string packageId,
                CancellationToken cancellationToken = default)
            {
                await Task.Delay(_delayMs, cancellationToken);
                return new MachinePackageLoadResult(
                    packageId, "stub://", _package, MachinePackageValidationResult.Valid, string.Empty);
            }
        }
    }
}
