namespace SpringModernisationAgent.Build;

/// <summary>Runs the JDK's <c>javap</c> class file disassembler.</summary>
public interface IJavapRunner
{
    Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class JavapRunner : IJavapRunner
{
    public Task<MavenProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        ExternalProcess.RunAsync(
            "javap",
            "javap",
            workingDirectory,
            arguments,
            cancellationToken);
}
