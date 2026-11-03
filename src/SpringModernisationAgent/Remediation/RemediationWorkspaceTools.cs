using System.ComponentModel;
using System.IO.Enumeration;
using System.Text;
using Microsoft.Extensions.AI;

namespace SpringModernisationAgent.Remediation;

/// <summary>
/// The only capabilities the Remediation Agent has: read-only views of one
/// migration workspace. The model asks for a tool call; the Agent Framework
/// runs these methods in this process. Every path is contained to the
/// workspace, <c>.git</c> and CI files are off limits, links are never
/// followed, output is bounded, absolute paths are never returned, and every
/// call is recorded. Problems are returned as <c>Error:</c> text so the
/// model can correct itself; they never throw.
/// </summary>
public sealed class RemediationWorkspaceTools
{
    public const int DefaultMaxCalls = 40;

    public const int MaxPreviews = 5;

    public const int MaxReadLines = 400;

    public const int MaxSearchResults = 50;

    public const int MaxListedFiles = 300;

    private const int MaxLineLength = 400;

    private const int MaxEvidenceChars = 64 * 1024;

    private const long MaxFileBytes = 1024 * 1024;

    // Never shown: version control, CI configuration and build output.
    private static readonly HashSet<string> HiddenDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", "target", "node_modules", ".idea", ".mvn"
    };

    private readonly string _root;
    private readonly string _openRewritePatch;
    private readonly IReadOnlyList<string> _diagnostics;
    private readonly int _maxCalls;
    private readonly List<RemediationToolCall> _calls = [];
    private readonly Lock _gate = new();

    public RemediationWorkspaceTools(
        string repositoryPath,
        string openRewritePatch,
        IReadOnlyList<string> diagnostics,
        int maxCalls = DefaultMaxCalls)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        ArgumentNullException.ThrowIfNull(openRewritePatch);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCalls);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
        _openRewritePatch = openRewritePatch;
        _diagnostics = diagnostics;
        _maxCalls = maxCalls;
    }

    public IReadOnlyList<RemediationToolCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public IList<AITool> AsAITools() =>
    [
        AIFunctionFactory.Create(
            ReadFile,
            "read_file",
            "Read a text file in the repository, with line numbers. Paths are repository-relative."),
        AIFunctionFactory.Create(
            SearchSource,
            "search_source",
            "Search repository text files for a literal string (case-sensitive). Returns path:line: text."),
        AIFunctionFactory.Create(
            ListFiles,
            "list_files",
            "List repository files, optionally under a repository-relative directory."),
        AIFunctionFactory.Create(
            GetOpenRewritePatch,
            "get_openrewrite_patch",
            "The patch OpenRewrite applied before validation."),
        AIFunctionFactory.Create(
            GetDiagnostics,
            "get_diagnostics",
            "The current validation diagnostics: failing tests and build errors."),
        AIFunctionFactory.Create(
            PreviewEdits,
            "preview_edits",
            "Check proposed edits against the same rules that will apply them, and show the " +
            "resulting files with line numbers. Nothing is written. Use it before answering.")
    ];

    public string ReadFile(
        [Description("Repository-relative file path, e.g. src/main/java/com/example/App.java")] string path,
        [Description("First line to return (1-based). Optional.")] int? startLine = null,
        [Description("Last line to return (inclusive). Optional.")] int? endLine = null)
    {
        var arguments = startLine is null && endLine is null
            ? path
            : $"{path}:{startLine?.ToString() ?? ""}-{endLine?.ToString() ?? ""}";

        return Run("read_file", arguments, () =>
        {
            var file = ResolveFile(path);

            var lines = File.ReadAllLines(file);
            var first = Math.Max(1, startLine ?? 1);
            var last = Math.Min(lines.Length, endLine ?? lines.Length);

            if (first > last)
            {
                return Fail($"no lines in range {first}-{last}; the file has {lines.Length} lines.");
            }

            var output = new StringBuilder();
            var shownLast = Math.Min(last, first + MaxReadLines - 1);

            for (var number = first; number <= shownLast; number++)
            {
                output.Append(number).Append(": ").AppendLine(Bound(lines[number - 1]));
            }

            if (shownLast < last)
            {
                output.AppendLine(
                    $"... truncated at {MaxReadLines} lines; request startLine={shownLast + 1} for more.");
            }

            return Ok(output.ToString());
        });
    }

    public string SearchSource(
        [Description("Literal text to find (case-sensitive).")] string pattern,
        [Description("Optional file name pattern, e.g. *.java or src/main/**/*.yml")] string? glob = null)
    {
        return Run("search_source", glob is null ? pattern : $"{pattern} | {glob}", () =>
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return Fail("a search pattern is required.");
            }

            var matches = new List<string>();
            var total = 0;

            foreach (var (fullPath, relativePath) in EnumerateVisibleFiles(_root))
            {
                if (!MatchesGlob(relativePath, glob) || !IsReadableText(fullPath))
                {
                    continue;
                }

                var lines = File.ReadAllLines(fullPath);

                for (var index = 0; index < lines.Length; index++)
                {
                    if (!lines[index].Contains(pattern, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    total++;

                    if (matches.Count < MaxSearchResults)
                    {
                        matches.Add($"{relativePath}:{index + 1}: {Bound(lines[index].Trim())}");
                    }
                }
            }

            if (total == 0)
            {
                return Ok("No matches.");
            }

            if (total > matches.Count)
            {
                matches.Add($"... {total - matches.Count} more matches omitted; narrow the pattern or glob.");
            }

            return Ok(string.Join('\n', matches));
        });
    }

    public string ListFiles(
        [Description("Optional repository-relative directory, e.g. src/main/java")] string? directory = null)
    {
        return Run("list_files", directory ?? ".", () =>
        {
            var start = string.IsNullOrWhiteSpace(directory) || directory == "."
                ? _root
                : ResolveDirectory(directory);

            var files = EnumerateVisibleFiles(start)
                .Select(file => file.RelativePath)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
            {
                return Ok("No files.");
            }

            var shown = files.Take(MaxListedFiles).ToList();

            if (files.Count > shown.Count)
            {
                shown.Add($"... {files.Count - shown.Count} more files omitted; list a subdirectory.");
            }

            return Ok(string.Join('\n', shown));
        });
    }

    public string PreviewEdits(
        [Description("The edits exactly as they would appear in the proposal.")] IReadOnlyList<RemediationEdit> edits)
    {
        return Run("preview_edits", $"{edits?.Count ?? 0} edits", () =>
        {
            var preview = new RemediationPatchApplier().Preview(_root, edits ?? []);

            if (!preview.Accepted)
            {
                return Fail($"rejected: {preview.RejectionReason}");
            }

            var output = new StringBuilder(
                "Accepted. Nothing was written. The changed files would read:\n");

            foreach (var (path, content) in preview.Files.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                output.Append("\n=== ").Append(path).AppendLine(" ===");

                var lines = content.Replace("\r\n", "\n").Split('\n');

                for (var index = 0; index < Math.Min(lines.Length, MaxReadLines); index++)
                {
                    output.Append(index + 1).Append(": ").AppendLine(Bound(lines[index]));
                }

                if (lines.Length > MaxReadLines)
                {
                    output.AppendLine($"... truncated at {MaxReadLines} lines.");
                }
            }

            return Ok(BoundEvidence(output.ToString()));
        });
    }

    public string GetOpenRewritePatch() =>
        Run("get_openrewrite_patch", string.Empty, () => Ok(BoundEvidence(_openRewritePatch)));

    public string GetDiagnostics() =>
        Run("get_diagnostics", string.Empty, () => Ok(BoundEvidence(string.Join('\n', _diagnostics))));

    private string Run(
        string tool,
        string arguments,
        Func<ToolResult> action)
    {
        // Previews have their own allowance, so running out of exploration
        // calls never stops the agent checking its edits before answering.
        lock (_gate)
        {
            var isPreview = tool == "preview_edits";

            var used = _calls.Count(call =>
                (call.Tool == "preview_edits") == isPreview);

            var refusal =
                isPreview && used >= MaxPreviews
                    ? $"Error: preview limit of {MaxPreviews} reached; return your proposal now."
                : !isPreview && used >= _maxCalls
                    ? $"Error: tool call budget of {_maxCalls} calls is used up; " +
                      "check your edits with preview_edits, then return your proposal."
                : null;

            if (refusal is not null)
            {
                _calls.Add(new RemediationToolCall(tool, arguments, Succeeded: false));
                return refusal;
            }
        }

        ToolResult result;

        try
        {
            result = action();
        }
        catch (ToolException exception)
        {
            result = Fail(exception.Message);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            result = Fail("the file could not be read.");
        }

        lock (_gate)
        {
            _calls.Add(new RemediationToolCall(tool, arguments, result.Succeeded));
        }

        return result.Text;
    }

    private string ResolveFile(
        string path)
    {
        var full = Resolve(path);

        if (!File.Exists(full))
        {
            throw new ToolException(Directory.Exists(full)
                ? $"'{path}' is a directory; use list_files."
                : $"'{path}' does not exist.");
        }

        if (!IsReadableText(full))
        {
            throw new ToolException($"'{path}' is binary or larger than {MaxFileBytes / 1024} KB.");
        }

        return full;
    }

    private string ResolveDirectory(
        string path)
    {
        var full = Resolve(path);

        if (!Directory.Exists(full))
        {
            throw new ToolException($"'{path}' is not a directory.");
        }

        return full;
    }

    private string Resolve(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ToolException("a repository-relative path is required.");
        }

        var normalised = path.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(normalised)
            || normalised.StartsWith('/')
            || (normalised.Length > 1 && normalised[1] == ':'))
        {
            throw new ToolException("paths must be repository-relative, not absolute.");
        }

        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment => segment == ".."))
        {
            throw new ToolException("paths may not contain '..'.");
        }

        if (segments.Any(HiddenDirectories.Contains))
        {
            throw new ToolException("version control, CI and build output files are not available.");
        }

        var full = Path.GetFullPath(Path.Combine(_root, Path.Combine(segments)));

        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ToolException("the path is outside the repository.");
        }

        // Refuse any link along the way, so nothing outside can be reached.
        var current = _root;

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);

            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ToolException("links are not followed.");
            }
        }

        return full;
    }

    private IEnumerable<(string FullPath, string RelativePath)> EnumerateVisibleFiles(
        string start)
    {
        var pending = new Stack<string>([start]);

        while (pending.TryPop(out var directory))
        {
            IEnumerable<string> entries;

            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToList();
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries.Order(StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(entry);

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!HiddenDirectories.Contains(Path.GetFileName(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                yield return (entry, Path.GetRelativePath(_root, entry).Replace('\\', '/'));
            }
        }
    }

    private static bool MatchesGlob(
        string relativePath,
        string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            return true;
        }

        var candidate = glob.Contains('/')
            ? relativePath
            : Path.GetFileName(relativePath);

        return FileSystemName.MatchesSimpleExpression(
            glob.Replace("**/", "*"),
            candidate,
            ignoreCase: true);
    }

    private static bool IsReadableText(
        string path)
    {
        var info = new FileInfo(path);

        if (info.Length > MaxFileBytes)
        {
            return false;
        }

        using var stream = info.OpenRead();
        var buffer = new byte[Math.Min(8192, (int)info.Length)];
        var read = stream.Read(buffer, 0, buffer.Length);

        return Array.IndexOf(buffer, (byte)0, 0, read) < 0;
    }

    private static string Bound(
        string line) =>
        line.Length > MaxLineLength ? line[..MaxLineLength] + "..." : line;

    private static string BoundEvidence(
        string text) =>
        text.Length > MaxEvidenceChars
            ? text[..MaxEvidenceChars] + "\n... truncated."
            : text;

    private static ToolResult Ok(string text) => new(text, Succeeded: true);

    private static ToolResult Fail(string message) => new($"Error: {message}", Succeeded: false);

    private sealed record ToolResult(string Text, bool Succeeded);

    private sealed class ToolException(string message) : Exception(message);
}

/// <summary>One tool call made by the Remediation Agent, for the audit trail.</summary>
public sealed record RemediationToolCall(
    string Tool,
    string Arguments,
    bool Succeeded);
