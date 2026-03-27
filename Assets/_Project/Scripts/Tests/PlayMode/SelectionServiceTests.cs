using System.Collections;
using NUnit.Framework;
using OSE.App;
using OSE.Interaction;
using UnityEngine;
using UnityEngine.TestTools;

namespace OSE.Tests.PlayMode
{
    [TestFixture]
    public class SelectionServiceTests
    {
        private GameObject _serviceGo;
        private SelectionService _service;

        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.Clear();
            _serviceGo = new GameObject("SelectionService");
            _service = _serviceGo.AddComponent<SelectionService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGo != null)
                Object.DestroyImmediate(_serviceGo);
            ServiceRegistry.Clear();
        }

        [UnityTest]
        public IEnumerator Self_Registers_In_ServiceRegistry()
        {
            yield return null; // Allow Awake to run

            Assert.IsTrue(ServiceRegistry.IsRegistered<SelectionService>());
            Assert.AreSame(_service, ServiceRegistry.Get<SelectionService>());
        }

        [UnityTest]
        public IEnumerator NotifySelected_Sets_CurrentSelection()
        {
            yield return null;

            var target = new GameObject("Target");
            _service.NotifySelected(target);

            Assert.AreSame(target, _service.CurrentSelection);

            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator NotifySelected_Fires_OnSelected_Event()
        {
            yield return null;

            GameObject received = null;
            _service.OnSelected += go => received = go;

            var target = new GameObject("Target");
            _service.NotifySelected(target);

            Assert.AreSame(target, received);

            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator NotifyInspected_Sets_CurrentInspection()
        {
            yield return null;

            var target = new GameObject("InspectTarget");
            _service.NotifyInspected(target);

            Assert.AreSame(target, _service.CurrentInspection);

            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator Deselect_Clears_Selection_And_Inspection()
        {
            yield return null;

            var target = new GameObject("Target");
            _service.NotifySelected(target);
            _service.NotifyInspected(target);

            _service.Deselect();

            Assert.IsNull(_service.CurrentSelection);
            Assert.IsNull(_service.CurrentInspection);

            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator Deselect_Fires_OnDeselected_With_Previous()
        {
            yield return null;

            var target = new GameObject("Target");
            _service.NotifySelected(target);

            GameObject deselectedGo = null;
            _service.OnDeselected += go => deselectedGo = go;

            _service.Deselect();

            Assert.AreSame(target, deselectedGo);

            Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator Deselect_When_Nothing_Selected_Is_Noop()
        {
            yield return null;

            bool eventFired = false;
            _service.OnDeselected += _ => eventFired = true;

            _service.Deselect();

            Assert.IsFalse(eventFired);
        }

        [UnityTest]
        public IEnumerator Destroy_Unregisters_From_ServiceRegistry()
        {
            yield return null;

            Assert.IsTrue(ServiceRegistry.IsRegistered<SelectionService>());

            Object.DestroyImmediate(_serviceGo);
            _serviceGo = null;

            Assert.IsFalse(ServiceRegistry.IsRegistered<SelectionService>());
        }
    }
}
