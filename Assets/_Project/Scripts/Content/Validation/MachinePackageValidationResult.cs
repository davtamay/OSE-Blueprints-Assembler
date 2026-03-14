using System.Text;

namespace OSE.Content.Validation
{
    public sealed class MachinePackageValidationResult
    {
        public static MachinePackageValidationResult Valid { get; } =
            new MachinePackageValidationResult(System.Array.Empty<MachinePackageValidationIssue>());

        public MachinePackageValidationResult(MachinePackageValidationIssue[] issues)
        {
            Issues = issues ?? System.Array.Empty<MachinePackageValidationIssue>();
        }

        public MachinePackageValidationIssue[] Issues { get; }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Length; i++)
                {
                    if (Issues[i].Severity == MachinePackageIssueSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool HasWarnings
        {
            get
            {
                for (int i = 0; i < Issues.Length; i++)
                {
                    if (Issues[i].Severity == MachinePackageIssueSeverity.Warning)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public string FormatSummary(int maxIssues = 5)
        {
            if (Issues.Length == 0)
            {
                return "No validation issues.";
            }

            int count = Issues.Length < maxIssues ? Issues.Length : maxIssues;
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < count; i++)
            {
                MachinePackageValidationIssue issue = Issues[i];
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(issue.Severity);
                builder.Append(": ");

                if (!string.IsNullOrWhiteSpace(issue.Path))
                {
                    builder.Append(issue.Path);
                    builder.Append(" - ");
                }

                builder.Append(issue.Message);
            }

            if (Issues.Length > count)
            {
                builder.AppendLine();
                builder.Append("... ");
                builder.Append(Issues.Length - count);
                builder.Append(" more issue(s).");
            }

            return builder.ToString();
        }
    }
}
