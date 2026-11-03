using SpringModernisationAgent.AI;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Commands;

/// <summary>
/// Read-only post-migration report: combines the deterministic assessment
/// and execution results with an AI explanation of what happened. It never
/// changes repositories, plans or results.
/// </summary>
public sealed class ReportCommand
{
    public async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var artifactsDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Artifacts");

        var transformationResults =
            await new TransformationResultsReader().ReadAsync(
                Path.Combine(
                    artifactsDirectory,
                    "transformation-results.json"),
                cancellationToken);

        if (transformationResults.Count == 0)
        {
            Console.WriteLine(
                "Migration results report skipped: no transformation " +
                "results were found.");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "FOUNDRY_PROJECT_ENDPOINT")))
        {
            Console.WriteLine(
                "Migration results report skipped: " +
                "FOUNDRY_PROJECT_ENDPOINT is not configured.");

            return;
        }

        var projectAssessment =
            await new ProjectAssessmentReader().ReadAsync(
                Path.Combine(
                    artifactsDirectory,
                    "project-assessment.json"),
                cancellationToken);

        Console.WriteLine(
            $"Analysing {transformationResults.Count} repository " +
            "execution results.");

        var input = new MigrationAnalysisInputBuilder().Build(
            projectAssessment,
            transformationResults);

        var analysis =
            await MigrationAnalysisAgentFactory
                .Create()
                .AnalyzeAsync(input, cancellationToken);

        var analysisPath = Path.Combine(
            artifactsDirectory,
            "migration-results-analysis.json");

        await new MigrationAnalysisResultWriter().WriteAsync(
            analysis,
            analysisPath,
            cancellationToken);

        Console.WriteLine(
            $"Migration results analysis written to: {analysisPath}");

        var documentPath = Path.Combine(
            artifactsDirectory,
            "spring-modernisation-migration-results.docx");

        await new MigrationResultsDocumentWriter().WriteAsync(
            projectAssessment,
            transformationResults,
            analysis,
            documentPath,
            cancellationToken);

        Console.WriteLine(
            $"Migration results document written to: {documentPath}");
    }
}
