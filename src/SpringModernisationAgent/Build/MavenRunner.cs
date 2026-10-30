using System.Diagnostics;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Build;

public sealed class MavenRunner : IMavenRunner
{
    public async Task<BaselineBuildResult> RunBaselineBuildAsync(
        string repositoryName,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var fullRepositoryPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(fullRepositoryPath))
        {
            return NotRun(
                repositoryName,
                $"Repository directory was not found: {fullRepositoryPath}");
        }

        var wrapperPath = FindMavenWrapper(fullRepositoryPath);

        if (wrapperPath is null)
        {
            return NotRun(
                repositoryName,
                "The repository does not contain mvnw or mvnw.cmd.");
        }

        var startedAt = DateTimeOffset.UtcNow;

        var startInfo = CreateStartInfo(
            wrapperPath,
            fullRepositoryPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(
                cancellationToken);

            var standardErrorTask = process.StandardError.ReadToEndAsync(
                cancellationToken);

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
            var completedAt = DateTimeOffset.UtcNow;

            return new BaselineBuildResult(
                RepositoryName: repositoryName,
                Status: process.ExitCode == 0
                    ? BaselineBuildStatus.Passed
                    : BaselineBuildStatus.Failed,
                ExitCode: process.ExitCode,
                StartedAt: startedAt,
                CompletedAt: completedAt,
                StandardOutput: standardOutput,
                StandardError: standardError);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new BaselineBuildResult(
                RepositoryName: repositoryName,
                Status: BaselineBuildStatus.NotRun,
                ExitCode: null,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow,
                StandardOutput: null,
                StandardError: exception.Message);
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string wrapperPath,
        string repositoryPath)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = repositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            startInfo.FileName = "cmd.exe";
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(wrapperPath);
        }
        else
        {
            startInfo.FileName = wrapperPath;
        }

        startInfo.ArgumentList.Add("-B");
        startInfo.ArgumentList.Add("clean");
        startInfo.ArgumentList.Add("verify");

        return startInfo;
    }

    private static string? FindMavenWrapper(string repositoryPath)
    {
        var wrapperName = OperatingSystem.IsWindows()
            ? "mvnw.cmd"
            : "mvnw";

        var wrapperPath = Path.Combine(
            repositoryPath,
            wrapperName);

        return File.Exists(wrapperPath)
            ? wrapperPath
            : null;
    }

    private static BaselineBuildResult NotRun(
        string repositoryName,
        string reason)
    {
        return new BaselineBuildResult(
            RepositoryName: repositoryName,
            Status: BaselineBuildStatus.NotRun,
            ExitCode: null,
            StartedAt: null,
            CompletedAt: null,
            StandardOutput: null,
            StandardError: reason);
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
            // The process exited between the check and kill call.
        }
    }
}