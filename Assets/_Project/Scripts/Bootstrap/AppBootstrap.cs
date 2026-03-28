using OSE.App;
using OSE.Core;
using OSE.Persistence;
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
            ServiceRegistry.Register<IMachineSessionController>(sessionController);

            var partController = new PartRuntimeController();
            ServiceRegistry.Register<IPartRuntimeController>(partController);

            var toolController = new ToolRuntimeController();
            ServiceRegistry.Register<IToolRuntimeController>(toolController);

            var placementValidator = new PlacementValidator();
            ServiceRegistry.Register<IPlacementValidator>(placementValidator);

            // Primary build target is WebGL — PlayerPrefs maps to IndexedDB there (no 1 MB cap).
            // FilePersistenceService exists for future standalone/native builds.
            var persistence = new PlayerPrefsPersistenceService();
            ServiceRegistry.Register<IPersistenceService>(persistence);

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
