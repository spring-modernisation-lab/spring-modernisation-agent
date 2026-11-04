using SpringModernisationAgent.Behaviour;

namespace SpringModernisationAgent.Models;

/// <summary>
/// The behaviour contract for one repository (docs/system-design.md §3): what
/// the Boot 2.7 application did, what the migrated one does, and how they
/// differ. The contract holds when every difference is allowed by a decision
/// (invariant <c>I-CONTRACT-MATCHES</c>).
/// </summary>
/// <param name="Baseline">Recorded on the assessed commit, before any change.</param>
/// <param name="After">Recorded on the migrated code; null when it could not be (see <paramref name="Note"/>).</param>
/// <param name="Comparison">Null when there is nothing to compare.</param>
/// <param name="Note">Why something is missing, for example "the migrated code does not compile".</param>
public sealed record BehaviourReport(
    BehaviourRecord? Baseline,
    BehaviourRecord? After,
    ContractComparison? Comparison,
    string? Note)
{
    /// <summary>True only when both records exist and every difference is allowed.</summary>
    public bool Holds => Comparison?.Matches == true;

    /// <summary>
    /// Differences a behaviour signature explains (item → <c>BS-…</c>): the
    /// platform's doing, decisions for a person. They still count against
    /// <see cref="Holds"/> until a decision allows them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Explained { get; init; } = new Dictionary<string, string>();
}
