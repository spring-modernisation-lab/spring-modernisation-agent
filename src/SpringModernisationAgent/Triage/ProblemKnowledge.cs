using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SpringModernisationAgent.Triage;

/// <summary>Which path a problem takes (docs/system-design.md §4).</summary>
public enum ProblemKind
{
    /// <summary>Fails only because another test's context failed; re-checked after other fixes.</summary>
    KnockOn,

    /// <summary>A deterministic recipe fixes it.</summary>
    Mechanical,

    /// <summary>A known API move: a scoped AI port.</summary>
    ApiMove,

    /// <summary>A library the platform dropped: a person decides.</summary>
    PlatformDropped,

    /// <summary>An HTTP status or URL changed: a person decides.</summary>
    Contract,

    /// <summary>Nothing matched: the AI may diagnose it, never edit.</summary>
    Unknown
}

/// <summary>One rule from <c>diagnostic-signatures.yaml</c>.</summary>
public sealed record DiagnosticSignature(
    string Id,
    ProblemKind Kind,
    string? Mapping,
    string? Recipe,
    string? Library,
    IReadOnlyList<string> Contains,
    IReadOnlyList<Regex> Patterns)
{
    public bool Matches(
        string evidence) =>
        Contains.Any(text => evidence.Contains(text, StringComparison.Ordinal))
        || Patterns.Any(pattern => pattern.IsMatch(evidence));
}

/// <summary>A known API move from <c>api-mappings.yaml</c>: notes for the AI and what the port must keep.</summary>
public sealed record ApiMapping(
    string Id,
    string Name,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> MustKeep);

/// <summary>
/// The signatures and API mappings, validated together: every mapping,
/// recipe and library a signature names must exist. A mistake stops the run,
/// naming the file and entry, rather than silently misclassifying.
/// </summary>
public sealed partial class ProblemKnowledge
{
    public const string SignaturesRelativePath = "Config/knowledge/diagnostic-signatures.yaml";
    public const string MappingsRelativePath = "Config/knowledge/api-mappings.yaml";

    private static readonly Dictionary<string, ProblemKind> Kinds = new(StringComparer.Ordinal)
    {
        ["knock-on"] = ProblemKind.KnockOn,
        ["mechanical"] = ProblemKind.Mechanical,
        ["api-move"] = ProblemKind.ApiMove,
        ["platform-dropped"] = ProblemKind.PlatformDropped,
        ["contract"] = ProblemKind.Contract
    };

    public ProblemKnowledge(
        IReadOnlyList<DiagnosticSignature> signatures,
        IReadOnlyDictionary<string, ApiMapping> mappings)
    {
        Signatures = signatures;
        Mappings = mappings;
    }

    public IReadOnlyList<DiagnosticSignature> Signatures { get; }

    public IReadOnlyDictionary<string, ApiMapping> Mappings { get; }

