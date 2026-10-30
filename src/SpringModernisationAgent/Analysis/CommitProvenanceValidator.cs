using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class CommitProvenanceValidator
{
    public CommitProvenance Validate(
        string? assessedCommitSha,
        string? builtCommitSha)
    {
        if (string.IsNullOrWhiteSpace(assessedCommitSha))
        {
            return new CommitProvenance(
                Status: CommitProvenanceStatus.Missing,
                AssessedCommitSha: assessedCommitSha,
                BuiltCommitSha: builtCommitSha,
                Reason: "The assessed repository commit SHA is missing.");
        }

        if (string.IsNullOrWhiteSpace(builtCommitSha))
        {
            return new CommitProvenance(
                Status: CommitProvenanceStatus.Missing,
                AssessedCommitSha: assessedCommitSha,
                BuiltCommitSha: builtCommitSha,
                Reason: "The baseline build commit SHA is missing.");
        }

        if (!string.Equals(
                assessedCommitSha,
                builtCommitSha,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CommitProvenance(
                Status: CommitProvenanceStatus.Mismatched,
                AssessedCommitSha: assessedCommitSha,
                BuiltCommitSha: builtCommitSha,
                Reason:
                    "The baseline build was run against a different commit from the assessed repository revision.");
        }

        return new CommitProvenance(
            Status: CommitProvenanceStatus.Matched,
            AssessedCommitSha: assessedCommitSha,
            BuiltCommitSha: builtCommitSha,
            Reason: null);
    }
}