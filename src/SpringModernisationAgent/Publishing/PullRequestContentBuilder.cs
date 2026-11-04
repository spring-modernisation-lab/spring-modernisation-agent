using System.Globalization;
using System.Text;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;

namespace SpringModernisationAgent.Publishing;

/// <param name="Branch">The migration branch; never a default branch.</param>
/// <param name="Title">Pull request title.</param>
/// <param name="Body">The reviewer summary, in Markdown.</param>
/// <param name="NeedsDecision">True when a person must choose before this can proceed.</param>
public sealed record PullRequestContent(
    string RepositoryName,
    string SourceCommitSha,
    string Branch,
    string Title,
    string Body,
    bool NeedsDecision);

/// <summary>
/// Decides whether a repository gets a draft pull request and writes its
/// reviewer summary. Facts come from the deterministic results; flags from
/// <see cref="ReviewFlagDetector"/>; only the "AI explanation" section is
/// model-written, and it is labelled as such.
/// </summary>
public static class PullRequestContentBuilder
{
    // GitHub rejects bodies over 65,536 characters.
    private const int MaxBodyLength = 60_000;

    /// <summary>
    /// Returns null when there must be no pull request: the repository
    /// neither passed validation nor stopped for a human decision, or its
    /// cumulative patch is missing.
    /// </summary>
    public static PullRequestContent? Build(
        TransformationPlan plan,
        TransformationExecutionResult result,
        IReadOnlyList<ReviewFlag> flags,
        MigrationAnalysis? analysis)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(flags);

        var needsDecision = result.Remediation?.Outcome == RemediationOutcome.NeedsHumanDecision;
        var cumulative = result.Patches.FirstOrDefault(patch => patch.Stage == "cumulative");

        if ((!result.Succeeded && !needsDecision)
            || result.Transformation.Status != TransformationStatus.Succeeded
            || cumulative is null
            || result.PatchRecordingError is not null)
        {
            return null;
        }

        var boot = plan.Target.SpringBootVersion.Replace(".x", string.Empty, StringComparison.Ordinal);
        var java = plan.Target.JavaVersion;
        var title = $"Migrate {plan.RepositoryName} to Spring Boot {boot} and Java {java}";

        var body = new StringBuilder();

        body.AppendLine($"# {title}");
        body.AppendLine();
        body.AppendLine(
            "> Draft opened by the Spring modernisation pipeline. Maven decided the outcome below; " +
            "sections marked AI-generated are explanation only. This pull request is never merged automatically.");
        body.AppendLine();

        if (needsDecision)
        {
            AppendDecision(body, result.Remediation!);
        }

        AppendUnsupportedLibraries(body, result);
        AppendOutcome(body, result);
        AppendFlags(body, flags);
        AppendChanges(body, result);

        if (analysis is not null)
        {
            AppendAnalysis(body, analysis);
        }

        AppendEvidence(body, result);

        var text = body.ToString();

        if (text.Length > MaxBodyLength)
        {
            text = text[..MaxBodyLength] + "\n\n... truncated; see the run artifacts for the full summary.\n";
        }

