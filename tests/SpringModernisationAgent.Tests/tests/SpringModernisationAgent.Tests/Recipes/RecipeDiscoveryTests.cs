using System.IO.Compression;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Recipes;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Recipes;

public class RecipeDiscoveryTests : IDisposable
{
    private const string Sha = "ce422587799bb8c49d2724f1e963e456b7e977d6";

    // Probe #5's T1 errors, as Maven prints them from a pipeline workspace.
    private const string Before =
        "[ERROR] /w/orders-service-0123456789abcdef0123456789abcdef/src/main/java/com/lab/orders/domain/CustomerOrder.java:[25,24] incompatible types: java.lang.Class<com.lab.orders.persistence.MoneyUserType> cannot be converted to java.lang.Class<? extends org.hibernate.usertype.UserType<?>>\n" +
        "[ERROR] /w/orders-service-0123456789abcdef0123456789abcdef/src/main/java/com/lab/orders/persistence/MoneyUserType.java:[21,5] method does not override or implement a method from a supertype\n";

    private readonly string _root = Directory.CreateTempSubdirectory("discovery-").FullName;

    private string Workspace => Path.Combine(_root, "orders-service-0123456789abcdef0123456789abcdef");

    public RecipeDiscoveryTests()
    {
        Directory.CreateDirectory(Path.Combine(Workspace, "src", "main"));
        File.WriteAllText(Path.Combine(Workspace, "src", "main", "A.java"), "class A {}");
        Directory.CreateDirectory(Path.Combine(Workspace, "target", "classes"));
        File.WriteAllText(Path.Combine(Workspace, "target", "classes", "A.class"), "bytes");
    }

    [Fact]
    public async Task DiscoverAsync_ACandidateThatRemovesErrorsAndAddsNone_IsSuitable()
    {
        // After the candidate only the MoneyUserType error remains, on a shifted line.
        var after = "[ERROR] /w/orders-service-candidate-fedcba9876543210fedcba9876543210/src/main/java/com/lab/orders/persistence/MoneyUserType.java:[30,5] method does not override or implement a method from a supertype\n";
        var runner = new FakeOpenRewrite(succeeds: true);
        var maven = new FakeMaven(new MavenProcessResult(1, after, string.Empty));

        var report = (await Discovery(runner, maven).DiscoverAsync(Plan(), Workspace, FailedCompile()))!;

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal("lab.hibernate.MigrateUserType", suggestion.Recipe);
        Assert.Equal(RecipeSuggestionVerdict.Suitable, suggestion.Verdict);
        Assert.Equal(["CustomerOrder.java: incompatible types: java.lang.Class<com.lab.orders.persistence.MoneyUserType> cannot be converted to java.lang.Class<? extends org.hibernate.usertype.UserType<?>>"], suggestion.Removed);
        Assert.Empty(suggestion.Added);
        Assert.Equal("lab:hibernate:2.25.0", suggestion.Coordinates);

        // The candidate ran on a copy, with its module's coordinates, never on the workspace.
        var plan = Assert.Single(runner.Plans);
        Assert.NotEqual(Workspace, plan.RepositoryPath);
        Assert.Equal(new OpenRewriteExecutionRecipe("lab.hibernate.MigrateUserType", "lab:hibernate", "2.25.0"), Assert.Single(plan.Recipes));
        Assert.Equal(["--batch-mode", "compile"], Assert.Single(maven.Calls).Arguments);
        Assert.Equal(plan.RepositoryPath, maven.Calls[0].WorkingDirectory);
    }

