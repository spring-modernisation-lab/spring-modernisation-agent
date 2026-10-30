namespace SpringModernisationAgent.Build;

public interface IMavenProcessRunner
{
    Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}