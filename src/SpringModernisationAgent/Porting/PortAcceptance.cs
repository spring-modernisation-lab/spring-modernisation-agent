using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Porting;

/// <param name="Remaining">The task's own problems still failing.</param>
/// <param name="Caused">New failures held against the port.</param>
public sealed record PortJudgement(
    bool Accepted,
    string? Reason,
    IReadOnlyList<ClassifiedProblem> Remaining,
    IReadOnlyList<ClassifiedProblem> Caused)
{
    /// <summary>The failure, as the repair attempt is told it.</summary>
    public IReadOnlyList<string> Feedback =>
        [.. Remaining.SelectMany(problem => problem.Evidence), .. Caused.SelectMany(problem => problem.Evidence)];
}

/// <summary>
/// Whether a port is kept (docs/system-design.md §4.1): the task's evidence
/// is gone, and nothing new fails because of it. Its obligations are checked
/// separately, once everything compiles (<see cref="PortObligations"/>).
/// </summary>
public static class PortAcceptance
{
    /// <param name="before">Validation before the port.</param>
    /// <param name="beforeProblems">Its classified problems.</param>
    /// <param name="after">Validation after the port.</param>
    /// <param name="afterProblems">Its classified problems, classified with <paramref name="beforeProblems"/> as the previous classification.</param>
    public static PortJudgement Judge(
        PortTask task,
        TransformationValidationResult before,
        IReadOnlyList<ClassifiedProblem> beforeProblems,
        TransformationValidationResult after,
        IReadOnlyList<ClassifiedProblem> afterProblems)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(beforeProblems);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(afterProblems);

        var stageBefore = Stage(before);
        var stageAfter = Stage(after);

        if (stageAfter < stageBefore)
        {
            var check = after.Checks.First(result => !result.Passed).Check;
            return new PortJudgement(false, $"{check} now fails, and it passed before the port", [], afterProblems.ToList());
        }

        // The task's own area: its files, or its mapping (a port's follow-up errors keep it).
        var remaining = afterProblems
            .Where(problem => task.Files.Contains(problem.Subject, StringComparer.Ordinal)
                              || string.Equals(problem.Mapping, task.Mapping.Id, StringComparison.Ordinal)
                              || (problem.SignatureId is not null && task.EvidenceSignatures.Contains(problem.SignatureId)))
            .ToList();

        // A new failure at the same check counts against the port when no
        // signature knows it. A known kind javac had not reported yet was
        // already there. A later check failing is progress, not a new failure.
        var known = beforeProblems.Select(problem => problem.Subject).ToHashSet(StringComparer.Ordinal);
        var caused = stageAfter == stageBefore
            ? afterProblems
                .Where(problem => !remaining.Contains(problem)
                                  && !known.Contains(problem.Subject)
                                  && problem.Kind == ProblemKind.Unknown)
                .ToList()
            : [];

        var reason = (remaining.Count, caused.Count) switch
        {
            (0, 0) => null,
            (> 0, 0) => $"the task still fails in {Describe(remaining)}",
            (0, _) => $"the port caused a new failure in {Describe(caused)}",
            _ => $"the task still fails in {Describe(remaining)}, and the port caused a new failure in {Describe(caused)}"
        };

        return new PortJudgement(reason is null, reason, remaining, caused);
    }

    /// <summary>How far the build gets: the number of checks before the first failure.</summary>
    private static int Stage(
        TransformationValidationResult validation)
    {
        var failed = validation.Checks.ToList().FindIndex(check => !check.Passed);
        return failed < 0 ? int.MaxValue : failed;
    }

    private static string Describe(
        IReadOnlyList<ClassifiedProblem> problems) =>
        string.Join(", ", problems.Select(problem => problem.Subject).Distinct(StringComparer.Ordinal));
}

/// <summary>A behaviour difference the platform causes, with the signature that explains it.</summary>
public sealed record ExplainedDifference(
    ContractDifference Difference,
    BehaviourSignature Signature);

/// <summary>One task's obligations after the contract gate.</summary>
/// <param name="Kept">Obligation items that are the same as on the baseline.</param>
/// <param name="Broken">Obligation items that differ, unexplained: held against the port.</param>
/// <param name="Explained">Obligation items that differ because of the platform: decisions, not the port's.</param>
/// <param name="Unchecked">Obligation items that could not be read because their application no longer starts.</param>
public sealed record ObligationResult(
    PortTask Task,
    IReadOnlyList<string> Kept,
    IReadOnlyList<ContractDifference> Broken,
    IReadOnlyList<ExplainedDifference> Explained,
    IReadOnlyList<string> Unchecked)
{
    public bool Holds => Broken.Count == 0;
}

/// <summary>
/// The contract gate (docs/system-design.md §4.1): once everything compiles,
/// one behaviour comparison checks every accepted port's obligations.
/// </summary>
public static class PortObligations
{
    public static IReadOnlyList<ObligationResult> Check(
        IReadOnlyList<PortTask> tasks,
        BehaviourRecord baseline,
        ContractComparison comparison,
        ProblemKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(knowledge);

        var differences = comparison.Differences.ToDictionary(difference => difference.Item, StringComparer.Ordinal);
        var notChecked = comparison.Differences
            .SelectMany(difference => difference.Unchecked ?? [])
            .ToHashSet(StringComparer.Ordinal);

        return tasks
            .Select(task =>
            {
                var obligations = baseline.Items.Keys
                    .Where(task.Obliges)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                var broken = new List<ContractDifference>();
                var explained = new List<ExplainedDifference>();

                foreach (var item in obligations)
                {
                    if (!differences.TryGetValue(item, out var difference) || difference.Status == ContractItemStatus.Allowed)
                    {
                        continue;
                    }

                    if (task.ExcusePlatformChanges && knowledge.Explain(difference) is { } signature)
                    {
                        explained.Add(new ExplainedDifference(difference, signature));
                    }
                    else
                    {
                        broken.Add(difference);
                    }
                }

                return new ObligationResult(
                    task,
                    obligations.Where(item => !differences.ContainsKey(item) && !notChecked.Contains(item)).ToList(),
                    broken,
                    explained,
                    obligations.Where(notChecked.Contains).ToList());
            })
            .ToList();
    }
}
