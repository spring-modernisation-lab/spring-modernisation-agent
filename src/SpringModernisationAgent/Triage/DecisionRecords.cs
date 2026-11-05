using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Triage;

/// <summary>
/// A person's choice, committed as <c>decisions/&lt;repo&gt;.yaml</c>
/// (docs/system-design.md §12.1). It triggers a change; it never marks a
/// problem resolved.
/// </summary>
public sealed record DecisionRecord(
    string Id,
    string Subject,
    string Option,
    string By,
    string On,
    DecisionSubject Playbook,
    DecisionOption ChosenOption);

public static class DecisionRecords
{
    /// <summary>Reads <c>decisions/&lt;repository&gt;.yaml</c> under <paramref name="root"/>; no file means no decisions.</summary>
    public static IReadOnlyList<DecisionRecord> Load(
        string root,
        string repository,
        DecisionPlaybooks playbooks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentNullException.ThrowIfNull(playbooks);

        var path = Path.Combine(root, "decisions", repository + ".yaml");

        return File.Exists(path)
            ? Parse(File.ReadAllText(path), playbooks, $"decisions/{repository}.yaml")
            : [];
    }

    /// <summary>A mistake stops the run, naming the file, the entry and what is valid.</summary>
    public static IReadOnlyList<DecisionRecord> Parse(
        string yaml,
        DecisionPlaybooks playbooks,
        string source)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(playbooks);

        DecisionsFile? file;

        try
        {
            file = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<DecisionsFile>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"{source}: {exception.Message} (line {exception.Start.Line}).", exception);
        }

        var records = new List<DecisionRecord>();

        foreach (var (entry, index) in (file?.Decisions ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"{source}: decision {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("D-", StringComparison.Ordinal), label, "needs an id starting with D-");
            Require(records.All(record => record.Id != entry.Id), label, "duplicates another decision's id");
            Require(!string.IsNullOrWhiteSpace(entry.By), label, "needs who decided (by)");

            var subject = playbooks.Subject(entry.Subject ?? string.Empty);
            Require(subject is not null, label,
                $"has an unknown subject '{entry.Subject}' (known: {string.Join(", ", playbooks.Subjects.Select(known => known.Id))})");

            var option = subject!.Option(entry.Option ?? string.Empty);
            Require(option is not null, label,
                $"has an unknown option '{entry.Option}' for {subject.Id} (known: {string.Join(", ", subject.Options.Select(known => known.Id))})");
            Require(records.All(record => record.Subject != subject.Id), label, $"decides {subject.Id} again");

            records.Add(new DecisionRecord(entry.Id!, subject.Id, option!.Id, entry.By!, entry.On ?? string.Empty, subject, option));
        }

        return records;
    }

    private static void Require(
        bool valid,
        string label,
        string problem)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"{label} {problem}.");
        }
    }

    private sealed class DecisionsFile
    {
        public List<DecisionEntry>? Decisions { get; set; }
    }

    private sealed class DecisionEntry
    {
        public string? Id { get; set; }

        public string? Subject { get; set; }

        public string? Option { get; set; }

        public string? By { get; set; }

        public string? On { get; set; }
    }
}
