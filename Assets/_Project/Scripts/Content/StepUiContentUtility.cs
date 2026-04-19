using System;
using System.Text;
using OSE.Core;

namespace OSE.Content
{
    public static class StepUiContentUtility
    {
        public readonly struct StepShellContent
        {
            public StepShellContent(string title, string instruction, bool showConfirmButton, bool showHintButton, ConfirmGate confirmGate)
            {
                Title = title;
                Instruction = instruction;
                ShowConfirmButton = showConfirmButton;
                ShowHintButton = showHintButton;
                ConfirmGate = confirmGate;
                HasContent = true;
            }

            public string Title { get; }
            public string Instruction { get; }
            public bool ShowConfirmButton { get; }
            public bool ShowHintButton { get; }
            public ConfirmGate ConfirmGate { get; }
            public bool HasContent { get; }
        }

        public readonly struct PartInfoShellContent
        {
            public PartInfoShellContent(string partName, string function, string material, string searchTerms)
            {
                PartName = partName;
                Function = function;
                Material = material;
                SearchTerms = searchTerms;
                HasContent = true;
            }

            public string PartName { get; }
            public string Function { get; }
            public string Material { get; }
            public string SearchTerms { get; }
            public bool HasContent { get; }
        }

        public static int ResolveDisplayStepNumber(StepDefinition[] orderedSteps, StepDefinition step)
        {
            if (orderedSteps == null || step == null || string.IsNullOrWhiteSpace(step.id))
                return 0;

            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null
                    && string.Equals(orderedSteps[i].id, step.id, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        public static ConfirmGate ResolveConfirmGate(StepDefinition step)
        {
            string[] tags = step.eventTags;
            if (tags != null)
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (string.Equals(tags[i], "select", StringComparison.OrdinalIgnoreCase))
                        return ConfirmGate.SelectPart;
                    if (string.Equals(tags[i], "hint", StringComparison.OrdinalIgnoreCase))
                        return ConfirmGate.RequestHint;
                }
            }

            if (step.IsToolAction)
                return ConfirmGate.EquipTool;

            if (HasAnyNonEmpty(step.GetEffectiveRequiredPartIds()))
                return ConfirmGate.SelectPart;

            // Confirm steps with inspection targets require the trainee to frame each
            // location with the camera before the button unlocks.
            if (step.IsConfirmation && HasAnyNonEmpty(step.targetIds))
                return ConfirmGate.ObserveTargets;

            return ConfirmGate.None;
        }

        public static bool HasAnyNonEmpty(string[] values)
        {
            if (values == null || values.Length == 0)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return true;
            }

            return false;
        }

        public static string GetFirstNonEmpty(string[] values)
        {
            if (values == null)
                return null;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }

            return null;
        }

        public static string JoinStrings(string[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                    continue;

                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(values[i].Trim());
            }

            return sb.ToString();
        }

        public static PartInfoShellContent BuildStepPartInfoShellContent(
            MachinePackageDefinition package,
            StepDefinition step,
            bool includeFallbackWhenNoRequiredPart)
        {
            if (package == null || step == null)
                return default;

            string partId = GetFirstNonEmpty(step.GetEffectiveRequiredPartIds());
            if (!string.IsNullOrEmpty(partId) && package.TryGetPart(partId, out PartDefinition part))
            {
                return new PartInfoShellContent(
                    part.GetDisplayName(),
                    part.function ?? string.Empty,
                    part.material ?? string.Empty,
                    JoinStrings(part.searchTerms));
            }

            if (!includeFallbackWhenNoRequiredPart)
                return default;

            return new PartInfoShellContent(
                "No part referenced",
                step.ResolvedInstructionText ?? string.Empty,
                string.Empty,
                string.Empty);
        }

        public static StepShellContent BuildStepShellContent(StepDefinition step)
        {
            if (step == null)
                return default;

            bool showConfirm = step.IsConfirmation;
            ConfirmGate gate = showConfirm ? ResolveConfirmGate(step) : ConfirmGate.None;

            return new StepShellContent(
                step.GetDisplayName(),
                step.BuildInstructionBody(),
                showConfirm,
                gate == ConfirmGate.RequestHint,
                gate);
        }
    }
}