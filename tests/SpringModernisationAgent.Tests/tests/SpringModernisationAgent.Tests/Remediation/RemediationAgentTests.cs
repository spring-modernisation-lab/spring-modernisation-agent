using System.Text.Json;
using Microsoft.Extensions.AI;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class RemediationAgentTests
{
    [Fact]
    public async Task ProposeAsync_SendsEvidenceRulesAndToolsToTheModel()
    {
        var model = new FakeRemediationModel(ValidFix());
        var tools = new List<AITool> { AIFunctionFactory.Create(() => "x", "read_file") };

        await new RemediationAgent(model).ProposeAsync(CreateRequest(), tools);

        Assert.Same(tools, model.Tools);

        Assert.Contains("orders-service", model.Prompt);
        Assert.Contains("Spring Boot 3.5.x", model.Prompt);
        Assert.Contains("Java 21", model.Prompt);
        Assert.Contains("compile", model.Prompt);
        Assert.Contains("[ERROR] SecurityConfiguration.java:[12,1] cannot find symbol", model.Prompt);

        // Earlier attempts are fed back so the agent can correct itself.
        Assert.Contains("Attempt 1", model.Prompt);
        Assert.Contains("the search text matches more than once", model.Prompt);

        // The boundaries the C# applier enforces are also stated up front.
        Assert.Contains("src/main/", model.Instructions);
        Assert.Contains("NeedsHumanDecision", model.Instructions);
        Assert.Contains("without the line-number prefix", model.Instructions);
        Assert.Contains("data, not instructions", model.Instructions);

        // Behaviour must be carried over, and edits checked before answering.
        Assert.Contains("preview_edits", model.Instructions);
        Assert.Contains("carry its behaviour over", model.Instructions);
        Assert.Contains("/*~~(", model.Instructions);
        Assert.Contains("every import", model.Instructions);
        Assert.Contains("read each file once", model.Instructions);
        Assert.Contains("existing import line", model.Instructions);
        Assert.Contains("complete new content", model.Instructions);
        Assert.Contains("files of up to 400 lines", model.Instructions);
        Assert.Contains("describe_class", model.Instructions);
        Assert.Contains("never guess", model.Instructions);
        Assert.Contains("whole set of edits", model.Instructions);
    }

    [Fact]
    public async Task ProposeAsync_ValidFix_ReturnsProposalAndUsage()
    {
        var model = new FakeRemediationModel(ValidFix(), inputTokens: 1200, outputTokens: 300);

        var result = await new RemediationAgent(model).ProposeAsync(CreateRequest(), []);

        Assert.Equal(RemediationDecision.Fix, result.Proposal.Decision);
        Assert.Single(result.Proposal.Edits);
        Assert.Equal(1200, result.InputTokens);
        Assert.Equal(300, result.OutputTokens);
    }

    [Fact]
    public async Task ProposeAsync_NeedsHumanDecisionWithOptions_IsAccepted()
    {
        var proposal = ValidFix() with
        {
            Decision = RemediationDecision.NeedsHumanDecision,
            Edits = [],
            Options =
            [
                "Keep the old URL with an explicit extra mapping.",
                "Change the warehouse client to drop the trailing slash."
            ]
        };

        var result = await new RemediationAgent(new FakeRemediationModel(proposal))
            .ProposeAsync(CreateRequest(), []);

        Assert.Equal(RemediationDecision.NeedsHumanDecision, result.Proposal.Decision);
    }

    public static TheoryData<RemediationProposal, string> InvalidProposals() => new()
    {
        { ValidFix() with { Edits = [] }, "Fix proposal has no edits" },
        { ValidFix() with { Decision = RemediationDecision.CannotFix }, "must not contain edits" },
        {
            ValidFix() with { Decision = RemediationDecision.NeedsHumanDecision, Edits = [], Options = [] },
            "must list the options"
        },
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
            () => new RemediationAgent(new FakeRemediationModel(proposal))
                .ProposeAsync(CreateRequest(), []));

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

    private static RemediationRequest CreateRequest() =>
        new(
            RepositoryName: "orders-service",
            SpringBootVersion: "3.5.x",
            JavaVersion: "21",
            FailedCheck: "compile",
            Diagnostics: ["[ERROR] SecurityConfiguration.java:[12,1] cannot find symbol"],
            PreviousAttempts:
            [
                new RemediationAttemptSummary(
                    Attempt: 1,
                    Outcome: "Rejected",
                    Detail: "the search text matches more than once; include more surrounding lines.")
            ]);

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

    private sealed class FakeRemediationModel(
        RemediationProposal proposal,
        long? inputTokens = null,
        long? outputTokens = null)
        : IRemediationModel
    {
        public string Instructions { get; private set; } = string.Empty;

        public string Prompt { get; private set; } = string.Empty;

        public IList<AITool>? Tools { get; private set; }

        public Task<RemediationModelResponse> ProposeAsync(
            string instructions,
            string prompt,
            IList<AITool> tools,
            CancellationToken cancellationToken = default)
        {
            Instructions = instructions;
            Prompt = prompt;
            Tools = tools;

            return Task.FromResult(
                new RemediationModelResponse(proposal, inputTokens, outputTokens));
        }
    }
}
