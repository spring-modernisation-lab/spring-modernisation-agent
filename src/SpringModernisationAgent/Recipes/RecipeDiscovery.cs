using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Recipes;

/// <summary>Builds the recipe index once per run, on first use.</summary>
public interface IRecipeCatalogProvider
{
    Task<RecipeCatalog> GetAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>The index of the modules in <c>recipe-sources.yaml</c>, from Maven Central (SHA-1 checked).</summary>
public sealed class MavenCentralRecipeCatalogProvider : IRecipeCatalogProvider
{
    private readonly RecipeSources _sources;
    private readonly MavenCentralJarCache _cache;
    private Task<RecipeCatalog>? _catalog;

    public MavenCentralRecipeCatalogProvider(
        RecipeSources sources,
        MavenCentralJarCache cache)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(cache);

        _sources = sources;
        _cache = cache;
    }

    public Task<RecipeCatalog> GetAsync(
        CancellationToken cancellationToken = default) =>
        _catalog ??= LoadAsync(cancellationToken);

    private async Task<RecipeCatalog> LoadAsync(
        CancellationToken cancellationToken)
    {
        var jars = new List<(RecipeModule, string)>();

        foreach (var module in _sources.Modules)
        {
            jars.Add((module, await _cache.GetAsync(module, cancellationToken)));
        }

        return RecipeCatalog.FromJars(jars);
    }
}

