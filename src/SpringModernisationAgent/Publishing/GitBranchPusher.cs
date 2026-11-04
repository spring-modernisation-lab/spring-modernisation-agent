using System.Text;
using System.Text.RegularExpressions;
using SpringModernisationAgent.Build;

namespace SpringModernisationAgent.Publishing;

/// <summary>Runs git with extra environment variables (for credentials).</summary>
public interface IGitCommandRunner
{
    Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}

/// <summary>git with hooks disabled and local file transport refused, as elsewhere.</summary>
public sealed class GitCommandRunner : IGitCommandRunner
{
    public Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default)
    {
        var hooks = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        return ExternalProcess.RunAsync(
            "git",
            "git",
            workingDirectory,
            ["-c", $"core.hooksPath={hooks}", "-c", "protocol.file.allow=never", .. arguments],
            cancellationToken,
            environment);
    }
}

/// <summary>
/// Clones the assessed commit, applies the cumulative patch, commits it and
/// force-pushes it to the migration branch. The token is given to git as an
/// HTTP header through environment configuration (as actions/checkout does),
/// so it never appears in arguments, URLs, logs or exception messages.
/// </summary>
public sealed partial class GitBranchPusher : IBranchPusher
{
    private const string BotName = "Spring Modernisation Agent";
    private const string BotEmail = "spring-modernisation-agent@users.noreply.github.com";

    private readonly IGitCommandRunner _git;
    private readonly string _workspaceRoot;
    private readonly IReadOnlyDictionary<string, string> _environment;
    private readonly string _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Push attempts when GitHub answers with a server error.</summary>
    public const int MaxPushAttempts = 3;

    private static readonly string[] TransientPushErrors =
    [
        "Internal Server Error", "502", "503", "504", "Service Unavailable", "Bad Gateway",
        "remote end hung up", "RPC failed", "Connection reset", "timed out"
    ];

    public GitBranchPusher(
        IGitCommandRunner git,
        string workspaceRoot,
        string token,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _git = git;
        _workspaceRoot = workspaceRoot;
        _token = token;
        _delay = delay ?? Task.Delay;

        var header = "AUTHORIZATION: basic " +
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));

        _environment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = header,
            ["GIT_TERMINAL_PROMPT"] = "0"
        };
    }

    public async Task PushAsync(
        string owner,
        string repository,
        string sourceSha,
        string patchPath,
        string branch,
        string message,
        CancellationToken cancellationToken = default)
    {
        Require(Identifier().IsMatch(owner), nameof(owner), owner);
        Require(Identifier().IsMatch(repository), nameof(repository), repository);
        Require(CommitSha().IsMatch(sourceSha), nameof(sourceSha), sourceSha);
        Require(branch.StartsWith("migration/", StringComparison.Ordinal) && !branch.Contains(".."), nameof(branch), branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(patchPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Directory.CreateDirectory(_workspaceRoot);
        var directory = Path.Combine(_workspaceRoot, $"{repository}-publish-{Guid.NewGuid():N}");

        try
        {
            await RunAsync(_workspaceRoot, "clone",
                ["clone", "--quiet", "--no-checkout", $"https://github.com/{owner}/{repository}.git", directory],
                cancellationToken);

            await RunAsync(directory, "checkout", ["checkout", "--detach", "--quiet", sourceSha], cancellationToken);
            await RunAsync(directory, "apply", ["apply", "--index", "--whitespace=nowarn", patchPath], cancellationToken);

            await RunAsync(directory, "commit",
            [
                "-c", $"user.name={BotName}",
                "-c", $"user.email={BotEmail}",
                "-c", "commit.gpgsign=false",
                "commit", "--quiet", "-m", message
            ], cancellationToken);

            // GitHub sometimes answers a push with a server error (run 21).
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await RunAsync(directory, "push",
                        ["push", "--force", "--quiet", "origin", $"HEAD:refs/heads/{branch}"],
                        cancellationToken);
                    break;
                }
                catch (InvalidOperationException exception)
                    when (attempt < MaxPushAttempts
                          && TransientPushErrors.Any(error => exception.Message.Contains(error, StringComparison.OrdinalIgnoreCase)))
                {
                    await _delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
                }
            }
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private async Task RunAsync(
        string workingDirectory,
        string step,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(workingDirectory, arguments, _environment, cancellationToken);

        if (result.ExitCode != 0)
        {
            var details = (result.StandardError + " " + result.StandardOutput)
                .Replace(_token, "***", StringComparison.Ordinal)
                .Trim();

            throw new InvalidOperationException(
                $"git {step} failed (exit code {result.ExitCode}): {details}");
        }
    }

    private static void Require(
        bool valid,
        string name,
        string value)
    {
        if (!valid)
        {
            throw new ArgumentException($"'{value}' is not a valid {name}.", name);
        }
    }

    private static void DeleteDirectory(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^[0-9a-f]{7,40}$")]
    private static partial Regex CommitSha();
}
