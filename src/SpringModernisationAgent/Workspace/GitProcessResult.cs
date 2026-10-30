namespace SpringModernisationAgent.Workspace;

public sealed record GitProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);