public interface IRecipeDiscovery
{
    /// <summary>Null when validation passed: there is nothing to discover.</summary>
    Task<RecipeDiscoveryReport?> DiscoverAsync(
        TransformationPlan plan,
        string repositoryPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recipe discovery (docs/system-design.md §5.1, steps 2 and 4): matches the
/// errors the route's recipes left against the index, and measures the best
/// candidates on a throwaway copy of the workspace. It never changes the
/// workspace, and its own failures never change the migration result.
/// </summary>
public sealed class RecipeDiscovery : IRecipeDiscovery
{
    /// <summary>Candidates measured per repository: each costs a recipe run and a compile.</summary>
    public const int MaxMeasured = 3;

    private const int MaxReported = 8;

    private readonly IRecipeCatalogProvider _catalogProvider;
    private readonly IOpenRewriteRunner _openRewriteRunner;
    private readonly IMavenProcessRunner _maven;
    private readonly IMigrationProgress _progress;

    public RecipeDiscovery(
        IRecipeCatalogProvider catalogProvider,
        IOpenRewriteRunner openRewriteRunner,
        IMavenProcessRunner maven,
        IMigrationProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(catalogProvider);
        ArgumentNullException.ThrowIfNull(openRewriteRunner);
        ArgumentNullException.ThrowIfNull(maven);

        _catalogProvider = catalogProvider;
        _openRewriteRunner = openRewriteRunner;
        _maven = maven;
        _progress = progress ?? NullMigrationProgress.Instance;
    }

    public async Task<RecipeDiscoveryReport?> DiscoverAsync(
        TransformationPlan plan,
        string repositoryPath,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(validation);

        var failed = validation.Checks.FirstOrDefault(check => !check.Passed);

        if (validation.Passed || failed is null)
        {
            return null;
        }

        try
        {
            return await DiscoverAsync(plan, repositoryPath, failed, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _progress.Report(plan.RepositoryName, $"recipe discovery failed: {exception.Message}");
            return new RecipeDiscoveryReport(failed.Check, 0, [], exception.Message);
        }
    }

    private async Task<RecipeDiscoveryReport> DiscoverAsync(
        TransformationPlan plan,
        string repositoryPath,
        TransformationValidationCheckResult failed,
        CancellationToken cancellationToken)
    {
        var repository = plan.RepositoryName;
        var diagnostics = ValidationDiagnosticsExtractor.ExtractUnbounded(failed);
        var problems = RecipeMatcher.Problems(diagnostics);

        if (problems.Count == 0)
        {
            return new RecipeDiscoveryReport(failed.Check, 0, [], null);
        }

        var catalog = await _catalogProvider.GetAsync(cancellationToken);
        var alreadyRun = catalog.ReachableFrom(plan.OpenRewrite.Select(recipe => recipe.Recipe));
        var candidates = RecipeMatcher.Match(catalog, alreadyRun, problems, MaxReported);

        _progress.Report(
            repository,
            $"recipe discovery: {problems.Count} problems, {candidates.Count(candidate => candidate.Status == RecipeCandidateStatus.NotRun)} " +
            $"candidates not run by the route, {candidates.Count(candidate => candidate.Status == RecipeCandidateStatus.AlreadyRun)} already run");

        var before = CompileErrorDiff.Errors(diagnostics);
        var suggestions = new List<RecipeSuggestion>();
        var measured = 0;

        foreach (var candidate in candidates)
        {
            var recipe = candidate.Recipe;

            if (candidate.Status == RecipeCandidateStatus.AlreadyRun)
            {
                suggestions.Add(Suggestion(candidate, RecipeSuggestionVerdict.AlreadyRun, [], [], null));
                continue;
            }

            if (failed.Check != "compile")
            {
                suggestions.Add(Suggestion(candidate, RecipeSuggestionVerdict.Unverified, [], [],
                    $"measured only for compile errors; this failure is in '{failed.Check}'"));
                continue;
            }

            if (measured == MaxMeasured)
            {
                suggestions.Add(Suggestion(candidate, RecipeSuggestionVerdict.Unverified, [], [],
                    $"not measured: at most {MaxMeasured} candidates are measured per repository"));
                continue;
            }

            measured++;
            _progress.Report(repository, $"recipe discovery: measuring {recipe.Name} on a throwaway copy");
            suggestions.Add(await MeasureAsync(plan, repositoryPath, candidate, before, cancellationToken));
        }

        return new RecipeDiscoveryReport(failed.Check, problems.Count, suggestions, null);
    }

    /// <summary>Runs the candidate on a copy of the workspace, compiles it, and compares the errors.</summary>
    private async Task<RecipeSuggestion> MeasureAsync(
        TransformationPlan plan,
        string repositoryPath,
        RecipeCandidate candidate,
        IReadOnlyList<CompileErrorKey> before,
        CancellationToken cancellationToken)
    {
        // Named like a workspace, so workspace paths are removed from its output too.
        var copy = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(repositoryPath))!,
            $"{plan.RepositoryName}-candidate-{Guid.NewGuid():N}");

        try
        {
            CopyDirectory(repositoryPath, copy);

            var module = candidate.Recipe.Module;
            var run = await _openRewriteRunner.RunAsync(
                new OpenRewriteExecutionPlan(
                    plan.RepositoryName,
                    copy,
                    plan.SourceCommitSha,
                    [new OpenRewriteExecutionRecipe(candidate.Recipe.Name, $"{module.GroupId}:{module.ArtifactId}", module.Version)]),
                cancellationToken);

            if (run.Status != TransformationStatus.Succeeded)
            {
                var reason = ValidationDiagnosticsExtractor.Extract(run.StandardOutput, run.StandardError).FirstOrDefault()
                             ?? run.Status.ToString();

                return Suggestion(candidate, RecipeSuggestionVerdict.Unverified, [], [], $"failed to run: {reason}");
            }

            var compile = await _maven.RunAsync(copy, TransformationValidationMapping.GetMavenArguments("compile"), cancellationToken);
            var after = compile.ExitCode == 0
                ? []
                : CompileErrorDiff.Errors(ValidationDiagnosticsExtractor.ExtractUnbounded(compile.StandardOutput, compile.StandardError));

            // A build that fails with no source errors (for example a broken
            // pom.xml) would otherwise look like "every error removed".
            if (compile.ExitCode != 0 && after.Count == 0)
            {
                var reason = ValidationDiagnosticsExtractor.Extract(compile.StandardOutput, compile.StandardError).FirstOrDefault()
                             ?? $"exit code {compile.ExitCode}";

                return Suggestion(candidate, RecipeSuggestionVerdict.Unverified, [], [],
                    $"the build failed without source errors after it ran: {reason}");
            }

            var comparison = CompileErrorDiff.Compare(before, after);
            var suitable = comparison.Removed.Count > 0 && comparison.Added.Count == 0;

            return Suggestion(
                candidate,
                suitable ? RecipeSuggestionVerdict.Suitable : RecipeSuggestionVerdict.Unsuitable,
                comparison.Removed.Select(error => error.ToString()).ToList(),
                comparison.Added.Select(error => error.ToString()).ToList(),
                null);
        }
        finally
        {
            DeleteDirectory(copy);
        }
    }

    private static RecipeSuggestion Suggestion(
        RecipeCandidate candidate,
        RecipeSuggestionVerdict verdict,
        IReadOnlyList<string> removed,
        IReadOnlyList<string> added,
        string? note) =>
        new(
            candidate.Recipe.Name,
            candidate.Recipe.Module.Coordinates,
            candidate.Score,
            candidate.MatchedOn,
            candidate.Files,
            verdict,
            removed,
            added,
            note);

    /// <summary>Everything but build output, including .git (OpenRewrite reads it).</summary>
    private static void CopyDirectory(
        string source,
        string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);

            if (name == "target" || new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static void DeleteDirectory(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
