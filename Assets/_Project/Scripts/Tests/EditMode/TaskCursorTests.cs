using System.Collections.Generic;
using NUnit.Framework;
using OSE.Content;
using OSE.Runtime;

namespace OSE.Tests.EditMode
{
    /// <summary>
    /// Phase I.b — exercises <see cref="TaskCursor"/>'s span-walking, event
    /// firing, and required-only advancement semantics on synthetic step
    /// fixtures. Pure data-structure tests; no runtime controller is wired
    /// until Phase I.c / I.d, so these are the authoritative contract for
    /// what the cursor promises when those integrations land.
    /// </summary>
    [TestFixture]
    public class TaskCursorTests
    {
        // ── fixture builders ──────────────────────────────────────────────

        private static TaskOrderEntry Part(string id, string set = null, bool optional = false)
            => new TaskOrderEntry { kind = "part", id = id, unorderedSet = set, isOptional = optional };

        private static TaskOrderEntry Tool(string id, string set = null, bool optional = false)
            => new TaskOrderEntry { kind = "toolAction", id = id, unorderedSet = set, isOptional = optional };

        private static StepDefinition Step(params TaskOrderEntry[] tasks)
            => new StepDefinition { id = "s1", sequenceIndex = 1, taskOrder = tasks };

        private static TaskCursor MakeCursor(StepDefinition step, List<TaskSpanOpenedInfo> spanLog)
        {
            var cursor = new TaskCursor(step);
            cursor.TaskSpanOpened += spanLog.Add;
            return cursor;
        }

        // ── empty / null ─────────────────────────────────────────────────

        [Test]
        public void EmptyTaskOrder_IsCompleteFromConstruction()
        {
            var cursor = new TaskCursor(Step());
            Assert.IsTrue(cursor.IsComplete);
            Assert.AreEqual(0, cursor.OpenTasks.Count);
            Assert.AreEqual(0, cursor.TotalSpans);
        }

        [Test]
        public void NullStep_IsCompleteAndQuiet()
        {
            var cursor = new TaskCursor(null);
            Assert.IsTrue(cursor.IsComplete);
            Assert.AreEqual(0, cursor.TotalSpans);

            // Null kind/id calls must not throw.
            Assert.DoesNotThrow(() => cursor.NotifyTaskCompleted(null, null));
        }

