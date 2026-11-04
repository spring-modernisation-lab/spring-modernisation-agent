using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Triage;

/// <summary>A failure, classified.</summary>
/// <param name="Id">P1, P2, … in order of appearance.</param>
/// <param name="Subject">The source file (compile errors) or test (Class.method).</param>
/// <param name="SignatureId">The signature that matched; null for <see cref="ProblemKind.Unknown"/>.</param>
/// <param name="Evidence">The diagnostic lines for this problem only.</param>
public sealed record ClassifiedProblem(
    string Id,
    ProblemKind Kind,
    string Subject,
    string? SignatureId,
    string? Mapping,
    string? Recipe,
    string? Library,
    IReadOnlyList<string> Evidence);

/// <summary>
/// Turns a failed check's diagnostics into problems and gives each exactly
/// one kind, from facts (docs/system-design.md §4). Deterministic: the AI
/// never chooses a problem's kind.
/// </summary>
public static partial class ProblemClassifier
{
    /// <param name="diagnostics">From <c>ValidationDiagnosticsExtractor.ExtractUnbounded</c>.</param>
    /// <param name="unsupportedLibraries">Libraries the repository uses that the platform dropped; a platform-dropped signature applies only to these.</param>
    /// <param name="previous">
    /// The classification before the last change, if any. A source file that
    /// was an API move keeps that kind for errors no signature matches: they
    /// are the port's own follow-up errors (for example, a wrong import).
    /// </param>
    public static IReadOnlyList<ClassifiedProblem> Classify(
        ProblemKnowledge knowledge,
        IReadOnlyList<string> diagnostics,
        IReadOnlyCollection<string> unsupportedLibraries,
        IReadOnlyList<ClassifiedProblem>? previous = null)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(unsupportedLibraries);

        var groups = Group(diagnostics);

        // A failure the extractor could not attribute still counts.
        if (groups.Count == 0 && diagnostics.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            groups.Add(("build", diagnostics.Where(line => !string.IsNullOrWhiteSpace(line)).Take(5).ToList()));
        }

        return groups
            .Select((group, index) =>
            {
                var evidence = group.Subject + "\n" + string.Join("\n", group.Lines);

                var signature = knowledge.Signatures.FirstOrDefault(candidate =>
                    (candidate.Kind != ProblemKind.PlatformDropped || unsupportedLibraries.Contains(candidate.Library!))
                    && candidate.Matches(evidence));

                var port = signature is null
                    ? previous?.FirstOrDefault(problem => problem.Kind == ProblemKind.ApiMove && problem.Subject == group.Subject)
                    : null;

                if (port is not null)
                {
                    return port with { Id = $"P{index + 1}", SignatureId = $"{port.SignatureId} (follow-up)", Evidence = group.Lines };
                }

                return new ClassifiedProblem(
                    $"P{index + 1}",
                    signature?.Kind ?? ProblemKind.Unknown,
                    group.Subject,
                    signature?.Id,
                    signature?.Mapping,
                    signature?.Recipe,
                    signature?.Library,
                    group.Lines);
            })
            .ToList();
    }

    /// <summary>A source file's compile errors form one problem; each failing test is one problem.</summary>
    private static List<(string Subject, List<string> Lines)> Group(
        IReadOnlyList<string> diagnostics)
    {
        var groups = new List<(string Subject, List<string> Lines)>();
        var byFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = -1;

        foreach (var line in diagnostics)
        {
            var error = CompileError().Match(line);

            if (error.Success)
            {
                var file = SourcePath(error.Groups["file"].Value);

                if (!byFile.TryGetValue(file, out current))
                {
                    current = groups.Count;
                    byFile[file] = current;
                    groups.Add((file, []));
                }

                groups[current].Lines.Add(line);
                continue;
            }

            var test = FailedTest().Match(line);

            if (test.Success)
            {
                current = groups.Count;
                groups.Add((test.Groups["test"].Value, [line]));
                continue;
            }

            if (current >= 0 && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                groups[current].Lines.Add(line);
            }
            else
            {
                current = -1;
            }
        }

        return groups;
    }

    private static string SourcePath(
        string path)
    {
        path = path.Replace('\\', '/');
        var source = path.IndexOf("src/", StringComparison.Ordinal);
        return source >= 0 ? path[source..] : path;
    }

    // [ERROR] /any/path/src/main/java/a/B.java:[12,72] cannot find symbol
    [GeneratedRegex(@"^\[ERROR\]\s+(?<file>\S+?\.(?:java|kt)):\[\d+,\d+\]")]
    private static partial Regex CompileError();

    // FAILED TEST a.b.CTests.method (error): ...
    [GeneratedRegex(@"^FAILED TEST (?<test>[\w.$]+)")]
    private static partial Regex FailedTest();
}
