using System.Text.RegularExpressions;
using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Porting;

/// <param name="Notes">Why a recorded decision produced no task, for the PR: never silent.</param>
public sealed record DecisionPlan(
    IReadOnlyList<PortTask> Tasks,
    IReadOnlyList<string> Notes);

/// <summary>
/// Turns recorded decisions into scoped port tasks (docs/system-design.md
/// §12.1). A decision's files and obligations come from facts about the
/// repository and its baseline, never from the AI.
/// </summary>
public static partial class DecisionTasks
{
    private const string ProductionSource = "src/main/java";

    public static DecisionPlan Plan(
        IReadOnlyList<DecisionRecord> decisions,
        BehaviourRecord? baseline,
        string repositoryPath,
        IReadOnlyList<ClassifiedProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(problems);

        var tasks = new List<PortTask>();
        var notes = new List<string>();

        foreach (var decision in decisions)
        {
            if (decision.ChosenOption.Port is not { } port)
            {
                continue;
            }

            var label = $"{decision.Id} ({decision.Subject}: {decision.Option})";
            var answered = problems.Where(problem => problem.SignatureId == decision.Playbook.Answers).ToList();

            if (answered.Count == 0)
            {
                notes.Add($"{label}: no problem left that it answers ({decision.Playbook.Answers}), so nothing was ported.");
                continue;
            }

            if (baseline is null)
            {
                notes.Add($"{label}: needs the behaviour baseline to know what to keep, and none was recorded.");
                continue;
            }

            var obligations = TrailingSlashItems(baseline);
            var files = MappingFiles(repositoryPath, obligations);

            if (files.Count == 0)
            {
                notes.Add($"{label}: no production file maps the paths the baseline served with a trailing slash, so nothing was ported.");
                continue;
            }

            var mapping = new ApiMapping(decision.Id, port.Name, port.Notes, port.MustKeep, obligations);

            tasks.Add(new PortTask(mapping, files, answered, answered.SelectMany(problem => problem.Evidence).ToList())
            {
                EvidenceSignatures = [decision.Playbook.Answers],
                ExcusePlatformChanges = false
            });
        }

        return new DecisionPlan(tasks, notes);
    }

    /// <summary>Baseline items for a path with a trailing slash that the baseline served (not a 404).</summary>
    private static List<string> TrailingSlashItems(
        BehaviourRecord baseline) =>
        baseline.Items
            .Where(item => TrailingSlashItem().IsMatch(item.Key) && item.Value != "404")
            .Select(item => item.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Production files with a Spring mapping annotation for a base path
    /// (<c>/api/orders/</c> → <c>/api/orders</c>).
    /// </summary>
    private static List<string> MappingFiles(
        string repositoryPath,
        IReadOnlyList<string> items)
    {
        var paths = items
            .Select(item => TrailingSlashItem().Match(item).Groups["path"].Value.TrimEnd('/'))
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var root = Path.Combine(repositoryPath, ProductionSource.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories)
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("Mapping(", StringComparison.Ordinal)
                       && paths.Any(path => text.Contains($"\"{path}\"", StringComparison.Ordinal));
            })
            .Select(file => Path.GetRelativePath(repositoryPath, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // B-SEC-<profile>-<METHOD> /path/-<ROLE>: the variant with a trailing slash.
    [GeneratedRegex(@"^B-SEC-.+?-[A-Z]+ (?<path>/\S*/)-[^-]+$")]
    private static partial Regex TrailingSlashItem();
}
