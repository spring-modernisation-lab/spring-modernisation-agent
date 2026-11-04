namespace SpringModernisationAgent.Models;

public sealed record TransformationExecutionResult(
    TransformationResult Transformation,
    TransformationValidationResult Validation)
{
    /// <summary>
    /// Migrated and validated, with nothing left for a person to decide. A
    /// repository that still uses an unsupported library never succeeds,
    /// even when Maven passes: the AI cannot edit <c>pom.xml</c>, so a green
    /// build means the library was worked around, not replaced (run 23).
    /// </summary>
    public bool Succeeded =>
        Transformation.Status == TransformationStatus.Succeeded
        && Validation.Status == TransformationValidationStatus.Passed
        && UnsupportedLibraries.Count == 0
        && BehaviourHoldsOrWasNotRecorded;

    /// <summary>
    /// A person must choose before this can proceed: the Remediation Agent
    /// asked for a decision, or the repository still uses a library the
    /// target platform dropped.
    /// </summary>
    public bool NeedsDecision =>
        Transformation.Status == TransformationStatus.Succeeded
        && (Remediation?.Outcome == RemediationOutcome.NeedsHumanDecision
            || UnsupportedLibraries.Count > 0
            || (Validation.Status == TransformationValidationStatus.Passed && !BehaviourHoldsOrWasNotRecorded));

    /// <summary>
    /// Invariant I-CONTRACT-MATCHES: when a baseline was recorded, success
    /// also needs the migrated application to behave the same (or every
    /// difference to be allowed by a decision). A baseline whose comparison
    /// could not be made does not count as holding.
    /// </summary>
    private bool BehaviourHoldsOrWasNotRecorded =>
        Behaviour?.Baseline is null || Behaviour.Holds;

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

    /// <summary>
    /// Names of libraries in the repository that the target platform no
    /// longer supports, found deterministically in the assessed source.
    /// </summary>
    public IReadOnlyList<string> UnsupportedLibraries { get; init; } = [];

    /// <summary>
    /// Recipes that might replace work the route's recipes left, measured on
    /// a throwaway copy; null when discovery was off or validation passed.
    /// Suggestions only: never applied.
    /// </summary>
    public RecipeDiscoveryReport? RecipeDiscovery { get; init; }

    /// <summary>
    /// The failures OpenRewrite's recipes left, each with exactly one kind
    /// (docs/system-design.md §4), classified from facts before any AI work.
    /// Empty when validation passed or classification was off.
    /// </summary>
    public IReadOnlyList<SpringModernisationAgent.Triage.ClassifiedProblem> Problems { get; init; } = [];

    /// <summary>The behaviour contract; null when it was off or the repository can't be probed.</summary>
    public BehaviourReport? Behaviour { get; init; }
}
