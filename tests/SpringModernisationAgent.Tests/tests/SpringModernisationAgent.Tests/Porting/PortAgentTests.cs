using System.Text.Json;
using Microsoft.Extensions.AI;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

public class PortAgentTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Http = "src/main/java/a/config/InventoryClientConfiguration.java";

    private static readonly PortTask HttpTask = new(
        Knowledge.Mappings["M-HTTPCLIENT-5-POOL"],
        [Http],
        [new ClassifiedProblem("P3", ProblemKind.ApiMove, Http, "S-HTTPCLIENT-5-POOL", "M-HTTPCLIENT-5-POOL", null, null, ["e"])],
        ["[ERROR] InventoryClientConfiguration.java:[28,17] cannot find symbol", "  symbol:   method setMaxConnTotal(int)"]);

    private static readonly PortRequest Request = new(
        "orders-service",
        "3.5.x",
        "21",
        HttpTask,
        new Dictionary<string, string>
        {
            ["B-BEAN-default-HttpClient.connectionTimeToLive"] = "30000ms",
            ["B-BEAN-default-HttpClient.maxTotal"] = "50"
        },
        Repair: null);

    private static readonly RemediationEdit HttpEdit = new(Http, "a", "b", "port the pool");

    [Fact]
    public void Prompt_IsTheTaskAndNothingElse()
    {
        var prompt = PortAgent.CreatePrompt(Request);

        Assert.Contains("Task: M-HTTPCLIENT-5-POOL (Apache HttpClient 5: pool and connection settings)", prompt);
        Assert.Contains($"- {Http}", prompt);
        Assert.Contains("symbol:   method setMaxConnTotal(int)", prompt);
        Assert.Contains("ConnectionConfig", prompt); // the mapping's checked notes
        Assert.Contains("- connection time-to-live", prompt); // mustKeep
        Assert.Contains("- B-BEAN-default-HttpClient.connectionTimeToLive = 30000ms", prompt);
        Assert.DoesNotContain("Your previous attempt", prompt);
    }

    [Fact]
    public void Prompt_ARepairGivesTheExactFailure()
    {
        var prompt = PortAgent.CreatePrompt(Request with
        {
            Repair = new PortRepair("a kept behaviour changed", ["B-BEAN-default-HttpClient.connectionTimeToLive: 30000ms -> unavailable"])
        });

        Assert.Contains("Your previous attempt was not kept: a kept behaviour changed.", prompt);
        Assert.Contains("B-BEAN-default-HttpClient.connectionTimeToLive: 30000ms -> unavailable", prompt);
        Assert.Contains("This is your only repair", prompt);
    }

    [Fact]
    public void Instructions_OfferOnlyFixOrCannotFix()
    {
        Assert.Contains("- Fix:", PortAgent.Instructions);
        Assert.Contains("- CannotFix:", PortAgent.Instructions);
        Assert.DoesNotContain("NeedsHumanDecision", PortAgent.Instructions);
    }

    [Fact]
    public async Task ProposeAsync_AFixIsReturnedWithItsTokens()
    {
        var model = new FakeModel(new RemediationProposal(RemediationDecision.Fix, "d", "r", [HttpEdit], []), 100, 20);

        var result = await new PortAgent(model).ProposeAsync(Request, []);

        Assert.Equal(RemediationDecision.Fix, result.Proposal.Decision);
        Assert.Equal((100L, 20L), (result.InputTokens!.Value, result.OutputTokens!.Value));
        Assert.Equal(PortAgent.Instructions, model.Instructions);
    }

    [Fact]
    public async Task ProposeAsync_ADecisionWithoutEditsBecomesCannotFixKeepingItsOptions()
    {
        // Decisions come from classification; a port that needs one is a port the AI can't do.
        var model = new FakeModel(new RemediationProposal(RemediationDecision.NeedsHumanDecision, "d", "r", [], ["keep", "drop"]));

        var result = await new PortAgent(model).ProposeAsync(Request, []);

        Assert.Equal(RemediationDecision.CannotFix, result.Proposal.Decision);
        Assert.Equal(["keep", "drop"], result.Proposal.Options);
    }

    [Fact]
    public async Task ProposeAsync_ADecisionCarryingEditsIsInvalid()
    {
        var model = new FakeModel(new RemediationProposal(RemediationDecision.NeedsHumanDecision, "d", "r", [HttpEdit], ["x"]));

        var exception = await Assert.ThrowsAsync<InvalidRemediationProposalException>(
            () => new PortAgent(model).ProposeAsync(Request, []));

        Assert.Contains("Fix or CannotFix", exception.Message);
    }

    public static TheoryData<RemediationProposal, string> InvalidProposals() => new()
    {
        { ValidFix() with { Edits = [] }, "Fix proposal has no edits" },
        { ValidFix() with { Decision = RemediationDecision.CannotFix }, "must not contain edits" },
        { ValidFix() with { Diagnosis = " " }, "diagnosis" },
        { ValidFix() with { RootCause = "" }, "root cause" },
        { ValidFix() with { Edits = [ValidEdit() with { Path = "" }] }, "edit 1 has no path" },
        { ValidFix() with { Edits = [ValidEdit() with { Rationale = "" }] }, "edit 1 has no rationale" },
        {
            ValidFix() with { Edits = [ValidEdit() with { Search = "", Replace = "" }] },
            "edit 1 changes nothing"
        },
        {
            ValidFix() with { Edits = [ValidEdit() with { Search = "same", Replace = "same" }] },
            "edit 1 changes nothing"
        }
    };

    [Theory]
    [MemberData(nameof(InvalidProposals))]
    public async Task ProposeAsync_InvalidProposal_Throws(
        RemediationProposal proposal,
        string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<InvalidRemediationProposalException>(
            () => new PortAgent(new FakeModel(proposal)).ProposeAsync(Request, []));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void Decision_SerialisesAsItsName()
    {
        // Structured output relies on the schema using names, not numbers.
        Assert.Equal(
            "\"NeedsHumanDecision\"",
            JsonSerializer.Serialize(RemediationDecision.NeedsHumanDecision));
    }

    private static RemediationEdit ValidEdit() =>
        new(
            Path: "src/main/java/com/example/SecurityConfiguration.java",
            Search: " extends WebSecurityConfigurerAdapter",
            Replace: "",
            Rationale: "The adapter was removed in Spring Security 6.");

    private static RemediationProposal ValidFix() =>
        new(
            Decision: RemediationDecision.Fix,
            Diagnosis: "SecurityConfiguration still extends the removed adapter.",
            RootCause: "WebSecurityConfigurerAdapter was removed in Spring Security 6.",
            Edits: [ValidEdit()],
            Options: []);

    [Fact]
    public void Instructions_KeepTheGuardrailRules()
    {
        Assert.Contains("Never remove annotations that carry behaviour", PortAgent.Instructions);
        Assert.Contains("Change only the files the task lists", PortAgent.Instructions);
    }

    private sealed class FakeModel(
        RemediationProposal proposal,
        long? inputTokens = null,
        long? outputTokens = null)
        : IRemediationModel
    {
        public string Instructions { get; private set; } = string.Empty;

        public Task<RemediationModelResponse> ProposeAsync(
            string instructions,
            string prompt,
            IList<AITool> tools,
            CancellationToken cancellationToken = default)
        {
            Instructions = instructions;
            return Task.FromResult(new RemediationModelResponse(proposal, inputTokens, outputTokens));
        }
    }
}
