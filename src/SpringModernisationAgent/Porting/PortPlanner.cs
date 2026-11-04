using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Porting;

/// <summary>
/// Turns classified problems into scoped port tasks (docs/system-design.md
/// §4.1). Deterministic: only known API moves in production code become
/// tasks, one per mapping, so tasks never share a file and each can be
/// reverted on its own.
/// </summary>
public static class PortPlanner
{
    private const string ProductionSource = "src/main/";

    /// <param name="attempted">Mappings already worked in this run; they are never planned again.</param>
    public static PortPlan Plan(
        IReadOnlyList<ClassifiedProblem> problems,
        ProblemKnowledge knowledge,
        IReadOnlyCollection<string> attempted)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(attempted);

        var unworkable = new List<UnworkableProblem>();
        var portable = new List<ClassifiedProblem>();

        foreach (var problem in problems)
        {
            var reason = WhyNot(problem, knowledge, attempted);

            if (reason is null)
            {
                portable.Add(problem);
            }
            else if (problem.Kind != ProblemKind.KnockOn)
            {
                unworkable.Add(new UnworkableProblem(problem, reason));
            }
        }

        var tasks = portable
            .GroupBy(problem => problem.Mapping!, StringComparer.Ordinal)
            .Select(group => new PortTask(
                knowledge.Mappings[group.Key],
                group.Select(problem => problem.Subject).Distinct(StringComparer.Ordinal).ToList(),
                group.ToList(),
                group.SelectMany(problem => problem.Evidence).ToList()))
            .ToList();

        return new PortPlan(tasks, unworkable);
    }

    private static string? WhyNot(
        ClassifiedProblem problem,
        ProblemKnowledge knowledge,
        IReadOnlyCollection<string> attempted) =>
        problem.Kind switch
        {
            ProblemKind.KnockOn => "a knock-on: re-checked after the others",
            ProblemKind.Mechanical => $"the recipe ({problem.Recipe}) ran but left it: a person checks the recipe",
            ProblemKind.PlatformDropped => $"the platform dropped {problem.Library}: a decision",
            ProblemKind.Contract => "an HTTP status or URL changed: a decision",
            ProblemKind.Unknown => "no signature matched: a person diagnoses it",
            _ when problem.Mapping is null || !knowledge.Mappings.ContainsKey(problem.Mapping) =>
                "an API move with no known mapping: a person ports it",
            _ when attempted.Contains(problem.Mapping) =>
                $"{problem.Mapping} was already attempted in this run: a person ports it",
            _ when !problem.Subject.Contains('/') =>
                "it fails in a test with no production source file to port: a person ports it",
            _ when !problem.Subject.StartsWith(ProductionSource, StringComparison.Ordinal) =>
                "it is in test code, which the AI may not edit: a person ports it",
            _ => null
        };
}
