using System.Threading;
using System.Threading.Tasks;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Abstraction over <see cref="MachinePackageLoader"/> so that
    /// <see cref="OSE.Runtime.MachineSessionController"/> can be tested without
    /// hitting the file system or requiring StreamingAssets.
    /// </summary>
    public interface IMachinePackageLoader
    {
        Task<MachinePackageLoadResult> LoadFromStreamingAssetsAsync(
            string packageId,
            CancellationToken cancellationToken = default);
    }
}
