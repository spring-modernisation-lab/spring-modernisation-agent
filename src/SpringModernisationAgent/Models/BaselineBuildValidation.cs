namespace SpringModernisationAgent.Models;

public enum BaselineBuildValidationStatus
{
    Satisfied,
    NotRequired,
    NotRun,
    Failed
}

public sealed record BaselineBuildValidation(
    BaselineBuildValidationStatus Status,
    string? Reason)
{
    public bool CanProceed =>
        Status is BaselineBuildValidationStatus.Satisfied
            or BaselineBuildValidationStatus.NotRequired;
}
