using System;
using NUnit.Framework;
using OSE.App;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class ServiceRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceRegistry.Clear();
        }

        [Test]
        public void Register_And_Get_Returns_Same_Instance()
        {
            var service = new DummyService();
            ServiceRegistry.Register<DummyService>(service);

            var resolved = ServiceRegistry.Get<DummyService>();

            Assert.AreSame(service, resolved);
        }

        [Test]
        public void Get_Unregistered_Throws_InvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => ServiceRegistry.Get<DummyService>());
        }

        [Test]
        public void Register_Null_Throws_ArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceRegistry.Register<DummyService>(null));
        }

        [Test]
        public void TryGet_Returns_True_When_Registered()
        {
            var service = new DummyService();
            ServiceRegistry.Register<DummyService>(service);

            bool found = ServiceRegistry.TryGet<DummyService>(out var resolved);

            Assert.IsTrue(found);
            Assert.AreSame(service, resolved);
        }

        [Test]
        public void TryGet_Returns_False_When_Not_Registered()
        {
            bool found = ServiceRegistry.TryGet<DummyService>(out var resolved);

            Assert.IsFalse(found);
            Assert.IsNull(resolved);
        }

        [Test]
        public void IsRegistered_Returns_Correct_State()
        {
            Assert.IsFalse(ServiceRegistry.IsRegistered<DummyService>());

            ServiceRegistry.Register<DummyService>(new DummyService());
            Assert.IsTrue(ServiceRegistry.IsRegistered<DummyService>());
        }

        [Test]
        public void Unregister_Removes_Service()
        {
            ServiceRegistry.Register<DummyService>(new DummyService());
            bool removed = ServiceRegistry.Unregister<DummyService>();

            Assert.IsTrue(removed);
            Assert.IsFalse(ServiceRegistry.IsRegistered<DummyService>());
        }

        [Test]
        public void Unregister_Returns_False_When_Not_Registered()
        {
            bool removed = ServiceRegistry.Unregister<DummyService>();
            Assert.IsFalse(removed);
        }

        [Test]
        public void Register_Overwrites_Previous_Registration()
        {
            var first = new DummyService();
            var second = new DummyService();

            ServiceRegistry.Register<DummyService>(first);
            ServiceRegistry.Register<DummyService>(second);

            Assert.AreSame(second, ServiceRegistry.Get<DummyService>());
        }

        [Test]
        public void Clear_Removes_All_Services()
        {
            ServiceRegistry.Register<DummyService>(new DummyService());
            ServiceRegistry.Register<IAnotherService>(new AnotherServiceImpl());

            ServiceRegistry.Clear();

            Assert.IsFalse(ServiceRegistry.IsRegistered<DummyService>());
            Assert.IsFalse(ServiceRegistry.IsRegistered<IAnotherService>());
        }

        [Test]
        public void Interface_And_Concrete_Are_Independent_Registrations()
        {
            var impl = new AnotherServiceImpl();
            ServiceRegistry.Register<IAnotherService>(impl);

            Assert.IsTrue(ServiceRegistry.IsRegistered<IAnotherService>());
            Assert.IsFalse(ServiceRegistry.IsRegistered<AnotherServiceImpl>());
        }

        [Test]
        public void Count_Reflects_Registered_Services()
        {
            Assert.AreEqual(0, ServiceRegistry.Count);
            ServiceRegistry.Register<DummyService>(new DummyService());
            Assert.AreEqual(1, ServiceRegistry.Count);
        }

        [Test]
        public void GetDiagnosticSummary_Lists_Registered_Services()
        {
            ServiceRegistry.Register<DummyService>(new DummyService());
            string summary = ServiceRegistry.GetDiagnosticSummary();

            Assert.IsTrue(summary.Contains("DummyService"));
            Assert.IsTrue(summary.Contains("OK"));
        }

        [Test]
        public void GetDiagnosticSummary_Empty_When_No_Services()
        {
            string summary = ServiceRegistry.GetDiagnosticSummary();
            Assert.IsTrue(summary.Contains("No services registered"));
        }

        // ── Test helpers ──

        private class DummyService { }

        private interface IAnotherService { }

        private class AnotherServiceImpl : IAnotherService { }
    }
}
