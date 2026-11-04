using System.Globalization;
using System.Text;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Reporting;

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
        MigrationAnalysis? analysis,
        UnsupportedLibraryCatalog? libraries = null)
    {
        libraries ??= UnsupportedLibraries.Catalog;

        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(flags);

        var needsDecision = result.NeedsDecision;
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
            AppendDecision(body, result, libraries);
        }

        AppendUnsupportedLibraries(body, result, libraries);
        AppendOutcome(body, result);
        AppendBehaviour(body, result.Behaviour);
        AppendFlags(body, flags, result.Remediation);
        AppendRecipeDiscovery(body, result.RecipeDiscovery);
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
        TransformationExecutionResult result,
        UnsupportedLibraryCatalog libraries)
    {
        var forAPerson = result.Remediation is null ? [] : PortText.ForAPerson(result.Remediation);
        var agentAsked = forAPerson.Count > 0;

        body.AppendLine("## Decision needed");
        body.AppendLine();

        if (agentAsked)
        {
            body.AppendLine(
                "The AI ported each known API move as a scoped task. These were not kept, or were never the AI's " +
                "to do, and need a person:");
            body.AppendLine();

            foreach (var item in forAPerson)
            {
                body.AppendLine($"- {item}");
            }
        }

        var known = result.UnsupportedLibraries
            .Select(libraries.Find)
            .OfType<UnsupportedLibrary>()
            .Select(library => library.Name)
            .ToList();

        if (known.Count > 0)
        {
            if (agentAsked)
            {
                body.AppendLine();
            }

            var names = string.Join(", ", known);

            body.AppendLine(
                $"This repository still declares {names}, which Spring Boot 3 no longer supports. The pipeline " +
                "never lets the AI change `pom.xml`, so the library is still there whatever Maven decided: choose a " +
                "replacement and follow the manual steps below.");

            if (result.Validation.Status == TransformationValidationStatus.Passed)
            {
                body.AppendLine();
                body.AppendLine(
                    "**Maven passed anyway.** That means the AI's changes work around the library rather than " +
                    "replace it. Read them closely, starting with the review flags, before keeping any of them.");
            }
        }

        if (result.Validation.Status == TransformationValidationStatus.Passed && result.Behaviour is { Baseline: not null, Holds: false })
        {
            body.AppendLine();
            body.AppendLine(
                result.Behaviour.Comparison is null
                    ? $"Maven passed, but the behaviour contract could not be checked ({result.Behaviour.Note}). A person must confirm the behaviour is unchanged."
                    : $"Maven passed, but the migrated application behaves differently from the Boot 2.7 one in {result.Behaviour.Comparison.Differences.Count} " +
                      "ways (see **Behaviour compared with the Boot 2.7 application**)." + BehaviourText.UncheckedSentence(result.Behaviour.Comparison) +
                      " Fix each difference or accept it with a decision.");
        }

        foreach (var (mapping, diagnosis, options) in result.Remediation is null ? [] : PortText.CannotFixOptions(result.Remediation))
        {
            body.AppendLine();
            body.AppendLine($"**Why `{mapping}` could not be ported (AI-generated):** {diagnosis}");
            body.AppendLine();
            body.AppendLine("**Options (AI-generated):**");
            body.AppendLine();

            foreach (var option in options)
            {
                body.AppendLine($"- {option}");
            }
        }

        body.AppendLine();
    }

    /// <summary>Fixed guidance from the unsupported-libraries catalogue: the same steps every run.</summary>
    private static void AppendUnsupportedLibraries(
        StringBuilder body,
        TransformationExecutionResult result,
        UnsupportedLibraryCatalog libraries)
    {
        var found = result.UnsupportedLibraries
            .Select(libraries.Find)
            .OfType<UnsupportedLibrary>()
            .ToList();

        if (found.Count == 0)
        {
            return;
        }

        body.AppendLine("## Unsupported libraries: manual steps");
        body.AppendLine();
        body.AppendLine(
            "Spring Boot 3 no longer supports these libraries. Replacing them changes `pom.xml` and often test " +
            "resources, which the pipeline never lets the AI edit, so a person makes these changes on this branch. " +
            "This guidance is fixed text from the pipeline, not AI-generated.");

        foreach (var library in found)
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
            body.AppendLine($"- AI ports: **{PortText.Counts(remediation)}**");
        }

        body.AppendLine();
    }

    private static void AppendFlags(
        StringBuilder body,
        IReadOnlyList<ReviewFlag> flags,
        RemediationReport? remediation)
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

            body.AppendLine($"- **{label}** `{flag.Path}` ({flag.Stage}): {flag.Detail}{Checked(flag, remediation)}");
        }

        body.AppendLine();
    }

    /// <summary>
    /// What the behaviour contract proved about the port behind a flag. A
    /// flag is never hidden: the probe does not read everything a port keeps.
    /// </summary>
    private static string Checked(
        ReviewFlag flag,
        RemediationReport? remediation)
    {
        var mapping = remediation?.Attempts.FirstOrDefault(attempt => attempt.Patch?.Stage == flag.Stage)?.Task;
        var task = remediation?.Tasks.FirstOrDefault(task => task.Mapping == mapping);

        return task is { ObligationsKept.Count: > 0, ObligationsBroken.Count: 0 }
            ? $" The behaviour contract checked this port: {task.ObligationsKept.Count} recorded behaviours are unchanged; anything the probe does not record still needs your review."
            : string.Empty;
    }

    /// <summary>What the migrated application does differently from the Boot 2.7 one (the behaviour contract).</summary>
    private static void AppendBehaviour(
        StringBuilder body,
        BehaviourReport? behaviour)
    {
        if (behaviour is null)
        {
            return;
        }

        body.AppendLine("## Behaviour compared with the Boot 2.7 application");
        body.AppendLine();
        body.AppendLine(
            "Recorded by the pipeline on both versions: whether each profile starts, who can call each endpoint, " +
            "resolved settings, and HTTP client and cache values. Not AI-generated.");
        body.AppendLine();
        body.AppendLine(BehaviourText.Summary(behaviour));

        if (behaviour.Comparison is { } comparison && comparison.Differences.Count > 0)
        {
            body.AppendLine();
            body.Append(BehaviourText.Differences(comparison, behaviour.Explained));
            body.AppendLine();
            body.AppendLine(
                "Each difference needs a person: fix it, or accept it with a decision record. A difference can be an " +
                "improvement (for example a request path that was open on Boot 2.7 and is closed now), but it is still a change.");
        }

        body.AppendLine();
    }

    /// <summary>Recipes that might replace remaining work, with what measuring them showed. Never applied.</summary>
    private static void AppendRecipeDiscovery(
        StringBuilder body,
        RecipeDiscoveryReport? discovery)
    {
        if (discovery is null || (discovery.Error is null && discovery.Suggestions.Count == 0))
        {
            return;
        }

        body.AppendLine("## Possible recipes for the remaining errors (suggestions, not applied)");
        body.AppendLine();

        if (discovery.Error is not null)
        {
            body.AppendLine($"Recipe discovery could not run: {discovery.Error}");
            body.AppendLine();
            return;
        }

        body.AppendLine(
            $"The pipeline matched the errors the route's recipes left in `{discovery.FailedCheck}` against its index of " +
            "OpenRewrite recipes, and measured the best candidates on a throwaway copy. None was applied. Adding a recipe " +
            "to the route is a reviewed change, with a probe run as evidence.");
        body.AppendLine();

        var candidates = discovery.Suggestions
            .Where(suggestion => suggestion.Verdict != RecipeSuggestionVerdict.AlreadyRun)
            .ToList();

        if (candidates.Count == 0)
        {
            body.AppendLine("No recipe outside the route matched these errors.");
        }

        foreach (var suggestion in candidates)
        {
            body.AppendLine(
                $"- **`{suggestion.Recipe}`** (`{suggestion.Coordinates}`), matched in {RecipeDiscoveryText.Files(suggestion)}: " +
                $"{RecipeDiscoveryText.Verdict(suggestion.Verdict)}, {RecipeDiscoveryText.Effect(suggestion)}.");

            // What a reviewer needs: what it would fix if suitable, what it breaks if not.
            var detail = suggestion.Verdict == RecipeSuggestionVerdict.Suitable ? suggestion.Removed : suggestion.Added;
            var label = suggestion.Verdict == RecipeSuggestionVerdict.Suitable ? "fixes" : "adds";

            foreach (var error in detail.Take(5))
            {
                body.AppendLine($"  - {label}: `{error}`");
            }

            if (detail.Count > 5)
            {
                body.AppendLine($"  - … and {detail.Count - 5} more");
            }
        }

        var alreadyRun = discovery.Suggestions
            .Where(suggestion => suggestion.Verdict == RecipeSuggestionVerdict.AlreadyRun)
            .Select(suggestion => $"`{RecipeDiscoveryText.ShortName(suggestion.Recipe)}`")
            .ToList();

        if (alreadyRun.Count > 0)
        {
            body.AppendLine();
            body.AppendLine(
                $"Already in the route, and these errors remain: {string.Join(", ", alreadyRun)}. " +
                "What is left there is port work, not a missing recipe.");
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

        if (result.Remediation!.Tasks.Count > 0)
        {
            body.AppendLine("**Scoped AI ports** (each kept only if its errors are gone, nothing new fails and its behaviour is unchanged):");
            body.AppendLine();
            body.Append(PortText.Tasks(result.Remediation));
            body.AppendLine();
        }

        body.AppendLine("**AI attempts** (a reverted attempt is recorded, but it is not in this pull request):");
        body.AppendLine();
        body.AppendLine("| # | Port | Check | Edits | Validation after | Kept |");
        body.AppendLine("|---|---|---|---|---|---|");

        foreach (var attempt in attempts)
        {
            var edits = attempt.AppliedEdits.Count == 0
                ? attempt.Proposal?.Decision.ToString() ?? attempt.Error ?? "-"
                : string.Join("; ", attempt.AppliedEdits.Select(edit =>
                    $"{Path.GetFileName(edit.Path)} ({Kind(edit.Kind)}, {edit.ChangedLines} lines)"));

            var after = attempt.Validation?.Status.ToString() ?? "-";

            body.AppendLine(
                $"| {attempt.Number} | {attempt.Task ?? "-"} | {attempt.FailedCheck} | {Cell(edits)} | {after} | {Cell(PortText.Kept(attempt))} |");
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
