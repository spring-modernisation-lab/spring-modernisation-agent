namespace SpringModernisationAgent.Behaviour;

/// <summary>
/// What an application did, recorded by the behaviour probe: item ID →
/// value (docs/system-design.md §3). For example
/// <c>B-SEC-default-POST /api/orders-VIEWER</c> → <c>403</c>.
/// </summary>
/// <param name="Schema">Always <see cref="CurrentSchema"/> for records written now.</param>
/// <param name="Commit">The commit the record was made from.</param>
public sealed record BehaviourRecord(
    string Schema,
    string Repository,
    string Commit,
    IReadOnlyDictionary<string, string> Items)
{
    public const string CurrentSchema = "behaviour/1";

    /// <summary>The value an item gets when the probe could not read it.</summary>
    public const string Unavailable = "unavailable";
}

public enum ContractItemStatus
{
    /// <summary>The value differs from the baseline.</summary>
    Changed,

    /// <summary>The probe could not read it after the change (for example, the bean is gone). Unknown is not "same".</summary>
    Unavailable,

    /// <summary>It differs, and a recorded decision allows it.</summary>
    Allowed
}

/// <param name="Decision">The decision (<c>D-…</c>) that allows it, when <see cref="ContractItemStatus.Allowed"/>.</param>
public sealed record ContractDifference(
    string Item,
    string Before,
    string? After,
    ContractItemStatus Status,
    string? Decision);

/// <param name="Differences">Every item that is not the same, in item order.</param>
/// <param name="Same">How many baseline items are unchanged.</param>
/// <param name="New">Items only the new record has: reported, never a difference.</param>
public sealed record ContractComparison(
    IReadOnlyList<ContractDifference> Differences,
    int Same,
    IReadOnlyList<string> New)
{
    /// <summary>
    /// The contract holds when every difference is allowed by a decision
    /// (invariant <c>I-CONTRACT-MATCHES</c>).
    /// </summary>
    public bool Matches => Differences.All(difference => difference.Status == ContractItemStatus.Allowed);
}

public static class BehaviourContract
{
    /// <summary>
    /// Compares a record made after a change with the baseline. Every baseline
    /// item must keep its value; one that is missing or unreadable afterwards
    /// is <see cref="ContractItemStatus.Unavailable"/>, never assumed the same.
    /// </summary>
    /// <param name="allowed">Item ID → the decision that allows it to differ.</param>
    public static ContractComparison Compare(
        BehaviourRecord baseline,
        BehaviourRecord after,
        IReadOnlyDictionary<string, string>? allowed = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(after);

        allowed ??= new Dictionary<string, string>();

        var differences = new List<ContractDifference>();
        var same = 0;

        foreach (var (item, before) in baseline.Items.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            after.Items.TryGetValue(item, out var now);

            if (now is not null && string.Equals(now, before, StringComparison.Ordinal))
            {
                same++;
                continue;
            }

            var status = now is null || now == BehaviourRecord.Unavailable
                ? ContractItemStatus.Unavailable
                : ContractItemStatus.Changed;

            var decision = allowed.GetValueOrDefault(item);

            differences.Add(new ContractDifference(
                item,
                before,
                now,
                decision is null ? status : ContractItemStatus.Allowed,
                decision));
        }

        var added = after.Items.Keys
            .Where(item => !baseline.Items.ContainsKey(item))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ContractComparison(differences, same, added);
    }
}
