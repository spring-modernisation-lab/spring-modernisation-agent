namespace SpringModernisationAgent.Build;

public sealed class MavenProcessRunner : IMavenProcessRunner
{
    public Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        ExternalProcess.RunAsync(
            "mvn",
            "Maven",
            workingDirectory,
            arguments,
            cancellationToken);
}
