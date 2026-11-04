using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Remediation;

/// <summary>A library the target platform no longer supports, and what a person does about it.</summary>
/// <param name="Name">For people, for example "Ehcache 2".</param>
/// <param name="Package">Java package prefix of its API, for example <c>net.sf.ehcache</c>.</param>
/// <param name="PomMarkers">Text in a <c>pom.xml</c> that shows the dependency is declared.</param>
/// <param name="Replacements">The supported replacements in one line, for the agent and rejection messages.</param>
/// <param name="Recommended">The replacement the manual steps follow.</param>
/// <param name="ManualSteps">What a person changes, in order. Fixed text, not AI-written.</param>
/// <param name="Alternatives">Other replacements and what choosing them would change.</param>
public sealed record UnsupportedLibrary(
    string Name,
    string Package,
    IReadOnlyList<string> PomMarkers,
    string Replacements,
    string Recommended,
    IReadOnlyList<string> ManualSteps,
    IReadOnlyList<string> Alternatives);

/// <summary>
/// The libraries in <c>Config/knowledge/unsupported-libraries.yaml</c>.
/// Replacing one is a design decision that also touches <c>pom.xml</c> and
/// often test resources, so the Remediation Agent may not write code against
/// them, is told which ones a repository uses, and the pull request lists the
/// manual steps. Adding a library is a YAML entry, never a code change.
/// </summary>
public sealed partial class UnsupportedLibraryCatalog
{
    public const string DefaultRelativePath = "Config/knowledge/unsupported-libraries.yaml";

    public UnsupportedLibraryCatalog(
        IReadOnlyList<UnsupportedLibrary> libraries)
    {
        ArgumentNullException.ThrowIfNull(libraries);

        All = libraries;
    }

    public IReadOnlyList<UnsupportedLibrary> All { get; }

    /// <summary>Reads and validates a catalogue file; any problem stops the run with the file and entry named.</summary>
    public static UnsupportedLibraryCatalog Load(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The unsupported-libraries catalogue was not found.", path);
        }

        return Parse(File.ReadAllText(path), path);
    }

    public static UnsupportedLibraryCatalog Parse(
        string yaml,
        string source = "unsupported-libraries.yaml")
    {
        ArgumentNullException.ThrowIfNull(yaml);

        // Unknown fields fail: a misspelt field must not silently switch a rule off.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        CatalogueFile? file;

        try
        {
            file = deserializer.Deserialize<CatalogueFile>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException(
                $"{source}: {exception.Message} (line {exception.Start.Line}).", exception);
        }

        var entries = file?.Libraries ?? [];
        var libraries = new List<UnsupportedLibrary>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var label = $"{source}: library {index + 1} ('{entry.Name ?? "?"}')";

            Require(!string.IsNullOrWhiteSpace(entry.Name), label, "needs a name");
            Require(entry.Package is not null && PackageName().IsMatch(entry.Package), label,
                "needs a package such as 'net.sf.ehcache'");
            Require(entry.PomMarkers is { Count: > 0 } && entry.PomMarkers.All(marker => !string.IsNullOrWhiteSpace(marker)),
                label, "needs at least one pomMarkers entry");
            Require(!string.IsNullOrWhiteSpace(entry.Replacements), label, "needs replacements");
            Require(!string.IsNullOrWhiteSpace(entry.Recommended), label, "needs recommended");
            Require(entry.ManualSteps is { Count: > 0 } && entry.ManualSteps.All(step => !string.IsNullOrWhiteSpace(step)),
                label, "needs at least one manualSteps entry");
            Require(libraries.All(library => library.Name != entry.Name), label, "duplicates another library's name");
            Require(libraries.All(library => library.Package != entry.Package), label, "duplicates another library's package");

            libraries.Add(new UnsupportedLibrary(
                entry.Name!,
                entry.Package!,
                entry.PomMarkers!,
                entry.Replacements!,
                entry.Recommended!,
                entry.ManualSteps!,
                entry.Alternatives ?? []));
        }

        return new UnsupportedLibraryCatalog(libraries);
    }

    public UnsupportedLibrary? Find(
        string name) =>
        All.FirstOrDefault(library => library.Name == name);

    /// <summary>The libraries a repository declares or imports, in catalogue order.</summary>
    public IReadOnlyList<UnsupportedLibrary> FindIn(
        string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath) || All.Count == 0)
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true
        };

        var found = new HashSet<UnsupportedLibrary>();

        foreach (var file in Directory.EnumerateFiles(repositoryPath, "*", options))
        {
            var relative = Path.GetRelativePath(repositoryPath, file).Replace('\\', '/');

            if (relative.Split('/').Any(segment => segment is ".git" or "target" or "node_modules"))
            {
                continue;
            }

            var isPom = Path.GetFileName(file) == "pom.xml";
            var isSource = relative.Contains("src/main/", StringComparison.Ordinal)
                           && Path.GetExtension(file) is ".java" or ".kt";

            if (!isPom && !isSource)
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var library in All)
            {
                if (isPom
                        ? library.PomMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal))
                        : text.Contains($"import {library.Package}.", StringComparison.Ordinal))
                {
                    found.Add(library);
                }
            }
        }

        return All.Where(found.Contains).ToList();
    }

    private static void Require(
        bool valid,
        string label,
        string problem)
    {
        if (!valid)
        {
            throw new InvalidOperationException($"{label} {problem}.");
        }
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex PackageName();

    private sealed class CatalogueFile
    {
        public List<CatalogueEntry>? Libraries { get; set; }
    }

    private sealed class CatalogueEntry
    {
        public string? Name { get; set; }

        public string? Package { get; set; }

        public List<string>? PomMarkers { get; set; }

        public string? Replacements { get; set; }

        public string? Recommended { get; set; }

        public List<string>? ManualSteps { get; set; }

        public List<string>? Alternatives { get; set; }
    }
}

/// <summary>
/// The catalogue shipped with the application
/// (<see cref="UnsupportedLibraryCatalog.DefaultRelativePath"/> under the
/// application directory), loaded on first use. A missing or invalid file
/// fails every use with the same clear error, rather than running without
/// the rules.
/// </summary>
public static class UnsupportedLibraries
{
    private static readonly Lazy<UnsupportedLibraryCatalog> Default = new(
        () => UnsupportedLibraryCatalog.Load(
            Path.Combine(AppContext.BaseDirectory, UnsupportedLibraryCatalog.DefaultRelativePath)),
        LazyThreadSafetyMode.PublicationOnly);

    public static UnsupportedLibraryCatalog Catalog => Default.Value;

    public static IReadOnlyList<UnsupportedLibrary> All => Catalog.All;

    public static UnsupportedLibrary? Find(
        string name) =>
        Catalog.Find(name);

    public static IReadOnlyList<UnsupportedLibrary> FindIn(
        string repositoryPath) =>
        Catalog.FindIn(repositoryPath);
}
