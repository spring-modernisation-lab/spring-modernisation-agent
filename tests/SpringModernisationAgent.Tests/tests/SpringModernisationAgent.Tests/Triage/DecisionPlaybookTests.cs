using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Triage;

/// <summary>Decision playbooks (docs/system-design.md §12.1): what a person can choose, and what each choice does.</summary>
public class DecisionPlaybookTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Mappings = "mappings:\n  - id: M-X\n    name: x\n    notes: [\"n\"]\n";

    [Fact]
    public void Shipped_TrailingSlash_KeepIsAPortAndRetireAllowsThe404s()
    {
        var subject = Knowledge.Decisions.Subject("trailing-slash")!;

        Assert.Equal("S-HTTP-CONTRACT", subject.Answers);

        var keep = subject.Option("keep")!;
        Assert.False(keep.Manual);
        Assert.Equal("trailing-slash-mappings", keep.Port!.Scope);
        Assert.NotEmpty(keep.Port.Notes);
        Assert.Null(keep.Allows);
        Assert.Contains("POST /api/orders/", keep.Note, StringComparison.Ordinal);

        var retire = subject.Option("retire")!;
        Assert.Null(retire.Port);
        Assert.Equal("BS-TRAILING-SLASH", retire.Allows);
    }

    [Fact]
    public void Shipped_Ehcache_IsManualOnly_BecauseTestsAreNeverEdited()
    {
        var subject = Knowledge.Decisions.Subject("ehcache")!;

        var manual = Assert.Single(subject.Options);
        Assert.True(manual.Manual);
        Assert.Null(manual.Port);
        Assert.Equal("S-EHCACHE-2", subject.Answers);
    }

    [Fact]
    public void Subject_ForASignature_FindsWhatAnswersIt()
    {
        Assert.Equal("trailing-slash", Knowledge.Decisions.AnsweringSignature("S-HTTP-CONTRACT")!.Id);
        Assert.Null(Knowledge.Decisions.AnsweringSignature("S-JAVAX-SERVLET-STRING"));
    }

    [Theory]
    [InlineData("answers: S-NOPE\n    options:\n      - id: a\n        label: l\n        manual: true", "answers a signature that is not in diagnostic-signatures.yaml")]
    [InlineData("answers: S-HTTP-CONTRACT\n    options: []", "needs at least one option")]
    [InlineData("answers: S-HTTP-CONTRACT\n    options:\n      - id: a\n        label: l", "needs a port, allows or manual")]
    [InlineData("answers: S-HTTP-CONTRACT\n    options:\n      - id: a\n        label: l\n        allows: BS-NOPE", "allows a behaviour signature that is not in behaviour-signatures.yaml")]
    [InlineData("answers: S-HTTP-CONTRACT\n    options:\n      - id: a\n        label: l\n        port:\n          name: p\n          scope: nowhere\n          notes: [n]", "has an unknown scope")]
    [InlineData("answers: S-HTTP-CONTRACT\n    options:\n      - id: a\n        label: l\n        manual: true\n      - id: a\n        label: m\n        manual: true", "duplicates another option's id")]
    public void Parse_AMistakeInAPlaybookStopsTheRunNamingIt(
        string body,
        string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProblemKnowledge.Parse(
            "signatures:\n  - id: S-HTTP-CONTRACT\n    kind: contract\n    contains: [\"x\"]\n",
            Mappings,
            knownLibraries: [],
            knownRecipes: [],
            behaviourYaml: null,
            decisionsYaml: $"subjects:\n  - id: test\n    title: t\n    {body}\n"));

        Assert.Contains("decisions.yaml: subject 1 ('test')", exception.Message);
        Assert.Contains(expected, exception.Message);
    }
}
