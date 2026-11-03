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
    // Third-party Maven dependencies as "group:artifact[:version] (scope)".
    IReadOnlyList<string> ExternalDependencies,
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
    // Bounded failure lines from ValidationDiagnosticsExtractor; raw build
    // logs (and workspace paths) are never sent to the model.
    IReadOnlyList<string> Diagnostics);

public sealed record MigrationAnalysisWaveInput(
    int Number,
    IReadOnlyList<string> Repositories);
