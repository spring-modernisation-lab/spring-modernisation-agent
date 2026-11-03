using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;
using static SpringModernisationAgent.Reporting.WordDocumentElements;

namespace SpringModernisationAgent.Reporting;

/// <summary>
/// Post-migration report. Repository status, changed files, validation
/// outcomes and build diagnostics come from deterministic evidence; the
/// summaries, findings, risks and recommendations come from the validated
/// AI analysis and are labelled as such.
/// </summary>
public sealed class MigrationResultsDocumentWriter
{
    public Task WriteAsync(
        ProjectAssessment projectAssessment,
        IReadOnlyList<TransformationExecutionResult> transformationResults,
        MigrationAnalysisResult migrationAnalysis,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectAssessment);
        ArgumentNullException.ThrowIfNull(transformationResults);
        ArgumentNullException.ThrowIfNull(migrationAnalysis);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var resultsByRepository = transformationResults.ToDictionary(
            result => result.Transformation.RepositoryName,
            StringComparer.OrdinalIgnoreCase);

        var analysesByRepository = migrationAnalysis.RepositoryAnalyses
            .ToDictionary(
                analysis => analysis.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        var waveByRepository = projectAssessment.MigrationWaves.Waves
            .SelectMany(wave => wave.Repositories
                .Select(repository => (repository, wave.Number)))
            .ToDictionary(
                entry => entry.repository,
                entry => entry.Number,
                StringComparer.OrdinalIgnoreCase);

        var repositories = projectAssessment.Repositories
            .OrderBy(repository =>
                waveByRepository.GetValueOrDefault(
                    repository.RepositoryName,
                    int.MaxValue))
            .ThenBy(
                repository => repository.RepositoryName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var document = WordprocessingDocument.Create(
            path,
            DocumentFormat.OpenXml.WordprocessingDocumentType.Document);

        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        AddStyles(mainPart);

        var body = mainPart.Document.Body
            ?? throw new InvalidOperationException(
                "The Word document body could not be created.");

        AddTitle(body, projectAssessment);

        AddSummary(
            body,
            repositories,
            resultsByRepository,
            migrationAnalysis);

        AddResultsTable(
            body,
            repositories,
            resultsByRepository,
            waveByRepository);

        cancellationToken.ThrowIfCancellationRequested();

        AddRepositoryDetails(
            body,
            repositories,
            resultsByRepository,
            analysesByRepository,
            waveByRepository);

        AddRisksAndRecommendations(body, migrationAnalysis);

        AddControlBoundaries(body);

        AddDocumentProperties(body);

        mainPart.Document.Save();

        return Task.CompletedTask;
    }

    private static void AddTitle(
        Body body,
        ProjectAssessment projectAssessment)
    {
        body.Append(CreateParagraph(
            "Spring Modernisation Migration Results",
            "DocumentTitle"));

        body.Append(CreateParagraph(
            $"Organisation: {projectAssessment.Organisation}",
            "DocumentSubtitle"));

        body.Append(CreateParagraph(
            $"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC",
            "DocumentSubtitle"));

        AddNote(
            body,
            "Repository status, changed files, validation results and build " +
            "diagnostics are deterministic evidence and are authoritative. " +
            "Summaries, findings, risks and recommendations are AI-generated " +
            "from that evidence and are explanatory only.");
    }

    private static void AddSummary(
        Body body,
        IReadOnlyList<RepositoryAssessment> repositories,
        IReadOnlyDictionary<string, TransformationExecutionResult> results,
        MigrationAnalysisResult migrationAnalysis)
    {
        AddHeading(body, "1. Summary", 1);

        var executed = repositories
            .Where(repository => results.ContainsKey(repository.RepositoryName))
            .Select(repository => results[repository.RepositoryName])
            .ToList();

        var succeeded = executed.Count(result => result.Succeeded);

        AddParagraph(
            body,
            $"{executed.Count} of {repositories.Count} repositories have " +
            $"been migrated and validated: {succeeded} succeeded and " +
            $"{executed.Count - succeeded} failed. " +
            $"{repositories.Count - executed.Count} are not yet executed.");

        AddHeading(body, "AI summary", 2);

        AddParagraph(body, migrationAnalysis.Strategy.Summary);
    }

    private static void AddResultsTable(
        Body body,
        IReadOnlyList<RepositoryAssessment> repositories,
        IReadOnlyDictionary<string, TransformationExecutionResult> results,
        IReadOnlyDictionary<string, int> waveByRepository)
    {
        AddHeading(body, "2. Results by Repository", 1);

        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Repository",
                "Wave",
                "OpenRewrite",
                "Validation",
                "Changed files",
                "Outcome"
            }
        };

        foreach (var repository in repositories)
        {
            var wave = waveByRepository.TryGetValue(
                repository.RepositoryName,
                out var number)
                ? number.ToString()
                : "Not assigned";

            if (!results.TryGetValue(
                    repository.RepositoryName,
                    out var result))
            {
                rows.Add(
                [
                    repository.RepositoryName,
                    wave,
                    "Not yet executed",
                    "Not yet executed",
                    "-",
                    "Not yet executed"
                ]);

                continue;
            }

            rows.Add(
            [
                repository.RepositoryName,
                wave,
                result.Transformation.Status.ToString(),
                result.Validation.Status.ToString(),
                result.Transformation.ChangedFiles.Count.ToString(),
                result.Succeeded ? "Succeeded" : "Failed"
            ]);
        }

