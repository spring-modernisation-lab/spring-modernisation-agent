using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Transformation;

/// <summary>
/// Reduces raw Maven validation output to the lines that explain a failure.
/// Deterministic and bounded, so AI agents receive concise evidence rather
/// than multi-megabyte logs, and never see runner workspace paths.
/// </summary>
public static partial class ValidationDiagnosticsExtractor
{
    public const int MaxLines = 60;

    public const int MaxLineLength = 400;

    private const string ErrorPrefix = "[ERROR]";

    private const string GoalFailurePrefix = "[ERROR] Failed to execute goal";

    private static readonly string[] BoilerplatePrefixes =
    [
        "-> [Help",
        "[Help ",
        "To see the full stack trace",
        "Re-run Maven using",
        "For more information about the errors",
        "BUILD FAILURE"
    ];

    public static IReadOnlyList<string> Extract(
        string? standardOutput,
        string? standardError)
    {
        var lines = new List<string>();

        lines.AddRange(ExtractMavenErrors(standardOutput));

        lines.AddRange(
            SplitLines(standardError)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim()));

        return Bound(
            lines
                .Select(RemoveWorkspacePaths)
                .ToList());
    }

    private static IEnumerable<string> ExtractMavenErrors(
        string? standardOutput)
    {
        var errors = new List<string>();

        // javac prints detail such as "  symbol: class X" on indented lines
        // without a level prefix; keep them only directly after a kept error.
        var previousLineKept = false;

        foreach (var rawLine in SplitLines(standardOutput))
        {
            var line = rawLine.TrimEnd();

            if (!line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                var isContinuation =
                    previousLineKept
                    && line.Length > 0
                    && char.IsWhiteSpace(line[0]);

                if (isContinuation)
                {
                    errors.Add(line);
                }
                else
                {
                    previousLineKept = false;
                }

                continue;
            }

            // Maven repeats the detail block after "Failed to execute goal";
            // everything from there on is duplicate or generic help text.
            if (line.StartsWith(GoalFailurePrefix, StringComparison.Ordinal))
            {
                if (errors.Count == 0)
                {
                    errors.Add(line);
                }

                break;
            }

            if (IsBoilerplate(line))
            {
                previousLineKept = false;
                continue;
            }

            errors.Add(line);
            previousLineKept = true;
        }

        return errors;
    }

    private static bool IsBoilerplate(
        string line)
    {
        var content = line[ErrorPrefix.Length..].Trim();

        return content.Length == 0
               || BoilerplatePrefixes.Any(prefix =>
                   content.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string RemoveWorkspacePaths(
        string line)
    {
        // Workspaces are "<root>/<repository>-<32 hex>/"; keep only the
        // repository-relative path.
        return WorkspacePathPattern().Replace(line, string.Empty);
    }

    private static IReadOnlyList<string> Bound(
        IReadOnlyList<string> lines)
    {
        var bounded = lines
            .Take(MaxLines)
            .Select(line => line.Length > MaxLineLength
                ? line[..MaxLineLength] + "..."
                : line)
            .ToList();

        if (lines.Count > MaxLines)
        {
            bounded.Add(
                $"... {lines.Count - MaxLines} more diagnostic lines omitted");
        }

        return bounded;
    }

    private static IEnumerable<string> SplitLines(
        string? text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split('\n').Select(line => line.TrimEnd('\r'));
    }

    [GeneratedRegex(@"\S*?[\\/][^\\/\s]+-[0-9a-f]{32}[\\/]")]
    private static partial Regex WorkspacePathPattern();
}
