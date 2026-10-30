namespace SpringModernisationAgent.Models;

public sealed record TransformationExecutionResult(
    TransformationResult Transformation,
    TransformationValidationResult Validation)
{
    public bool Succeeded =>
        Transformation.Status == TransformationStatus.Succeeded
        && Validation.Status == TransformationValidationStatus.Passed;
}