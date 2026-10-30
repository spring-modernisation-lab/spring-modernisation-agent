namespace SpringModernisationAgent.Workspace;

public sealed class RepositoryWorkspaceCleaner :
    IRepositoryWorkspaceCleaner
{
    private readonly string _workspaceRoot;

    public RepositoryWorkspaceCleaner(
        string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public Task DeleteAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        cancellationToken.ThrowIfCancellationRequested();

        var workspacePath = Path.GetFullPath(
            workspace.RepositoryPath);

        EnsurePathIsWithinWorkspaceRoot(workspacePath);

        if (!Directory.Exists(workspacePath))
        {
            return Task.CompletedTask;
        }

        RemoveReadOnlyAttributes(
            workspacePath,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        Directory.Delete(
            workspacePath,
            recursive: true);

        return Task.CompletedTask;
    }

    private void EnsurePathIsWithinWorkspaceRoot(
        string workspacePath)
    {
        var root = _workspaceRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        var expectedPrefix =
            root + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!workspacePath.StartsWith(
                expectedPrefix,
                comparison))
        {
            throw new InvalidOperationException(
                $"Workspace '{workspacePath}' is outside the permitted workspace root.");
        }

        if (string.Equals(
                workspacePath,
                root,
                comparison))
        {
            throw new InvalidOperationException(
                "The workspace root itself cannot be deleted.");
        }
    }

    private static void RemoveReadOnlyAttributes(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.EnumerateFiles(
                     workspacePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributes =
                File.GetAttributes(filePath);

            if ((attributes & FileAttributes.ReadOnly) == 0)
            {
                continue;
            }

            File.SetAttributes(
                filePath,
                attributes & ~FileAttributes.ReadOnly);
        }
    }
}