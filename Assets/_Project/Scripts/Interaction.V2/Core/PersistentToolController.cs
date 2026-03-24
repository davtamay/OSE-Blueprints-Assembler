using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Manages persistent tool lifecycle (clamps, fixtures, vises) that survive
    /// step transitions. Extracted from InteractionOrchestrator to keep that class
    /// focused on input routing.
    ///
    /// Persistent tools are placed at tool-action targets and remain on the
    /// workpiece until explicitly removed by content (removePersistentToolIds)
    /// or automatically when no longer needed.
    /// </summary>
    public sealed class PersistentToolController
    {
        private readonly IPersistentToolManager _manager;
        private readonly IToolGhostProvider _ghostProvider;

        public PersistentToolController(IPersistentToolManager manager, IToolGhostProvider ghostProvider)
        {
            _manager = manager;
            _ghostProvider = ghostProvider;
        }

        /// <summary>
        /// Called at the end of a preview action phase (before return animation).
        /// Converts the cursor ghost in-place so it stays at the target.
        /// Returns true if the ghost was converted (caller should skip return animation).
        /// </summary>
        public bool TryConvertGhostAtAction(string targetId, Vector3 actionPos, Quaternion actionRot)
        {
            string toolId = _ghostProvider.GetActiveToolId();
            if (string.IsNullOrEmpty(toolId)) return false;

            if (!ToolActionProfiles.IsToolPersistent(toolId)) return false;
            if (_manager.HasPersistentToolAt(targetId)) return false;

            var result = _manager.ConvertGhostToPersistent(toolId, targetId, actionPos, actionRot);
            return result != null;
        }

        /// <summary>
        /// Non-preview path: converts the cursor ghost into a persistent tool
        /// for click-to-complete actions.
        /// </summary>
        public void TryConvertGhostOnComplete(string targetId, Vector3 worldPos)
        {
            string toolId = _ghostProvider.GetActiveToolId();
            if (string.IsNullOrEmpty(toolId)) return;

            if (!ToolActionProfiles.IsToolPersistent(toolId)) return;
            if (_manager.HasPersistentToolAt(targetId)) return;

            GameObject ghost = _ghostProvider.GetToolGhost();
            Quaternion rotation = ghost != null ? ghost.transform.rotation : Quaternion.identity;

            _manager.ConvertGhostToPersistent(toolId, targetId, worldPos, rotation);
        }

        /// <summary>
        /// Removes persistent tools when the new step's content says to.
        /// Call on StepActivated.
        /// </summary>
        public void CleanUpForStep(string stepId)
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            var stepCtrl = session?.AssemblyController?.StepController;
            StepDefinition step = stepCtrl?.CurrentStepDefinition;
            if (step == null) return;

            OseLog.Info($"[PersistentTool] CleanUp '{stepId}' — remove={FormatIds(step.removePersistentToolIds)}, relevant={FormatIds(step.relevantToolIds)}");

            // Explicit removal list from content
            if (step.removePersistentToolIds != null && step.removePersistentToolIds.Length > 0)
            {
                foreach (var toolId in step.removePersistentToolIds)
                {
                    int removed = _manager.RemoveAllPersistentTools(toolId);
                    OseLog.Info($"[PersistentTool] RemoveAll('{toolId}') → {removed} for step '{stepId}'.");
                }
            }

            // Auto-remove when step has no tool actions (clamp phase is over)
            if (step.requiredToolActions == null || step.requiredToolActions.Length == 0)
            {
                int removed = _manager.RemoveAllPersistentTools(null);
                if (removed > 0)
                    OseLog.Info($"[PersistentTool] Auto-removed {removed} — step '{stepId}' has no requiredToolActions.");
            }
        }

        private static string FormatIds(string[] ids)
        {
            if (ids == null) return "null";
            if (ids.Length == 0) return "[]";
            return "[" + string.Join(", ", ids) + "]";
        }
    }
}
