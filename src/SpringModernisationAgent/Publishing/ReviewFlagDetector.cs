using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Publishing;

/// <summary>One stage's patch, as recorded during the migration.</summary>
/// <param name="Stage">For example <c>01-openrewrite</c> or <c>03-remediation-attempt-2</c>.</param>
/// <param name="AiWritten">True for remediation attempts.</param>
/// <param name="Text">The unified diff.</param>
public sealed record ReviewPatch(
    string Stage,
    bool AiWritten,
    string Text);

public enum ReviewFlagKind
{
    /// <summary>Security configuration was changed by the AI.</summary>
    SecurityChanged,

    /// <summary>An HTTP request mapping was changed by the AI.</summary>
    RequestMappingChanged,

    /// <summary>The AI removed a method call and never added it back.</summary>
    CallRemoved,

    /// <summary>A CI workflow file changed (any stage).</summary>
    CiConfigurationChanged,

    /// <summary>The AI wrote a lot of new code in one file.</summary>
    LargeAddition
}

/// <summary>Something a reviewer should look at, found deterministically.</summary>
public sealed record ReviewFlag(
    ReviewFlagKind Kind,
    string Stage,
    string Path,
    string Detail);

/// <summary>
/// Finds changes a green build cannot vouch for. Tests prove what they
/// cover; these flags point the reviewer at what they may not: security
/// rules, request mappings, behaviour the AI deleted, and CI changes.
/// They are deliberately cheap and slightly over-eager.
/// </summary>
public static partial class ReviewFlagDetector
{
    public const int LargeAdditionLines = 40;

    private static readonly string[] SecurityTerms =
    [
        "SecurityFilterChain", "requestMatchers", "authorizeHttpRequests", "hasRole", "hasAnyRole",
        "hasAuthority", "permitAll", "denyAll", "@PreAuthorize", "@Secured", "WebSecurityCustomizer",
        "ignoring()", "csrf", "UserDetailsService", "PasswordEncoder"
    ];

    public static IReadOnlyList<ReviewFlag> Detect(
        IReadOnlyList<ReviewPatch> patches)
    {
        ArgumentNullException.ThrowIfNull(patches);

        var flags = new List<ReviewFlag>();
        var parsed = patches
            .Select(patch => (Patch: patch, Files: UnifiedDiff.Parse(patch.Text)))
            .ToList();

        foreach (var (patch, files) in parsed)
        {
            foreach (var file in files)
            {
                if (file.Path.StartsWith(".github/workflows/", StringComparison.Ordinal))
                {
                    Add(flags, ReviewFlagKind.CiConfigurationChanged, patch.Stage, file.Path,
                        "A CI workflow changed; check the pipeline still builds what it should.");
                }

                if (!patch.AiWritten)
                {
                    continue;
                }

                if (file.AddedLines.Count >= LargeAdditionLines)
                {
                    Add(flags, ReviewFlagKind.LargeAddition, patch.Stage, file.Path,
                        $"The AI added {file.AddedLines.Count} lines here; check it is not re-implementing " +
                        "a library or framework feature, and read it closely.");
                }

                var changed = file.AddedLines.Concat(file.RemovedLines).ToList();

                if (Path.GetFileName(file.Path).Contains("Security", StringComparison.OrdinalIgnoreCase)
                    || changed.Any(line => SecurityTerms.Any(term => line.Contains(term, StringComparison.Ordinal))))
                {
                    Add(flags, ReviewFlagKind.SecurityChanged, patch.Stage, file.Path,
                        "The AI changed security configuration; check who can reach what is unchanged.");
                }

                var mappings = file.AddedLines.Where(line => MappingAnnotation().IsMatch(line)).ToList();

                if (mappings.Count > 0 || file.RemovedLines.Any(line => MappingAnnotation().IsMatch(line)))
                {
                    var shown = mappings.Count == 0
                        ? "mappings removed"
                        : string.Join("; ", mappings.Select(line => line.Trim()));

                    Add(flags, ReviewFlagKind.RequestMappingChanged, patch.Stage, file.Path,
                        $"{shown}. Check the security rules still cover every new path.");
                }
            }
        }

        // A call is only "removed" if no AI stage adds it back and it is
        // not still present unchanged in the surrounding code.
        foreach (var (patch, files) in parsed.Where(entry => entry.Patch.AiWritten))
        {
            foreach (var file in files)
            {
                var stillPresent = parsed
                    .Where(entry => entry.Patch.AiWritten)
                    .SelectMany(entry => entry.Files)
                    .Where(other => other.Path == file.Path)
                    .SelectMany(other => other.AddedLines.Concat(other.ContextLines))
                    .SelectMany(CallNames)
                    .ToHashSet(StringComparer.Ordinal);

                // Short names are builder plumbing (and(), of(), get()).
                var removed = file.RemovedLines
                    .SelectMany(CallNames)
                    .Where(name => name.Length > 3 && !stillPresent.Contains(name))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (removed.Count > 0)
                {
                    Add(flags, ReviewFlagKind.CallRemoved, patch.Stage, file.Path,
                        $"The AI removed calls it did not add back ({string.Join(", ", removed)}); " +
                        "check each behaviour they configured was carried over or renamed.");
                }
            }
        }

        return flags;
    }

    private static IEnumerable<string> CallNames(
        string line) =>
        MethodCall().Matches(line).Select(match => match.Groups[1].Value);

    private static void Add(
        List<ReviewFlag> flags,
        ReviewFlagKind kind,
        string stage,
        string path,
        string detail)
    {
        if (!flags.Any(flag => flag.Kind == kind && flag.Path == path && flag.Detail == detail))
        {
            flags.Add(new ReviewFlag(kind, stage, path, detail));
        }
    }

    [GeneratedRegex(@"@(Request|Get|Post|Put|Patch|Delete)Mapping\b")]
    private static partial Regex MappingAnnotation();

    [GeneratedRegex(@"\.(\w+)\s*\(")]
    private static partial Regex MethodCall();
}

/// <summary>A file's changes in a unified diff.</summary>
public sealed record DiffFile(
    string Path,
    IReadOnlyList<string> AddedLines,
    IReadOnlyList<string> RemovedLines,
    IReadOnlyList<string> ContextLines);

public static class UnifiedDiff
{
    public static IReadOnlyList<DiffFile> Parse(
        string patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var files = new List<DiffFile>();
        string? path = null;
        List<string> added = [], removed = [], context = [];

        void Flush()
        {
            if (path is not null)
            {
                files.Add(new DiffFile(path, added, removed, context));
            }
        }

        foreach (var line in patch.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                Flush();
                var target = line.LastIndexOf(" b/", StringComparison.Ordinal);
                path = target >= 0 ? line[(target + 3)..] : line["diff --git ".Length..];
                (added, removed, context) = ([], [], []);
            }
            else if (path is null
                     || line.StartsWith("+++", StringComparison.Ordinal)
                     || line.StartsWith("---", StringComparison.Ordinal)
                     || line.StartsWith("@@", StringComparison.Ordinal))
            {
                continue;
            }
            else if (line.StartsWith('+'))
            {
                added.Add(line[1..]);
            }
            else if (line.StartsWith('-'))
            {
                removed.Add(line[1..]);
            }
            else if (line.StartsWith(' '))
            {
                context.Add(line[1..]);
            }
        }

        Flush();
        return files;
    }
}
