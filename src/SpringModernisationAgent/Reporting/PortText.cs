using System.Text;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Reporting;

/// <summary>The same wording for scoped AI ports in the job summary, the PR and the console.</summary>
public static class PortText
{
    /// <summary>"2 accepted, 1 reverted (3 attempts)".</summary>
    public static string Counts(
        RemediationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var counts = report.Tasks
            .GroupBy(task => task.Status)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count()} {Describe(group.Key)}");

        var attempts = report.Attempts.Count;

        return report.Tasks.Count == 0
            ? $"no ports ({report.Outcome})"
            : $"{string.Join(", ", counts)} ({attempts} attempt{(attempts == 1 ? "" : "s")})";
    }

    /// <summary>One row per task, as a Markdown table (empty when there are none).</summary>
    public static string Tasks(
        RemediationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Tasks.Count == 0)
        {
            return string.Empty;
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("| Port | Files | Attempts | Outcome | Behaviour it must keep |");
        markdown.AppendLine("|---|---|---|---|---|");

        foreach (var task in report.Tasks)
        {
            markdown.AppendLine(
                $"| `{task.Mapping}` {Cell(task.Name)} | {Cell(string.Join(", ", task.Files.Select(Path.GetFileName)))} | " +
                $"{string.Join(", ", task.Attempts)} | {Cell(Outcome(task))} | {Cell(Behaviour(task))} |");
        }

        return markdown.ToString();
    }

    /// <summary>Every port that was not kept and every problem no port worked: a person's list.</summary>
    public static IReadOnlyList<string> ForAPerson(
        RemediationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return
        [
            .. report.Tasks
                .Where(task => task.Status != PortTaskStatus.Accepted)
                .Select(task => $"`{task.Mapping}` ({string.Join(", ", task.Files.Select(Path.GetFileName))}): {Outcome(task)}"),
            .. report.LeftForAPerson
        ];
    }

    /// <summary>The options the AI gave when it could not port a task (AI-generated).</summary>
    public static IReadOnlyList<(string Mapping, string Diagnosis, IReadOnlyList<string> Options)> CannotFixOptions(
        RemediationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return report.Attempts
            .Where(attempt => attempt.Task is not null
                              && attempt.Proposal is { Decision: RemediationDecision.CannotFix, Options.Count: > 0 })
            .Select(attempt => (attempt.Task!, attempt.Proposal!.Diagnosis, attempt.Proposal.Options))
            .ToList();
    }

    /// <summary>The attempt's fate: kept, not kept (and reverted), rejected, or why there was nothing to apply.</summary>
    public static string Kept(
        RemediationAttemptRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return attempt.Error is not null ? $"error: {attempt.Error}"
            : attempt.RejectionReason is not null ? $"rejected by the guardrails: {attempt.RejectionReason}"
            : !attempt.Applied ? attempt.Proposal?.Decision.ToString() ?? "-"
            : attempt.NotKept is not null ? $"reverted: {attempt.NotKept}"
            : "kept";
    }

    public static string Outcome(
        PortTaskRecord task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return task.Status switch
        {
            PortTaskStatus.Accepted => "✅ Accepted",
            PortTaskStatus.Reverted => $"↩️ Reverted: {task.Reason}",
            PortTaskStatus.CannotFix => $"✋ Cannot fix: {task.Reason}",
            _ => $"❌ Error: {task.Reason}"
        };
    }

    public static string Behaviour(
        PortTaskRecord task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var parts = new List<string>();

        if (task.ObligationsKept.Count > 0)
        {
            parts.Add($"{task.ObligationsKept.Count} unchanged");
        }

        if (task.ObligationsBroken.Count > 0)
        {
            parts.Add($"changed: {string.Join("; ", task.ObligationsBroken)}");
        }

        if (task.ObligationsExplained.Count > 0)
        {
            parts.Add($"changed by the platform (a decision): {string.Join("; ", task.ObligationsExplained)}");
        }

        if (task.ObligationsUnchecked.Count > 0)
        {
            parts.Add($"not checked: {string.Join("; ", task.ObligationsUnchecked)}");
        }

        return parts.Count == 0 ? "-" : string.Join(". ", parts);
    }

    private static string Describe(
        PortTaskStatus status) =>
        status switch
        {
            PortTaskStatus.Accepted => "accepted",
            PortTaskStatus.Reverted => "reverted",
            PortTaskStatus.CannotFix => "cannot fix",
            _ => "error"
        };

    private static string Cell(
        string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
