using System.Diagnostics;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class OpenRewriteRunner : IOpenRewriteRunner
{
    private readonly IMavenProcessRunner _mavenProcessRunner;
    private readonly IRepositoryChangeDetector _changeDetector;
    private readonly string _mavenPluginVersion;
    private readonly string _configDirectory;

    /// <param name="configDirectory">
    /// Where pipeline recipe files are resolved from; defaults to the
    /// application's <c>Config</c> folder.
    /// </param>
    public OpenRewriteRunner(
        IMavenProcessRunner mavenProcessRunner,
        IRepositoryChangeDetector changeDetector,
        string mavenPluginVersion,
        string? configDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(mavenProcessRunner);
        ArgumentNullException.ThrowIfNull(changeDetector);
        ArgumentException.ThrowIfNullOrWhiteSpace(mavenPluginVersion);

        _mavenProcessRunner = mavenProcessRunner;
        _changeDetector = changeDetector;
        _mavenPluginVersion = mavenPluginVersion;
        _configDirectory = Path.GetFullPath(configDirectory ?? Path.Combine(AppContext.BaseDirectory, "Config"));
    }

    public async Task<TransformationResult> RunAsync(
        OpenRewriteExecutionPlan executionPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionPlan);

        if (!Directory.Exists(executionPlan.RepositoryPath))
        {
            return NotRun(
                executionPlan,
                $"Repository directory was not found: {executionPlan.RepositoryPath}");
        }

        if (executionPlan.Recipes.Count == 0)
        {
            return NotRun(
                executionPlan,
                "No OpenRewrite recipes were supplied.");
        }

        var output = new List<string>();
        var errors = new List<string>();
        var appliedRecipes = new List<string>();

        foreach (var recipe in executionPlan.Recipes)
        {
            var result = await RunRecipeAsync(
                executionPlan.RepositoryPath,
                recipe,
                cancellationToken);

            output.Add(result.StandardOutput);

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                errors.Add(result.StandardError);
            }

            if (result.ExitCode != 0)
            {
                return new TransformationResult(
                    RepositoryName: executionPlan.RepositoryName,
                    SourceCommitSha: executionPlan.SourceCommitSha,
                    Status: TransformationStatus.Failed,
                    AppliedRecipes: appliedRecipes,
                    ChangedFiles: [],
                    StandardOutput: JoinOutput(output),
                    StandardError: JoinOutput(errors));
            }

            appliedRecipes.Add(recipe.Recipe);
        }

        var changedFiles = await _changeDetector.GetChangedFilesAsync(
            executionPlan.RepositoryPath,
            cancellationToken);

        return new TransformationResult(
            RepositoryName: executionPlan.RepositoryName,
            SourceCommitSha: executionPlan.SourceCommitSha,
            Status: TransformationStatus.Succeeded,
            AppliedRecipes: appliedRecipes,
            ChangedFiles: changedFiles,
            StandardOutput: JoinOutput(output),
            StandardError: JoinOutput(errors));
    }

    private async Task<MavenProcessResult> RunRecipeAsync(
        string repositoryPath,
        OpenRewriteExecutionRecipe recipe,
        CancellationToken cancellationToken)
    {
        ValidateRecipe(recipe);

        // runNoFork, not run: the run goal forks the lifecycle to
        // process-test-classes, i.e. compiles the project first. Recipes
        // execute one at a time, so after the first recipe the code is
        // intentionally mid-migration and will not compile; compilation
        // belongs to post-transformation validation, not recipe execution.
        var arguments = new List<string>
        {
                "--batch-mode",

                $"org.openrewrite.maven:rewrite-maven-plugin:" +
                $"{_mavenPluginVersion}:runNoFork",

                $"-Drewrite.activeRecipes={recipe.Recipe}"
        };

        if (recipe.Configuration is null)
        {
            arguments.Add($"-Drewrite.recipeArtifactCoordinates={recipe.Artifact}:{recipe.Version}");
        }
        else
        {
            // A pipeline recipe: its file stays outside the repository, so
            // it never appears in a patch.
            arguments.Add($"-Drewrite.configLocation={ResolveConfiguration(recipe)}");
        }

        return await _mavenProcessRunner.RunAsync(
            repositoryPath,
            arguments,
            cancellationToken);
    }

    private static void ValidateRecipe(
        OpenRewriteExecutionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (string.IsNullOrWhiteSpace(recipe.Recipe))
        {
            throw new InvalidOperationException(
                "The OpenRewrite recipe name is missing.");
        }

        if (recipe.Configuration is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(recipe.Artifact))
        {
            throw new InvalidOperationException(
                $"OpenRewrite recipe '{recipe.Recipe}' has no artifact.");
        }

        if (string.IsNullOrWhiteSpace(recipe.Version))
        {
            throw new InvalidOperationException(
                $"OpenRewrite recipe '{recipe.Recipe}' has no pinned version.");
        }
    }

    /// <summary>The recipe file's full path; it must exist inside the Config folder.</summary>
    private string ResolveConfiguration(
        OpenRewriteExecutionRecipe recipe)
    {
        var path = Path.GetFullPath(Path.Combine(_configDirectory, recipe.Configuration!));

        if (!path.StartsWith(_configDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OpenRewrite recipe '{recipe.Recipe}' configuration '{recipe.Configuration}' is outside the Config folder.");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"OpenRewrite recipe '{recipe.Recipe}' configuration was not found: Config/{recipe.Configuration}");
        }

        return path;
    }

    private static TransformationResult NotRun(
        OpenRewriteExecutionPlan executionPlan,
        string reason)
    {
        return new TransformationResult(
            RepositoryName: executionPlan.RepositoryName,
            SourceCommitSha: executionPlan.SourceCommitSha,
            Status: TransformationStatus.NotRun,
            AppliedRecipes: [],
            ChangedFiles: [],
            StandardOutput: null,
            StandardError: reason);
    }

    private static string? JoinOutput(
        IReadOnlyCollection<string> values)
    {
        var nonEmptyValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return nonEmptyValues.Count == 0
            ? null
            : string.Join(
                Environment.NewLine,
                nonEmptyValues);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between checking and killing it.
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
