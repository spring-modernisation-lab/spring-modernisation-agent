using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Workspace;

public sealed partial class GitClient : IGitClient
{
    private static string DisabledHooksPath =>
        OperatingSystem.IsWindows()
            ? "NUL"
            : "/dev/null";

    public async Task CloneAsync(
        string repositoryUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestinationPath = Path.GetFullPath(destinationPath);

        if (Directory.Exists(fullDestinationPath) &&
            Directory.EnumerateFileSystemEntries(fullDestinationPath).Any())
        {
            throw new InvalidOperationException(
                $"Git clone destination '{fullDestinationPath}' is not empty.");
        }

        var parentDirectory = Path.GetDirectoryName(fullDestinationPath);

        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await RunGitAsync(
            workingDirectory: parentDirectory ?? Directory.GetCurrentDirectory(),
            arguments:
            [
                "clone",
                "--no-checkout",
                "--no-recurse-submodules",
                "--",
                repositoryUrl,
                fullDestinationPath
            ],
            cancellationToken);
    }

    public async Task CheckoutAsync(
        string repositoryPath,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        ValidateCommitSha(commitSha);

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(fullRepositoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Git repository directory was not found: {fullRepositoryPath}");
        }

        await RunGitAsync(
            workingDirectory: fullRepositoryPath,
            arguments:
            [
                "checkout",
                "--detach",
                commitSha
            ],
            cancellationToken);
    }

    public async Task<string> GetHeadShaAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(fullRepositoryPath))
        {
            throw new DirectoryNotFoundException(
                $"Git repository directory was not found: {fullRepositoryPath}");
        }

        var result = await RunGitAsync(
            workingDirectory: fullRepositoryPath,
            arguments:
            [
                "rev-parse",
                "HEAD"
            ],
            cancellationToken);

        var commitSha = result.StandardOutput.Trim();

        ValidateCommitSha(commitSha);

        return commitSha;
    }

    private static async Task<GitProcessResult> RunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Defense in depth for repository-controlled Git state.
        // Do not execute repository hooks and do not permit file:// transport.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.hooksPath={DisabledHooksPath}");

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("protocol.file.allow=never");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "The Git process could not be started.");
            }

            var standardOutputTask =
                process.StandardOutput.ReadToEndAsync(cancellationToken);

            var standardErrorTask =
                process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                throw;
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git operation failed with exit code {process.ExitCode}. " +
                    standardError.Trim());
            }

            return new GitProcessResult(
                process.ExitCode,
                standardOutput,
                standardError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Git could not execute the requested operation.",
                exception);
        }
    }

    private static void ValidateCommitSha(string commitSha)
    {
        if (!CommitShaRegex().IsMatch(commitSha))
        {
            throw new ArgumentException(
                "The Git commit SHA must contain exactly 40 hexadecimal characters.",
                nameof(commitSha));
        }
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
            // The process exited between checking HasExited and killing it.
        }
    }

    [GeneratedRegex(
        "^[0-9a-fA-F]{40}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    private sealed record GitProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
