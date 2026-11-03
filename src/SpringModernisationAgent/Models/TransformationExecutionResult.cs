namespace SpringModernisationAgent.Models;

public sealed record TransformationExecutionResult(
    TransformationResult Transformation,
    TransformationValidationResult Validation)
{
    public bool Succeeded =>
        Transformation.Status == TransformationStatus.Succeeded
        && Validation.Status == TransformationValidationStatus.Passed;

    /// <summary>
    /// Preserved patches, in stage order, plus <c>cumulative</c>. Empty for
    /// results written before patches were recorded, when OpenRewrite failed,
    /// or when recording failed (see <see cref="PatchRecordingError"/>).
    /// </summary>
    public IReadOnlyList<PatchArtifact> Patches { get; init; } = [];

    /// <summary>
    /// Why the patches could not be recorded, if they could not. The
    /// migration and validation results are still authoritative, but no
    /// pull request can be opened without a patch.
    /// </summary>
    public string? PatchRecordingError { get; init; }
}
