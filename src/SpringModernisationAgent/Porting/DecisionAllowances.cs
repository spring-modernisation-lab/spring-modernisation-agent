using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Porting;

/// <summary>
/// What a recorded decision did, for the PR, the summary and the console.
/// </summary>
/// <param name="Result">For example "applied: its port was kept" or "allows 2 differences".</param>
public sealed record DecisionOutcome(
    string Id,
    string Subject,
    string Option,
    string By,
    string On,
    string Result);

/// <summary>
/// Applies the one thing a decision may write into the behaviour contract:
/// the differences its option <c>allows</c> (docs/system-design.md §12.1).
/// </summary>
public static class DecisionAllowances
{
    /// <summary>
    /// Marks the differences that a decision's behaviour signature matches
    /// as allowed by that decision. Nothing else changes, and a report with
    /// nothing to allow is returned as it was.
    /// </summary>
    public static BehaviourReport Apply(
        BehaviourReport report,
        IReadOnlyList<DecisionRecord> decisions,
        ProblemKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(knowledge);

        if (report.Comparison is not { } comparison)
        {
            return report;
        }

        var allowing = decisions
            .Where(decision => decision.ChosenOption.Allows is not null)
            .Select(decision => (Decision: decision, Signature: knowledge.BehaviourSignatures.FirstOrDefault(signature => signature.Id == decision.ChosenOption.Allows)))
            .Where(entry => entry.Signature is not null)
            .ToList();

        if (allowing.Count == 0)
        {
            return report;
        }

        var allowedItems = new HashSet<string>(StringComparer.Ordinal);

        var differences = comparison.Differences
            .Select(difference =>
            {
                if (difference.Status == ContractItemStatus.Allowed)
                {
                    return difference;
                }

                var hit = allowing.FirstOrDefault(entry => entry.Signature!.Matches(difference));

                if (hit.Decision is null)
                {
                    return difference;
                }

                allowedItems.Add(difference.Item);
                return difference with { Status = ContractItemStatus.Allowed, Decision = hit.Decision.Id };
            })
            .ToList();

        if (allowedItems.Count == 0)
        {
            return report;
        }

        return report with
        {
            Comparison = comparison with { Differences = differences },
            Explained = report.Explained
                .Where(entry => !allowedItems.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value)
        };
    }
}

public static class DecisionOutcomes
{
    public static IReadOnlyList<DecisionOutcome> Of(
        IReadOnlyList<DecisionRecord> decisions,
        RemediationReport? remediation,
        BehaviourReport? behaviour)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        return decisions
            .Select(decision => new DecisionOutcome(decision.Id, decision.Subject, decision.Option, decision.By, decision.On, Result(decision, remediation, behaviour)))
            .ToList();
    }

    private static string Result(
        DecisionRecord decision,
        RemediationReport? remediation,
        BehaviourReport? behaviour)
    {
        if (decision.ChosenOption.Manual)
        {
            return "manual: the pipeline does not apply it";
        }

        if (decision.ChosenOption.Allows is not null)
        {
            var allowed = behaviour?.Comparison?.Differences.Count(difference => difference.Decision == decision.Id) ?? 0;
            return allowed == 0 ? "allows nothing yet: no difference matched" : $"allows {allowed} difference{(allowed == 1 ? "" : "s")}";
        }

        if (remediation?.Tasks.FirstOrDefault(task => task.Mapping == decision.Id) is { } task)
        {
            return task.Status switch
            {
                PortTaskStatus.Accepted => "applied: its port was kept",
                PortTaskStatus.Reverted => $"not applied: its port was reverted ({task.Reason})",
                PortTaskStatus.CannotFix => $"not applied: the AI could not port it ({task.Reason})",
                _ => $"not applied: error ({task.Reason})"
            };
        }

        var note = remediation?.DecisionNotes.FirstOrDefault(note => note.StartsWith(decision.Id + " ", StringComparison.Ordinal));

        return note is null
            ? "recorded; its port has not run yet"
            : "nothing was ported: " + note[(note.IndexOf("): ", StringComparison.Ordinal) + 3)..];
    }
}
