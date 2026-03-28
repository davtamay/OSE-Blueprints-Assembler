using System;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime
{
    /// <summary>
    /// Service interface for tool availability and active tool selection.
    /// Allows call sites to depend on the abstraction rather than the
    /// concrete ToolRuntimeController implementation.
    /// </summary>
    public interface IToolRuntimeController
    {
        event Action StateChanged;

        string ActiveStepId { get; }
        string ActiveToolId { get; }
        bool HasPackage { get; }

        void Initialize(MachinePackageDefinition package);
        void Dispose();

        ToolDefinition[] GetAvailableTools();
        string[] GetRequiredToolIds();
        bool IsToolRequiredForActiveStep(string toolId);

        bool TryGetTool(string toolId, out ToolDefinition tool);
        bool TryGetActiveTool(out ToolDefinition tool);

        bool EquipTool(string toolId);
        void UnequipTool();

        ToolRuntimeController.ToolActionExecutionResult TryExecutePrimaryAction(string interactedTargetId = null);
        bool TryGetPrimaryActionSnapshot(out ToolRuntimeController.ToolActionSnapshot snapshot);
        bool TryGetActionSnapshots(out ToolRuntimeController.ToolActionSnapshot[] snapshots);
    }
}
