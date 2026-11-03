using System.Xml;
using System.Xml.Linq;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

/// <summary>
/// Reads failing tests from <c>target/surefire-reports/TEST-*.xml</c> in a
/// workspace. The console output names failing tests but buries the cause of
/// a context-startup failure under hundreds of framework frames; the reports
/// give the cause chain and the repository's own frames cleanly.
/// The reports are produced by the migrated code's build, so they are parsed
/// as untrusted input: no DTDs, no external resolution, bounded sizes.
/// </summary>
public static class SurefireReportReader
{
    public const int MaxTests = 20;

    public const int MaxFrames = 5;

    public const int MaxTextLength = 400;

    private const long MaxReportBytes = 10 * 1024 * 1024;

    // Frames from these packages explain how a test ran, not why it failed.
    private static readonly string[] FrameworkPrefixes =
    [
        "java.", "javax.", "jakarta.", "jdk.", "sun.", "com.sun.",
        "org.junit.", "org.springframework.", "org.apache.", "org.mockito.",
        "org.assertj.", "org.hamcrest.", "org.hibernate.", "net.bytebuddy.",
        "kotlin.", "org.opentest4j.", "org.eclipse.", "com.fasterxml."
    ];

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true
    };

    /// <param name="repositoryPath">The workspace repository.</param>
    /// <param name="writtenSinceUtc">
    /// Only reports written at or after this time are read, so reports left
    /// by an earlier validation run are never mistaken for current failures.
    /// </param>
    public static IReadOnlyList<FailingTest> Read(
        string repositoryPath,
        DateTime writtenSinceUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        return Directory
            .EnumerateFiles(repositoryPath, "TEST-*.xml", options)
            .Where(IsSurefireReport)
            .Select(path => new FileInfo(path))
            .Where(file =>
                file.LastWriteTimeUtc >= writtenSinceUtc
                && file.Length <= MaxReportBytes)
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .SelectMany(file => ReadReport(file.FullName, repositoryPath))
            .OrderBy(test => test.TestClass, StringComparer.Ordinal)
            .ThenBy(test => test.TestName, StringComparer.Ordinal)
            .Take(MaxTests)
            .ToList();
    }

    private static bool IsSurefireReport(
        string path)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(path));
        var parent = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));

        return directory is "surefire-reports" or "failsafe-reports"
               && parent == "target";
    }

    private static IEnumerable<FailingTest> ReadReport(
        string path,
        string repositoryPath)
    {
        XDocument document;

        try
        {
            using var reader = XmlReader.Create(path, XmlSettings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            // A malformed or DTD-bearing report is skipped, not trusted.
            return [];
        }

        return document
            .Descendants("testcase")
            .Select(testCase => ToFailingTest(testCase, repositoryPath))
            .OfType<FailingTest>()
            .ToList();
    }

    private static FailingTest? ToFailingTest(
        XElement testCase,
        string repositoryPath)
    {
        var problem =
            testCase.Element("failure")
            ?? testCase.Element("error");

        if (problem is null)
        {
            return null;
        }

        var trace = problem.Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        var rootCause = trace
            .LastOrDefault(line =>
                line.StartsWith("Caused by:", StringComparison.Ordinal))?
            ["Caused by:".Length..]
            .Trim();

        var frames = trace
            .Where(line => line.StartsWith("at ", StringComparison.Ordinal))
            .Select(line => StripModulePrefix(line[3..].Trim()))
            .Where(frame => !FrameworkPrefixes.Any(prefix =>
                frame.StartsWith(prefix, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxFrames)
            .Select(frame => Clean(frame, repositoryPath)!)
            .ToList();

        return new FailingTest(
            TestClass: Clean((string?)testCase.Attribute("classname"), repositoryPath) ?? string.Empty,
            TestName: Clean((string?)testCase.Attribute("name"), repositoryPath) ?? string.Empty,
            Kind: problem.Name.LocalName,
            Type: Clean((string?)problem.Attribute("type"), repositoryPath),
            Message: Clean(FirstLine((string?)problem.Attribute("message")), repositoryPath),
            RootCause: Clean(rootCause, repositoryPath),
            Frames: frames);
    }

    // "java.base/java.lang.Class.forName(...)" -> "java.lang.Class.forName(...)"
    private static string StripModulePrefix(
        string frame)
    {
        var slash = frame.IndexOf('/');
        var paren = frame.IndexOf('(');

        return slash > 0 && (paren < 0 || slash < paren)
            ? frame[(slash + 1)..]
            : frame;
    }

    private static string? FirstLine(
        string? text)
    {
        return text?
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
    }

    private static string? Clean(
        string? text,
        string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));

        var cleaned = ValidationDiagnosticsExtractor.RemoveWorkspacePaths(
            text.Replace(root + Path.DirectorySeparatorChar, string.Empty)
                .Replace(root, string.Empty))
            .Trim();

        return cleaned.Length > MaxTextLength
            ? cleaned[..MaxTextLength] + "..."
            : cleaned;
    }
}
