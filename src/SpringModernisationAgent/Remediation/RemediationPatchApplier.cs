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
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(edits);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));

        if (edits.Count == 0)
        {
            return Reject("the proposal contains no edits.");
        }

        // Path -> (original state, pending content), in first-seen order.
        var pending = new Dictionary<string, PendingFile>(StringComparer.Ordinal);
        var changedLines = 0;

        foreach (var edit in edits)
        {
            var pathError = CheckPath(edit.Path);

            if (pathError is not null)
            {
                return Reject($"'{edit.Path}': {pathError}");
            }

            var relativePath = Normalise(edit.Path);
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Reject($"'{edit.Path}': the path is outside the repository.");
            }

            if (PassesThroughLink(root, relativePath))
            {
                return Reject($"'{edit.Path}': links are not followed.");
            }

            if (!pending.TryGetValue(relativePath, out var file))
            {
                file = PendingFile.Load(fullPath);
                pending[relativePath] = file;
            }

            var editError = file.ApplyEdit(edit);

            if (editError is not null)
            {
                return Reject($"'{edit.Path}': {editError}");
            }

            changedLines += Math.Max(CountLines(edit.Search), CountLines(edit.Replace));
        }

        if (pending.Count > MaxFiles)
        {
            return Reject($"a proposal may change at most {MaxFiles} files; this one changes {pending.Count}.");
        }

        if (changedLines > MaxChangedLines)
        {
            return Reject(
                $"a proposal may change at most {MaxChangedLines} lines; this one changes about {changedLines}.");
        }

        foreach (var file in pending.Values)
        {
            file.Write();
        }

        return new RemediationApplyResult(
            Applied: true,
            RejectionReason: null,
            ChangedFiles: pending.Keys.Order(StringComparer.Ordinal).ToList());
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

        public string? ApplyEdit(
            RemediationEdit edit)
        {
            var search = edit.Search ?? string.Empty;
            var replace = edit.Replace ?? string.Empty;

            if (search.Length == 0)
            {
                if (_existed || _content is not null)
                {
                    return "the file already exists; use a search text to edit it.";
                }

                if (replace.Length == 0)
                {
                    return "a new file needs content.";
                }

                _content = replace;
                return null;
            }

            if (_content is null)
            {
                return "the file does not exist; use an empty search text to create it.";
            }

            // Models write "\n"; match and write in the file's own style.
            if (_content.Contains("\r\n", StringComparison.Ordinal))
            {
                search = ToCrlf(search);
                replace = ToCrlf(replace);
            }

            var first = _content.IndexOf(search, StringComparison.Ordinal);

            if (first < 0)
            {
                return "the search text was not found; read the file again and copy it exactly.";
            }

            if (_content.IndexOf(search, first + 1, StringComparison.Ordinal) >= 0)
            {
                return "the search text matches more than once; include more surrounding lines.";
            }

            _content = string.Concat(_content.AsSpan(0, first), replace, _content.AsSpan(first + search.Length));
            return null;
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
