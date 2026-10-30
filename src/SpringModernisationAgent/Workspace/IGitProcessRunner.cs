namespace SpringModernisationAgent.Workspace;

public interface IGitProcessRunner
{
    Task<GitProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}