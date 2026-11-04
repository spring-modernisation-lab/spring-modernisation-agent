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
    IReadOnlyList<RemediationAttemptRecord> Attempts)
{
    /// <summary>The scoped port tasks, in the order they were worked (docs/system-design.md §4.1).</summary>
    public IReadOnlyList<PortTaskRecord> Tasks { get; init; } = [];

    /// <summary>Problems no task worked, with why: they go to a person.</summary>
    public IReadOnlyList<string> LeftForAPerson { get; init; } = [];
}

/// <summary>How a scoped port ended.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PortTaskStatus>))]
public enum PortTaskStatus
{
    /// <summary>Its errors are gone, nothing new fails, and its obligations hold (or could not be checked).</summary>
    Accepted,

    /// <summary>Not kept after one attempt and one repair: its files are as the recipes left them.</summary>
    Reverted,

    /// <summary>The AI said it cannot port it within the rules.</summary>
    CannotFix,

    /// <summary>The model or the pipeline failed; the run stopped.</summary>
    Error
}

/// <summary>One scoped port and what became of it.</summary>
/// <param name=Mapping>The API mapping (<c>M-…</c>), which is also the task's ID.</param>
/// <param name=Files>The only files the AI could change.</param>
/// <param name=Problems>The classified problems it covered (<c>P…</c>).</param>
/// <param name=Reason>Why it was not accepted, when it was not.</param>
/// <param name=Attempts>Its attempt numbers in <see cref="RemediationReport.Attempts"/>.</param>
public sealed record PortTaskRecord(
    string Mapping,
    string Name,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Problems,
    PortTaskStatus Status,
    string? Reason,
    IReadOnlyList<int> Attempts)
{
    /// <summary>Obligation items unchanged from the baseline.</summary>
    public IReadOnlyList<string> ObligationsKept { get; init; } = [];

    /// <summary>Obligation items that changed, unexplained, as <c>item: before → after</c>.</summary>
    public IReadOnlyList<string> ObligationsBroken { get; init; } = [];

    /// <summary>Obligation items that changed because of the platform: decisions, not the port's.</summary>
    public IReadOnlyList<string> ObligationsExplained { get; init; } = [];

    /// <summary>Obligation items that could not be read, or why none could be checked.</summary>
    public IReadOnlyList<string> ObligationsUnchecked { get; init; } = [];
}

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
    long? OutputTokens)
{
    /// <summary>What each applied edit did, in proposal order; empty if not applied.</summary>
    public IReadOnlyList<AppliedEdit> AppliedEdits { get; init; } = [];

    /// <summary>The port task (its mapping) this attempt worked on, if any.</summary>
    public string? Task { get; init; }

    /// <summary>Why the attempt was not kept (it was reverted), if it was applied and not kept.</summary>
    public string? NotKept { get; init; }
}
