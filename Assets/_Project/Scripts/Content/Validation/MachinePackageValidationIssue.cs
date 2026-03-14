namespace OSE.Content.Validation
{
    public enum MachinePackageIssueSeverity
    {
        Warning,
        Error
    }

    public readonly struct MachinePackageValidationIssue
    {
        public MachinePackageValidationIssue(
            MachinePackageIssueSeverity severity,
            string path,
            string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public MachinePackageIssueSeverity Severity { get; }

        public string Path { get; }

        public string Message { get; }
    }
}
