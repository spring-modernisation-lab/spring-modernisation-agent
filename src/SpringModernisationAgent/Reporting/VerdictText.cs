using System.Text;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Reporting;

/// <summary>The same wording for a verdict in the job summary, the PR, the results document and the console.</summary>
public static class VerdictText
{
    public static string State(
        VerdictState state) =>
        state switch
        {
            VerdictState.Succeeded => "✅ Succeeded",
            VerdictState.NeedsDecision => "⚠️ Needs decision",
            VerdictState.Failed => "❌ Failed",
            _ => "⏭️ Not run"
        };

    /// <summary>"why it needs a decision", for a heading.</summary>
    public static string Why(
        VerdictState state) =>
        state switch
        {
            VerdictState.NeedsDecision => "why it needs a decision",
            VerdictState.Failed => "why it failed",
            VerdictState.NotRun => "why it did not run",
            _ => "verdict"
        };

    /// <summary>One bullet per reason; decisions are marked as a person's.</summary>
    public static string Reasons(
        Verdict verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        var markdown = new StringBuilder();

        foreach (var reason in verdict.Reasons)
        {
            markdown.AppendLine($"- `{reason.Id}`{(reason.NeedsAPerson ? " (a person)" : "")}: {reason.Text}");
        }

        return markdown.ToString();
    }
}
