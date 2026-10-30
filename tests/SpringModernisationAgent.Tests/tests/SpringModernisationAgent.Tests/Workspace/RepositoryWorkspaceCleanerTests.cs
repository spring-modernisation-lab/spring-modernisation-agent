using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Workspace;

public class RepositoryWorkspaceCleanerTests
{
    [Fact]
    public async Task DeleteAsync_ExistingWorkspace_DeletesWorkspace()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        var workspacePath = Path.Combine(
            workspaceRoot,
            "customer-service-abc123");

        Directory.CreateDirectory(workspacePath);

        await File.WriteAllTextAsync(
            Path.Combine(workspacePath, "pom.xml"),
            "test");

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            await cleaner.DeleteAsync(workspace);

            Assert.False(Directory.Exists(workspacePath));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteAsync_MissingWorkspace_DoesNotThrow()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        var workspacePath = Path.Combine(
            workspaceRoot,
            "missing-workspace");

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            var exception = await Record.ExceptionAsync(
                () => cleaner.DeleteAsync(workspace));

            Assert.Null(exception);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteAsync_WorkspaceOutsideRoot_ThrowsInvalidOperationException()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        var outsideRoot = CreateTemporaryDirectory();

        try
        {
            var workspacePath = Path.Combine(
                outsideRoot,
                "customer-service");

            Directory.CreateDirectory(workspacePath);

            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => cleaner.DeleteAsync(workspace));

            Assert.Contains(
                "outside the permitted workspace root",
                exception.Message);

            Assert.True(Directory.Exists(workspacePath));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
            DeleteDirectory(outsideRoot);
        }
    }

    [Fact]
    public async Task DeleteAsync_SimilarPrefixOutsideRoot_ThrowsInvalidOperationException()
    {
        var parent = CreateTemporaryDirectory();

        var workspaceRoot = Path.Combine(
            parent,
            "workspaces");

        var similarRoot = Path.Combine(
            parent,
            "workspaces-evil");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(similarRoot);

        var workspacePath = Path.Combine(
            similarRoot,
            "customer-service");

        Directory.CreateDirectory(workspacePath);

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cleaner.DeleteAsync(workspace));

            Assert.True(Directory.Exists(workspacePath));
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task DeleteAsync_ReadOnlyFile_DeletesWorkspace()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        var workspacePath = Path.Combine(
            workspaceRoot,
            "customer-service");

        Directory.CreateDirectory(workspacePath);

        var filePath = Path.Combine(
            workspacePath,
            "readonly.txt");

        await File.WriteAllTextAsync(
            filePath,
            "test");

        File.SetAttributes(
            filePath,
            File.GetAttributes(filePath)
            | FileAttributes.ReadOnly);

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            await cleaner.DeleteAsync(workspace);

            Assert.False(Directory.Exists(workspacePath));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.SetAttributes(
                    filePath,
                    FileAttributes.Normal);
            }

            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var workspaceRoot = CreateTemporaryDirectory();
        var workspacePath = Path.Combine(
            workspaceRoot,
            "customer-service");

        Directory.CreateDirectory(workspacePath);

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            var workspace = CreateWorkspace(workspacePath);

            using var cancellationTokenSource =
                new CancellationTokenSource();

            cancellationTokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => cleaner.DeleteAsync(
                    workspace,
                    cancellationTokenSource.Token));

            Assert.True(Directory.Exists(workspacePath));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task DeleteAsync_NullWorkspace_ThrowsArgumentNullException()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var cleaner = new RepositoryWorkspaceCleaner(
                workspaceRoot);

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => cleaner.DeleteAsync(null!));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public void Constructor_EmptyWorkspaceRoot_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new RepositoryWorkspaceCleaner(
                string.Empty));
    }

    private static RepositoryWorkspace CreateWorkspace(
        string workspacePath)
    {
        return new RepositoryWorkspace(
            RepositoryName: "customer-service",
            RepositoryPath: workspacePath,
            CommitSha:
                "078718f4100ea1340e9a175c13c50bbf71806ae3");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(
                filePath,
                FileAttributes.Normal);
        }

        Directory.Delete(
            path,
            recursive: true);
    }
}