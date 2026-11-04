using System.Text.Json.Serialization;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Models;

[JsonConverter(typeof(JsonStringEnumConverter<VerdictState>))]
public enum VerdictState
{
    /// <summary>Maven passed, the behaviour matches, and nothing waits on a person. Opens the next wave.</summary>
    Succeeded,

    /// <summary>Something only a person can decide remains.</summary>
    NeedsDecision,

    /// <summary>The build fails and nothing is waiting on a person: work is left.</summary>
    Failed,

    /// <summary>OpenRewrite did not complete, so nothing was validated.</summary>
    NotRun
}

/// <param name="Id">A stable identifier: an invariant (<c>I-…</c>), a problem kind (<c>K-…</c>), a port's mapping (<c>M-…</c>) or a failure (<c>F-…</c>).</param>
/// <param name="NeedsAPerson">True for a decision; false for a failure or work left.</param>
public sealed record VerdictReason(
    string Id,
    string Text,
    bool NeedsAPerson);

/// <summary>A repository's state and every reason for it (docs/system-design.md §8.2).</summary>
public sealed record Verdict(
    VerdictState State,
    IReadOnlyList<VerdictReason> Reasons)
{
    /// <summary>
    /// The one verdict function. Decided from facts only: OpenRewrite's and
    /// Maven's results, unsupported libraries, the problems that remain and
    /// their kinds, the ports, and the behaviour contract. Never from what
    /// the AI said about its own work.
    /// </summary>
    public static Verdict Of(
        TransformationExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Transformation.Status != TransformationStatus.Succeeded)
        {
            return new Verdict(VerdictState.NotRun,
                [new VerdictReason("F-OPENREWRITE", $"OpenRewrite did not complete ({result.Transformation.Status}).", false)]);
        }

        var mavenPassed = result.Validation.Status == TransformationValidationStatus.Passed;
        var decisions = new List<VerdictReason>();
        var failures = new List<VerdictReason>();
        var tasks = result.Remediation?.Tasks ?? [];

        // I-UNSUPPORTED-NEVER-SUCCEEDS: the AI cannot edit pom.xml, so a green build cannot mean the library was replaced.
        decisions.AddRange(result.UnsupportedLibraries.Select(library =>
            new VerdictReason("I-UNSUPPORTED-NEVER-SUCCEEDS", $"{library}: Spring Boot 3 dropped it, and only a person can replace it in pom.xml.", true)));

        foreach (var problem in result.RemainingProblems.DistinctBy(problem => problem.Subject))
        {
            switch (problem.Kind)
            {
                case ProblemKind.PlatformDropped:
                    decisions.Add(new VerdictReason("K-PLATFORM-DROPPED", $"{problem.Subject}: fails because the platform dropped {problem.Library}.", true));
                    break;

                case ProblemKind.Contract:
                    decisions.Add(new VerdictReason("K-CONTRACT", $"{problem.Subject}: an HTTP status or URL changed.", true));
                    break;

                // Reported once, as its port (below).
                case ProblemKind.ApiMove when tasks.Any(task => task.Mapping == problem.Mapping):
                    break;

                case ProblemKind.ApiMove:
                    failures.Add(new VerdictReason("K-API-MOVE", $"{problem.Subject}: a known API move ({problem.Mapping}) that no port worked.", false));
                    break;

                case ProblemKind.Mechanical:
                    failures.Add(new VerdictReason("K-MECHANICAL", $"{problem.Subject}: the recipe {problem.Recipe} ran but left it.", false));
                    break;

                case ProblemKind.Unknown:
                    failures.Add(new VerdictReason("K-UNKNOWN", $"{problem.Subject}: no known signature; a person diagnoses it.", false));
                    break;

                // A knock-on follows from the others and is not a reason of its own.
            }
        }

        foreach (var task in tasks.Where(task => task.Status != PortTaskStatus.Accepted))
        {
            var reason = new VerdictReason(task.Mapping, $"{task.Name}: not ported ({task.Status}): {task.Reason}", task.Status != PortTaskStatus.Error);
            (reason.NeedsAPerson ? decisions : failures).Add(reason);
        }

        if (Contract(result, mavenPassed) is { } contract)
        {
            decisions.Add(contract);
        }

        // Recorded before scoped ports: the agent itself asked for a decision.
        if (result.Remediation is { Outcome: RemediationOutcome.NeedsHumanDecision, Tasks.Count: 0, Attempts.Count: > 0 })
        {
            decisions.Add(new VerdictReason("AI-DECISION", "The AI asked for a person's decision (a run recorded before scoped ports).", true));
        }

        if (!mavenPassed)
        {
            var check = result.Validation.Checks.FirstOrDefault(check => !check.Passed)?.Check;
            failures.Insert(0, new VerdictReason("F-MAVEN", check is null ? "Maven validation did not run." : $"Maven {check} failed.", false));
        }

        if (result.Remediation?.Outcome == RemediationOutcome.Error && tasks.All(task => task.Status != PortTaskStatus.Error))
        {
            failures.Add(new VerdictReason("F-AI", "The AI ports stopped on an error.", false));
        }

        var state = decisions.Count > 0 ? VerdictState.NeedsDecision
            : failures.Count > 0 ? VerdictState.Failed
            : VerdictState.Succeeded;

        return new Verdict(state, [.. decisions, .. failures]);
    }

    /// <summary>
    /// I-CONTRACT-MATCHES: with a baseline, success needs the behaviour to match.
    /// A comparison that was never made because the build failed is a
    /// consequence of the failure, not a decision.
    /// </summary>
    private static VerdictReason? Contract(
        TransformationExecutionResult result,
        bool mavenPassed)
    {
        var behaviour = result.Behaviour;

        if (behaviour?.Baseline is null || behaviour.Holds)
        {
            return null;
        }

        if (behaviour.Comparison is not { } comparison)
        {
            return mavenPassed
                ? new VerdictReason("I-CONTRACT-MATCHES", $"The behaviour could not be checked ({behaviour.Note}); a person must confirm it is unchanged.", true)
                : null;
        }

        var differ = comparison.Differences.Count(difference => difference.Status != Behaviour.ContractItemStatus.Allowed);
        var text = $"{differ} behaviour{(differ == 1 ? " differs" : "s differ")} from Boot 2.7";

        if (behaviour.Explained.Count > 0)
        {
            text += $" ({behaviour.Explained.Count} {(behaviour.Explained.Count == 1 ? "a known platform change" : "known platform changes")})";
        }

        if (comparison.Unchecked > 0)
        {
            text += $"; {comparison.Unchecked} could not be checked because an application no longer starts";
        }

        return new VerdictReason("I-CONTRACT-MATCHES", text + ".", true);
    }
}