        return new PullRequestContent(
            plan.RepositoryName,
            plan.SourceCommitSha,
            $"migration/spring-boot-{boot}-java-{java}",
            needsDecision ? $"[Needs decision] {title}" : title,
            text,
            needsDecision);
    }

    private static void AppendDecision(
        StringBuilder body,
        RemediationReport remediation)
    {
        var proposal = remediation.Attempts.LastOrDefault()?.Proposal;

        body.AppendLine("## Decision needed");
        body.AppendLine();
        body.AppendLine("The Remediation Agent stopped because the remaining fix needs a person to choose.");

        if (proposal is not null)
        {
            body.AppendLine();
            body.AppendLine($"**Why (AI-generated):** {proposal.Diagnosis}");
            body.AppendLine();
            body.AppendLine("**Options (AI-generated):**");
            body.AppendLine();

            foreach (var option in proposal.Options)
            {
                body.AppendLine($"- {option}");
            }
        }

        body.AppendLine();
    }

    /// <summary>Fixed guidance from <see cref="UnsupportedLibraries"/>: the same steps every run.</summary>
    private static void AppendUnsupportedLibraries(
        StringBuilder body,
        TransformationExecutionResult result)
    {
        var libraries = result.UnsupportedLibraries
            .Select(UnsupportedLibraries.Find)
            .OfType<UnsupportedLibrary>()
            .ToList();

        if (libraries.Count == 0)
        {
            return;
        }

        body.AppendLine("## Unsupported libraries: manual steps");
        body.AppendLine();
        body.AppendLine(
            "Spring Boot 3 no longer supports these libraries. Replacing them changes `pom.xml` and often test " +
            "resources, which the pipeline never lets the AI edit, so a person makes these changes on this branch. " +
            "This guidance is fixed text from the pipeline, not AI-generated.");

        foreach (var library in libraries)
        {
            body.AppendLine();
            body.AppendLine($"### {library.Name} (`{library.Package}`)");
            body.AppendLine();
            body.AppendLine($"**Recommended:** {library.Recommended}.");
            body.AppendLine();

            for (var index = 0; index < library.ManualSteps.Count; index++)
            {
                body.AppendLine($"{index + 1}. {library.ManualSteps[index]}");
            }

            if (library.Alternatives.Count > 0)
            {
                body.AppendLine();
                body.AppendLine("**Alternatives:**");
                body.AppendLine();

                foreach (var alternative in library.Alternatives)
                {
                    body.AppendLine($"- {alternative}");
                }
            }
        }

        body.AppendLine();
    }

    private static void AppendOutcome(
        StringBuilder body,
        TransformationExecutionResult result)
    {
        var failed = result.Validation.Checks.FirstOrDefault(check => !check.Passed);

        body.AppendLine("## Outcome");
        body.AppendLine();
        body.AppendLine(
            $"- Maven validation: **{result.Validation.Status}**" +
            (failed is null ? string.Empty : $" ({failed.Check})"));
        body.AppendLine(
            "- Checks: " + string.Join(", ", result.Validation.Checks.Select(check =>
                $"{check.Check} {(check.Passed ? "passed" : "failed")}")));

        if (result.Remediation is { } remediation)
        {
            var attempts = remediation.Attempts.Count;

            body.AppendLine(
                $"- Remediation: **{remediation.Outcome}** after {attempts} attempt{(attempts == 1 ? "" : "s")}");
        }

        body.AppendLine();
    }

    private static void AppendFlags(
        StringBuilder body,
        IReadOnlyList<ReviewFlag> flags)
    {
        body.AppendLine($"## Review flags ({flags.Count})");
        body.AppendLine();

        if (flags.Count == 0)
        {
            body.AppendLine("No changes matched the review checks.");
        }

        foreach (var flag in flags)
        {
            var label = flag.Kind switch
            {
                ReviewFlagKind.SecurityChanged => "Security",
                ReviewFlagKind.RequestMappingChanged => "Request mapping",
                ReviewFlagKind.CallRemoved => "Removed code",
                ReviewFlagKind.LargeAddition => "Large new code",
                _ => "CI configuration"
            };

            body.AppendLine($"- **{label}** `{flag.Path}` ({flag.Stage}): {flag.Detail}");
        }

        body.AppendLine();
    }

    private static void AppendChanges(
        StringBuilder body,
        TransformationExecutionResult result)
    {
        body.AppendLine("## What changed");
        body.AppendLine();
        body.AppendLine(
            $"**OpenRewrite** ({string.Join(", ", result.Transformation.AppliedRecipes.Select(recipe => recipe.Split('.')[^1]))}): " +
            $"{result.Transformation.ChangedFiles.Count} files.");
        body.AppendLine();

        var attempts = result.Remediation?.Attempts ?? [];

        if (attempts.Count == 0)
        {
            return;
        }

        body.AppendLine("**Remediation Agent attempts:**");
        body.AppendLine();
        body.AppendLine("| # | Check | Edits | Validation after |");
        body.AppendLine("|---|---|---|---|");

        foreach (var attempt in attempts)
        {
            var edits = attempt.AppliedEdits.Count == 0
                ? attempt.Proposal?.Decision.ToString() ?? attempt.Error ?? "-"
                : string.Join("; ", attempt.AppliedEdits.Select(edit =>
                    $"{Path.GetFileName(edit.Path)} ({Kind(edit.Kind)}, {edit.ChangedLines} lines)"));

            var after = attempt.Validation is null
                ? attempt.RejectionReason is null ? "-" : "rejected by guardrails"
                : attempt.Validation.Status.ToString();

            body.AppendLine($"| {attempt.Number} | {attempt.FailedCheck} | {Cell(edits)} | {after} |");
        }

        body.AppendLine();
    }

    private static void AppendAnalysis(
        StringBuilder body,
        MigrationAnalysis analysis)
    {
        body.AppendLine("## AI explanation (AI-generated, not authoritative)");
        body.AppendLine();
        body.AppendLine(analysis.Summary);
        body.AppendLine();

        foreach (var finding in analysis.Findings)
        {
            body.AppendLine($"- {finding}");
        }

        if (analysis.RecommendedActions.Count > 0)
        {
            body.AppendLine();
            body.AppendLine("**Suggested checks:**");
            body.AppendLine();

            foreach (var action in analysis.RecommendedActions)
            {
                body.AppendLine($"- {action}");
            }
        }

        body.AppendLine();
    }

    private static void AppendEvidence(
        StringBuilder body,
        TransformationExecutionResult result)
    {
        body.AppendLine("## Evidence");
        body.AppendLine();
        body.AppendLine($"- Source commit: `{result.Transformation.SourceCommitSha}`");

        foreach (var patch in result.Patches)
        {
            body.AppendLine($"- `{patch.RelativePath}` (sha256 `{patch.Sha256}`)");
        }

        var attempts = result.Remediation?.Attempts ?? [];

        if (attempts.Count > 0)
        {
            var input = attempts.Sum(attempt => attempt.InputTokens ?? 0);
            var output = attempts.Sum(attempt => attempt.OutputTokens ?? 0);

            body.AppendLine(
                $"- AI tokens: {input.ToString("N0", CultureInfo.InvariantCulture)} in / " +
                $"{output.ToString("N0", CultureInfo.InvariantCulture)} out");
        }
    }

    private static string Kind(
        RemediationEditKind kind) =>
        kind switch
        {
            RemediationEditKind.WholeFileRewrite => "whole file",
            RemediationEditKind.NewFile => "new file",
            _ => "search/replace"
        };

    private static string Cell(
        string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
