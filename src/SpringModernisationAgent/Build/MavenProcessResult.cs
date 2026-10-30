namespace SpringModernisationAgent.Build;

public sealed record MavenProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);