namespace SpringModernisationAgent.Models;

public enum TransformationStatus
{
    NotRun,
    Succeeded,
    Failed
}

public sealed record TransformationResult(
    string RepositoryName,
    string SourceCommitSha,
    TransformationStatus Status,
    IReadOnlyList<string> AppliedRecipes,
    IReadOnlyList<string> ChangedFiles,
    string? StandardOutput,
    string? StandardError);