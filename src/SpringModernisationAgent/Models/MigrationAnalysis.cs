namespace SpringModernisationAgent.Models;

public sealed record MigrationAnalysis(
    string RepositoryName,
    string Summary,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> RecommendedActions);