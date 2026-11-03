using Microsoft.Extensions.AI;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Remediation;

public class RemediationLoopTests : IDisposable
{
    private const string CommitSha = "b39866542351f9c79f05df7558e84aa9799ef33f";

    private const string SecurityPath = "src/main/java/com/example/SecurityConfiguration.java";

    private const string CompileError =
        "[ERROR] src/main/java/com/example/SecurityConfiguration.java:[3,45] cannot find symbol";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"remediation-loop-{Guid.NewGuid():N}");

    private string Repository => Path.Combine(_root, "orders-service");

    private string OpenRewritePatchPath => Path.Combine(_root, "01-openrewrite.patch");

    public RemediationLoopTests()
    {
        Write(SecurityPath, "class SecurityConfiguration extends WebSecurityConfigurerAdapter {}\n");
        File.WriteAllText(OpenRewritePatchPath, "diff --git a/pom.xml b/pom.xml");
    }

    [Fact]
    public async Task RunAsync_FixPassesValidation_ReportsFixedWithPatchAndEvidence()
    {
        var agent = new FakeAgent(Fix(" extends WebSecurityConfigurerAdapter", ""));
        var recorder = new FakeRecorder();
        var maven = new FakeMaven(Passed(), Passed(), Passed());

        var result = await CreateLoop(agent, maven, recorder).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.Fixed, result.Report.Outcome);
        Assert.True(result.FinalValidation.Passed);

        var attempt = Assert.Single(result.Report.Attempts);
        Assert.Equal(1, attempt.Number);
        Assert.Equal("compile", attempt.FailedCheck);
        Assert.True(attempt.Applied);
        Assert.Equal(RemediationDecision.Fix, attempt.Proposal!.Decision);
        Assert.True(attempt.Validation!.Passed);
        Assert.Equal(1000, attempt.InputTokens);
        Assert.Equal("02-remediation-attempt-1", attempt.Patch!.Stage);

        Assert.Equal(["02-remediation-attempt-1"], result.AttemptPatches.Select(patch => patch.Stage));
        Assert.Equal("tree-openrewrite", recorder.Calls.Single().BaseRevision);

        Assert.Equal("class SecurityConfiguration {}\n", Read(SecurityPath));

        // The agent saw the failing check's diagnostics and the OpenRewrite patch.
        var request = agent.Requests.Single();
        Assert.Equal("compile", request.FailedCheck);
        Assert.Contains(CompileError, request.Diagnostics);
        Assert.Empty(request.PreviousAttempts);
        Assert.Equal("get_openrewrite_patch", agent.ToolNames.Single());
    }

    [Fact]
    public async Task RunAsync_SecondAttemptFixes_ChainsPatchesAndFeedsBackTheNewFailure()
    {
        var agent = new FakeAgent(
            Fix(" extends WebSecurityConfigurerAdapter", ""),
            Fix("class SecurityConfiguration {}", "class SecurityConfiguration { }"));

        var recorder = new FakeRecorder();

        var maven = new FakeMaven(
            Passed(), Failed("[ERROR] Tests run: 1, Failures: 1"),
            Passed(), Passed(), Passed());

        var result = await CreateLoop(agent, maven, recorder).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.Fixed, result.Report.Outcome);
        Assert.Equal(2, result.Report.Attempts.Count);
        Assert.Equal("unit-tests", result.Report.Attempts[1].FailedCheck);

        // Attempt 2's patch is relative to attempt 1's snapshot.
        Assert.Equal(["tree-openrewrite", "tree-1"], recorder.Calls.Select(call => call.BaseRevision));

        Assert.Equal(
            ["02-remediation-attempt-1", "03-remediation-attempt-2"],
            result.AttemptPatches.Select(patch => patch.Stage));

        var feedback = Assert.Single(agent.Requests[1].PreviousAttempts);
        Assert.Contains("still fails", feedback.Outcome);
        Assert.Contains("unit-tests", feedback.Detail);
        Assert.Contains("Tests run: 1, Failures: 1", feedback.Detail);
    }

    [Fact]
    public async Task RunAsync_GuardrailRejection_IsRecordedFedBackAndRetried()
    {
        var agent = new FakeAgent(
            Fix("class Missing", "class Found"),
            Fix(" extends WebSecurityConfigurerAdapter", ""));

        var result = await CreateLoop(agent, new FakeMaven(Passed(), Passed(), Passed())).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.Fixed, result.Report.Outcome);

        var rejected = result.Report.Attempts[0];
        Assert.False(rejected.Applied);
        Assert.Contains("not found", rejected.RejectionReason);
        Assert.Null(rejected.Patch);
        Assert.Null(rejected.Validation);

        var feedback = Assert.Single(agent.Requests[1].PreviousAttempts);
        Assert.Contains("Rejected", feedback.Outcome);
        Assert.Contains("not found", feedback.Detail);
    }

    [Theory]
    [InlineData(RemediationDecision.CannotFix, RemediationOutcome.CannotFix)]
    [InlineData(RemediationDecision.NeedsHumanDecision, RemediationOutcome.NeedsHumanDecision)]
    public async Task RunAsync_AgentDeclines_StopsWithoutChangingAnything(
        RemediationDecision decision,
        RemediationOutcome expected)
    {
        var proposal = Fix("x", "y") with { Decision = decision, Edits = [], Options = ["Option A", "Option B"] };
        var maven = new FakeMaven();

        var result = await CreateLoop(new FakeAgent(proposal), maven).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(expected, result.Report.Outcome);
        Assert.False(result.FinalValidation.Passed);
        Assert.Empty(maven.Calls);
        Assert.Empty(result.AttemptPatches);
        Assert.Equal(["Option A", "Option B"], result.Report.Attempts.Single().Proposal!.Options);
    }

    [Fact]
    public async Task RunAsync_SameFailureAfterAFix_StopsWithNoProgress()
    {
        var agent = new FakeAgent(
            Fix(" extends WebSecurityConfigurerAdapter", ""),
            Fix("class SecurityConfiguration {}", "class SecurityConfiguration { }"));

        var maven = new FakeMaven(Failed(CompileError));

        var result = await CreateLoop(agent, maven).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.NoProgress, result.Report.Outcome);
        Assert.Single(result.Report.Attempts);
        Assert.Single(agent.Requests);
    }

    [Fact]
    public async Task RunAsync_EveryAttemptFails_StopsAtTheLimit()
    {
        var agent = new FakeAgent(
            Fix("class Missing1", "x"),
            Fix("class Missing2", "x"),
            Fix("class Missing3", "x"),
            Fix("class Missing4", "x"));

        var result = await CreateLoop(agent, new FakeMaven(), maxAttempts: 3).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.AttemptsExhausted, result.Report.Outcome);
        Assert.Equal(3, result.Report.Attempts.Count);
        Assert.Equal(3, agent.Requests.Count);
        Assert.False(result.FinalValidation.Passed);
    }

    [Fact]
    public async Task RunAsync_InvalidProposal_CountsAsAFailedAttempt()
    {
        var agent = new FakeAgent(
            new InvalidRemediationProposalException("A Fix proposal has no edits."),
            Fix(" extends WebSecurityConfigurerAdapter", ""));

        var result = await CreateLoop(agent, new FakeMaven(Passed(), Passed(), Passed())).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.Fixed, result.Report.Outcome);
        Assert.Equal("A Fix proposal has no edits.", result.Report.Attempts[0].Error);
        Assert.Contains("no edits", agent.Requests[1].PreviousAttempts.Single().Detail);
    }

    [Fact]
    public async Task RunAsync_AgentInfrastructureFails_StopsWithErrorAndDeterministicResultStands()
    {
        var agent = new FakeAgent(new InvalidOperationException("Foundry returned 401."));

        var result = await CreateLoop(agent, new FakeMaven()).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile());

        Assert.Equal(RemediationOutcome.Error, result.Report.Outcome);
        Assert.Equal("Foundry returned 401.", result.Report.Attempts.Single().Error);
        Assert.Equal("compile", result.FinalValidation.Checks.Single().Check);
    }

    [Fact]
    public async Task RunAsync_ValidationAlreadyPassed_ReportsNotNeeded()
    {
        var agent = new FakeAgent();

        var passed = new TransformationValidationResult(
            "orders-service", CommitSha, TransformationValidationStatus.Passed, []);

        var result = await CreateLoop(agent, new FakeMaven()).RunAsync(
            CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, passed);

        Assert.Equal(RemediationOutcome.NotNeeded, result.Report.Outcome);
        Assert.Empty(agent.Requests);
    }

    [Fact]
    public async Task RunAsync_Cancelled_Propagates()
    {
        var agent = new FakeAgent(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateLoop(agent, new FakeMaven()).RunAsync(
                CreatePlan(), CreateWorkspace(), "tree-openrewrite", OpenRewritePatchPath, FailedCompile()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static RemediationLoop CreateLoop(
        FakeAgent agent,
        FakeMaven maven,
        FakeRecorder? recorder = null,
        int maxAttempts = 3) =>
        new(
            agent,
            new RemediationPatchApplier(),
            recorder ?? new FakeRecorder(),
            new TransformationValidator(maven),
            maxAttempts);

    private RepositoryWorkspace CreateWorkspace() =>
        new("orders-service", Repository, CommitSha);

    private static TransformationPlan CreatePlan() =>
        new(
            RepositoryName: "orders-service",
            SourceCommitSha: CommitSha,
            MigrationWave: 1,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget(SpringBootVersion: "3.5.x", JavaVersion: "21"),
            OpenRewrite: [],
            Validation: ["compile", "unit-tests", "application-context-test"]);

    private static TransformationValidationResult FailedCompile() =>
        new(
            "orders-service",
            CommitSha,
            TransformationValidationStatus.Failed,
            [new TransformationValidationCheckResult("compile", false, CompileError, null)]);

    private static MavenProcessResult Passed() => new(0, string.Empty, string.Empty);

    private static MavenProcessResult Failed(string output) => new(1, output, string.Empty);

    private static RemediationProposal Fix(
        string search,
        string replace) =>
        new(
            Decision: RemediationDecision.Fix,
            Diagnosis: "Still extends the removed adapter.",
            RootCause: "WebSecurityConfigurerAdapter was removed.",
            Edits: [new RemediationEdit(SecurityPath, search, replace, "Use a SecurityFilterChain bean.")],
            Options: []);

    private string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Repository, relativePath));

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(Repository, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class FakeAgent(params object[] responses) : IRemediationAgent
    {
        private readonly Queue<object> _responses = new(responses);

        public List<RemediationRequest> Requests { get; } = [];

        public List<string> ToolNames { get; } = [];

        public Task<RemediationAgentResult> ProposeAsync(
            RemediationRequest request,
            IList<AITool> tools,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ToolNames.AddRange(tools.Select(tool => tool.Name).Where(name => name == "get_openrewrite_patch"));

            var next = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("No fake agent response configured.");

            return next switch
            {
                Exception exception => throw exception,
                RemediationProposal proposal => Task.FromResult(new RemediationAgentResult(proposal, 1000, 200)),
                _ => throw new InvalidOperationException()
            };
        }
    }

    private sealed class FakeRecorder : IWorkspacePatchRecorder
    {
        public List<(string BaseRevision, string Stage)> Calls { get; } = [];

        public Task<RecordedPatch> RecordAsync(
            string repositoryPath,
            string repositoryName,
            string baseRevision,
            string stage,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((baseRevision, stage));

            return Task.FromResult(new RecordedPatch(
                $"tree-{Calls.Count}",
                new PatchArtifact(stage, $"patches/{repositoryName}/{stage}.patch", "hash", [SecurityPath]),
                $"/artifacts/{stage}.patch"));
        }
    }

    private sealed class FakeMaven(params MavenProcessResult[] results) : IMavenProcessRunner
    {
        private readonly Queue<MavenProcessResult> _results = new(results);

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);

            return Task.FromResult(
                _results.Count > 0
                    ? _results.Dequeue()
                    : throw new InvalidOperationException("No fake Maven result configured."));
        }
    }
}
