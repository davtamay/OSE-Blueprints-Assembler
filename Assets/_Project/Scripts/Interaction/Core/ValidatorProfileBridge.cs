using OSE.Content.Validation;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Wires <see cref="ToolProfileRegistry.Has"/> into <see cref="MachinePackageValidator"/>
    /// so the validator can check profile strings dynamically without a direct assembly reference.
    /// </summary>
    internal static class ValidatorProfileBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            MachinePackageValidator.IsProfileRegistered = ToolProfileRegistry.Has;
        }
    }
}
