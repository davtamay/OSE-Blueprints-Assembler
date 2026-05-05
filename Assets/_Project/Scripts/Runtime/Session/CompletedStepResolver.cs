using System;
using OSE.Content;

namespace OSE.Runtime
{
    /// <summary>
    /// Authoritative source for "which steps are completed at this point in the session".
    ///
    /// Why this exists: <see cref="OSE.Core.MachineSessionState.CompletedStepCount"/>
    /// is a persisted field that has been observed inflated past the total step count
    /// (see memory: feedback_completedstepcount_is_corrupt_use_position; observed
    /// 540/305 vs ~305 total). It is unreliable for visual-state rebuilds.
    ///
    /// Every visual-state rebuild — including per-frame sync, async part-ready
    /// callbacks, and assembly-picker progress — must derive the completed set
    /// from the active step's position in the ordered step list, not from the
    /// stored count.
    /// </summary>
    public static class CompletedStepResolver
    {
        /// <summary>
        /// Returns every step ordered BEFORE <paramref name="activeStepId"/> in the
        /// package's global step order. Empty when the active id is unknown,
        /// missing, or first in the order.
        /// </summary>
        public static StepDefinition[] DeriveCompletedStepsBefore(
            MachinePackageDefinition package, string activeStepId)
        {
            if (package == null || string.IsNullOrWhiteSpace(activeStepId))
                return Array.Empty<StepDefinition>();

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0)
                return Array.Empty<StepDefinition>();

            int activeIdx = -1;
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].id, activeStepId, StringComparison.Ordinal))
                {
                    activeIdx = i;
                    break;
                }
            }
            if (activeIdx <= 0) return Array.Empty<StepDefinition>();

            StepDefinition[] result = new StepDefinition[activeIdx];
            Array.Copy(orderedSteps, result, activeIdx);
            return result;
        }

        /// <summary>
        /// Reliable count — equivalent to <c>DeriveCompletedStepsBefore(...).Length</c>
        /// without allocating the array. Use this for progress display ("Step X of Y")
        /// instead of <see cref="OSE.Core.MachineSessionState.CompletedStepCount"/>.
        /// </summary>
        public static int CountCompletedStepsBefore(
            MachinePackageDefinition package, string activeStepId)
        {
            if (package == null || string.IsNullOrWhiteSpace(activeStepId)) return 0;

            StepDefinition[] orderedSteps = package.GetOrderedSteps();
            if (orderedSteps == null || orderedSteps.Length == 0) return 0;

            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null &&
                    string.Equals(orderedSteps[i].id, activeStepId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return 0;
        }
    }
}
