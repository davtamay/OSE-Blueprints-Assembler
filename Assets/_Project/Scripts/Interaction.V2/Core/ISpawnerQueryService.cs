using System.Collections.Generic;
using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Provides read-only access to spawned parts, package data, and target
    /// placement information. Lives in OSE.Interaction so the orchestrator
    /// can consume it without a circular dependency on OSE.UI.
    /// Implemented by PackagePartSpawner (OSE.UI), registered via ServiceRegistry.
    /// </summary>
    public interface ISpawnerQueryService
    {
        IReadOnlyList<GameObject> SpawnedParts { get; }
        MachinePackageDefinition CurrentPackage { get; }
        TargetPreviewPlacement FindTargetPlacement(string targetId);
        Transform PreviewRoot { get; }
    }
}
