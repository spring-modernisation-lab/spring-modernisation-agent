namespace SpringModernisationAgent.Triage;

/// <summary>
/// A scoped AI port a decision applies: how its files and obligations are
/// found (<see cref="Scope"/>), and what the AI is told.
/// </summary>
public sealed record DecisionPort(
    string Name,
    string Scope,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> MustKeep);

/// <param name="Allows">A behaviour signature (<c>BS-…</c>): the differences it matches are allowed by this decision.</param>
/// <param name="Manual">The pipeline does not apply it; the PR points at the manual steps.</param>
public sealed record DecisionOption(
    string Id,
    string Label,
    string? Note,
    DecisionPort? Port,
    string? Allows,
    bool Manual);

/// <param name="Answers">The diagnostic signature (<c>S-…</c>) of the problem this answers.</param>
public sealed record DecisionSubject(
    string Id,
    string Title,
    string Answers,
    IReadOnlyList<DecisionOption> Options)
{
    public DecisionOption? Option(
        string id) =>
        Options.FirstOrDefault(option => option.Id == id);
}

/// <summary>What a person can choose, from <c>decisions.yaml</c> (docs/system-design.md §12.1).</summary>
public sealed class DecisionPlaybooks(
    IReadOnlyList<DecisionSubject> subjects)
{
    /// <summary>How a decision port's files and obligations are found, from facts.</summary>
    public const string TrailingSlashMappings = "trailing-slash-mappings";

    public static readonly IReadOnlyList<string> KnownScopes = [TrailingSlashMappings];

    public IReadOnlyList<DecisionSubject> Subjects { get; } = subjects;

    public DecisionSubject? Subject(
        string id) =>
        Subjects.FirstOrDefault(subject => subject.Id == id);

    public DecisionSubject? AnsweringSignature(
        string signatureId) =>
        Subjects.FirstOrDefault(subject => subject.Answers == signatureId);
}
