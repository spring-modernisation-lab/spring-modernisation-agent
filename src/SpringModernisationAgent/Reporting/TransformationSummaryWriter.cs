using System.Globalization;
using System.Text;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Reporting;

/// <summary>
/// A short Markdown summary of one migration wave, for the GitHub Actions
/// job summary page: one row per repository, then one row per remediation
/// attempt. Every value comes from the deterministic results.
/// </summary>
public static class TransformationSummaryWriter
{
    public static string Render(
        IReadOnlyList<TransformationExecutionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var markdown = new StringBuilder();

        markdown.AppendLine("## Migration wave results");
        markdown.AppendLine();
        markdown.AppendLine("| Repository | OpenRewrite | Validation | Remediation | Outcome |");
        markdown.AppendLine("|---|---|---|---|---|");

        foreach (var result in results)
        {
            var remediation = result.Remediation is null
                ? "not enabled"
                : result.Remediation.Attempts.Count == 0
                    ? result.Remediation.Outcome.ToString()
                    : $"{result.Remediation.Outcome} ({Plural(result.Remediation.Attempts.Count, "attempt")})";

            markdown.AppendLine(Row(
                result.Transformation.RepositoryName,
                result.Transformation.Status == TransformationStatus.Succeeded
                    ? $"{result.Transformation.ChangedFiles.Count} files changed"
                    : result.Transformation.Status.ToString(),
                DescribeValidation(result.Validation),
                remediation,
                result.Succeeded ? "✅ Succeeded"
                    : result.NeedsDecision ? "⚠️ Needs decision"
                    : "❌ Failed"));
        }

        foreach (var result in results.Where(result => result.Remediation?.Attempts.Count > 0))
        {
            markdown.AppendLine();
            markdown.AppendLine($"### {result.Transformation.RepositoryName}: remediation");
            markdown.AppendLine();
            markdown.AppendLine("| # | Check | Decision | Edits | Guardrails | Validation after | Tool calls | Tokens in/out |");
            markdown.AppendLine("|---|---|---|---|---|---|---|---|");

            foreach (var attempt in result.Remediation!.Attempts)
            {
                var guardrails =
                    attempt.Error is not null ? $"error: {attempt.Error}"
                    : attempt.RejectionReason is not null ? $"rejected: {attempt.RejectionReason}"
                    : attempt.Applied ? "accepted"
                    : "-";

                var edits = attempt.AppliedEdits.Count == 0
                    ? "-"
                    : string.Join("; ", attempt.AppliedEdits.Select(edit =>
                        $"{Path.GetFileName(edit.Path)} ({DescribeKind(edit.Kind)}, {edit.ChangedLines} lines)"));

                var previews = attempt.ToolCalls.Count(call => call.Tool == "preview_edits");

                markdown.AppendLine(Row(
                    attempt.Number.ToString(CultureInfo.InvariantCulture),
                    attempt.FailedCheck,
                    attempt.Proposal?.Decision.ToString() ?? "-",
                    edits,
                    guardrails,
                    attempt.Validation is null ? "-" : DescribeValidation(attempt.Validation),
                    $"{attempt.ToolCalls.Count} ({previews} previews)",
                    $"{Tokens(attempt.InputTokens)} / {Tokens(attempt.OutputTokens)}"));
            }
        }

        return markdown.ToString();
    }

    private static string DescribeValidation(
        TransformationValidationResult validation)
    {
        var failed = validation.Checks.FirstOrDefault(check => !check.Passed);

        return failed is null
            ? validation.Status.ToString()
            : $"{validation.Status} ({failed.Check})";
    }

    private static string DescribeKind(
        RemediationEditKind kind) =>
        kind switch
        {
            RemediationEditKind.WholeFileRewrite => "whole file",
            RemediationEditKind.NewFile => "new file",
            _ => "search/replace"
        };

    private static string Tokens(
        long? count) =>
        count?.ToString("N0", CultureInfo.InvariantCulture) ?? "?";

    private static string Plural(
        int count,
        string noun) =>
        $"{count} {noun}{(count == 1 ? "" : "s")}";

    // Free text must not break the table: escape pipes, flatten newlines.
    private static string Row(
        params string[] cells) =>
        "| " + string.Join(
            " | ",
            cells.Select(cell => cell
                .Replace("|", "\\|", StringComparison.Ordinal)
                .ReplaceLineEndings(" "))) + " |";
}
