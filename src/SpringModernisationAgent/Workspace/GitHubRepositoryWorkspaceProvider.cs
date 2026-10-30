using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Workspace;

public sealed partial class GitHubRepositoryWorkspaceProvider
    : IRepositoryWorkspaceProvider
{
    private readonly IGitClient _gitClient;
    private readonly string _workspaceRoot;

    public GitHubRepositoryWorkspaceProvider(
        IGitClient gitClient,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(gitClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _gitClient = gitClient;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<RepositoryWorkspace> CreateAsync(
        string organisation,
        string repositoryName,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(organisation, nameof(organisation));
        ValidateIdentifier(repositoryName, nameof(repositoryName));
        ValidateCommitSha(commitSha);

        Directory.CreateDirectory(_workspaceRoot);

        var repositoryPath = CreateRepositoryPath(repositoryName);
        var repositoryUrl = CreateRepositoryUrl(
            organisation,
            repositoryName);

        try
        {
            await _gitClient.CloneAsync(
                repositoryUrl,
                repositoryPath,
                cancellationToken);

            await _gitClient.CheckoutAsync(
                repositoryPath,
                commitSha,
                cancellationToken);

            var actualCommitSha =
                await _gitClient.GetHeadShaAsync(
                    repositoryPath,
                    cancellationToken);

            if (!string.Equals(
                    commitSha,
                    actualCommitSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Repository '{repositoryName}' was checked out at " +
                    $"commit '{actualCommitSha}' instead of the requested " +
                    $"commit '{commitSha}'.");
            }

            return new RepositoryWorkspace(
                RepositoryName: repositoryName,
                RepositoryPath: repositoryPath,
                CommitSha: actualCommitSha);
        }
        catch
        {
            DeleteWorkspace(repositoryPath);
            throw;
        }
    }

    private string CreateRepositoryPath(string repositoryName)
    {
        var directoryName =
            $"{repositoryName}-{Guid.NewGuid():N}";

        var repositoryPath = Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                directoryName));

        EnsurePathIsWithinWorkspaceRoot(repositoryPath);

        return repositoryPath;
    }

    private void EnsurePathIsWithinWorkspaceRoot(
        string repositoryPath)
    {
        var expectedPrefix =
            _workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!repositoryPath.StartsWith(
                expectedPrefix,
                comparison))
        {
            throw new InvalidOperationException(
                "Repository path is outside the permitted workspace root.");
        }
    }

    private static string CreateRepositoryUrl(
        string organisation,
        string repositoryName)
    {
        return $"https://github.com/{organisation}/{repositoryName}.git";
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (!GitHubIdentifierRegex().IsMatch(value))
        {
            throw new ArgumentException(
                "GitHub organisation and repository names may contain " +
                "only letters, numbers, periods, underscores and hyphens.",
                parameterName);
        }
    }

    private static void ValidateCommitSha(string commitSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        if (!CommitShaRegex().IsMatch(commitSha))
        {
            throw new ArgumentException(
                "The Git commit SHA must contain exactly " +
                "40 hexadecimal characters.",
                nameof(commitSha));
        }
    }

    private static void DeleteWorkspace(string repositoryPath)
    {
        if (!Directory.Exists(repositoryPath))
        {
            return;
        }

        try
        {
            RemoveReadOnlyAttributes(repositoryPath);

            Directory.Delete(
                repositoryPath,
                recursive: true);
        }
        catch
        {
            // Cleanup must not hide the original clone, checkout
            // or provenance failure.
        }
    }

    private static void RemoveReadOnlyAttributes(string path)
    {
        foreach (var filePath in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(filePath);

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(
                    filePath,
                    attributes & ~FileAttributes.ReadOnly);
            }
        }
    }

    [GeneratedRegex(
        "^[A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex GitHubIdentifierRegex();

    [GeneratedRegex(
        "^[0-9a-fA-F]{40}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();
}