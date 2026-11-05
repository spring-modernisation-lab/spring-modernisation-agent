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
        markdown.AppendLine("| Repository | OpenRewrite | Validation | AI ports | Outcome |");
        markdown.AppendLine("|---|---|---|---|---|");

        foreach (var result in results)
        {
            var remediation = result.Remediation is null
                ? "not enabled"
                : result.Remediation.Attempts.Count == 0
                    ? result.Remediation.Outcome.ToString()
                    : result.Remediation.Tasks.Count > 0
                        ? PortText.Counts(result.Remediation)
                        : $"{result.Remediation.Outcome} ({Plural(result.Remediation.Attempts.Count, "attempt")})";

            markdown.AppendLine(Row(
                result.Transformation.RepositoryName,
                result.Transformation.Status == TransformationStatus.Succeeded
                    ? $"{result.Transformation.ChangedFiles.Count} files changed"
                    : result.Transformation.Status.ToString(),
                DescribeValidation(result.Validation),
                remediation,
                VerdictText.State(result.Verdict.State)));
        }

        foreach (var result in results.Where(result => result.Verdict.Reasons.Count > 0))
        {
            markdown.AppendLine();
            markdown.AppendLine($"### {result.Transformation.RepositoryName}: {VerdictText.Why(result.Verdict.State)}");
            markdown.AppendLine();
            markdown.Append(VerdictText.Reasons(result.Verdict));
        }

        foreach (var result in results.Where(result => result.Remediation?.Attempts.Count > 0))
        {
            markdown.AppendLine();
            markdown.AppendLine($"### {result.Transformation.RepositoryName}: AI ports");
            markdown.AppendLine();

            if (result.Remediation!.Tasks.Count > 0)
            {
                markdown.Append(PortText.Tasks(result.Remediation));
                markdown.AppendLine();
            }

            markdown.AppendLine("| # | Port | Check | Decision | Edits | Validation after | Kept | Tool calls | Tokens in/out |");
            markdown.AppendLine("|---|---|---|---|---|---|---|---|---|");

            foreach (var attempt in result.Remediation!.Attempts)
            {
                var edits = attempt.AppliedEdits.Count == 0
                    ? "-"
                    : string.Join("; ", attempt.AppliedEdits.Select(edit =>
                        $"{Path.GetFileName(edit.Path)} ({DescribeKind(edit.Kind)}, {edit.ChangedLines} lines)"));

                var previews = attempt.ToolCalls.Count(call => call.Tool == "preview_edits");

                markdown.AppendLine(Row(
                    attempt.Number.ToString(CultureInfo.InvariantCulture),
                    attempt.Task ?? "-",
                    attempt.FailedCheck,
                    attempt.Proposal?.Decision.ToString() ?? "-",
                    edits,
                    attempt.Validation is null ? "-" : DescribeValidation(attempt.Validation),
                    PortText.Kept(attempt),
                    $"{attempt.ToolCalls.Count} ({previews} previews)",
                    $"{Tokens(attempt.InputTokens)} / {Tokens(attempt.OutputTokens)}"));
            }
        }

        foreach (var result in results.Where(result => result.Behaviour is not null))
        {
            markdown.AppendLine();
            markdown.AppendLine($"### {result.Transformation.RepositoryName}: behaviour contract");
            markdown.AppendLine();
            markdown.AppendLine(BehaviourText.Summary(result.Behaviour!));

            if (result.Behaviour!.Comparison is { } comparison && comparison.Differences.Count > 0)
            {
                markdown.AppendLine();
                markdown.Append(BehaviourText.Differences(comparison, result.Behaviour.Explained));
            }
        }

        foreach (var result in results.Where(result => result.Decisions.Count > 0))
        {
            markdown.AppendLine();
            markdown.AppendLine($"### {result.Transformation.RepositoryName}: decisions");
            markdown.AppendLine();
            markdown.AppendLine("| Decision | Subject | Option | By | What it did |");
            markdown.AppendLine("|---|---|---|---|---|");

            foreach (var decision in result.Decisions)
            {
                markdown.AppendLine(Row($"`{decision.Id}`", decision.Subject, decision.Option, decision.By, decision.Result));
            }
        }

        foreach (var result in results.Where(result => result.Problems.Count > 0))
        {
            AppendProblems(markdown, result.Transformation.RepositoryName, result.Problems);
        }

        foreach (var result in results.Where(result => result.RecipeDiscovery is not null))
        {
            AppendRecipeDiscovery(markdown, result.Transformation.RepositoryName, result.RecipeDiscovery!);
        }

        return markdown.ToString();
    }

    /// <summary>What the recipes left, by kind: who or what handles each problem.</summary>
    private static void AppendProblems(
        StringBuilder markdown,
        string repository,
        IReadOnlyList<SpringModernisationAgent.Triage.ClassifiedProblem> problems)
    {
        markdown.AppendLine();
        markdown.AppendLine($"### {repository}: problems left by the recipes");
        markdown.AppendLine();
        markdown.AppendLine("Classified from facts (`Config/knowledge/diagnostic-signatures.yaml`), before any AI work.");
        markdown.AppendLine();
        markdown.AppendLine("| Kind | Handled by | Problems |");
        markdown.AppendLine("|---|---|---|");

        foreach (var group in problems.GroupBy(problem => problem.Kind).OrderBy(group => group.Key))
        {
            var route = group.Key switch
            {
                SpringModernisationAgent.Triage.ProblemKind.ApiMove => "AI port, scoped and checked",
                SpringModernisationAgent.Triage.ProblemKind.Mechanical => "recipe",
                SpringModernisationAgent.Triage.ProblemKind.PlatformDropped => "a person (decision)",
                SpringModernisationAgent.Triage.ProblemKind.Contract => "a person (decision)",
                SpringModernisationAgent.Triage.ProblemKind.KnockOn => "re-checked after the others",
                _ => "AI diagnosis only"
            };

            var items = group
                .GroupBy(problem => problem.Mapping ?? problem.Recipe ?? problem.Library)
                .Select(byRoute =>
                {
                    var subjects = string.Join(", ", byRoute.Select(problem => ShortSubject(problem.Subject)).Distinct());
                    return byRoute.Key is null ? subjects : $"{subjects} ({byRoute.Key})";
                });

            markdown.AppendLine(Row($"{group.Key} ({group.Count()})", route, string.Join("; ", items)));
        }
    }

    /// <summary><c>src/main/java/a/B.java</c> → <c>B.java</c>; <c>a.b.CTests.method</c> → <c>CTests.method</c>.</summary>
    private static string ShortSubject(
        string subject)
    {
        if (subject.Contains('/'))
        {
            return subject[(subject.LastIndexOf('/') + 1)..];
        }

        var parts = subject.Split('.');
        return parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : subject;
    }

    private static void AppendRecipeDiscovery(
        StringBuilder markdown,
        string repository,
        RecipeDiscoveryReport discovery)
    {
        markdown.AppendLine();
        markdown.AppendLine($"### {repository}: recipe candidates");
        markdown.AppendLine();

        if (discovery.Error is not null)
        {
            markdown.AppendLine($"Recipe discovery could not run: {discovery.Error}");
            return;
        }

        if (discovery.Suggestions.Count == 0)
        {
            markdown.AppendLine(
                $"No indexed recipe matched the {Plural(discovery.Errors, "problem")} left in `{discovery.FailedCheck}`.");
            return;
        }

        markdown.AppendLine("Suggestions only, measured on a throwaway copy; nothing here was applied.");
        markdown.AppendLine();
        markdown.AppendLine("| Recipe | Module | Matched in | Verdict | Effect |");
        markdown.AppendLine("|---|---|---|---|---|");

        foreach (var suggestion in discovery.Suggestions)
        {
            markdown.AppendLine(Row(
                $"`{RecipeDiscoveryText.ShortName(suggestion.Recipe)}`",
                suggestion.Coordinates,
                RecipeDiscoveryText.Files(suggestion),
                RecipeDiscoveryText.Verdict(suggestion.Verdict),
                RecipeDiscoveryText.Effect(suggestion)));
        }
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
