using OSE.Content.Validation;

namespace OSE.Content.Loading
{
    public readonly struct MachinePackageLoadResult
    {
        public MachinePackageLoadResult(
            string packageId,
            string sourcePath,
            MachinePackageDefinition package,
            MachinePackageValidationResult validation,
            string errorMessage)
        {
            PackageId = packageId ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            Package = package;
            Validation = validation ?? MachinePackageValidationResult.Valid;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string PackageId { get; }

        public string SourcePath { get; }

        public MachinePackageDefinition Package { get; }

        public MachinePackageValidationResult Validation { get; }

        public string ErrorMessage { get; }

        public bool IsSuccess =>
            Package != null &&
            !Validation.HasErrors &&
            string.IsNullOrWhiteSpace(ErrorMessage);
    }
}
