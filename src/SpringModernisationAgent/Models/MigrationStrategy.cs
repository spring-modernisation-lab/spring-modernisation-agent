namespace SpringModernisationAgent.Models;

public sealed record MigrationStrategy(
    string Summary,
    IReadOnlyList<MigrationStrategyWave> Waves,
    IReadOnlyList<string> KeyRisks,
    IReadOnlyList<string> Recommendations);

public sealed record MigrationStrategyWave(
    int Number,
    IReadOnlyList<string> Repositories,
    string Objective,
    string Rationale);