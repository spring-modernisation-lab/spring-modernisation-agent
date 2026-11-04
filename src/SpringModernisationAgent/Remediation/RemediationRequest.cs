namespace SpringModernisationAgent.Remediation;

/// <summary>The evidence the Remediation Agent is given for one attempt.</summary>
public sealed record RemediationRequest(
    string RepositoryName,
    string SpringBootVersion,
    string JavaVersion,
    string FailedCheck,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<RemediationAttemptSummary> PreviousAttempts)
{
    /// <summary>Libraries the repository uses that the target platform dropped.</summary>
    public IReadOnlyList<UnsupportedLibrary> UnsupportedLibraries { get; init; } = [];
}

/// <summary>
/// What happened to an earlier attempt in the same run, so the agent can
/// correct itself: for example a guardrail rejection reason, or the check
/// that still failed after its edits were applied.
/// </summary>
public sealed record RemediationAttemptSummary(
    int Attempt,
    string Outcome,
    string Detail);
