using SpringModernisationAgent.AI;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.GitHub;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Commands;

public sealed class AssessCommand
{
    public async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine();
        Console.WriteLine(
            "Spring modernisation assessment requested.");

        var token =
            GetRequiredEnvironmentVariable(
                "GITHUB_TOKEN");

        var organisation =
            GetRequiredEnvironmentVariable(
                "GITHUB_ORGANISATION");

        var provider =
            new GitHubRepositoryProvider(token);

        var parser =
            new MavenPomParser();

        var repositories =
            await provider.GetRepositoriesAsync(
                organisation);

        var inventories =
            new List<RepositoryInventory>();

        foreach (var repository in repositories
                     .OrderBy(
                         repository => repository.Name,
                         StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var commitSha =
                await provider.GetCommitShaAsync(
                    organisation,
                    repository.Name,
                    repository.DefaultBranch);

            var pom =
                await provider.GetFileContentAsync(
                    organisation,
                    repository.Name,
                    "pom.xml",
                    commitSha);

            if (pom is null)
            {
                Console.WriteLine(
                    $"{repository.Name}: not a Maven project");

                continue;
            }

            try
            {
                var project = parser.Parse(pom);

                inventories.Add(
                    new RepositoryInventory(
                        RepositoryName: repository.Name,
                        CommitSha: commitSha,
                        MavenProject: project));
            }
            catch (Exception exception)
            {
                // Fail closed: a repository missing from the inventory would
                // silently disappear from the dependency graph, and its
                // dependants could be scheduled in too early a wave.
                throw new InvalidOperationException(
                    $"Unable to analyse {repository.Name} at {commitSha}: " +
                    $"{exception.Message} Assessment stopped because the " +
                    "dependency graph would be incomplete.",
                    exception);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var graphBuilder =
            new DependencyGraphBuilder();

        var graph =
            graphBuilder.Build(inventories);

        WriteDependencyGraph(graph);

        var repositoriesByArtifact =
            inventories.ToDictionary(
                inventory =>
                    inventory.MavenProject.Coordinate.Key,
                inventory => inventory,
                StringComparer.OrdinalIgnoreCase);

        var springAnalyzer =
            new SpringPlatformAnalyzer();

        var repositoryClassifier =
            new RepositoryClassifier();

        var assessments =
            new List<RepositoryAssessment>();

        foreach (var inventory in inventories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind =
                repositoryClassifier.Classify(
                    inventory.MavenProject);

            var spring =
                springAnalyzer.Analyze(
                    inventory,
                    repositoriesByArtifact);

            var internalEdges =
                graph.Edges
                    .Where(edge =>
                        edge.FromRepository.Equals(
                            inventory.RepositoryName,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var internalArtifactKeys =
                internalEdges
                    .Select(edge =>
                        inventories.Single(candidate =>
                            candidate.RepositoryName.Equals(
                                edge.ToRepository,
                                StringComparison.OrdinalIgnoreCase)))
                    .Select(candidate =>
                        candidate.MavenProject.Coordinate.Key)
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            var externalDependencies =
                inventory.MavenProject.Dependencies
                    .Where(dependency =>
                        !internalArtifactKeys.Contains(
                            dependency.Coordinate.Key))
                    .ToList();

            assessments.Add(
                new RepositoryAssessment(
                    RepositoryName:
                        inventory.RepositoryName,
                    CommitSha:
                        inventory.CommitSha,
                    Kind:
                        kind,
                    MavenProject:
                        inventory.MavenProject,
                    Spring:
                        spring,
                    InternalDependencies:
                        internalEdges,
                    ExternalDependencies:
                        externalDependencies));
        }

        WriteTechnologyInventory(assessments);

        cancellationToken.ThrowIfCancellationRequested();

        var migrationWaveBuilder =
            new MigrationWaveBuilder();

        var assessmentBuilder =
            new ProjectAssessmentBuilder(
                migrationWaveBuilder);

        var projectAssessment =
            assessmentBuilder.Build(
                organisation,
                assessments,
                graph);

        projectAssessment =
            ApplyMigrationPolicy(
                projectAssessment);

        projectAssessment =
            await ApplyBaselineBuildEvidenceAsync(
                projectAssessment,
                cancellationToken);

        projectAssessment =
            ApplyMigrationReadiness(
                projectAssessment);

        var transformationPlans =
            BuildTransformationPlans(
                projectAssessment);

        var waveGateResult =
            new MigrationWaveGate().Apply(
                projectAssessment.MigrationWaves,
                transformationPlans);

        WriteWaveGateSummary(waveGateResult);

        var artifactsDirectory =
            GetArtifactsDirectory();

        // Deterministic artifacts are written before the optional AI stage,
        // so an AI or authentication failure can never prevent migration.
        // Only plans released by the wave gate become executable contracts.
        await WriteTransformationPlansAsync(
            waveGateResult.ExecutablePlans,
            artifactsDirectory,
            cancellationToken);

        await WriteProjectAssessmentAsync(
            projectAssessment,
            artifactsDirectory,
            cancellationToken);

        // The strategy explains the whole estate, including withheld plans.
        await WriteStrategyArtifactsAsync(
            projectAssessment,
            transformationPlans,
            artifactsDirectory,
            cancellationToken);

        WriteCompletionSummary(
            projectAssessment,
            waveGateResult.ExecutablePlans);
    }

    private static async Task WriteStrategyArtifactsAsync(
        ProjectAssessment projectAssessment,
        IReadOnlyList<TransformationPlan> transformationPlans,
        string artifactsDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "FOUNDRY_PROJECT_ENDPOINT")))
        {
            Console.WriteLine();
            Console.WriteLine(
                "Strategy Agent skipped: FOUNDRY_PROJECT_ENDPOINT is not " +
                "configured. Deterministic assessment artifacts are complete.");

            return;
        }

        MigrationAnalysisResult migrationAnalysis;

        try
        {
            migrationAnalysis =
                await GenerateMigrationAnalysisAsync(
                    projectAssessment,
                    cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine(
                "WARNING: Strategy Agent failed; the AI strategy and Word " +
                "document were not produced. Deterministic assessment " +
                "artifacts are complete and migration is not affected.");

            Console.WriteLine(
                $"  {exception.GetType().Name}: {exception.Message}");

            return;
        }

        await WriteMigrationAnalysisAsync(
            migrationAnalysis,
            artifactsDirectory,
            cancellationToken);

        await WriteMigrationStrategyDocumentAsync(
            projectAssessment,
            migrationAnalysis,
            transformationPlans,
            artifactsDirectory,
            cancellationToken);
    }

    private static void WriteWaveGateSummary(
        MigrationWaveGateResult waveGateResult)
    {
        if (waveGateResult.EarliestBlockedWave is null)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Migration wave {waveGateResult.EarliestBlockedWave} contains " +
            "repositories that are not ready: " +
            string.Join(", ", waveGateResult.BlockedRepositories));

        if (waveGateResult.WithheldPlans.Count > 0)
        {
            Console.WriteLine(
                "Plans withheld from execution until they are resolved: " +
                string.Join(
                    ", ",
                    waveGateResult.WithheldPlans.Select(
                        plan => $"{plan.RepositoryName} " +
                                $"(wave {plan.MigrationWave})")));
        }
    }

    private static ProjectAssessment ApplyMigrationPolicy(
        ProjectAssessment projectAssessment)
    {
        var policyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "spring.yaml");

        var policyLoader =
            new SpringMigrationPolicyLoader();

        var policy =
            policyLoader.Load(policyPath);

        var routeSelector =
            new MigrationRouteSelector();

        var policyAssessment =
            new MigrationPolicyAssessment(
                routeSelector);

        return policyAssessment.Apply(
            projectAssessment,
            policy);
    }

