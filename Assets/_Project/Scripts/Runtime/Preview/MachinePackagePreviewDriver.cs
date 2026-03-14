using System.Collections;
using System.Text;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MachinePackagePreviewDriver : MonoBehaviour
    {
        [Header("Package Preview")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField] private string _packageId = "tutorial_build";
        [SerializeField, Min(1)] private int _previewStepSequenceIndex = 1;
        [SerializeField] private bool _advanceOnPlay = true;
        [SerializeField, Min(1)] private int _playModeStepSequenceIndex = 2;
        [SerializeField, Min(0f)] private float _playModeAdvanceDelay = 2.5f;

        [Header("Debug Status")]
        [SerializeField, TextArea(2, 6)] private string _statusMessage = "Awaiting package load.";
        [SerializeField] private string _loadedMachineName = string.Empty;

        private readonly MachinePackageLoader _loader = new MachinePackageLoader();
        private MachinePackageDefinition _package;
        private bool _previewApplied;
        private bool _playModeAdvanceStarted;
        private int _loadVersion;

        private void OnEnable()
        {
            _previewApplied = false;
            _playModeAdvanceStarted = false;
            RequestRefresh();
        }

        private void Update()
        {
            if (!Application.isPlaying && !_previewInEditMode)
            {
                return;
            }

            if (!_previewApplied)
            {
                TryApplyPreview(_previewStepSequenceIndex);
            }

            StartPlayModeAdvanceIfNeeded();
        }

        private void OnValidate()
        {
            _previewStepSequenceIndex = Mathf.Max(1, _previewStepSequenceIndex);
            _playModeStepSequenceIndex = Mathf.Max(1, _playModeStepSequenceIndex);
            _playModeAdvanceDelay = Mathf.Max(0f, _playModeAdvanceDelay);

            if (!isActiveAndEnabled)
            {
                return;
            }

            RequestRefresh();
        }

        private void RequestRefresh()
        {
            _previewApplied = false;
            _playModeAdvanceStarted = false;
            _ = ReloadPackageAsync(++_loadVersion);
        }

        private async Task ReloadPackageAsync(int loadVersion)
        {
            string packageId = string.IsNullOrWhiteSpace(_packageId) ? string.Empty : _packageId.Trim();
            _statusMessage = string.IsNullOrWhiteSpace(packageId)
                ? "Package id is required."
                : $"Loading package '{packageId}'...";

            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId);

            if (loadVersion != _loadVersion || !this)
            {
                return;
            }

            _package = result.Package;
            _previewApplied = false;
            _playModeAdvanceStarted = false;

            if (result.IsSuccess)
            {
                _loadedMachineName = _package.GetDisplayMachineName();
                _statusMessage = result.Validation.HasWarnings
                    ? $"Loaded '{_loadedMachineName}' with warnings."
                    : $"Loaded '{_loadedMachineName}'.";
            }
            else
            {
                _loadedMachineName = string.Empty;
                _statusMessage = BuildFailureMessage(result);
            }

            TryApplyPreview(_previewStepSequenceIndex);
        }

        private bool TryApplyPreview(int stepSequenceIndex)
        {
            if (!ShouldApplyPreviewNow())
            {
                return false;
            }

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter presentationAdapter))
            {
                return false;
            }

            if (_package == null)
            {
                ApplyFailurePreview(
                    presentationAdapter,
                    "Package Preview Unavailable",
                    _statusMessage);
                _previewApplied = true;
                return true;
            }

            StepDefinition[] orderedSteps = _package.GetOrderedSteps();
            if (orderedSteps.Length == 0)
            {
                ApplyFailurePreview(
                    presentationAdapter,
                    "Package Has No Steps",
                    $"{_package.GetDisplayMachineName()} loaded, but no steps were authored.");
                _previewApplied = true;
                return true;
            }

            StepDefinition selectedStep = ResolveStep(orderedSteps, stepSequenceIndex);
            PartDefinition selectedPart = ResolvePrimaryPart(selectedStep);

            presentationAdapter.ShowStepShell(
                selectedStep.sequenceIndex,
                orderedSteps.Length,
                selectedStep.GetDisplayName(),
                selectedStep.BuildInstructionBody());

            presentationAdapter.ShowPartInfoShell(
                selectedPart != null ? selectedPart.GetDisplayName() : "Referenced Part Missing",
                selectedPart != null ? selectedPart.function : $"No part metadata was found for step '{selectedStep.GetDisplayName()}'.",
                selectedPart != null ? selectedPart.material : "Material metadata missing.",
                ResolveToolSummary(selectedStep, selectedPart),
                ResolveSearchTerms(selectedPart));

            _previewApplied = true;
            return true;
        }

        private void ApplyFailurePreview(
            IPresentationAdapter presentationAdapter,
            string title,
            string instruction)
        {
            presentationAdapter.ShowStepShell(0, 0, title, instruction);
            presentationAdapter.ShowPartInfoShell(
                string.IsNullOrWhiteSpace(_packageId) ? "No Package Id" : _packageId.Trim(),
                instruction,
                _loadedMachineName,
                MachinePackageLoader.BuildMachineJsonPath(string.IsNullOrWhiteSpace(_packageId) ? string.Empty : _packageId.Trim()),
                "machine package preview");
        }

        private void StartPlayModeAdvanceIfNeeded()
        {
            if (!Application.isPlaying || !_advanceOnPlay || _playModeAdvanceStarted || !_previewApplied)
            {
                return;
            }

            _playModeAdvanceStarted = true;
            StartCoroutine(AdvancePreviewStepCoroutine());
        }

        private IEnumerator AdvancePreviewStepCoroutine()
        {
            yield return new WaitForSeconds(_playModeAdvanceDelay);
            _previewApplied = false;
            TryApplyPreview(_playModeStepSequenceIndex);
        }

        private bool ShouldApplyPreviewNow() =>
            Application.isPlaying || _previewInEditMode;

        private static StepDefinition ResolveStep(StepDefinition[] orderedSteps, int sequenceIndex)
        {
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                StepDefinition step = orderedSteps[i];
                if (step != null && step.sequenceIndex == sequenceIndex)
                {
                    return step;
                }
            }

            return orderedSteps[0];
        }

        private PartDefinition ResolvePrimaryPart(StepDefinition step)
        {
            string partId = ResolvePrimaryPartId(step);
            if (string.IsNullOrWhiteSpace(partId))
            {
                return null;
            }

            return _package.TryGetPart(partId, out PartDefinition part) ? part : null;
        }

        private static string ResolvePrimaryPartId(StepDefinition step)
        {
            if (step == null)
            {
                return string.Empty;
            }

            string requiredPartId = GetFirstValue(step.requiredPartIds);
            if (!string.IsNullOrWhiteSpace(requiredPartId))
            {
                return requiredPartId;
            }

            return GetFirstValue(step.optionalPartIds);
        }

        private string ResolveToolSummary(StepDefinition step, PartDefinition part)
        {
            StringBuilder builder = new StringBuilder();
            AppendTools(builder, step != null ? step.relevantToolIds : null);

            if (builder.Length == 0 && part != null)
            {
                AppendTools(builder, part.toolIds);
            }

            return builder.Length > 0
                ? builder.ToString()
                : "No tool metadata referenced for this preview step.";
        }

        private void AppendTools(StringBuilder builder, string[] toolIds)
        {
            if (toolIds == null)
            {
                return;
            }

            for (int i = 0; i < toolIds.Length; i++)
            {
                string toolId = toolIds[i];
                if (string.IsNullOrWhiteSpace(toolId) || !_package.TryGetTool(toolId, out ToolDefinition tool))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(tool.GetDisplayName());
            }
        }

        private static string ResolveSearchTerms(PartDefinition part)
        {
            if (part == null || part.searchTerms == null || part.searchTerms.Length == 0)
            {
                return "No search terms authored for this part.";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < part.searchTerms.Length; i++)
            {
                string searchTerm = part.searchTerms[i];
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(searchTerm.Trim());
            }

            return builder.Length > 0
                ? builder.ToString()
                : "No search terms authored for this part.";
        }

        private static string GetFirstValue(string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private static string BuildFailureMessage(MachinePackageLoadResult result)
        {
            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                builder.Append(result.ErrorMessage.Trim());
            }

            if (result.Validation != null && result.Validation.Issues.Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.Append(result.Validation.FormatSummary());
            }

            return builder.Length > 0
                ? builder.ToString()
                : "Machine package preview failed to load.";
        }
    }
}
