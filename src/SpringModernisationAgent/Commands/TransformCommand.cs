using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Commands;

public sealed class TransformCommand
{
    public async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var organisation =
            GetRequiredEnvironmentVariable(
                "GITHUB_ORGANISATION");

        var workspaceRoot =
            GetRequiredEnvironmentVariable(
                "MIGRATION_WORKSPACE_ROOT");

        var mavenPluginVersion =
            GetRequiredEnvironmentVariable(
                "OPENREWRITE_MAVEN_PLUGIN_VERSION");

        var artifactsDirectory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Artifacts");

        var transformationPlansPath = Path.Combine(
            artifactsDirectory,
            "transformation-plans.json");

        var transformationResultsPath = Path.Combine(
            artifactsDirectory,
            "transformation-results.json");

        var transformationPlans =
            await ReadTransformationPlansAsync(
                transformationPlansPath,
                cancellationToken);

        var existingResults =
            await ReadTransformationResultsAsync(
                transformationResultsPath,
                cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "Transformation execution requested.");

        Console.WriteLine(
            $"Loaded {transformationPlans.Count} " +
            "transformation plans.");

        Console.WriteLine(
            $"Loaded {existingResults.Count} " +
            "previous execution results.");

        var wavePlanner =
            new MigrationWaveExecutionPlanner();

        var repositorySelector =
            new MigrationWaveRepositorySelector();

        var activeWave =
            wavePlanner.GetNextWave(
                transformationPlans,
                existingResults);

        if (activeWave.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "No migration wave is currently eligible " +
                "for execution.");