        [Test]
        public void Start_EmptyTaskOrder_DoesNotFireEvent()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(), log);
            cursor.Start();
            Assert.AreEqual(0, log.Count);
        }

        // ── singletons (strict sequential) ────────────────────────────────

        [Test]
        public void SingleSingleton_AdvancesToCompleteOnNotify()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(Part("a")), log);
            cursor.Start();

            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(1, cursor.OpenTasks.Count);
            Assert.AreEqual("a", cursor.OpenTasks[0].id);
            Assert.IsNull(cursor.CurrentSetLabel);
            Assert.AreEqual(1, cursor.RemainingInCurrentSet);
            Assert.IsTrue(cursor.IsPartTaskOpen("a"));

            cursor.NotifyTaskCompleted("part", "a");

            Assert.IsTrue(cursor.IsComplete);
            Assert.IsFalse(cursor.IsPartTaskOpen("a"));
        }

        [Test]
        public void ThreeSingletonsSequential_AdvanceOneAtATime()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(Part("a"), Part("b"), Tool("c")), log);
            cursor.Start();

            // Initial span: just "a".
            Assert.AreEqual(1, log.Count);
            Assert.AreEqual("a", log[0].Entries[0].id);
            Assert.AreEqual(3, cursor.TotalSpans);
            Assert.IsTrue(cursor.IsPartTaskOpen("a"));
            Assert.IsFalse(cursor.IsPartTaskOpen("b"));

            cursor.NotifyTaskCompleted("part", "a");
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual("b", log[1].Entries[0].id);
            Assert.IsFalse(cursor.IsPartTaskOpen("a"));
            Assert.IsTrue(cursor.IsPartTaskOpen("b"));

            cursor.NotifyTaskCompleted("part", "b");
            Assert.AreEqual(3, log.Count);
            Assert.AreEqual("c", log[2].Entries[0].id);
            Assert.IsTrue(cursor.IsToolActionOpen("c"));

            cursor.NotifyTaskCompleted("toolAction", "c");
            Assert.IsTrue(cursor.IsComplete);
            Assert.AreEqual(3, log.Count);  // no fourth event for completion
        }

        // ── unordered sets ────────────────────────────────────────────────

        [Test]
        public void UnorderedSetOfThree_OpensAllAtOnce_AdvancesOnLastCompletion()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(
                Part("a", set: "panel"),
                Part("b", set: "panel"),
                Part("c", set: "panel")), log);
            cursor.Start();

            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(3, cursor.OpenTasks.Count);
            Assert.AreEqual("panel", cursor.CurrentSetLabel);
            Assert.AreEqual(3, cursor.RemainingInCurrentSet);

            // Complete out of order.
            cursor.NotifyTaskCompleted("part", "b");
            Assert.IsFalse(cursor.IsComplete);
            Assert.AreEqual(2, cursor.RemainingInCurrentSet);
            Assert.IsTrue(cursor.IsPartTaskOpen("a"));
            Assert.IsFalse(cursor.IsPartTaskOpen("b"));
            Assert.IsTrue(cursor.IsPartTaskOpen("c"));

            cursor.NotifyTaskCompleted("part", "c");
            Assert.IsFalse(cursor.IsComplete);
            Assert.AreEqual(1, cursor.RemainingInCurrentSet);

            cursor.NotifyTaskCompleted("part", "a");
            Assert.IsTrue(cursor.IsComplete);
        }

        [Test]
        public void SetThenSingleton_RespectsBoundary()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(
                Part("a", set: "frames"),
                Part("b", set: "frames"),
                Tool("tack")), log);
            cursor.Start();

            Assert.AreEqual(2, cursor.TotalSpans);
            Assert.AreEqual("frames", cursor.CurrentSetLabel);
            Assert.IsFalse(cursor.IsToolActionOpen("tack"), "singleton tack should not be open while the frame set is active");

            cursor.NotifyTaskCompleted("part", "a");
            cursor.NotifyTaskCompleted("part", "b");

            Assert.AreEqual(2, log.Count);
            Assert.IsNull(cursor.CurrentSetLabel);
            Assert.IsTrue(cursor.IsToolActionOpen("tack"));

            cursor.NotifyTaskCompleted("toolAction", "tack");
            Assert.IsTrue(cursor.IsComplete);
        }

        // ── optional task semantics (Phase I risk #3 locked decision) ────

        [Test]
        public void OptionalMemberInSet_DoesNotBlockAdvancement_WhenRequiredsDone()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(
                Part("a", set: "mixed"),
                Part("b", set: "mixed"),
                Part("c", set: "mixed", optional: true)), log);
            cursor.Start();

            Assert.AreEqual(2, cursor.RemainingInCurrentSet, "optional members excluded from required count");

            cursor.NotifyTaskCompleted("part", "a");
            cursor.NotifyTaskCompleted("part", "b");

            // All required members done — cursor advances past the span, optional "c" auto-closes.
            Assert.IsTrue(cursor.IsComplete);
        }

        [Test]
        public void OptionalCompletionBeforeRequireds_IsRecorded_DoesNotAdvance()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(
                Part("a", set: "mixed"),
                Part("b", set: "mixed", optional: true)), log);
            cursor.Start();

            // Optional first — required "a" still open.
            cursor.NotifyTaskCompleted("part", "b");
            Assert.IsFalse(cursor.IsComplete);
            Assert.AreEqual(1, cursor.RemainingInCurrentSet);

            cursor.NotifyTaskCompleted("part", "a");
            Assert.IsTrue(cursor.IsComplete);
        }

        // ── stale / unknown completions ──────────────────────────────────

        [Test]
        public void NotifyTaskCompleted_ForTaskNotInCurrentSpan_IsIgnored()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(Part("a"), Part("b")), log);
            cursor.Start();

            // Notify about "b" while only "a" is open — should have no effect.
            cursor.NotifyTaskCompleted("part", "b");
            Assert.IsFalse(cursor.IsComplete);
            Assert.AreEqual(1, log.Count);
            Assert.IsTrue(cursor.IsPartTaskOpen("a"));

            // Unknown task entirely — also ignored.
            cursor.NotifyTaskCompleted("part", "ghost");
            Assert.IsFalse(cursor.IsComplete);
            Assert.AreEqual(1, log.Count);
        }

        [Test]
        public void NotifyTaskCompleted_AfterComplete_IsNoOp()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(Part("a")), log);
            cursor.Start();
            cursor.NotifyTaskCompleted("part", "a");
            Assert.IsTrue(cursor.IsComplete);

            // Extra notifies must not throw or re-fire events.
            Assert.DoesNotThrow(() => cursor.NotifyTaskCompleted("part", "a"));
            Assert.DoesNotThrow(() => cursor.NotifyTaskCompleted("part", "anything"));
            Assert.AreEqual(1, log.Count);
        }

        // ── event payload ────────────────────────────────────────────────

        // ── StepTasksComplete event (Phase I.e) ──────────────────────────

        [Test]
        public void StepTasksComplete_FiresOnceWhenCursorAdvancesPastLastSpan()
        {
            int completeCount = 0;
            var cursor = new TaskCursor(Step(Part("a"), Part("b")));
            cursor.StepTasksComplete += () => completeCount++;
            cursor.Start();

            Assert.AreEqual(0, completeCount);

            cursor.NotifyTaskCompleted("part", "a");
            Assert.AreEqual(0, completeCount, "still on span 2 of 2");

            cursor.NotifyTaskCompleted("part", "b");
            Assert.AreEqual(1, completeCount, "advanced past last span");

            // Extra notifies after completion must not re-fire.
            cursor.NotifyTaskCompleted("part", "a");
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void StepTasksComplete_DoesNotFireForEmptyTaskOrder()
        {
            int completeCount = 0;
            var cursor = new TaskCursor(Step());
            cursor.StepTasksComplete += () => completeCount++;
            cursor.Start();

            // Empty taskOrder → IsComplete from construction. Event must NOT
            // fire from Start() — subscribers that want to treat pre-complete
            // cursors specially check IsComplete explicitly.
            Assert.AreEqual(0, completeCount);
            Assert.IsTrue(cursor.IsComplete);
        }

        [Test]
        public void StepTasksComplete_FiresAfterUnorderedSetCloses()
        {
            int completeCount = 0;
            var cursor = new TaskCursor(Step(
                Part("a", set: "panel"),
                Part("b", set: "panel")));
            cursor.StepTasksComplete += () => completeCount++;
            cursor.Start();

            cursor.NotifyTaskCompleted("part", "b");
            Assert.AreEqual(0, completeCount);

            cursor.NotifyTaskCompleted("part", "a");
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void TaskSpanOpenedInfo_ReportsCorrectIndexAndTotal()
        {
            var log = new List<TaskSpanOpenedInfo>();
            var cursor = MakeCursor(Step(
                Part("a"),
                Part("b", set: "g"),
                Part("c", set: "g"),
                Tool("d")), log);
            cursor.Start();

            Assert.AreEqual(1, log.Count);
            Assert.AreEqual(0, log[0].SpanIndex);
            Assert.AreEqual(3, log[0].TotalSpans);
            Assert.IsNull(log[0].Label);

            cursor.NotifyTaskCompleted("part", "a");
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(1, log[1].SpanIndex);
            Assert.AreEqual("g", log[1].Label);
            Assert.AreEqual(2, log[1].Entries.Count);

            cursor.NotifyTaskCompleted("part", "c");
            cursor.NotifyTaskCompleted("part", "b");
            Assert.AreEqual(3, log.Count);
            Assert.AreEqual(2, log[2].SpanIndex);
            Assert.IsNull(log[2].Label);
        }
    }
}
