using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Recipes;

/// <summary>
/// One compile error, keyed so the same error is recognised after a change:
/// the source file from <c>src/</c> on, the message, and its symbol and
/// location detail. Line and column are left out, because edits shift them.
/// </summary>
public sealed record CompileErrorKey(
    string File,
    string Message)
{
    public override string ToString() => $"{System.IO.Path.GetFileName(File)}: {Message}";
}

/// <summary>Which errors a change removed and which it added (per occurrence, not just per kind).</summary>
public sealed record CompileErrorComparison(
    IReadOnlyList<CompileErrorKey> Removed,
    IReadOnlyList<CompileErrorKey> Added,
    int Unchanged);

/// <summary>
/// Compares two sets of compile errors. Comparing counts misleads: probe #6
/// went from 12 to 13 errors while fixing one and adding three.
/// </summary>
public static partial class CompileErrorDiff
{
    /// <summary>The compile errors in extracted diagnostics (see <c>ValidationDiagnosticsExtractor.ExtractUnbounded</c>).</summary>
    public static IReadOnlyList<CompileErrorKey> Errors(
        IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var errors = new List<(string File, List<string> Parts)>();

        foreach (var line in diagnostics)
        {
            var error = ErrorLine().Match(line);

            if (error.Success)
            {
                errors.Add((SourcePath(error.Groups["file"].Value), [Normalise(error.Groups["message"].Value)]));
            }
            else if (errors.Count > 0 && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                // "  symbol: class BigDecimal" / "  location: class ...": part of the error.
                errors[^1].Parts.Add(Normalise(line));
            }
        }

        return errors
            .Select(error => new CompileErrorKey(error.File, string.Join(" | ", error.Parts)))
            .ToList();
    }

    public static CompileErrorComparison Compare(
        IReadOnlyList<CompileErrorKey> before,
        IReadOnlyList<CompileErrorKey> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var remaining = after.GroupBy(error => error).ToDictionary(group => group.Key, group => group.Count());
        var removed = new List<CompileErrorKey>();
        var unchanged = 0;

        foreach (var error in before)
        {
            if (remaining.TryGetValue(error, out var count) && count > 0)
            {
                remaining[error] = count - 1;
                unchanged++;
            }
            else
            {
                removed.Add(error);
            }
        }

        var added = remaining
            .SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value))
            .ToList();

        return new CompileErrorComparison(removed, added, unchanged);
    }

    /// <summary>The path from <c>src/</c> on, so workspace and throwaway copies compare equal.</summary>
    private static string SourcePath(
        string path)
    {
        path = path.Replace('\\', '/');
        var source = path.IndexOf("src/", StringComparison.Ordinal);
        return source >= 0 ? path[source..] : path;
    }

    private static string Normalise(
        string text) =>
        Whitespace().Replace(text.Trim(), " ");

    // [ERROR] /any/path/src/main/java/a/B.java:[12,72] cannot find symbol
    [GeneratedRegex(@"^\[ERROR\]\s+(?<file>\S+?\.(?:java|kt)):\[\d+,\d+\]\s*(?<message>.*)$")]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
