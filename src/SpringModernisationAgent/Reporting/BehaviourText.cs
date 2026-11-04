using System.Text;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Reporting;

/// <summary>The same wording for the behaviour contract in the job summary, the PR and the CLI.</summary>
public static class BehaviourText
{
    /// <summary>One line: holds, does not hold, or why it wasn't compared.</summary>
    public static string Summary(
        BehaviourReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.Comparison is null)
        {
            return report.Note ?? "not compared";
        }

        var comparison = report.Comparison;

        return comparison.Matches
            ? $"**Holds**: {comparison.Same} recorded behaviours are unchanged" +
              (comparison.Differences.Count > 0 ? $"; {comparison.Differences.Count} differences are allowed by decisions." : ".")
            : $"**Does not hold**: {comparison.Differences.Count} differ from the Boot 2.7 application; {comparison.Same} are unchanged.";
    }

    /// <summary>The differences as a Markdown table (empty when there are none).</summary>
    public static string Differences(
        ContractComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        if (comparison.Differences.Count == 0)
        {
            return string.Empty;
        }

        var markdown = new StringBuilder();
        markdown.AppendLine("| Behaviour | Boot 2.7 | Migrated | Status |");
        markdown.AppendLine("|---|---|---|---|");

        foreach (var difference in comparison.Differences)
        {
            markdown.AppendLine(
                $"| `{Cell(difference.Item)}` | {Cell(difference.Before)} | {Cell(difference.After ?? "(not recorded)")} | " +
                $"{difference.Status}{(difference.Decision is null ? "" : $" ({difference.Decision})")} |");
        }

        return markdown.ToString();
    }

    private static string Cell(
        string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
