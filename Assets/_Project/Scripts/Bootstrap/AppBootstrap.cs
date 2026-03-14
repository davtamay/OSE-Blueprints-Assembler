using OSE.App;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Bootstrap
{
    public class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private bool _verboseLogging = false;

        private void Awake()
        {
            OseLog.Verbose = _verboseLogging;
            OseLog.Info("[Bootstrap] Initializing OSE application.");

            RegisterCoreServices();
            InitializePlatform();

            OseLog.Info("[Bootstrap] Initialization complete.");
        }

        private void RegisterCoreServices()
        {
            var sessionController = new MachineSessionController();
            ServiceRegistry.Register<MachineSessionController>(sessionController);

            var partController = new PartRuntimeController();
            ServiceRegistry.Register<PartRuntimeController>(partController);

            var placementValidator = new PlacementValidator();
            ServiceRegistry.Register<IPlacementValidator>(placementValidator);

            OseLog.Info("[Bootstrap] Core services registered.");
        }

        private void InitializePlatform()
        {
            // Platform capability profiling will be implemented in Phase 3+.
            // For now we default to a standard desktop profile.
            OseLog.Info("[Bootstrap] Platform initialized with default capability profile.");
        }
    }
}
