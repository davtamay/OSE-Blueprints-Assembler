using NUnit.Framework;
using OSE.Core;
using OSE.Persistence;
using UnityEngine;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Unit tests for <see cref="PlayerPrefsPersistenceService"/>.
    /// These tests write real PlayerPrefs keys with a test-only prefix and
    /// clean them up in TearDown so they don't pollute the editor environment.
    /// </summary>
    [TestFixture]
    public sealed class PlayerPrefsPersistenceServiceTests
    {
        private const string TestMachineId = "test_persist_machine";
        private PlayerPrefsPersistenceService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new PlayerPrefsPersistenceService();
            // Ensure no stale data from a previous run
            _service.ClearSession(TestMachineId);
        }

        [TearDown]
        public void TearDown()
        {
            _service.ClearSession(TestMachineId);
        }

        // ── HasSavedSession ──────────────────────────────────────────────────

        [Test]
        public void HasSavedSession_WhenNothingSaved_ReturnsFalse()
        {
            Assert.IsFalse(_service.HasSavedSession(TestMachineId));
        }

        [Test]
        public void HasSavedSession_AfterSave_ReturnsTrue()
        {
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 3));

            Assert.IsTrue(_service.HasSavedSession(TestMachineId));
        }

        [Test]
        public void HasSavedSession_NullOrEmptyId_ReturnsFalse()
        {
            Assert.IsFalse(_service.HasSavedSession(null));
            Assert.IsFalse(_service.HasSavedSession(string.Empty));
        }

        // ── SaveSession / LoadSession round-trip ─────────────────────────────

        [Test]
        public void LoadSession_AfterSave_RestoresCompletedStepCount()
        {
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 7));

            var loaded = _service.LoadSession(TestMachineId);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, loaded.CompletedStepCount);
        }

        [Test]
        public void LoadSession_AfterSave_RestoresMachineId()
        {
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 1));

            var loaded = _service.LoadSession(TestMachineId);

            Assert.AreEqual(TestMachineId, loaded.MachineId);
        }

        [Test]
        public void LoadSession_AfterSave_RestoresElapsedSeconds()
        {
            var state = BuildState(TestMachineId, completedSteps: 2);
            state.ElapsedSeconds = 123.45f;
            _service.SaveSession(state);

            var loaded = _service.LoadSession(TestMachineId);

            Assert.AreEqual(123.45f, loaded.ElapsedSeconds, delta: 0.001f);
        }

        [Test]
        public void LoadSession_WhenNothingSaved_ReturnsNull()
        {
            var loaded = _service.LoadSession(TestMachineId);

            Assert.IsNull(loaded);
        }

        [Test]
        public void LoadSession_NullId_ReturnsNull()
        {
            Assert.IsNull(_service.LoadSession(null));
        }

        // ── ClearSession ─────────────────────────────────────────────────────

        [Test]
        public void ClearSession_RemovesExistingEntry()
        {
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 5));

            _service.ClearSession(TestMachineId);

            Assert.IsFalse(_service.HasSavedSession(TestMachineId));
            Assert.IsNull(_service.LoadSession(TestMachineId));
        }

        [Test]
        public void ClearSession_OnNonExistentKey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.ClearSession("nonexistent_machine_key_xyz"));
        }

        // ── Corrupt data recovery ─────────────────────────────────────────────

        [Test]
        public void LoadSession_CorruptJson_ReturnsNullAndClearsKey()
        {
            // Write garbage JSON directly via PlayerPrefs, bypassing the service.
            PlayerPrefs.SetString("ose_session_" + TestMachineId, "{ this is not valid json !!!");
            PlayerPrefs.Save();

            var loaded = _service.LoadSession(TestMachineId);

            Assert.IsNull(loaded, "Corrupt save should return null.");
            Assert.IsFalse(_service.HasSavedSession(TestMachineId),
                "Corrupt key should be cleared after failed load.");
        }

        [Test]
        public void LoadSession_EmptyJson_ReturnsNull()
        {
            PlayerPrefs.SetString("ose_session_" + TestMachineId, string.Empty);
            PlayerPrefs.Save();

            var loaded = _service.LoadSession(TestMachineId);

            Assert.IsNull(loaded);
        }

        // ── Overwrite behavior ────────────────────────────────────────────────

        [Test]
        public void SaveSession_Twice_SecondValueWins()
        {
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 3));
            _service.SaveSession(BuildState(TestMachineId, completedSteps: 9));

            var loaded = _service.LoadSession(TestMachineId);

            Assert.AreEqual(9, loaded.CompletedStepCount);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static MachineSessionState BuildState(string machineId, int completedSteps)
        {
            return new MachineSessionState
            {
                MachineId          = machineId,
                MachineVersion     = "0.1.0",
                Mode               = SessionMode.Guided,
                CompletedStepCount = completedSteps,
                Lifecycle          = SessionLifecycle.StepActive
            };
        }
    }
}
