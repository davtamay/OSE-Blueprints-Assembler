using System.Collections.Generic;
using NUnit.Framework;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class StepControllerTests
    {
        private StepController _controller;

        [SetUp]
        public void SetUp()
        {
            RuntimeEventBus.Clear();
            _controller = new StepController();
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeEventBus.Clear();
        }

        // ── Helper ─────────────────────────────────────────────────────────────

        private static StepDefinition MakeStep(string id = "step-001")
        {
            return new StepDefinition { id = id };
        }

        // ── 1. ActivateStep ────────────────────────────────────────────────────

        [Test]
        public void ActivateStep_SetsHasActiveStep_True()
        {
            _controller.ActivateStep(MakeStep(), 0f);

            Assert.IsTrue(_controller.HasActiveStep);
        }

        [Test]
        public void ActivateStep_SetsState_Active()
        {
            _controller.ActivateStep(MakeStep(), 0f);

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ActivateStep_SetsCurrentStepDefinition()
        {
            var step = MakeStep("step-abc");
            _controller.ActivateStep(step, 0f);

            Assert.AreEqual("step-abc", _controller.CurrentStepDefinition.id);
        }

        [Test]
        public void ActivateStep_PublishesStepStateChanged_ToActive()
        {
            var events = new List<StepStateChanged>();
            RuntimeEventBus.Subscribe<StepStateChanged>(e => events.Add(e));

            _controller.ActivateStep(MakeStep("step-001"), 5f);

            // TransitionTo fires Available then Active — the last event must be Active
            var last = events[events.Count - 1];
            Assert.AreEqual(StepState.Active, last.Current);
            Assert.AreEqual("step-001", last.StepId);
            Assert.AreEqual(5f, last.AtSeconds);
        }

        // ── 2. ActivateStep null ───────────────────────────────────────────────

        [Test]
        public void ActivateStep_Null_DoesNotSetHasActiveStep()
        {
            _controller.ActivateStep(null, 0f);

            Assert.IsFalse(_controller.HasActiveStep);
        }

        [Test]
        public void ActivateStep_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.ActivateStep(null, 0f));
        }

        // ── 3. CompleteStep from Active ────────────────────────────────────────

        [Test]
        public void CompleteStep_FromActive_SetsState_Completed()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(10f);

            Assert.AreEqual(StepState.Completed, _controller.CurrentStepState.State);
        }

        [Test]
        public void CompleteStep_FromActive_SetsHasActiveStep_False()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(10f);

            Assert.IsFalse(_controller.HasActiveStep);
        }

        [Test]
        public void CompleteStep_PublishesStepStateChanged_ToCompleted()
        {
            StepStateChanged? received = null;
            RuntimeEventBus.Subscribe<StepStateChanged>(e =>
            {
                if (e.Current == StepState.Completed)
                    received = e;
            });

            _controller.ActivateStep(MakeStep("step-002"), 0f);
            _controller.CompleteStep(20f);

            Assert.IsNotNull(received);
            Assert.AreEqual("step-002", received.Value.StepId);
            Assert.AreEqual(StepState.Active, received.Value.Previous);
            Assert.AreEqual(20f, received.Value.AtSeconds);
        }

        // ── 4. CompleteStep with no active step ────────────────────────────────

        [Test]
        public void CompleteStep_WithNoActiveStep_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.CompleteStep(0f));
        }

        [Test]
        public void CompleteStep_WithNoActiveStep_HasActiveStep_RemainsFlase()
        {
            _controller.CompleteStep(0f);

            Assert.IsFalse(_controller.HasActiveStep);
        }

        // ── 5. CompleteStep from wrong state ───────────────────────────────────

        [Test]
        public void CompleteStep_FromCompleted_DoesNotThrow()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);

            // Calling CompleteStep again from Completed — wrong state, should not throw
            Assert.DoesNotThrow(() => _controller.CompleteStep(2f));
        }

        [Test]
        public void CompleteStep_FromCompleted_StateRemainsCompleted()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);
            _controller.CompleteStep(2f);

            Assert.AreEqual(StepState.Completed, _controller.CurrentStepState.State);
        }

        [Test]
        public void CompleteStep_FromSuspended_DoesNotThrow()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();

            Assert.DoesNotThrow(() => _controller.CompleteStep(5f));
        }

        [Test]
        public void CompleteStep_FromSuspended_StateRemainsSuspended()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();
            _controller.CompleteStep(5f);

            Assert.AreEqual(StepState.Suspended, _controller.CurrentStepState.State);
        }

        // ── 6. FailAttempt ─────────────────────────────────────────────────────

        [Test]
        public void FailAttempt_IncrementsAttemptCount()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.FailAttempt();

            Assert.AreEqual(1, _controller.CurrentStepState.AttemptCount);
        }

        [Test]
        public void FailAttempt_TransitionsActiveToFailedAttemptBackToActive()
        {
            var states = new List<StepState>();
            RuntimeEventBus.Subscribe<StepStateChanged>(e => states.Add(e.Current));

            _controller.ActivateStep(MakeStep(), 0f);
            states.Clear(); // discard activation transitions

            _controller.FailAttempt();

            Assert.AreEqual(2, states.Count, "Expected FailedAttempt then Active");
            Assert.AreEqual(StepState.FailedAttempt, states[0]);
            Assert.AreEqual(StepState.Active, states[1]);
        }

        [Test]
        public void FailAttempt_FinalState_IsActive()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.FailAttempt();

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void FailAttempt_MultipleFailures_AccumulatesAttemptCount()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.FailAttempt();
            _controller.FailAttempt();
            _controller.FailAttempt();

            Assert.AreEqual(3, _controller.CurrentStepState.AttemptCount);
        }

        // ── 7. FailAttempt when not active ─────────────────────────────────────

        [Test]
        public void FailAttempt_WhenNoActiveStep_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.FailAttempt());
        }

        [Test]
        public void FailAttempt_WhenNoActiveStep_HasActiveStep_RemainsFalse()
        {
            _controller.FailAttempt();

            Assert.IsFalse(_controller.HasActiveStep);
        }

        [Test]
        public void FailAttempt_WhenCompleted_DoesNotChangeState()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);
            _controller.FailAttempt(); // should be ignored

            Assert.AreEqual(StepState.Completed, _controller.CurrentStepState.State);
        }

        // ── 8. SuspendStep ─────────────────────────────────────────────────────

        [Test]
        public void SuspendStep_FromActive_SetsState_Suspended()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();

            Assert.AreEqual(StepState.Suspended, _controller.CurrentStepState.State);
        }

        [Test]
        public void SuspendStep_FromActive_HasActiveStep_RemainsTrue()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();

            // Suspended is not terminal, so HasActiveStep should still be true
            Assert.IsTrue(_controller.HasActiveStep);
        }

        [Test]
        public void SuspendStep_WhenNotActive_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.SuspendStep());
        }

        [Test]
        public void SuspendStep_WhenAlreadySuspended_DoesNotThrow()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();

            Assert.DoesNotThrow(() => _controller.SuspendStep());
        }

        [Test]
        public void SuspendStep_WhenAlreadySuspended_StateRemainsSuspended()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();
            _controller.SuspendStep(); // second call is a no-op

            Assert.AreEqual(StepState.Suspended, _controller.CurrentStepState.State);
        }

        [Test]
        public void SuspendStep_PublishesStepStateChanged_ToSuspended()
        {
            StepStateChanged? received = null;
            RuntimeEventBus.Subscribe<StepStateChanged>(e =>
            {
                if (e.Current == StepState.Suspended)
                    received = e;
            });

            _controller.ActivateStep(MakeStep("step-s"), 0f);
            _controller.SuspendStep();

            Assert.IsNotNull(received);
            Assert.AreEqual("step-s", received.Value.StepId);
            Assert.AreEqual(StepState.Active, received.Value.Previous);
        }

        // ── 9. ResumeStep ──────────────────────────────────────────────────────

        [Test]
        public void ResumeStep_FromSuspended_SetsState_Active()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();
            _controller.ResumeStep(15f);

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ResumeStep_FromSuspended_HasActiveStep_True()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.SuspendStep();
            _controller.ResumeStep(15f);

            Assert.IsTrue(_controller.HasActiveStep);
        }

        [Test]
        public void ResumeStep_WhenNotSuspended_DoesNotThrow()
        {
            _controller.ActivateStep(MakeStep(), 0f);

            Assert.DoesNotThrow(() => _controller.ResumeStep(5f));
        }

        [Test]
        public void ResumeStep_WhenNotSuspended_StateRemainsActive()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.ResumeStep(5f); // no-op: already Active, not Suspended

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ResumeStep_WhenNoStep_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.ResumeStep(0f));
        }

        [Test]
        public void ResumeStep_PublishesStepStateChanged_ToActive()
        {
            StepStateChanged? received = null;
            RuntimeEventBus.Subscribe<StepStateChanged>(e =>
            {
                if (e.Previous == StepState.Suspended && e.Current == StepState.Active)
                    received = e;
            });

            _controller.ActivateStep(MakeStep("step-r"), 0f);
            _controller.SuspendStep();
            _controller.ResumeStep(30f);

            Assert.IsNotNull(received);
            Assert.AreEqual("step-r", received.Value.StepId);
            Assert.AreEqual(30f, received.Value.AtSeconds);
        }

        // ── 10. Reset ──────────────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsCurrentStepDefinition()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.Reset();

            Assert.IsNull(_controller.CurrentStepDefinition);
        }

        [Test]
        public void Reset_SetsHasActiveStep_False()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.Reset();

            Assert.IsFalse(_controller.HasActiveStep);
        }

        [Test]
        public void Reset_WhenNoActiveStep_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _controller.Reset());
        }

        [Test]
        public void Reset_AfterComplete_HasActiveStep_RemainsFalse()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);
            _controller.Reset();

            Assert.IsFalse(_controller.HasActiveStep);
        }

        [Test]
        public void Reset_ClearsStepState_ToDefault()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.FailAttempt(); // bump AttemptCount to 1
            _controller.Reset();

            Assert.AreEqual(default(RuntimeStepState), _controller.CurrentStepState);
        }

        // ── 11. Valid FSM transitions via public API ────────────────────────────

        [Test]
        public void ValidTransitions_ActivateThenComplete_Succeeds()
        {
            Assert.DoesNotThrow(() =>
            {
                _controller.ActivateStep(MakeStep(), 0f);
                _controller.CompleteStep(1f);
            });

            Assert.AreEqual(StepState.Completed, _controller.CurrentStepState.State);
        }

        [Test]
        public void ValidTransitions_ActivateSuspendResume_Succeeds()
        {
            Assert.DoesNotThrow(() =>
            {
                _controller.ActivateStep(MakeStep(), 0f);
                _controller.SuspendStep();
                _controller.ResumeStep(5f);
            });

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ValidTransitions_ActivateFailActivate_Succeeds()
        {
            Assert.DoesNotThrow(() =>
            {
                _controller.ActivateStep(MakeStep(), 0f);
                _controller.FailAttempt();
            });

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ValidTransitions_ActivateNewStepOverCompleted_Succeeds()
        {
            _controller.ActivateStep(MakeStep("step-A"), 0f);
            _controller.CompleteStep(1f);

            Assert.DoesNotThrow(() => _controller.ActivateStep(MakeStep("step-B"), 2f));

            Assert.AreEqual("step-B", _controller.CurrentStepDefinition.id);
            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        // ── 12. FSM validator: invalid transition warned but does not throw ─────

        [Test]
        public void CompleteStep_CalledTwice_SecondCallDoesNotThrow()
        {
            // First complete is valid (Active → Completed).
            // Second complete is invalid: HasActiveStep is false after Completed.
            // The guard in CompleteStep catches this and warns — no exception.
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);

            Assert.DoesNotThrow(() => _controller.CompleteStep(2f));
        }

        [Test]
        public void CompleteStep_CalledTwice_StateRemainsCompleted()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.CompleteStep(1f);
            _controller.CompleteStep(2f); // second call must be silently ignored

            Assert.AreEqual(StepState.Completed, _controller.CurrentStepState.State);
        }

        // ── 13. ActivateStep over existing active step ─────────────────────────

        [Test]
        public void ActivateStep_OverExistingActiveStep_DoesNotThrow()
        {
            _controller.ActivateStep(MakeStep("step-A"), 0f);

            Assert.DoesNotThrow(() => _controller.ActivateStep(MakeStep("step-B"), 1f));
        }

        [Test]
        public void ActivateStep_OverExistingActiveStep_SwitchesToNewStep()
        {
            _controller.ActivateStep(MakeStep("step-A"), 0f);
            _controller.ActivateStep(MakeStep("step-B"), 1f);

            Assert.AreEqual("step-B", _controller.CurrentStepDefinition.id);
        }

        [Test]
        public void ActivateStep_OverExistingActiveStep_StateIsActive()
        {
            _controller.ActivateStep(MakeStep("step-A"), 0f);
            _controller.ActivateStep(MakeStep("step-B"), 1f);

            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        [Test]
        public void ActivateStep_OverExistingActiveStep_HasActiveStep_IsTrue()
        {
            _controller.ActivateStep(MakeStep("step-A"), 0f);
            _controller.ActivateStep(MakeStep("step-B"), 1f);

            Assert.IsTrue(_controller.HasActiveStep);
        }

        // ── Event bus integration ──────────────────────────────────────────────

        [Test]
        public void ActivateStep_PublishesTwoEvents_AvailableThenActive()
        {
            var states = new List<StepState>();
            RuntimeEventBus.Subscribe<StepStateChanged>(e => states.Add(e.Current));

            _controller.ActivateStep(MakeStep(), 0f);

            Assert.AreEqual(2, states.Count);
            Assert.AreEqual(StepState.Available, states[0]);
            Assert.AreEqual(StepState.Active, states[1]);
        }

        [Test]
        public void StepId_IsPreservedThroughStateTransitions()
        {
            var ids = new List<string>();
            RuntimeEventBus.Subscribe<StepStateChanged>(e => ids.Add(e.StepId));

            _controller.ActivateStep(MakeStep("my-step"), 0f);
            _controller.SuspendStep();
            _controller.ResumeStep(5f);
            _controller.CompleteStep(10f);

            foreach (var id in ids)
                Assert.AreEqual("my-step", id);
        }

        [Test]
        public void AttemptCount_IsPreservedAcrossMultipleFailures()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            _controller.FailAttempt();
            _controller.FailAttempt();

            Assert.AreEqual(2, _controller.CurrentStepState.AttemptCount);
            Assert.AreEqual(StepState.Active, _controller.CurrentStepState.State);
        }

        // ── Phase I.c.1 — TaskCursor lifecycle ─────────────────────────────────

        [Test]
        public void ActivateStep_ConstructsTaskCursorFromTaskOrder()
        {
            var step = new StepDefinition
            {
                id = "step-with-tasks",
                taskOrder = new[]
                {
                    new TaskOrderEntry { kind = "part", id = "a" },
                    new TaskOrderEntry { kind = "part", id = "b" }
                }
            };
            _controller.ActivateStep(step, 0f);

            Assert.IsNotNull(_controller.CurrentTaskCursor);
            Assert.AreEqual(2, _controller.CurrentTaskCursor.TotalSpans);
            Assert.IsFalse(_controller.CurrentTaskCursor.IsComplete);
        }

        [Test]
        public void ActivateStep_EmptyTaskOrder_CursorIsCompleteImmediately()
        {
            _controller.ActivateStep(MakeStep(), 0f);

            Assert.IsNotNull(_controller.CurrentTaskCursor);
            Assert.IsTrue(_controller.CurrentTaskCursor.IsComplete);
        }

        [Test]
        public void Reset_ClearsCurrentTaskCursor()
        {
            _controller.ActivateStep(MakeStep(), 0f);
            Assert.IsNotNull(_controller.CurrentTaskCursor);

            _controller.Reset();
            Assert.IsNull(_controller.CurrentTaskCursor);
        }
    }
}
