using System.Text.Json.Serialization;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Models;

/// <summary>How a remediation run ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemediationOutcome>))]
public enum RemediationOutcome
{
    /// <summary>Validation passed after OpenRewrite; no remediation was needed.</summary>
    NotNeeded,

    /// <summary>An applied proposal made validation pass.</summary>
    Fixed,

    /// <summary>The agent found no safe fix.</summary>
    CannotFix,

    /// <summary>The agent reported that the fix needs a person's decision.</summary>
    NeedsHumanDecision,

    /// <summary>An attempt left exactly the same failure as the one before.</summary>
    NoProgress,

    /// <summary>Every allowed attempt was used and validation still fails.</summary>
    AttemptsExhausted,

    /// <summary>The agent or the attempt infrastructure failed; the deterministic result stands.</summary>
    Error
}

/// <summary>
/// What the Remediation Agent did for one repository. The repository's
/// success is still decided only by its final validation result.
/// </summary>
public sealed record RemediationReport(
    RemediationOutcome Outcome,
    IReadOnlyList<RemediationAttemptRecord> Attempts);

/// <summary>One attempt, with everything needed to audit it.</summary>
/// <param name="Number">1-based attempt number.</param>
/// <param name="FailedCheck">The validation check the attempt tried to fix.</param>
/// <param name="Proposal">The agent's proposal, if it produced a valid one.</param>
/// <param name="Applied">Whether the guardrails accepted and applied the edits.</param>
/// <param name="RejectionReason">Why the guardrails rejected the edits, if they did.</param>
/// <param name="Error">Why the attempt failed before a proposal could be applied, if it did.</param>
/// <param name="Patch">The patch of this attempt's edits, if applied.</param>
/// <param name="Validation">Validation after the edits, if applied.</param>
/// <param name="ToolCalls">Every tool call the agent made in this attempt.</param>
public sealed record RemediationAttemptRecord(
    int Number,
    string FailedCheck,
    RemediationProposal? Proposal,
    bool Applied,
    string? RejectionReason,
    string? Error,
    PatchArtifact? Patch,
    TransformationValidationResult? Validation,
    IReadOnlyList<RemediationToolCall> ToolCalls,
    long? InputTokens,
    long? OutputTokens);
