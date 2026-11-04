using SpringModernisationAgent.Models;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Tests.Remediation;

public class UnsupportedLibrariesTests : IDisposable
{
    // A library that exists only in this YAML, never in code.
    private const string WidgetsYaml =
        """
        libraries:
          - name: "Legacy Widgets"
            package: "com.legacywidgets"
            pomMarkers:
              - "<artifactId>legacy-widgets</artifactId>"
            replacements: "Modern Widgets"
            recommended: "Modern Widgets 2"
            manualSteps:
              - "`pom.xml`: replace `legacy-widgets` with `modern-widgets`."
            alternatives: []
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("unsupported-").FullName;

    [Fact]
    public void ShippedCatalogue_LoadsFromConfigWithEveryEntryComplete()
    {
        Assert.Equal(
            ["Ehcache 2", "Keycloak Spring adapter", "Springfox"],
            UnsupportedLibraries.All.Select(library => library.Name));
    }

    [Fact]
    public void FindIn_ADependencyInThePomOrAnImportInMainCode_IsFound()
    {
        Write("pom.xml", "<dependency><groupId>net.sf.ehcache</groupId><artifactId>ehcache</artifactId></dependency>");
        Write("api/src/main/java/a/Docs.java", "import springfox.documentation.spring.web.plugins.Docket;\nclass Docs {}\n");

        var found = UnsupportedLibraries.FindIn(_root).Select(library => library.Name);

        Assert.Equal(["Ehcache 2", "Springfox"], found);
    }

    [Fact]
    public void FindIn_TestCodeBuildOutputAndOtherLibraries_AreIgnored()
    {
        Write("pom.xml", "<dependency><groupId>org.ehcache</groupId><artifactId>ehcache</artifactId></dependency>");
        Write("src/test/java/a/T.java", "import net.sf.ehcache.Element;\n");
        Write("target/classes/pom.xml", "<groupId>io.springfox</groupId>");

        Assert.Empty(UnsupportedLibraries.FindIn(_root));
    }

    [Fact]
    public void ALibraryAddedOnlyInYaml_IsDetectedEnforcedAndGivenManualStepsInThePullRequest()
    {
        var catalog = UnsupportedLibraryCatalog.Parse(WidgetsYaml);

        // Detected.
        Write("pom.xml", "<dependency><artifactId>legacy-widgets</artifactId></dependency>");
        var found = Assert.Single(catalog.FindIn(_root));
        Assert.Equal("Legacy Widgets", found.Name);

        // Enforced by the guardrail.
        Write("src/main/java/a/A.java", "class A {\n    void run() {}\n}\n");
        var applied = new RemediationPatchApplier(catalog).Apply(
            _root,
            [new RemediationEdit("src/main/java/a/A.java", "    void run() {}\n", "    void run() { com.legacywidgets.Widget.make(); }\n", "x")]);

        Assert.False(applied.Applied);
        Assert.Contains("uses Legacy Widgets (com.legacywidgets)", applied.RejectionReason);
        Assert.Contains("(Modern Widgets)", applied.RejectionReason);

        // Shown in the pull request.
        var body = PullRequestContentBuilder.Build(
            Plan(),
            PassedResult() with { UnsupportedLibraries = ["Legacy Widgets"] },
            [],
            null,
            catalog)!.Body;

        Assert.Contains("This repository still declares Legacy Widgets", body);
        Assert.Contains("### Legacy Widgets (`com.legacywidgets`)", body);
        Assert.Contains("1. `pom.xml`: replace `legacy-widgets` with `modern-widgets`.", body);
    }

    [Fact]
    public void Parse_AnUnknownField_Fails()
    {
        // A misspelt field must not silently switch a rule off.
        var exception = Assert.Throws<InvalidOperationException>(
            () => UnsupportedLibraryCatalog.Parse(WidgetsYaml + "\n    severity: high\n", "test.yaml"));

        Assert.Contains("test.yaml", exception.Message);
        Assert.Contains("severity", exception.Message);
    }

    [Theory]
    [InlineData("manualSteps:\n      - \"`pom.xml`: replace `legacy-widgets` with `modern-widgets`.\"", "manualSteps: []", "needs at least one manualSteps entry")]
    [InlineData("package: \"com.legacywidgets\"", "package: \"Legacy Widgets\"", "needs a package")]
    [InlineData("recommended: \"Modern Widgets 2\"", "recommended: \"\"", "needs recommended")]
    public void Parse_IncompleteEntries_FailNamingTheEntryAndTheProblem(
        string replace,
        string with,
        string problem)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => UnsupportedLibraryCatalog.Parse(WidgetsYaml.Replace(replace, with), "test.yaml"));

        Assert.Contains("test.yaml: library 1 ('Legacy Widgets')", exception.Message);
        Assert.Contains(problem, exception.Message);
    }

    [Fact]
    public void Parse_DuplicateLibraries_Fail()
    {
        var twice = WidgetsYaml + "\n" + WidgetsYaml["libraries:\n".Length..];

        var exception = Assert.Throws<InvalidOperationException>(() => UnsupportedLibraryCatalog.Parse(twice));

        Assert.Contains("library 2", exception.Message);
        Assert.Contains("duplicates another library's name", exception.Message);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TransformationPlan Plan() =>
        new(
            RepositoryName: "widgets-service",
            SourceCommitSha: "b39866542351f9c79f05df7558e84aa9799ef33f",
            MigrationWave: 1,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget("3.5.x", "21"),
            OpenRewrite: [],
            Validation: ["compile"]);

    private static TransformationExecutionResult PassedResult() =>
        new(
            new TransformationResult(
                "widgets-service", "b39866542351f9c79f05df7558e84aa9799ef33f",
                TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
            new TransformationValidationResult(
                "widgets-service", "b39866542351f9c79f05df7558e84aa9799ef33f",
                TransformationValidationStatus.Passed,
                [new TransformationValidationCheckResult("compile", true, null, null)]))
        {
            Patches = [new PatchArtifact("cumulative", "patches/widgets-service/cumulative.patch", "abc", ["pom.xml"])]
        };

    private void Write(
        string relativePath,
        string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
