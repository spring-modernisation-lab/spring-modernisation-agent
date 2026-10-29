namespace SpringModernisationAgent.Config;

public enum MigrationRouteSelectionStatus
{
    Matched,
    NoMatch,
    Ambiguous
}

public sealed record MigrationRouteSelection(
    MigrationRouteSelectionStatus Status,
    SpringMigrationRoute? Route,
    string? Reason);
