using System.Text.Json.Serialization;

namespace SpringModernisationAgent.Remediation;

/// <summary>What the Remediation Agent decided about a validation failure.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemediationDecision>))]
public enum RemediationDecision
{
    /// <summary>The agent proposes edits that should fix the failure.</summary>
    Fix,

    /// <summary>The agent cannot find a safe fix within its limits.</summary>
    CannotFix,

    /// <summary>
    /// A fix exists but changes behaviour or a contract (an API, a
    /// dependency, a test expectation), so a person must choose.
    /// </summary>
    NeedsHumanDecision
}

/// <summary>
/// The agent's only output. It never writes files: C# validates these edits
/// and applies all of them or none, then Maven decides whether they worked.
/// </summary>
public sealed record RemediationProposal(
    RemediationDecision Decision,
    string Diagnosis,
    string RootCause,
    IReadOnlyList<RemediationEdit> Edits,
    IReadOnlyList<string> Options);

/// <summary>
/// One exact search/replace in a repository-relative file. An empty
/// <paramref name="Search"/> creates a new file with <paramref name="Replace"/>
/// as its content.
/// </summary>
public sealed record RemediationEdit(
    string Path,
    string Search,
    string Replace,
    string Rationale);

/// <param name="Applied">True only if every edit passed every guardrail and was written.</param>
/// <param name="RejectionReason">Why the proposal was rejected, fed back to the next attempt.</param>
/// <param name="ChangedFiles">Repository-relative files written, empty when rejected.</param>
public sealed record RemediationApplyResult(
    bool Applied,
    string? RejectionReason,
    IReadOnlyList<string> ChangedFiles);
