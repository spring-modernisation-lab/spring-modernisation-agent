namespace SpringModernisationAgent.AI;

public sealed record MigrationAnalysisInput(
    string Organisation,
    IReadOnlyList<MigrationAnalysisRepositoryInput> Repositories,
    IReadOnlyList<MigrationAnalysisWaveInput> Waves);

public sealed record MigrationAnalysisRepositoryInput(
    string RepositoryName,
    string CommitSha,
    string RepositoryKind,
    string? SpringBootVersion,
    string? JavaVersion,
    MigrationAnalysisTargetInput Target,
    string MigrationRouteId,
    IReadOnlyList<string> InternalDependencies,
    string ReadinessStatus,
    IReadOnlyList<string> ReadinessReasons,
    MigrationAnalysisTransformationInput? Transformation);

public sealed record MigrationAnalysisTargetInput(
    string SpringBootVersion,
    string JavaVersion);

public sealed record MigrationAnalysisTransformationInput(
    string Status,
    IReadOnlyList<string> AppliedRecipes,
    IReadOnlyList<string> ChangedFiles,
    string ValidationStatus,
    IReadOnlyList<MigrationAnalysisValidationCheckInput> ValidationChecks);

public sealed record MigrationAnalysisValidationCheckInput(
    string Check,
    bool Passed,
    string? StandardOutput,
    string? StandardError);

public sealed record MigrationAnalysisWaveInput(
    int Number,
    IReadOnlyList<string> Repositories);