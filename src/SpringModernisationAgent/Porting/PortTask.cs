using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Porting;

/// <summary>
/// One scoped AI port (docs/system-design.md §4.1): a known API move, the
/// only files the AI may change for it, the evidence for this move only, and
/// the behaviour it must keep.
/// </summary>
/// <param name="Files">Repository-relative production source files; the AI may change only these.</param>
/// <param name="Problems">The classified problems this task covers, all with <see cref="Mapping"/>.</param>
/// <param name="Evidence">Those problems' diagnostic lines, and nothing else.</param>
public sealed record PortTask(
    ApiMapping Mapping,
    IReadOnlyList<string> Files,
    IReadOnlyList<ClassifiedProblem> Problems,
    IReadOnlyList<string> Evidence)
{
    /// <summary>
    /// Signatures of problems this task answers wherever they fail. A
    /// decision port (docs/system-design.md §12.1) is judged by these, because
    /// the failing test has no mapping and is not in the port's files.
    /// </summary>
    public IReadOnlyList<string> EvidenceSignatures { get; init; } = [];

    /// <summary>
    /// Whether a behaviour signature may excuse a change in this task's
    /// obligations. True for a port (the platform did it); false for a
    /// decision port, which exists to undo exactly that change.
    /// </summary>
    public bool ExcusePlatformChanges { get; init; } = true;

    /// <summary>The task's ID: its mapping's, since there is one task per mapping.</summary>
    public string Id => Mapping.Id;

    /// <summary>Whether the port must keep this behaviour-contract item.</summary>
    public bool Obliges(
        string item) =>
        Mapping.Obliges(item);
}

/// <summary>A problem no task will work, and why: it goes to a person.</summary>
public sealed record UnworkableProblem(
    ClassifiedProblem Problem,
    string Reason);

/// <param name="Tasks">In the order they are worked.</param>
/// <param name="Unworkable">Every other problem except knock-ons, which are re-checked after the others.</param>
public sealed record PortPlan(
    IReadOnlyList<PortTask> Tasks,
    IReadOnlyList<UnworkableProblem> Unworkable);