    /// <summary>
    /// The files shipped with the application. Mechanical signatures may name
    /// any recipe defined in <c>Config/recipes/*.yml</c>.
    /// </summary>
    public static ProblemKnowledge LoadDefault(
        IReadOnlyCollection<string> knownLibraries)
    {
        var root = AppContext.BaseDirectory;
        var recipesDirectory = Path.Combine(root, "Config", "recipes");

        var knownRecipes = Directory.Exists(recipesDirectory)
            ? Directory.EnumerateFiles(recipesDirectory, "*.yml")
                .SelectMany(file => RecipeName().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
                .ToHashSet(StringComparer.Ordinal)
            : [];

        return Parse(
            File.ReadAllText(Path.Combine(root, SignaturesRelativePath)),
            File.ReadAllText(Path.Combine(root, MappingsRelativePath)),
            knownLibraries,
            knownRecipes);
    }

    [GeneratedRegex(@"(?m)^name:\s*(\S+)\s*$")]
    private static partial Regex RecipeName();

    /// <param name="knownLibraries">Library names in the unsupported-libraries catalogue.</param>
    /// <param name="knownRecipes">Recipe names a mechanical signature may name (the pipeline recipes).</param>
    public static ProblemKnowledge Parse(
        string signaturesYaml,
        string mappingsYaml,
        IReadOnlyCollection<string> knownLibraries,
        IReadOnlyCollection<string> knownRecipes)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var mappingsFile = Read<MappingsFile>(deserializer, mappingsYaml, "api-mappings.yaml");
        var mappings = new Dictionary<string, ApiMapping>(StringComparer.Ordinal);

        foreach (var (entry, index) in (mappingsFile?.Mappings ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"api-mappings.yaml: mapping {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("M-", StringComparison.Ordinal), label, "needs an id starting with M-");
            Require(!string.IsNullOrWhiteSpace(entry.Name), label, "needs a name");
            Require(entry.Notes is { Count: > 0 }, label, "needs at least one note");
            Require(!mappings.ContainsKey(entry.Id!), label, "duplicates another mapping's id");

            mappings[entry.Id!] = new ApiMapping(entry.Id!, entry.Name!, entry.Notes!, entry.MustKeep ?? []);
        }

        var signaturesFile = Read<SignaturesFile>(deserializer, signaturesYaml, "diagnostic-signatures.yaml");
        var signatures = new List<DiagnosticSignature>();

        foreach (var (entry, index) in (signaturesFile?.Signatures ?? []).Select((entry, index) => (entry, index + 1)))
        {
            var label = $"diagnostic-signatures.yaml: signature {index} ('{entry.Id ?? "?"}')";

            Require(entry.Id is { Length: > 0 } && entry.Id.StartsWith("S-", StringComparison.Ordinal), label, "needs an id starting with S-");
            Require(signatures.All(signature => signature.Id != entry.Id), label, "duplicates another signature's id");
            Require(entry.Kind is not null && Kinds.ContainsKey(entry.Kind), label,
                $"needs a kind: {string.Join(", ", Kinds.Keys)}");
            Require((entry.Contains?.Count ?? 0) + (entry.Pattern?.Count ?? 0) > 0, label, "needs contains or pattern");

            var kind = Kinds[entry.Kind!];

            Require(kind != ProblemKind.ApiMove || (entry.Mapping is not null && mappings.ContainsKey(entry.Mapping)), label,
                "is api-move, so it needs a mapping defined in api-mappings.yaml");
            Require(kind != ProblemKind.Mechanical || (entry.Recipe is not null && knownRecipes.Contains(entry.Recipe)), label,
                "is mechanical, so it needs a recipe the pipeline has");
            Require(kind != ProblemKind.PlatformDropped || (entry.Library is not null && knownLibraries.Contains(entry.Library)), label,
                "is platform-dropped, so it needs a library from unsupported-libraries.yaml");

            var patterns = new List<Regex>();

            foreach (var pattern in entry.Pattern ?? [])
            {
                try
                {
                    patterns.Add(new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException($"{label} has an invalid pattern '{pattern}': {exception.Message}");
                }
            }

            signatures.Add(new DiagnosticSignature(
                entry.Id!, kind, entry.Mapping, entry.Recipe, entry.Library, entry.Contains ?? [], patterns));
        }

        return new ProblemKnowledge(signatures, mappings);
    }

    private static T? Read<T>(
        IDeserializer deserializer,
        string yaml,
        string source)
    {
        try
        {
            return deserializer.Deserialize<T>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"{source}: {exception.Message} (line {exception.Start.Line}).", exception);
        }
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

    private sealed class SignaturesFile
    {
        public List<SignatureEntry>? Signatures { get; set; }
    }

    private sealed class SignatureEntry
    {
        public string? Id { get; set; }

        public string? Kind { get; set; }

        public string? Mapping { get; set; }

        public string? Recipe { get; set; }

        public string? Library { get; set; }

        public List<string>? Contains { get; set; }

        public List<string>? Pattern { get; set; }

        /// <summary>The runs whose real failures the signature was written from (documentation).</summary>
        public List<string>? SeenIn { get; set; }
    }

    private sealed class MappingsFile
    {
        public List<MappingEntry>? Mappings { get; set; }
    }

    private sealed class MappingEntry
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Notes { get; set; }

        public List<string>? MustKeep { get; set; }
    }
}