    [Fact]
    public async Task DiscoverAsync_ACandidateThatAddsErrors_IsUnsuitableAndSaysWhat()
    {
        // Probe #6's shape: one error fixed, a new one introduced.
        var after =
            "[ERROR] /c/src/main/java/com/lab/orders/persistence/MoneyUserType.java:[21,5] method does not override or implement a method from a supertype\n" +
            "[ERROR] /c/src/main/java/com/lab/orders/persistence/MoneyUserType.java:[42,12] cannot find symbol\n" +
            "  symbol:   class BigDecimal\n";

        var report = (await Discovery(new FakeOpenRewrite(true), new FakeMaven(new MavenProcessResult(1, after, ""))).DiscoverAsync(Plan(), Workspace, FailedCompile()))!;

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(RecipeSuggestionVerdict.Unsuitable, suggestion.Verdict);
        Assert.Single(suggestion.Removed);
        Assert.Equal(["MoneyUserType.java: cannot find symbol | symbol: class BigDecimal"], suggestion.Added);
    }

    [Fact]
    public async Task DiscoverAsync_ACandidateThatFailsToRun_IsUnverifiedAndCompileIsSkipped()
    {
        var maven = new FakeMaven();

        var report = (await Discovery(new FakeOpenRewrite(false), maven).DiscoverAsync(Plan(), Workspace, FailedCompile()))!;

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(RecipeSuggestionVerdict.Unverified, suggestion.Verdict);
        Assert.StartsWith("failed to run", suggestion.Note);
        Assert.Empty(maven.Calls);
    }

    [Fact]
    public async Task DiscoverAsync_ABuildThatFailsWithoutSourceErrors_IsUnverifiedNotSuitable()
    {
        // For example the candidate broke pom.xml: no file errors to compare.
        var broken = new MavenProcessResult(1, "[ERROR] Non-resolvable parent POM for lab:orders-service", "");

        var report = (await Discovery(new FakeOpenRewrite(true), new FakeMaven(broken)).DiscoverAsync(Plan(), Workspace, FailedCompile()))!;

        var suggestion = Assert.Single(report.Suggestions);
        Assert.Equal(RecipeSuggestionVerdict.Unverified, suggestion.Verdict);
        Assert.Contains("failed without source errors", suggestion.Note);
        Assert.Contains("Non-resolvable parent POM", suggestion.Note);
    }

    [Fact]
    public async Task DiscoverAsync_AFailureOutsideCompile_IsMatchedButNotMeasured()
    {
        var runner = new FakeOpenRewrite(true);
        var validation = FailedCompile() with
        {
            Checks = [new TransformationValidationCheckResult("unit-tests", false, Before, null)]
        };

        var report = (await Discovery(runner, new FakeMaven()).DiscoverAsync(Plan(), Workspace, validation))!;

        Assert.Equal(RecipeSuggestionVerdict.Unverified, Assert.Single(report.Suggestions).Verdict);
        Assert.Contains("only for compile errors", report.Suggestions[0].Note);
        Assert.Empty(runner.Plans);
    }

    [Fact]
    public async Task DiscoverAsync_ARecipeTheRouteRuns_IsReportedAsAlreadyRunAndNeverMeasured()
    {
        var runner = new FakeOpenRewrite(true);
        var plan = Plan() with
        {
            OpenRewrite = [new OpenRewriteTransformation("lab.hibernate.MigrateUserType", "lab:hibernate", "2.25.0")]
        };

        var report = (await Discovery(runner, new FakeMaven()).DiscoverAsync(plan, Workspace, FailedCompile()))!;

        Assert.Equal(RecipeSuggestionVerdict.AlreadyRun, Assert.Single(report.Suggestions).Verdict);
        Assert.Empty(runner.Plans);
    }

