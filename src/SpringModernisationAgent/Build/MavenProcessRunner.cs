namespace SpringModernisationAgent.Build;

/// <param name="environment">
/// Extra environment for Maven, for example <c>JAVA_HOME</c> to build the
/// Boot 2.7 baseline with the JDK it was made for.
/// </param>
public sealed class MavenProcessRunner(
    IReadOnlyDictionary<string, string>? environment = null) : IMavenProcessRunner
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
            cancellationToken,
            environment);
}