            return;
        }

        var repositoriesToExecute =
            repositorySelector.GetRepositoriesToExecute(
                activeWave,
                existingResults);

        if (repositoriesToExecute.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "The active migration wave has no repositories " +
                "eligible for execution.");

            return;
        }

        WriteExecutionPreview(
            organisation,
            repositoriesToExecute);

        cancellationToken.ThrowIfCancellationRequested();

        var waveExecutor =
            CreateWaveExecutor(
                workspaceRoot,
                mavenPluginVersion,
                wavePlanner,
                repositorySelector);

        var newResults =
            await waveExecutor.ExecuteNextWaveAsync(
                organisation,
                transformationPlans,
                existingResults,
                cancellationToken);

        if (newResults.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "No repositories were transformed.");

            return;
        }

        var allResults = existingResults
            .Concat(newResults)
            .ToList();

        var resultsWriter =
            new TransformationResultsWriter();

        await resultsWriter.WriteAsync(
            allResults,
            transformationResultsPath,
            cancellationToken);

        Console.WriteLine();
        Console.WriteLine(
            "Transformation execution results");

        Console.WriteLine(
            "================================");

        foreach (var result in newResults)
        {
            Console.WriteLine();

            Console.WriteLine(
                result.Transformation.RepositoryName);

            Console.WriteLine(
                $"  Transformation: " +
                $"{result.Transformation.Status}");

            Console.WriteLine(
                $"  Validation: " +
                $"{result.Validation.Status}");

            Console.WriteLine(
                $"  Overall: " +
                $"{(result.Succeeded
                    ? "Succeeded"
                    : "Failed")}");

            if (result.Transformation.ChangedFiles.Count > 0)
            {
                Console.WriteLine(
                    "  Changed files:");

                foreach (var changedFile in
                         result.Transformation.ChangedFiles)
                {
                    Console.WriteLine(
                        $"    - {changedFile}");
                }
            }

            if (result.Validation.Checks.Count > 0)
            {
                Console.WriteLine(
                    "  Validation checks:");

                foreach (var check in
                         result.Validation.Checks)
                {
                    Console.WriteLine(
                        $"    - {check.Check}: " +
                        $"{(check.Passed
                            ? "Passed"
                            : "Failed")}");
                }
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            $"Transformation results written to: " +
            $"{transformationResultsPath}");
    }

    private static void WriteExecutionPreview(
        string organisation,
        IReadOnlyList<TransformationPlan> repositoriesToExecute)
    {
        var waveNumber =
            repositoriesToExecute[0].MigrationWave;

        Console.WriteLine();
        Console.WriteLine(
            "==============================================");

        Console.WriteLine(
            "TRANSFORMATION EXECUTION");

        Console.WriteLine(
            "==============================================");

        Console.WriteLine();

        Console.WriteLine(
            $"Organisation: {organisation}");

        Console.WriteLine(
            $"Migration wave: {waveNumber}");

        Console.WriteLine(
            $"Repositories to execute: " +
            $"{repositoriesToExecute.Count}");

        Console.WriteLine();

        foreach (var plan in repositoriesToExecute)
        {
            Console.WriteLine(
                $"  {plan.RepositoryName}");

            Console.WriteLine(
                $"    Commit: {plan.SourceCommitSha}");

            Console.WriteLine(
                $"    Route:  {plan.MigrationRouteId}");

            Console.WriteLine(
                $"    Target: Spring Boot " +
                $"{plan.Target.SpringBootVersion}, " +
                $"Java {plan.Target.JavaVersion}");

            Console.WriteLine(
                "    OpenRewrite recipes:");

            foreach (var recipe in plan.OpenRewrite)
            {
                Console.WriteLine(
                    $"      - {recipe.Recipe}");

                Console.WriteLine(
                    $"        {recipe.Artifact}:" +
                    $"{recipe.Version}");
            }

            Console.WriteLine(
                "    Validation:");

            foreach (var validation in
                     plan.Validation)
            {
                Console.WriteLine(
                    $"      - {validation}");
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            "Only the repositories listed above will be transformed.");

        Console.WriteLine(
            "Later migration waves will not execute during this invocation.");

        Console.WriteLine(
            "==============================================");

        Console.WriteLine();
    }

    private static MigrationWaveExecutor CreateWaveExecutor(
        string workspaceRoot,
        string mavenPluginVersion,
        MigrationWaveExecutionPlanner wavePlanner,
        MigrationWaveRepositorySelector repositorySelector)
    {
        var gitClient =
            new GitClient();

        var workspaceProvider =
            new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

        var workspaceCleaner =
            new RepositoryWorkspaceCleaner(
                workspaceRoot);

        var mavenProcessRunner =
            new MavenProcessRunner();

        var gitProcessRunner =
            new GitProcessRunner();

        var changeDetector =
            new GitRepositoryChangeDetector(
                gitProcessRunner);

        var openRewriteRunner =
            new OpenRewriteRunner(
                mavenProcessRunner,
                changeDetector,
                mavenPluginVersion);

        var transformationValidator =
            new TransformationValidator(
                mavenProcessRunner);

        IOpenRewriteExecutionCoordinator executionCoordinator =
            new OpenRewriteExecutionCoordinator(
                workspaceProvider,
                workspaceCleaner,
                new OpenRewriteExecutionPlanBuilder(),
                openRewriteRunner,
                transformationValidator);

        return new MigrationWaveExecutor(
            wavePlanner,
            repositorySelector,
            executionCoordinator);
    }

    private static string GetRequiredEnvironmentVariable(
        string name)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{name} is not configured.");
        }

        return value;
    }

    private static async Task<
        IReadOnlyList<TransformationPlan>>
        ReadTransformationPlansAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Transformation plans were not found.",
                path);
        }

        await using var stream =
            File.OpenRead(path);

        var plans =
            await JsonSerializer.DeserializeAsync<
                List<TransformationPlan>>(
                stream,
                CreateJsonOptions(),
                cancellationToken);

        return plans
            ?? throw new InvalidOperationException(
                "Transformation plans could not be deserialized.");
    }

    private static async Task<
        IReadOnlyList<TransformationExecutionResult>>
        ReadTransformationResultsAsync(
            string path,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream =
            File.OpenRead(path);

        var results =
            await JsonSerializer.DeserializeAsync<
                List<TransformationExecutionResult>>(
                stream,
                CreateJsonOptions(),
                cancellationToken);

        return results ?? [];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }
}