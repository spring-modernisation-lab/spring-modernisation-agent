using System.Diagnostics;

namespace SpringModernisationAgent.Workspace;

public sealed class GitProcessRunner : IGitProcessRunner
{
    public async Task<GitProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Git working directory was not found: {workingDirectory}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Apply our Git hardening to every invocation.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            $"core.hooksPath={DisabledHooksPath}");

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "protocol.file.allow=never");

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

            return new GitProcessResult(
                ExitCode: process.ExitCode,
                StandardOutput: await standardOutputTask,
                StandardError: await standardErrorTask);
        }
        catch (OperationCanceledException)
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

    private static string DisabledHooksPath =>
        OperatingSystem.IsWindows()
            ? "NUL"
            : "/dev/null";

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
            // Process exited between the check and kill.
        }
    }
}