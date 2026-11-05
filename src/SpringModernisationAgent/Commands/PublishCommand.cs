using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Commands;

/// <summary>
/// Writes a reviewer summary for every repository that earned a pull
/// request (validation passed, or the agent stopped for a human decision)
/// and, only when MIGRATION_PUBLISH_PULL_REQUESTS is "enabled", opens or
/// updates a draft pull request on its migration branch. A publishing
/// failure never changes the migration results.
/// </summary>
public sealed class PublishCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var artifacts = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts");

        var plans = await TransformationPlansReader.ReadAsync(
            Path.Combine(artifacts, "transformation-plans.json"),
            cancellationToken);

        var results = await new TransformationResultsReader().ReadAsync(
            Path.Combine(artifacts, "transformation-results.json"),
            cancellationToken);

        var playbooks = ProblemKnowledge.LoadDefault(
            UnsupportedLibraries.All.Select(library => library.Name).ToList()).Decisions;

        var analyses = await ReadAnalysesAsync(
            Path.Combine(artifacts, "migration-results-analysis.json"),
            cancellationToken);

        var enabled = string.Equals(
            Environment.GetEnvironmentVariable("MIGRATION_PUBLISH_PULL_REQUESTS"),
            "enabled",
            StringComparison.Ordinal);

        Console.WriteLine(enabled
            ? "Draft pull requests: enabled."
            : "Draft pull requests: not enabled (reviewer summaries only).");

        var publisher = enabled ? CreatePublisher() : null;
        var organisation = Environment.GetEnvironmentVariable("GITHUB_ORGANISATION");
        var publications = new List<PullRequestPublication>();
        var failures = new List<(string Repository, string Message)>();
        var skipped = new List<string>();

        foreach (var result in results)
        {
            var repository = result.Transformation.RepositoryName;
            var plan = plans.FirstOrDefault(candidate => candidate.RepositoryName == repository);

            if (plan is null)
            {
                continue;
            }

            var flags = ReviewFlagDetector.Detect(
                await ReadPatchesAsync(artifacts, result, cancellationToken));

            var content = PullRequestContentBuilder.Build(
                plan,
                result,
                flags,
                analyses.GetValueOrDefault(repository),
                playbooks: playbooks);

            if (content is null)
            {
                Console.WriteLine($"{repository}: no pull request (validation failed without a decision to make).");
                skipped.Add(repository);
                continue;
            }

            var summaryPath = Path.Combine(artifacts, "summaries", $"{repository}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(summaryPath)!);
            await File.WriteAllTextAsync(summaryPath, content.Body, cancellationToken);

            Console.WriteLine($"{repository}: reviewer summary written ({flags.Count} review flags).");

            if (publisher is null)
            {
                continue;
            }

            try
            {
                var cumulative = result.Patches.Single(patch => patch.Stage == "cumulative");

                var publication = await publisher.PublishAsync(
                    organisation!,
                    content,
                    Path.Combine(artifacts, cumulative.RelativePath),
                    cancellationToken);

                publications.Add(publication);

                Console.WriteLine(
                    $"{repository}: draft pull request {(publication.Created ? "opened" : "updated")}: {publication.Url}");
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                failures.Add((repository, exception.Message));
                Console.Error.WriteLine($"{repository}: draft pull request failed: {exception.Message}");
            }
        }

        if (enabled)
        {
            await File.WriteAllTextAsync(
                Path.Combine(artifacts, "pull-requests.json"),
                JsonSerializer.Serialize(publications, JsonOptions),
                cancellationToken);

            await AppendJobSummaryAsync(publications, failures, skipped, cancellationToken);
        }

        if (failures.Count > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private static DraftPullRequestPublisher CreatePublisher()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var workspaceRoot = Environment.GetEnvironmentVariable("MIGRATION_WORKSPACE_ROOT");

        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(workspaceRoot)
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ORGANISATION")))
        {
            throw new InvalidOperationException(
                "Publishing needs GITHUB_TOKEN, GITHUB_ORGANISATION and MIGRATION_WORKSPACE_ROOT.");
        }

        return new DraftPullRequestPublisher(
            new OctokitPullRequestApi(token),
            new GitBranchPusher(new GitCommandRunner(), workspaceRoot, token));
    }

    private static async Task<IReadOnlyList<ReviewPatch>> ReadPatchesAsync(
        string artifacts,
        TransformationExecutionResult result,
        CancellationToken cancellationToken)
    {
        var patches = new List<ReviewPatch>();

        foreach (var patch in result.Patches.Where(patch => patch.Stage != "cumulative"))
        {
            var path = Path.Combine(artifacts, patch.RelativePath);

            if (File.Exists(path))
            {
                patches.Add(new ReviewPatch(
                    patch.Stage,
                    AiWritten: result.Remediation?.IsKeptAiPatch(patch.Stage) == true,
                    await File.ReadAllTextAsync(path, cancellationToken)));
            }
        }

        return patches;
    }

    /// <summary>The AI report is optional; without it the summary has no AI section.</summary>
    private static async Task<IReadOnlyDictionary<string, MigrationAnalysis>> ReadAnalysesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, MigrationAnalysis>();
        }

        try
        {
            await using var stream = File.OpenRead(path);

            var analysis = await JsonSerializer.DeserializeAsync<MigrationAnalysisResult>(
                stream,
                JsonOptions,
                cancellationToken);

            return (analysis?.RepositoryAnalyses ?? [])
                .ToDictionary(entry => entry.RepositoryName, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            Console.WriteLine("The AI results analysis could not be read; summaries will not include it.");
            return new Dictionary<string, MigrationAnalysis>();
        }
    }

    private static async Task AppendJobSummaryAsync(
        IReadOnlyList<PullRequestPublication> publications,
        IReadOnlyList<(string Repository, string Message)> failures,
        IReadOnlyList<string> skipped,
        CancellationToken cancellationToken)
    {
        var jobSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");

        if (string.IsNullOrWhiteSpace(jobSummary))
        {
            return;
        }

        await File.AppendAllTextAsync(
            jobSummary,
            RenderJobSummary(publications, failures, skipped),
            cancellationToken);
    }

    /// <summary>
    /// Every repository appears: its link, why it has no pull request, or the
    /// publishing failure (the step itself stays green: continue-on-error).
    /// </summary>
    public static string RenderJobSummary(
        IReadOnlyList<PullRequestPublication> publications,
        IReadOnlyList<(string Repository, string Message)> failures,
        IReadOnlyList<string> skipped)
    {
        var markdown = new StringBuilder("\n## Draft pull requests\n\n");

        foreach (var publication in publications)
        {
            markdown.AppendLine(
                $"- ✅ {publication.RepositoryName}: [#{publication.Number}]({publication.Url}) " +
                (publication.Created ? "opened" : "updated") +
                (publication.NeedsDecision ? " — needs a decision" : string.Empty));
        }

        foreach (var (repository, message) in failures)
        {
            var firstLine = message.ReplaceLineEndings("\n").Split('\n')[0];
            markdown.AppendLine($"- ⚠️ {repository}: **publishing failed**: {firstLine}");
        }

        foreach (var repository in skipped)
        {
            markdown.AppendLine($"- {repository}: no pull request (validation failed without a decision to make)");
        }

        if (publications.Count + failures.Count + skipped.Count == 0)
        {
            markdown.AppendLine("No repositories to publish.");
        }

        return markdown.ToString();
    }
}
