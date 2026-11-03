using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Transformation;

/// <summary>
/// Records workspace changes without committing: <c>git add -A</c> stages
/// everything not ignored, <c>git write-tree</c> turns the index into a tree
/// id, and <c>git diff --binary</c> between the base revision and that tree
/// is written straight to a file by git, so the patch is byte-exact.
/// </summary>
public sealed partial class GitWorkspacePatchRecorder : IWorkspacePatchRecorder
{
    private readonly IGitProcessRunner _gitProcessRunner;
    private readonly string _artifactsDirectory;

    public GitWorkspacePatchRecorder(
        IGitProcessRunner gitProcessRunner,
        string artifactsDirectory)
    {
        ArgumentNullException.ThrowIfNull(gitProcessRunner);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsDirectory);

        _gitProcessRunner = gitProcessRunner;
        _artifactsDirectory = Path.GetFullPath(artifactsDirectory);
    }

    public async Task<RecordedPatch> RecordAsync(
        string repositoryPath,
        string repositoryName,
        string baseRevision,
        string stage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRevision);
        EnsureSafeName(repositoryName, nameof(repositoryName));
        EnsureSafeName(stage, nameof(stage));

        await RunAsync(
            repositoryPath,
            ["add", "-A"],
            "Git could not stage the workspace changes.",
            cancellationToken);

        var treeId = (await RunAsync(
            repositoryPath,
            ["write-tree"],
            "Git could not snapshot the workspace.",
            cancellationToken)).Trim();

        var relativePath = $"patches/{repositoryName}/{stage}.patch";

        var patchPath = Path.Combine(
            _artifactsDirectory,
            "patches",
            repositoryName,
            $"{stage}.patch");

        Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);

        // Never run repository-configured diff drivers or text conversion.
        await RunAsync(
            repositoryPath,
            [
                "diff",
                "--binary",
                "--no-color",
                "--no-ext-diff",
                "--no-textconv",
                $"--output={patchPath}",
                baseRevision,
                treeId
            ],
            "Git could not write the workspace patch.",
            cancellationToken);

        var changedFiles = (await RunAsync(
                repositoryPath,
                ["diff", "--name-only", "-z", "--no-renames", baseRevision, treeId],
                "Git could not list the patch's changed files.",
                cancellationToken))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToList();

        var sha256 = Convert.ToHexStringLower(
            SHA256.HashData(
                await File.ReadAllBytesAsync(patchPath, cancellationToken)));

        return new RecordedPatch(
            treeId,
            new PatchArtifact(
                Stage: stage,
                RelativePath: relativePath,
                Sha256: sha256,
                ChangedFiles: changedFiles));
    }

    private async Task<string> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var result = await _gitProcessRunner.RunAsync(
            repositoryPath,
            arguments,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(result.StandardError)
                ? string.Empty
                : $" {result.StandardError.Trim()}";

            throw new InvalidOperationException(
                $"{failureMessage} Git exited with code {result.ExitCode}.{details}");
        }

        return result.StandardOutput;
    }

    private static void EnsureSafeName(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!SafeNamePattern().IsMatch(value) || value is "." or "..")
        {
            throw new ArgumentException(
                $"'{value}' is not a safe artifact path segment.",
                parameterName);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex SafeNamePattern();
}
