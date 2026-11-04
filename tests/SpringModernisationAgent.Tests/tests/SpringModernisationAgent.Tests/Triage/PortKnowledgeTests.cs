using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Triage;

/// <summary>What 8.4's scoped ports need from the knowledge base: obligations and behaviour signatures.</summary>
public class PortKnowledgeTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Mappings = "mappings:\n  - id: M-X\n    name: x\n    notes: [\"n\"]\n";

    [Fact]
    public void ShippedMappings_HttpClientIsObligedToKeepItsClientValues()
    {
        var mapping = Knowledge.Mappings["M-HTTPCLIENT-5-POOL"];

        Assert.True(mapping.Obliges("B-BEAN-default-HttpClient.connectionTimeToLive"));
        Assert.True(mapping.Obliges("B-BEAN-production-HttpClient.maxTotal"));
        Assert.False(mapping.Obliges("B-SEC-default-GET /api/orders-VIEWER"));
    }

    [Fact]
    public void ShippedMappings_SecurityIsObligedToKeepWhoMayCallWhat()
    {
        var mapping = Knowledge.Mappings["M-SECURITY-6-FILTERCHAIN"];

        Assert.True(mapping.Obliges("B-SEC-default-POST /api/orders-VIEWER"));
        Assert.False(mapping.Obliges("B-BEAN-default-HttpClient.maxTotal"));
    }

    [Fact]
    public void ShippedMappings_HibernateHasNoObligationTheProbeCanRead()
    {
        // The stored column format isn't probed; the plan says so rather than pretending.
        Assert.Empty(Knowledge.Mappings["M-HIBERNATE-6-USERTYPE"].Obligations);
    }

    [Theory]
    [InlineData("B-SEC-default-GET /api/orders/-VIEWER", "200", "404", "BS-TRAILING-SLASH")]
    [InlineData("B-SEC-default-POST /api/orders/-OPERATOR", "400", "404", "BS-TRAILING-SLASH")]
    [InlineData("B-SEC-default-GET /api/orders-VIEWER", "200", "404", null)]
    [InlineData("B-SEC-default-GET /api/orders/-VIEWER", "404", "404", null)]
    [InlineData("B-SEC-default-GET /api/orders/-VIEWER", "200", "403", null)]
    [InlineData("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms", BehaviourRecord.Unavailable, null)]
    public void ShippedBehaviourSignatures_ExplainOnlyTheTrailingSlashChange(
        string item,
        string before,
        string after,
        string? expected)
    {
        // Run 26: Spring 6 no longer matches a trailing slash. That is the platform, not a port.
        var explained = Knowledge.Explain(new ContractDifference(item, before, after, ContractItemStatus.Changed, null));

        Assert.Equal(expected, explained?.Id);
        if (explained is not null)
        {
            Assert.Equal(ProblemKind.Contract, explained.Kind);
        }
    }

    [Fact]
    public void Parse_ObligationsAreGlobsOverContractItems()
    {
        var knowledge = ProblemKnowledge.Parse(
            "signatures: []\n",
            "mappings:\n  - id: M-X\n    name: x\n    notes: [\"n\"]\n    obligations: [\"B-CFG-*-server.port\"]\n",
            knownLibraries: [],
            knownRecipes: []);

        var mapping = knowledge.Mappings["M-X"];
        Assert.Equal(["B-CFG-*-server.port"], mapping.Obligations);
        Assert.True(mapping.Obliges("B-CFG-production-server.port"));
        Assert.False(mapping.Obliges("B-CFG-production-server.port.extra"));
    }

    [Fact]
    public void Parse_AnObligationThatIsNotAContractItemStopsTheRun()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProblemKnowledge.Parse(
            "signatures: []\n",
            "mappings:\n  - id: M-X\n    name: x\n    notes: [\"n\"]\n    obligations: [\"HttpClient.*\"]\n",
            knownLibraries: [],
            knownRecipes: []));

        Assert.Contains("mapping 1 ('M-X')", exception.Message);
        Assert.Contains("obligations must be contract item patterns starting with B-", exception.Message);
    }

    [Theory]
    [InlineData("kind: contract\n    reason: r", "needs an item pattern")]
    [InlineData("kind: api-move\n    item: \"^B-\"\n    reason: r", "needs a kind: contract, platform-dropped")]
    [InlineData("kind: contract\n    item: \"(unclosed\"\n    reason: r", "has an invalid pattern")]
    [InlineData("kind: contract\n    item: \"^B-\"", "needs a reason")]
    public void Parse_AMistakeInABehaviourSignatureStopsTheRunNamingIt(
        string body,
        string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProblemKnowledge.Parse(
            "signatures: []\n",
            Mappings,
            knownLibraries: [],
            knownRecipes: [],
            behaviourYaml: $"signatures:\n  - id: BS-TEST\n    {body}\n"));

        Assert.Contains("behaviour-signatures.yaml: signature 1 ('BS-TEST')", exception.Message);
        Assert.Contains(expected, exception.Message);
    }
}
