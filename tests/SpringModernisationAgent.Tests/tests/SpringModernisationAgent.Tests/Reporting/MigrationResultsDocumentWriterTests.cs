using DocumentFormat.OpenXml.Packaging;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Tests.AI;

namespace SpringModernisationAgent.Tests.Reporting;

public class MigrationResultsDocumentWriterTests : IDisposable
{
    private const string Workspace =
        "/home/runner/work/_temp/spring-modernisation-workspaces/" +
        "customer-service-0123456789abcdef0123456789abcdef";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"migration-results-document-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteAsync_FailedRepository_ContainsDeterministicFactsAndAiNarrative()
    {
        var assessment = MigrationAnalysisInputBuilderTests.CreateProjectAssessment();
        var repository = Assert.Single(assessment.Repositories);

        var text = await WriteAndReadAsync(
            assessment,
            [CreateFailedResult(repository.RepositoryName, repository.CommitSha)],
            CreateAnalysis(repository.RepositoryName));

        Assert.Contains("Spring Modernisation Migration Results", text);
        Assert.Contains(assessment.Organisation, text);

        // Deterministic evidence.
        Assert.Contains("customer-service", text);
        Assert.Contains(repository.CommitSha, text);
        Assert.Contains("src/main/java/SecurityConfiguration.java", text);
        Assert.Contains("compile: Failed", text);
        Assert.Contains(
            "[ERROR] src/main/java/SecurityConfiguration.java:[12,72] cannot find symbol",
            text);
        Assert.Contains("symbol:   class WebSecurityConfigurerAdapter", text);

        // AI narrative.
        Assert.Contains("Wave 1 partly migrated.", text);
        Assert.Contains("WebSecurityConfigurerAdapter was removed in Spring Security 6.", text);
        Assert.Contains("Replace the adapter with a SecurityFilterChain bean.", text);
        Assert.Contains("Custom Hibernate types need porting.", text);

        // Control boundaries are stated explicitly.
        Assert.Contains("AI Contribution and Control Boundaries", text);
    }

    [Fact]
    public async Task WriteAsync_NeverIncludesRawLogsOrWorkspacePaths()
    {
        var assessment = MigrationAnalysisInputBuilderTests.CreateProjectAssessment();
        var repository = Assert.Single(assessment.Repositories);

        var text = await WriteAndReadAsync(
            assessment,
            [CreateFailedResult(repository.RepositoryName, repository.CommitSha)],
            CreateAnalysis(repository.RepositoryName));

        Assert.DoesNotContain("/home/runner", text);
        Assert.DoesNotContain("spring-modernisation-workspaces", text);
        Assert.DoesNotContain("[INFO] Scanning for projects", text);
        Assert.DoesNotContain("To see the full stack trace", text);
    }

    [Fact]
    public async Task WriteAsync_RepositoryWithoutResult_IsReportedAsNotYetExecuted()
    {
        var assessment = MigrationAnalysisInputBuilderTests.CreateProjectAssessment();
        var repository = Assert.Single(assessment.Repositories);

        var text = await WriteAndReadAsync(
            assessment,
            [],
            CreateAnalysis(repository.RepositoryName));

        Assert.Contains("Not yet executed", text);
        Assert.DoesNotContain("compile: Failed", text);
    }

    [Fact]
    public async Task WriteAsync_NullArguments_Throw()
    {
        var writer = new MigrationResultsDocumentWriter();
        var assessment = MigrationAnalysisInputBuilderTests.CreateProjectAssessment();
        var analysis = CreateAnalysis("customer-service");
        var path = Path.Combine(_directory, "results.docx");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(null!, [], analysis, path));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(assessment, null!, analysis, path));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(assessment, [], null!, path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> WriteAndReadAsync(
        ProjectAssessment assessment,
        IReadOnlyList<TransformationExecutionResult> results,
        MigrationAnalysisResult analysis)
    {
        var path = Path.Combine(_directory, "results.docx");

        await new MigrationResultsDocumentWriter().WriteAsync(
            assessment,
            results,
            analysis,
            path);

        using var document = WordprocessingDocument.Open(path, false);

        // Join paragraphs with newlines so assertions match whole lines.
        return string.Join(
            "\n",
            document.MainDocumentPart!.Document.Body!
                .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                .Select(paragraph => paragraph.InnerText));
    }

    private static TransformationExecutionResult CreateFailedResult(
        string repositoryName,
        string commitSha)
    {
        var compileOutput = string.Join(
            "\n",
            "[INFO] Scanning for projects...",
            "[ERROR] COMPILATION ERROR : ",
            $"[ERROR] {Workspace}/src/main/java/SecurityConfiguration.java:[12,72] cannot find symbol",
            "  symbol:   class WebSecurityConfigurerAdapter",
            "[ERROR] Failed to execute goal compiler",
            "[ERROR] To see the full stack trace of the errors, re-run Maven with the -e switch.");

        return new TransformationExecutionResult(
            Transformation: new TransformationResult(
                RepositoryName: repositoryName,
                SourceCommitSha: commitSha,
                Status: TransformationStatus.Succeeded,
                AppliedRecipes:
                [
                    "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5"
                ],
                ChangedFiles:
                [
                    "pom.xml",
                    "src/main/java/SecurityConfiguration.java"
                ],
                StandardOutput: null,
                StandardError: null),
            Validation: new TransformationValidationResult(
                RepositoryName: repositoryName,
                SourceCommitSha: commitSha,
                Status: TransformationValidationStatus.Failed,
                Checks:
                [
                    new TransformationValidationCheckResult(
                        Check: "compile",
                        Passed: false,
                        StandardOutput: compileOutput,
                        StandardError: null)
                ]));
    }

    private static MigrationAnalysisResult CreateAnalysis(
        string repositoryName)
    {
        return new MigrationAnalysisResult(
            Strategy: new MigrationStrategy(
                Summary: "Wave 1 partly migrated.",
                Waves: [],
                KeyRisks: ["Custom Hibernate types need porting."],
                Recommendations: ["Fix compile errors before wave 2."]),
            RepositoryAnalyses:
            [
                new MigrationAnalysis(
                    RepositoryName: repositoryName,
                    Summary: "Compilation failed after OpenRewrite.",
                    Findings:
                    [
                        "WebSecurityConfigurerAdapter was removed in Spring Security 6."
                    ],
                    RecommendedActions:
                    [
                        "Replace the adapter with a SecurityFilterChain bean."
                    ])
            ]);
    }
}
