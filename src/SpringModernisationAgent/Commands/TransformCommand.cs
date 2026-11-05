using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Behaviour;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Recipes;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;
using SpringModernisationAgent.Reporting;
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
            await TransformationPlansReader.ReadAsync(
                transformationPlansPath,
                cancellationToken);

        var existingResults =
            await new TransformationResultsReader().ReadAsync(
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
                artifactsDirectory,
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

        await WriteSummaryAsync(
            newResults,
            artifactsDirectory,
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
                $"{result.Verdict.State}");

            foreach (var reason in result.Verdict.Reasons)
            {
                Console.WriteLine(
                    $"    {reason.Id}{(reason.NeedsAPerson ? " (a person)" : "")}: {reason.Text}");
            }

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

            foreach (var patch in result.Patches)
            {
                Console.WriteLine(
                    $"  Patch: {patch.RelativePath} " +
                    $"({patch.ChangedFiles.Count} files, sha256 {patch.Sha256})");
            }

            if (result.Remediation is not null)
            {
                WriteRemediation(result.Remediation);
            }

            if (result.PatchRecordingError is not null)
            {
                Console.WriteLine(
                    $"  Patch recording failed: {result.PatchRecordingError}");
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
                    recipe.Configuration is null
                        ? $"        {recipe.Artifact}:{recipe.Version}"
                        : $"        pipeline recipe: Config/{recipe.Configuration}");
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
        string artifactsDirectory,
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

        // Live, timestamped progress so a CI log shows each stage as it runs.
        var progress =
            new ConsoleMigrationProgress();

        var transformationValidator =
            new TransformationValidator(
                mavenProcessRunner,
                progress);

        var patchRecorder =
            new GitWorkspacePatchRecorder(
                gitProcessRunner,
                artifactsDirectory);

        var knowledge =
            ProblemKnowledge.LoadDefault(
                UnsupportedLibraries.All.Select(library => library.Name).ToList());

        var behaviourContract =
            CreateBehaviourContract(
                mavenProcessRunner,
                progress);

        var ports =
            CreatePortRunner(
                patchRecorder,
                transformationValidator,
                mavenProcessRunner,
                knowledge,
                behaviourContract,
                progress);

        var recipeDiscovery =
            CreateRecipeDiscovery(
                openRewriteRunner,
                mavenProcessRunner,
                progress);

        IOpenRewriteExecutionCoordinator executionCoordinator =
            new OpenRewriteExecutionCoordinator(
                workspaceProvider,
                workspaceCleaner,
                new OpenRewriteExecutionPlanBuilder(),
                openRewriteRunner,
                transformationValidator,
                patchRecorder,
                ports,
                progress,
                recipeDiscovery,
                knowledge,
                behaviourContract,
                // Decisions are read from the pipeline repository's checkout.
                Environment.GetEnvironmentVariable("MIGRATION_DECISIONS_DIRECTORY") ?? Directory.GetCurrentDirectory());

        return new MigrationWaveExecutor(
            wavePlanner,
            repositorySelector,
            executionCoordinator);
    }

    /// <summary>
    /// The behaviour contract runs unless MIGRATION_BEHAVIOUR_CONTRACT is
    /// "disabled". The baseline is built with MIGRATION_BASELINE_JAVA_HOME
    /// (the source platform's JDK) when set; the migrated application with
    /// the JDK Maven finds by default.
    /// </summary>
    private static IBehaviourContractRecorder? CreateBehaviourContract(
        IMavenProcessRunner targetMaven,
        IMigrationProgress progress)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("MIGRATION_BEHAVIOUR_CONTRACT"),
                "disabled",
                StringComparison.Ordinal))
        {
            Console.WriteLine("Behaviour contract: disabled for this run.");
            return null;
        }

        var baselineJavaHome = Environment.GetEnvironmentVariable("MIGRATION_BASELINE_JAVA_HOME");

        var baselineMaven = string.IsNullOrWhiteSpace(baselineJavaHome)
            ? targetMaven
            : new MavenProcessRunner(new Dictionary<string, string> { ["JAVA_HOME"] = baselineJavaHome });

        var configuration = ProbeConfiguration.LoadDefault();

        Console.WriteLine(
            "Behaviour contract: enabled (baseline JDK: " +
            (string.IsNullOrWhiteSpace(baselineJavaHome) ? "the default" : baselineJavaHome) + ").");

        return new BehaviourContractRecorder(
            new ProbeRecorder(new BehaviourProbe(baselineMaven, configuration)),
            new ProbeRecorder(new BehaviourProbe(targetMaven, configuration)),
            progress);
    }

    /// <summary>
    /// Recipe discovery is deterministic and changes nothing, so it runs
    /// unless MIGRATION_RECIPE_DISCOVERY is "disabled". JARs are cached in
    /// MIGRATION_RECIPE_CACHE, or a folder under the system temp directory.
    /// </summary>
    private static IRecipeDiscovery? CreateRecipeDiscovery(
        IOpenRewriteRunner openRewriteRunner,
        IMavenProcessRunner mavenProcessRunner,
        IMigrationProgress progress)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("MIGRATION_RECIPE_DISCOVERY"),
                "disabled",
                StringComparison.Ordinal))
        {
            Console.WriteLine("Recipe discovery: disabled for this run.");
            return null;
        }

        var cache = Environment.GetEnvironmentVariable("MIGRATION_RECIPE_CACHE");

        if (string.IsNullOrWhiteSpace(cache))
        {
            cache = Path.Combine(Path.GetTempPath(), "spring-modernisation-recipe-cache");
        }

        var sources = RecipeSources.Load(
            Path.Combine(AppContext.BaseDirectory, RecipeSources.DefaultRelativePath));

        Console.WriteLine(
            $"Recipe discovery: enabled ({sources.Modules.Count} modules indexed on first use; " +
            $"at most {RecipeDiscovery.MaxMeasured} candidates measured per repository).");

        return new RecipeDiscovery(
            new MavenCentralRecipeCatalogProvider(
                sources,
                new MavenCentralJarCache(cache, new HttpClient { Timeout = TimeSpan.FromMinutes(2) })),
            openRewriteRunner,
            mavenProcessRunner,
            progress);
    }

    /// <summary>
    /// The AI changes code, so scoped ports run only when MIGRATION_REMEDIATION
    /// is exactly "enabled". Foundry settings are then required up front.
    /// Bounds are fixed: one attempt and one repair per task.
    /// </summary>
    private static IPortRunner? CreatePortRunner(
        GitWorkspacePatchRecorder patchRecorder,
        TransformationValidator transformationValidator,
        IMavenProcessRunner mavenProcessRunner,
        ProblemKnowledge knowledge,
        IBehaviourContractRecorder? behaviourContract,
        IMigrationProgress progress)
    {
        var setting =
            Environment.GetEnvironmentVariable(
                "MIGRATION_REMEDIATION");

        if (!string.Equals(setting, "enabled", StringComparison.Ordinal))
        {
            Console.WriteLine(
                "Scoped AI ports: not enabled for this run.");

            return null;
        }

        Console.WriteLine(
            "Scoped AI ports: enabled (one attempt and one repair per known API move).");

        return new PortRunner(
            PortAgentFactory.Create(),
            new RemediationPatchApplier(),
            patchRecorder,
            patchRecorder,
            transformationValidator,
            knowledge,
            behaviourContract,
            progress,
            new MavenClassInspectorFactory(
                mavenProcessRunner,
                new JavapRunner()));
    }

    private static void WriteRemediation(
        RemediationReport remediation)
    {
        Console.WriteLine(
            $"  AI ports: {PortText.Counts(remediation)}");

        foreach (var task in remediation.Tasks)
        {
            Console.WriteLine(
                $"    {task.Mapping}: {PortText.Outcome(task)}; behaviour: {PortText.Behaviour(task)}");
        }

        foreach (var left in remediation.LeftForAPerson)
        {
            Console.WriteLine(
                $"    For a person: {left}");
        }

        foreach (var attempt in remediation.Attempts)
        {
            var proposal = attempt.Proposal;

            var status =
                attempt.Error is not null ? $"error: {attempt.Error}"
                : attempt.RejectionReason is not null ? $"rejected: {attempt.RejectionReason}"
                : proposal is null ? "no proposal"
                : proposal.Decision != RemediationDecision.Fix ? proposal.Decision.ToString()
                : attempt.Validation?.Passed == true ? "applied; validation passed"
                : "applied; validation still fails";

            Console.WriteLine(
                $"    Attempt {attempt.Number} ({attempt.Task ?? attempt.FailedCheck}): {status}; {PortText.Kept(attempt)}");

            if (proposal is not null)
            {
                Console.WriteLine(
                    $"      Diagnosis: {proposal.Diagnosis}");

                for (var index = 0; index < proposal.Edits.Count; index++)
                {
                    var edit = proposal.Edits[index];

                    var applied = index < attempt.AppliedEdits.Count
                        ? $" ({DescribeKind(attempt.AppliedEdits[index].Kind)}, " +
                          $"{attempt.AppliedEdits[index].ChangedLines} lines changed)"
                        : string.Empty;

                    Console.WriteLine(
                        $"      Edit {edit.Path}{applied}: {edit.Rationale}");
                }

                foreach (var option in proposal.Options)
                {
                    Console.WriteLine(
                        $"      Option: {option}");
                }
            }

            Console.WriteLine(
                $"      Tool calls: {attempt.ToolCalls.Count}; " +
                $"tokens in/out: {attempt.InputTokens?.ToString() ?? "?"}/" +
                $"{attempt.OutputTokens?.ToString() ?? "?"}");
        }
    }

    /// <summary>
    /// Writes a Markdown summary of this wave to the artifacts and, in
    /// GitHub Actions, to the job summary page (GITHUB_STEP_SUMMARY).
    /// </summary>
    private static async Task WriteSummaryAsync(
        IReadOnlyList<TransformationExecutionResult> results,
        string artifactsDirectory,
        CancellationToken cancellationToken)
    {
        var markdown = TransformationSummaryWriter.Render(results);

        await File.WriteAllTextAsync(
            Path.Combine(artifactsDirectory, "transformation-summary.md"),
            markdown,
            cancellationToken);

        var jobSummary =
            Environment.GetEnvironmentVariable(
                "GITHUB_STEP_SUMMARY");

        if (!string.IsNullOrWhiteSpace(jobSummary))
        {
            await File.AppendAllTextAsync(
                jobSummary,
                markdown,
                cancellationToken);
        }
    }

    private static string DescribeKind(
        RemediationEditKind kind) =>
        kind switch
        {
            RemediationEditKind.WholeFileRewrite => "whole-file rewrite",
            RemediationEditKind.NewFile => "new file",
            _ => "search and replace"
        };

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
}