using System.Collections;
using NUnit.Framework;
using OSE.App;
using OSE.Core;
using OSE.Input;
using UnityEngine;
using UnityEngine.TestTools;

namespace OSE.Tests.PlayMode
{
    [TestFixture]
    public class InputActionRouterTests
    {
        private GameObject _routerGo;
        private InputActionRouter _router;

        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.Clear();
            RuntimeEventBus.Clear();
            _routerGo = new GameObject("InputActionRouter");
            _router = _routerGo.AddComponent<InputActionRouter>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_routerGo != null)
                Object.DestroyImmediate(_routerGo);
            RuntimeEventBus.Clear();
            ServiceRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator Self_Registers_As_Both_Interface_And_Concrete()
        {
            yield return null;

            Assert.IsTrue(ServiceRegistry.IsRegistered<IInputRouter>());
            Assert.IsTrue(ServiceRegistry.IsRegistered<InputActionRouter>());
            Assert.AreSame(_router, ServiceRegistry.Get<IInputRouter>());
            Assert.AreSame(_router, ServiceRegistry.Get<InputActionRouter>());
        }

        [UnityTest]
        public IEnumerator Default_Context_Is_None()
        {
            yield return null;

            Assert.AreEqual(InputContext.None, _router.CurrentContext);
        }

        [UnityTest]
        public IEnumerator SetContext_Changes_CurrentContext()
        {
            yield return null;

            _router.SetContext(InputContext.StepInteraction);
            Assert.AreEqual(InputContext.StepInteraction, _router.CurrentContext);
        }

        [UnityTest]
        public IEnumerator SetContext_Same_Value_Is_Idempotent()
        {
            yield return null;

            _router.SetContext(InputContext.StepInteraction);
            _router.SetContext(InputContext.StepInteraction);
            Assert.AreEqual(InputContext.StepInteraction, _router.CurrentContext);
        }

        [UnityTest]
        public IEnumerator InjectAction_Dispatches_When_Context_Set()
        {
            yield return null;

            _router.SetContext(InputContext.StepInteraction);

            CanonicalAction received = default;
            bool fired = false;
            RuntimeEventBus.Subscribe<CanonicalActionDispatched>(evt => { received = evt.Action; fired = true; });

            _router.InjectAction(CanonicalAction.Select);

            Assert.IsTrue(fired);
            Assert.AreEqual(CanonicalAction.Select, received);
        }

        [UnityTest]
        public IEnumerator InjectAction_Blocked_When_Context_Is_None()
        {
            yield return null;

            bool fired = false;
            RuntimeEventBus.Subscribe<CanonicalActionDispatched>(_ => fired = true);

            _router.InjectAction(CanonicalAction.Select);

            Assert.IsFalse(fired, "Actions should be blocked when context is None");
        }

        [UnityTest]
        public IEnumerator Destroy_Unregisters_From_ServiceRegistry()
        {
            yield return null;

            Assert.IsTrue(ServiceRegistry.IsRegistered<IInputRouter>());
            Assert.IsTrue(ServiceRegistry.IsRegistered<InputActionRouter>());

            Object.DestroyImmediate(_routerGo);
            _routerGo = null;

            Assert.IsFalse(ServiceRegistry.IsRegistered<IInputRouter>());
            Assert.IsFalse(ServiceRegistry.IsRegistered<InputActionRouter>());
        }

        [UnityTest]
        public IEnumerator IInputRouter_Interface_Exposes_InjectAction()
        {
            yield return null;

            IInputRouter iRouter = ServiceRegistry.Get<IInputRouter>();
            iRouter.SetContext(InputContext.SessionActive);

            CanonicalAction received = default;
            RuntimeEventBus.Subscribe<CanonicalActionDispatched>(evt => received = evt.Action);

            iRouter.InjectAction(CanonicalAction.Confirm);

            Assert.AreEqual(CanonicalAction.Confirm, received);
        }
    }
}
