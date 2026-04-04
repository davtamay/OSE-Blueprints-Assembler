using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using System;

namespace OSE.Runtime
{
    /// <summary>
    /// Validates that all resources referenced by a step definition exist in the
    /// package before the step fully activates. Logs diagnostics and publishes a
    /// <see cref="StepReadinessChecked"/> event. Does NOT block step activation.
    /// </summary>
    public sealed class StepPreflightValidator
    {
        private readonly List<string> _issues = new List<string>();

        /// <summary>
        /// Runs all pre-flight checks for the given step against the loaded package.
        /// Returns true when the step is ready (no errors found).
        /// Issues are logged and published via <see cref="RuntimeEventBus"/>.
        /// </summary>
        public bool Validate(MachinePackageDefinition package, StepDefinition step)
        {
            _issues.Clear();

            if (package == null)
            {
                _issues.Add("Package is null.");
                Publish(step?.id);
                return false;
            }

            if (step == null)
            {
                _issues.Add("Step definition is null.");
                Publish(null);
                return false;
            }

            ValidatePartReferences(package, step);
            ValidateTargetReferences(package, step);
            ValidateToolReferences(package, step);
            ValidateToolActions(package, step);

            bool isReady = _issues.Count == 0;

            if (!isReady)
            {
                for (int i = 0; i < _issues.Count; i++)
                    OseLog.Warn($"[Preflight] Step '{step.id}': {_issues[i]}");
            }
            else
            {
                OseLog.VerboseInfo($"[Preflight] Step '{step.id}' passed all pre-flight checks.");
            }

            Publish(step.id);
            return isReady;
        }

        private void ValidatePartReferences(MachinePackageDefinition package, StepDefinition step)
        {
            CheckPartArray(package, step.requiredPartIds, step.id, "requiredPartIds");
            CheckPartArray(package, step.optionalPartIds, step.id, "optionalPartIds");
        }

        private void CheckPartArray(MachinePackageDefinition package, string[] partIds, string stepId, string arrayName)
        {
            if (partIds == null) return;
            for (int i = 0; i < partIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(partIds[i])) continue;
                if (!package.TryGetPart(partIds[i], out _))
                    _issues.Add($"{arrayName}[{i}] references unknown part '{partIds[i]}'.");
            }
        }

        private void ValidateTargetReferences(MachinePackageDefinition package, StepDefinition step)
        {
            if (step.targetIds == null) return;
            for (int i = 0; i < step.targetIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(step.targetIds[i])) continue;
                if (!package.TryGetTarget(step.targetIds[i], out _))
                    _issues.Add($"targetIds[{i}] references unknown target '{step.targetIds[i]}'.");
            }
        }

        private void ValidateToolReferences(MachinePackageDefinition package, StepDefinition step)
        {
            if (step.relevantToolIds == null) return;
            for (int i = 0; i < step.relevantToolIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(step.relevantToolIds[i])) continue;
                if (!package.TryGetTool(step.relevantToolIds[i], out _))
                    _issues.Add($"relevantToolIds[{i}] references unknown tool '{step.relevantToolIds[i]}'.");
            }
        }

        private void ValidateToolActions(MachinePackageDefinition package, StepDefinition step)
        {
            if (step.requiredToolActions == null) return;
            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                var action = step.requiredToolActions[i];
                if (action == null)
                {
                    _issues.Add($"requiredToolActions[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(action.toolId))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') has no toolId.");
                else if (!package.TryGetTool(action.toolId, out _))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') references unknown tool '{action.toolId}'.");

                if (!string.IsNullOrWhiteSpace(action.targetId) && !package.TryGetTarget(action.targetId, out _))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') references unknown target '{action.targetId}'.");

                if (action.requiredCount < 1)
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') has requiredCount={action.requiredCount} (must be >= 1).");

                if (!IsValidActionType(action.actionType))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') has unrecognized actionType '{action.actionType}'.");

                // Cross-reference: tool action target should be in step's targetIds
                if (!string.IsNullOrWhiteSpace(action.targetId) && !ArrayContains(step.targetIds, action.targetId))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') targetId '{action.targetId}' is not in step's targetIds array.");

                // Cross-reference: tool action toolId should be in step's relevantToolIds
                if (!string.IsNullOrWhiteSpace(action.toolId) && !ArrayContains(step.relevantToolIds, action.toolId))
                    _issues.Add($"requiredToolActions[{i}] ('{action.id}') toolId '{action.toolId}' is not in step's relevantToolIds array.");
            }
        }

        private void Publish(string stepId)
        {
            RuntimeEventBus.Publish(new StepReadinessChecked(
                stepId ?? string.Empty,
                isReady: _issues.Count == 0,
                issueCount: _issues.Count,
                firstIssue: _issues.Count > 0 ? _issues[0] : null));
        }

        private static bool IsValidActionType(string actionType)
        {
            if (string.IsNullOrWhiteSpace(actionType)) return false;
            return string.Equals(actionType, "measure", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "tighten", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "strike", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "weld_pass", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionType, "grind_pass", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ArrayContains(string[] array, string value)
        {
            if (array == null) return false;
            for (int i = 0; i < array.Length; i++)
            {
                if (string.Equals(array[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
