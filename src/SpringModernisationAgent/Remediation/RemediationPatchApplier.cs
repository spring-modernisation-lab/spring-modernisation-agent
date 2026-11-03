using System.Text;

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
public sealed class RemediationPatchApplier
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

        return (pending, appliedEdits, null);
    }

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

        public string Content => _content ?? string.Empty;

        private PendingFile(string fullPath, bool existed, bool hasBom, string? content)
        {
            _fullPath = fullPath;
            _existed = existed;
            _hasBom = hasBom;
            _content = content;
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
