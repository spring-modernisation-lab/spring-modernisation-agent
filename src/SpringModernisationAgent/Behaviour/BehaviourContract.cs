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
/// <param name="Unchecked">
/// On a context (<c>B-CTX-…</c>) that no longer starts: the profile's items
/// that could not be read because of it. They are one difference, not many.
/// </param>
public sealed record ContractDifference(
    string Item,
    string Before,
    string? After,
    ContractItemStatus Status,
    string? Decision,
    IReadOnlyList<string>? Unchecked = null);

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

    /// <summary>Items not checked because their application no longer starts.</summary>
    public int Unchecked => Differences.Sum(difference => difference.Unchecked?.Count ?? 0);
}

public static class BehaviourContract
{
    /// <summary>
    /// Compares a record made after a change with the baseline. Every baseline
    /// item must keep its value; one that is missing or unreadable afterwards
    /// is <see cref="ContractItemStatus.Unavailable"/>, never assumed the same.
    /// </summary>
    /// <remarks>
    /// When a profile's application started on the baseline and no longer
    /// does, its unreadable items are folded into that one context difference
    /// (<see cref="ContractDifference.Unchecked"/>): the cause, not dozens of
    /// symptoms. Values that were read and differ are still listed.
    /// </remarks>
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

        // Profiles whose application started on the baseline but not now, unless a decision allows that.
        var stopped = baseline.Items
            .Where(entry => entry.Key.StartsWith(ContextPrefix, StringComparison.Ordinal)
                            && entry.Value == ContextStarts
                            && after.Items.TryGetValue(entry.Key, out var now)
                            && now != ContextStarts
                            && !allowed.ContainsKey(entry.Key))
            .ToDictionary(entry => entry.Key[ContextPrefix.Length..], _ => new List<string>(), StringComparer.Ordinal);

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

            if (status == ContractItemStatus.Unavailable
                && !allowed.ContainsKey(item)
                && ProfileOf(item, stopped.Keys) is { } profile
                && stopped.TryGetValue(profile, out var hidden))
            {
                hidden.Add(item);
                continue;
            }

            var decision = allowed.GetValueOrDefault(item);

            differences.Add(new ContractDifference(
                item,
                before,
                now,
                decision is null ? status : ContractItemStatus.Allowed,
                decision));
        }

        foreach (var (profile, hidden) in stopped.Where(entry => entry.Value.Count > 0))
        {
            var index = differences.FindIndex(difference => difference.Item == ContextPrefix + profile);
            differences[index] = differences[index] with { Unchecked = hidden };
        }

        var added = after.Items.Keys
            .Where(item => !baseline.Items.ContainsKey(item))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ContractComparison(differences, same, added);
    }

    private const string ContextPrefix = "B-CTX-";
    private const string ContextStarts = "starts";

    /// <summary>
    /// Which of <paramref name="profiles"/> an item belongs to:
    /// <c>B-SEC-production-GET /x-VIEWER</c> → <c>production</c>. Matched,
    /// not split, because profile names can contain hyphens.
    /// </summary>
    private static string? ProfileOf(
        string item,
        IEnumerable<string> profiles)
    {
        var kindEnd = item.StartsWith(ContextPrefix, StringComparison.Ordinal) ? -1 : item.IndexOf('-', 2);

        if (kindEnd < 0)
        {
            return null;
        }

        var rest = item[(kindEnd + 1)..];
        return profiles
            .Where(profile => rest.StartsWith(profile + "-", StringComparison.Ordinal))
            .MaxBy(profile => profile.Length);
    }
}
