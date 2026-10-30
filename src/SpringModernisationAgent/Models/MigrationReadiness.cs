namespace SpringModernisationAgent.Models;

public enum MigrationReadinessStatus
{
    Ready,
    Blocked
}

public sealed record MigrationReadiness(
    MigrationReadinessStatus Status,
    IReadOnlyList<string> Reasons)
{
    public bool CanProceed => Status == MigrationReadinessStatus.Ready;
}