    [Fact]
    public async Task DiscoverAsync_LeavesTheWorkspaceUntouchedAndRemovesItsCopy()
    {
        var runner = new FakeOpenRewrite(true, edit: "class A { int changed; }");

        await Discovery(runner, new FakeMaven(new MavenProcessResult(0, "", ""))).DiscoverAsync(Plan(), Workspace, FailedCompile());

        Assert.Equal("class A {}", File.ReadAllText(Path.Combine(Workspace, "src", "main", "A.java")));
        Assert.False(runner.SawTarget);
        Assert.Equal([Workspace], Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task DiscoverAsync_PassedValidation_ReturnsNull()
    {
        var passed = FailedCompile() with
        {
            Status = TransformationValidationStatus.Passed,
            Checks = [new TransformationValidationCheckResult("compile", true, null, null)]
        };

        Assert.Null(await Discovery(new FakeOpenRewrite(true), new FakeMaven()).DiscoverAsync(Plan(), Workspace, passed));
    }

    [Fact]
    public async Task DiscoverAsync_AnIndexThatCannotLoad_IsRecordedNotThrown()
    {
        var discovery = new RecipeDiscovery(new BrokenCatalog(), new FakeOpenRewrite(true), new FakeMaven());

        var report = (await discovery.DiscoverAsync(Plan(), Workspace, FailedCompile()))!;

        Assert.Equal("Maven Central unreachable", report.Error);
        Assert.Empty(report.Suggestions);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private RecipeDiscovery Discovery(
        FakeOpenRewrite runner,
        FakeMaven maven) =>
        new(new FixedCatalog(Catalog()), runner, maven);

    private RecipeCatalog Catalog()
    {
        var path = Path.Combine(Path.GetTempPath(), $"discovery-{Guid.NewGuid():N}.jar");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var csv = new StreamWriter(archive.CreateEntry("META-INF/rewrite/recipes.csv").Open()))
        {
            csv.WriteLine("ecosystem,packageName,name,displayName,description");
            csv.WriteLine("maven,lab:hibernate,lab.hibernate.MigrateUserType,Migrate `UserType` to Hibernate 6,x");
        }

        try
        {
            return RecipeCatalog.FromJars([(new RecipeModule("lab", "hibernate", "2.25.0", RecipeModuleRole.Candidate, "8.89.0"), path)]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TransformationPlan Plan() =>
        new(
            RepositoryName: "orders-service",
            SourceCommitSha: Sha,
            MigrationWave: 1,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget("3.5.x", "21"),
            OpenRewrite: [new OpenRewriteTransformation("lab.boot.Upgrade", "lab:spring", "6.37.1")],
            Validation: ["compile"]);

    private static TransformationValidationResult FailedCompile() =>
        new(
            "orders-service",
            Sha,
            TransformationValidationStatus.Failed,
            [new TransformationValidationCheckResult("compile", false, Before, null)]);

    private sealed class FixedCatalog(RecipeCatalog catalog) : IRecipeCatalogProvider
    {
        public Task<RecipeCatalog> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(catalog);
    }

    private sealed class BrokenCatalog : IRecipeCatalogProvider
    {
        public Task<RecipeCatalog> GetAsync(CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Maven Central unreachable");
    }

    private sealed class FakeOpenRewrite(bool succeeds, string? edit = null) : IOpenRewriteRunner
    {
        public List<OpenRewriteExecutionPlan> Plans { get; } = [];

        public bool SawTarget { get; private set; }

        public Task<TransformationResult> RunAsync(OpenRewriteExecutionPlan executionPlan, CancellationToken cancellationToken = default)
        {
            Plans.Add(executionPlan);
            SawTarget = Directory.Exists(Path.Combine(executionPlan.RepositoryPath, "target"));

            if (edit is not null)
            {
                File.WriteAllText(Path.Combine(executionPlan.RepositoryPath, "src", "main", "A.java"), edit);
            }

            return Task.FromResult(new TransformationResult(
                executionPlan.RepositoryName,
                executionPlan.SourceCommitSha,
                succeeds ? TransformationStatus.Succeeded : TransformationStatus.Failed,
                [],
                [],
                succeeds ? "ok" : "[ERROR] Recipe validation error: requires a newer OpenRewrite",
                null));
        }
    }

    private sealed class FakeMaven(params MavenProcessResult[] results) : IMavenProcessRunner
    {
        private readonly Queue<MavenProcessResult> _results = new(results);

        public List<(string WorkingDirectory, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<MavenProcessResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add((workingDirectory, arguments.ToList()));
            return Task.FromResult(_results.Dequeue());
        }
    }
}
