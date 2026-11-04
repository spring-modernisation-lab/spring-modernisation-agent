using System.Security.Cryptography;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Transformation;

/// <summary>
/// Runs against real git repositories in temporary directories: the patch
/// must be byte-exact and re-appliable, which a fake cannot prove.
/// </summary>
public class GitWorkspacePatchRecorderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"patch-recorder-{Guid.NewGuid():N}");

    private readonly GitProcessRunner _git = new();

    private string RepositoryPath => Path.Combine(_root, "repository");

    private string ArtifactsPath => Path.Combine(_root, "Artifacts");

    [Fact]
    public async Task RecordAsync_ModifiedAndUntrackedFiles_WritesPatchThatReappliesExactly()
    {
        var baseCommit = await CreateRepositoryAsync();

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "pom.xml"),
            "<project>3.5.16</project>\n");

        Directory.CreateDirectory(Path.Combine(RepositoryPath, "src"));

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "src", "New.java"),
            "class New {}\n");

        var recorded = await CreateRecorder().RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "01-openrewrite");

        Assert.Equal("01-openrewrite", recorded.Artifact.Stage);

        Assert.Equal(
            "patches/orders-service/01-openrewrite.patch",
            recorded.Artifact.RelativePath);

        Assert.Equal(
            ["pom.xml", "src/New.java"],
            recorded.Artifact.ChangedFiles);

        var patchPath = Path.Combine(
            ArtifactsPath,
            "patches",
            "orders-service",
            "01-openrewrite.patch");

        Assert.Equal(
            Convert.ToHexStringLower(
                SHA256.HashData(await File.ReadAllBytesAsync(patchPath))),
            recorded.Artifact.Sha256);

        // The patch re-applies on a clean checkout of the base commit.
        var clonePath = await CloneAtAsync(baseCommit);

        await RunGitAsync(clonePath, "apply", "--index", patchPath);

        Assert.Equal(
            "<project>3.5.16</project>\n",
            await File.ReadAllTextAsync(Path.Combine(clonePath, "pom.xml")));

        Assert.Equal(
            "class New {}\n",
            await File.ReadAllTextAsync(
                Path.Combine(clonePath, "src", "New.java")));
    }

    [Fact]
    public async Task RecordAsync_BinaryFile_IsPreservedByteForByte()
    {
        var baseCommit = await CreateRepositoryAsync();

        byte[] binary = [0x00, 0xFF, 0x10, 0x80, 0x00, 0x7F, 0xC3, 0x28];

        await File.WriteAllBytesAsync(
            Path.Combine(RepositoryPath, "logo.bin"),
            binary);

        var recorded = await CreateRecorder().RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "01-openrewrite");

        var patchPath = Path.Combine(ArtifactsPath, recorded.Artifact.RelativePath);

        Assert.Contains(
            "GIT binary patch",
            await File.ReadAllTextAsync(patchPath));

        var clonePath = await CloneAtAsync(baseCommit);

        await RunGitAsync(clonePath, "apply", "--index", patchPath);

        Assert.Equal(
            binary,
            await File.ReadAllBytesAsync(Path.Combine(clonePath, "logo.bin")));
    }

    [Fact]
    public async Task RecordAsync_ChainedStages_SecondPatchContainsOnlyItsOwnChanges()
    {
        var baseCommit = await CreateRepositoryAsync();
        var recorder = CreateRecorder();

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "pom.xml"),
            "<project>3.5.16</project>\n");

        var first = await recorder.RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "01-openrewrite");

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "Fix.java"),
            "class Fix {}\n");

        var second = await recorder.RecordAsync(
            RepositoryPath,
            "orders-service",
            first.TreeId,
            "02-remediation-attempt-1");

        var cumulative = await recorder.RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "cumulative");

        Assert.Equal(["pom.xml"], first.Artifact.ChangedFiles);
        Assert.Equal(["Fix.java"], second.Artifact.ChangedFiles);
        Assert.Equal(["Fix.java", "pom.xml"], cumulative.Artifact.ChangedFiles);
        Assert.Equal(second.TreeId, cumulative.TreeId);
    }

    [Fact]
    public async Task RecordAsync_NoChanges_WritesEmptyPatch()
    {
        var baseCommit = await CreateRepositoryAsync();

        var recorded = await CreateRecorder().RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "01-openrewrite");

        Assert.Empty(recorded.Artifact.ChangedFiles);

        Assert.Equal(
            0,
            new FileInfo(Path.Combine(
                ArtifactsPath,
                recorded.Artifact.RelativePath)).Length);
    }

    [Fact]
    public async Task RecordAsync_IgnoredBuildOutput_IsExcluded()
    {
        var baseCommit = await CreateRepositoryAsync(gitignore: "target/\n");

        Directory.CreateDirectory(Path.Combine(RepositoryPath, "target"));

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "target", "App.class"),
            "compiled");

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "pom.xml"),
            "<project>3.5.16</project>\n");

        var recorded = await CreateRecorder().RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "01-openrewrite");

        Assert.Equal(["pom.xml"], recorded.Artifact.ChangedFiles);
    }

    [Fact]
    public async Task RecordAsync_BuildOutputWithoutGitignore_IsStillExcluded()
    {
        // Remediation attempts are recorded after Maven has already run.
        var baseCommit = await CreateRepositoryAsync();

        foreach (var directory in new[] { "target", Path.Combine("module", "target") })
        {
            Directory.CreateDirectory(Path.Combine(RepositoryPath, directory, "classes"));

            await File.WriteAllTextAsync(
                Path.Combine(RepositoryPath, directory, "classes", "App.class"),
                "compiled");
        }

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "pom.xml"),
            "<project>3.5.16</project>\n");

        var recorded = await CreateRecorder().RecordAsync(
            RepositoryPath,
            "orders-service",
            baseCommit,
            "02-remediation-attempt-1");

        Assert.Equal(["pom.xml"], recorded.Artifact.ChangedFiles);
        Assert.True(File.Exists(recorded.FullPath));
    }

    [Theory]
    [InlineData("..", "01-openrewrite")]
    [InlineData("orders/service", "01-openrewrite")]
    [InlineData("orders-service", "../escape")]
    [InlineData("orders-service", "")]
    [InlineData("", "01-openrewrite")]
    public async Task RecordAsync_UnsafeNames_Throw(
        string repositoryName,
        string stage)
    {
        var baseCommit = await CreateRepositoryAsync();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => CreateRecorder().RecordAsync(
                RepositoryPath,
                repositoryName,
                baseCommit,
                stage));
    }

    [Fact]
    public async Task RecordAsync_GitFails_ThrowsInvalidOperationException()
    {
        await CreateRepositoryAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateRecorder().RecordAsync(
                RepositoryPath,
                "orders-service",
                "0000000000000000000000000000000000000000",
                "01-openrewrite"));
    }

    [Fact]
    public async Task RestoreAsync_PutsOnlyTheNamedFilesBackAsTheyWereInTheTree()
    {
        // A reverted port: its files go back to the task's base tree, nothing else moves.
        var baseCommit = await CreateRepositoryAsync();
        Directory.CreateDirectory(Path.Combine(RepositoryPath, "src"));
        await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Port.java"), "class Port { /* recipes */ }\n");
        await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Other.java"), "class Other {}\n");
        var taskBase = await CreateRecorder().RecordAsync(RepositoryPath, "orders-service", baseCommit, "01-openrewrite");

        await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Port.java"), "class Port { /* AI */ }\n");
        await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "New.java"), "class New {}\n");
        await File.WriteAllTextAsync(Path.Combine(RepositoryPath, "src", "Other.java"), "class Other { /* another task */ }\n");

        await CreateRecorder().RestoreAsync(RepositoryPath, taskBase.TreeId, ["src/Port.java", "src/New.java"]);

        Assert.Equal("class Port { /* recipes */ }\n", await File.ReadAllTextAsync(Path.Combine(RepositoryPath, "src", "Port.java")));
        Assert.False(File.Exists(Path.Combine(RepositoryPath, "src", "New.java")));
        Assert.Equal("class Other { /* another task */ }\n", await File.ReadAllTextAsync(Path.Combine(RepositoryPath, "src", "Other.java")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/x.txt")]
    public async Task RestoreAsync_APathOutsideTheRepositoryIsRefused(
        string path)
    {
        var baseCommit = await CreateRepositoryAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateRecorder().RestoreAsync(RepositoryPath, baseCommit, [path]));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // Git marks object files read-only, which blocks deletion on Windows.
        foreach (var file in Directory.EnumerateFiles(
                     _root,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private GitWorkspacePatchRecorder CreateRecorder() =>
        new(_git, ArtifactsPath);

    private async Task<string> CreateRepositoryAsync(
        string? gitignore = null)
    {
        Directory.CreateDirectory(RepositoryPath);

        await RunGitAsync(RepositoryPath, "init", "--quiet");
        await RunGitAsync(RepositoryPath, "config", "core.autocrlf", "false");
        await RunGitAsync(RepositoryPath, "config", "user.name", "Test");
        await RunGitAsync(RepositoryPath, "config", "user.email", "test@example.invalid");
        await RunGitAsync(RepositoryPath, "config", "commit.gpgsign", "false");

        await File.WriteAllTextAsync(
            Path.Combine(RepositoryPath, "pom.xml"),
            "<project>2.7.18</project>\n");

        if (gitignore is not null)
        {
            await File.WriteAllTextAsync(
                Path.Combine(RepositoryPath, ".gitignore"),
                gitignore);
        }

        await RunGitAsync(RepositoryPath, "add", "-A");
        await RunGitAsync(RepositoryPath, "commit", "--quiet", "-m", "Initial");

        return (await RunGitAsync(RepositoryPath, "rev-parse", "HEAD")).Trim();
    }

    private async Task<string> CloneAtAsync(string commit)
    {
        var clonePath = Path.Combine(_root, $"clone-{Guid.NewGuid():N}");

        await RunGitAsync(_root, "clone", "--quiet", "--no-checkout", RepositoryPath, clonePath);
        await RunGitAsync(clonePath, "config", "core.autocrlf", "false");
        await RunGitAsync(clonePath, "checkout", "--quiet", commit);

        return clonePath;
    }

    private async Task<string> RunGitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        // The hardened runner disables the file protocol, which local-path
        // clones use; re-enable it (the later -c wins) for test clones only.
        var result = await _git.RunAsync(
            workingDirectory,
            arguments[0] == "clone"
                ? ["-c", "protocol.file.allow=always", .. arguments]
                : arguments);

        Assert.True(
            result.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {result.StandardError}");

        return result.StandardOutput;
    }
}
