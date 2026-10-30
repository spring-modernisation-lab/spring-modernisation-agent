namespace SpringModernisationAgent.Models;

public enum CommitProvenanceStatus
{
    Matched,
    Mismatched,
    Missing
}

public sealed record CommitProvenance(
    CommitProvenanceStatus Status,
    string? AssessedCommitSha,
    string? BuiltCommitSha,
    string? Reason)
{
    public bool IsValid => Status == CommitProvenanceStatus.Matched;
}