    private static async Task<ProjectAssessment>
        ApplyBaselineBuildEvidenceAsync(
            ProjectAssessment projectAssessment,
            CancellationToken cancellationToken)
    {
        var baselineResultsPath = Path.Combine(
            GetArtifactsDirectory(),
            "baseline-build-results.json");

        if (!File.Exists(baselineResultsPath))
        {
            Console.WriteLine();
            Console.WriteLine(
                "No baseline-build results were found.");

            Console.WriteLine(
                "Migration readiness will reflect the missing " +
                "baseline-build evidence.");

            return projectAssessment;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var resultsReader =
            new PortfolioBaselineBuildResultsReader();

        var baselineBuildResults =
            await resultsReader.ReadAsync(
                baselineResultsPath);

        var baselineBuildValidator =
            new BaselineBuildValidator();

        var commitProvenanceValidator =
            new CommitProvenanceValidator();

        var baselineBuildAssessment =
            new BaselineBuildAssessment(
                baselineBuildValidator,
                commitProvenanceValidator);

        return baselineBuildAssessment.Apply(
            projectAssessment,
            baselineBuildResults);
    }

    private static ProjectAssessment ApplyMigrationReadiness(
        ProjectAssessment projectAssessment)
    {
        var readinessEvaluator =
            new MigrationReadinessEvaluator();

        var repositoriesWithReadiness =
            projectAssessment.Repositories
                .Select(repository =>
                    repository with
                    {
                        MigrationReadiness =
                            readinessEvaluator.Evaluate(
                                repository)
                    })
                .ToList();

        return projectAssessment with
        {
            Repositories =
                repositoriesWithReadiness
        };
    }

    private static IReadOnlyList<TransformationPlan>
        BuildTransformationPlans(
            ProjectAssessment projectAssessment)
    {
        var openRewriteValidator =
            new OpenRewriteConfigurationValidator();

        var transformationPlanBuilder =
            new TransformationPlanBuilder(
                openRewriteValidator);

        return projectAssessment.Repositories
            .Where(repository =>
                repository.MigrationReadiness?.CanProceed == true)
            .Select(repository =>
                transformationPlanBuilder.Build(
                    repository,
                    projectAssessment.MigrationWaves))
            .OrderBy(plan =>
                plan.MigrationWave)
            .ThenBy(
                plan => plan.RepositoryName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<MigrationAnalysisResult>
        GenerateMigrationAnalysisAsync(
            ProjectAssessment projectAssessment,
            CancellationToken cancellationToken)
    {
        var inputBuilder =
            new MigrationAnalysisInputBuilder();

        // This is the pre-migration Strategy Agent stage.
        // No synthetic transformation results are supplied.
        IReadOnlyList<TransformationExecutionResult>
            transformationResults = [];

        var input =
            inputBuilder.Build(
                projectAssessment,
                transformationResults);

        var strategyAgent =
            MigrationAnalysisAgentFactory.Create();

        return await strategyAgent.AnalyzeAsync(
            input,
            cancellationToken);
    }

    private static async Task WriteTransformationPlansAsync(
        IReadOnlyList<TransformationPlan> transformationPlans,
        string artifactsDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(
            artifactsDirectory,
            "transformation-plans.json");

        var writer =
            new TransformationPlansWriter();

        await writer.WriteAsync(
            transformationPlans,
            path);

        Console.WriteLine();
        Console.WriteLine(
            $"Transformation plans written to: {path}");
    }

    private static async Task WriteMigrationAnalysisAsync(
        MigrationAnalysisResult migrationAnalysis,
        string artifactsDirectory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            artifactsDirectory,
            "migration-analysis.json");

        var writer =
            new MigrationAnalysisResultWriter();

        await writer.WriteAsync(
            migrationAnalysis,
            path,
            cancellationToken);

        Console.WriteLine(
            $"Migration analysis written to: {path}");
    }

    private static async Task
        WriteMigrationStrategyDocumentAsync(
            ProjectAssessment projectAssessment,
            MigrationAnalysisResult migrationAnalysis,
            IReadOnlyList<TransformationPlan> transformationPlans,
            string artifactsDirectory,
            CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            artifactsDirectory,
            "spring-modernisation-migration-strategy.docx");

        var writer =
            new MigrationStrategyDocumentWriter();

        await writer.WriteAsync(
            projectAssessment,
            migrationAnalysis,
            transformationPlans,
            path,
            cancellationToken);

        Console.WriteLine(
            $"Migration strategy document written to: {path}");
    }

    private static async Task WriteProjectAssessmentAsync(
        ProjectAssessment projectAssessment,
        string artifactsDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(
            artifactsDirectory,
            "project-assessment.json");

        var writer =
            new ProjectAssessmentWriter();

        await writer.WriteAsync(
            projectAssessment,
            path);

        Console.WriteLine(
            $"Project assessment written to: {path}");
    }

    private static void WriteDependencyGraph(
        RepositoryDependencyGraph graph)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Repository dependency graph");

        Console.WriteLine(
            "===========================");

        foreach (var edge in graph.Edges)
        {
            Console.WriteLine(
                $"{edge.FromRepository} " +
                $"--{edge.Type}--> " +
                $"{edge.ToRepository} " +
                $"[{edge.Version ?? "unknown"}]");
        }
    }

