namespace SpringModernisationAgent.Models;

public sealed record TransformationExecutionResult(
    TransformationResult Transformation,
    TransformationValidationResult Validation)
{
    /// <summary>
    /// The repository's state and every reason for it, from one function
    /// over the facts (docs/system-design.md §8.2). Everything that shows or
    /// gates on an outcome reads this.
    /// </summary>
    public Verdict Verdict => Verdict.Of(this);

    /// <summary>Migrated, validated, behaving as before, and nothing left for a person.</summary>
    public bool Succeeded => Verdict.State == VerdictState.Succeeded;

    /// <summary>Something only a person can decide remains (see <see cref="Verdict"/> for what).</summary>
    public bool NeedsDecision => Verdict.State == VerdictState.NeedsDecision;

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

    /// <summary>
    /// The problems still failing at the end, after any ports, classified
    /// the same way. Empty when validation passed. The verdict reads these.
    /// </summary>
    public IReadOnlyList<SpringModernisationAgent.Triage.ClassifiedProblem> RemainingProblems { get; init; } = [];

    /// <summary>A person's recorded decisions for this repository and what each did (docs/system-design.md §12.1).</summary>
    public IReadOnlyList<SpringModernisationAgent.Porting.DecisionOutcome> Decisions { get; init; } = [];

    /// <summary>The behaviour contract; null when it was off or the repository can't be probed.</summary>
    public BehaviourReport? Behaviour { get; init; }
}
