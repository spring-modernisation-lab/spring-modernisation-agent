using System.Diagnostics;

namespace SpringModernisationAgent.Build;

/// <summary>
/// Runs a command-line tool without a shell: arguments are passed as a
/// list, output is captured, and cancellation kills the process tree.
/// </summary>
internal static class ExternalProcess
{
    public static async Task<MavenProcessResult> RunAsync(
        string fileName,
        string displayName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"{displayName} working directory was not found: {workingDirectory}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Secrets go through the environment, never the argument list.
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
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
                    $"The {displayName} process could not be started.");
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

            return new MavenProcessResult(
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
                $"{displayName} could not execute the requested operation.",
                exception);
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
            // Process exited between the check and kill.
        }
    }
}
