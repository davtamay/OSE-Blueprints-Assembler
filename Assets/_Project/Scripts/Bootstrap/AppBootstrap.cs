using OSE.Core;
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
            // Core services are registered here as concrete implementations
            // are created in later phases. Placeholders prevent null-ref during
            // early development.
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
