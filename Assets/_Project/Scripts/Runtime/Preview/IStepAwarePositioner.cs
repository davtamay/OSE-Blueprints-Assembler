using OSE.Content;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Implemented by PackagePartSpawner (OSE.UI) and registered in ServiceRegistry.
    /// Allows SessionDriver (OSE.Runtime) to trigger step-aware part repositioning
    /// in edit mode without a direct assembly reference to OSE.UI.
    /// </summary>
    public interface IStepAwarePositioner
    {
        void ApplyStepAwarePositions(int targetSequenceIndex, MachinePackageDefinition pkg);
        void ClearGhosts();
    }
}
