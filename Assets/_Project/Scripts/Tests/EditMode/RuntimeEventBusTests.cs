using NUnit.Framework;
using OSE.Core;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class RuntimeEventBusTests
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

        [Test]
        public void Publish_Invokes_Subscriber()
        {
            TestEvent received = default;
            RuntimeEventBus.Subscribe<TestEvent>(e => received = e);

            RuntimeEventBus.Publish(new TestEvent("hello"));

            Assert.AreEqual("hello", received.Value);
        }

        [Test]
        public void Publish_Without_Subscribers_Does_Not_Throw()
        {
            Assert.DoesNotThrow(() => RuntimeEventBus.Publish(new TestEvent("orphan")));
        }

        [Test]
        public void Multiple_Subscribers_All_Receive_Event()
        {
            int callCount = 0;
            RuntimeEventBus.Subscribe<TestEvent>(_ => callCount++);
            RuntimeEventBus.Subscribe<TestEvent>(_ => callCount++);

            RuntimeEventBus.Publish(new TestEvent("multi"));

            Assert.AreEqual(2, callCount);
        }

        [Test]
        public void Unsubscribe_Prevents_Future_Invocations()
        {
            int callCount = 0;
            void Handler(TestEvent _) => callCount++;

            RuntimeEventBus.Subscribe<TestEvent>(Handler);
            RuntimeEventBus.Publish(new TestEvent("first"));
            Assert.AreEqual(1, callCount);

            RuntimeEventBus.Unsubscribe<TestEvent>(Handler);
            RuntimeEventBus.Publish(new TestEvent("second"));
            Assert.AreEqual(1, callCount);
        }

        [Test]
        public void Unsubscribe_Only_Removes_Specified_Listener()
        {
            int countA = 0;
            int countB = 0;
            void HandlerA(TestEvent _) => countA++;
            void HandlerB(TestEvent _) => countB++;

            RuntimeEventBus.Subscribe<TestEvent>(HandlerA);
            RuntimeEventBus.Subscribe<TestEvent>(HandlerB);

            RuntimeEventBus.Unsubscribe<TestEvent>(HandlerA);
            RuntimeEventBus.Publish(new TestEvent("after-unsub"));

            Assert.AreEqual(0, countA);
            Assert.AreEqual(1, countB);
        }

        [Test]
        public void Different_Event_Types_Are_Independent()
        {
            bool testEventFired = false;
            bool otherEventFired = false;

            RuntimeEventBus.Subscribe<TestEvent>(_ => testEventFired = true);
            RuntimeEventBus.Subscribe<OtherEvent>(_ => otherEventFired = true);

            RuntimeEventBus.Publish(new TestEvent("only-test"));

            Assert.IsTrue(testEventFired);
            Assert.IsFalse(otherEventFired);
        }

        [Test]
        public void Clear_Removes_All_Listeners()
        {
            int callCount = 0;
            RuntimeEventBus.Subscribe<TestEvent>(_ => callCount++);
            RuntimeEventBus.Subscribe<OtherEvent>(_ => callCount++);

            RuntimeEventBus.Clear();

            RuntimeEventBus.Publish(new TestEvent("cleared"));
            RuntimeEventBus.Publish(new OtherEvent(42));

            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Event_Data_Is_Passed_By_Value()
        {
            TestEvent received = default;
            RuntimeEventBus.Subscribe<TestEvent>(e => received = e);

            var original = new TestEvent("original");
            RuntimeEventBus.Publish(original);

            Assert.AreEqual(original.Value, received.Value);
        }

        // ── Test event structs ──

        private readonly struct TestEvent
        {
            public readonly string Value;
            public TestEvent(string value) => Value = value;
        }

        private readonly struct OtherEvent
        {
            public readonly int Code;
            public OtherEvent(int code) => Code = code;
        }
    }
}
