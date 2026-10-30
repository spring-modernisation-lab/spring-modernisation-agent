using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Transformation;

public sealed class GitRepositoryChangeDetector
    : IRepositoryChangeDetector
{
    private readonly IGitProcessRunner _gitProcessRunner;

    public GitRepositoryChangeDetector(
        IGitProcessRunner gitProcessRunner)
    {
        ArgumentNullException.ThrowIfNull(gitProcessRunner);

        _gitProcessRunner = gitProcessRunner;
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(fullRepositoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Repository directory was not found: {fullRepositoryPath}");
        }

        var trackedChanges = await GetTrackedChangesAsync(
            fullRepositoryPath,
            cancellationToken);

        var untrackedChanges = await GetUntrackedChangesAsync(
            fullRepositoryPath,
            cancellationToken);

        return trackedChanges
            .Concat(untrackedChanges)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetTrackedChangesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _gitProcessRunner.RunAsync(
            repositoryPath,
            [
                "diff",
                "--name-only",
                "HEAD"
            ],
            cancellationToken);

        EnsureSuccess(
            result,
            "Git could not detect tracked repository changes.");

        return ParseFileNames(result.StandardOutput);
    }

    private async Task<IReadOnlyList<string>> GetUntrackedChangesAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result = await _gitProcessRunner.RunAsync(
            repositoryPath,
            [
                "ls-files",
                "--others",
                "--exclude-standard"
            ],
            cancellationToken);

        EnsureSuccess(
            result,
            "Git could not detect untracked repository files.");

        return ParseFileNames(result.StandardOutput);
    }

    private static IReadOnlyList<string> ParseFileNames(
        string standardOutput)
    {
        return standardOutput
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static void EnsureSuccess(
        GitProcessResult result,
        string message)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? string.Empty
            : $" {result.StandardError.Trim()}";

        throw new InvalidOperationException(
            $"{message} Git exited with code {result.ExitCode}.{details}");
    }
}