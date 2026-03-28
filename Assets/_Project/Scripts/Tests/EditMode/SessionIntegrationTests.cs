using System.Collections.Generic;
using NUnit.Framework;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Integration tests for session state progression and persistence round-trips.
    ///
    /// Coverage:
    ///   1. MachineSessionState tracks completed step count after step transitions.
    ///   2. JSON serialization/deserialization preserves CompletedStepCount and MachineId.
    ///   3. A stub IPersistenceService can save and reload a session state snapshot.
    ///
    /// Note: PlayerPrefsPersistenceService requires PlayerPrefs (Unity runtime) so
    /// cannot be used in EditMode. The tests exercise the same JSON round-trip logic
    /// via JsonUtility directly and via an in-memory stub, covering the full testable
    /// surface without a MonoBehaviour dependency.
    /// </summary>
    [TestFixture]
    public class SessionIntegrationTests
    {
        // ── Minimal in-memory IPersistenceService stub ─────────────────────────

        private sealed class InMemoryPersistenceService : IPersistenceService
        {
            private readonly Dictionary<string, string> _store = new Dictionary<string, string>();

            public void SaveSession(MachineSessionState state)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.MachineId))
                    return;

                _store[state.MachineId] = JsonUtility.ToJson(state);
            }

            public MachineSessionState LoadSession(string machineId)
            {
                if (!_store.TryGetValue(machineId, out string json))
                    return null;

                return JsonUtility.FromJson<MachineSessionState>(json);
            }

            public bool HasSavedSession(string machineId) => _store.ContainsKey(machineId);

            public void ClearSession(string machineId) => _store.Remove(machineId);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static MachineSessionState MakeSessionState(string machineId, int completedStepCount)
        {
            return new MachineSessionState
            {
                MachineId = machineId,
                MachineVersion = "1.0",
                Mode = SessionMode.Tutorial,
                Lifecycle = SessionLifecycle.StepActive,
                CompletedStepCount = completedStepCount,
                ElapsedSeconds = 42f
            };
        }

        // ── 1. MachineSessionState progression ─────────────────────────────────

        [Test]
        public void MachineSessionState_InitialCompletedStepCount_IsZero()
        {
            var state = new MachineSessionState { MachineId = "test_machine" };

            Assert.AreEqual(0, state.CompletedStepCount);
        }

        [Test]
        public void MachineSessionState_CompletedStepCount_CanBeAdvanced()
        {
            var state = MakeSessionState("test_machine", 0);
            state.CompletedStepCount++;

            Assert.AreEqual(1, state.CompletedStepCount);
        }

        [Test]
        public void MachineSessionState_AfterTwoSteps_CompletedStepCountIsTwo()
        {
            var state = MakeSessionState("test_machine", 0);
            state.CompletedStepCount++;
            state.CompletedStepCount++;

            Assert.AreEqual(2, state.CompletedStepCount);
        }

        // ── 2. JSON serialization round-trip ───────────────────────────────────

        [Test]
        public void JsonRoundTrip_PreservesCompletedStepCount()
        {
            var original = MakeSessionState("machine_001", completedStepCount: 3);

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<MachineSessionState>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(3, restored.CompletedStepCount);
        }

        [Test]
        public void JsonRoundTrip_PreservesMachineId()
        {
            var original = MakeSessionState("machine_001", completedStepCount: 1);

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<MachineSessionState>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual("machine_001", restored.MachineId);
        }

        [Test]
        public void JsonRoundTrip_PreservesMachineVersion()
        {
            var original = MakeSessionState("machine_001", completedStepCount: 0);
            original.MachineVersion = "2.3";

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<MachineSessionState>(json);

            Assert.AreEqual("2.3", restored.MachineVersion);
        }

        [Test]
        public void JsonRoundTrip_PreservesElapsedSeconds()
        {
            var original = MakeSessionState("machine_001", completedStepCount: 2);
            original.ElapsedSeconds = 125.5f;

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<MachineSessionState>(json);

            Assert.AreEqual(125.5f, restored.ElapsedSeconds, delta: 0.001f);
        }

        // ── 3. In-memory persistence service ──────────────────────────────────

        [Test]
        public void InMemoryPersistence_HasSavedSession_ReturnsFalse_BeforeSave()
        {
            var svc = new InMemoryPersistenceService();

            Assert.IsFalse(svc.HasSavedSession("machine_001"));
        }

        [Test]
        public void InMemoryPersistence_SaveAndLoad_RestoresCompletedStepIndex()
        {
            var svc = new InMemoryPersistenceService();
            var state = MakeSessionState("machine_001", completedStepCount: 1);

            svc.SaveSession(state);
            var restored = svc.LoadSession("machine_001");

            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored.CompletedStepCount);
        }

        [Test]
        public void InMemoryPersistence_HasSavedSession_ReturnsTrueAfterSave()
        {
            var svc = new InMemoryPersistenceService();
            svc.SaveSession(MakeSessionState("machine_001", completedStepCount: 0));

            Assert.IsTrue(svc.HasSavedSession("machine_001"));
        }

        [Test]
        public void InMemoryPersistence_ClearSession_RemovesEntry()
        {
            var svc = new InMemoryPersistenceService();
            svc.SaveSession(MakeSessionState("machine_001", completedStepCount: 2));

            svc.ClearSession("machine_001");

            Assert.IsFalse(svc.HasSavedSession("machine_001"));
        }

        [Test]
        public void InMemoryPersistence_LoadAfterClear_ReturnsNull()
        {
            var svc = new InMemoryPersistenceService();
            svc.SaveSession(MakeSessionState("machine_001", completedStepCount: 1));
            svc.ClearSession("machine_001");

            var restored = svc.LoadSession("machine_001");

            Assert.IsNull(restored);
        }

        // ── 4. ServiceRegistry integration with persistence ───────────────────

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

        [Test]
        public void ServiceRegistry_CanRegisterAndResolveIPersistenceService()
        {
            var svc = new InMemoryPersistenceService();
            ServiceRegistry.Register<IPersistenceService>(svc);

            bool found = ServiceRegistry.TryGet<IPersistenceService>(out var resolved);

            Assert.IsTrue(found);
            Assert.AreSame(svc, resolved);
        }

        [Test]
        public void SessionState_RoundTrip_ThroughRegisteredPersistenceService()
        {
            var svc = new InMemoryPersistenceService();
            ServiceRegistry.Register<IPersistenceService>(svc);

            // Simulate step 1 completed
            var state = MakeSessionState("heatbed_v18", completedStepCount: 0);
            state.CompletedStepCount++;

            // Save via registered service
            if (ServiceRegistry.TryGet<IPersistenceService>(out var persistence))
                persistence.SaveSession(state);

            // Reload and verify
            var loaded = svc.LoadSession("heatbed_v18");
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.CompletedStepCount);
            Assert.AreEqual("heatbed_v18", loaded.MachineId);
        }
    }
}
