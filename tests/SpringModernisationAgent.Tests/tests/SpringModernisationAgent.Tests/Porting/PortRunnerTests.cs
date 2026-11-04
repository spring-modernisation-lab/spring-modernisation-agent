using Microsoft.Extensions.AI;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Triage;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Porting;

/// <summary>
/// The runner against a real git workspace (so reverts are proven on disk),
/// with the AI and Maven faked. Shapes follow run 26.
/// </summary>
public class PortRunnerTests : IDisposable
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Security = "src/main/java/a/config/SecurityConfiguration.java";
    private const string Order = "src/main/java/a/domain/CustomerOrder.java";
    private const string Http = "src/main/java/a/config/InventoryClientConfiguration.java";
    private const string Money = "src/main/java/a/persistence/MoneyUserType.java";

    private static readonly Dictionary<string, string[]> Errors = new()
    {
        [Security] = ["[ERROR] /w/r/" + Security + ":[12,72] cannot find symbol", "  symbol:   class WebSecurityConfigurerAdapter"],
        [Order] = ["[ERROR] /w/r/" + Order + ":[25,24] incompatible types: cannot be converted to java.lang.Class<? extends org.hibernate.usertype.UserType<?>>"],
        [Http] = ["[ERROR] /w/r/" + Http + ":[28,17] cannot find symbol", "  symbol:   method setMaxConnTotal(int)", "  location: class org.apache.hc.client5.http.impl.classic.HttpClientBuilder"],
        [Money] = ["[ERROR] /w/r/" + Money + ":[17,8] a.MoneyUserType is not abstract and does not override abstract method nullSafeGet(java.sql.ResultSet,int) in org.hibernate.usertype.UserType"]
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"port-runner-{Guid.NewGuid():N}");
    private readonly GitProcessRunner _git = new();

    private string Repository => Path.Combine(_root, "orders-service");

    [Fact]
    public async Task RunAsync_Run26Shape_ThreeScopedTasksEachKept()
    {
        var input = await SetUpAsync(Security, Order, Http, Money);
        var agent = new FakeAgent(Fix(Security), FixAll(Order, Money), Fix(Http));
        var validator = new FakeValidator(CompileFails(Order, Http, Money), CompileFails(Http), Passes());

        var result = await Runner(agent, validator).RunAsync(input);

        Assert.Equal(RemediationOutcome.Fixed, result.Report.Outcome);
        Assert.Equal(
            [("M-SECURITY-6-FILTERCHAIN", PortTaskStatus.Accepted), ("M-HIBERNATE-6-USERTYPE", PortTaskStatus.Accepted), ("M-HTTPCLIENT-5-POOL", PortTaskStatus.Accepted)],
            result.Report.Tasks.Select(task => (task.Mapping, task.Status)));
        Assert.Equal([Order, Money], result.Report.Tasks[1].Files);

        // Each request is the task alone.
        Assert.Equal([[Security], [Order, Money], [Http]], agent.Requests.Select(request => request.Task.Files));
        Assert.Equal(["M-SECURITY-6-FILTERCHAIN", "M-HIBERNATE-6-USERTYPE", "M-HTTPCLIENT-5-POOL"], result.Report.Attempts.Select(attempt => attempt.Task));
        Assert.Equal(3, result.AttemptPatches.Count);
        Assert.True(result.FinalValidation.Passed);
    }

    [Fact]
    public async Task RunAsync_AnAttemptThatLeavesItsErrors_IsRevertedAndRepairedWithTheExactFailure()
    {
        var input = await SetUpAsync(Http);
        var agent = new FakeAgent(Fix(Http, "first"), Fix(Http, "repair"));
        var validator = new FakeValidator(CompileFails(Http), Passes());

        var result = await Runner(agent, validator).RunAsync(input);

        var task = Assert.Single(result.Report.Tasks);
        Assert.Equal(PortTaskStatus.Accepted, task.Status);
        Assert.Equal([1, 2], task.Attempts);
        Assert.Contains("still fails", result.Report.Attempts[0].NotKept);
        Assert.Null(result.Report.Attempts[1].NotKept);

        var repair = agent.Requests[1].Repair!;
        Assert.Contains("still fails", repair.Outcome);
        Assert.Contains(repair.Failure, line => line.Contains("setMaxConnTotal", StringComparison.Ordinal));
        Assert.Equal("class InventoryClientConfiguration { /* repair */ }\n", Read(Http));
    }

    [Fact]
    public async Task RunAsync_NotKeptTwice_IsRevertedToTheRecipesOutput_AndTheNextTaskStillRuns()
    {
        var input = await SetUpAsync(Http, Security);
        var agent = new FakeAgent(Fix(Http, "first"), Fix(Http, "second"), Fix(Security));
        var validator = new FakeValidator(CompileFails(Http, Security), CompileFails(Http, Security), CompileFails(Http));

        var result = await Runner(agent, validator).RunAsync(input);

        Assert.Equal(
            [("M-HTTPCLIENT-5-POOL", PortTaskStatus.Reverted), ("M-SECURITY-6-FILTERCHAIN", PortTaskStatus.Accepted)],
            result.Report.Tasks.Select(task => (task.Mapping, task.Status)));
        Assert.Equal("class InventoryClientConfiguration { /* recipes */ }\n", Read(Http));
        Assert.Equal(RemediationOutcome.NeedsHumanDecision, result.Report.Outcome);
        Assert.Contains(result.Report.LeftForAPerson, left => left.Contains("M-HTTPCLIENT-5-POOL was already attempted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_CannotFix_GetsNoRepair()
    {
        var input = await SetUpAsync(Http);
        var agent = new FakeAgent(new RemediationProposal(RemediationDecision.CannotFix, "the TTL has no equivalent here", "r", [], ["a person ports it"]));

        var result = await Runner(agent, new FakeValidator()).RunAsync(input);

        var task = Assert.Single(result.Report.Tasks);
        Assert.Equal(PortTaskStatus.CannotFix, task.Status);
        Assert.Contains("the TTL has no equivalent here", task.Reason);
        Assert.Single(agent.Requests);
    }

    [Fact]
    public async Task RunAsync_AnEditOutsideTheTask_IsRejectedAndRepaired()
    {
        var input = await SetUpAsync(Http);
        var outside = new RemediationProposal(RemediationDecision.Fix, "d", "r",
            [new RemediationEdit("src/main/resources/application.yml", "", "spring:\n  mvc: {}\n", "path matching")], []);
        var agent = new FakeAgent(outside, Fix(Http));

        var result = await Runner(agent, new FakeValidator(Passes())).RunAsync(input);

        Assert.Contains("this task may change only", result.Report.Attempts[0].RejectionReason);
        Assert.False(File.Exists(Path.Combine(Repository, "src", "main", "resources", "application.yml")));
        Assert.Equal(PortTaskStatus.Accepted, result.Report.Tasks[0].Status);
    }

    [Fact]
    public async Task RunAsync_ContractGate_ATtlLostTwiceIsReverted_TheTrailingSlashIsNotBlamed()
    {
        var input = await SetUpAsync(Security, Http);
        var baseline = Record(
            ("B-CTX-default", "starts"),
            ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "200"));
        var lost = Record(
            ("B-CTX-default", "starts"),
            ("B-BEAN-default-HttpClient.connectionTimeToLive", BehaviourRecord.Unavailable),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "404"));
        var behaviour = new FakeBehaviour(lost, lost);
        var agent = new FakeAgent(Fix(Security), Fix(Http), Fix(Http, "repair"));
        var validator = new FakeValidator(CompileFails(Http), Passes(), Passes(), CompileFails(Http));

        var result = await Runner(agent, validator, behaviour).RunAsync(input with
        {
            Baseline = new BehaviourReport(baseline, null, null, null)
        });

        var security = result.Report.Tasks[0];
        Assert.Equal(PortTaskStatus.Accepted, security.Status);
        Assert.Equal(["B-SEC-default-GET /api/orders-VIEWER"], security.ObligationsKept);
        Assert.Contains("BS-TRAILING-SLASH", Assert.Single(security.ObligationsExplained));

        var http = result.Report.Tasks[1];
        Assert.Equal(PortTaskStatus.Reverted, http.Status);
        Assert.Contains("connectionTimeToLive: 30000ms → unavailable", http.Reason);
        Assert.Equal([2, 3], http.Attempts);

        // The repair was told exactly what changed; the obligations were in the first request.
        Assert.Equal("30000ms", agent.Requests[1].Obligations["B-BEAN-default-HttpClient.connectionTimeToLive"]);
        Assert.Contains(agent.Requests[2].Repair!.Failure, line => line.Contains("connectionTimeToLive", StringComparison.Ordinal));

        Assert.Equal("class InventoryClientConfiguration { /* recipes */ }\n", Read(Http));
        Assert.Equal("class SecurityConfiguration { /* port */ }\n", Read(Security));
        Assert.EndsWith("-port-m-httpclient-5-pool-reverted", result.AttemptPatches[^1].Stage);
        Assert.Equal(2, behaviour.Calls);
        Assert.Null(result.Behaviour); // the files changed after the last comparison
        Assert.False(result.FinalValidation.Passed);
    }

    [Fact]
    public async Task RunAsync_DecisionsNeverBecomeTasks()
    {
        var input = await SetUpAsync();
        var validation = new TransformationValidationResult("orders-service", "abc", TransformationValidationStatus.Failed,
            [new TransformationValidationCheckResult("compile", true, null, null), new TransformationValidationCheckResult("unit-tests", false, null, null)]);
        ClassifiedProblem[] problems =
        [
            new("P1", ProblemKind.PlatformDropped, "a.InventoryCachingTests.cached", "S-EHCACHE-2", null, null, "Ehcache 2", ["x"]),
            new("P2", ProblemKind.Contract, "a.OrderControllerTests.trailingSlash", "S-HTTP-CONTRACT", null, null, null, ["x"])
        ];
        var agent = new FakeAgent();

        var result = await Runner(agent, new FakeValidator()).RunAsync(input with { Validation = validation, Problems = problems });

        Assert.Empty(agent.Requests);
        Assert.Equal(RemediationOutcome.NeedsHumanDecision, result.Report.Outcome);
        Assert.Equal(2, result.Report.LeftForAPerson.Count);
    }

    [Fact]
    public async Task RunAsync_TheAgentFailing_StopsTheRunAsAnError()
    {
        var input = await SetUpAsync(Http);

        var result = await Runner(new FakeAgent(new HttpRequestException("model unavailable")), new FakeValidator()).RunAsync(input);

        Assert.Equal(RemediationOutcome.Error, result.Report.Outcome);
        Assert.Equal(PortTaskStatus.Error, result.Report.Tasks[0].Status);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private PortRunner Runner(
        FakeAgent agent,
        FakeValidator validator,
        FakeBehaviour? behaviour = null)
    {
        var recorder = new GitWorkspacePatchRecorder(_git, Path.Combine(_root, "Artifacts"));

        return new PortRunner(agent, new RemediationPatchApplier(), recorder, recorder, validator, Knowledge, behaviour);
    }

    /// <summary>A workspace where the recipes left these files failing to compile.</summary>
    private async Task<PortRunInput> SetUpAsync(
        params string[] failing)
    {
        Directory.CreateDirectory(Repository);
        await GitAsync("init", "--quiet");
        await GitAsync("config", "core.autocrlf", "false");
        await GitAsync("config", "user.name", "Test");
        await GitAsync("config", "user.email", "test@example.invalid");
        await GitAsync("config", "commit.gpgsign", "false");

        foreach (var file in new[] { Security, Order, Http, Money })
        {
            Write(file, $"class {Path.GetFileNameWithoutExtension(file)} {{ /* source */ }}\n");
        }

        await GitAsync("add", "-A");
        await GitAsync("commit", "--quiet", "-m", "Initial");
        var commit = (await GitAsync("rev-parse", "HEAD")).Trim();

        foreach (var file in new[] { Security, Order, Http, Money })
        {
            Write(file, $"class {Path.GetFileNameWithoutExtension(file)} {{ /* recipes */ }}\n");
        }

        var recorder = new GitWorkspacePatchRecorder(_git, Path.Combine(_root, "Artifacts"));
        var openRewrite = await recorder.RecordAsync(Repository, "orders-service", commit, "01-openrewrite");

        var validation = CompileFails(failing);

        return new PortRunInput(
            new TransformationPlan("orders-service", commit, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
                new TransformationTarget("3.5.x", "21"), [], ["compile", "unit-tests"]),
            new RepositoryWorkspace("orders-service", Repository, commit),
            openRewrite.TreeId,
            openRewrite.FullPath,
            validation,
            validation.Passed ? [] : ProblemClassifier.Classify(Knowledge, ValidationDiagnosticsExtractor.ExtractUnbounded(validation.Checks[0]), []),
            [],
            null);
    }

    private static TransformationValidationResult CompileFails(
        params string[] files) =>
        files.Length == 0
            ? Passes()
            : new("orders-service", "abc", TransformationValidationStatus.Failed,
                [new TransformationValidationCheckResult("compile", false, string.Join("\n", files.SelectMany(file => Errors[file])), null)]);

    private static TransformationValidationResult Passes() =>
        new("orders-service", "abc", TransformationValidationStatus.Passed,
            [new TransformationValidationCheckResult("compile", true, null, null), new TransformationValidationCheckResult("unit-tests", true, null, null)]);

    private static RemediationProposal Fix(
        string file,
        string marker = "port") =>
        Fix([file], marker);

    private static RemediationProposal FixAll(
        params string[] files) =>
        Fix(files, "port");

    private static RemediationProposal Fix(
        string[] files,
        string marker) =>
        new(RemediationDecision.Fix, "d", "r",
            files.Select(file => new RemediationEdit(file, "", $"class {Path.GetFileNameWithoutExtension(file)} {{ /* {marker} */ }}\n", "port")).ToList(), []);

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", "abc", items.ToDictionary(item => item.Item, item => item.Value));

    private string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Repository, relativePath));

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(Repository, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<string> GitAsync(params string[] arguments)
    {
        var result = await _git.RunAsync(Repository, arguments);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput;
    }

    private sealed class FakeAgent(params object[] responses) : IPortAgent
    {
        private readonly Queue<object> _responses = new(responses);

        public List<PortRequest> Requests { get; } = [];

        public Task<RemediationAgentResult> ProposeAsync(
            PortRequest request,
            IList<AITool> tools,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return _responses.Dequeue() switch
            {
                Exception exception => throw exception,
                RemediationProposal proposal => Task.FromResult(new RemediationAgentResult(proposal, 1000, 200)),
                _ => throw new InvalidOperationException()
            };
        }
    }

    private sealed class FakeValidator(params TransformationValidationResult[] results) : ITransformationValidator
    {
        private readonly Queue<TransformationValidationResult> _results = new(results);

        public Task<TransformationValidationResult> ValidateAsync(
            TransformationPlan transformationPlan,
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_results.Dequeue());
    }

    private sealed class FakeBehaviour(params BehaviourRecord[] afters) : IBehaviourContractRecorder
    {
        private readonly Queue<BehaviourRecord> _afters = new(afters);

        public int Calls { get; private set; }

        public Task<BehaviourReport?> RecordBaselineAsync(
            TransformationPlan plan,
            string repositoryPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the runner never records a baseline");

        public Task<BehaviourReport> CompareAsync(
            TransformationPlan plan,
            string repositoryPath,
            BehaviourReport baseline,
            TransformationValidationResult validation,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var after = _afters.Dequeue();
            return Task.FromResult(new BehaviourReport(baseline.Baseline, after, BehaviourContract.Compare(baseline.Baseline!, after), null));
        }
    }
}
