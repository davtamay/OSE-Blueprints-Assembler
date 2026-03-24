using System;
using OSE.Content;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Resolves the <see cref="ViewMode"/> for a step using the resolution order:
    /// 1. Explicit viewMode field on the step
    /// 2. Profile override (e.g. Use.Measure → PairEndpoints)
    /// 3. Family default
    /// </summary>
    public static class ViewModeResolver
    {
        public static ViewMode Resolve(StepDefinition step)
        {
            // 1. Explicit viewMode field
            if (!string.IsNullOrEmpty(step.viewMode))
            {
                if (TryParse(step.viewMode, out ViewMode explicit_))
                    return explicit_;
            }

            StepFamily family = step.ResolvedFamily;
            string profile = step.profile;

            // 2. Profile overrides
            if (!string.IsNullOrEmpty(profile))
            {
                switch (family)
                {
                    case StepFamily.Place:
                        if (profile.Equals(ToolActionProfiles.AxisFit, StringComparison.OrdinalIgnoreCase))
                            return ViewMode.WorkZone;
                        break;
                    case StepFamily.Use:
                        if (profile.Equals(ToolActionProfiles.Measure, StringComparison.OrdinalIgnoreCase))
                            return ViewMode.PairEndpoints;
                        if (profile.Equals(ToolActionProfiles.Weld, StringComparison.OrdinalIgnoreCase) ||
                            profile.Equals(ToolActionProfiles.Cut, StringComparison.OrdinalIgnoreCase))
                            return ViewMode.PathView;
                        break;
                }
            }

            // 3. Family default
            switch (family)
            {
                case StepFamily.Place:   return ViewMode.SourceAndTarget;
                case StepFamily.Use:     return ViewMode.WorkZone;
                case StepFamily.Connect: return ViewMode.PairEndpoints;
                case StepFamily.Confirm: return ViewMode.Overview;
                default:                 return ViewMode.SourceAndTarget;
            }
        }

        private static bool TryParse(string value, out ViewMode mode)
        {
            switch (value)
            {
                case "SourceAndTarget": mode = ViewMode.SourceAndTarget; return true;
                case "PairEndpoints":   mode = ViewMode.PairEndpoints;   return true;
                case "WorkZone":        mode = ViewMode.WorkZone;        return true;
                case "PathView":        mode = ViewMode.PathView;        return true;
                case "Overview":        mode = ViewMode.Overview;        return true;
                case "Inspect":         mode = ViewMode.Inspect;         return true;
                case "ToolFocus":       mode = ViewMode.ToolFocus;       return true;
                default:                mode = ViewMode.SourceAndTarget;  return false;
            }
        }
    }
}
