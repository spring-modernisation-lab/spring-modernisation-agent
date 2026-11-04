using System.Text;
using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// Applies a Remediation Agent proposal under fixed guardrails, all or
/// nothing: every edit is checked and computed in memory first, and files
/// are written only if all of them pass.
/// <list type="bullet">
/// <item>Only source and resource files under a <c>src/main/</c> folder may change, so
/// tests, <c>pom.xml</c>, CI files and build scripts are out of reach.</item>
/// <item>Only <c>.java</c>, <c>.kt</c>, <c>.properties</c>, <c>.yml</c> and <c>.yaml</c> files.</item>
/// <item>Each search text must match exactly once; new files must not already exist.</item>
/// <item>At most <see cref="MaxFiles"/> files and <see cref="MaxChangedLines"/> lines per proposal.</item>
/// <item>No path may pass through a link.</item>
/// </list>
/// </summary>
public sealed partial class RemediationPatchApplier
{
    public const int MaxFiles = 10;

    public const int MaxChangedLines = 300;

    /// <summary>Larger files must be changed with search-and-replace edits.</summary>
    public const int MaxWholeFileLines = 400;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".java", ".kt", ".properties", ".yml", ".yaml"
    };

    private static readonly HashSet<string> ForbiddenSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", ".mvn", "target", "node_modules"
    };

    public RemediationApplyResult Apply(
        string repositoryPath,
        IReadOnlyList<RemediationEdit> edits)
    {
        var (pending, appliedEdits, rejection) = Prepare(repositoryPath, edits);

        if (pending is null)
        {
            return Reject(rejection!);
        }

        foreach (var file in pending.Values)
        {
            file.Write();
        }

        return new RemediationApplyResult(
            Applied: true,
            RejectionReason: null,
            ChangedFiles: pending.Keys.Order(StringComparer.Ordinal).ToList())
        {
            AppliedEdits = appliedEdits
        };
    }

    /// <summary>
    /// Runs exactly the same checks as <see cref="Apply"/> and returns the
    /// resulting file contents, without writing anything, so the agent can
    /// inspect its edits before it answers.
    /// </summary>
    public RemediationPreviewResult Preview(
        string repositoryPath,
        IReadOnlyList<RemediationEdit> edits)
    {
        var (pending, _, rejection) = Prepare(repositoryPath, edits);

        return pending is null
            ? new RemediationPreviewResult(false, rejection, new Dictionary<string, string>())
            : new RemediationPreviewResult(
                true,
                null,
                pending.ToDictionary(entry => entry.Key, entry => entry.Value.Content));
    }

    /// <summary>Checks every edit and computes the new contents in memory.</summary>
    private static (Dictionary<string, PendingFile>? Pending, List<AppliedEdit> Edits, string? Rejection) Prepare(
        string repositoryPath,
        IReadOnlyList<RemediationEdit> edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(edits);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));

        if (edits.Count == 0)
        {
            return (null, [], "the proposal contains no edits.");
        }

        // Insertion order is first-seen order.
        var pending = new Dictionary<string, PendingFile>(StringComparer.Ordinal);
        var changedLines = 0;
        var appliedEdits = new List<AppliedEdit>();

        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var label = $"edit {index + 1} ('{edit.Path}')";
            var pathError = CheckPath(edit.Path);

            if (pathError is not null)
            {
                return (null, [], $"{label}: {pathError}");
            }

            var relativePath = Normalise(edit.Path);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return (null, [], $"{label}: the path is outside the repository.");
            }

            if (PassesThroughLink(root, relativePath))
            {
                return (null, [], $"{label}: links are not followed.");
            }

            if (!pending.TryGetValue(relativePath, out var file))
            {
                file = PendingFile.Load(fullPath);
                pending[relativePath] = file;
            }

            var (editError, editChangedLines, kind) = file.ApplyEdit(edit);

            if (editError is not null)
            {
                return (null, [], $"{label}: {editError}");
            }

            changedLines += editChangedLines;
            appliedEdits.Add(new AppliedEdit(relativePath, kind, editChangedLines));
        }

        if (pending.Count > MaxFiles)
        {
            return (null, [], $"a proposal may change at most {MaxFiles} files; this one changes {pending.Count}.");
        }

        if (changedLines > MaxChangedLines)
        {
            return (null, [],
                $"a proposal may change at most {MaxChangedLines} lines; this one changes about {changedLines}.");
        }

        var designError = CheckDesign(root, pending);

        return designError is null
            ? (pending, appliedEdits, null)
            : (null, [], designError);
    }

    /// <summary>
    /// Two rules on the resulting code, not just on paths:
    /// no new types (fixing code is the agent's job, designing new components
    /// is not), and production code must not refer to test-only files (a
    /// sign it special-cases the test to make it pass).
    /// </summary>
    private static string? CheckDesign(
        string root,
        IReadOnlyDictionary<string, PendingFile> pending)
    {
        HashSet<string>? testOnlyNames = null;

        foreach (var (relativePath, file) in pending)
        {
            var extension = Path.GetExtension(relativePath);

            if (extension is ".java" or ".kt")
            {
                var before = StripCode(file.OriginalContent);
                var after = StripCode(file.Content);
                var added = TypeNames(after).Except(TypeNames(before), StringComparer.Ordinal).ToList();

                if (added.Count > 0)
                {
                    return $"'{relativePath}': adds new types ({string.Join(", ", added)}); writing a new " +
                           "component is a design decision. Change the existing types, or choose NeedsHumanDecision.";
                }

                // Run 20: the same new component, written as an anonymous
                // class returned from an existing type made to implement a
                // framework interface.
                var anonymous = NewAnonymousClasses(before, after);

                if (anonymous.Count > 0)
                {
                    return $"'{relativePath}': adds anonymous classes ({string.Join(", ", anonymous)}); implementing " +
                           "a framework or library interface yourself is a design decision. Change the existing code, " +
                           "or choose NeedsHumanDecision.";
                }

                var supertypes = NewSupertypes(before, after);

                if (supertypes.Count > 0)
                {
                    return $"'{relativePath}': makes existing types extend or implement new types " +
                           $"({string.Join(", ", supertypes)}); that turns them into a new component, which is a " +
                           "design decision. Change the existing code, or choose NeedsHumanDecision.";
                }
            }

            testOnlyNames ??= TestOnlyFileNames(root);

            var references = testOnlyNames
                .Where(name =>
                    file.Content.Contains(name, StringComparison.Ordinal)
                    && !file.OriginalContent.Contains(name, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();

            if (references.Count > 0)
            {
                return $"'{relativePath}': production code refers to test files ({string.Join(", ", references)}); " +
                       "code under src/main must not special-case tests.";
            }
        }

        return null;
    }

    /// <summary>Code without comments or string contents, so neither can hide or fake a declaration.</summary>
    private static string StripCode(
        string code)
    {
        var stripped = StringLiteral().Replace(BlockComment().Replace(code, " "), "\"\"");
        return LineComment().Replace(stripped, string.Empty);
    }

    private static IReadOnlyList<string> TypeNames(
        string stripped) =>
        TypeDeclaration().Matches(stripped)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Base types of anonymous classes the edit adds (more of them than before).</summary>
    private static IReadOnlyList<string> NewAnonymousClasses(
        string before,
        string after)
    {
        static Dictionary<string, int> Count(string code) =>
            AnonymousClass().Matches(RemoveGenerics(code))
                .GroupBy(match => SimpleName(match.Groups[1].Value), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var original = Count(before);

        return Count(after)
            .Where(entry => entry.Value > original.GetValueOrDefault(entry.Key))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Supertypes added to types that already existed, as "Type: Supertype".
    /// Generic arguments are ignored, so <c>UserType</c> to
    /// <c>UserType&lt;Money&gt;</c> is not a new supertype.
    /// </summary>
    private static IReadOnlyList<string> NewSupertypes(
        string before,
        string after)
    {
        var original = Supertypes(before);

        return Supertypes(after)
            .Where(entry => original.ContainsKey(entry.Key))
            .SelectMany(entry => entry.Value
                .Except(original[entry.Key], StringComparer.Ordinal)
                .Select(supertype => $"{entry.Key}: {supertype}"))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, HashSet<string>> Supertypes(
        string stripped)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (Match match in TypeHeader().Matches(RemoveGenerics(stripped)))
        {
            var names = result.TryGetValue(match.Groups[1].Value, out var existing)
                ? existing
                : result[match.Groups[1].Value] = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match clause in SupertypeClause().Matches(match.Groups[2].Value))
            {
                foreach (var name in clause.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    names.Add(SimpleName(name));
                }
            }
        }

        return result;
    }

    private static string RemoveGenerics(
        string code)
    {
        string previous;

        do
        {
            previous = code;
            code = GenericArguments().Replace(code, string.Empty);
        }
        while (code != previous);

        return code;
    }

    private static string SimpleName(
        string name) =>
        name.Trim().Split('.')[^1];

    /// <summary>File names (and test class names) that exist only under a src/test folder.</summary>
    private static HashSet<string> TestOnlyFileNames(
        string root)
    {
        var testNames = new HashSet<string>(StringComparer.Ordinal);
        var mainNames = new HashSet<string>(StringComparer.Ordinal);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            var segments = Path.GetRelativePath(root, file).Replace('\\', '/').Split('/');

            if (segments.Any(ForbiddenSegments.Contains))
            {
                continue;
            }

            var target = ContainsPair(segments, "src", "test") ? testNames
                : ContainsPair(segments, "src", "main") ? mainNames
                : null;

            if (target is null)
            {
                continue;
            }

            var name = Path.GetFileName(file);
            target.Add(name);

            if (Path.GetExtension(name) is ".java" or ".kt")
            {
                target.Add(Path.GetFileNameWithoutExtension(name));
            }
        }

        // Very short names would match ordinary words.
        testNames.RemoveWhere(name => name.Length < 6 || mainNames.Contains(name));
        return testNames;
    }

    [GeneratedRegex(@"\b(?:class|interface|record|enum|object)\s+([A-Z][A-Za-z0-9_]*)")]
    private static partial Regex TypeDeclaration();

    // new Cache() {   new Foo(a, b(c)) {   (array initialisers have no parentheses)
    [GeneratedRegex(@"\bnew\s+([A-Za-z_][\w.]*)\s*\((?:[^()]|\([^()]*\))*\)\s*\{")]
    private static partial Regex AnonymousClass();

    // The header between a type's name and its opening brace.
    [GeneratedRegex(@"\b(?:class|interface|record|enum)\s+([A-Z][A-Za-z0-9_]*)([^{;]*)\{")]
    private static partial Regex TypeHeader();

    [GeneratedRegex(@"\b(?:extends|implements)\s+([\w.\s,]+?)(?=\b(?:extends|implements|permits)\b|$)")]
    private static partial Regex SupertypeClause();

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex GenericArguments();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"""(?:\\.|[^""\\\n])*""")]
    private static partial Regex StringLiteral();

    private static string? CheckPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "a repository-relative path is required.";
        }

        var normalised = Normalise(path);

        if (Path.IsPathRooted(path)
            || path.TrimStart().StartsWith('/')
            || (normalised.Length > 1 && normalised[1] == ':'))
        {
            return "paths must be repository-relative.";
        }

        var segments = normalised.Split('/');

        if (segments.Any(segment => segment == ".."))
        {
            return "paths may not contain '..'.";
        }

        if (ContainsPair(segments, "src", "test"))
        {
            return "test sources may not be edited; a test that needs changing is a human decision.";
        }

        if (segments.Any(ForbiddenSegments.Contains) || !ContainsPair(segments, "src", "main"))
        {
            return "only source and resource files under src/main/ may be edited.";
        }

        if (!AllowedExtensions.Contains(Path.GetExtension(normalised)))
        {
            return $"this file type may not be edited; allowed: {string.Join(", ", AllowedExtensions)}.";
        }

        return null;
    }

    private static bool ContainsPair(
        IReadOnlyList<string> segments,
        string first,
        string second)
    {
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (segments[index] == first && segments[index + 1] == second)
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalise(
        string path) =>
        string.Join(
            '/',
            path.Trim()
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != "."));

    private static bool PassesThroughLink(
        string root,
        string relativePath)
    {
        var current = root;

        foreach (var segment in relativePath.Split('/'))
        {
            current = Path.Combine(current, segment);

            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountLines(
        string text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : text.Count(character => character == '\n') + (text.EndsWith('\n') ? 0 : 1);

    private static RemediationApplyResult Reject(
        string reason) =>
        new(Applied: false, RejectionReason: reason, ChangedFiles: []);

    /// <summary>One file's content while a proposal is checked in memory.</summary>
    private sealed class PendingFile
    {
        private readonly string _fullPath;
        private readonly bool _existed;
        private readonly bool _hasBom;
        private string? _content;
        private readonly string _originalContent;

        public string Content => _content ?? string.Empty;

        public string OriginalContent => _originalContent;

        private PendingFile(string fullPath, bool existed, bool hasBom, string? content)
        {
            _fullPath = fullPath;
            _existed = existed;
            _hasBom = hasBom;
            _content = content;
            _originalContent = content ?? string.Empty;
        }

        public static PendingFile Load(
            string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                return new PendingFile(fullPath, existed: false, hasBom: false, content: null);
            }

            var bytes = File.ReadAllBytes(fullPath);
            var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);

            return new PendingFile(
                fullPath,
                existed: true,
                hasBom,
                Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0)));
        }

        /// <summary>
        /// Applies one edit in memory. An empty search text means the
        /// replacement is the file's complete new content (creating the file
        /// if it does not exist). Returns the number of changed lines.
        /// </summary>
        public (string? Error, int ChangedLines, RemediationEditKind Kind) ApplyEdit(
            RemediationEdit edit)
        {
            var search = edit.Search ?? string.Empty;
            var replace = edit.Replace ?? string.Empty;

            // Models write "\n"; match and write in the file's own style.
            if (_content?.Contains("\r\n", StringComparison.Ordinal) == true)
            {
                search = ToCrlf(search);
                replace = ToCrlf(replace);
            }

            if (search.Length == 0)
            {
                var kind = _existed
                    ? RemediationEditKind.WholeFileRewrite
                    : RemediationEditKind.NewFile;

                if (replace.Length == 0)
                {
                    return ("a whole-file edit needs the file's complete new content.", 0, kind);
                }

                var currentLines = CountLines(_content ?? string.Empty);

                if (_existed && currentLines > MaxWholeFileLines)
                {
                    return (
                        $"whole-file edits are allowed only for files of up to {MaxWholeFileLines} lines; " +
                        $"this file has {currentLines}. Use search-and-replace edits instead.",
                        0,
                        kind);
                }

                var changed = CountChangedLines(_content ?? string.Empty, replace);
                _content = replace;
                return (null, changed, kind);
            }

            if (_content is null)
            {
                return ("the file does not exist; use an empty search text to create it.", 0, RemediationEditKind.SearchReplace);
            }

            var first = _content.IndexOf(search, StringComparison.Ordinal);

            if (first < 0)
            {
                return ("the search text was not found; read the file again and copy it exactly.", 0, RemediationEditKind.SearchReplace);
            }

            if (_content.IndexOf(search, first + 1, StringComparison.Ordinal) >= 0)
            {
                return ("the search text matches more than once; include more surrounding lines.", 0, RemediationEditKind.SearchReplace);
            }

            _content = string.Concat(_content.AsSpan(0, first), replace, _content.AsSpan(first + search.Length));
            return (null, Math.Max(CountLines(search), CountLines(replace)), RemediationEditKind.SearchReplace);
        }

        /// <summary>
        /// Lines removed plus lines added, ignoring order: a cheap, slightly
        /// generous measure of how much a whole-file rewrite changes.
        /// </summary>
        private static int CountChangedLines(
            string before,
            string after)
        {
            var remaining = before.Replace("\r\n", "\n").Split('\n')
                .GroupBy(line => line, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            var added = 0;

            foreach (var line in after.Replace("\r\n", "\n").Split('\n'))
            {
                if (remaining.TryGetValue(line, out var count) && count > 0)
                {
                    remaining[line] = count - 1;
                }
                else
                {
                    added++;
                }
            }

            return added + remaining.Values.Sum();
        }

        public void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fullPath)!);
            File.WriteAllText(_fullPath, _content, new UTF8Encoding(_hasBom));
        }

        private static string ToCrlf(
            string text) =>
            text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
