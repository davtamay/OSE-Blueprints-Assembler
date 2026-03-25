using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Interaction
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

            if (!IsToolPersistent(toolId)) return false;
            if (_manager.HasPersistentToolAt(targetId)) return false;

            var result = _manager.ConvertGhostToPersistent(toolId, targetId, actionPos, actionRot);
            return result != null;
        }

        /// <summary>
        /// Non-preview path: converts the cursor ghost into a persistent tool
        /// for click-to-complete actions.
        /// </summary>
        public void TryConvertGhostOnComplete(string targetId, Vector3 worldPos, Quaternion? rotationOverride = null)
        {
            string toolId = _ghostProvider.GetActiveToolId();
            if (string.IsNullOrEmpty(toolId)) return;

            if (!IsToolPersistent(toolId)) return;
            if (_manager.HasPersistentToolAt(targetId)) return;

            GameObject ghost = _ghostProvider.GetToolGhost();
            Quaternion rotation = rotationOverride ?? (ghost != null ? ghost.transform.rotation : Quaternion.identity);

            _manager.ConvertGhostToPersistent(toolId, targetId, worldPos, rotation);
        }

        /// <summary>
        /// Removes a persistent tool at the given target. Used to roll back
        /// speculative creation if the tool action subsequently fails.
        /// </summary>
        public void RemoveAt(string targetId)
        {
            _manager.RemovePersistentTool(targetId);
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

            // Explicit removal list from content (highest priority)
            if (step.removePersistentToolIds != null && step.removePersistentToolIds.Length > 0)
            {
                foreach (var toolId in step.removePersistentToolIds)
                {
                    int removed = _manager.RemoveAllPersistentTools(toolId);
                    OseLog.Info($"[PersistentTool] RemoveAll('{toolId}') → {removed} for step '{stepId}'.");
                }
            }

            // Auto-remove persistent tools that are no longer relevant.
            // Any persistent tool whose toolId is NOT mentioned by the new step's
            // relevantToolIds or requiredToolActions is removed. Steps that mention
            // no tools at all remove all persistent tools by default. This keeps
            // clamps visible while they matter (e.g. during tack welding) and
            // cleans them up automatically when the content moves on.
            AutoRemoveIrrelevantTools(step, stepId);
        }

        private void AutoRemoveIrrelevantTools(StepDefinition step, string stepId)
        {
            string[] placed = _manager.GetPlacedPersistentToolIds();
            if (placed.Length == 0)
                return;

            // Build a lookup of tool IDs the step considers relevant.
            // If the step mentions no tools at all, every persistent tool is stale
            // and should be removed.
            var relevantSet = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

            if (step.relevantToolIds != null)
            {
                for (int i = 0; i < step.relevantToolIds.Length; i++)
                {
                    string toolId = step.relevantToolIds[i];
                    if (!string.IsNullOrWhiteSpace(toolId))
                        relevantSet.Add(toolId);
                }
            }

            // Also keep tools listed in requiredToolActions — they are actively used
            if (step.requiredToolActions != null)
            {
                foreach (var action in step.requiredToolActions)
                {
                    if (!string.IsNullOrEmpty(action.toolId))
                        relevantSet.Add(action.toolId);
                }
            }

            foreach (string toolId in placed)
            {
                if (relevantSet.Contains(toolId))
                    continue;

                // Tools marked persistent in the definition stay until explicitly
                // removed via removePersistentToolIds — no need to list them in
                // every subsequent step's relevantToolIds.
                if (IsToolPersistent(toolId))
                {
                    OseLog.Info($"[PersistentTool] Keeping '{toolId}' (persistent=true, removal requires removePersistentToolIds).");
                    continue;
                }

                int removed = _manager.RemoveAllPersistentTools(toolId);
                if (removed > 0)
                    OseLog.Info($"[PersistentTool] Auto-removed {removed} '{toolId}' (not in relevantToolIds for '{stepId}').");
            }
        }

        /// <summary>
        /// Data-driven persistence check: reads ToolDefinition.persistent from the
        /// loaded package first, falls back to ToolActionProfiles substring matching.
        /// </summary>
        private static bool IsToolPersistent(string toolId)
        {
            if (ServiceRegistry.TryGet<MachineSessionController>(out var session)
                && session?.Package != null
                && session.Package.TryGetTool(toolId, out var toolDef))
            {
                return toolDef.persistent;
            }

            return ToolActionProfiles.IsToolPersistent(toolId);
        }

        private static string FormatIds(string[] ids)
        {
            if (ids == null) return "null";
            if (ids.Length == 0) return "[]";
            return "[" + string.Join(", ", ids) + "]";
        }
    }
}
