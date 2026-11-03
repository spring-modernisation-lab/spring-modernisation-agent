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

    /// <summary>
    /// What the Remediation Agent did, or null when remediation was not
    /// enabled for the run. It never overrides <see cref="Validation"/>,
    /// which is always the final, authoritative Maven result.
    /// </summary>
    public RemediationReport? Remediation { get; init; }
}