    private static void WriteTechnologyInventory(
        IReadOnlyList<RepositoryAssessment> assessments)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Technology inventory");

        Console.WriteLine(
            "====================");

        foreach (var assessment in assessments
                     .OrderBy(
                         assessment =>
                             assessment.RepositoryName,
                         StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine();
            Console.WriteLine(
                assessment.RepositoryName);

            Console.WriteLine(
                $"  Spring Boot: " +
                $"{assessment.Spring.SpringBootVersion ?? "unknown"}");

            Console.WriteLine(
                $"  Java: " +
                $"{assessment.Spring.JavaVersion ?? "unknown"}");

            Console.WriteLine(
                "  Spring starters:");

            foreach (var starter in
                     assessment.Spring.Starters)
            {
                Console.WriteLine(
                    $"    - {starter}");
            }

            Console.WriteLine(
                "  Internal dependencies:");

            foreach (var dependency in
                     assessment.InternalDependencies)
            {
                Console.WriteLine(
                    $"    - {dependency.ToRepository} " +
                    $"[{dependency.Type}]");
            }

            Console.WriteLine(
                "  External dependencies:");

            foreach (var dependency in
                     assessment.ExternalDependencies)
            {
                Console.WriteLine(
                    $"    - {dependency.Coordinate} " +
                    $"[{dependency.Scope}]");
            }
        }
    }

    private static void WriteCompletionSummary(
        ProjectAssessment projectAssessment,
        IReadOnlyList<TransformationPlan> transformationPlans)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Assessment and strategy generation completed.");

        Console.WriteLine(
            $"Repositories assessed: " +
            $"{projectAssessment.Repositories.Count}");

        Console.WriteLine(
            $"Migration waves: " +
            $"{projectAssessment.MigrationWaves.Waves.Count}");

        Console.WriteLine(
            $"Transformation plans generated: " +
            $"{transformationPlans.Count}");

        Console.WriteLine();
        Console.WriteLine(
            "No repository transformations were executed.");
    }

    private static string GetArtifactsDirectory()
    {
        var directory = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Artifacts");

        Directory.CreateDirectory(directory);

        return directory;
    }

    private static string GetRequiredEnvironmentVariable(
        string name)
    {
        var value =
            Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{name} environment variable isn't set.");
        }

        return value;
    }
}