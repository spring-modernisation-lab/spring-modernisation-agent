using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

public class PortPlannerTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    // Probe #5 / run 26: what OpenRewrite leaves in orders-service.
    private static readonly IReadOnlyList<ClassifiedProblem> AfterOpenRewrite =
    [
        ApiMove("P1", "src/main/java/a/config/SecurityConfiguration.java", "M-SECURITY-6-FILTERCHAIN"),
        ApiMove("P2", "src/main/java/a/domain/CustomerOrder.java", "M-HIBERNATE-6-USERTYPE"),
        ApiMove("P3", "src/main/java/a/config/InventoryClientConfiguration.java", "M-HTTPCLIENT-5-POOL"),
        ApiMove("P4", "src/main/java/a/persistence/MoneyUserType.java", "M-HIBERNATE-6-USERTYPE")
    ];

    [Fact]
    public void Plan_OneTaskPerMapping_InOrderOfFirstAppearance()
    {
        var plan = PortPlanner.Plan(AfterOpenRewrite, Knowledge, attempted: []);

        Assert.Equal(
            ["M-SECURITY-6-FILTERCHAIN", "M-HIBERNATE-6-USERTYPE", "M-HTTPCLIENT-5-POOL"],
            plan.Tasks.Select(task => task.Mapping.Id));
        Assert.Empty(plan.Unworkable);
    }

    [Fact]
    public void Plan_ATaskCoversEveryFileOfItsMapping_WithTheirEvidence()
    {
        var hibernate = PortPlanner.Plan(AfterOpenRewrite, Knowledge, attempted: []).Tasks[1];

        Assert.Equal(
            ["src/main/java/a/domain/CustomerOrder.java", "src/main/java/a/persistence/MoneyUserType.java"],
            hibernate.Files);
        Assert.Equal(["P2", "P4"], hibernate.Problems.Select(problem => problem.Id));
        Assert.Equal(
            ["error in src/main/java/a/domain/CustomerOrder.java", "error in src/main/java/a/persistence/MoneyUserType.java"],
            hibernate.Evidence);
    }

    [Fact]
    public void Plan_AMappingAlreadyAttemptedIsNeverPlannedAgain()
    {
        var plan = PortPlanner.Plan(AfterOpenRewrite, Knowledge, attempted: ["M-SECURITY-6-FILTERCHAIN"]);

        Assert.DoesNotContain(plan.Tasks, task => task.Mapping.Id == "M-SECURITY-6-FILTERCHAIN");
        var left = Assert.Single(plan.Unworkable);
        Assert.Equal("P1", left.Problem.Id);
        Assert.Contains("already attempted", left.Reason);
    }

    [Fact]
    public void Plan_OtherKindsNeverCreateATask()
    {
        ClassifiedProblem[] problems =
        [
            new("P1", ProblemKind.PlatformDropped, "a.InventoryCachingTests.cached", "S-EHCACHE-2", null, null, "Ehcache 2", ["x"]),
            new("P2", ProblemKind.Contract, "a.OrderControllerTests.trailingSlash", "S-HTTP-CONTRACT", null, null, null, ["x"]),
            new("P3", ProblemKind.Mechanical, "a.OrderControllerTests.servlet", "S-JAVAX-SERVLET-STRING", null, "lab.jakarta.ServletStringLiterals", null, ["x"]),
            new("P4", ProblemKind.KnockOn, "a.OrderControllerTests.anonymous", "S-CONTEXT-KNOCK-ON", null, null, null, ["x"]),
            new("P5", ProblemKind.Unknown, "build", null, null, null, null, ["x"])
        ];

        var plan = PortPlanner.Plan(problems, Knowledge, attempted: []);

        Assert.Empty(plan.Tasks);
        // Knock-ons are re-checked after the others; the rest go to a person.
        Assert.Equal(["P1", "P2", "P3", "P5"], plan.Unworkable.Select(left => left.Problem.Id));
    }

    [Fact]
    public void Plan_AnApiMoveInTestCodeOrWithNoSourceFile_IsForAPerson()
    {
        ClassifiedProblem[] problems =
        [
            ApiMove("P1", "src/test/java/a/config/SecurityTests.java", "M-SECURITY-6-FILTERCHAIN"),
            ApiMove("P2", "a.web.OrderControllerTests.listsOrders", "M-HTTPCLIENT-5-POOL")
        ];

        var plan = PortPlanner.Plan(problems, Knowledge, attempted: []);

        Assert.Empty(plan.Tasks);
        Assert.Contains("test code", plan.Unworkable[0].Reason);
        Assert.Contains("no production source file", plan.Unworkable[1].Reason);
    }

    [Fact]
    public void Plan_ATaskIsObligedByItsMapping()
    {
        var http = PortPlanner.Plan(AfterOpenRewrite, Knowledge, attempted: []).Tasks[2];

        Assert.True(http.Obliges("B-BEAN-default-HttpClient.connectionTimeToLive"));
        Assert.False(http.Obliges("B-SEC-default-GET /api/orders-VIEWER"));
    }

    private static ClassifiedProblem ApiMove(
        string id,
        string subject,
        string mapping) =>
        new(id, ProblemKind.ApiMove, subject, "S-TEST", mapping, null, null, [$"error in {subject}"]);
}
