using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Triage;

public class DecisionRecordTests : IDisposable
{
    private static readonly DecisionPlaybooks Playbooks =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList()).Decisions;

    private const string Valid =
        "decisions:\n" +
        "  - id: D-orders-service-1\n" +
        "    subject: trailing-slash\n" +
        "    option: keep\n" +
        "    by: a-person\n" +
        "    on: 2026-11-05\n";

    private readonly string _directory = Directory.CreateTempSubdirectory("decisions-").FullName;

    [Fact]
    public void Parse_AValidFile_GivesTheRecordWithItsOption()
    {
        var record = Assert.Single(DecisionRecords.Parse(Valid, Playbooks, "decisions/orders-service.yaml"));

        Assert.Equal("D-orders-service-1", record.Id);
        Assert.Equal(("trailing-slash", "keep", "a-person", "2026-11-05"), (record.Subject, record.Option, record.By, record.On));
        Assert.Equal("trailing-slash", record.Playbook.Id);
        Assert.Equal("keep", record.ChosenOption.Id);
    }

    [Fact]
    public void Load_NoFile_MeansNoDecisions()
    {
        Assert.Empty(DecisionRecords.Load(_directory, "orders-service", Playbooks));
    }

    [Fact]
    public void Load_ReadsDecisionsDirectoryByRepositoryName()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "decisions"));
        File.WriteAllText(Path.Combine(_directory, "decisions", "orders-service.yaml"), Valid);

        Assert.Single(DecisionRecords.Load(_directory, "orders-service", Playbooks));
        Assert.Empty(DecisionRecords.Load(_directory, "platform-parent", Playbooks));
    }

    [Theory]
    [InlineData("subject: nothing", "unknown subject 'nothing' (known: trailing-slash, ehcache)")]
    [InlineData("option: maybe", "unknown option 'maybe' for trailing-slash (known: keep, retire)")]
    [InlineData("id: orders-1", "needs an id starting with D-")]
    [InlineData("by: ''", "needs who decided (by)")]
    public void Parse_AMistakeStopsTheRunNamingTheFileAndTheValidChoices(
        string replacement,
        string expected)
    {
        var field = replacement[..replacement.IndexOf(':', StringComparison.Ordinal)];
        var yaml = string.Join('\n', Valid.Split('\n').Select(line => line.TrimStart(' ', '-').StartsWith(field + ":", StringComparison.Ordinal)
            ? line[..(line.IndexOf(field + ":", StringComparison.Ordinal))] + replacement
            : line));

        var exception = Assert.Throws<InvalidOperationException>(() => DecisionRecords.Parse(yaml, Playbooks, "decisions/orders-service.yaml"));

        Assert.Contains("decisions/orders-service.yaml: decision 1", exception.Message);
        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public void Parse_TwoDecisionsForOneSubject_AreRefused()
    {
        var twice = Valid + Valid.Replace("decisions:\n", string.Empty, StringComparison.Ordinal).Replace("D-orders-service-1", "D-orders-service-2", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(() => DecisionRecords.Parse(twice, Playbooks, "decisions/orders-service.yaml"));

        Assert.Contains("decision 2", exception.Message);
        Assert.Contains("decides trailing-slash again", exception.Message);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