        body.Append(CreateTable(rows));
    }

    private static void AddRepositoryDetails(
        Body body,
        IReadOnlyList<RepositoryAssessment> repositories,
        IReadOnlyDictionary<string, TransformationExecutionResult> results,
        IReadOnlyDictionary<string, MigrationAnalysis> analyses,
        IReadOnlyDictionary<string, int> waveByRepository)
    {
        AddHeading(body, "3. Repository Details", 1);

        foreach (var repository in repositories)
        {
            AddHeading(body, repository.RepositoryName, 2);

            AddLabelledParagraph(
                body,
                "Migration wave",
                waveByRepository.TryGetValue(
                    repository.RepositoryName,
                    out var wave)
                    ? wave.ToString()
                    : "Not assigned");

            if (results.TryGetValue(
                    repository.RepositoryName,
                    out var result))
            {
                AddExecutionEvidence(body, result);
            }
            else
            {
                AddLabelledParagraph(
                    body,
                    "Status",
                    "Not yet executed: this repository is in a later " +
                    "migration wave than the one that has run.");
            }

            if (analyses.TryGetValue(
                    repository.RepositoryName,
                    out var analysis))
            {
                AddAiAnalysis(body, analysis);
            }
        }
    }

    private static void AddExecutionEvidence(
        Body body,
        TransformationExecutionResult result)
    {
        AddLabelledParagraph(
            body,
            "Source commit",
            result.Transformation.SourceCommitSha);

        AddLabelledParagraph(
            body,
            "Outcome",
            result.Succeeded ? "Succeeded" : "Failed");

        AddHeading(body, "OpenRewrite recipes applied", 3);

        foreach (var recipe in result.Transformation.AppliedRecipes)
        {
            AddBullet(body, recipe);
        }

        AddHeading(body, "Files changed by OpenRewrite", 3);

        if (result.Transformation.ChangedFiles.Count == 0)
        {
            AddParagraph(body, "No files were changed.");
        }

        foreach (var file in result.Transformation.ChangedFiles)
        {
            AddBullet(body, file);
        }

        AddHeading(body, "Validation checks", 3);

        if (result.Validation.Checks.Count == 0)
        {
            AddParagraph(
                body,
                $"Validation status: {result.Validation.Status}.");
        }

        foreach (var check in result.Validation.Checks)
        {
            AddBullet(
                body,
                $"{check.Check}: {(check.Passed ? "Passed" : "Failed")}");
        }

        foreach (var check in result.Validation.Checks.Where(check => !check.Passed))
        {
            AddHeading(body, $"Build diagnostics ({check.Check})", 3);

            var diagnostics = ValidationDiagnosticsExtractor.Extract(
                check.StandardOutput,
                check.StandardError);

            if (diagnostics.Count == 0)
            {
                AddParagraph(body, "No diagnostic lines were reported.");
            }

            foreach (var line in diagnostics)
            {
                AddParagraph(body, line);
            }
        }
    }

    private static void AddAiAnalysis(
        Body body,
        MigrationAnalysis analysis)
    {
        AddHeading(body, "AI analysis", 3);

        AddParagraph(body, analysis.Summary);

        foreach (var finding in analysis.Findings)
        {
            AddBullet(body, finding);
        }

        if (analysis.RecommendedActions.Count > 0)
        {
            AddLabelledParagraph(body, "Recommended actions", string.Empty);

            foreach (var action in analysis.RecommendedActions)
            {
                AddBullet(body, action);
            }
        }
    }

    private static void AddRisksAndRecommendations(
        Body body,
        MigrationAnalysisResult migrationAnalysis)
    {
        AddHeading(body, "4. Risks and Recommendations (AI-generated)", 1);

        AddHeading(body, "Key risks", 2);

        foreach (var risk in migrationAnalysis.Strategy.KeyRisks)
        {
            AddBullet(body, risk);
        }

        AddHeading(body, "Recommendations", 2);

        foreach (var recommendation in migrationAnalysis.Strategy.Recommendations)
        {
            AddBullet(body, recommendation);
        }
    }

    private static void AddControlBoundaries(
        Body body)
    {
        AddHeading(body, "5. AI Contribution and Control Boundaries", 1);

        body.Append(CreateTable(
        [
            new[] { "Area", "Deterministic (authoritative)", "AI contribution" },
            new[]
            {
                "Migration execution",
                "Pinned OpenRewrite recipes run at the exact assessed commit.",
                "None."
            },
            new[]
            {
                "Validation",
                "Fixed Maven checks decide pass or fail.",
                "None; it cannot change a result."
            },
            new[]
            {
                "Diagnostics",
                "Failure lines extracted from build output; workspace " +
                "paths removed.",
                "Groups failures by cause and explains them."
            },
            new[]
            {
                "Recommendations",
                "Not applicable.",
                "Suggests investigation and remediation steps. No code is " +
                "changed."
            }
        ]));
    }
}
