namespace SpringModernisationAgent.Models;

public sealed record MigrationWave(
    int Number,
    IReadOnlyList<string> Repositories);

public sealed record MigrationWavePlan(
    IReadOnlyList<MigrationWave> Waves);