namespace SpringModernisationAgent.Models;

public sealed record MigrationAnalysisResult(
    MigrationStrategy Strategy,
    IReadOnlyList<MigrationAnalysis> RepositoryAnalyses);